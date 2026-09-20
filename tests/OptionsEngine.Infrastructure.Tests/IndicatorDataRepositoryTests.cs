using Microsoft.EntityFrameworkCore;
using OptionsEngine.Application.MarketData;
using OptionsEngine.Application.EntryStrategy;
using OptionsEngine.Infrastructure.Persistence;
using OptionsEngine.MarketData.Models;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Infrastructure.Tests;

public sealed class IndicatorDataRepositoryTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"options-engine-indicators-{Guid.NewGuid():N}.db");
    private static readonly DateOnly Start = new(2026, 1, 1);
    private static readonly DateTimeOffset CalculatedAt = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly IndicatorCalculationVersion Version = new("3.0.0");

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync()
    {
        File.Delete(_path);
        File.Delete($"{_path}-shm");
        File.Delete($"{_path}-wal");
        return Task.CompletedTask;
    }

    [Fact]
    public async Task EmptyDatabaseAppliesFullMigrationLineage()
    {
        await using var db = CreateContext();
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(5, applied.Length);
        Assert.EndsWith("_InitialCreate", applied[0], StringComparison.Ordinal);
        Assert.EndsWith("_AddMarketData", applied[1], StringComparison.Ordinal);
        Assert.EndsWith("_AddIndicatorSnapshots", applied[2], StringComparison.Ordinal);
        Assert.EndsWith("_AddEntryStrategyEvaluations", applied[3], StringComparison.Ordinal);
        Assert.EndsWith("_AddPositionSizing", applied[4], StringComparison.Ordinal);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(0, await db.IndicatorSnapshots.CountAsync());
        Assert.Equal(0, await db.EmptyOptionChainSnapshots.CountAsync());
    }

    [Fact]
    public async Task CanonicalSnapshotRoundTripsAndSameIdentityAtomicallyReplacesFacts()
    {
        await using var db = CreateContext();
        var repository = new SqliteIndicatorDataRepository(db);
        var first = Snapshot(Start, Version, 1) with
        {
            Sma20 = IndicatorValue<decimal>.Available(0m),
            Iv30UnavailableReason = Iv30UnavailableReason.NoEligibleAtmPair
        };
        await repository.UpsertSnapshotAsync(first with { Symbol = " msft " });
        var initial = await repository.GetSnapshotAsync("MSFT", Start, Version, new ConfigurationVersion(1));
        Assert.NotNull(initial);
        Assert.Equal(0m, initial.Sma20.Value);
        Assert.Equal(IndicatorValueStatus.Available, initial.Sma20.Status);
        Assert.Null(initial.Sma50.Value);
        Assert.Equal(IndicatorValueStatus.InsufficientData, initial.Sma50.Status);
        Assert.Equal(Iv30UnavailableReason.NoEligibleAtmPair, initial.Iv30UnavailableReason);

        var replacement = first with { Sma20 = IndicatorValue<decimal>.Available(2m), CalculatedAt = CalculatedAt.AddHours(1) };
        await repository.UpsertSnapshotAsync(replacement);
        await repository.UpsertSnapshotAsync(replacement);
        var current = await repository.GetSnapshotAsync("msft", Start, Version, new ConfigurationVersion(1));
        Assert.NotNull(current);
        Assert.Equal(2m, current.Sma20.Value);
        Assert.Equal(CalculatedAt.AddHours(1), current.CalculatedAt);
        Assert.Equal(("MSFT", Start, Version, new ConfigurationVersion(1)),
            (current.Symbol, current.AsOfDate, current.IndicatorCalculationVersion, current.ConfigurationVersion));
        Assert.Equal(1, await db.IndicatorSnapshots.CountAsync());
    }

    [Fact]
    public async Task LatestTradingDateLookupHonorsSymbolProviderAndInclusiveBoundary()
    {
        await using var db = CreateContext();
        var cache = new SqliteMarketDataCache(db);
        var repository = new SqliteIndicatorDataRepository(db);
        var friday = new DateOnly(2026, 9, 18);
        var monday = friday.AddDays(3);
        await cache.UpsertHistoricalBarsAsync(
            [new HistoricalBar("MSFT", friday, 100m, 101m, 99m, 100m, 1, "Tradier"),
             new HistoricalBar("MSFT", monday, 101m, 102m, 100m, 101m, 1, "Tradier"),
             new HistoricalBar("AAPL", friday.AddDays(1), 100m, 101m, 99m, 100m, 1, "Tradier")],
            friday, monday, CalculatedAt);

        Assert.Equal(friday, await repository.GetLatestPriceObservationDateAsync(" msft ", "Tradier", friday.AddDays(2)));
        Assert.Equal(monday, await repository.GetLatestPriceObservationDateAsync("MSFT", "Tradier", monday));
        Assert.Null(await repository.GetLatestPriceObservationDateAsync("MSFT", "Other", monday));
        Assert.Null(await repository.GetLatestPriceObservationDateAsync("MSFT", "Tradier", friday.AddDays(-1)));
    }

    [Fact]
    public async Task ExactIdentityRetrievalDoesNotSubstituteFutureOrOtherVersions()
    {
        await using var db = CreateContext();
        var repository = new SqliteIndicatorDataRepository(db);
        var nextVersion = new IndicatorCalculationVersion("3.0.1");
        await repository.UpsertSnapshotAsync(Snapshot(Start, Version, 1));
        await repository.UpsertSnapshotAsync(Snapshot(Start.AddDays(1), Version, 1));
        await repository.UpsertSnapshotAsync(Snapshot(Start, nextVersion, 1));
        await repository.UpsertSnapshotAsync(Snapshot(Start, Version, 2));
        Assert.Equal(4, await db.IndicatorSnapshots.CountAsync());
        Assert.Null(await repository.GetSnapshotAsync("MSFT", Start.AddDays(2), Version, new ConfigurationVersion(1)));
        Assert.Null(await repository.GetSnapshotAsync("MSFT", Start, new IndicatorCalculationVersion("other"), new ConfigurationVersion(1)));
        Assert.Null(await repository.GetSnapshotAsync("MSFT", Start, Version, new ConfigurationVersion(3)));
        Assert.Equal(Start, (await repository.GetSnapshotAsync("MSFT", Start, Version, new ConfigurationVersion(1)))!.AsOfDate);
    }

    [Fact]
    public async Task Iv30HistorySelectsMostRecentValidValuesWithinMatchingVersions()
    {
        await using var db = CreateContext();
        var repository = new SqliteIndicatorDataRepository(db);
        for (var index = 0; index < 260; index++)
        {
            var iv = index == 254 ? IndicatorValue<double>.InsufficientData() : IndicatorValue<double>.Available(index / 1000d);
            await repository.UpsertSnapshotAsync(Snapshot(Start.AddDays(index), Version, 1) with { Iv30 = iv });
        }
        await repository.UpsertSnapshotAsync(Snapshot(Start.AddDays(260), Version, 1) with { Iv30 = IndicatorValue<double>.Available(9d) });
        await repository.UpsertSnapshotAsync(Snapshot(Start.AddDays(250), new IndicatorCalculationVersion("other"), 1) with { Iv30 = IndicatorValue<double>.Available(8d) });
        await repository.UpsertSnapshotAsync(Snapshot(Start.AddDays(250), Version, 2) with { Iv30 = IndicatorValue<double>.Available(7d) });

        var history = await repository.GetPriorValidIv30Async("msft", Start.AddDays(260), Version, new ConfigurationVersion(1), 252);
        Assert.Equal(252, history.Count);
        Assert.Equal(Start.AddDays(259), history[0].AsOfDate);
        Assert.DoesNotContain(history, item => item.AsOfDate == Start.AddDays(254));
        Assert.All(history, item =>
        {
            Assert.Equal(Version, item.IndicatorCalculationVersion);
            Assert.Equal(new ConfigurationVersion(1), item.ConfigurationVersion);
            Assert.True(item.AsOfDate < Start.AddDays(260));
            Assert.NotNull(item.Iv30.Value);
        });
        Assert.Equal(0d, (await repository.GetPriorValidIv30Async("MSFT", Start.AddDays(1), Version,
            new ConfigurationVersion(1), 252)).Single().Iv30.Value);
    }

    [Fact]
    public async Task HistoricalOptionChainsExcludeFutureSnapshotsAndPreserveObservedContracts()
    {
        await using var db = CreateContext();
        var cache = new SqliteMarketDataCache(db);
        var repository = new SqliteIndicatorDataRepository(db);
        var asOf = new DateOnly(2026, 1, 2);
        var expiration = asOf.AddDays(30);
        var firstAt = new DateTimeOffset(2026, 1, 2, 15, 0, 0, TimeSpan.Zero);
        var futureAt = firstAt.AddDays(1);
        await cache.SaveOptionChainAsync(Chain(firstAt, expiration, 0.2));
        var beforeFutureAppend = await repository.GetOptionChainsThroughAsync("msft", "Tradier", asOf, CalculatedAt);
        await cache.SaveOptionChainAsync(Chain(futureAt, expiration, 0.8));

        var historical = await repository.GetOptionChainsThroughAsync("msft", "Tradier", asOf, CalculatedAt);
        var earlierCutoff = await repository.GetOptionChainsThroughAsync("MSFT", "Tradier", asOf, firstAt.AddMinutes(-1));
        Assert.Single(historical);
        Assert.Equal(firstAt, historical[0].Timestamp);
        Assert.Equal(2, historical[0].Contracts.Count);
        Assert.Equal(0.2, historical[0].Contracts[0].ImpliedVolatility);
        Assert.Equal(beforeFutureAppend.Select(x => (x.Expiration, x.Timestamp, x.Contracts.Count)),
            historical.Select(x => (x.Expiration, x.Timestamp, x.Contracts.Count)));
        Assert.Empty(earlierCutoff);
    }

    [Fact]
    public async Task EmptyHistoricalOptionChainRemainsObservableWithoutFabricatedContracts()
    {
        await using var db = CreateContext();
        var cache = new SqliteMarketDataCache(db);
        var repository = new SqliteIndicatorDataRepository(db);
        var asOf = new DateOnly(2026, 1, 2);
        var observedAt = new DateTimeOffset(2026, 1, 2, 15, 0, 0, TimeSpan.Zero);
        await cache.SaveOptionChainAsync(new OptionChain("MSFT", asOf.AddDays(30), observedAt, [], "Tradier"));

        var historical = await repository.GetOptionChainsThroughAsync("MSFT", "Tradier", asOf, CalculatedAt);
        Assert.Single(historical);
        Assert.Empty(historical[0].Contracts);
        Assert.Equal(observedAt, historical[0].Timestamp);
        Assert.Empty(await repository.GetOptionChainsThroughAsync("MSFT", "Tradier", asOf.AddDays(-1), CalculatedAt));
    }

    [Fact]
    public async Task PhaseFourOptionChainQueryUsesUtcCutoffAndInclusiveConfiguredExpirationWindow()
    {
        await using var db = CreateContext();
        var cache = new SqliteMarketDataCache(db);
        var repository = new SqliteEntryStrategyMarketDataRepository(db);
        var cutoff = new DateTimeOffset(2026, 9, 18, 1, 0, 0, TimeSpan.Zero); // Sep 17 in New York.
        var evaluationDate = new DateOnly(2026, 9, 17);
        var minimum = evaluationDate.AddDays(20);
        var maximum = evaluationDate.AddDays(30);
        var eligibleAt = cutoff.AddMinutes(-30); // UTC Sep 18; Phase 3's date cap would incorrectly exclude this.
        await cache.SaveOptionChainAsync(Chain(eligibleAt, minimum, .2));
        await cache.SaveOptionChainAsync(new OptionChain("MSFT", evaluationDate.AddDays(25), eligibleAt.AddMinutes(1), [], "Tradier"));
        await cache.SaveOptionChainAsync(Chain(cutoff.AddMinutes(1), maximum, .8));
        await cache.SaveOptionChainAsync(Chain(eligibleAt, minimum.AddDays(-1), .3));
        await cache.SaveOptionChainAsync(Chain(eligibleAt, maximum.AddDays(1), .4));

        var chains = await repository.GetOptionChainsAsync(" msft ", "Tradier", minimum, maximum, cutoff);

        Assert.Equal([minimum, evaluationDate.AddDays(25)], chains.Select(x => x.Expiration));
        Assert.Equal(eligibleAt, chains[0].Timestamp);
        Assert.Empty(chains[1].Contracts); // Newer empty observations remain complete observations for the selector.
        Assert.DoesNotContain(chains, x => x.Timestamp > cutoff);
    }

    [Fact]
    public async Task PersistedNewerEmptyChainMakesOlderValidIvUnavailable()
    {
        await using var db = CreateContext();
        var cache = new SqliteMarketDataCache(db);
        var repository = new SqliteIndicatorDataRepository(db);
        var asOf = new DateOnly(2026, 1, 2);
        var expiration = asOf.AddDays(30);
        var earlierAt = new DateTimeOffset(2026, 1, 2, 14, 0, 0, TimeSpan.Zero);
        var laterAt = earlierAt.AddHours(1);
        await cache.SaveOptionChainAsync(Chain(earlierAt, expiration, 0.2));
        await cache.SaveOptionChainAsync(new OptionChain("MSFT", expiration, laterAt, [], "Tradier"));

        var persisted = await repository.GetOptionChainsThroughAsync("MSFT", "Tradier", asOf, CalculatedAt);
        Assert.Equal(2, persisted.Count);
        Assert.Equal(earlierAt, persisted[0].Timestamp);
        Assert.Equal(laterAt, persisted[1].Timestamp);
        Assert.Empty(persisted[1].Contracts);

        var result = new ImpliedVolatilityContextCalculator().Calculate(new ImpliedVolatilityCalculationRequest(
            "MSFT", asOf, persisted.Select(ImpliedVolatilityObservationMapper.Map).ToArray(), [],
            new IndicatorConfiguration { Version = new ConfigurationVersion(1) }, Version, CalculatedAt));
        Assert.Equal(IndicatorValueStatus.InsufficientData, result.Iv30.Status);
        Assert.Null(result.Iv30.Value);
        Assert.Equal(Iv30UnavailableReason.NoEligibleAtmPair, result.Iv30UnavailableReason);
    }

    [Fact]
    public async Task PhaseTwoDatabaseUpgradesWithoutLosingNormalizedMarketObservations()
    {
        var upgradePath = Path.Combine(Path.GetTempPath(), $"options-engine-phase2-upgrade-{Guid.NewGuid():N}.db");
        try
        {
            await using (var phaseTwo = CreateContext(upgradePath))
            {
                await phaseTwo.Database.MigrateAsync("20260917211407_AddMarketData");
                var cache = new SqliteMarketDataCache(phaseTwo);
                var bar = new HistoricalBar("MSFT", Start, 1m, 2m, 1m, 2m, 100, "Tradier");
                await cache.UpsertHistoricalBarsAsync([bar], Start, Start, CalculatedAt);
                await cache.SaveOptionChainAsync(Chain(CalculatedAt, Start.AddDays(30), 0.2));
            }
            await using (var phaseThree = CreateContext(upgradePath))
            {
                await phaseThree.Database.MigrateAsync();
                Assert.Single(await phaseThree.HistoricalPriceBars.ToListAsync());
                Assert.Equal(2, await phaseThree.OptionContractSnapshots.CountAsync());
                Assert.Contains(await phaseThree.Database.GetAppliedMigrationsAsync(), x => x.EndsWith("_AddIndicatorSnapshots", StringComparison.Ordinal));
                Assert.Contains(await phaseThree.Database.GetAppliedMigrationsAsync(), x => x.EndsWith("_AddEntryStrategyEvaluations", StringComparison.Ordinal));
                Assert.Equal(0, await phaseThree.IndicatorSnapshots.CountAsync());
                Assert.Equal(0, await phaseThree.EntryStrategyEvaluations.CountAsync());
                Assert.Empty(await phaseThree.Database.GetPendingMigrationsAsync());
            }
        }
        finally
        {
            File.Delete(upgradePath);
            File.Delete($"{upgradePath}-shm");
            File.Delete($"{upgradePath}-wal");
        }
    }

    [Fact]
    public async Task PopulatedPhaseThreeDatabaseUpgradesToPhaseFourWithoutLosingExistingData()
    {
        var upgradePath = Path.Combine(Path.GetTempPath(), $"options-engine-phase3-to-phase4-{Guid.NewGuid():N}.db");
        try
        {
            var account = new OptionsEngine.Domain.Accounts.Account("Phase3", OptionsEngine.Domain.Accounts.Broker.Other,
                OptionsEngine.Domain.Accounts.AccountType.Taxable, false);
            var holding = new OptionsEngine.Domain.Accounts.Holding(account, "MSFT", OptionsEngine.Domain.Accounts.AssetType.Stock,
                100m, OptionsEngine.Domain.Accounts.AssignmentSensitivity.Level3, OptionsEngine.Domain.Accounts.TaxSensitivity.Moderate,
                1m, .25, .12, .18, 70, 80, .1m, .1, 0);
            var lot = new OptionsEngine.Domain.Accounts.TaxLot(holding, new DateOnly(2020, 1, 2), 100m, 10m, 1000m,
                OptionsEngine.Domain.Accounts.HoldingPeriodClassification.LongTerm);
            var expiration = new DateOnly(2026, 10, 16);
            var chain = Chain(new DateTimeOffset(2026, 9, 17, 15, 0, 0, TimeSpan.Zero), expiration, .2);

            await using (var phase3 = CreateContext(upgradePath))
            {
                await phase3.Database.MigrateAsync("20260918024627_AddIndicatorSnapshots");
                phase3.AddRange(account, holding, lot);
                var cache = new SqliteMarketDataCache(phase3);
                await cache.UpsertHistoricalBarsAsync([new HistoricalBar("MSFT", Start, 1m, 2m, 1m, 2m, 100, "Tradier")], Start, Start, CalculatedAt);
                await cache.SaveOptionChainAsync(chain);
                await new SqliteIndicatorDataRepository(phase3).UpsertSnapshotAsync(Snapshot(Start, Version, 1));
            }

            await using (var phase4 = CreateContext(upgradePath))
            {
                await phase4.Database.MigrateAsync("20260918170325_AddEntryStrategyEvaluations");
                Assert.Contains(await phase4.Database.GetAppliedMigrationsAsync(), x => x.EndsWith("_AddEntryStrategyEvaluations", StringComparison.Ordinal));
                Assert.Empty(await phase4.EntryStrategyEvaluations.ToListAsync());
                Assert.Equal(["20260919210206_AddPositionSizing"], await phase4.Database.GetPendingMigrationsAsync());
            }

            await using var verify = CreateContext(upgradePath);
            Assert.Single(await verify.Accounts.ToListAsync());
            Assert.Single(await verify.Holdings.ToListAsync());
            Assert.Single(await verify.TaxLots.ToListAsync());
            Assert.Single(await verify.HistoricalPriceBars.ToListAsync());
            Assert.Equal(2, await verify.OptionContractSnapshots.CountAsync());
            Assert.Single(await verify.IndicatorSnapshots.ToListAsync());
        }
        finally
        {
            File.Delete(upgradePath);
            File.Delete($"{upgradePath}-shm");
            File.Delete($"{upgradePath}-wal");
        }
    }

    private OptionsEngineDbContext CreateContext() => new(
        new DbContextOptionsBuilder<OptionsEngineDbContext>().UseSqlite($"Data Source={_path}").Options);

    private static OptionsEngineDbContext CreateContext(string path) => new(
        new DbContextOptionsBuilder<OptionsEngineDbContext>().UseSqlite($"Data Source={path}").Options);

    private static IndicatorSnapshot Snapshot(DateOnly date, IndicatorCalculationVersion version, int configVersion)
    {
        var configuration = new IndicatorConfiguration { Version = new ConfigurationVersion(configVersion) };
        var bars = Enumerable.Range(0, 20).Select(index => new IndicatorPriceObservation("MSFT", date.AddDays(index - 19),
            0m, 0m, 0m, 0m, 100)).ToArray();
        return new SimpleMovingAverageIndicatorCalculator().Calculate(new IndicatorCalculationRequest("MSFT", date, bars,
            configuration, version, CalculatedAt));
    }

    private static OptionChain Chain(DateTimeOffset timestamp, DateOnly expiration, double iv) => new("MSFT", expiration,
        timestamp,
        [new OptionContractSnapshot("MSFT-C", "MSFT", timestamp, expiration, 100m, OptionType.Call, null, null, null,
            null, null, iv, null, null, null, null, 100m, "Tradier"),
         new OptionContractSnapshot("MSFT-P", "MSFT", timestamp, expiration, 100m, OptionType.Put, null, null, null,
            null, null, iv, null, null, null, null, 100m, "Tradier")], "Tradier");
}

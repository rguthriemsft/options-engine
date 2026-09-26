using Microsoft.EntityFrameworkCore;
using OptionsEngine.Application.Defense;
using OptionsEngine.Application.EntryStrategy;
using OptionsEngine.Application.Indicators;
using OptionsEngine.Domain.Accounts;
using OptionsEngine.Infrastructure.Persistence;
using OptionsEngine.MarketData;
using OptionsEngine.MarketData.Models;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Infrastructure.Tests;

public sealed class CurrentCcosResolverTests
{
    private static readonly DateTimeOffset EvaluationAt =
        new(2026, 9, 20, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly AsOfDate = new(2026, 9, 18);
    private static readonly IndicatorCalculationVersion IndicatorVersion = new("3.0.0");

    [Fact]
    public async Task ResolvesCurrentIndicatorContextAndUsesTheExistingPhaseFourCcosCalculator()
    {
        var repository = new FakeIndicatorRepository { Snapshot = Snapshot() };
        var provider = new FakeProvider();
        var configuration = Configuration();
        var resolver = new CurrentCcosResolver(new IndicatorOrchestrationService(repository,
            provider, new IndicatorOrchestrationConfiguration()), configuration);

        var result = await resolver.ResolveAsync(Holding(),
            new EarningsContext(AvailabilityStatus.Available, new DateOnly(2027, 1, 1)), EvaluationAt,
            new ConfigurationVersion(1));

        Assert.NotNull(result);
        Assert.Equal(EvaluationAt, result.EvaluationContext.EvaluationTimestampUtc);
        Assert.Equal(AsOfDate, result.EvaluationContext.IndicatorAsOfDate);
        Assert.Equal(configuration.StrategyConfiguration.Version,
            result.EvaluationContext.Configuration.Version);
        var directlyCalculated = new CcosCalculator().Evaluate(result.EvaluationContext).Ccos;
        Assert.Equal((directlyCalculated.Status, directlyCalculated.Score,
                directlyCalculated.Classification, directlyCalculated.ConfiguredMinimum),
            (result.Result.Status, result.Result.Score, result.Result.Classification,
                result.Result.ConfiguredMinimum));
        Assert.Equal(directlyCalculated.Components.Select(component => (component.Code, component.Score)),
            result.Result.Components.Select(component => (component.Code, component.Score)));
        Assert.Equal(ScoreStatus.Available, result.Result.Status);
        Assert.Equal(1, repository.LatestDateCalls);
        Assert.Equal(0, repository.CalculationReadCalls);
        Assert.Equal(0, repository.UpsertCalls);
    }

    [Fact]
    public async Task FuturePersistedSnapshotUsesTransientCutoffSafeCalculationWithoutUpsert()
    {
        var repository = new FakeIndicatorRepository
        {
            Snapshot = Snapshot() with { CalculatedAt = EvaluationAt.AddMinutes(1) }
        };
        var resolver = new CurrentCcosResolver(new IndicatorOrchestrationService(repository,
            new FakeProvider(), new IndicatorOrchestrationConfiguration()), Configuration());

        var result = await resolver.ResolveAsync(Holding(),
            new EarningsContext(AvailabilityStatus.Available, new DateOnly(2027, 1, 1)), EvaluationAt,
            new ConfigurationVersion(1));

        Assert.NotNull(result);
        Assert.Equal(EvaluationAt, result.EvaluationContext.Indicators.IndicatorCalculatedAtUtc);
        Assert.Equal(IndicatorVersion,
            result.EvaluationContext.Indicators.IndicatorCalculationVersion);
        Assert.Equal(new ConfigurationVersion(1),
            result.EvaluationContext.Indicators.ConfigurationVersion);
        Assert.True(repository.CalculationReadCalls > 0);
        Assert.Equal(0, repository.UpsertCalls);
    }

    [Fact]
    public async Task MissingPersistedSnapshotUsesTransientCalculationWithoutUpsert()
    {
        var repository = new FakeIndicatorRepository { Snapshot = null };
        var resolver = new CurrentCcosResolver(new IndicatorOrchestrationService(repository,
            new FakeProvider(), new IndicatorOrchestrationConfiguration()), Configuration());

        var result = await resolver.ResolveAsync(Holding(),
            new EarningsContext(AvailabilityStatus.Available, new DateOnly(2027, 1, 1)), EvaluationAt,
            new ConfigurationVersion(1));

        Assert.NotNull(result);
        Assert.Equal(EvaluationAt, result.EvaluationContext.Indicators.IndicatorCalculatedAtUtc);
        Assert.True(repository.CalculationReadCalls > 0);
        Assert.Equal(0, repository.UpsertCalls);
    }

    [Fact]
    public async Task HistoricalResolutionDoesNotOverwriteNewerCanonicalSqliteSnapshot()
    {
        var databasePath = Path.Combine(Path.GetTempPath(),
            $"options-engine-current-ccos-{Guid.NewGuid():N}.db");
        try
        {
            await using var db = CreateContext(databasePath);
            await db.Database.MigrateAsync();
            var repository = new SqliteIndicatorDataRepository(db);
            var cache = new SqliteMarketDataCache(db);
            await cache.UpsertHistoricalBarsAsync(
                [new HistoricalBar("MSFT", AsOfDate, 100m, 101m, 99m, 100m, 1000, "FakeProvider")],
                AsOfDate, AsOfDate, EvaluationAt.AddMinutes(-1));
            await repository.UpsertSnapshotAsync(Snapshot() with { CalculatedAt = EvaluationAt.AddHours(1) });
            var original = await db.IndicatorSnapshots.AsNoTracking().SingleAsync();

            var resolver = new CurrentCcosResolver(new IndicatorOrchestrationService(repository,
                new FakeProvider(), new IndicatorOrchestrationConfiguration()), Configuration());
            var result = await resolver.ResolveAsync(Holding(),
                new EarningsContext(AvailabilityStatus.Available, new DateOnly(2027, 1, 1)), EvaluationAt,
                new ConfigurationVersion(1));

            Assert.NotNull(result);
            Assert.Equal(EvaluationAt, result.EvaluationContext.Indicators.IndicatorCalculatedAtUtc);
            Assert.Equal(AsOfDate, result.EvaluationContext.IndicatorAsOfDate);
            db.ChangeTracker.Clear();
            var reloaded = await db.IndicatorSnapshots.AsNoTracking().SingleAsync();
            Assert.Equal(original.CalculatedAt, reloaded.CalculatedAt);
            Assert.Equal(original.SnapshotJson, reloaded.SnapshotJson);
        }
        finally
        {
            File.Delete(databasePath);
            File.Delete($"{databasePath}-shm");
            File.Delete($"{databasePath}-wal");
        }
    }

    [Fact]
    public async Task MissingCurrentIndicatorDateProducesUnavailableCcosContextWithoutFallback()
    {
        var repository = new FakeIndicatorRepository { LatestDate = null, Snapshot = Snapshot() };
        var resolver = new CurrentCcosResolver(new IndicatorOrchestrationService(repository,
            new FakeProvider(), new IndicatorOrchestrationConfiguration()), Configuration());

        var result = await resolver.ResolveAsync(Holding(),
            new EarningsContext(AvailabilityStatus.Unavailable, null), EvaluationAt,
            new ConfigurationVersion(1));

        Assert.Null(result);
        Assert.Equal(1, repository.LatestDateCalls);
        Assert.Equal(0, repository.SnapshotCalls);
    }

    [Fact]
    public async Task RejectsAConfigurationVersionDifferentFromThePhaseSixEvaluation()
    {
        var repository = new FakeIndicatorRepository { Snapshot = Snapshot() };
        var resolver = new CurrentCcosResolver(new IndicatorOrchestrationService(repository,
            new FakeProvider(), new IndicatorOrchestrationConfiguration()), Configuration());

        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(Holding(),
            new EarningsContext(AvailabilityStatus.Unavailable, null), EvaluationAt,
            new ConfigurationVersion(2)));

        Assert.Equal(0, repository.LatestDateCalls);
    }

    private static EntryStrategyOrchestrationConfiguration Configuration() => new()
    {
        StrategyConfiguration = new EntryStrategyConfiguration { Version = new ConfigurationVersion(1) },
        IndicatorConfiguration = new IndicatorConfiguration { Version = new ConfigurationVersion(1) },
        IndicatorCalculationVersion = IndicatorVersion,
        StrategyVersion = new StrategyVersion("4.0.0")
    };

    private static Holding Holding() => new(new Account("Test", Broker.Other,
        AccountType.Taxable, false), "MSFT", AssetType.Stock, 100,
        AssignmentSensitivity.Level3, TaxSensitivity.Moderate, 1, .25, .12, .18,
        70, 80, .1m, .1, .25);

    private static IndicatorSnapshot Snapshot() => new("MSFT", AsOfDate,
        Available(105m), Available(100m), Available(95m), Available(60d),
        Available(.5d), Available(1d), Available(0d), Available(.8d), Available(.1d),
        Available(1d), Available(.5d), Available(.5d), Available(2d), Available(.02d),
        Available(.2d), Available(.25d), IndicatorVersion, new ConfigurationVersion(1),
        EvaluationAt.AddMinutes(-1))
    {
        Iv30 = Available(.30d), IvRank = Available(50d), IvPercentile = Available(60d),
        ResistancePrice = Available(110m), DistanceToResistancePercent = Available(.05d),
        ResistanceTouchCount = Available(3), ResistanceAgeTradingDays = Available(20),
        MarketRegime = MarketRegime.Neutral, SectorRegime = MarketRegime.Neutral
    };

    private static IndicatorValue<T> Available<T>(T value) where T : struct =>
        IndicatorValue<T>.Available(value);

    private static OptionsEngineDbContext CreateContext(string databasePath) => new(
        new DbContextOptionsBuilder<OptionsEngineDbContext>().UseSqlite($"Data Source={databasePath}").Options);

    private sealed class FakeIndicatorRepository : IIndicatorDataRepository
    {
        public DateOnly? LatestDate { get; init; } = AsOfDate;
        public IndicatorSnapshot? Snapshot { get; init; }
        public int LatestDateCalls { get; private set; }
        public int SnapshotCalls { get; private set; }
        public int CalculationReadCalls { get; private set; }
        public int UpsertCalls { get; private set; }

        public Task<DateOnly?> GetLatestPriceObservationDateAsync(string symbol, string provider,
            DateOnly onOrBefore, CancellationToken cancellationToken = default)
        {
            LatestDateCalls++;
            return Task.FromResult(LatestDate);
        }

        public Task<IndicatorSnapshot?> GetSnapshotAsync(string symbol, DateOnly asOfDate,
            IndicatorCalculationVersion calculationVersion, ConfigurationVersion configurationVersion,
            CancellationToken cancellationToken = default)
        {
            SnapshotCalls++;
            return Task.FromResult<IndicatorSnapshot?>(Snapshot);
        }

        public Task<IReadOnlyList<IndicatorPriceObservation>> GetPricesThroughAsync(string symbol,
            string provider, DateOnly asOfDate, CancellationToken cancellationToken = default)
        {
            CalculationReadCalls++;
            return Task.FromResult<IReadOnlyList<IndicatorPriceObservation>>(
                [new(symbol, AsOfDate, 100m, 101m, 99m, 100m, 1000)]);
        }
        public Task<IReadOnlyList<OptionChain>> GetOptionChainsThroughAsync(string symbol,
            string provider, DateOnly asOfDate, DateTimeOffset calculatedAt,
            CancellationToken cancellationToken = default)
        {
            CalculationReadCalls++;
            return Task.FromResult<IReadOnlyList<OptionChain>>([]);
        }
        public Task<IReadOnlyList<HistoricalIv30Observation>> GetPriorValidIv30Async(string symbol,
            DateOnly asOfDate, IndicatorCalculationVersion calculationVersion,
            ConfigurationVersion configurationVersion, int limit,
            CancellationToken cancellationToken = default)
        {
            CalculationReadCalls++;
            return Task.FromResult<IReadOnlyList<HistoricalIv30Observation>>([]);
        }
        public Task UpsertSnapshotAsync(IndicatorSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            UpsertCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProvider : IMarketDataProvider
    {
        public string ProviderName => "FakeProvider";
        public Task<MarketQuote> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<HistoricalBar>> GetHistoricalPricesAsync(string symbol, DateOnly start,
            DateOnly end, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DateOnly>> GetOptionExpirationsAsync(string symbol,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OptionChain> GetOptionChainAsync(string symbol, DateOnly expiration,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

using Microsoft.EntityFrameworkCore;
using OptionsEngine.Application.PositionSizing;
using OptionsEngine.Domain.Accounts;
using OptionsEngine.Infrastructure.Persistence;

namespace OptionsEngine.Infrastructure.Tests;

public sealed class PositionSizingMarketDataRepositoryTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"options-engine-position-sizing-market-{Guid.NewGuid():N}.db");
    private static readonly DateOnly AsOfDate = new(2026, 9, 17);
    private static readonly DateTimeOffset Cutoff = new(2026, 9, 18, 16, 0, 0, TimeSpan.Zero);

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
    public async Task LatestDailyCloseUsesInclusiveAsOfCutoffWithoutFutureOrProviderSubstitution()
    {
        await using var db = CreateContext();
        db.HistoricalPriceBars.AddRange(
            Bar("MSFT", "TestProvider", AsOfDate.AddDays(-1), 100m),
            Bar("MSFT", "TestProvider", AsOfDate, 101m),
            Bar("MSFT", "TestProvider", AsOfDate.AddDays(1), 102m),
            Bar("MSFT", "OtherProvider", AsOfDate, 999m));
        await db.SaveChangesAsync();

        var repository = new SqlitePositionSizingMarketDataRepository(db);
        var selected = await repository.GetLatestDailyCloseAsync(" msft ", "TestProvider", AsOfDate);

        Assert.Equal(new PositionSizingDailyClose(101m, AsOfDate), selected);
        Assert.Null(await repository.GetLatestDailyCloseAsync("MSFT", "Missing", AsOfDate));
    }

    [Fact]
    public async Task LatestOptionObservationsUsePerSymbolInclusiveUtcCutoffWithoutFreshnessLimit()
    {
        await using var db = CreateContext();
        const string first = "MSFT261023C00500000";
        const string second = "MSFT261120C00510000";
        db.OptionContractSnapshots.AddRange(
            Option(first, "TestProvider", Cutoff.AddMinutes(-1), .10),
            Option(first, "TestProvider", Cutoff, .20),
            Option(first, "TestProvider", Cutoff.AddTicks(1), .90),
            Option(second, "TestProvider", Cutoff.AddYears(-10), null),
            Option(second, "OtherProvider", Cutoff, .80));
        await db.SaveChangesAsync();

        var repository = new SqlitePositionSizingMarketDataRepository(db);
        var observations = await repository.GetLatestOptionDeltaObservationsAsync([first, second], "TestProvider", Cutoff);

        Assert.Equal(2, observations.Count);
        Assert.Equal((.20, Cutoff), (observations.Single(x => x.OptionSymbol == first).Delta,
            observations.Single(x => x.OptionSymbol == first).ObservationTimestampUtc));
        Assert.Equal((null, Cutoff.AddYears(-10)), (observations.Single(x => x.OptionSymbol == second).Delta,
            observations.Single(x => x.OptionSymbol == second).ObservationTimestampUtc));
    }

    [Fact]
    public async Task AccountHoldingReadIncludesDisabledHoldingsAndExcludesOtherAccounts()
    {
        await using var db = CreateContext();
        var targetAccount = new Account("Target", Broker.Other, AccountType.Taxable, false);
        var otherAccount = new Account("Other", Broker.Other, AccountType.Taxable, false);
        var enabled = Holding(targetAccount, "MSFT", true);
        var disabled = Holding(targetAccount, "AAPL", false);
        var other = Holding(otherAccount, "NVDA", true);
        db.AddRange(targetAccount, otherAccount, enabled, disabled, other);
        await db.SaveChangesAsync();

        var holdings = await new SqliteHoldingRepository(db).GetByAccountIdAsync(targetAccount.AccountId);

        Assert.Equal(["AAPL", "MSFT"], holdings.Select(holding => holding.Symbol).OrderBy(symbol => symbol));
        Assert.Contains(holdings, holding => !holding.IsEnabled);
        Assert.DoesNotContain(holdings, holding => holding.HoldingId == other.HoldingId);
    }

    private OptionsEngineDbContext CreateContext() => new(
        new DbContextOptionsBuilder<OptionsEngineDbContext>().UseSqlite($"Data Source={_path}").Options);

    private static HistoricalPriceBarEntity Bar(string symbol, string provider, DateOnly date, decimal close) => new()
    {
        Symbol = symbol,
        Provider = provider,
        Date = date,
        Open = close,
        High = close,
        Low = close,
        Close = close,
        RetrievedAt = Cutoff
    };

    private static OptionContractSnapshotEntity Option(string optionSymbol, string provider, DateTimeOffset timestamp,
        double? delta) => new()
    {
        OptionSymbol = optionSymbol,
        UnderlyingSymbol = "MSFT",
        Provider = provider,
        Timestamp = timestamp,
        TimestampUtcTicks = timestamp.UtcTicks,
        Expiration = new DateOnly(2026, 10, 23),
        Strike = 500m,
        OptionType = OptionsEngine.MarketData.Models.OptionType.Call,
        Delta = delta
    };

    private static Holding Holding(Account account, string symbol, bool isEnabled) => new(account, symbol, AssetType.Stock,
        100m, AssignmentSensitivity.Level3, TaxSensitivity.Moderate, 1m, .25, .12, .18, 70, 80, .1m, .1, .25, isEnabled);
}

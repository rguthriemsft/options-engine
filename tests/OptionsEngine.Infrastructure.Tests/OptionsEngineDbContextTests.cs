using Microsoft.EntityFrameworkCore;
using OptionsEngine.Domain.Accounts;
using OptionsEngine.Infrastructure.Persistence;
using OptionsEngine.MarketData.Models;

namespace OptionsEngine.Infrastructure.Tests;

public sealed class OptionsEngineDbContextTests : IAsyncLifetime
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"options-engine-{Guid.NewGuid():N}.db");

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync()
    {
        File.Delete(databasePath);
        File.Delete($"{databasePath}-shm");
        File.Delete($"{databasePath}-wal");
        return Task.CompletedTask;
    }

    [Fact]
    public async Task PersistsAccountHoldingsAndMultipleTaxLotsWithTheirRelationships()
    {
        var taxable = new Account("Taxable", Broker.Fidelity, AccountType.Taxable, false);
        var roth = new Account("Roth", Broker.Schwab, AccountType.RothIra, true);
        var firstHolding = CreateHolding(taxable, "MSFT");
        var secondHolding = CreateHolding(roth, "MSFT");
        var firstLot = new TaxLot(firstHolding, new DateOnly(2020, 1, 2), 100m, 10m, 1000m, HoldingPeriodClassification.LongTerm);
        var secondLot = new TaxLot(firstHolding, new DateOnly(2021, 3, 4), 50m, 20m, 1000m, HoldingPeriodClassification.LongTerm);

        await using (var context = CreateContext())
        {
            context.AddRange(taxable, roth, firstHolding, secondHolding, firstLot, secondLot);
            await context.SaveChangesAsync();
        }

        await using var verificationContext = CreateContext();
        var holdings = await verificationContext.Holdings
            .Include(holding => holding.Account)
            .Include(holding => holding.TaxLots)
            .OrderBy(holding => holding.Account.Name)
            .ToListAsync();

        Assert.Equal(2, holdings.Count);
        Assert.All(holdings, holding => Assert.Equal("MSFT", holding.Symbol));
        Assert.Contains(holdings, holding => holding.Account.Name == "Taxable" && holding.TaxLots.Count == 2);
        Assert.Contains(holdings, holding => holding.Account.Name == "Roth" && holding.TaxLots.Count == 0);
    }

    [Fact]
    public async Task MarketSnapshotsRetainOptionHistoryAndUpsertDailyBars()
    {
        await using var context = CreateContext();
        var cache = new SqliteMarketDataCache(context);
        var first = new DateTimeOffset(2026, 1, 2, 14, 0, 0, TimeSpan.Zero);
        await cache.SaveQuoteAsync(new("MSFT", first, 500m, null, null, null, null, null, null, null, "Tradier", null));
        await cache.SaveQuoteAsync(new("MSFT", first.AddMinutes(1), 501m, null, null, null, null, null, null, null, "Tradier", null));
        var bar = new HistoricalBar("MSFT", new DateOnly(2026, 1, 2), 1m, 2m, 1m, 2m, null, "Tradier");
        await cache.UpsertHistoricalBarsAsync([bar], bar.Date, bar.Date, first);
        await cache.UpsertHistoricalBarsAsync([bar with { Close = 3m }], bar.Date, bar.Date, first.AddHours(1));
        var chain = new OptionChain("MSFT", new DateOnly(2026, 1, 17), first, [new("MSFT260117C00500000", "MSFT", first, new DateOnly(2026, 1, 17), 500m, OptionType.Call, null, null, null, null, null, null, null, null, null, null, null, "Tradier")], "Tradier");
        await cache.SaveOptionChainAsync(chain); await cache.SaveOptionChainAsync(chain with { Timestamp = first.AddMinutes(1), Contracts = [chain.Contracts[0] with { Timestamp = first.AddMinutes(1), Delta = 0d }] });
        Assert.Equal(2, await context.MarketQuoteSnapshots.CountAsync()); Assert.Equal(1, await context.HistoricalPriceBars.CountAsync()); Assert.Equal(3m, (await context.HistoricalPriceBars.SingleAsync()).Close); Assert.Equal(2, await context.OptionContractSnapshots.CountAsync()); Assert.Null((await context.OptionContractSnapshots.ToListAsync()).OrderBy(x => x.Timestamp).First().Delta);
    }

    [Fact]
    public async Task PopulatedPhase1DatabaseUpgradesWithoutLosingPortfolioData()
    {
        var account = new Account("Preserve", Broker.Fidelity, AccountType.Taxable, false);
        var holding = CreateHolding(account, "MSFT");
        var lot = new TaxLot(holding, new DateOnly(2020, 1, 2), 100m, 10m, 1000m, HoldingPeriodClassification.LongTerm);
        var accountId = account.AccountId; var holdingId = holding.HoldingId; var lotId = lot.TaxLotId;
        await using (var phase1 = CreateContext()) { await phase1.Database.MigrateAsync("20260917184555_InitialCreate"); phase1.AddRange(account, holding, lot); await phase1.SaveChangesAsync(); }
        await using (var upgrade = CreateContext()) { await upgrade.Database.MigrateAsync(); }
        await using var verify = CreateContext();
        var persisted = await verify.Accounts.Include(x => x.Holdings).ThenInclude(x => x.TaxLots).SingleAsync(x => x.AccountId == accountId);
        var persistedHolding = Assert.Single(persisted.Holdings); var persistedLot = Assert.Single(persistedHolding.TaxLots);
        Assert.Equal("Preserve", persisted.Name); Assert.Equal(holdingId, persistedHolding.HoldingId); Assert.Equal("MSFT", persistedHolding.Symbol); Assert.Equal(lotId, persistedLot.TaxLotId); Assert.Equal(1000m, persistedLot.TotalCostBasis); Assert.Equal(0, await verify.HistoricalPriceCoverages.CountAsync());
    }

    [Fact]
    public void RelationshipsUseRestrictiveDeleteBehavior()
    {
        using var context = CreateContext();
        var holdingEntityType = context.Model.FindEntityType(typeof(Holding))!;
        var taxLotEntityType = context.Model.FindEntityType(typeof(TaxLot))!;
        var holdingForeignKey = holdingEntityType
            .FindForeignKeys(holdingEntityType.FindProperty(nameof(Holding.AccountId))!)
            .Single();
        var lotForeignKey = taxLotEntityType
            .FindForeignKeys(taxLotEntityType.FindProperty(nameof(TaxLot.HoldingId))!)
            .Single();

        Assert.Equal(DeleteBehavior.Restrict, holdingForeignKey.DeleteBehavior);
        Assert.Equal(DeleteBehavior.Restrict, lotForeignKey.DeleteBehavior);
    }

    private OptionsEngineDbContext CreateContext() => new(
        new DbContextOptionsBuilder<OptionsEngineDbContext>().UseSqlite($"Data Source={databasePath}").Options);

    private static Holding CreateHolding(Account account, string symbol) => new(
        account, symbol, AssetType.Stock, 250m, AssignmentSensitivity.Level4, TaxSensitivity.High,
        0.7m, 0.2, 0.12, 0.18, 70, 80, 0.5m, 0.1, 0.15);
}

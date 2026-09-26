using Microsoft.EntityFrameworkCore;
using OptionsEngine.Infrastructure.Persistence;
using OptionsEngine.MarketData.Models;

namespace OptionsEngine.Infrastructure.Tests;

public sealed class DefenseMarketDataRepositoryTests : IAsyncLifetime
{
    private const string Provider = "TestProvider";
    private const string ExistingOption = "MSFT260930C00100000";
    private readonly string path = Path.Combine(Path.GetTempPath(),
        $"options-engine-defense-market-{Guid.NewGuid():N}.db");

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync()
    {
        File.Delete(path);
        File.Delete($"{path}-shm");
        File.Delete($"{path}-wal");
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CurrentObservationUsesExactContractProviderAndInclusiveCutoffWithStableIdTieBreak()
    {
        var cutoff = Utc(2026, 9, 18, 16);
        await using var db = CreateContext();
        var firstAtCutoff = Option(ExistingOption, new DateOnly(2026, 9, 30), cutoff, .30);
        var lastAtCutoff = Option(ExistingOption, new DateOnly(2026, 9, 30), cutoff, .40);
        db.OptionContractSnapshots.AddRange(
            Option(ExistingOption, new DateOnly(2026, 9, 30), cutoff.AddMinutes(-1), .20),
            firstAtCutoff,
            lastAtCutoff,
            Option(ExistingOption, new DateOnly(2026, 9, 30), cutoff.AddTicks(1), .90),
            Option("OTHER", new DateOnly(2026, 9, 30), cutoff, .99),
            Option(ExistingOption, new DateOnly(2026, 9, 30), cutoff, .98, "OtherProvider"));
        await db.SaveChangesAsync();

        var selected = await new SqliteDefenseMarketDataRepository(db)
            .GetLatestOptionObservationAsync(ExistingOption, Provider, cutoff);

        Assert.NotNull(selected);
        Assert.True(lastAtCutoff.OptionContractSnapshotId > firstAtCutoff.OptionContractSnapshotId);
        Assert.Equal((cutoff, .40), (selected.ObservationTimestampUtc, selected.Delta));
    }

    [Theory]
    [InlineData(2026, 9, 18, 2026, 9, 17)]
    [InlineData(2026, 9, 21, 2026, 9, 18)]
    [InlineData(2026, 9, 22, 2026, 9, 18)]
    public async Task PreviousObservationUsesLatestEarlierNewYorkDateWithAnExactContractObservation(
        int currentYear, int currentMonth, int currentDay,
        int expectedYear, int expectedMonth, int expectedDay)
    {
        var current = Utc(currentYear, currentMonth, currentDay, 16);
        var expected = Utc(expectedYear, expectedMonth, expectedDay, 19);
        await using var db = CreateContext();
        db.OptionContractSnapshots.AddRange(
            Option(ExistingOption, new DateOnly(2026, 9, 30), expected.AddHours(-2), .20),
            Option(ExistingOption, new DateOnly(2026, 9, 30), expected, .25),
            Option("WRONG", new DateOnly(2026, 9, 30), current.AddMinutes(-1), .99),
            Option(ExistingOption, new DateOnly(2026, 9, 30), current, .40));
        await db.SaveChangesAsync();

        var selected = await new SqliteDefenseMarketDataRepository(db)
            .GetPreviousTradingDateOptionObservationAsync(ExistingOption, Provider, current, current);

        Assert.NotNull(selected);
        Assert.Equal((expected, .25, ExistingOption),
            (selected.ObservationTimestampUtc, selected.Delta, selected.OptionSymbol));
    }

    [Fact]
    public async Task ReplacementChainsSelectLatestCompleteSnapshotPerExpirationWithoutTimestampMixing()
    {
        var cutoff = Utc(2026, 9, 18, 16);
        var firstExpiration = new DateOnly(2026, 10, 20);
        var secondExpiration = new DateOnly(2026, 10, 27);
        var emptyExpiration = new DateOnly(2026, 11, 3);
        var firstSelectedAt = cutoff.AddMinutes(-2);
        var secondSelectedAt = cutoff.AddMinutes(-5);
        await using var db = CreateContext();
        db.OptionContractSnapshots.AddRange(
            Option("OLD", firstExpiration, cutoff.AddMinutes(-10), .10),
            Option("A", firstExpiration, firstSelectedAt, .20),
            Option("B", firstExpiration, firstSelectedAt, .21),
            Option("FUTURE", firstExpiration, cutoff.AddTicks(1), .90),
            Option("C", secondExpiration, secondSelectedAt, .22),
            Option("C-FUTURE", secondExpiration, cutoff.AddMinutes(1), .80));
        db.EmptyOptionChainSnapshots.Add(new EmptyOptionChainSnapshotEntity
        {
            UnderlyingSymbol = "MSFT", Provider = Provider, Expiration = emptyExpiration,
            Timestamp = cutoff.AddMinutes(-1), TimestampUtcTicks = cutoff.AddMinutes(-1).UtcTicks
        });
        await db.SaveChangesAsync();

        var selected = await new SqliteDefenseMarketDataRepository(db).GetLatestOptionChainsAsync(
            "MSFT", Provider, firstExpiration, emptyExpiration, cutoff);

        Assert.Equal(3, selected.Count);
        Assert.Equal(firstSelectedAt, selected[0].ObservationTimestampUtc);
        Assert.Equal(new[] { "A", "B" },
            selected[0].Contracts.Select(value => value.OptionSymbol).ToArray());
        Assert.All(selected[0].Contracts,
            value => Assert.Equal(firstSelectedAt, value.ObservationTimestampUtc));
        Assert.Equal(secondSelectedAt, selected[1].ObservationTimestampUtc);
        Assert.Equal("C", Assert.Single(selected[1].Contracts).OptionSymbol);
        Assert.Equal(emptyExpiration, selected[2].Expiration);
        Assert.Empty(selected[2].Contracts);
    }

    private OptionsEngineDbContext CreateContext() => new(
        new DbContextOptionsBuilder<OptionsEngineDbContext>().UseSqlite($"Data Source={path}").Options);

    private static OptionContractSnapshotEntity Option(string optionSymbol, DateOnly expiration,
        DateTimeOffset timestamp, double? delta, string provider = Provider) => new()
    {
        OptionSymbol = optionSymbol,
        UnderlyingSymbol = "MSFT",
        Provider = provider,
        Timestamp = timestamp,
        TimestampUtcTicks = timestamp.UtcTicks,
        Expiration = expiration,
        Strike = optionSymbol == ExistingOption ? 100m : 110m,
        OptionType = OptionType.Call,
        Bid = 1m,
        Ask = 1.1m,
        OpenInterest = 500,
        Delta = delta,
        UnderlyingPrice = 105m
    };

    private static DateTimeOffset Utc(int year, int month, int day, int hour) =>
        new(year, month, day, hour, 0, 0, TimeSpan.Zero);
}

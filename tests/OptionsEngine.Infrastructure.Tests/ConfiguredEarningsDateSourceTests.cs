using OptionsEngine.Application.EntryStrategy;

namespace OptionsEngine.Infrastructure.Tests;

public sealed class ConfiguredEarningsDateSourceTests
{
    private static readonly DateTimeOffset EvaluationAt = new(2026, 9, 18, 15, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("msft")]
    [InlineData("MSFT")]
    [InlineData(" MsFt ")]
    public async Task SymbolNormalizationSelectsTheSameConfiguredDate(string symbol)
    {
        var source = Source(["2026-10-28"]);

        Assert.Equal(new DateOnly(2026, 10, 28), await source.GetNextEarningsDateAsync(symbol, EvaluationAt));
    }

    [Fact]
    public async Task SelectsEarliestCurrentOrFutureDateRegardlessOfOrderingOrDuplicates()
    {
        var source = Source(["2027-01-27", "2026-10-28", "2026-07-20", "2026-10-28"]);

        Assert.Equal(new DateOnly(2026, 10, 28), await source.GetNextEarningsDateAsync("MSFT", EvaluationAt));
    }

    [Fact]
    public async Task DateEqualToNewYorkEvaluationDateIsCurrent()
    {
        var source = Source(["2026-09-17", "2026-10-28"]);
        var cutoff = new DateTimeOffset(2026, 9, 18, 1, 0, 0, TimeSpan.Zero); // Sep 17 in New York.

        Assert.Equal(new DateOnly(2026, 9, 17), await source.GetNextEarningsDateAsync("MSFT", cutoff));
    }

    [Fact]
    public async Task MissingSymbolAndPastOnlyDatesAreUnavailable()
    {
        var source = Source(["2026-07-20"]);
        var empty = Source([]);

        Assert.Null(await source.GetNextEarningsDateAsync("MSFT", EvaluationAt));
        Assert.Null(await source.GetNextEarningsDateAsync("AAPL", EvaluationAt));
        Assert.Null(await empty.GetNextEarningsDateAsync("MSFT", EvaluationAt));
    }

    [Theory]
    [InlineData("2026/10/28")]
    [InlineData("not-a-date")]
    public void MalformedConfiguredDateFailsClearlyAtConstruction(string configuredDate)
    {
        var exception = Assert.Throws<ArgumentException>(() => Source([configuredDate]));

        Assert.Contains("yyyy-MM-dd", exception.Message);
    }

    private static ConfiguredEarningsDateSource Source(string[] dates) => new(new EarningsCalendarConfiguration
    {
        Symbols = new Dictionary<string, string[]> { ["MSFT"] = dates }
    });
}

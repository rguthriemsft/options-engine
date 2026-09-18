using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OptionsEngine.Infrastructure.Persistence;
using OptionsEngine.MarketData;
using OptionsEngine.MarketData.Models;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Api.Tests;

public sealed class IndicatorEndpointTests : IAsyncLifetime
{
    private static readonly DateOnly Friday = new(2026, 9, 18);
    private static readonly DateOnly Sunday = Friday.AddDays(2);
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"options-engine-api-indicators-{Guid.NewGuid():N}.db");
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero));
    private readonly Dictionary<string, string?> _originalEnvironment = new();

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync()
    {
        foreach (var (key, value) in _originalEnvironment)
            Environment.SetEnvironmentVariable(key, value);
        File.Delete(_path);
        File.Delete($"{_path}-shm");
        File.Delete($"{_path}-wal");
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ExplicitAsOfCalculatesPersistsAndReturnsCompleteVersionedFacts()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        await SeedTrendAsync(factory);
        var response = await client.GetAsync($"/api/indicators/msft?asOf={Friday:yyyy-MM-dd}");
        var body = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("MSFT", body.GetProperty("symbol").GetString());
        Assert.Equal(Friday.ToString("yyyy-MM-dd"), body.GetProperty("asOfDate").GetString());
        Assert.Equal("phase3f-test", body.GetProperty("indicatorCalculationVersion").GetString());
        Assert.Equal(7, body.GetProperty("configurationVersion").GetInt32());
        Assert.Equal("Available", body.GetProperty("sma200").GetProperty("status").GetString());
        Assert.Equal("Available", body.GetProperty("rsi14").GetProperty("status").GetString());
        Assert.Equal("Available", body.GetProperty("iv30").GetProperty("status").GetString());
        Assert.Equal("BULLISH", body.GetProperty("marketRegime").GetString());
        Assert.Equal("BEARISH", body.GetProperty("sectorRegime").GetString());
        Assert.True(body.TryGetProperty("bollingerPercentB", out _));
        Assert.True(body.TryGetProperty("macdHistogram", out _));
        Assert.True(body.TryGetProperty("resistanceAgeTradingDays", out _));
        Assert.False(body.TryGetProperty("provider", out _));

        await using var db = Context();
        Assert.Single(await db.IndicatorSnapshots.ToListAsync());
        Assert.Equal(230 * 3, await db.HistoricalPriceBars.CountAsync());
        Assert.Equal(2, await db.OptionContractSnapshots.CountAsync());
    }

    [Fact]
    public async Task OmittedAsOfUsesLatestPersistedTradingDateBeforeRequestBoundary()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        await SeedTrendAsync(factory);
        await SeedBarAsync(factory, "MSFT", Sunday.AddDays(1), 999m);

        var response = await client.GetAsync("/api/indicators/MSFT");
        var body = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Friday.ToString("yyyy-MM-dd"), body.GetProperty("asOfDate").GetString());
    }

    [Fact]
    public async Task ExplicitNonTradingAsOfRetainsRequestedDateAndPrecedingTradingRegimes()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        await SeedTrendAsync(factory);
        var trading = await BodyAsync(await client.GetAsync($"/api/indicators/MSFT?asOf={Friday:yyyy-MM-dd}"));
        var nonTrading = await BodyAsync(await client.GetAsync($"/api/indicators/MSFT?asOf={Sunday:yyyy-MM-dd}"));

        Assert.Equal(Sunday.ToString("yyyy-MM-dd"), nonTrading.GetProperty("asOfDate").GetString());
        Assert.Equal(trading.GetProperty("sma50").GetRawText(), nonTrading.GetProperty("sma50").GetRawText());
        Assert.Equal(trading.GetProperty("marketRegime").GetString(), nonTrading.GetProperty("marketRegime").GetString());
        Assert.Equal(trading.GetProperty("sectorRegime").GetString(), nonTrading.GetProperty("sectorRegime").GetString());
    }

    [Fact]
    public async Task RepeatedGetReplacesOneCanonicalRowAndEligibleCorrectionChangesFacts()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        await SeedTrendAsync(factory);
        var first = await BodyAsync(await client.GetAsync($"/api/indicators/MSFT?asOf={Friday:yyyy-MM-dd}"));
        _clock.Now = _clock.Now.AddHours(1);
        await SeedBarAsync(factory, "MSFT", Friday, 400m);
        var second = await BodyAsync(await client.GetAsync($"/api/indicators/MSFT?asOf={Friday:yyyy-MM-dd}"));

        Assert.NotEqual(first.GetProperty("sma20").GetRawText(), second.GetProperty("sma20").GetRawText());
        Assert.NotEqual(first.GetProperty("calculatedAt").GetString(), second.GetProperty("calculatedAt").GetString());
        Assert.Equal(first.GetProperty("asOfDate").GetString(), second.GetProperty("asOfDate").GetString());
        await using var db = Context();
        Assert.Single(await db.IndicatorSnapshots.ToListAsync());
    }

    [Fact]
    public async Task FutureSourceObservationsCannotChangeHistoricalApiFacts()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        await SeedTrendAsync(factory);
        var before = await BodyAsync(await client.GetAsync($"/api/indicators/MSFT?asOf={Friday:yyyy-MM-dd}"));
        foreach (var symbol in new[] { "MSFT", "SPY", "XLK" })
            await SeedBarAsync(factory, symbol, Sunday.AddDays(1), symbol == "SPY" ? 1m : 999m);
        await SeedChainAsync(factory, Sunday.AddDays(1), 0.9);
        var after = await BodyAsync(await client.GetAsync($"/api/indicators/MSFT?asOf={Friday:yyyy-MM-dd}"));

        Assert.Equal(before.GetRawText(), after.GetRawText());
        await using var db = Context();
        Assert.Single(await db.IndicatorSnapshots.ToListAsync());
    }

    [Fact]
    public async Task PersistedNewerEmptyOptionChainDoesNotResurrectOlderIv()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        await SeedBarAsync(factory, "MSFT", Friday, 100m);
        await SeedChainAsync(factory, Friday, 0.3, 100m);
        await using (var db = Context())
        {
            await new SqliteMarketDataCache(db).SaveOptionChainAsync(
                new OptionChain("MSFT", Friday.AddDays(30), At(Friday, 11), [], "Test"));
        }
        var body = await BodyAsync(await client.GetAsync($"/api/indicators/MSFT?asOf={Friday:yyyy-MM-dd}"));

        Assert.Equal("InsufficientData", body.GetProperty("iv30").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("iv30").GetProperty("value").ValueKind);
        Assert.Equal("NoEligibleAtmPair", body.GetProperty("iv30UnavailableReason").GetString());
    }

    [Fact]
    public async Task FutureOptionSnapshotDoesNotDisplaceLatestHistoricalValidChain()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        await SeedBarAsync(factory, "MSFT", Friday, 100m);
        await SeedChainAsync(factory, Friday, 0.3, 100m);
        await using (var db = Context())
        {
            await new SqliteMarketDataCache(db).SaveOptionChainAsync(
                new OptionChain("MSFT", Friday.AddDays(30), At(Sunday.AddDays(1), 10), [], "Test"));
        }
        var body = await BodyAsync(await client.GetAsync($"/api/indicators/MSFT?asOf={Friday:yyyy-MM-dd}"));

        Assert.Equal("Available", body.GetProperty("iv30").GetProperty("status").GetString());
        Assert.Equal(0.3, body.GetProperty("iv30").GetProperty("value").GetDouble(), 12);
    }

    [Fact]
    public async Task ZeroRemainsAvailableWhileMissingFactsAndSectorRemainUnavailable()
    {
        using var factory = Factory(withSector: false);
        using var client = factory.CreateClient();
        await SeedBarsAsync(factory, "MSFT", WeekdaysThrough(Friday, 20), _ => 0m);
        var body = await BodyAsync(await client.GetAsync($"/api/indicators/MSFT?asOf={Friday:yyyy-MM-dd}"));

        Assert.Equal(0m, body.GetProperty("sma20").GetProperty("value").GetDecimal());
        Assert.Equal("Available", body.GetProperty("sma20").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("sma50").GetProperty("value").ValueKind);
        Assert.Equal("InsufficientData", body.GetProperty("sma50").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("iv30").GetProperty("value").ValueKind);
        Assert.Equal("NoEligibleSnapshot", body.GetProperty("iv30UnavailableReason").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("resistancePrice").GetProperty("value").ValueKind);
        Assert.Equal("INSUFFICIENT_DATA", body.GetProperty("sectorRegime").GetString());
    }

    [Fact]
    public async Task NoPersistedTradingDateIsNotFoundButExplicitDateCanReturnInsufficientSnapshot()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/indicators/MSFT")).StatusCode);
        var explicitResponse = await client.GetAsync($"/api/indicators/MSFT?asOf={Friday:yyyy-MM-dd}");
        var body = await BodyAsync(explicitResponse);
        Assert.Equal(HttpStatusCode.OK, explicitResponse.StatusCode);
        Assert.Equal("InsufficientData", body.GetProperty("sma20").GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("/api/indicators/%20?asOf=2026-09-18")]
    [InlineData("/api/indicators/M%20SFT?asOf=2026-09-18")]
    [InlineData("/api/indicators/MSFT?asOf=invalid")]
    [InlineData("/api/indicators/MSFT?asOf=2026-09-18&asOf=2026-09-18")]
    [InlineData("/api/indicators/MSFT?calculationVersion=other")]
    [InlineData("/api/indicators/MSFT?version=other")]
    [InlineData("/api/indicators/MSFT?indicatorCalculationVersion=other")]
    [InlineData("/api/indicators/MSFT?configurationVersion=99")]
    public async Task MalformedOrClientVersionRequestsAreRejected(string path)
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path)).StatusCode);
        await using var db = Context();
        Assert.Empty(await db.IndicatorSnapshots.ToListAsync());
    }

    [Fact]
    public async Task ServerVersionsCanCoexistAndClientCannotRetrieveAnotherVersion()
    {
        using (var firstFactory = Factory(version: "phase3f-a", configurationVersion: 7))
        {
            using var firstClient = firstFactory.CreateClient();
            await SeedBarAsync(firstFactory, "MSFT", Friday, 100m);
            Assert.Equal(HttpStatusCode.OK, (await firstClient.GetAsync($"/api/indicators/MSFT?asOf={Friday:yyyy-MM-dd}")).StatusCode);
        }
        using (var secondFactory = Factory(version: "phase3f-b", configurationVersion: 8))
        {
            using var secondClient = secondFactory.CreateClient();
            var body = await BodyAsync(await secondClient.GetAsync($"/api/indicators/MSFT?asOf={Friday:yyyy-MM-dd}"));
            Assert.Equal("phase3f-b", body.GetProperty("indicatorCalculationVersion").GetString());
            Assert.Equal(8, body.GetProperty("configurationVersion").GetInt32());
            await using var db = Context();
            Assert.Equal(2, await db.IndicatorSnapshots.CountAsync());
            var repository = new SqliteIndicatorDataRepository(db);
            Assert.NotNull(await repository.GetSnapshotAsync("MSFT", Friday, new IndicatorCalculationVersion("phase3f-a"),
                new ConfigurationVersion(7)));
            Assert.Null(await repository.GetSnapshotAsync("MSFT", Friday, new IndicatorCalculationVersion("phase3f-a"),
                new ConfigurationVersion(8)));
        }
    }

    [Theory]
    [InlineData("", 7)]
    [InlineData("phase3f-test", 0)]
    public void MissingOrInvalidServerVersionFailsStartup(string version, int configurationVersion)
    {
        using var factory = Factory(version: version, configurationVersion: configurationVersion);
        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public void InvalidIndicatorParametersFailStartup()
    {
        using var factory = Factory(extraConfiguration: new Dictionary<string, string?>
        {
            ["Indicators:RelativeStrengthIndex:Period"] = "0"
        });
        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public async Task ResistanceUsesQualifiedTwoTouchClusterNotIsolatedSwingHigh()
    {
        using var factory = Factory(extraConfiguration: new Dictionary<string, string?>
        {
            ["Indicators:AverageTrueRange:Period"] = "1",
            ["Indicators:Resistance:LookbackTradingDays"] = "40"
        });
        using var client = factory.CreateClient();
        var dates = WeekdaysThrough(Friday, 40);
        await using (var db = Context())
        {
            var cache = new SqliteMarketDataCache(db);
            var bars = dates.Select((date, index) => new HistoricalBar("MSFT", date, 90m,
                index == 5 ? 100m : index == 15 ? 100.6m : index == 39 ? 91m : 90m,
                90m, 90m, 1000, "Test")).ToArray();
            await cache.UpsertHistoricalBarsAsync(bars, dates[0], Friday, _clock.Now);
        }
        var body = await BodyAsync(await client.GetAsync($"/api/indicators/MSFT?asOf={Friday:yyyy-MM-dd}"));
        Assert.Equal(100.3m, body.GetProperty("resistancePrice").GetProperty("value").GetDecimal());
        Assert.Equal(2, body.GetProperty("resistanceTouchCount").GetProperty("value").GetInt32());

        await using (var db = Context())
        {
            var loneTouch = await db.HistoricalPriceBars.SingleAsync(x => x.Symbol == "MSFT" && x.Date == dates[15]);
            loneTouch.High = 90m;
            await db.SaveChangesAsync();
        }
        var oneTouch = await BodyAsync(await client.GetAsync($"/api/indicators/MSFT?asOf={Friday:yyyy-MM-dd}"));
        Assert.Equal("InsufficientData", oneTouch.GetProperty("resistancePrice").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, oneTouch.GetProperty("resistancePrice").GetProperty("value").ValueKind);
    }

    private WebApplicationFactory<Program> Factory(string version = "phase3f-test", int configurationVersion = 7,
        bool withSector = true, IReadOnlyDictionary<string, string?>? extraConfiguration = null)
    {
        SetConfiguration("ConnectionStrings__OptionsEngine", $"Data Source={_path}");
        SetConfiguration("Indicators__CalculationVersion", version);
        SetConfiguration("Indicators__ConfigurationVersion", configurationVersion.ToString());
        SetConfiguration("Indicators__SectorBenchmarks__MSFT", withSector ? "XLK" : null);
        if (extraConfiguration is not null)
            foreach (var pair in extraConfiguration)
                SetConfiguration(pair.Key.Replace(":", "__"), pair.Value);
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMarketDataProvider>();
                services.AddScoped<IMarketDataProvider, TestProvider>();
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(_clock);
            });
        });
    }

    private void SetConfiguration(string key, string? value)
    {
        if (!_originalEnvironment.ContainsKey(key))
            _originalEnvironment[key] = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, value);
    }

    private OptionsEngineDbContext Context() => new(
        new DbContextOptionsBuilder<OptionsEngineDbContext>().UseSqlite($"Data Source={_path}").Options);

    private async Task SeedTrendAsync(WebApplicationFactory<Program> factory)
    {
        var dates = WeekdaysThrough(Friday, 230);
        await SeedBarsAsync(factory, "MSFT", dates, index => 300m + index);
        await SeedBarsAsync(factory, "SPY", dates, index => 300m + index);
        await SeedBarsAsync(factory, "XLK", dates, index => 300m - index);
        await SeedChainAsync(factory, Friday, 0.3);
    }

    private async Task SeedBarsAsync(WebApplicationFactory<Program> factory, string symbol,
        IReadOnlyList<DateOnly> dates, Func<int, decimal> close)
    {
        await using var db = Context();
        var bars = dates.Select((date, index) => Bar(symbol, date, close(index))).ToArray();
        await new SqliteMarketDataCache(db).UpsertHistoricalBarsAsync(bars, dates[0], dates[^1], _clock.Now);
    }

    private async Task SeedBarAsync(WebApplicationFactory<Program> factory, string symbol, DateOnly date, decimal close)
    {
        await using var db = Context();
        await new SqliteMarketDataCache(db).UpsertHistoricalBarsAsync([Bar(symbol, date, close)], date, date, _clock.Now);
    }

    private async Task SeedChainAsync(WebApplicationFactory<Program> factory, DateOnly observedDate, double iv,
        decimal underlyingPrice = 529m)
    {
        await using var db = Context();
        var timestamp = At(observedDate, 10);
        var expiration = Friday.AddDays(30);
        var contracts = new[]
        {
            new OptionContractSnapshot("MSFT-C", "MSFT", timestamp, expiration, underlyingPrice, OptionType.Call,
                null, null, null, null, null, iv, null, null, null, null, underlyingPrice, "Test"),
            new OptionContractSnapshot("MSFT-P", "MSFT", timestamp, expiration, underlyingPrice, OptionType.Put,
                null, null, null, null, null, iv, null, null, null, null, underlyingPrice, "Test")
        };
        await new SqliteMarketDataCache(db).SaveOptionChainAsync(new OptionChain("MSFT", expiration, timestamp, contracts, "Test"));
    }

    private static HistoricalBar Bar(string symbol, DateOnly date, decimal close) =>
        new(symbol, date, close, close + 1m, close - 1m, close, 1000, "Test");

    private static DateTimeOffset At(DateOnly date, int hour) => new(date, new TimeOnly(hour, 0), TimeSpan.Zero);

    private static DateOnly[] WeekdaysThrough(DateOnly last, int count) => Enumerable.Range(0, count * 2)
        .Select(offset => last.AddDays(-offset))
        .Where(date => date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
        .Take(count).Reverse().ToArray();

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class TestProvider : IMarketDataProvider
    {
        public string ProviderName => "Test";
        public Task<MarketQuote> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<HistoricalBar>> GetHistoricalPricesAsync(string symbol, DateOnly start, DateOnly end,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DateOnly>> GetOptionExpirationsAsync(string symbol, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<OptionChain> GetOptionChainAsync(string symbol, DateOnly expiration, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

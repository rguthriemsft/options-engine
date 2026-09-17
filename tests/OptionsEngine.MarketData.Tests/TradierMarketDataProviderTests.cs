using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using OptionsEngine.MarketData;
using OptionsEngine.MarketData.Tradier;

namespace OptionsEngine.MarketData.Tests;

public sealed class TradierMarketDataProviderTests
{
    [Fact]
    public async Task QuoteUsesProductionShapeAuthenticationAndNormalizesFields()
    {
        var handler = new StubHandler(_ => Json("{\"quotes\":{\"quote\":{\"symbol\":\"MSFT\",\"last\":500.25,\"bid\":500.20,\"ask\":500.30,\"volume\":123,\"trade_date\":1760000000000}}}"));
        var provider = Create(handler);
        var quote = await provider.GetQuoteAsync(" msft ");
        Assert.Equal("MSFT", quote.Symbol); Assert.Equal(500.25m, quote.Last); Assert.Null(quote.Open); Assert.Equal("Bearer", handler.Authorization!.Scheme); Assert.Equal("token", handler.Authorization.Parameter); Assert.Contains("markets/quotes?symbols=MSFT&greeks=false", handler.PathAndQuery);
    }
    [Fact]
    public async Task ChainPreservesOccGreeksAndMissingValues()
    {
        var provider = Create(new StubHandler(_ => Json("{\"options\":{\"option\":[{\"symbol\":\"MSFT260117C00500000\",\"underlying\":\"MSFT\",\"expiration_date\":\"2026-01-17\",\"strike\":500,\"option_type\":\"call\",\"bid\":2.1,\"greeks\":{\"delta\":0.2,\"gamma\":0.1,\"theta\":-0.02,\"vega\":0.3,\"smv_vol\":0.25}},{\"symbol\":\"MSFT260117P00500000\",\"expiration_date\":\"2026-01-17\",\"strike\":500,\"option_type\":\"put\"}]}}")));
        var chain = await provider.GetOptionChainAsync("MSFT", new DateOnly(2026, 1, 17));
        Assert.Equal(2, chain.Contracts.Count); Assert.Equal("MSFT260117C00500000", chain.Contracts[0].OptionSymbol); Assert.Equal(0.2, chain.Contracts[0].Delta); Assert.Equal(0.25, chain.Contracts[0].ImpliedVolatility); Assert.Null(chain.Contracts[1].Delta); Assert.Null(chain.Contracts[1].Volume);
    }
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, MarketDataFailureKind.AuthenticationFailure)]
    [InlineData(HttpStatusCode.TooManyRequests, MarketDataFailureKind.RateLimited)]
    [InlineData(HttpStatusCode.BadRequest, MarketDataFailureKind.InvalidRequest)]
    public async Task HttpFailuresAreTranslatedWithoutRetries(HttpStatusCode status, MarketDataFailureKind expected)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(status)); var provider = Create(handler);
        var exception = await Assert.ThrowsAsync<MarketDataException>(() => provider.GetQuoteAsync("MSFT"));
        Assert.Equal(expected, exception.Kind); Assert.Equal(1, handler.Calls);
    }
    [Fact]
    public async Task ExpirationsAreSortedAndDeduplicated()
    {
        var provider = Create(new StubHandler(_ => Json("{\"expirations\":{\"date\":[\"2026-02-20\",\"2026-01-17\",\"2026-01-17\"]}}")));
        var dates = await provider.GetOptionExpirationsAsync("MSFT"); Assert.Equal([new DateOnly(2026, 1, 17), new DateOnly(2026, 2, 20)], dates);
    }
    [Fact]
    public async Task HistoricalRequestNormalizesDailyOhlcv()
    {
        var handler = new StubHandler(_ => Json("{\"history\":{\"day\":[{\"date\":\"2026-01-02\",\"open\":1.1,\"high\":2.2,\"low\":1.0,\"close\":2.0,\"volume\":42}]}}")); var provider = Create(handler);
        var bars = await provider.GetHistoricalPricesAsync("msft", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 3));
        Assert.Single(bars); Assert.Equal(2.2m, bars[0].High); Assert.Equal(42, bars[0].Volume); Assert.Contains("markets/history?symbol=MSFT&interval=daily&start=2026-01-01&end=2026-01-03", handler.PathAndQuery);
    }
    [Fact]
    public async Task IncompleteHistoricalBarIsMalformed()
    {
        var exception = await Assert.ThrowsAsync<MarketDataException>(() => Create(new StubHandler(_ => Json("{\"history\":{\"day\":{\"date\":\"2026-01-02\"}}}"))).GetHistoricalPricesAsync("MSFT", Start, End)); Assert.Equal(MarketDataFailureKind.MalformedProviderResponse, exception.Kind);
    }
    [Fact]
    public async Task NotFoundTranslatesToInvalidSymbol()
    {
        var exception = await Assert.ThrowsAsync<MarketDataException>(() => Create(new StubHandler(_ => new(HttpStatusCode.NotFound))).GetQuoteAsync("BAD")); Assert.Equal(MarketDataFailureKind.InvalidSymbol, exception.Kind);
    }
    [Fact]
    public async Task ServerFailureRetriesOnceThenSucceeds()
    {
        var calls = 0; var handler = new StubHandler(_ => ++calls == 1 ? new(HttpStatusCode.ServiceUnavailable) : Json("{\"quotes\":{\"quote\":{\"symbol\":\"MSFT\"}}}")); var quote = await Create(handler).GetQuoteAsync("MSFT"); Assert.Equal("MSFT", quote.Symbol); Assert.Equal(2, handler.Calls);
    }
    [Fact]
    public async Task MalformedJsonIsTranslated()
    {
        var exception = await Assert.ThrowsAsync<MarketDataException>(() => Create(new StubHandler(_ => Json("{"))).GetQuoteAsync("MSFT")); Assert.Equal(MarketDataFailureKind.MalformedProviderResponse, exception.Kind);
    }
    [Fact]
    public async Task NetworkFailureRetriesThenSucceeds()
    {
        var calls = 0; var handler = new StubHandler(_ => { if (++calls == 1) throw new HttpRequestException("transient"); return Json("{\"quotes\":{\"quote\":{\"symbol\":\"MSFT\"}}}"); });
        Assert.Equal("MSFT", (await Create(handler).GetQuoteAsync("MSFT")).Symbol); Assert.Equal(2, handler.Calls);
    }
    [Fact]
    public async Task CancellationPropagates()
    {
        using var source = new CancellationTokenSource(); source.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(new StubHandler(_ => Json("{}"))).GetQuoteAsync("MSFT", source.Token));
    }
    private static readonly DateOnly Start = new(2026, 1, 1); private static readonly DateOnly End = new(2026, 1, 3);
    [Fact]
    public async Task RateLimitExpiryUsesUnixEpochMilliseconds()
    {
        const long expiryMilliseconds = 1760000000000;
        var handler = new StubHandler(_ => { var response = Json("{\"quotes\":{\"quote\":{\"symbol\":\"MSFT\",\"trade_date\":1760000000000}}}"); response.Headers.Add("X-Ratelimit-Expiry", expiryMilliseconds.ToString()); return response; });
        var provider = Create(handler); await provider.GetQuoteAsync("MSFT");
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(expiryMilliseconds), provider.LastRateLimit!.Expiry);
    }
    private static TradierMarketDataProvider Create(StubHandler handler) => new(new HttpClient(handler) { BaseAddress = new Uri("https://api.tradier.com/v1/") }, new TradierOptions { AccessToken = "token" }, NullLogger<TradierMarketDataProvider>.Instance);
    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") };
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler { public int Calls { get; private set; } public System.Net.Http.Headers.AuthenticationHeaderValue? Authorization { get; private set; } public string PathAndQuery { get; private set; } = ""; protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); Calls++; Authorization = request.Headers.Authorization; PathAndQuery = request.RequestUri!.PathAndQuery.TrimStart('/'); return Task.FromResult(response(request)); } }
}

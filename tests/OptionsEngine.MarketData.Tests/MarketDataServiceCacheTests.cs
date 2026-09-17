using Microsoft.Extensions.Logging.Abstractions;
using OptionsEngine.Application.MarketData;
using OptionsEngine.MarketData;
using OptionsEngine.MarketData.Models;

namespace OptionsEngine.MarketData.Tests;

public sealed class MarketDataServiceCacheTests
{
    private static readonly DateOnly Start = new(2025, 1, 1); private static readonly DateOnly End = new(2025, 1, 31);
    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(-1, 1, true)]
    [InlineData(1, 0, false)]
    [InlineData(0, -1, false)]
    public async Task HistoricalCacheUsesFreshKnownCoverage(int startOffset, int endOffset, bool expectedCacheHit)
    {
        var cache = new Cache { Historical = new([Bar], Start.AddDays(startOffset), End.AddDays(endOffset), DateTimeOffset.UtcNow) }; var provider = new Provider("ExampleProvider"); var service = Service(provider, cache);
        await service.GetHistoricalPricesAsync("MSFT", Start, End);
        Assert.Equal(expectedCacheHit ? 0 : 1, provider.HistoryCalls); Assert.Equal(expectedCacheHit ? 0 : 1, cache.HistoricalSaves); Assert.Equal("ExampleProvider", cache.LastProvider);
    }
    [Fact]
    public async Task StaleCompleteHistoricalCoverageRefreshes()
    {
        var cache = new Cache { Historical = new([Bar], Start, End, DateTimeOffset.UtcNow.AddDays(-2)) }; var provider = new Provider("Alternate");
        await Service(provider, cache).GetHistoricalPricesAsync("MSFT", Start, End);
        Assert.Equal(1, provider.HistoryCalls); Assert.Equal(1, cache.HistoricalSaves); Assert.Equal(Start, cache.LastStart); Assert.Equal(End, cache.LastEnd);
    }
    [Fact]
    public async Task QuoteMissCallsProviderAndPersistsWithoutFabrication()
    {
        var cache = new Cache(); var provider = new Provider("Other"); var result = await Service(provider, cache).GetQuoteAsync("MSFT"); Assert.Equal(1, provider.QuoteCalls); Assert.Equal(1, cache.QuoteSaves); Assert.Equal("Other", result.Provider); Assert.Equal("Other", cache.LastProvider);
    }
    private static readonly HistoricalBar Bar = new("MSFT", Start, 1m, 2m, 1m, 2m, 1, "ExampleProvider");
    private static MarketDataService Service(Provider provider, Cache cache) => new(provider, cache, new MarketDataCacheOptions { HistoricalBarsFreshness = TimeSpan.FromHours(1) }, NullLogger<MarketDataService>.Instance);
    private sealed class Provider(string name) : IMarketDataProvider
    { public string ProviderName => name; public int HistoryCalls; public int QuoteCalls; public Task<MarketQuote> GetQuoteAsync(string s, CancellationToken c = default) { QuoteCalls++; return Task.FromResult(new MarketQuote(s, DateTimeOffset.UtcNow, 1m, null, null, null, null, null, null, null, name, null)); } public Task<IReadOnlyList<HistoricalBar>> GetHistoricalPricesAsync(string s, DateOnly a, DateOnly b, CancellationToken c = default) { HistoryCalls++; return Task.FromResult<IReadOnlyList<HistoricalBar>>([new(s, a, 1m, 2m, 1m, 2m, 1, name)]); } public Task<IReadOnlyList<DateOnly>> GetOptionExpirationsAsync(string s, CancellationToken c = default) => Task.FromResult<IReadOnlyList<DateOnly>>([]); public Task<OptionChain> GetOptionChainAsync(string s, DateOnly e, CancellationToken c = default) => Task.FromResult(new OptionChain(s,e,DateTimeOffset.UtcNow,[],name)); }
    private sealed class Cache : IMarketDataCache
    { public HistoricalBarsCacheEntry? Historical; public int HistoricalSaves; public int QuoteSaves; public string? LastProvider; public DateOnly LastStart; public DateOnly LastEnd; public Task<MarketQuote?> GetLatestQuoteAsync(string s,string p,CancellationToken c=default)=>Task.FromResult<MarketQuote?>(null); public Task SaveQuoteAsync(MarketQuote q,CancellationToken c=default){QuoteSaves++;LastProvider=q.Provider;return Task.CompletedTask;} public Task<HistoricalBarsCacheEntry?> GetHistoricalBarsAsync(string s,DateOnly a,DateOnly b,string p,CancellationToken c=default){LastProvider=p;return Task.FromResult(Historical);} public Task UpsertHistoricalBarsAsync(IReadOnlyList<HistoricalBar> b,DateOnly a,DateOnly e,DateTimeOffset r,CancellationToken c=default){HistoricalSaves++;LastStart=a;LastEnd=e;return Task.CompletedTask;} public Task<MarketDataCacheEntry<IReadOnlyList<DateOnly>>?> GetExpirationsAsync(string s,string p,CancellationToken c=default)=>Task.FromResult<MarketDataCacheEntry<IReadOnlyList<DateOnly>>?>(null); public Task SaveExpirationsAsync(string s,string p,DateTimeOffset r,IReadOnlyList<DateOnly> d,CancellationToken c=default)=>Task.CompletedTask; public Task<OptionChain?> GetLatestOptionChainAsync(string s,DateOnly e,string p,CancellationToken c=default)=>Task.FromResult<OptionChain?>(null); public Task SaveOptionChainAsync(OptionChain c,CancellationToken t=default)=>Task.CompletedTask; }
}

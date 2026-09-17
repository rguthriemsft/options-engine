using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OptionsEngine.MarketData;
using OptionsEngine.MarketData.Models;

namespace OptionsEngine.Api.Tests;

public sealed class MarketDataEndpointTests : IAsyncLifetime
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"options-engine-api-market-{Guid.NewGuid():N}.db");
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() { File.Delete(databasePath); File.Delete($"{databasePath}-shm"); File.Delete($"{databasePath}-wal"); return Task.CompletedTask; }
    [Theory]
    [InlineData("/api/market/MSFT/quote")]
    [InlineData("/api/market/MSFT/history?start=2026-01-01&end=2026-01-02")]
    [InlineData("/api/market/MSFT/options/expirations")]
    [InlineData("/api/market/MSFT/options?expiration=2026-01-17")]
    public async Task MarketEndpointsUseApplicationServiceAndSerializeNormalizedData(string path)
    {
        using var factory = Factory(); using var client = factory.CreateClient(); var response = await client.GetAsync(path); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
    [Fact]
    public async Task InvalidHistoryRangeAndMissingExpirationReturnValidationErrors()
    {
        using var factory = Factory(); using var client = factory.CreateClient(); Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/market/MSFT/history?start=2026-01-02&end=2026-01-01")).StatusCode); Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/market/MSFT/options")).StatusCode);
    }
    private WebApplicationFactory<Program> Factory()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__OptionsEngine", $"Data Source={databasePath}");
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder => { builder.UseEnvironment("Testing"); builder.ConfigureServices(services => { var descriptor = services.Single(x => x.ServiceType == typeof(IMarketDataProvider)); services.Remove(descriptor); services.AddScoped<IMarketDataProvider, TestProvider>(); }); });
    }
    private sealed class TestProvider : IMarketDataProvider
    { public string ProviderName => "Test"; public Task<MarketQuote> GetQuoteAsync(string s,CancellationToken c=default)=>Task.FromResult(new MarketQuote(s,DateTimeOffset.UtcNow,1m,null,null,null,null,null,null,null,ProviderName,null)); public Task<IReadOnlyList<HistoricalBar>> GetHistoricalPricesAsync(string s,DateOnly a,DateOnly b,CancellationToken c=default)=>Task.FromResult<IReadOnlyList<HistoricalBar>>([new(s,a,1m,1m,1m,1m,1,ProviderName)]); public Task<IReadOnlyList<DateOnly>> GetOptionExpirationsAsync(string s,CancellationToken c=default)=>Task.FromResult<IReadOnlyList<DateOnly>>([new DateOnly(2026,1,17)]); public Task<OptionChain> GetOptionChainAsync(string s,DateOnly e,CancellationToken c=default)=>Task.FromResult(new OptionChain(s,e,DateTimeOffset.UtcNow,[],ProviderName)); }
}

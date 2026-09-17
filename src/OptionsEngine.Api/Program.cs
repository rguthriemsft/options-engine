using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using OptionsEngine.Api.Health;
using OptionsEngine.Infrastructure.Persistence;
using OptionsEngine.Application.MarketData;
using OptionsEngine.MarketData;
using OptionsEngine.MarketData.Tradier;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("OptionsEngine")
    ?? throw new InvalidOperationException("Connection string 'OptionsEngine' is required.");

builder.Services.AddDbContext<OptionsEngineDbContext>(options => options.UseSqlite(connectionString));
var tradierOptions = builder.Configuration.GetSection(TradierOptions.SectionName).Get<TradierOptions>() ?? new TradierOptions();
if (!Uri.TryCreate(tradierOptions.BaseUrl, UriKind.Absolute, out var tradierBaseUrl) || tradierBaseUrl.Host != "api.tradier.com")
    throw new InvalidOperationException("Tradier:BaseUrl must be the Tradier production API URL.");
builder.Services.AddSingleton(tradierOptions);
builder.Services.Configure<MarketDataCacheOptions>(builder.Configuration.GetSection("MarketDataCache"));
var cacheOptions = builder.Configuration.GetSection("MarketDataCache").Get<MarketDataCacheOptions>() ?? new MarketDataCacheOptions();
builder.Services.AddSingleton(cacheOptions);
builder.Services.AddHttpClient<TradierMarketDataProvider>(client => client.BaseAddress = tradierBaseUrl);
builder.Services.AddScoped<IMarketDataProvider>(sp => sp.GetRequiredService<TradierMarketDataProvider>());
builder.Services.AddScoped<IMarketDataCache, SqliteMarketDataCache>();
builder.Services.AddScoped<MarketDataService>();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("OptionsEngine.Api.Startup");
    var database = scope.ServiceProvider.GetRequiredService<OptionsEngineDbContext>();
    logger.LogInformation("Applying database migrations during application startup");
    await database.Database.MigrateAsync();
}

app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = HealthResponseWriter.WriteAsync });
var market = app.MapGroup("/api/market").AddEndpointFilter(async (context, next) =>
{
    try { return await next(context); }
    catch (MarketDataException ex) { return Results.Problem(ex.Message, statusCode: ex.Kind switch { MarketDataFailureKind.InvalidSymbol or MarketDataFailureKind.InvalidRequest => StatusCodes.Status400BadRequest, MarketDataFailureKind.AuthenticationFailure => StatusCodes.Status502BadGateway, MarketDataFailureKind.RateLimited => StatusCodes.Status429TooManyRequests, MarketDataFailureKind.ProviderUnavailable => StatusCodes.Status503ServiceUnavailable, _ => StatusCodes.Status502BadGateway }, title: ex.Kind.ToString()); }
});
market.MapGet("/{symbol}/quote", async (string symbol, MarketDataService service, CancellationToken ct) => Results.Ok(await service.GetQuoteAsync(symbol, ct)));
market.MapGet("/{symbol}/history", async (string symbol, DateOnly start, DateOnly end, MarketDataService service, CancellationToken ct) => Results.Ok(await service.GetHistoricalPricesAsync(symbol, start, end, ct)));
market.MapGet("/{symbol}/options/expirations", async (string symbol, MarketDataService service, CancellationToken ct) => Results.Ok(await service.GetOptionExpirationsAsync(symbol, ct)));
market.MapGet("/{symbol}/options", async (string symbol, DateOnly? expiration, MarketDataService service, CancellationToken ct) => expiration is null ? Results.ValidationProblem(new Dictionary<string, string[]> { ["expiration"] = ["An expiration date is required."] }) : Results.Ok(await service.GetOptionChainAsync(symbol, expiration.Value, ct)));
app.Run();

public partial class Program;

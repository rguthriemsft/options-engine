using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OptionsEngine.MarketData.Models;

namespace OptionsEngine.MarketData.Tradier;

/// <summary>Maps Tradier production REST payloads into provider-independent market-data records.</summary>
public sealed class TradierMarketDataProvider(HttpClient httpClient, TradierOptions options, ILogger<TradierMarketDataProvider> logger) : IMarketDataProvider
{
    public const string ProviderName = "Tradier";
    private readonly HttpClient httpClient = httpClient;
    private readonly TradierOptions options = options;
    private readonly ILogger<TradierMarketDataProvider> logger = logger;
    public RateLimitMetadata? LastRateLimit { get; private set; }

    public async Task<MarketQuote> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeSymbol(symbol);
        using var document = await GetJsonAsync($"markets/quotes?symbols={Uri.EscapeDataString(normalized)}&greeks=false", normalized, cancellationToken);
        var quotes = document.RootElement.GetProperty("quotes");
        if (!quotes.TryGetProperty("quote", out var quoteElement) || quoteElement.ValueKind == JsonValueKind.Null)
            throw Missing("Tradier returned no quote data.");
        var quote = quoteElement.ValueKind == JsonValueKind.Array ? quoteElement.EnumerateArray().FirstOrDefault() : quoteElement;
        if (quote.ValueKind != JsonValueKind.Object) throw Malformed("Tradier quote response has an invalid quote shape.");
        var resultSymbol = RequiredString(quote, "symbol");
        return new MarketQuote(resultSymbol, Timestamp(quote, "trade_date"), Decimal(quote, "last"), Decimal(quote, "bid"), Decimal(quote, "ask"), Decimal(quote, "open"), Decimal(quote, "high"), Decimal(quote, "low"), Decimal(quote, "prevclose"), Int64(quote, "volume"), ProviderName, Bool(quote, "delayed"));
    }

    public async Task<IReadOnlyList<HistoricalBar>> GetHistoricalPricesAsync(string symbol, DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
    {
        if (start > end) throw new MarketDataException(MarketDataFailureKind.InvalidRequest, "Start date must be on or before end date.");
        var normalized = NormalizeSymbol(symbol);
        using var document = await GetJsonAsync($"markets/history?symbol={Uri.EscapeDataString(normalized)}&interval=daily&start={start:yyyy-MM-dd}&end={end:yyyy-MM-dd}", normalized, cancellationToken);
        if (!document.RootElement.TryGetProperty("history", out var history) || !history.TryGetProperty("day", out var days) || days.ValueKind == JsonValueKind.Null) return [];
        IEnumerable<JsonElement> sequence = days.ValueKind == JsonValueKind.Array ? days.EnumerateArray() : [days];
        var result = new List<HistoricalBar>();
        foreach (var day in sequence)
        {
            try { result.Add(new HistoricalBar(normalized, DateOnly.Parse(RequiredString(day, "date"), CultureInfo.InvariantCulture), RequiredDecimal(day, "open"), RequiredDecimal(day, "high"), RequiredDecimal(day, "low"), RequiredDecimal(day, "close"), Int64(day, "volume"), ProviderName)); }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException) { throw Malformed("Tradier returned an incomplete historical daily bar.", ex); }
        }
        return result.OrderBy(x => x.Date).ToArray();
    }

    public async Task<IReadOnlyList<DateOnly>> GetOptionExpirationsAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeSymbol(symbol);
        using var document = await GetJsonAsync($"markets/options/expirations?symbol={Uri.EscapeDataString(normalized)}&includeAllRoots=false&strikes=false", normalized, cancellationToken);
        if (!document.RootElement.TryGetProperty("expirations", out var expirations) || !expirations.TryGetProperty("date", out var dates) || dates.ValueKind == JsonValueKind.Null) return [];
        IEnumerable<JsonElement> raw = dates.ValueKind == JsonValueKind.Array ? dates.EnumerateArray() : [dates];
        try { return raw.Select(x => DateOnly.Parse(x.GetString()!, CultureInfo.InvariantCulture)).Distinct().Order().ToArray(); }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException) { throw Malformed("Tradier returned an invalid expiration date.", ex); }
    }

    public async Task<OptionChain> GetOptionChainAsync(string symbol, DateOnly expiration, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeSymbol(symbol);
        using var document = await GetJsonAsync($"markets/options/chains?symbol={Uri.EscapeDataString(normalized)}&expiration={expiration:yyyy-MM-dd}&greeks=true", normalized, cancellationToken);
        if (!document.RootElement.TryGetProperty("options", out var optionsElement) || !optionsElement.TryGetProperty("option", out var optionsArray) || optionsArray.ValueKind == JsonValueKind.Null)
            return new OptionChain(normalized, expiration, DateTimeOffset.UtcNow, [], ProviderName);
        IEnumerable<JsonElement> raw = optionsArray.ValueKind == JsonValueKind.Array ? optionsArray.EnumerateArray() : [optionsArray];
        var timestamp = DateTimeOffset.UtcNow;
        var contracts = raw.Select(x => MapContract(x, normalized, timestamp)).ToArray();
        return new OptionChain(normalized, expiration, timestamp, contracts, ProviderName);
    }

    private async Task<JsonDocument> GetJsonAsync(string path, string symbol, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.AccessToken)) throw new MarketDataException(MarketDataFailureKind.ConfigurationFailure, "Tradier access token is required before market data can be requested.");
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.AccessToken);
            try
            {
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                LastRateLimit = ParseRateLimit(response);
                if ((int)response.StatusCode >= 500 && attempt < options.TransientRetryCount) { await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken); continue; }
                if (!response.IsSuccessStatusCode) throw Translate(response.StatusCode);
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            }
            catch (HttpRequestException) when (attempt < options.TransientRetryCount)
            { logger.LogWarning("Transient {Provider} request failure for {Symbol}; retrying once.", ProviderName, symbol); await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken); }
            catch (JsonException ex) { throw Malformed("Tradier returned malformed JSON.", ex); }
        }
    }

    private OptionContractSnapshot MapContract(JsonElement item, string underlying, DateTimeOffset timestamp)
    {
        try
        {
            var type = RequiredString(item, "option_type") switch { "call" => OptionType.Call, "put" => OptionType.Put, _ => throw new InvalidOperationException("Unknown option type.") };
            var greeks = item.TryGetProperty("greeks", out var greekElement) && greekElement.ValueKind == JsonValueKind.Object ? greekElement : default;
            return new OptionContractSnapshot(RequiredString(item, "symbol"), String(item, "underlying") ?? underlying, timestamp, DateOnly.Parse(RequiredString(item, "expiration_date"), CultureInfo.InvariantCulture), RequiredDecimal(item, "strike"), type, Decimal(item, "bid"), Decimal(item, "ask"), Decimal(item, "last"), Int64(item, "volume"), Int64(item, "open_interest"), Double(greeks, "smv_vol") ?? Double(greeks, "mid_iv"), Double(greeks, "delta"), Double(greeks, "gamma"), Double(greeks, "theta"), Double(greeks, "vega"), Decimal(item, "underlying_price"), ProviderName);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException) { throw Malformed("Tradier returned an invalid option contract.", ex); }
    }
    private static MarketDataException Translate(HttpStatusCode code) => code switch { HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new(MarketDataFailureKind.AuthenticationFailure, "Tradier authentication failed.", (int)code), HttpStatusCode.TooManyRequests => new(MarketDataFailureKind.RateLimited, "Tradier rate limit was exceeded.", 429), HttpStatusCode.BadRequest => new(MarketDataFailureKind.InvalidRequest, "Tradier rejected the market-data request.", 400), HttpStatusCode.NotFound => new(MarketDataFailureKind.InvalidSymbol, "Tradier did not find the requested symbol.", 404), _ => new(MarketDataFailureKind.ProviderUnavailable, "Tradier market data is unavailable.", (int)code) };
    private static RateLimitMetadata ParseRateLimit(HttpResponseMessage response) => new(Int(response, "X-Ratelimit-Allowed"), Int(response, "X-Ratelimit-Used"), Int(response, "X-Ratelimit-Available"), DateTimeOffset.TryParse(Header(response, "X-Ratelimit-Expiry"), out var expiry) ? expiry : null);
    private static string? Header(HttpResponseMessage r, string n) => r.Headers.TryGetValues(n, out var v) ? v.FirstOrDefault() : null; private static int? Int(HttpResponseMessage r, string n) => int.TryParse(Header(r, n), out var v) ? v : null;
    private static string NormalizeSymbol(string symbol) { var value = symbol.Trim().ToUpperInvariant(); if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace)) throw new MarketDataException(MarketDataFailureKind.InvalidSymbol, "A non-empty market symbol is required."); return value; }
    private static string RequiredString(JsonElement e, string n) => String(e, n) ?? throw new InvalidOperationException($"Required field '{n}' is missing."); private static string? String(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var p) && p.ValueKind != JsonValueKind.Null ? p.GetString() : null;
    private static decimal RequiredDecimal(JsonElement e, string n) => Decimal(e, n) ?? throw new InvalidOperationException($"Required field '{n}' is missing."); private static decimal? Decimal(JsonElement e, string n) => Number<decimal>(e, n, decimal.TryParse); private static double? Double(JsonElement e, string n) => Number<double>(e, n, double.TryParse); private static long? Int64(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var p) && p.ValueKind != JsonValueKind.Null && p.TryGetInt64(out var v) ? v : null; private static bool? Bool(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False ? p.GetBoolean() : null;
    private delegate bool Parser<T>(string? text, NumberStyles styles, IFormatProvider provider, out T value); private static T? Number<T>(JsonElement e, string n, Parser<T> parser) where T : struct => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var p) && p.ValueKind != JsonValueKind.Null && parser(p.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    private static DateTimeOffset Timestamp(JsonElement e, string n) { if (e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var ms)) return DateTimeOffset.FromUnixTimeMilliseconds(ms); return DateTimeOffset.UtcNow; }
    private static MarketDataException Missing(string message) => new(MarketDataFailureKind.MissingMarketData, message); private static MarketDataException Malformed(string message, Exception? ex = null) => new(MarketDataFailureKind.MalformedProviderResponse, message, null, ex);
}

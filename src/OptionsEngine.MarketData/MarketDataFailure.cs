namespace OptionsEngine.MarketData;
public enum MarketDataFailureKind { AuthenticationFailure, RateLimited, InvalidSymbol, InvalidRequest, ProviderUnavailable, MalformedProviderResponse, MissingMarketData, ConfigurationFailure }
public sealed class MarketDataException(MarketDataFailureKind kind, string message, int? statusCode = null, Exception? innerException = null) : Exception(message, innerException)
{ public MarketDataFailureKind Kind { get; } = kind; public int? StatusCode { get; } = statusCode; }
public sealed record RateLimitMetadata(int? Allowed, int? Used, int? Available, DateTimeOffset? Expiry);

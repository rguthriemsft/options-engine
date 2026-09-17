namespace OptionsEngine.MarketData.Models;

public enum OptionType { Call, Put }

public sealed record MarketQuote(string Symbol, DateTimeOffset Timestamp, decimal? Last, decimal? Bid, decimal? Ask, decimal? Open, decimal? High, decimal? Low, decimal? PreviousClose, long? Volume, string Provider, bool? IsDelayed);
public sealed record HistoricalBar(string Symbol, DateOnly Date, decimal Open, decimal High, decimal Low, decimal Close, long? Volume, string Provider);
public sealed record OptionContractSnapshot(string OptionSymbol, string UnderlyingSymbol, DateTimeOffset Timestamp, DateOnly Expiration, decimal Strike, OptionType OptionType, decimal? Bid, decimal? Ask, decimal? Last, long? Volume, long? OpenInterest, double? ImpliedVolatility, double? Delta, double? Gamma, double? Theta, double? Vega, decimal? UnderlyingPrice, string Provider);
public sealed record OptionChain(string UnderlyingSymbol, DateOnly Expiration, DateTimeOffset Timestamp, IReadOnlyList<OptionContractSnapshot> Contracts, string Provider);

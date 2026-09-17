namespace OptionsEngine.MarketData;
public sealed class MarketDataCacheOptions { public TimeSpan QuoteFreshness { get; set; } = TimeSpan.FromMinutes(1); public TimeSpan HistoricalBarsFreshness { get; set; } = TimeSpan.FromHours(24); public TimeSpan OptionExpirationsFreshness { get; set; } = TimeSpan.FromHours(24); public TimeSpan OptionChainFreshness { get; set; } = TimeSpan.FromMinutes(5); }

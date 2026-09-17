using OptionsEngine.MarketData.Models;

namespace OptionsEngine.Infrastructure.Persistence;

public sealed class MarketQuoteSnapshotEntity
{
    public long MarketQuoteSnapshotId { get; set; }
    public required string Symbol { get; set; } public required string Provider { get; set; } public DateTimeOffset Timestamp { get; set; } public long TimestampUtcTicks { get; set; }
    public decimal? Last { get; set; } public decimal? Bid { get; set; } public decimal? Ask { get; set; } public decimal? Open { get; set; } public decimal? High { get; set; } public decimal? Low { get; set; } public decimal? PreviousClose { get; set; } public long? Volume { get; set; } public bool? IsDelayed { get; set; }
}
public sealed class HistoricalPriceBarEntity
{
    public long HistoricalPriceBarId { get; set; }
    public required string Symbol { get; set; } public required string Provider { get; set; } public DateOnly Date { get; set; } public decimal Open { get; set; } public decimal High { get; set; } public decimal Low { get; set; } public decimal Close { get; set; } public long? Volume { get; set; } public DateTimeOffset RetrievedAt { get; set; }
}
public sealed class OptionContractSnapshotEntity
{
    public long OptionContractSnapshotId { get; set; }
    public required string OptionSymbol { get; set; } public required string UnderlyingSymbol { get; set; } public required string Provider { get; set; } public DateTimeOffset Timestamp { get; set; } public long TimestampUtcTicks { get; set; } public DateOnly Expiration { get; set; } public decimal Strike { get; set; } public OptionType OptionType { get; set; }
    public decimal? Bid { get; set; } public decimal? Ask { get; set; } public decimal? Last { get; set; } public long? Volume { get; set; } public long? OpenInterest { get; set; } public double? ImpliedVolatility { get; set; } public double? Delta { get; set; } public double? Gamma { get; set; } public double? Theta { get; set; } public double? Vega { get; set; } public decimal? UnderlyingPrice { get; set; }
}
public sealed class OptionExpirationCacheEntity { public long OptionExpirationCacheId { get; set; } public required string Symbol { get; set; } public required string Provider { get; set; } public required string ExpirationsJson { get; set; } public DateTimeOffset RetrievedAt { get; set; } }
public sealed class HistoricalPriceCoverageEntity { public long HistoricalPriceCoverageId { get; set; } public required string Symbol { get; set; } public required string Provider { get; set; } public DateOnly StartDate { get; set; } public DateOnly EndDate { get; set; } public DateTimeOffset RetrievedAt { get; set; } }

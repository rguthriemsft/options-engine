using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Api.Indicators;

public sealed record IndicatorValueResponse<T>(T? Value, string Status) where T : struct
{
    public static IndicatorValueResponse<T> From(IndicatorValue<T> source) => new(source.Value, source.Status.ToString());
}

public sealed record IndicatorSnapshotResponse
{
    public required string Symbol { get; init; }
    public DateOnly AsOfDate { get; init; }
    public required string IndicatorCalculationVersion { get; init; }
    public int ConfigurationVersion { get; init; }
    public DateTimeOffset CalculatedAt { get; init; }
    public required IndicatorValueResponse<decimal> Sma20 { get; init; }
    public required IndicatorValueResponse<decimal> Sma50 { get; init; }
    public required IndicatorValueResponse<decimal> Sma200 { get; init; }
    public required IndicatorValueResponse<double> Rsi14 { get; init; }
    public required IndicatorValueResponse<double> BollingerMiddle { get; init; }
    public required IndicatorValueResponse<double> BollingerUpper { get; init; }
    public required IndicatorValueResponse<double> BollingerLower { get; init; }
    public required IndicatorValueResponse<double> BollingerPercentB { get; init; }
    public required IndicatorValueResponse<double> BollingerBandwidth { get; init; }
    public required IndicatorValueResponse<double> Macd { get; init; }
    public required IndicatorValueResponse<double> MacdSignal { get; init; }
    public required IndicatorValueResponse<double> MacdHistogram { get; init; }
    public required IndicatorValueResponse<double> Atr14 { get; init; }
    public required IndicatorValueResponse<double> AtrPercent { get; init; }
    public required IndicatorValueResponse<double> RealizedVolatility20 { get; init; }
    public required IndicatorValueResponse<double> RealizedVolatility30 { get; init; }
    public required IndicatorValueResponse<double> Iv30 { get; init; }
    public required IndicatorValueResponse<double> IvRank { get; init; }
    public required IndicatorValueResponse<double> IvPercentile { get; init; }
    public string? Iv30UnavailableReason { get; init; }
    public required IndicatorValueResponse<decimal> ResistancePrice { get; init; }
    public required IndicatorValueResponse<decimal> DistanceToResistance { get; init; }
    public required IndicatorValueResponse<double> DistanceToResistancePercent { get; init; }
    public required IndicatorValueResponse<int> ResistanceTouchCount { get; init; }
    public required IndicatorValueResponse<DateOnly> ResistanceLastTouchDate { get; init; }
    public required IndicatorValueResponse<int> ResistanceAgeTradingDays { get; init; }
    public required string MarketRegime { get; init; }
    public required string SectorRegime { get; init; }

    public static IndicatorSnapshotResponse From(IndicatorSnapshot snapshot) => new()
    {
        Symbol = snapshot.Symbol,
        AsOfDate = snapshot.AsOfDate,
        IndicatorCalculationVersion = snapshot.IndicatorCalculationVersion.Value,
        ConfigurationVersion = snapshot.ConfigurationVersion.Value,
        CalculatedAt = snapshot.CalculatedAt,
        Sma20 = IndicatorValueResponse<decimal>.From(snapshot.Sma20),
        Sma50 = IndicatorValueResponse<decimal>.From(snapshot.Sma50),
        Sma200 = IndicatorValueResponse<decimal>.From(snapshot.Sma200),
        Rsi14 = IndicatorValueResponse<double>.From(snapshot.Rsi14),
        BollingerMiddle = IndicatorValueResponse<double>.From(snapshot.BollingerMiddle),
        BollingerUpper = IndicatorValueResponse<double>.From(snapshot.BollingerUpper),
        BollingerLower = IndicatorValueResponse<double>.From(snapshot.BollingerLower),
        BollingerPercentB = IndicatorValueResponse<double>.From(snapshot.BollingerPercentB),
        BollingerBandwidth = IndicatorValueResponse<double>.From(snapshot.BollingerBandwidth),
        Macd = IndicatorValueResponse<double>.From(snapshot.Macd),
        MacdSignal = IndicatorValueResponse<double>.From(snapshot.MacdSignal),
        MacdHistogram = IndicatorValueResponse<double>.From(snapshot.MacdHistogram),
        Atr14 = IndicatorValueResponse<double>.From(snapshot.Atr14),
        AtrPercent = IndicatorValueResponse<double>.From(snapshot.AtrPercent),
        RealizedVolatility20 = IndicatorValueResponse<double>.From(snapshot.RealizedVolatility20),
        RealizedVolatility30 = IndicatorValueResponse<double>.From(snapshot.RealizedVolatility30),
        Iv30 = IndicatorValueResponse<double>.From(snapshot.Iv30),
        IvRank = IndicatorValueResponse<double>.From(snapshot.IvRank),
        IvPercentile = IndicatorValueResponse<double>.From(snapshot.IvPercentile),
        Iv30UnavailableReason = snapshot.Iv30UnavailableReason?.ToString(),
        ResistancePrice = IndicatorValueResponse<decimal>.From(snapshot.ResistancePrice),
        DistanceToResistance = IndicatorValueResponse<decimal>.From(snapshot.DistanceToResistance),
        DistanceToResistancePercent = IndicatorValueResponse<double>.From(snapshot.DistanceToResistancePercent),
        ResistanceTouchCount = IndicatorValueResponse<int>.From(snapshot.ResistanceTouchCount),
        ResistanceLastTouchDate = IndicatorValueResponse<DateOnly>.From(snapshot.ResistanceLastTouchDate),
        ResistanceAgeTradingDays = IndicatorValueResponse<int>.From(snapshot.ResistanceAgeTradingDays),
        MarketRegime = FormatRegime(snapshot.MarketRegime),
        SectorRegime = FormatRegime(snapshot.SectorRegime)
    };

    private static string FormatRegime(MarketRegime regime) => regime switch
    {
        OptionsEngine.Strategy.Indicators.MarketRegime.InsufficientData => "INSUFFICIENT_DATA",
        _ => regime.ToString().ToUpperInvariant()
    };
}

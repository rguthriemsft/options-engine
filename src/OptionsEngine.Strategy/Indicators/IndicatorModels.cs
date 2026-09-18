namespace OptionsEngine.Strategy.Indicators;

/// <summary>
/// Provider- and persistence-independent daily price observation supplied to an indicator calculation.
/// </summary>
public sealed record IndicatorPriceObservation(
    string Symbol,
    DateOnly TradingDate,
    decimal? Open,
    decimal? High,
    decimal? Low,
    decimal? Close,
    long? Volume);

public enum IndicatorValueStatus
{
    Available,
    InsufficientData
}

/// <summary>
/// A numeric indicator value whose status makes unavailable data distinct from a valid numeric zero.
/// </summary>
public sealed record IndicatorValue<T>(T? Value, IndicatorValueStatus Status) where T : struct
{
    public static IndicatorValue<T> Available(T value) => new(value, IndicatorValueStatus.Available);
    public static IndicatorValue<T> InsufficientData() => new(null, IndicatorValueStatus.InsufficientData);
}

/// <summary>
/// Identifies the mathematical implementation used to derive indicator values.
/// </summary>
public sealed record IndicatorCalculationVersion
{
    public IndicatorCalculationVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("An indicator calculation version is required.", nameof(value));
        Value = value;
    }

    public string Value { get; }
}

/// <summary>
/// Identifies the versioned set of parameter values supplied to an indicator calculation.
/// </summary>
public readonly record struct ConfigurationVersion
{
    public ConfigurationVersion(int value)
    {
        if (value < 1) throw new ArgumentOutOfRangeException(nameof(value), "Configuration version must be positive.");
        Value = value;
    }

    public int Value { get; }
}

public sealed record SimpleMovingAverageConfiguration
{
    public int Sma20Period { get; init; } = 20;
    public int Sma50Period { get; init; } = 50;
    public int Sma200Period { get; init; } = 200;

    internal void Validate()
    {
        if (Sma20Period < 1) throw new ArgumentOutOfRangeException(nameof(Sma20Period), "SMA periods must be positive.");
        if (Sma50Period < 1) throw new ArgumentOutOfRangeException(nameof(Sma50Period), "SMA periods must be positive.");
        if (Sma200Period < 1) throw new ArgumentOutOfRangeException(nameof(Sma200Period), "SMA periods must be positive.");
    }
}

public sealed record RelativeStrengthIndexConfiguration
{
    public int Period { get; init; } = 14;

    internal void Validate()
    {
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period), "RSI period must be positive.");
    }
}

public sealed record BollingerBandsConfiguration
{
    public int Period { get; init; } = 20;
    public double StandardDeviations { get; init; } = 2;

    internal void Validate()
    {
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period), "Bollinger period must be positive.");
        if (!double.IsFinite(StandardDeviations) || StandardDeviations < 0) throw new ArgumentOutOfRangeException(nameof(StandardDeviations), "Bollinger standard deviations must be finite and non-negative.");
    }
}

public sealed record MovingAverageConvergenceDivergenceConfiguration
{
    public int FastPeriod { get; init; } = 12;
    public int SlowPeriod { get; init; } = 26;
    public int SignalPeriod { get; init; } = 9;

    internal void Validate()
    {
        if (FastPeriod < 1) throw new ArgumentOutOfRangeException(nameof(FastPeriod), "MACD fast period must be positive.");
        if (SlowPeriod <= FastPeriod) throw new ArgumentOutOfRangeException(nameof(SlowPeriod), "MACD slow period must exceed the fast period.");
        if (SignalPeriod < 1) throw new ArgumentOutOfRangeException(nameof(SignalPeriod), "MACD signal period must be positive.");
    }
}

public sealed record AverageTrueRangeConfiguration
{
    public int Period { get; init; } = 14;

    internal void Validate()
    {
        if (Period < 1) throw new ArgumentOutOfRangeException(nameof(Period), "ATR period must be positive.");
    }
}

public sealed record RealizedVolatilityConfiguration
{
    public int ShortPeriod { get; init; } = 20;
    public int StandardPeriod { get; init; } = 30;
    public int AnnualizationTradingDays { get; init; } = 252;

    internal void Validate()
    {
        if (ShortPeriod < 2) throw new ArgumentOutOfRangeException(nameof(ShortPeriod), "Realized volatility requires at least two returns for sample standard deviation.");
        if (StandardPeriod < 2) throw new ArgumentOutOfRangeException(nameof(StandardPeriod), "Realized volatility requires at least two returns for sample standard deviation.");
        if (AnnualizationTradingDays < 1) throw new ArgumentOutOfRangeException(nameof(AnnualizationTradingDays), "Annualization trading days must be positive.");
    }
}

public sealed record ImpliedVolatilityConfiguration
{
    public int TargetDteCalendarDays { get; init; } = 30;
    public decimal MaxAtmStrikeDistanceRatio { get; init; } = 0.05m;
    public int HistoricalLookbackValidObservations { get; init; } = 252;
    public int MinimumHistoricalValidObservations { get; init; } = 126;

    internal void Validate()
    {
        if (TargetDteCalendarDays < 1) throw new ArgumentOutOfRangeException(nameof(TargetDteCalendarDays));
        if (MaxAtmStrikeDistanceRatio < 0) throw new ArgumentOutOfRangeException(nameof(MaxAtmStrikeDistanceRatio));
        if (HistoricalLookbackValidObservations < 1) throw new ArgumentOutOfRangeException(nameof(HistoricalLookbackValidObservations));
        if (MinimumHistoricalValidObservations < 1 || MinimumHistoricalValidObservations > HistoricalLookbackValidObservations)
            throw new ArgumentOutOfRangeException(nameof(MinimumHistoricalValidObservations));
    }
}

public sealed record ResistanceConfiguration
{
    public int SwingWindow { get; init; } = 3;
    public int LookbackTradingDays { get; init; } = 120;
    public double ClusterDistanceAtrMultiplier { get; init; } = 0.75;
    public int MinimumResistanceTouches { get; init; } = 2;

    internal void Validate()
    {
        if (SwingWindow < 1) throw new ArgumentOutOfRangeException(nameof(SwingWindow));
        if (LookbackTradingDays < 1) throw new ArgumentOutOfRangeException(nameof(LookbackTradingDays));
        if (!double.IsFinite(ClusterDistanceAtrMultiplier) || ClusterDistanceAtrMultiplier < 0)
            throw new ArgumentOutOfRangeException(nameof(ClusterDistanceAtrMultiplier));
        if (MinimumResistanceTouches < 1) throw new ArgumentOutOfRangeException(nameof(MinimumResistanceTouches));
    }
}

public sealed record RegimeConfiguration
{
    public int FastSmaPeriod { get; init; } = 50;
    public int LongSmaPeriod { get; init; } = 200;
    public int SlopeLookbackTradingDays { get; init; } = 20;
    public string MarketBenchmark { get; init; } = "SPY";

    internal void Validate()
    {
        if (FastSmaPeriod < 1) throw new ArgumentOutOfRangeException(nameof(FastSmaPeriod));
        if (LongSmaPeriod <= FastSmaPeriod) throw new ArgumentOutOfRangeException(nameof(LongSmaPeriod));
        if (SlopeLookbackTradingDays < 1) throw new ArgumentOutOfRangeException(nameof(SlopeLookbackTradingDays));
        if (string.IsNullOrWhiteSpace(MarketBenchmark)) throw new ArgumentException("A market benchmark is required.", nameof(MarketBenchmark));
    }
}

/// <summary>
/// Versioned Phase 3 indicator configuration.
/// </summary>
public sealed record IndicatorConfiguration
{
    public required ConfigurationVersion Version { get; init; }
    public SimpleMovingAverageConfiguration SimpleMovingAverage { get; init; } = new();
    public RelativeStrengthIndexConfiguration RelativeStrengthIndex { get; init; } = new();
    public BollingerBandsConfiguration BollingerBands { get; init; } = new();
    public MovingAverageConvergenceDivergenceConfiguration Macd { get; init; } = new();
    public AverageTrueRangeConfiguration AverageTrueRange { get; init; } = new();
    public RealizedVolatilityConfiguration RealizedVolatility { get; init; } = new();
    public ImpliedVolatilityConfiguration ImpliedVolatility { get; init; } = new();
    public ResistanceConfiguration Resistance { get; init; } = new();
    public RegimeConfiguration Regime { get; init; } = new();

    internal void Validate()
    {
        if (Version.Value < 1) throw new ArgumentOutOfRangeException(nameof(Version), "Configuration version must be positive.");
        SimpleMovingAverage.Validate();
        RelativeStrengthIndex.Validate();
        BollingerBands.Validate();
        Macd.Validate();
        AverageTrueRange.Validate();
        RealizedVolatility.Validate();
        ImpliedVolatility.Validate();
        Resistance.Validate();
        Regime.Validate();
    }
}

public sealed record IndicatorCalculationRequest(
    string Symbol,
    DateOnly AsOfDate,
    IReadOnlyList<IndicatorPriceObservation> Observations,
    IndicatorConfiguration Configuration,
    IndicatorCalculationVersion IndicatorCalculationVersion,
    DateTimeOffset CalculatedAt);

/// <summary>
/// Provider-independent Phase 3 calculated facts as of a date. Uncalculated context remains explicitly unavailable.
/// </summary>
public sealed record IndicatorSnapshot(
    string Symbol,
    DateOnly AsOfDate,
    IndicatorValue<decimal> Sma20,
    IndicatorValue<decimal> Sma50,
    IndicatorValue<decimal> Sma200,
    IndicatorValue<double> Rsi14,
    IndicatorValue<double> BollingerMiddle,
    IndicatorValue<double> BollingerUpper,
    IndicatorValue<double> BollingerLower,
    IndicatorValue<double> BollingerPercentB,
    IndicatorValue<double> BollingerBandwidth,
    IndicatorValue<double> Macd,
    IndicatorValue<double> MacdSignal,
    IndicatorValue<double> MacdHistogram,
    IndicatorValue<double> Atr14,
    IndicatorValue<double> AtrPercent,
    IndicatorValue<double> RealizedVolatility20,
    IndicatorValue<double> RealizedVolatility30,
    IndicatorCalculationVersion IndicatorCalculationVersion,
    ConfigurationVersion ConfigurationVersion,
    DateTimeOffset CalculatedAt)
{
    public IndicatorValue<double> Iv30 { get; init; } = IndicatorValue<double>.InsufficientData();
    public IndicatorValue<double> IvRank { get; init; } = IndicatorValue<double>.InsufficientData();
    public IndicatorValue<double> IvPercentile { get; init; } = IndicatorValue<double>.InsufficientData();
    public Iv30UnavailableReason? Iv30UnavailableReason { get; init; }
    public IndicatorValue<decimal> ResistancePrice { get; init; } = IndicatorValue<decimal>.InsufficientData();
    public IndicatorValue<decimal> DistanceToResistance { get; init; } = IndicatorValue<decimal>.InsufficientData();
    public IndicatorValue<double> DistanceToResistancePercent { get; init; } = IndicatorValue<double>.InsufficientData();
    public IndicatorValue<int> ResistanceTouchCount { get; init; } = IndicatorValue<int>.InsufficientData();
    public IndicatorValue<DateOnly> ResistanceLastTouchDate { get; init; } = IndicatorValue<DateOnly>.InsufficientData();
    public IndicatorValue<int> ResistanceAgeTradingDays { get; init; } = IndicatorValue<int>.InsufficientData();
    public MarketRegime MarketRegime { get; init; } = MarketRegime.InsufficientData;
    public MarketRegime SectorRegime { get; init; } = MarketRegime.InsufficientData;
}

public interface IIndicatorCalculator
{
    IndicatorSnapshot Calculate(IndicatorCalculationRequest request);
}

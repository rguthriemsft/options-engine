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

/// <summary>
/// Phase 3 indicator configuration for the implemented SMA slice. Later indicator settings belong here as their calculations are added.
/// </summary>
public sealed record IndicatorConfiguration
{
    public required ConfigurationVersion Version { get; init; }
    public SimpleMovingAverageConfiguration SimpleMovingAverage { get; init; } = new();

    internal void Validate()
    {
        if (Version.Value < 1) throw new ArgumentOutOfRangeException(nameof(Version), "Configuration version must be positive.");
        SimpleMovingAverage.Validate();
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
/// Provider-independent Phase 3 calculated facts as of a date. Fields not implemented in Phase 3A are deliberately absent.
/// </summary>
public sealed record IndicatorSnapshot(
    string Symbol,
    DateOnly AsOfDate,
    IndicatorValue<decimal> Sma20,
    IndicatorValue<decimal> Sma50,
    IndicatorValue<decimal> Sma200,
    IndicatorCalculationVersion IndicatorCalculationVersion,
    ConfigurationVersion ConfigurationVersion,
    DateTimeOffset CalculatedAt);

public interface IIndicatorCalculator
{
    IndicatorSnapshot Calculate(IndicatorCalculationRequest request);
}

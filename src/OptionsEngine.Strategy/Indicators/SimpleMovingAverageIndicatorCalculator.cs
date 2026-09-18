namespace OptionsEngine.Strategy.Indicators;

/// <summary>
/// Calculates complete-window arithmetic SMAs from normalized daily observations.
/// </summary>
public sealed class SimpleMovingAverageIndicatorCalculator : IIndicatorCalculator
{
    public IndicatorSnapshot Calculate(IndicatorCalculationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Symbol);
        ArgumentNullException.ThrowIfNull(request.Observations);
        ArgumentNullException.ThrowIfNull(request.Configuration);
        ArgumentNullException.ThrowIfNull(request.IndicatorCalculationVersion);
        if (request.CalculatedAt.Offset != TimeSpan.Zero) throw new ArgumentException("CalculatedAt must be expressed in UTC.", nameof(request));
        request.Configuration.Validate();

        var closes = request.Observations
            .Where(x => string.Equals(x.Symbol, request.Symbol, StringComparison.OrdinalIgnoreCase))
            .Where(x => x.TradingDate <= request.AsOfDate && x.Close.HasValue)
            .OrderBy(x => x.TradingDate)
            .Select(x => x.Close!.Value)
            .ToArray();

        return new IndicatorSnapshot(
            request.Symbol,
            request.AsOfDate,
            CalculateSma(closes, request.Configuration.SimpleMovingAverage.Sma20Period),
            CalculateSma(closes, request.Configuration.SimpleMovingAverage.Sma50Period),
            CalculateSma(closes, request.Configuration.SimpleMovingAverage.Sma200Period),
            request.IndicatorCalculationVersion,
            request.Configuration.Version,
            request.CalculatedAt);
    }

    private static IndicatorValue<decimal> CalculateSma(IReadOnlyList<decimal> closes, int period)
    {
        if (closes.Count < period) return IndicatorValue<decimal>.InsufficientData();

        decimal sum = 0;
        for (var index = closes.Count - period; index < closes.Count; index++) sum += closes[index];
        return IndicatorValue<decimal>.Available(sum / period);
    }
}

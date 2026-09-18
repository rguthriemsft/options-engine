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

        var observations = request.Observations
            .Where(x => string.Equals(x.Symbol, request.Symbol, StringComparison.OrdinalIgnoreCase))
            .Where(x => x.TradingDate <= request.AsOfDate)
            .OrderBy(x => x.TradingDate)
            .ToArray();

        return new IndicatorSnapshot(
            request.Symbol,
            request.AsOfDate,
            CalculateSma(observations, request.Configuration.SimpleMovingAverage.Sma20Period),
            CalculateSma(observations, request.Configuration.SimpleMovingAverage.Sma50Period),
            CalculateSma(observations, request.Configuration.SimpleMovingAverage.Sma200Period),
            request.IndicatorCalculationVersion,
            request.Configuration.Version,
            request.CalculatedAt);
    }

    private static IndicatorValue<decimal> CalculateSma(IReadOnlyList<IndicatorPriceObservation> observations, int period)
    {
        if (observations.Count < period) return IndicatorValue<decimal>.InsufficientData();

        var windowStart = observations.Count - period;
        decimal sum = 0;
        for (var index = windowStart; index < observations.Count; index++)
        {
            var close = observations[index].Close;
            if (!close.HasValue) return IndicatorValue<decimal>.InsufficientData();
            sum += close.Value;
        }
        return IndicatorValue<decimal>.Available(sum / period);
    }
}

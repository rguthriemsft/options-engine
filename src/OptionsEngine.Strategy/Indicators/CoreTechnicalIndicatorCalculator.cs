namespace OptionsEngine.Strategy.Indicators;

/// <summary>
/// Calculates the Phase 3B technical indicators from normalized daily observations without provider or persistence dependencies.
/// </summary>
public sealed class CoreTechnicalIndicatorCalculator : IIndicatorCalculator
{
    public IndicatorSnapshot Calculate(IndicatorCalculationRequest request)
    {
        Validate(request);
        var observations = request.Observations
            .Where(x => string.Equals(x.Symbol, request.Symbol, StringComparison.OrdinalIgnoreCase))
            .Where(x => x.TradingDate <= request.AsOfDate)
            .OrderBy(x => x.TradingDate)
            .ToArray();

        var sma = new SimpleMovingAverageIndicatorCalculator().Calculate(request);
        var closes = TrailingCompleteSegment(observations, x => x.Close).Select(x => (double)x.Close!.Value).ToArray();
        var rsi = CalculateRsi(closes, request.Configuration.RelativeStrengthIndex.Period);
        var bollinger = CalculateBollinger(closes, request.Configuration.BollingerBands);
        var macd = CalculateMacd(closes, request.Configuration.Macd);
        var atr = CalculateAtr(TrailingCompleteOhlcSegment(observations), request.Configuration.AverageTrueRange.Period);
        var volatility = CalculateVolatility(closes, request.Configuration.RealizedVolatility);

        return sma with
        {
            Rsi14 = rsi,
            BollingerMiddle = bollinger.Middle,
            BollingerUpper = bollinger.Upper,
            BollingerLower = bollinger.Lower,
            BollingerPercentB = bollinger.PercentB,
            BollingerBandwidth = bollinger.Bandwidth,
            Macd = macd.Value,
            MacdSignal = macd.Signal,
            MacdHistogram = macd.Histogram,
            Atr14 = atr.Value,
            AtrPercent = atr.Percent,
            RealizedVolatility20 = volatility.Short,
            RealizedVolatility30 = volatility.Standard
        };
    }

    private static void Validate(IndicatorCalculationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Symbol);
        ArgumentNullException.ThrowIfNull(request.Observations);
        ArgumentNullException.ThrowIfNull(request.Configuration);
        ArgumentNullException.ThrowIfNull(request.IndicatorCalculationVersion);
        if (request.CalculatedAt.Offset != TimeSpan.Zero) throw new ArgumentException("CalculatedAt must be expressed in UTC.", nameof(request));
        request.Configuration.Validate();
    }

    private static IReadOnlyList<IndicatorPriceObservation> TrailingCompleteSegment(
        IReadOnlyList<IndicatorPriceObservation> observations,
        Func<IndicatorPriceObservation, decimal?> requiredValue)
    {
        var first = observations.Count;
        for (var index = observations.Count - 1; index >= 0; index--)
        {
            if (!requiredValue(observations[index]).HasValue) break;
            first = index;
        }
        return observations.Skip(first).ToArray();
    }

    private static IReadOnlyList<IndicatorPriceObservation> TrailingCompleteOhlcSegment(IReadOnlyList<IndicatorPriceObservation> observations)
    {
        var first = observations.Count;
        for (var index = observations.Count - 1; index >= 0; index--)
        {
            var observation = observations[index];
            if (!observation.High.HasValue || !observation.Low.HasValue || !observation.Close.HasValue) break;
            first = index;
        }
        return observations.Skip(first).ToArray();
    }

    private static IndicatorValue<double> CalculateRsi(IReadOnlyList<double> closes, int period)
    {
        if (closes.Count < period + 1) return IndicatorValue<double>.InsufficientData();

        double averageGain = 0, averageLoss = 0;
        for (var index = 1; index <= period; index++) AddGainLoss(closes[index] - closes[index - 1], ref averageGain, ref averageLoss);
        averageGain /= period;
        averageLoss /= period;
        for (var index = period + 1; index < closes.Count; index++)
        {
            double gain = 0, loss = 0;
            AddGainLoss(closes[index] - closes[index - 1], ref gain, ref loss);
            averageGain = ((averageGain * (period - 1)) + gain) / period;
            averageLoss = ((averageLoss * (period - 1)) + loss) / period;
        }

        if (averageGain == 0 && averageLoss == 0) return IndicatorValue<double>.Available(50);
        if (averageLoss == 0) return IndicatorValue<double>.Available(100);
        if (averageGain == 0) return IndicatorValue<double>.Available(0);
        return IndicatorValue<double>.Available(100 - (100 / (1 + (averageGain / averageLoss))));
    }

    private static void AddGainLoss(double change, ref double gain, ref double loss)
    {
        if (change > 0) gain += change;
        else if (change < 0) loss -= change;
    }

    private static (IndicatorValue<double> Middle, IndicatorValue<double> Upper, IndicatorValue<double> Lower, IndicatorValue<double> PercentB, IndicatorValue<double> Bandwidth) CalculateBollinger(IReadOnlyList<double> closes, BollingerBandsConfiguration configuration)
    {
        if (closes.Count < configuration.Period) return UnavailableBollinger();
        var window = closes.Skip(closes.Count - configuration.Period).ToArray();
        var middle = window.Average();
        // Bollinger Bands use population standard deviation over the observed-price window.
        var variance = window.Sum(value => Math.Pow(value - middle, 2)) / configuration.Period;
        var standardDeviation = Math.Sqrt(variance);
        var upper = middle + (configuration.StandardDeviations * standardDeviation);
        var lower = middle - (configuration.StandardDeviations * standardDeviation);
        var width = upper - lower;
        var percentB = width == 0
            ? IndicatorValue<double>.InsufficientData()
            : IndicatorValue<double>.Available((window[^1] - lower) / width);
        var bandwidth = middle == 0
            ? IndicatorValue<double>.InsufficientData()
            : IndicatorValue<double>.Available(width / middle);
        return (IndicatorValue<double>.Available(middle), IndicatorValue<double>.Available(upper), IndicatorValue<double>.Available(lower), percentB, bandwidth);
    }

    private static (IndicatorValue<double>, IndicatorValue<double>, IndicatorValue<double>, IndicatorValue<double>, IndicatorValue<double>) UnavailableBollinger() =>
        (IndicatorValue<double>.InsufficientData(), IndicatorValue<double>.InsufficientData(), IndicatorValue<double>.InsufficientData(), IndicatorValue<double>.InsufficientData(), IndicatorValue<double>.InsufficientData());

    private static (IndicatorValue<double> Value, IndicatorValue<double> Signal, IndicatorValue<double> Histogram) CalculateMacd(IReadOnlyList<double> closes, MovingAverageConvergenceDivergenceConfiguration configuration)
    {
        if (closes.Count < configuration.SlowPeriod) return UnavailableMacd();
        var fast = Ema(closes, configuration.FastPeriod);
        var slow = Ema(closes, configuration.SlowPeriod);
        var macdValues = Enumerable.Range(configuration.SlowPeriod - 1, closes.Count - configuration.SlowPeriod + 1)
            .Select(index => fast[index]!.Value - slow[index]!.Value).ToArray();
        var macd = IndicatorValue<double>.Available(macdValues[^1]);
        if (macdValues.Length < configuration.SignalPeriod) return (macd, IndicatorValue<double>.InsufficientData(), IndicatorValue<double>.InsufficientData());
        var signal = Ema(macdValues, configuration.SignalPeriod)[^1]!.Value;
        return (macd, IndicatorValue<double>.Available(signal), IndicatorValue<double>.Available(macdValues[^1] - signal));
    }

    private static (IndicatorValue<double>, IndicatorValue<double>, IndicatorValue<double>) UnavailableMacd() =>
        (IndicatorValue<double>.InsufficientData(), IndicatorValue<double>.InsufficientData(), IndicatorValue<double>.InsufficientData());

    private static double?[] Ema(IReadOnlyList<double> values, int period)
    {
        var result = new double?[values.Count];
        if (values.Count < period) return result;
        var ema = values.Take(period).Average();
        result[period - 1] = ema;
        var multiplier = 2d / (period + 1);
        for (var index = period; index < values.Count; index++)
        {
            ema = (values[index] * multiplier) + (ema * (1 - multiplier));
            result[index] = ema;
        }
        return result;
    }

    private static (IndicatorValue<double> Value, IndicatorValue<double> Percent) CalculateAtr(IReadOnlyList<IndicatorPriceObservation> observations, int period)
    {
        if (observations.Count < period + 1) return (IndicatorValue<double>.InsufficientData(), IndicatorValue<double>.InsufficientData());
        var trueRanges = new double[observations.Count - 1];
        for (var index = 1; index < observations.Count; index++)
        {
            var current = observations[index];
            var previousClose = observations[index - 1].Close!.Value;
            var high = current.High!.Value;
            var low = current.Low!.Value;
            trueRanges[index - 1] = Math.Max((double)(high - low), Math.Max(Math.Abs((double)(high - previousClose)), Math.Abs((double)(low - previousClose))));
        }
        var atr = trueRanges.Take(period).Average();
        for (var index = period; index < trueRanges.Length; index++) atr = ((atr * (period - 1)) + trueRanges[index]) / period;
        var close = observations[^1].Close!.Value;
        return (IndicatorValue<double>.Available(atr), close == 0 ? IndicatorValue<double>.InsufficientData() : IndicatorValue<double>.Available(atr / (double)close));
    }

    private static (IndicatorValue<double> Short, IndicatorValue<double> Standard) CalculateVolatility(IReadOnlyList<double> closes, RealizedVolatilityConfiguration configuration) =>
        (CalculateVolatility(closes, configuration.ShortPeriod, configuration.AnnualizationTradingDays), CalculateVolatility(closes, configuration.StandardPeriod, configuration.AnnualizationTradingDays));

    private static IndicatorValue<double> CalculateVolatility(IReadOnlyList<double> closes, int period, int annualizationTradingDays)
    {
        if (closes.Count < period + 1) return IndicatorValue<double>.InsufficientData();
        var window = closes.Skip(closes.Count - period - 1).ToArray();
        if (window.Any(close => close <= 0)) return IndicatorValue<double>.InsufficientData();
        var returns = Enumerable.Range(1, period).Select(index => Math.Log(window[index] / window[index - 1])).ToArray();
        var mean = returns.Average();
        // Realized volatility uses sample standard deviation of the N log returns.
        var sampleVariance = returns.Sum(value => Math.Pow(value - mean, 2)) / (period - 1);
        return IndicatorValue<double>.Available(Math.Sqrt(sampleVariance) * Math.Sqrt(annualizationTradingDays));
    }
}

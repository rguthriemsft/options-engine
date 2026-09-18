using System.Globalization;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Strategy.EntryStrategy;

/// <summary>Pure Phase 4B CCOS calculation and the approved underlying breakout veto.</summary>
public sealed class CcosCalculator
{
    public UnderlyingEligibilityResult Evaluate(EvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Validate();

        var indicators = context.Indicators;
        var configuration = context.Configuration.Ccos;
        var components = new[]
        {
            Volatility(indicators, configuration),
            Rsi(indicators, configuration),
            Bollinger(indicators, configuration),
            TrendMomentum(indicators, configuration),
            Resistance(indicators, configuration),
            MarketSectorRegime(indicators, configuration)
        };
        var missing = components.SelectMany(x => x.MissingInputs).Distinct().ToArray();
        var ccos = components.Any(x => x.Status != ScoreStatus.Available)
            ? new ScoreResult(ScoreStatus.Unavailable, null, 100, null, context.Holding.MinimumCcos, null, components, missing,
                "CCOS is unavailable because one or more required components are unavailable.")
            : AvailableCcos(components, context.Holding.MinimumCcos, configuration.Classification);

        return new UnderlyingEligibilityResult(ccos, BreakoutVeto(indicators, configuration));
    }

    private static ScoreResult AvailableCcos(IReadOnlyList<ScoreComponentResult> components, double minimum,
        CcosClassificationConfiguration classification)
    {
        var score = components.Sum(x => x.Score!.Value);
        var label = Classify(score, classification) switch
        {
            CcosClassification.NoTrade => "NO_TRADE",
            CcosClassification.Weak => "WEAK",
            CcosClassification.Watch => "WATCH",
            CcosClassification.SellCandidate => "SELL_CANDIDATE",
            CcosClassification.Strong => "STRONG",
            CcosClassification.Exceptional => "EXCEPTIONAL",
            _ => throw new ArgumentOutOfRangeException(nameof(score))
        };
        return new ScoreResult(ScoreStatus.Available, score, 100, label, minimum, score >= minimum, components, [],
            $"CCOS is {score.ToString(CultureInfo.InvariantCulture)} and classified as {label}.");
    }

    public static CcosClassification Classify(double score, CcosClassificationConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();
        if (!double.IsFinite(score) || score < 0 || score > 100)
            throw new ArgumentOutOfRangeException(nameof(score), "CCOS must be finite and within 0–100.");
        return score < configuration.WeakMinimum ? CcosClassification.NoTrade
            : score < configuration.WatchMinimum ? CcosClassification.Weak
            : score < configuration.SellCandidateMinimum ? CcosClassification.Watch
            : score < configuration.StrongMinimum ? CcosClassification.SellCandidate
            : score < configuration.ExceptionalMinimum ? CcosClassification.Strong
            : CcosClassification.Exceptional;
    }

    private static ScoreComponentResult Volatility(IndicatorContext context, CcosConfiguration configuration)
    {
        var inputs = new List<ScoreInput>();
        var missing = new List<MissingInputCode>();
        var percentile = Required(context.IvPercentile, MissingInputCode.IvPercentile, "IV_PERCENTILE", inputs, missing);
        var iv30 = Required(context.Iv30, MissingInputCode.Iv30, "IV30", inputs, missing);
        var rv30 = Required(context.RealizedVolatility30, MissingInputCode.RealizedVolatility30, "RV30", inputs, missing);
        if (rv30 is <= 0)
        {
            SetUnavailable(inputs, missing, "RV30", MissingInputCode.RealizedVolatility30);
            rv30 = null;
        }
        if (percentile is null || iv30 is null || rv30 is null)
            return Unavailable(ScoreComponentCode.CcosVolatility, "Volatility", configuration.VolatilityMaximumScore, inputs, missing);

        var ratio = iv30.Value / rv30.Value;
        inputs.Add(Input("IV30_TO_RV30", ratio));
        return Available(ScoreComponentCode.CcosVolatility, "Volatility",
            Score(configuration.Volatility.IvPercentile, percentile.Value) + Score(configuration.Volatility.Iv30ToRv30, ratio),
            configuration.VolatilityMaximumScore, inputs, "IV Percentile and IV30/RV30 were scored.");
    }

    private static ScoreComponentResult Rsi(IndicatorContext context, CcosConfiguration configuration)
    {
        var inputs = new List<ScoreInput>();
        var missing = new List<MissingInputCode>();
        var rsi = Required(context.Rsi14, MissingInputCode.Rsi14, "RSI14", inputs, missing);
        if (rsi is { } value && (value < 0 || value > 100))
        {
            SetUnavailable(inputs, missing, "RSI14", MissingInputCode.Rsi14);
            rsi = null;
        }
        return rsi is null
            ? Unavailable(ScoreComponentCode.CcosRsi, "RSI", configuration.RsiMaximumScore, inputs, missing)
            : Available(ScoreComponentCode.CcosRsi, "RSI", Score(configuration.Rsi, rsi.Value), configuration.RsiMaximumScore,
                inputs, "RSI14 was scored.");
    }

    private static ScoreComponentResult Bollinger(IndicatorContext context, CcosConfiguration configuration)
    {
        var inputs = new List<ScoreInput>();
        var missing = new List<MissingInputCode>();
        var percentB = Required(context.BollingerPercentB, MissingInputCode.BollingerPercentB, "BOLLINGER_PERCENT_B", inputs, missing);
        var bandwidth = Required(context.BollingerBandwidth, MissingInputCode.BollingerBandwidth, "BOLLINGER_BANDWIDTH", inputs, missing);
        return percentB is null || bandwidth is null
            ? Unavailable(ScoreComponentCode.CcosBollinger, "Bollinger Bands", configuration.BollingerMaximumScore, inputs, missing)
            : Available(ScoreComponentCode.CcosBollinger, "Bollinger Bands",
                Score(configuration.Bollinger.PercentB, percentB.Value) + configuration.Bollinger.BandwidthPoints,
                configuration.BollingerMaximumScore, inputs, "Bollinger %B and bandwidth were available.");
    }

    private static ScoreComponentResult TrendMomentum(IndicatorContext context, CcosConfiguration configuration)
    {
        var inputs = new List<ScoreInput>();
        var missing = new List<MissingInputCode>();
        var sma20 = Required(context.Sma20, MissingInputCode.Sma20, "SMA20", inputs, missing);
        var sma50 = Required(context.Sma50, MissingInputCode.Sma50, "SMA50", inputs, missing);
        var sma200 = Required(context.Sma200, MissingInputCode.Sma200, "SMA200", inputs, missing);
        var macd = Required(context.MacdHistogram, MissingInputCode.MacdHistogram, "MACD_HISTOGRAM", inputs, missing);
        if (sma20 is null || sma50 is null || sma200 is null || macd is null)
            return Unavailable(ScoreComponentCode.CcosTrendMomentum, "Trend/Momentum", configuration.TrendMomentumMaximumScore, inputs, missing);

        var score = (sma20.Value > sma50.Value ? configuration.TrendMomentum.Sma20AboveSma50Points : 0) +
                    (sma50.Value > sma200.Value ? configuration.TrendMomentum.Sma50AboveSma200Points : 0) +
                    (macd.Value > 0 ? configuration.TrendMomentum.PositiveMacdHistogramPoints : 0);
        return Available(ScoreComponentCode.CcosTrendMomentum, "Trend/Momentum", score,
            configuration.TrendMomentumMaximumScore, inputs, "Trend and MACD conditions were scored.");
    }

    private static ScoreComponentResult Resistance(IndicatorContext context, CcosConfiguration configuration)
    {
        var inputs = new List<ScoreInput>
        {
            new("RESISTANCE_UNAVAILABLE_REASON", context.ResistanceUnavailableReason == ResistanceUnavailableReason.InsufficientData
                ? AvailabilityStatus.Unavailable : AvailabilityStatus.Available,
                context.ResistanceUnavailableReason?.ToString())
        };
        if (context.ResistanceUnavailableReason == ResistanceUnavailableReason.NoQualifiedResistance)
            return Available(ScoreComponentCode.CcosResistanceStructure, "Resistance/Structure",
                configuration.ResistanceStructure.NoQualifiedResistancePoints, configuration.ResistanceStructureMaximumScore, inputs,
                "No qualified resistance is a valid structural state.");

        var missing = new List<MissingInputCode>();
        if (context.ResistanceUnavailableReason == ResistanceUnavailableReason.InsufficientData)
            missing.Add(MissingInputCode.Resistance);
        var distance = Required(context.DistanceToResistancePercent, MissingInputCode.Resistance, "DISTANCE_TO_RESISTANCE_PERCENT", inputs, missing);
        var touches = Required(context.ResistanceTouchCount, MissingInputCode.Resistance, "RESISTANCE_TOUCH_COUNT", inputs, missing);
        var age = Required(context.ResistanceAgeTradingDays, MissingInputCode.Resistance, "RESISTANCE_AGE_TRADING_DAYS", inputs, missing);
        if (context.ResistanceUnavailableReason is not null || distance is null || touches is null || age is null)
            return Unavailable(ScoreComponentCode.CcosResistanceStructure, "Resistance/Structure", configuration.ResistanceStructureMaximumScore,
                inputs, missing);

        var score = Score(configuration.ResistanceStructure.DistancePercent, distance.Value) +
                    Score(configuration.ResistanceStructure.TouchCount, touches.Value) +
                    Score(configuration.ResistanceStructure.AgeTradingDays, age.Value);
        return Available(ScoreComponentCode.CcosResistanceStructure, "Resistance/Structure", score,
            configuration.ResistanceStructureMaximumScore, inputs, "Resistance distance, touches, and recency were scored.");
    }

    private static ScoreComponentResult MarketSectorRegime(IndicatorContext context, CcosConfiguration configuration)
    {
        var inputs = new List<ScoreInput>
        {
            new("MARKET_REGIME", context.MarketRegime == MarketRegime.InsufficientData ? AvailabilityStatus.Unavailable : AvailabilityStatus.Available,
                context.MarketRegime.ToString()),
            new("SECTOR_REGIME", context.SectorRegime == MarketRegime.InsufficientData ? AvailabilityStatus.Unavailable : AvailabilityStatus.Available,
                context.SectorRegime.ToString())
        };
        var missing = new List<MissingInputCode>();
        if (context.MarketRegime == MarketRegime.InsufficientData) missing.Add(MissingInputCode.MarketRegime);
        if (context.SectorRegime == MarketRegime.InsufficientData) missing.Add(MissingInputCode.SectorRegime);
        if (missing.Count > 0)
            return Unavailable(ScoreComponentCode.CcosMarketSectorRegime, "Market/Sector Regime",
                configuration.MarketSectorRegimeMaximumScore, inputs, missing);

        var score = RegimePoints(context.MarketRegime, configuration.MarketSectorRegime.Market) +
                    RegimePoints(context.SectorRegime, configuration.MarketSectorRegime.Sector);
        return Available(ScoreComponentCode.CcosMarketSectorRegime, "Market/Sector Regime", score,
            configuration.MarketSectorRegimeMaximumScore, inputs, "Market and sector regimes were scored.");
    }

    private static GateResult BreakoutVeto(IndicatorContext context, CcosConfiguration configuration)
    {
        var inputs = new List<ScoreInput>();
        var missing = new List<MissingInputCode>();
        var percentB = Required(context.BollingerPercentB, MissingInputCode.BollingerPercentB, "BOLLINGER_PERCENT_B", inputs, missing);
        var rsi = Required(context.Rsi14, MissingInputCode.Rsi14, "RSI14", inputs, missing);
        var macd = Required(context.MacdHistogram, MissingInputCode.MacdHistogram, "MACD_HISTOGRAM", inputs, missing);
        if (percentB is null || rsi is null || macd is null)
            return new GateResult(GateCode.BreakoutVeto, GateStatus.Unavailable, RejectionReasonCode.InsufficientData, inputs, missing,
                "Breakout veto could not be evaluated because a required input is unavailable.");

        var veto = percentB.Value > configuration.BreakoutVeto.BollingerPercentBThresholdExclusive &&
                   rsi.Value >= configuration.BreakoutVeto.RsiThresholdInclusive &&
                   macd.Value > configuration.BreakoutVeto.MacdHistogramThresholdExclusive;
        return veto
            ? new GateResult(GateCode.BreakoutVeto, GateStatus.Failed, RejectionReasonCode.BreakoutVeto, inputs, [],
                "Bollinger %B, RSI14, and MACD histogram meet the breakout-veto conditions.")
            : new GateResult(GateCode.BreakoutVeto, GateStatus.Passed, null, inputs, [],
                "The breakout-veto conditions are not all present.");
    }

    private static double? Required(IndicatorValue<double> value, MissingInputCode missingCode, string code,
        ICollection<ScoreInput> inputs, ICollection<MissingInputCode> missing)
    {
        if (value.Status == IndicatorValueStatus.Available && value.Value is { } number && double.IsFinite(number))
        {
            inputs.Add(Input(code, number));
            return number;
        }
        SetUnavailable(inputs, missing, code, missingCode);
        return null;
    }

    private static decimal? Required(IndicatorValue<decimal> value, MissingInputCode missingCode, string code,
        ICollection<ScoreInput> inputs, ICollection<MissingInputCode> missing)
    {
        if (value.Status == IndicatorValueStatus.Available && value.Value is { } number)
        {
            inputs.Add(Input(code, number));
            return number;
        }
        SetUnavailable(inputs, missing, code, missingCode);
        return null;
    }

    private static int? Required(IndicatorValue<int> value, MissingInputCode missingCode, string code,
        ICollection<ScoreInput> inputs, ICollection<MissingInputCode> missing)
    {
        if (value.Status == IndicatorValueStatus.Available && value.Value is { } number)
        {
            inputs.Add(Input(code, number));
            return number;
        }
        SetUnavailable(inputs, missing, code, missingCode);
        return null;
    }

    private static ScoreInput Input(string code, double value) => new(code, AvailabilityStatus.Available, value.ToString("G17", CultureInfo.InvariantCulture));
    private static ScoreInput Input(string code, decimal value) => new(code, AvailabilityStatus.Available, value.ToString(CultureInfo.InvariantCulture));
    private static ScoreInput Input(string code, int value) => new(code, AvailabilityStatus.Available, value.ToString(CultureInfo.InvariantCulture));

    private static void SetUnavailable(ICollection<ScoreInput> inputs, ICollection<MissingInputCode> missing, string code, MissingInputCode missingCode)
    {
        inputs.Add(new ScoreInput(code, AvailabilityStatus.Unavailable, null));
        missing.Add(missingCode);
    }

    private static ScoreComponentResult Available(ScoreComponentCode code, string name, double score, double maximum,
        IReadOnlyList<ScoreInput> inputs, string explanation) => new(code, name, ScoreStatus.Available, score, maximum, inputs, [], explanation);

    private static ScoreComponentResult Unavailable(ScoreComponentCode code, string name, double maximum,
        IReadOnlyList<ScoreInput> inputs, IReadOnlyList<MissingInputCode> missing) =>
        new(code, name, ScoreStatus.Unavailable, null, maximum, inputs, missing.Distinct().ToArray(),
            $"{name} is unavailable because required inputs are unavailable.");

    private static double Score(ContinuousScoreTable table, double value) => table.Bands.Single(band =>
        (band.Minimum is null || value > band.Minimum || value == band.Minimum && band.IncludesMinimum) &&
        (band.Maximum is null || value < band.Maximum || value == band.Maximum && band.IncludesMaximum)).Points;

    private static double Score(IntegerScoreTable table, int value) => table.Bands.Single(band =>
        value >= band.Minimum && (band.Maximum is null || value <= band.Maximum)).Points;

    private static double RegimePoints(MarketRegime regime, RegimeScorePoints points) => regime switch
    {
        MarketRegime.Neutral => points.Neutral,
        MarketRegime.Bullish => points.Bullish,
        MarketRegime.Bearish => points.Bearish,
        _ => throw new ArgumentOutOfRangeException(nameof(regime))
    };
}

/// <summary>Phase 4B result retained for later packet orchestration and selection.</summary>
public sealed record UnderlyingEligibilityResult(ScoreResult Ccos, GateResult BreakoutVeto);

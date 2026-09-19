using System.Collections.Immutable;

namespace OptionsEngine.Strategy.EntryStrategy;

/// <summary>A continuous scoring interval. Adjacent intervals must assign a shared boundary to exactly one band.</summary>
public sealed record ContinuousScoreBand(
    double? Minimum,
    bool IncludesMinimum,
    double? Maximum,
    bool IncludesMaximum,
    double Points);

public sealed record ContinuousScoreTable
{
    public required ImmutableArray<ContinuousScoreBand> Bands { get; init; }

    internal void Validate(string name, int count, double maximumPoints, double? firstMinimum = null, double? lastMaximum = null)
    {
        if (Bands.IsDefault || Bands.Length != count)
            throw new ArgumentException($"{name} must contain {count} scoring bands.", name);
        if (Bands[0].Minimum != firstMinimum || Bands[^1].Maximum != lastMaximum)
            throw new ArgumentException($"{name} has an invalid scoring domain.", name);

        for (var index = 0; index < Bands.Length; index++)
        {
            var band = Bands[index];
            ScoreConfigurationValidation.Points(band.Points, maximumPoints, name);
            if (band.Minimum is { } minimum && !double.IsFinite(minimum) ||
                band.Maximum is { } maximum && !double.IsFinite(maximum) ||
                band.Minimum is { } lower && band.Maximum is { } upper && lower >= upper ||
                index > 0 && band.Minimum is null || index < Bands.Length - 1 && band.Maximum is null)
                throw new ArgumentException($"{name} has an invalid or unordered boundary.", name);
            if (index > 0)
            {
                var previous = Bands[index - 1];
                if (previous.Maximum != band.Minimum || previous.IncludesMaximum == band.IncludesMinimum)
                    throw new ArgumentException($"{name} has a gap or overlapping boundary.", name);
            }
        }

        if (Math.Abs(Bands.Max(x => x.Points) - maximumPoints) > 0.0000001)
            throw new ArgumentException($"{name} must be able to award its full component points.", name);
    }
}

/// <summary>Inclusive integer intervals for trading-day counts, touches, and open interest.</summary>
public sealed record IntegerScoreBand(int Minimum, int? Maximum, double Points);

public sealed record IntegerScoreTable
{
    public required ImmutableArray<IntegerScoreBand> Bands { get; init; }

    internal void Validate(string name, int count, double maximumPoints, int firstMinimum, int? lastMaximum)
    {
        if (Bands.IsDefault || Bands.Length != count)
            throw new ArgumentException($"{name} must contain {count} scoring bands.", name);
        if (Bands[0].Minimum != firstMinimum || Bands[^1].Maximum != lastMaximum)
            throw new ArgumentException($"{name} has an invalid scoring domain.", name);

        for (var index = 0; index < Bands.Length; index++)
        {
            var band = Bands[index];
            ScoreConfigurationValidation.Points(band.Points, maximumPoints, name);
            if (band.Minimum < 0 || band.Maximum is { } maximum && maximum < band.Minimum ||
                index < Bands.Length - 1 && band.Maximum is null)
                throw new ArgumentException($"{name} has an invalid or unordered boundary.", name);
            if (index > 0)
            {
                var previousMaximum = Bands[index - 1].Maximum;
                if (previousMaximum is null || band.Minimum != previousMaximum.Value + 1)
                    throw new ArgumentException($"{name} has a gap or overlapping boundary.", name);
            }
        }

        if (Math.Abs(Bands.Max(x => x.Points) - maximumPoints) > 0.0000001)
            throw new ArgumentException($"{name} must be able to award its full component points.", name);
    }
}

internal static class ScoreConfigurationValidation
{
    internal static void Points(double value, double maximum, string name)
    {
        if (!double.IsFinite(value) || value < 0 || value > maximum)
            throw new ArgumentOutOfRangeException(name, $"{name} points must be finite and between 0 and {maximum}.");
    }

    internal static void OrderedBoundaries(string name, params double[] boundaries)
    {
        if (boundaries.Length == 0 || boundaries.Any(x => !double.IsFinite(x) || x <= 0 || x > 100) ||
            boundaries.Zip(boundaries.Skip(1)).Any(x => x.First >= x.Second))
            throw new ArgumentException($"{name} boundaries must be finite, positive, ordered, and within 0–100.", name);
    }
}

public sealed record CcosClassificationConfiguration
{
    public double WeakMinimum { get; init; } = 40;
    public double WatchMinimum { get; init; } = 55;
    public double SellCandidateMinimum { get; init; } = 70;
    public double StrongMinimum { get; init; } = 80;
    public double ExceptionalMinimum { get; init; } = 90;

    internal void Validate() => ScoreConfigurationValidation.OrderedBoundaries(nameof(CcosClassificationConfiguration),
        WeakMinimum, WatchMinimum, SellCandidateMinimum, StrongMinimum, ExceptionalMinimum);
}

public sealed record ContractScoreClassificationConfiguration
{
    public double WeakMinimum { get; init; } = 60;
    public double AcceptableMinimum { get; init; } = 70;
    public double GoodMinimum { get; init; } = 80;
    public double ExcellentMinimum { get; init; } = 90;

    internal void Validate() => ScoreConfigurationValidation.OrderedBoundaries(nameof(ContractScoreClassificationConfiguration),
        WeakMinimum, AcceptableMinimum, GoodMinimum, ExcellentMinimum);
}

public sealed record CcosVolatilityScoringConfiguration
{
    public double IvPercentileMaximumScore { get; init; } = 15;
    public double Iv30ToRv30MaximumScore { get; init; } = 10;
    public ContinuousScoreTable IvPercentile { get; init; } = new() { Bands =
    [
        new(null, false, 20, false, 0), new(20, true, 30, false, 3), new(30, true, 40, false, 6),
        new(40, true, 50, false, 9), new(50, true, 60, false, 11), new(60, true, 70, true, 13),
        new(70, false, null, false, 15)
    ] };

    public ContinuousScoreTable Iv30ToRv30 { get; init; } = new() { Bands =
    [
        new(null, false, .90, false, 0), new(.90, true, 1.00, false, 2),
        new(1.00, true, 1.10, false, 4), new(1.10, true, 1.20, false, 6),
        new(1.20, true, 1.35, true, 8), new(1.35, false, null, false, 10)
    ] };

    internal void Validate(double maximum)
    {
        ArgumentNullException.ThrowIfNull(IvPercentile);
        ArgumentNullException.ThrowIfNull(Iv30ToRv30);
        ScoreConfigurationValidation.Points(IvPercentileMaximumScore, maximum, nameof(IvPercentileMaximumScore));
        ScoreConfigurationValidation.Points(Iv30ToRv30MaximumScore, maximum, nameof(Iv30ToRv30MaximumScore));
        IvPercentile.Validate(nameof(IvPercentile), 7, IvPercentileMaximumScore);
        Iv30ToRv30.Validate(nameof(Iv30ToRv30), 6, Iv30ToRv30MaximumScore);
        if (Math.Abs(IvPercentileMaximumScore + Iv30ToRv30MaximumScore - maximum) > 0.0000001)
            throw new ArgumentException("Volatility subcomponent maximums must total the volatility component maximum.", nameof(maximum));
    }
}

public sealed record CcosBollingerScoringConfiguration
{
    public double PercentBMaximumScore { get; init; } = 10;
    public ContinuousScoreTable PercentB { get; init; } = new() { Bands =
    [
        new(null, false, .40, false, 0), new(.40, true, .60, false, 2),
        new(.60, true, .75, false, 5), new(.75, true, .90, false, 8),
        new(.90, true, 1.05, false, 10), new(1.05, true, 1.15, true, 7),
        new(1.15, false, null, false, 3)
    ] };
    public double BandwidthPoints { get; init; } = 5;

    internal void Validate(double maximum)
    {
        ArgumentNullException.ThrowIfNull(PercentB);
        ScoreConfigurationValidation.Points(PercentBMaximumScore, maximum, nameof(PercentBMaximumScore));
        PercentB.Validate(nameof(PercentB), 7, PercentBMaximumScore);
        ScoreConfigurationValidation.Points(BandwidthPoints, maximum, nameof(BandwidthPoints));
        if (Math.Abs(PercentBMaximumScore + BandwidthPoints - maximum) > 0.0000001)
            throw new ArgumentException("Bollinger subcomponent points must total its maximum score.", nameof(maximum));
    }
}

public sealed record CcosTrendScoringConfiguration
{
    public double Sma20AboveSma50Points { get; init; } = 5;
    public double Sma50AboveSma200Points { get; init; } = 5;
    public double PositiveMacdHistogramPoints { get; init; } = 5;

    internal void Validate(double maximum)
    {
        ScoreConfigurationValidation.Points(Sma20AboveSma50Points, maximum, nameof(Sma20AboveSma50Points));
        ScoreConfigurationValidation.Points(Sma50AboveSma200Points, maximum, nameof(Sma50AboveSma200Points));
        ScoreConfigurationValidation.Points(PositiveMacdHistogramPoints, maximum, nameof(PositiveMacdHistogramPoints));
        if (Math.Abs(Sma20AboveSma50Points + Sma50AboveSma200Points + PositiveMacdHistogramPoints - maximum) > 0.0000001)
            throw new ArgumentException("Trend condition points must total its maximum score.", nameof(maximum));
    }
}

public sealed record CcosResistanceScoringConfiguration
{
    public double NoQualifiedResistancePoints { get; init; } = 0;
    public double DistanceMaximumScore { get; init; } = 5;
    public double TouchMaximumScore { get; init; } = 5;
    public double RecencyMaximumScore { get; init; } = 5;
    public ContinuousScoreTable DistancePercent { get; init; } = new() { Bands =
    [
        new(null, false, .02, true, 5), new(.02, false, .05, true, 3),
        new(.05, false, null, false, 1)
    ] };
    public IntegerScoreTable TouchCount { get; init; } = new() { Bands =
    [
        new(1, 1, 0), new(2, 2, 1), new(3, 3, 3), new(4, null, 5)
    ] };
    public IntegerScoreTable AgeTradingDays { get; init; } = new() { Bands =
    [
        new(0, 20, 5), new(21, 60, 3), new(61, null, 1)
    ] };

    internal void Validate(double maximum)
    {
        ScoreConfigurationValidation.Points(NoQualifiedResistancePoints, maximum, nameof(NoQualifiedResistancePoints));
        ArgumentNullException.ThrowIfNull(DistancePercent);
        ArgumentNullException.ThrowIfNull(TouchCount);
        ArgumentNullException.ThrowIfNull(AgeTradingDays);
        ScoreConfigurationValidation.Points(DistanceMaximumScore, maximum, nameof(DistanceMaximumScore));
        ScoreConfigurationValidation.Points(TouchMaximumScore, maximum, nameof(TouchMaximumScore));
        ScoreConfigurationValidation.Points(RecencyMaximumScore, maximum, nameof(RecencyMaximumScore));
        DistancePercent.Validate(nameof(DistancePercent), 3, DistanceMaximumScore);
        TouchCount.Validate(nameof(TouchCount), 4, TouchMaximumScore, 1, null);
        AgeTradingDays.Validate(nameof(AgeTradingDays), 3, RecencyMaximumScore, 0, null);
        if (Math.Abs(DistanceMaximumScore + TouchMaximumScore + RecencyMaximumScore - maximum) > 0.0000001)
            throw new ArgumentException("Resistance subcomponent maximums must total its maximum score.", nameof(maximum));
    }
}

public sealed record RegimeScorePoints(double Neutral, double Bullish, double Bearish)
{
    internal void Validate(string name, double maximum)
    {
        ScoreConfigurationValidation.Points(Neutral, maximum, name);
        ScoreConfigurationValidation.Points(Bullish, maximum, name);
        ScoreConfigurationValidation.Points(Bearish, maximum, name);
        if (Math.Abs(Math.Max(Neutral, Math.Max(Bullish, Bearish)) - maximum) > 0.0000001)
            throw new ArgumentException($"{name} must be able to award its full points.", name);
    }
}

public sealed record CcosRegimeScoringConfiguration
{
    public double MarketMaximumScore { get; init; } = 8;
    public double SectorMaximumScore { get; init; } = 7;
    public RegimeScorePoints Market { get; init; } = new(8, 4, 0);
    public RegimeScorePoints Sector { get; init; } = new(7, 3, 0);

    internal void Validate(double maximum)
    {
        ArgumentNullException.ThrowIfNull(Market);
        ArgumentNullException.ThrowIfNull(Sector);
        ScoreConfigurationValidation.Points(MarketMaximumScore, maximum, nameof(MarketMaximumScore));
        ScoreConfigurationValidation.Points(SectorMaximumScore, maximum, nameof(SectorMaximumScore));
        Market.Validate(nameof(Market), MarketMaximumScore);
        Sector.Validate(nameof(Sector), SectorMaximumScore);
        if (Math.Abs(MarketMaximumScore + SectorMaximumScore - maximum) > 0.0000001)
            throw new ArgumentException("Regime subcomponent maximums must total its maximum score.", nameof(maximum));
    }
}

public sealed record BreakoutVetoConfiguration
{
    public double BollingerPercentBThresholdExclusive { get; init; } = 1.15;
    public double RsiThresholdInclusive { get; init; } = 75;
    public double MacdHistogramThresholdExclusive { get; init; } = 0;

    internal void Validate()
    {
        if (!double.IsFinite(BollingerPercentBThresholdExclusive) ||
            !double.IsFinite(RsiThresholdInclusive) || RsiThresholdInclusive < 0 || RsiThresholdInclusive > 100 ||
            !double.IsFinite(MacdHistogramThresholdExclusive))
            throw new ArgumentOutOfRangeException(nameof(BollingerPercentBThresholdExclusive), "Breakout thresholds must be finite and RSI must be within 0–100.");
    }
}

public sealed record ContractDeltaScoringConfiguration
{
    public double BelowPreferredPoints { get; init; } = 20;
    public double WithinPreferredPoints { get; init; } = 25;
    public double AbovePreferredPoints { get; init; } = 10;

    internal void Validate(double maximum)
    {
        ScoreConfigurationValidation.Points(BelowPreferredPoints, maximum, nameof(BelowPreferredPoints));
        ScoreConfigurationValidation.Points(WithinPreferredPoints, maximum, nameof(WithinPreferredPoints));
        ScoreConfigurationValidation.Points(AbovePreferredPoints, maximum, nameof(AbovePreferredPoints));
        if (Math.Abs(Math.Max(BelowPreferredPoints, Math.Max(WithinPreferredPoints, AbovePreferredPoints)) - maximum) > 0.0000001)
            throw new ArgumentException("Delta scoring must be able to award its full points.", nameof(maximum));
    }
}

public sealed record ContractLiquidityScoringConfiguration
{
    public double SpreadMaximumScore { get; init; } = 5;
    public double OpenInterestMaximumScore { get; init; } = 5;
    public ContinuousScoreTable SpreadPercent { get; init; } = new() { Bands =
    [
        new(null, false, .05, true, 5), new(.05, false, .10, true, 4),
        new(.10, false, .15, true, 2), new(.15, false, .20, true, 1)
    ] };
    public IntegerScoreTable OpenInterest { get; init; } = new() { Bands =
    [
        new(100, 249, 1), new(250, 499, 2), new(500, 999, 3),
        new(1000, 1999, 4), new(2000, null, 5)
    ] };

    internal void Validate(double maximum, double maximumSpread, long minimumOpenInterest)
    {
        ArgumentNullException.ThrowIfNull(SpreadPercent);
        ArgumentNullException.ThrowIfNull(OpenInterest);
        ScoreConfigurationValidation.Points(SpreadMaximumScore, maximum, nameof(SpreadMaximumScore));
        ScoreConfigurationValidation.Points(OpenInterestMaximumScore, maximum, nameof(OpenInterestMaximumScore));
        SpreadPercent.Validate(nameof(SpreadPercent), 4, SpreadMaximumScore, null, maximumSpread);
        if (minimumOpenInterest > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(minimumOpenInterest), "The configured open-interest floor exceeds the scoring table range.");
        OpenInterest.Validate(nameof(OpenInterest), 5, OpenInterestMaximumScore, (int)minimumOpenInterest, null);
        if (Math.Abs(SpreadMaximumScore + OpenInterestMaximumScore - maximum) > 0.0000001)
            throw new ArgumentException("Liquidity subcomponent maximums must total its maximum score.", nameof(maximum));
    }
}

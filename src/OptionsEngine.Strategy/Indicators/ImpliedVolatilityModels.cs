namespace OptionsEngine.Strategy.Indicators;

public enum IvOptionType { Call, Put }

/// <summary>Normalized Phase 2 contract facts needed for underlying-level IV context.</summary>
public sealed record IvOptionContract(
    decimal Strike,
    IvOptionType OptionType,
    double? ImpliedVolatility,
    decimal? UnderlyingPrice);

public sealed record IvOptionChainObservation(
    string UnderlyingSymbol,
    DateOnly Expiration,
    DateTimeOffset ObservedAt,
    IReadOnlyList<IvOptionContract> Contracts);

public sealed record HistoricalIv30Observation(
    string Symbol,
    DateOnly AsOfDate,
    IndicatorValue<double> Iv30,
    IndicatorCalculationVersion IndicatorCalculationVersion,
    ConfigurationVersion ConfigurationVersion);

public enum Iv30UnavailableReason
{
    NoEligibleSnapshot,
    NoEligibleAtmPair,
    NoExactOrBracketingExpirations
}

public sealed record ImpliedVolatilityCalculationRequest(
    string Symbol,
    DateOnly AsOfDate,
    IReadOnlyList<IvOptionChainObservation> OptionChains,
    IReadOnlyList<HistoricalIv30Observation> HistoricalIv30,
    IndicatorConfiguration Configuration,
    IndicatorCalculationVersion IndicatorCalculationVersion,
    DateTimeOffset CalculatedAt);

public sealed record ImpliedVolatilityContext(
    string Symbol,
    DateOnly AsOfDate,
    IndicatorValue<double> Iv30,
    IndicatorValue<double> IvRank,
    IndicatorValue<double> IvPercentile,
    Iv30UnavailableReason? Iv30UnavailableReason,
    IndicatorCalculationVersion IndicatorCalculationVersion,
    ConfigurationVersion ConfigurationVersion,
    DateTimeOffset CalculatedAt)
{
    public IndicatorSnapshot ApplyTo(IndicatorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!string.Equals(snapshot.Symbol, Symbol, StringComparison.OrdinalIgnoreCase) || snapshot.AsOfDate != AsOfDate ||
            snapshot.IndicatorCalculationVersion != IndicatorCalculationVersion || snapshot.ConfigurationVersion != ConfigurationVersion)
            throw new ArgumentException("IV context and indicator snapshot identity must match.", nameof(snapshot));
        return snapshot with { Iv30 = Iv30, IvRank = IvRank, IvPercentile = IvPercentile, Iv30UnavailableReason = Iv30UnavailableReason };
    }
}

using OptionsEngine.Domain.Accounts;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Application.EntryStrategy;

/// <summary>Shared Phase 4 context mapping reused by later analytical orchestration.</summary>
internal static class EntryStrategyContextMapper
{
    private static readonly TimeZoneInfo NewYork =
        TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    public static HoldingContext MapHolding(Holding holding) => new(holding.HoldingId, holding.Symbol,
        holding.AssetType, holding.AssignmentSensitivity, holding.TaxSensitivity, holding.MaximumInitialDelta,
        holding.PreferredDeltaMinimum, holding.PreferredDeltaMaximum, holding.MinimumCcos,
        holding.MinimumContractScore, holding.MinimumPremium, holding.MinimumAnnualizedYield);

    public static IndicatorContext MapIndicators(IndicatorSnapshot snapshot) => new(snapshot.Symbol,
        snapshot.AsOfDate, snapshot.Iv30, snapshot.IvPercentile, snapshot.RealizedVolatility30, snapshot.Rsi14,
        snapshot.BollingerPercentB, snapshot.BollingerBandwidth, snapshot.Sma20, snapshot.Sma50, snapshot.Sma200,
        snapshot.MacdHistogram, snapshot.ResistancePrice, snapshot.DistanceToResistancePercent,
        snapshot.ResistanceTouchCount, snapshot.ResistanceAgeTradingDays, snapshot.ResistanceUnavailableReason,
        snapshot.MarketRegime, snapshot.SectorRegime, snapshot.IndicatorCalculationVersion,
        snapshot.ConfigurationVersion, snapshot.CalculatedAt)
    {
        IvRank = snapshot.IvRank
    };

    public static async Task<EarningsContext> ResolveEarningsAsync(Holding holding,
        DateTimeOffset evaluationTimestampUtc, IEarningsDateSource earningsDateSource,
        CancellationToken cancellationToken)
    {
        if (holding.AssetType == AssetType.ExchangeTradedFund)
            return new EarningsContext(AvailabilityStatus.NotApplicable, null);

        var date = await earningsDateSource.GetNextEarningsDateAsync(holding.Symbol,
            evaluationTimestampUtc, cancellationToken);
        var evaluationDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(evaluationTimestampUtc, NewYork).DateTime);
        return date is not null && date >= evaluationDate
            ? new EarningsContext(AvailabilityStatus.Available, date)
            : new EarningsContext(AvailabilityStatus.Unavailable, null);
    }
}

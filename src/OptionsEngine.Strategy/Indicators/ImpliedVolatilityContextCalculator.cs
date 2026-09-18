namespace OptionsEngine.Strategy.Indicators;

/// <summary>Pure provider-independent IV30, IV Rank, and IV Percentile calculations.</summary>
public sealed class ImpliedVolatilityContextCalculator
{
    public ImpliedVolatilityContext Calculate(ImpliedVolatilityCalculationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Symbol);
        ArgumentNullException.ThrowIfNull(request.OptionChains);
        ArgumentNullException.ThrowIfNull(request.HistoricalIv30);
        ArgumentNullException.ThrowIfNull(request.Configuration);
        ArgumentNullException.ThrowIfNull(request.IndicatorCalculationVersion);
        if (request.CalculatedAt.Offset != TimeSpan.Zero) throw new ArgumentException("CalculatedAt must be UTC.", nameof(request));
        request.Configuration.Validate();

        var settings = request.Configuration.ImpliedVolatility;
        var asOfChains = request.OptionChains
            .Where(chain => string.Equals(chain.UnderlyingSymbol, request.Symbol, StringComparison.OrdinalIgnoreCase))
            .Where(chain => chain.Expiration.DayNumber > request.AsOfDate.DayNumber)
            .Where(chain => DateOnly.FromDateTime(chain.ObservedAt.UtcDateTime) <= request.AsOfDate && chain.ObservedAt <= request.CalculatedAt)
            .ToArray();

        var expirationIvs = asOfChains
            .Select(chain => (Chain: chain, Iv: ExpirationIv(chain, settings)))
            .Where(item => item.Iv.HasValue)
            .GroupBy(item => item.Chain.Expiration)
            .Select(group => group.OrderByDescending(item => item.Chain.ObservedAt).First())
            .Select(item => (Dte: item.Chain.Expiration.DayNumber - request.AsOfDate.DayNumber, Iv: item.Iv!.Value))
            .ToArray();

        var exact = expirationIvs.SingleOrDefault(item => item.Dte == settings.TargetDteCalendarDays);
        double? current = exact.Dte == settings.TargetDteCalendarDays ? exact.Iv : null;
        if (!current.HasValue)
        {
            var near = expirationIvs.Where(item => item.Dte < settings.TargetDteCalendarDays).OrderByDescending(item => item.Dte).FirstOrDefault();
            var far = expirationIvs.Where(item => item.Dte > settings.TargetDteCalendarDays).OrderBy(item => item.Dte).FirstOrDefault();
            if (near.Dte > 0 && far.Dte > 0)
                current = near.Iv + ((double)(settings.TargetDteCalendarDays - near.Dte) / (far.Dte - near.Dte)) * (far.Iv - near.Iv);
        }

        var iv30 = current.HasValue ? IndicatorValue<double>.Available(current.Value) : IndicatorValue<double>.InsufficientData();
        Iv30UnavailableReason? reason = current.HasValue ? null : asOfChains.Length == 0
            ? Iv30UnavailableReason.NoEligibleSnapshot
            : expirationIvs.Length == 0 ? Iv30UnavailableReason.NoEligibleAtmPair
            : Iv30UnavailableReason.NoExactOrBracketingExpirations;

        var rank = IndicatorValue<double>.InsufficientData();
        var percentile = IndicatorValue<double>.InsufficientData();
        if (current.HasValue)
        {
            // Replace any supplied same-day value with the IV30 calculated from the current snapshots.
            var history = request.HistoricalIv30
                .Where(item => string.Equals(item.Symbol, request.Symbol, StringComparison.OrdinalIgnoreCase))
                .Where(item => item.IndicatorCalculationVersion == request.IndicatorCalculationVersion && item.ConfigurationVersion == request.Configuration.Version)
                .Where(item => item.AsOfDate < request.AsOfDate && item.Iv30.Status == IndicatorValueStatus.Available)
                .Where(item => item.Iv30.Value is { } value && double.IsFinite(value) && value >= 0)
                .OrderByDescending(item => item.AsOfDate)
                .Take(settings.HistoricalLookbackValidObservations - 1)
                .Select(item => item.Iv30.Value!.Value)
                .Append(current.Value)
                .ToArray();

            if (history.Length >= settings.MinimumHistoricalValidObservations)
            {
                var minimum = history.Min();
                var maximum = history.Max();
                if (maximum != minimum)
                    rank = IndicatorValue<double>.Available((current.Value - minimum) / (maximum - minimum) * 100);
                percentile = IndicatorValue<double>.Available(100d * history.Count(value => value < current.Value) / history.Length);
            }
        }

        return new ImpliedVolatilityContext(request.Symbol, request.AsOfDate, iv30, rank, percentile, reason,
            request.IndicatorCalculationVersion, request.Configuration.Version, request.CalculatedAt);
    }

    private static double? ExpirationIv(IvOptionChainObservation chain, ImpliedVolatilityConfiguration settings)
    {
        ArgumentNullException.ThrowIfNull(chain.Contracts);
        var prices = chain.Contracts.Select(contract => contract.UnderlyingPrice).Where(price => price.HasValue).Distinct().ToArray();
        if (prices.Length != 1 || prices[0] is not { } underlyingPrice || underlyingPrice <= 0) return null;

        var strike = chain.Contracts.Select(contract => contract.Strike).Distinct()
            .OrderBy(value => Math.Abs(value - underlyingPrice)).ThenBy(value => value).FirstOrDefault();
        if (chain.Contracts.Count == 0 || Math.Abs(strike - underlyingPrice) / underlyingPrice > settings.MaxAtmStrikeDistanceRatio)
            return null;

        var atStrike = chain.Contracts.Where(contract => contract.Strike == strike).ToArray();
        var calls = atStrike.Where(contract => contract.OptionType == IvOptionType.Call).ToArray();
        var puts = atStrike.Where(contract => contract.OptionType == IvOptionType.Put).ToArray();
        if (calls.Length != 1 || puts.Length != 1 || calls[0].ImpliedVolatility is not { } callIv ||
            puts[0].ImpliedVolatility is not { } putIv || !double.IsFinite(callIv) || !double.IsFinite(putIv) || callIv < 0 || putIv < 0)
            return null;

        return (callIv + putIv) / 2;
    }
}

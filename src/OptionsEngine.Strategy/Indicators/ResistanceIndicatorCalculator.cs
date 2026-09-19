namespace OptionsEngine.Strategy.Indicators;

public sealed record ResistanceCluster(
    decimal RepresentativePrice,
    int TouchCount,
    DateOnly LastTouchDate,
    decimal MinimumPrice,
    decimal MaximumPrice);

public sealed record ResistanceContext(
    string Symbol,
    DateOnly AsOfDate,
    IReadOnlyList<ResistanceCluster> Clusters,
    IndicatorValue<decimal> ResistancePrice,
    IndicatorValue<decimal> DistanceToResistance,
    IndicatorValue<double> DistanceToResistancePercent,
    IndicatorValue<int> ResistanceTouchCount,
    IndicatorValue<DateOnly> ResistanceLastTouchDate,
    IndicatorValue<int> ResistanceAgeTradingDays,
    ResistanceUnavailableReason? ResistanceUnavailableReason,
    IndicatorCalculationVersion IndicatorCalculationVersion,
    ConfigurationVersion ConfigurationVersion,
    DateTimeOffset CalculatedAt)
{
    public IndicatorSnapshot ApplyTo(IndicatorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!string.Equals(snapshot.Symbol, Symbol, StringComparison.OrdinalIgnoreCase) || snapshot.AsOfDate != AsOfDate ||
            snapshot.IndicatorCalculationVersion != IndicatorCalculationVersion || snapshot.ConfigurationVersion != ConfigurationVersion)
            throw new ArgumentException("Resistance context and indicator snapshot identity must match.", nameof(snapshot));
        return snapshot with
        {
            ResistancePrice = ResistancePrice,
            DistanceToResistance = DistanceToResistance,
            DistanceToResistancePercent = DistanceToResistancePercent,
            ResistanceTouchCount = ResistanceTouchCount,
            ResistanceLastTouchDate = ResistanceLastTouchDate,
            ResistanceAgeTradingDays = ResistanceAgeTradingDays,
            ResistanceUnavailableReason = ResistanceUnavailableReason
        };
    }
}

/// <summary>Pure confirmed swing-high and complete-linkage resistance calculation.</summary>
public sealed class ResistanceIndicatorCalculator
{
    public ResistanceContext Calculate(IndicatorCalculationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Also validates request identity, UTC calculation time, and the full versioned configuration.
        var atr = new CoreTechnicalIndicatorCalculator().Calculate(request).Atr14;
        var settings = request.Configuration.Resistance;
        var observations = request.Observations
            .Where(x => string.Equals(x.Symbol, request.Symbol, StringComparison.OrdinalIgnoreCase) && x.TradingDate <= request.AsOfDate)
            .OrderBy(x => x.TradingDate)
            .ToArray();
        var unavailable = IndicatorValue<decimal>.InsufficientData();
        var unavailablePercent = IndicatorValue<double>.InsufficientData();
        var unavailableCount = IndicatorValue<int>.InsufficientData();
        var unavailableDate = IndicatorValue<DateOnly>.InsufficientData();

        ResistanceContext Result(IReadOnlyList<ResistanceCluster> clusters, ResistanceUnavailableReason? unavailableReason = null,
            decimal? price = null, int? count = null, DateOnly? lastTouch = null, int? age = null)
        {
            var distance = price.HasValue && observations[^1].Close is { } close ? price.Value - close : (decimal?)null;
            return new ResistanceContext(request.Symbol, request.AsOfDate, clusters,
                price.HasValue ? IndicatorValue<decimal>.Available(price.Value) : unavailable,
                distance.HasValue ? IndicatorValue<decimal>.Available(distance.Value) : unavailable,
                distance.HasValue && observations[^1].Close is { } currentClose && currentClose != 0
                    ? IndicatorValue<double>.Available((double)(distance.Value / currentClose)) : unavailablePercent,
                count.HasValue ? IndicatorValue<int>.Available(count.Value) : unavailableCount,
                lastTouch.HasValue ? IndicatorValue<DateOnly>.Available(lastTouch.Value) : unavailableDate,
                age.HasValue ? IndicatorValue<int>.Available(age.Value) : unavailableCount,
                unavailableReason,
                request.IndicatorCalculationVersion, request.Configuration.Version, request.CalculatedAt);
        }

        if (observations.Length == 0) return new ResistanceContext(request.Symbol, request.AsOfDate, [], unavailable, unavailable,
            unavailablePercent, unavailableCount, unavailableDate, unavailableCount,
            ResistanceUnavailableReason.InsufficientData,
            request.IndicatorCalculationVersion, request.Configuration.Version, request.CalculatedAt);
        if (observations.Length < settings.LookbackTradingDays ||
            atr.Status != IndicatorValueStatus.Available || atr.Value is not { } atrValue || !double.IsFinite(atrValue) ||
            observations[^1].Close is null || observations.Any(x => x.High is null)) return Result([], ResistanceUnavailableReason.InsufficientData);

        var threshold = (decimal)(settings.ClusterDistanceAtrMultiplier * atrValue);
        var firstCandidate = Math.Max(0, observations.Length - settings.LookbackTradingDays);
        var highs = new List<(decimal Price, DateOnly Date, int Index)>();
        for (var index = firstCandidate; index < observations.Length; index++)
        {
            if (index < settings.SwingWindow || index + settings.SwingWindow >= observations.Length ||
                observations[index].High is not { } high) continue;
            var confirmed = true;
            for (var offset = 1; offset <= settings.SwingWindow; offset++)
            {
                // Missing highs in either required confirmation window cannot confirm a touch.
                if (observations[index - offset].High is not { } before || high <= before ||
                    observations[index + offset].High is not { } after || high < after)
                {
                    confirmed = false;
                    break;
                }
            }
            if (confirmed) highs.Add((high, observations[index].TradingDate, index));
        }

        var sorted = highs.OrderBy(x => x.Price).ThenBy(x => x.Date).ToArray();
        var groups = new List<List<(decimal Price, DateOnly Date, int Index)>>();
        foreach (var high in sorted)
        {
            if (groups.Count == 0 || high.Price - groups[^1][0].Price > threshold)
                groups.Add([]);
            groups[^1].Add(high);
        }

        var clusters = groups.Select(group => new ResistanceCluster(
            group.Average(x => x.Price), group.Count, group.Max(x => x.Date), group[0].Price, group[^1].Price)).ToArray();
        var clusterView = Array.AsReadOnly(clusters);
        var current = observations[^1].Close!.Value;
        var selected = clusters.Where(x => x.TouchCount >= settings.MinimumResistanceTouches && x.RepresentativePrice > current)
            .OrderBy(x => x.RepresentativePrice).FirstOrDefault();
        if (selected is null) return Result(clusterView, ResistanceUnavailableReason.NoQualifiedResistance);

        var touchIndex = Array.FindLastIndex(observations, x => x.TradingDate == selected.LastTouchDate);
        return Result(clusterView, null, selected.RepresentativePrice, selected.TouchCount, selected.LastTouchDate,
            observations.Length - 1 - touchIndex);
    }
}

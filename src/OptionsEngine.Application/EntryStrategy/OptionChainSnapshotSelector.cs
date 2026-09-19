using OptionsEngine.MarketData.Models;

namespace OptionsEngine.Application.EntryStrategy;

/// <summary>Selects one complete normalized chain snapshot per expiration at a reproducible evaluation cutoff.</summary>
public static class OptionChainSnapshotSelector
{
    public static IReadOnlyList<OptionChain> Select(IReadOnlyList<OptionChain> observations,
        DateTimeOffset evaluationTimestampUtc)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (evaluationTimestampUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Evaluation timestamp must be UTC.", nameof(evaluationTimestampUtc));
        return observations
            .Where(x => x.Timestamp <= evaluationTimestampUtc)
            .GroupBy(x => x.Expiration)
            .Select(SelectLatestCompleteObservation)
            .OrderBy(x => x.Expiration)
            .ToArray();
    }

    private static OptionChain SelectLatestCompleteObservation(IGrouping<DateOnly, OptionChain> expiration)
    {
        var latestTimestamp = expiration.Max(x => x.Timestamp);
        var candidates = expiration.Where(x => x.Timestamp == latestTimestamp).ToArray();
        if (candidates.Length != 1)
            throw new InvalidOperationException($"More than one complete chain observation exists for expiration {expiration.Key:yyyy-MM-dd} at {latestTimestamp:O}.");

        var selected = candidates[0];
        if (selected.Contracts.Any(x => x.Timestamp != selected.Timestamp || x.Expiration != selected.Expiration))
            throw new InvalidOperationException("A chain observation contains contracts from another timestamp or expiration.");
        return selected with { Contracts = selected.Contracts.OrderBy(x => x.OptionSymbol, StringComparer.Ordinal).ToArray() };
    }
}

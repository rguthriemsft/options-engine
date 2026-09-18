using OptionsEngine.MarketData.Models;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Application.Indicators;

/// <summary>Historical normalized inputs and canonical calculated results needed by Phase 3 orchestration.</summary>
public interface IIndicatorDataRepository
{
    Task<DateOnly?> GetLatestPriceObservationDateAsync(string symbol, string provider, DateOnly onOrBefore,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IndicatorPriceObservation>> GetPricesThroughAsync(string symbol, string provider, DateOnly asOfDate, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OptionChain>> GetOptionChainsThroughAsync(string symbol, string provider, DateOnly asOfDate, DateTimeOffset calculatedAt, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HistoricalIv30Observation>> GetPriorValidIv30Async(string symbol, DateOnly asOfDate,
        IndicatorCalculationVersion calculationVersion, ConfigurationVersion configurationVersion, int limit, CancellationToken cancellationToken = default);
    Task<IndicatorSnapshot?> GetSnapshotAsync(string symbol, DateOnly asOfDate,
        IndicatorCalculationVersion calculationVersion, ConfigurationVersion configurationVersion, CancellationToken cancellationToken = default);
    Task UpsertSnapshotAsync(IndicatorSnapshot snapshot, CancellationToken cancellationToken = default);
}

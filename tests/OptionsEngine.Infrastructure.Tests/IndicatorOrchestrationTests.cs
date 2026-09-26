using OptionsEngine.Application.Indicators;
using OptionsEngine.MarketData;
using OptionsEngine.MarketData.Models;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Infrastructure.Tests;

public sealed class IndicatorOrchestrationTests
{
    private static readonly DateOnly AsOf = new(2026, 9, 16);
    private static readonly DateTimeOffset CalculatedAt = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly IndicatorCalculationVersion Version = new("3.0.0");

    [Fact]
    public async Task ComposesAllPhaseThreeContextsAndPersistsCanonicalSnapshot()
    {
        var repository = Fixture();
        var service = Service(repository);
        var snapshot = await service.CalculateAndPersistAsync(" msft ", AsOf, Configuration(), Version, CalculatedAt);

        Assert.Equal(("MSFT", AsOf, Version, new ConfigurationVersion(1)),
            (snapshot.Symbol, snapshot.AsOfDate, snapshot.IndicatorCalculationVersion, snapshot.ConfigurationVersion));
        Assert.Equal(IndicatorValueStatus.Available, snapshot.Sma200.Status);
        Assert.Equal(IndicatorValueStatus.Available, snapshot.Atr14.Status);
        Assert.Equal(0.3, snapshot.Iv30.Value!.Value, 12);
        Assert.Equal(MarketRegime.Bullish, snapshot.MarketRegime);
        Assert.Equal(MarketRegime.Bearish, snapshot.SectorRegime);
        Assert.Equal(IndicatorValueStatus.InsufficientData, snapshot.ResistancePrice.Status);
        Assert.Equal(snapshot, await service.GetSnapshotAsync("mSfT", AsOf, Version, new ConfigurationVersion(1)));
        Assert.Contains(repository.PriceRequests, request => request.Symbol == "SPY" && request.AsOfDate == AsOf);
        Assert.Contains(repository.PriceRequests, request => request.Symbol == "XLK" && request.AsOfDate == AsOf);
        Assert.Equal(251, repository.IvHistoryLimit);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task CalculateComposesTheSameSnapshotWithoutPersistingCanonicalState()
    {
        var repository = Fixture();
        var service = Service(repository);

        var transient = await service.CalculateAsync(" msft ", AsOf, Configuration(), Version, CalculatedAt);

        Assert.Equal(("MSFT", AsOf, Version, new ConfigurationVersion(1), CalculatedAt),
            (transient.Symbol, transient.AsOfDate, transient.IndicatorCalculationVersion,
                transient.ConfigurationVersion, transient.CalculatedAt));
        Assert.Equal(IndicatorValueStatus.Available, transient.Sma200.Status);
        Assert.Equal(0.3, transient.Iv30.Value!.Value, 12);
        Assert.Equal(MarketRegime.Bullish, transient.MarketRegime);
        Assert.Equal(MarketRegime.Bearish, transient.SectorRegime);
        Assert.Equal(0, repository.SaveCount);
        Assert.Empty(repository.Snapshots);
    }

    [Fact]
    public async Task RetryReplacesSameIdentityAndFutureInputsDoNotChangeHistoricalFacts()
    {
        var repository = Fixture();
        var service = Service(repository);
        var first = await service.CalculateAndPersistAsync("MSFT", AsOf, Configuration(), Version, CalculatedAt);
        repository.Prices["MSFT"].Add(Bar("MSFT", AsOf.AddDays(1), 999m));
        repository.Prices["SPY"].Add(Bar("SPY", AsOf.AddDays(1), 1m));
        repository.Prices["XLK"].Add(Bar("XLK", AsOf.AddDays(1), 999m));
        repository.Chains.Add(Chain(AsOf.AddDays(1), 0.9));
        var second = await service.CalculateAndPersistAsync("MSFT", AsOf, Configuration(), Version, CalculatedAt);

        Assert.Equal(first, second);
        Assert.Single(repository.Snapshots);
        Assert.Equal(2, repository.SaveCount);
    }

    [Fact]
    public async Task CorrectedEligibleInputChangesCurrentCanonicalFactsWithoutChangingIdentity()
    {
        var repository = Fixture();
        var service = Service(repository);
        var first = await service.CalculateAndPersistAsync("MSFT", AsOf, Configuration(), Version, CalculatedAt);
        var bars = repository.Prices["MSFT"];
        bars[^1] = Bar("MSFT", AsOf, 400m);
        var secondAt = CalculatedAt.AddMinutes(5);
        var second = await service.CalculateAndPersistAsync("MSFT", AsOf, Configuration(), Version, secondAt);

        Assert.NotEqual(first.Sma20, second.Sma20);
        Assert.Equal(first.Symbol, second.Symbol);
        Assert.Equal(first.AsOfDate, second.AsOfDate);
        Assert.Equal(first.IndicatorCalculationVersion, second.IndicatorCalculationVersion);
        Assert.Equal(first.ConfigurationVersion, second.ConfigurationVersion);
        Assert.Equal(secondAt, second.CalculatedAt);
        Assert.Single(repository.Snapshots);
        Assert.Equal(second, await service.GetSnapshotAsync("MSFT", AsOf, Version, new ConfigurationVersion(1)));
    }

    [Fact]
    public async Task VersionMatchedHistoricalIv30FeedsRankAndPercentile()
    {
        var repository = Fixture();
        repository.IvHistory.AddRange(Enumerable.Range(1, 125).Select(daysAgo =>
            new HistoricalIv30Observation("MSFT", AsOf.AddDays(-daysAgo), IndicatorValue<double>.Available(0.2),
                Version, new ConfigurationVersion(1))));
        repository.IvHistory.Add(new HistoricalIv30Observation("MSFT", AsOf.AddDays(-1),
            IndicatorValue<double>.Available(0.9), new IndicatorCalculationVersion("other"), new ConfigurationVersion(1)));
        repository.IvHistory.Add(new HistoricalIv30Observation("MSFT", AsOf.AddDays(-1),
            IndicatorValue<double>.Available(0.9), Version, new ConfigurationVersion(2)));
        repository.IvHistory.Add(new HistoricalIv30Observation("MSFT", AsOf.AddDays(1),
            IndicatorValue<double>.Available(0.9), Version, new ConfigurationVersion(1)));

        var snapshot = await Service(repository).CalculateAndPersistAsync("MSFT", AsOf, Configuration(), Version, CalculatedAt);

        Assert.Equal(100d, snapshot.IvRank.Value!.Value, 12);
        Assert.Equal(100d * 125 / 126, snapshot.IvPercentile.Value!.Value, 12);
        Assert.Equal(251, repository.IvHistoryLimit);
    }

    [Fact]
    public async Task MissingSectorMappingAndInsufficientHistoryRemainExplicit()
    {
        var repository = Fixture();
        repository.Prices["MSFT"] = [Bar("MSFT", AsOf, 100m)];
        var service = Service(repository, new IndicatorOrchestrationConfiguration());
        var snapshot = await service.CalculateAndPersistAsync("MSFT", AsOf, Configuration(), Version, CalculatedAt);
        Assert.Equal(MarketRegime.InsufficientData, snapshot.SectorRegime);
        Assert.Equal(IndicatorValueStatus.InsufficientData, snapshot.Sma200.Status);
        Assert.Equal(IndicatorValueStatus.InsufficientData, snapshot.ResistancePrice.Status);
    }

    [Fact]
    public async Task CancellationTokenIsForwardedToPersistenceBoundary()
    {
        var repository = Fixture();
        using var source = new CancellationTokenSource();
        await Service(repository).CalculateAndPersistAsync("MSFT", AsOf, Configuration(), Version, CalculatedAt, source.Token);
        Assert.Equal(source.Token, repository.LastToken);
    }

    private static IndicatorOrchestrationService Service(FakeRepository repository,
        IndicatorOrchestrationConfiguration? configuration = null) => new(repository, new Provider(),
            configuration ?? new IndicatorOrchestrationConfiguration
            {
                SectorBenchmarks = new Dictionary<string, string> { ["MSFT"] = "XLK" }
            });

    private static IndicatorConfiguration Configuration() => new() { Version = new ConfigurationVersion(1) };

    private static FakeRepository Fixture()
    {
        var repository = new FakeRepository();
        repository.Prices["MSFT"] = Bars("MSFT", 230, 1);
        repository.Prices["SPY"] = Bars("SPY", 230, 1);
        repository.Prices["XLK"] = Bars("XLK", 230, -1);
        repository.Chains.Add(Chain(AsOf, 0.3));
        return repository;
    }

    private static List<IndicatorPriceObservation> Bars(string symbol, int count, int slope) =>
        Enumerable.Range(0, count).Select(index => Bar(symbol, AsOf.AddDays(index - count + 1),
            300m + (slope * index))).ToList();

    private static IndicatorPriceObservation Bar(string symbol, DateOnly date, decimal close) =>
        new(symbol, date, close, close + 1m, close - 1m, close, 1000);

    private static OptionChain Chain(DateOnly observedDate, double iv)
    {
        var timestamp = new DateTimeOffset(observedDate, new TimeOnly(10, 0), TimeSpan.Zero);
        var expiration = AsOf.AddDays(30);
        return new OptionChain("MSFT", expiration, timestamp,
            [new OptionContractSnapshot("MSFT-C", "MSFT", timestamp, expiration, 529m, OptionType.Call,
                null, null, null, null, null, iv, null, null, null, null, 529m, "Tradier"),
             new OptionContractSnapshot("MSFT-P", "MSFT", timestamp, expiration, 529m, OptionType.Put,
                null, null, null, null, null, iv, null, null, null, null, 529m, "Tradier")], "Tradier");
    }

    private sealed class FakeRepository : IIndicatorDataRepository
    {
        public Dictionary<string, List<IndicatorPriceObservation>> Prices { get; } = new();
        public List<OptionChain> Chains { get; } = [];
        public List<HistoricalIv30Observation> IvHistory { get; } = [];
        public Dictionary<(string, DateOnly, string, int), IndicatorSnapshot> Snapshots { get; } = new();
        public List<(string Symbol, DateOnly AsOfDate)> PriceRequests { get; } = [];
        public int IvHistoryLimit { get; private set; }
        public int SaveCount { get; private set; }
        public CancellationToken LastToken { get; private set; }

        public Task<DateOnly?> GetLatestPriceObservationDateAsync(string symbol, string provider, DateOnly onOrBefore,
            CancellationToken cancellationToken = default) => Task.FromResult<DateOnly?>(Prices.GetValueOrDefault(symbol)?
                .Where(x => x.TradingDate <= onOrBefore).Select(x => (DateOnly?)x.TradingDate).Max());

        public Task<IReadOnlyList<IndicatorPriceObservation>> GetPricesThroughAsync(string symbol, string provider, DateOnly asOfDate,
            CancellationToken cancellationToken = default)
        {
            PriceRequests.Add((symbol, asOfDate));
            LastToken = cancellationToken;
            return Task.FromResult<IReadOnlyList<IndicatorPriceObservation>>(Prices.GetValueOrDefault(symbol) ?? []);
        }

        public Task<IReadOnlyList<OptionChain>> GetOptionChainsThroughAsync(string symbol, string provider, DateOnly asOfDate,
            DateTimeOffset calculatedAt, CancellationToken cancellationToken = default)
        {
            LastToken = cancellationToken;
            return Task.FromResult<IReadOnlyList<OptionChain>>(Chains);
        }

        public Task<IReadOnlyList<HistoricalIv30Observation>> GetPriorValidIv30Async(string symbol, DateOnly asOfDate,
            IndicatorCalculationVersion calculationVersion, ConfigurationVersion configurationVersion, int limit,
            CancellationToken cancellationToken = default)
        {
            IvHistoryLimit = limit;
            LastToken = cancellationToken;
            return Task.FromResult<IReadOnlyList<HistoricalIv30Observation>>(IvHistory
                .Where(item => item.Symbol == symbol && item.AsOfDate < asOfDate &&
                    item.IndicatorCalculationVersion == calculationVersion && item.ConfigurationVersion == configurationVersion &&
                    item.Iv30.Status == IndicatorValueStatus.Available)
                .OrderByDescending(item => item.AsOfDate).Take(limit).ToArray());
        }

        public Task<IndicatorSnapshot?> GetSnapshotAsync(string symbol, DateOnly asOfDate,
            IndicatorCalculationVersion calculationVersion, ConfigurationVersion configurationVersion,
            CancellationToken cancellationToken = default)
        {
            LastToken = cancellationToken;
            Snapshots.TryGetValue((symbol, asOfDate, calculationVersion.Value, configurationVersion.Value), out var value);
            return Task.FromResult(value);
        }

        public Task UpsertSnapshotAsync(IndicatorSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            LastToken = cancellationToken;
            SaveCount++;
            Snapshots[(snapshot.Symbol, snapshot.AsOfDate, snapshot.IndicatorCalculationVersion.Value,
                snapshot.ConfigurationVersion.Value)] = snapshot;
            return Task.CompletedTask;
        }
    }

    private sealed class Provider : IMarketDataProvider
    {
        public string ProviderName => "Tradier";
        public Task<MarketQuote> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<HistoricalBar>> GetHistoricalPricesAsync(string symbol, DateOnly start, DateOnly end,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DateOnly>> GetOptionExpirationsAsync(string symbol,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OptionChain> GetOptionChainAsync(string symbol, DateOnly expiration,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

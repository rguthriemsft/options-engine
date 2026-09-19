using OptionsEngine.Application.EntryStrategy;
using OptionsEngine.Application.Indicators;
using OptionsEngine.Domain.Accounts;
using OptionsEngine.MarketData;
using OptionsEngine.MarketData.Models;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Infrastructure.Tests;

public sealed class EntryStrategyEvaluationOrchestratorTests
{
    private static readonly Guid HoldingId = Guid.Parse("1B539C36-CA1A-4FB0-B34A-84C821258AAB");
    private static readonly DateOnly AsOfDate = new(2026, 9, 17);
    private static readonly DateTimeOffset EvaluationAt = new(2026, 9, 18, 15, 0, 0, TimeSpan.Zero);
    private static readonly IndicatorCalculationVersion IndicatorVersion = new("3.0.0");

    [Fact]
    public void ChainSelectionUsesLatestCompleteObservationAtOrBeforeCutoffWithoutMixing()
    {
        var expiration = new DateOnly(2026, 10, 16);
        var first = Chain(expiration, EvaluationAt.AddMinutes(-10), [Contract("A", expiration, EvaluationAt.AddMinutes(-10))]);
        var latest = Chain(expiration, EvaluationAt.AddMinutes(-5), [Contract("C", expiration, EvaluationAt.AddMinutes(-5)), Contract("D", expiration, EvaluationAt.AddMinutes(-5))]);
        var future = Chain(expiration, EvaluationAt.AddMinutes(1), [Contract("F", expiration, EvaluationAt.AddMinutes(1))]);

        var selected = OptionChainSnapshotSelector.Select([future, first, latest], EvaluationAt);

        var chain = Assert.Single(selected);
        Assert.Equal(latest.Timestamp, chain.Timestamp);
        Assert.Equal(new[] { "C", "D" }, chain.Contracts.Select(x => x.OptionSymbol));
    }

    [Fact]
    public void ChainSelectionAcceptsCutoffTimestampAndNewerEmptySupersedesPopulated()
    {
        var firstExpiration = new DateOnly(2026, 10, 16);
        var secondExpiration = new DateOnly(2026, 10, 23);
        var populated = Chain(firstExpiration, EvaluationAt.AddMinutes(-5), [Contract("OLD", firstExpiration, EvaluationAt.AddMinutes(-5))]);
        var empty = Chain(firstExpiration, EvaluationAt, []);
        var exact = Chain(secondExpiration, EvaluationAt, [Contract("EXACT", secondExpiration, EvaluationAt)]);

        var selected = OptionChainSnapshotSelector.Select([populated, exact, empty], EvaluationAt);

        Assert.Equal(new[] { firstExpiration, secondExpiration }, selected.Select(x => x.Expiration));
        Assert.Empty(selected[0].Contracts);
        Assert.Equal(EvaluationAt, selected[0].Timestamp);
        Assert.Equal("EXACT", Assert.Single(selected[1].Contracts).OptionSymbol);
    }

    [Fact]
    public void ChainSelectionIsIndependentOfEnumerationOrderAndRejectsAmbiguousIdentity()
    {
        var expiration = new DateOnly(2026, 10, 16);
        var earlier = Chain(expiration, EvaluationAt.AddMinutes(-10), [Contract("EARLIER", expiration, EvaluationAt.AddMinutes(-10))]);
        var later = Chain(expiration, EvaluationAt.AddMinutes(-5), [Contract("LATER", expiration, EvaluationAt.AddMinutes(-5))]);
        var forward = OptionChainSnapshotSelector.Select([earlier, later], EvaluationAt);
        var reverse = OptionChainSnapshotSelector.Select([later, earlier], EvaluationAt);
        Assert.Equal(forward.Select(x => (x.Expiration, x.Timestamp, x.Contracts.Select(c => c.OptionSymbol))),
            reverse.Select(x => (x.Expiration, x.Timestamp, x.Contracts.Select(c => c.OptionSymbol))));

        var duplicate = later with { Contracts = [Contract("OTHER", expiration, later.Timestamp)] };
        Assert.Throws<InvalidOperationException>(() => OptionChainSnapshotSelector.Select([later, duplicate], EvaluationAt));
    }

    [Fact]
    public async Task AssemblesReproducibleBundleAndInvokesActualStrategy()
    {
        var fixture = Fixture();
        var firstExpiration = new DateOnly(2026, 10, 16);
        var secondExpiration = new DateOnly(2026, 10, 23);
        var older = Chain(firstExpiration, EvaluationAt.AddMinutes(-10), [Contract("OLD", firstExpiration, EvaluationAt.AddMinutes(-10))]);
        var selectedFirst = Chain(firstExpiration, EvaluationAt.AddMinutes(-5), [Contract("PREFERRED", firstExpiration, EvaluationAt.AddMinutes(-5))]);
        var selectedSecond = Chain(secondExpiration, EvaluationAt.AddMinutes(-2), [Contract("ALTERNATE", secondExpiration, EvaluationAt.AddMinutes(-2)) with { Delta = .20, Strike = 108m, Theta = -.08 }]);
        fixture.MarketData.Chains = [older, selectedSecond, selectedFirst];
        fixture.Earnings.NextDate = new DateOnly(2027, 1, 1);

        var bundle = await fixture.Orchestrator.EvaluateAsync(HoldingId, EvaluationAt);

        Assert.Equal(EvaluationAt, bundle.Context.EvaluationTimestampUtc);
        Assert.Equal(AsOfDate, bundle.Context.IndicatorAsOfDate);
        Assert.Equal((IndicatorVersion, new ConfigurationVersion(1), new StrategyVersion("4.0.0")),
            (bundle.Context.Indicators.IndicatorCalculationVersion, bundle.Context.Configuration.Version, bundle.Context.StrategyVersion));
        Assert.Equal((fixture.Holdings.Holding!.HoldingId, "MSFT", .25, .12, .18, 70d, 80d, .1m, .1),
            (bundle.Context.Holding.HoldingId, bundle.Context.Holding.Symbol, bundle.Context.Holding.MaximumInitialDelta,
             bundle.Context.Holding.PreferredDeltaMinimum, bundle.Context.Holding.PreferredDeltaMaximum, bundle.Context.Holding.MinimumCcos,
             bundle.Context.Holding.MinimumContractScore, bundle.Context.Holding.MinimumPremium, bundle.Context.Holding.MinimumAnnualizedYield));
        Assert.Equal(new[] { selectedFirst.Timestamp, selectedSecond.Timestamp }, bundle.SelectedOptionChains.Select(x => x.Timestamp));
        Assert.DoesNotContain(bundle.SelectedContracts, x => x.OptionSymbol == "OLD");
        Assert.Equal(new DateOnly(2027, 1, 1), bundle.Context.Earnings.NextEarningsDate);
        Assert.Equal(AvailabilityStatus.Available, bundle.Context.Earnings.Status);
        Assert.True(bundle.StrategyResult.EntryCandidateExists);
        Assert.Equal("PREFERRED", bundle.StrategyResult.PreferredInitialOptionSymbol);
        Assert.Equal(1, Assert.Single(bundle.StrategyResult.Contracts, x => x.Contract.OptionSymbol == "PREFERRED").Rank);
        Assert.Equal(2, Assert.Single(bundle.StrategyResult.Contracts, x => x.Contract.OptionSymbol == "ALTERNATE").Rank);
    }

    [Fact]
    public async Task DisabledAndMissingHoldingDoNotInvokeIndicatorOrStrategyWork()
    {
        var fixture = Fixture();
        fixture.Holdings.Holding = null;
        await Assert.ThrowsAsync<HoldingNotFoundException>(() => fixture.Orchestrator.EvaluateAsync(HoldingId, EvaluationAt));
        Assert.Equal(0, fixture.Repository.IndicatorLookupCount);

        fixture.Holdings.Holding = Holding(isEnabled: false);
        await Assert.ThrowsAsync<HoldingDisabledException>(() => fixture.Orchestrator.EvaluateAsync(HoldingId, EvaluationAt));
        Assert.Equal(0, fixture.Repository.IndicatorLookupCount);
    }

    [Fact]
    public async Task IndicatorStatesAndUnavailableValuesAreMappedWithoutSubstitution()
    {
        var fixture = Fixture();
        fixture.Repository.Snapshot = Snapshot() with
        {
            Rsi14 = IndicatorValue<double>.InsufficientData(),
            ResistanceUnavailableReason = ResistanceUnavailableReason.NoQualifiedResistance,
            ResistancePrice = IndicatorValue<decimal>.InsufficientData()
        };
        fixture.MarketData.Chains = [Chain(new DateOnly(2026, 10, 16), EvaluationAt, [Contract("NO_PRICE", new DateOnly(2026, 10, 16), EvaluationAt) with { UnderlyingPrice = null }])];

        var bundle = await fixture.Orchestrator.EvaluateAsync(HoldingId, EvaluationAt);

        Assert.Equal(IndicatorValueStatus.InsufficientData, bundle.Context.Indicators.Rsi14.Status);
        Assert.Null(bundle.Context.Indicators.Rsi14.Value);
        Assert.Equal(ResistanceUnavailableReason.NoQualifiedResistance, bundle.Context.Indicators.ResistanceUnavailableReason);
        Assert.Null(bundle.SelectedContracts[0].UnderlyingPrice);
        Assert.Equal(ScoreStatus.Unavailable, bundle.StrategyResult.Ccos.Status);
    }

    [Fact]
    public async Task OptionMappingPreservesNormalizedValuesAndMissingFields()
    {
        var fixture = Fixture();
        var expiration = new DateOnly(2026, 10, 16);
        var observedAt = EvaluationAt.AddMinutes(-1);
        var source = new OptionContractSnapshot("MSFT-MAP", "MSFT", observedAt, expiration, 105m, OptionType.Call,
            null, 2.2m, 2.1m, 123, null, .3, null, null, -.06, null, null, "FakeProvider");
        fixture.MarketData.Chains = [Chain(expiration, observedAt, [source])];

        var mapped = Assert.Single((await fixture.Orchestrator.EvaluateAsync(HoldingId, EvaluationAt)).SelectedContracts);

        Assert.Equal((source.OptionSymbol, source.UnderlyingSymbol, source.Timestamp, source.Expiration, source.Strike,
                source.Bid, source.Ask, source.OpenInterest, source.ImpliedVolatility, source.Delta, source.Theta,
                source.UnderlyingPrice, source.Last, source.Volume, source.Provider),
            (mapped.OptionSymbol, mapped.UnderlyingSymbol, mapped.ObservationTimestampUtc, mapped.Expiration, mapped.Strike,
                mapped.Bid, mapped.Ask, mapped.OpenInterest, mapped.ImpliedVolatility, mapped.Delta, mapped.Theta,
                mapped.UnderlyingPrice, mapped.Last, mapped.Volume, mapped.Provider));
        Assert.Equal(OptionContractType.Call, mapped.OptionType);
        Assert.Null(mapped.UnderlyingPrice);
        Assert.Null(mapped.Bid);
        Assert.Null(mapped.Delta);
    }

    [Theory]
    [InlineData("available")]
    [InlineData("unavailable")]
    [InlineData("etf")]
    public async Task EarningsBoundaryPreservesStockAndEtfSemantics(string scenario)
    {
        var fixture = Fixture();
        fixture.MarketData.Chains = [Chain(new DateOnly(2026, 10, 16), EvaluationAt, [Contract("CALL", new DateOnly(2026, 10, 16), EvaluationAt)])];
        if (scenario == "available") fixture.Earnings.NextDate = new DateOnly(2026, 10, 16);
        if (scenario == "unavailable") fixture.Earnings.NextDate = null;
        if (scenario == "etf") fixture.Holdings.Holding = Holding(assetType: AssetType.ExchangeTradedFund);

        var bundle = await fixture.Orchestrator.EvaluateAsync(HoldingId, EvaluationAt);
        var earningsGate = Assert.Single(bundle.StrategyResult.Contracts[0].Gates, x => x.Code == GateCode.Earnings);
        if (scenario == "available")
        {
            Assert.Equal(AvailabilityStatus.Available, bundle.Context.Earnings.Status);
            Assert.Equal(new DateOnly(2026, 10, 16), bundle.Context.Earnings.NextEarningsDate);
            Assert.Equal(RejectionReasonCode.EarningsBeforeExpiration, earningsGate.ReasonCode);
        }
        else if (scenario == "unavailable")
        {
            Assert.Equal(AvailabilityStatus.Unavailable, bundle.Context.Earnings.Status);
            Assert.Null(bundle.Context.Earnings.NextEarningsDate);
            Assert.Equal(RejectionReasonCode.InsufficientData, earningsGate.ReasonCode);
        }
        else
        {
            Assert.Equal(AvailabilityStatus.NotApplicable, bundle.Context.Earnings.Status);
            Assert.Null(bundle.Context.Earnings.NextEarningsDate);
            Assert.Equal(GateStatus.NotApplicable, earningsGate.Status);
            Assert.Equal(0, fixture.Earnings.CallCount);
        }
    }

    [Fact]
    public async Task MismatchedSnapshotConfigurationVersionIsRejected()
    {
        var fixture = Fixture();
        fixture.Repository.Snapshot = Snapshot() with { ConfigurationVersion = new ConfigurationVersion(2) };

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Orchestrator.EvaluateAsync(HoldingId, EvaluationAt));
    }

    [Fact]
    public async Task FutureCalculatedIndicatorSnapshotIsRecalculatedAtTheEvaluationCutoff()
    {
        var fixture = Fixture();
        fixture.Repository.Snapshot = Snapshot() with { CalculatedAt = EvaluationAt.AddMinutes(1) };
        fixture.MarketData.Chains = [Chain(new DateOnly(2026, 10, 16), EvaluationAt, [Contract("CALL", new DateOnly(2026, 10, 16), EvaluationAt)])];

        var bundle = await fixture.Orchestrator.EvaluateAsync(HoldingId, EvaluationAt);

        Assert.Equal(EvaluationAt, bundle.Context.Indicators.IndicatorCalculatedAtUtc);
        Assert.Equal(1, fixture.Repository.UpsertCount);
    }

    [Fact]
    public async Task IndicatorAsOfBoundaryUsesNewYorkDateThenPhaseThreeLatestTradingDateSemantics()
    {
        var fixture = Fixture();
        fixture.MarketData.Chains = [Chain(new DateOnly(2026, 10, 16), EvaluationAt, [Contract("CALL", new DateOnly(2026, 10, 16), EvaluationAt)])];
        var cutoff = new DateTimeOffset(2026, 9, 18, 1, 0, 0, TimeSpan.Zero); // Sep 17 in New York.

        await fixture.Orchestrator.EvaluateAsync(HoldingId, cutoff);

        Assert.Equal(AsOfDate, fixture.Repository.LastLatestPriceBoundary);
    }

    [Fact]
    public async Task PhaseFourChainCutoffCrossingUtcMidnightUsesTimestampRatherThanPhaseThreeDateCap()
    {
        var fixture = Fixture();
        var cutoff = new DateTimeOffset(2026, 9, 18, 1, 0, 0, TimeSpan.Zero); // Sep 17 in New York.
        var expiration = new DateOnly(2026, 10, 16);
        var eligible = Chain(expiration, new DateTimeOffset(2026, 9, 18, 0, 30, 0, TimeSpan.Zero),
            [Contract("ELIGIBLE", expiration, new DateTimeOffset(2026, 9, 18, 0, 30, 0, TimeSpan.Zero))]);
        var future = Chain(expiration, cutoff.AddMinutes(1), [Contract("FUTURE", expiration, cutoff.AddMinutes(1))]);
        fixture.MarketData.Chains = [future, eligible];

        var bundle = await fixture.Orchestrator.EvaluateAsync(HoldingId, cutoff);

        Assert.Equal(eligible.Timestamp, Assert.Single(bundle.SelectedOptionChains).Timestamp);
        Assert.Equal("ELIGIBLE", Assert.Single(bundle.SelectedContracts).OptionSymbol);
        Assert.Equal(cutoff, fixture.MarketData.LastRequest!.Value.Cutoff);
    }

    [Fact]
    public async Task PhaseFourRetrievalUsesConfiguredInclusiveExpirationWindow()
    {
        var fixture = Fixture(
            new ContractEligibilityConfiguration { MinimumDte = 20, MaximumDte = 30 },
            new ContractScoreConfiguration
            {
                DteEfficiency = new IntegerScoreTable { Bands = [new(20, 22, 5), new(23, 27, 10), new(28, 30, 8)] }
            });
        var evaluationDate = new DateOnly(2026, 9, 18);
        fixture.MarketData.Chains = new[] { 19, 20, 25, 30, 31 }.Select(dte =>
        {
            var expiration = evaluationDate.AddDays(dte);
            return Chain(expiration, EvaluationAt, [Contract($"DTE-{dte}", expiration, EvaluationAt)]);
        }).ToArray();

        var bundle = await fixture.Orchestrator.EvaluateAsync(HoldingId, EvaluationAt);

        Assert.Equal([evaluationDate.AddDays(20), evaluationDate.AddDays(25), evaluationDate.AddDays(30)],
            bundle.SelectedOptionChains.Select(x => x.Expiration));
        Assert.Equal((evaluationDate.AddDays(20), evaluationDate.AddDays(30), EvaluationAt), fixture.MarketData.LastRequest!.Value);
    }

    [Fact]
    public async Task PersistenceServiceUsesRequestedCutoffAndInjectedCalculatedAtAndInsertsOnce()
    {
        var fixture = Fixture();
        var expiration = new DateOnly(2026, 10, 16);
        fixture.MarketData.Chains = [Chain(expiration, EvaluationAt, [Contract("CALL", expiration, EvaluationAt)])];
        var repository = new CapturingEvaluationRepository();
        var calculatedAt = new DateTimeOffset(2026, 9, 18, 16, 30, 0, TimeSpan.Zero);
        var service = new EntryStrategyEvaluationPersistenceService(fixture.Orchestrator, repository,
            new FixedTimeProvider(calculatedAt));

        var persisted = await service.CreateAsync(HoldingId, EvaluationAt);

        Assert.Equal(1, repository.InsertCount);
        Assert.Equal(persisted.Evaluation.EntryStrategyEvaluationId, repository.Value!.Evaluation.EntryStrategyEvaluationId);
        Assert.Equal(EvaluationAt, persisted.Evaluation.Context.EvaluationTimestampUtc);
        Assert.Equal(calculatedAt, persisted.Evaluation.CalculatedAtUtc);
        Assert.NotEqual(persisted.Evaluation.Context.EvaluationTimestampUtc, persisted.Evaluation.CalculatedAtUtc);
    }

    [Fact]
    public async Task ConfiguredEarningsSourceBuildsAvailableContextConsumedByStrategyGate()
    {
        var fixture = Fixture();
        var expiration = new DateOnly(2026, 10, 16);
        fixture.MarketData.Chains = [Chain(expiration, EvaluationAt, [Contract("CALL", expiration, EvaluationAt)])];
        var source = new ConfiguredEarningsDateSource(new EarningsCalendarConfiguration
        {
            Symbols = new Dictionary<string, string[]> { ["MSFT"] = ["2026-10-16"] }
        });
        var orchestrator = new EntryStrategyEvaluationOrchestrator(fixture.Holdings, fixture.MarketData,
            new IndicatorOrchestrationService(fixture.Repository, fixture.Provider, new IndicatorOrchestrationConfiguration()),
            fixture.Provider, source, fixture.Configuration);

        var bundle = await orchestrator.EvaluateAsync(HoldingId, EvaluationAt);

        Assert.Equal(AvailabilityStatus.Available, bundle.Context.Earnings.Status);
        Assert.Equal(expiration, bundle.Context.Earnings.NextEarningsDate);
        Assert.Equal(RejectionReasonCode.EarningsBeforeExpiration,
            Assert.Single(bundle.StrategyResult.Contracts[0].Gates, x => x.Code == GateCode.Earnings).ReasonCode);
    }

    private static OrchestrationFixture Fixture(ContractEligibilityConfiguration? contractEligibility = null,
        ContractScoreConfiguration? contractScore = null)
    {
        var repository = new FakeIndicatorRepository { Snapshot = Snapshot() };
        var marketData = new FakeEntryStrategyMarketDataRepository();
        var holdings = new FakeHoldingRepository { Holding = Holding() };
        var earnings = new FakeEarningsDateSource();
        var provider = new FakeProvider();
        var orchestration = new IndicatorOrchestrationService(repository, provider, new IndicatorOrchestrationConfiguration());
        var configuration = new EntryStrategyOrchestrationConfiguration
        {
            StrategyConfiguration = new EntryStrategyConfiguration { Version = new ConfigurationVersion(1), ContractEligibility = contractEligibility ?? new(), ContractScore = contractScore ?? new() },
            IndicatorConfiguration = new IndicatorConfiguration { Version = new ConfigurationVersion(1) },
            IndicatorCalculationVersion = IndicatorVersion,
            StrategyVersion = new StrategyVersion("4.0.0")
        };
        return new OrchestrationFixture(repository, marketData, holdings, earnings, provider, configuration,
            new EntryStrategyEvaluationOrchestrator(holdings, marketData, orchestration, provider, earnings, configuration));
    }

    private static Holding Holding(bool isEnabled = true, AssetType assetType = AssetType.Stock) => new(
        new Account("Test", Broker.Other, AccountType.Taxable, false), "MSFT", assetType, 100m,
        AssignmentSensitivity.Level3, TaxSensitivity.Moderate, 1m, .25, .12, .18, 70, 80, .1m, .1, 0, isEnabled);

    private static IndicatorSnapshot Snapshot() => new("MSFT", AsOfDate,
        Available(3m), Available(2m), Available(1m), Available(65d), Missing<double>(), Missing<double>(), Missing<double>(),
        Available(.95d), Available(.1d), Missing<double>(), Missing<double>(), Available(1d), Missing<double>(), Missing<double>(),
        Missing<double>(), Available(.2d), IndicatorVersion, new ConfigurationVersion(1), EvaluationAt.AddMinutes(-30))
    {
        Iv30 = Available(1d), IvRank = Available(50d), IvPercentile = Available(50d),
        ResistancePrice = Available(105m), DistanceToResistancePercent = Available(.02d), ResistanceTouchCount = Available(4),
        ResistanceAgeTradingDays = Available(20), MarketRegime = MarketRegime.Neutral, SectorRegime = MarketRegime.Neutral
    };

    private static OptionChain Chain(DateOnly expiration, DateTimeOffset timestamp, IReadOnlyList<OptionContractSnapshot> contracts) =>
        new("MSFT", expiration, timestamp, contracts, "FakeProvider");

    private static OptionContractSnapshot Contract(string symbol, DateOnly expiration, DateTimeOffset timestamp) => new(symbol, "MSFT", timestamp,
        expiration, 105m, OptionType.Call, 2m, 2.1m, 20m, 10, 500, .3, .15, null, -.06, null, 100m, "FakeProvider");

    private static IndicatorValue<T> Available<T>(T value) where T : struct => IndicatorValue<T>.Available(value);
    private static IndicatorValue<T> Missing<T>() where T : struct => IndicatorValue<T>.InsufficientData();

    private sealed record OrchestrationFixture(FakeIndicatorRepository Repository, FakeEntryStrategyMarketDataRepository MarketData, FakeHoldingRepository Holdings,
        FakeEarningsDateSource Earnings, FakeProvider Provider, EntryStrategyOrchestrationConfiguration Configuration,
        EntryStrategyEvaluationOrchestrator Orchestrator);

    private sealed class FakeHoldingRepository : IHoldingRepository
    {
        public Holding? Holding { get; set; }
        public Task<Holding?> GetByIdAsync(Guid holdingId, CancellationToken cancellationToken = default) => Task.FromResult(Holding);
    }

    private sealed class FakeEarningsDateSource : IEarningsDateSource
    {
        public DateOnly? NextDate { get; set; } = new(2027, 1, 1);
        public int CallCount { get; private set; }
        public Task<DateOnly?> GetNextEarningsDateAsync(string symbol, DateTimeOffset evaluationTimestampUtc,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(NextDate);
        }
    }

    private sealed class FakeProvider : IMarketDataProvider
    {
        public string ProviderName => "FakeProvider";
        public Task<MarketQuote> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<HistoricalBar>> GetHistoricalPricesAsync(string symbol, DateOnly start, DateOnly end, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DateOnly>> GetOptionExpirationsAsync(string symbol, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OptionChain> GetOptionChainAsync(string symbol, DateOnly expiration, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeEntryStrategyMarketDataRepository : IEntryStrategyMarketDataRepository
    {
        public IReadOnlyList<OptionChain> Chains { get; set; } = [];
        public (DateOnly Minimum, DateOnly Maximum, DateTimeOffset Cutoff)? LastRequest { get; private set; }

        public Task<IReadOnlyList<OptionChain>> GetOptionChainsAsync(string symbol, string provider, DateOnly minimumExpiration,
            DateOnly maximumExpiration, DateTimeOffset evaluationTimestampUtc, CancellationToken cancellationToken = default)
        {
            LastRequest = (minimumExpiration, maximumExpiration, evaluationTimestampUtc);
            return Task.FromResult<IReadOnlyList<OptionChain>>(Chains.Where(x => x.Expiration >= minimumExpiration &&
                x.Expiration <= maximumExpiration && x.Timestamp <= evaluationTimestampUtc).ToArray());
        }
    }

    private sealed class CapturingEvaluationRepository : IEntryStrategyEvaluationRepository
    {
        public int InsertCount { get; private set; }
        public PersistedEntryStrategyEvaluation? Value { get; private set; }
        public Task InsertAsync(PersistedEntryStrategyEvaluation evaluation, CancellationToken cancellationToken = default)
        {
            InsertCount++;
            Value = evaluation;
            return Task.CompletedTask;
        }
        public Task<PersistedEntryStrategyEvaluation?> GetByIdAsync(Guid evaluationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Value?.Evaluation.EntryStrategyEvaluationId == evaluationId ? Value : null);
        public Task<IReadOnlyList<EntryStrategyEvaluationHistoryItem>> GetHistoryByHoldingAsync(Guid holdingId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EntryStrategyEvaluationHistoryItem>>([]);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakeIndicatorRepository : IIndicatorDataRepository
    {
        public IndicatorSnapshot Snapshot { get; set; } = null!;
        public int IndicatorLookupCount { get; private set; }
        public int UpsertCount { get; private set; }
        public DateOnly? LastLatestPriceBoundary { get; private set; }
        public Task<DateOnly?> GetLatestPriceObservationDateAsync(string symbol, string provider, DateOnly onOrBefore, CancellationToken cancellationToken = default)
        {
            IndicatorLookupCount++;
            LastLatestPriceBoundary = onOrBefore;
            return Task.FromResult<DateOnly?>(AsOfDate <= onOrBefore ? AsOfDate : null);
        }
        public Task<IReadOnlyList<IndicatorPriceObservation>> GetPricesThroughAsync(string symbol, string provider, DateOnly asOfDate, CancellationToken cancellationToken = default)
        {
            var observations = Enumerable.Range(0, 230).Select(index =>
            {
                var date = AsOfDate.AddDays(index - 229);
                var close = 100m + index;
                return new IndicatorPriceObservation(symbol, date, close, close + 1m, close - 1m, close, 1000);
            }).ToArray();
            return Task.FromResult<IReadOnlyList<IndicatorPriceObservation>>(observations);
        }
        public Task<IReadOnlyList<OptionChain>> GetOptionChainsThroughAsync(string symbol, string provider, DateOnly asOfDate, DateTimeOffset calculatedAt, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OptionChain>>([]);
        public Task<IReadOnlyList<HistoricalIv30Observation>> GetPriorValidIv30Async(string symbol, DateOnly asOfDate, IndicatorCalculationVersion calculationVersion, ConfigurationVersion configurationVersion, int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<HistoricalIv30Observation>>([]);
        public Task<IndicatorSnapshot?> GetSnapshotAsync(string symbol, DateOnly asOfDate, IndicatorCalculationVersion calculationVersion, ConfigurationVersion configurationVersion, CancellationToken cancellationToken = default) => Task.FromResult<IndicatorSnapshot?>(Snapshot);
        public Task UpsertSnapshotAsync(IndicatorSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            UpsertCount++;
            Snapshot = snapshot;
            return Task.CompletedTask;
        }
    }
}

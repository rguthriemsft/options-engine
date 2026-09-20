using OptionsEngine.Application.EntryStrategy;
using OptionsEngine.Application.PositionSizing;
using OptionsEngine.Domain.Accounts;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;
using OptionsEngine.Strategy.PositionSizing;

namespace OptionsEngine.Infrastructure.Tests;

public sealed class PositionSizingEvaluationOrchestratorTests
{
    private static readonly Guid HoldingId = Guid.Parse("71C7B79F-F191-4857-BE5B-46C93316F04F");
    private static readonly Guid OtherHoldingId = Guid.Parse("7079CBDB-0E83-4858-A0CB-AB4C779827F2");
    private static readonly Guid OtherAccountHoldingId = Guid.Parse("BAF9B4E4-A68A-46DB-A0EA-02E6B9B1DE28");
    private static readonly DateOnly AsOfDate = new(2026, 9, 17);
    private static readonly DateTimeOffset SizingAt = new(2026, 9, 18, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AssemblesCurrentHoldingAllAccountHoldingsAndSelectedDeltaObservations()
    {
        var fixture = Fixture();
        fixture.AccountHoldings.Holdings =
        [
            fixture.Holdings.Holding!,
            MakeHolding("AAPL", 500m, isEnabled: false, holdingId: OtherHoldingId,
                account: fixture.Holdings.Holding!.Account),
            MakeHolding("NVDA", 900m, holdingId: OtherAccountHoldingId, account: OtherAccount())
        ];
        fixture.OpenCalls.Exposures =
        [
            Exposure("MSFT261023C00500000", 1),
            Exposure("MSFT261120C00510000", 2)
        ];
        fixture.Market.DailyCloses["MSFT"] = new(100m, AsOfDate);
        fixture.Market.DailyCloses["AAPL"] = new(200m, AsOfDate.AddDays(-1));
        fixture.Market.OptionObservations =
        [
            new("MSFT261023C00500000", .10, SizingAt),
            new("MSFT261120C00510000", .20, SizingAt.AddDays(-100))
        ];

        var bundle = await fixture.Orchestrator.EvaluateAsync(fixture.Source.Evaluation.EntryStrategyEvaluationId, SizingAt);

        Assert.Equal(1, fixture.SourceRepository.GetByIdCount);
        Assert.Equal(1_000m, bundle.Input.Holding!.SharesOwned);
        Assert.Equal(2, bundle.Input.PortfolioConcentration!.Holdings.Length);
        Assert.Contains(bundle.Input.PortfolioConcentration.Holdings, holding => holding.HoldingId == OtherHoldingId);
        Assert.DoesNotContain(bundle.Input.PortfolioConcentration.Holdings, holding => holding.HoldingId == OtherAccountHoldingId);
        Assert.Equal(1_000m, bundle.Input.PortfolioConcentration.Holdings
            .Single(holding => holding.HoldingId == HoldingId).SharesOwned);
        Assert.Equal("TestProvider", Assert.Single(fixture.Market.Providers));
        Assert.Equal(2, bundle.Input.ExistingShortCallDeltaObservations.Length);
        Assert.Equal(50, bundle.Result.ExistingDeltaShares);
        Assert.Equal(.05, bundle.Result.ExistingDer);
        Assert.Equal(.15, bundle.SourceEvaluation.Evaluation.Contracts.Single().Contract.Delta);
        Assert.DoesNotContain("PREFERRED", fixture.Market.OptionSymbols);
    }

    [Fact]
    public async Task MissingParticipatingDailyCloseRemainsUnavailableForStrategy()
    {
        var fixture = Fixture();
        fixture.AccountHoldings.Holdings = [fixture.Holdings.Holding!, MakeHolding("AAPL", 500m,
            holdingId: OtherHoldingId, account: fixture.Holdings.Holding!.Account)];
        fixture.Market.DailyCloses["MSFT"] = new(100m, AsOfDate);

        var bundle = await fixture.Orchestrator.EvaluateAsync(fixture.Source.Evaluation.EntryStrategyEvaluationId, SizingAt);

        Assert.Equal(PositionSizingStatus.InsufficientData, bundle.Result.Status);
        Assert.Contains(PositionSizingMissingInputCode.PortfolioPrice, bundle.Result.MissingInputs);
        Assert.Null(bundle.Input.PortfolioConcentration!.Holdings.Single(x => x.HoldingId == OtherHoldingId).AsOfPrice);
    }

    [Fact]
    public async Task MissingExistingDeltaObservationRemainsMissingForStrategy()
    {
        var fixture = Fixture();
        fixture.OpenCalls.Exposures = [Exposure("MSFT261023C00500000", 1)];
        fixture.Market.DailyCloses["MSFT"] = new(100m, AsOfDate);

        var bundle = await fixture.Orchestrator.EvaluateAsync(fixture.Source.Evaluation.EntryStrategyEvaluationId, SizingAt);

        Assert.Equal(PositionSizingStatus.InsufficientData, bundle.Result.Status);
        Assert.Contains(PositionSizingMissingInputCode.ExistingShortCallDelta, bundle.Result.MissingInputs);
        Assert.Empty(bundle.Input.ExistingShortCallDeltaObservations);
    }

    [Fact]
    public async Task NoEntryCandidateSkipsHoldingMarketAndOpenCallLookups()
    {
        var fixture = Fixture(entryCandidateExists: false);

        var bundle = await fixture.Orchestrator.EvaluateAsync(fixture.Source.Evaluation.EntryStrategyEvaluationId, SizingAt);

        Assert.Equal(PositionSizingStatus.NotApplicable, bundle.Result.Status);
        Assert.Equal(0, fixture.Holdings.GetByIdCount);
        Assert.Equal(0, fixture.AccountHoldings.GetByAccountCount);
        Assert.Equal(0, fixture.Market.DailyCloseCount);
        Assert.Equal(0, fixture.Market.OptionObservationCount);
        Assert.Equal(0, fixture.OpenCalls.GetOpenCallsCount);
    }

    [Fact]
    public async Task SourceIsLoadedPassivelyAndIdenticalInputsProduceEqualBundles()
    {
        var fixture = Fixture();
        fixture.Market.DailyCloses["MSFT"] = new(100m, AsOfDate);

        var first = await fixture.Orchestrator.EvaluateAsync(fixture.Source.Evaluation.EntryStrategyEvaluationId, SizingAt);
        var second = await fixture.Orchestrator.EvaluateAsync(fixture.Source.Evaluation.EntryStrategyEvaluationId, SizingAt);

        Assert.Equal(first.Input.SizingTimestampUtc, second.Input.SizingTimestampUtc);
        Assert.Equal(first.Input.Holding, second.Input.Holding);
        Assert.Equal(first.Input.PortfolioConcentration!.Holdings.ToArray(),
            second.Input.PortfolioConcentration!.Holdings.ToArray());
        Assert.Equal(first.Result.Status, second.Result.Status);
        Assert.Equal(first.Result.ExistingDer, second.Result.ExistingDer);
        Assert.Equal(first.Result.AdditionalContracts, second.Result.AdditionalContracts);
        Assert.Equal(2, fixture.SourceRepository.GetByIdCount);
        Assert.Equal(0, fixture.SourceRepository.InsertCount);
    }

    [Fact]
    public async Task MissingSourceAndNonUtcTimestampFailBeforeSizing()
    {
        var fixture = Fixture();
        fixture.SourceRepository.Source = null;

        await Assert.ThrowsAsync<EntryStrategyEvaluationNotFoundException>(() =>
            fixture.Orchestrator.EvaluateAsync(Guid.NewGuid(), SizingAt));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Orchestrator.EvaluateAsync(
            fixture.Source.Evaluation.EntryStrategyEvaluationId, SizingAt.ToOffset(TimeSpan.FromHours(-7))));
    }

    private static OrchestrationFixture Fixture(bool entryCandidateExists = true)
    {
        var source = Source(entryCandidateExists);
        var sourceRepository = new FakeSourceRepository { Source = source };
        var holding = MakeHolding("MSFT", 1_000m, holdingId: HoldingId);
        var holdings = new FakeHoldingRepository { Holding = holding };
        var accountHoldings = new FakeAccountHoldingsRepository { Holdings = [holding] };
        var market = new FakeMarketDataRepository();
        var openCalls = new FakeOpenCallsRepository();
        return new OrchestrationFixture(source, sourceRepository, holdings, accountHoldings, market, openCalls,
            new PositionSizingEvaluationOrchestrator(sourceRepository, holdings, accountHoldings, market, openCalls,
                new PositionSizingEngine(), new PositionSizingOrchestrationConfiguration
                {
                    PositionSizingConfiguration = new() { Version = new ConfigurationVersion(1) },
                    StrategyVersion = new PositionSizingStrategyVersion("5.0.0")
                }));
    }

    private static PersistedEntryStrategyEvaluation Source(bool entryCandidateExists)
    {
        var account = Account();
        var context = new EvaluationContext(
            new HoldingContext(HoldingId, "MSFT", AssetType.Stock, AssignmentSensitivity.Level3, TaxSensitivity.Moderate,
                .25, .12, .18, 70, 80, .1m, .1),
            new IndicatorContext("MSFT", AsOfDate,
                IndicatorValue<double>.InsufficientData(), IndicatorValue<double>.InsufficientData(),
                IndicatorValue<double>.InsufficientData(), IndicatorValue<double>.InsufficientData(),
                IndicatorValue<double>.InsufficientData(), IndicatorValue<double>.InsufficientData(),
                IndicatorValue<decimal>.InsufficientData(), IndicatorValue<decimal>.InsufficientData(),
                IndicatorValue<decimal>.InsufficientData(), IndicatorValue<double>.InsufficientData(),
                IndicatorValue<decimal>.InsufficientData(), IndicatorValue<double>.InsufficientData(),
                IndicatorValue<int>.InsufficientData(), IndicatorValue<int>.InsufficientData(), null,
                MarketRegime.InsufficientData, MarketRegime.InsufficientData,
                new IndicatorCalculationVersion("3.0.0"), new ConfigurationVersion(1), SizingAt),
            new EarningsContext(AvailabilityStatus.Available, AsOfDate.AddDays(40)), AsOfDate, SizingAt,
            new EntryStrategyConfiguration { Version = new ConfigurationVersion(1) }, new StrategyVersion("4.0.0"));
        var contract = new OptionContractContext("PREFERRED", "MSFT", SizingAt.AddMinutes(-1), AsOfDate.AddDays(35),
            500m, OptionContractType.Call, 1m, 1.1m, 100, .30, .15, -.01, 500m, 1m, 10, "TestProvider");
        var evaluation = new EntryStrategyEvaluation(Guid.Parse("E17D5BFA-9AE5-4519-9EEB-49E87C9B0071"), SizingAt.AddMinutes(-1),
            context, Score(80), [], [new ContractEvaluation(contract, new ContractDerivedMetrics(null, null, null, null,
                null, null, null, null, null), [], Score(85), true, true, 1, [], [])], entryCandidateExists,
            entryCandidateExists ? "PREFERRED" : null, entryCandidateExists ? 500m : null,
            entryCandidateExists ? AsOfDate.AddDays(35) : null, entryCandidateExists ? 1m : null,
            entryCandidateExists ? DispositionReasonCode.EntryCandidate : DispositionReasonCode.NoAcceptableContract, [], []);
        return new PersistedEntryStrategyEvaluation(evaluation, [], []);
    }

    private static ScoreResult Score(double value) => new(ScoreStatus.Available, value, 100, null, 0, true, [], [], "Persisted.");

    private static Account Account() => new("Account", Broker.Other, AccountType.Taxable, false);

    private static Account OtherAccount() => new("Other", Broker.Other, AccountType.Taxable, false);

    private static Holding MakeHolding(string symbol, decimal shares, bool isEnabled = true, Guid? holdingId = null,
        Account? account = null)
    {
        var holding = new Holding(account ?? Account(), symbol, AssetType.Stock, shares, AssignmentSensitivity.Level3,
            TaxSensitivity.Moderate, 1m, .25, .12, .18, 70, 80, .1m, .1, .50, isEnabled);
        if (holdingId is not null)
        {
            typeof(Holding).GetProperty(nameof(Holding.HoldingId))!.SetValue(holding, holdingId);
        }
        return holding;
    }

    private static ExistingShortCallExposure Exposure(string symbol, int contracts) =>
        new(HoldingId, symbol, contracts, 500m, AsOfDate.AddDays(35));

    private sealed record OrchestrationFixture(PersistedEntryStrategyEvaluation Source, FakeSourceRepository SourceRepository,
        FakeHoldingRepository Holdings, FakeAccountHoldingsRepository AccountHoldings, FakeMarketDataRepository Market,
        FakeOpenCallsRepository OpenCalls, PositionSizingEvaluationOrchestrator Orchestrator);

    private sealed class FakeSourceRepository : IEntryStrategyEvaluationRepository
    {
        public PersistedEntryStrategyEvaluation? Source { get; set; }
        public int GetByIdCount { get; private set; }
        public int InsertCount { get; private set; }
        public Task InsertAsync(PersistedEntryStrategyEvaluation evaluation, CancellationToken cancellationToken = default)
        {
            InsertCount++;
            return Task.CompletedTask;
        }
        public Task<PersistedEntryStrategyEvaluation?> GetByIdAsync(Guid evaluationId, CancellationToken cancellationToken = default)
        {
            GetByIdCount++;
            return Task.FromResult(Source?.Evaluation.EntryStrategyEvaluationId == evaluationId ? Source : null);
        }
        public Task<IReadOnlyList<EntryStrategyEvaluationHistoryItem>> GetHistoryByHoldingAsync(Guid holdingId,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<EntryStrategyEvaluationHistoryItem>>([]);
    }

    private sealed class FakeHoldingRepository : IHoldingRepository
    {
        public Holding? Holding { get; set; }
        public int GetByIdCount { get; private set; }
        public Task<Holding?> GetByIdAsync(Guid holdingId, CancellationToken cancellationToken = default)
        {
            GetByIdCount++;
            return Task.FromResult(Holding?.HoldingId == holdingId ? Holding : null);
        }
    }

    private sealed class FakeAccountHoldingsRepository : IPositionSizingHoldingRepository
    {
        public IReadOnlyList<Holding> Holdings { get; set; } = [];
        public int GetByAccountCount { get; private set; }
        public Task<IReadOnlyList<Holding>> GetByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default)
        {
            GetByAccountCount++;
            return Task.FromResult<IReadOnlyList<Holding>>(Holdings.Where(holding => holding.AccountId == accountId).ToArray());
        }
    }

    private sealed class FakeMarketDataRepository : IPositionSizingMarketDataRepository
    {
        public Dictionary<string, PositionSizingDailyClose> DailyCloses { get; } = new(StringComparer.Ordinal);
        public IReadOnlyList<PositionSizingOptionDeltaObservation> OptionObservations { get; set; } = [];
        public HashSet<string> Providers { get; } = new(StringComparer.Ordinal);
        public HashSet<string> OptionSymbols { get; } = new(StringComparer.Ordinal);
        public int DailyCloseCount { get; private set; }
        public int OptionObservationCount { get; private set; }
        public Task<PositionSizingDailyClose?> GetLatestDailyCloseAsync(string symbol, string provider, DateOnly indicatorAsOfDate,
            CancellationToken cancellationToken = default)
        {
            DailyCloseCount++;
            Providers.Add(provider);
            return Task.FromResult(DailyCloses.TryGetValue(symbol, out var close) ? (PositionSizingDailyClose?)close : null);
        }
        public Task<IReadOnlyList<PositionSizingOptionDeltaObservation>> GetLatestOptionDeltaObservationsAsync(
            IReadOnlyCollection<string> optionSymbols, string provider, DateTimeOffset sizingTimestampUtc,
            CancellationToken cancellationToken = default)
        {
            OptionObservationCount++;
            Providers.Add(provider);
            OptionSymbols.UnionWith(optionSymbols);
            return Task.FromResult<IReadOnlyList<PositionSizingOptionDeltaObservation>>(OptionObservations
                .Where(observation => optionSymbols.Contains(observation.OptionSymbol)).ToArray());
        }
    }

    private sealed class FakeOpenCallsRepository : IOpenShortCallPositionRepository
    {
        public IReadOnlyList<ExistingShortCallExposure> Exposures { get; set; } = [];
        public int GetOpenCallsCount { get; private set; }
        public Task<IReadOnlyList<ExistingShortCallExposure>> GetOpenShortCallsByHoldingAsync(Guid holdingId,
            CancellationToken cancellationToken = default)
        {
            GetOpenCallsCount++;
            return Task.FromResult(Exposures);
        }
    }
}

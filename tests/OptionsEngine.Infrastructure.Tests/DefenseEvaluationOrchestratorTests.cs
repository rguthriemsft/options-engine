using OptionsEngine.Application.Defense;
using OptionsEngine.Application.EntryStrategy;
using OptionsEngine.Domain.Accounts;
using OptionsEngine.MarketData;
using OptionsEngine.MarketData.Models;
using OptionsEngine.Strategy.Defense;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Infrastructure.Tests;

public sealed class DefenseEvaluationOrchestratorTests
{
    private static readonly DateTimeOffset EvaluationAt =
        new(2026, 9, 20, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CalculatedAt = EvaluationAt.AddMinutes(5);

    [Fact]
    public async Task SafeRunBuildsArtifactWithoutRollChainsOrCurrentCcos()
    {
        var fixture = Fixture(Current(delta: .10, underlyingPrice: 90, ask: 1.1m), []);

        var bundle = await fixture.Orchestrator.EvaluateAsync(fixture.Holding.HoldingId,
            fixture.Position.OpenShortCallPositionId, EvaluationAt);

        Assert.Equal(DefenseDisposition.NoAction, bundle.DefenseEvaluation.Disposition);
        Assert.False(bundle.DefenseEvaluation.RollEngineRequired);
        Assert.Null(bundle.DefenseEvaluation.RollEvaluationId);
        Assert.Null(bundle.RollEvaluation);
        Assert.Equal(0, fixture.MarketData.ChainCalls);
        Assert.Equal(0, fixture.Ccos.Calls);
        Assert.Equal(EvaluationAt, bundle.DefenseEvaluation.DefenseEvaluationTimestampUtc);
        Assert.Equal(CalculatedAt, bundle.DefenseEvaluation.CalculatedAtUtc);
        Assert.Equal(fixture.Position, bundle.DefenseEvaluation.PositionSnapshot);
        Assert.Equal(fixture.MarketData.Current, bundle.DefenseEvaluation.CurrentOptionObservation);
        Assert.Equal(fixture.MarketData.Previous, bundle.DefenseEvaluation.PreviousDeltaObservation);
    }

    [Fact]
    public async Task ProfitCloseRemainsACompleteNoDefenseArtifact()
    {
        var fixture = Fixture(Current(delta: .10, underlyingPrice: 90, ask: .4m), []);

        var bundle = await Evaluate(fixture);

        Assert.Equal(ProfitTakingSignal.StrongCloseCandidate,
            bundle.DefenseEvaluation.ProfitTaking.Signal);
        Assert.Equal(DefenseDisposition.ProfitClose, bundle.DefenseEvaluation.Disposition);
        Assert.Null(bundle.RollEvaluation);
        Assert.Equal(0, fixture.MarketData.ChainCalls);
        Assert.Equal(0, fixture.Ccos.Calls);
    }

    [Fact]
    public async Task HardTriggerRunsCandidateAnalysisAndStrongCurrentCcosSelectsRoll()
    {
        var candidate = Candidate();
        var fixture = Fixture(Current(delta: .50, underlyingPrice: 105, ask: 2), [Chain(candidate)]);
        fixture.Ccos.Score = 55;

        var bundle = await Evaluate(fixture);

        Assert.Equal(DefenseDisposition.Roll, bundle.DefenseEvaluation.Disposition);
        Assert.Equal(HardDefenseStatus.Triggered, bundle.DefenseEvaluation.HardDefenseStatus);
        Assert.Equal(1, fixture.MarketData.ChainCalls);
        Assert.Equal(1, fixture.Ccos.Calls);
        Assert.NotNull(bundle.RollEvaluation);
        Assert.Equal(bundle.DefenseEvaluation.DefenseEvaluationId,
            bundle.RollEvaluation!.DefenseEvaluationId);
        Assert.Equal(bundle.DefenseEvaluation.RollEvaluationId, bundle.RollEvaluation.RollEvaluationId);
        Assert.Equal(55, bundle.RollEvaluation.CurrentCcos);
        Assert.Equal(fixture.MarketData.Chains, bundle.RollEvaluation.SelectedChainSnapshots);
        Assert.Equal(new ConfigurationVersion(1), bundle.DefenseEvaluation.ConfigurationVersion);
        Assert.Equal(new DefenseStrategyVersion("6.0.0"),
            bundle.DefenseEvaluation.DefenseStrategyVersion);
        Assert.Equal(new RollStrategyVersion("6.0.0"), bundle.RollEvaluation.StrategyVersion);
        Assert.Equal(RollCandidateEvaluationState.Rankable,
            Assert.Single(bundle.RollEvaluation.Candidates).State);
        Assert.Equal(candidate.OptionSymbol, bundle.RollEvaluation.PreferredOptionSymbol);
    }

    [Theory]
    [InlineData(54.999, DefenseDisposition.CloseWait)]
    [InlineData(null, DefenseDisposition.DefenseReview)]
    public async Task RankableCandidateUsesLazyCurrentCcosForFinalDisposition(
        double? ccos, DefenseDisposition expected)
    {
        var fixture = Fixture(Current(delta: .50, underlyingPrice: 105, ask: 2), [Chain(Candidate())]);
        fixture.Ccos.Score = ccos;

        var bundle = await Evaluate(fixture);

        Assert.Equal(expected, bundle.DefenseEvaluation.Disposition);
        Assert.Equal(1, fixture.Ccos.Calls);
        Assert.Equal(ccos, bundle.DefenseEvaluation.CurrentCcos);
    }

    [Fact]
    public async Task CandidateAnalysisRunsBeforeCcosAndInsufficientCandidateProducesReview()
    {
        var fixture = Fixture(Current(delta: .50, underlyingPrice: 105, ask: 2),
            [Chain(Candidate() with { Ask = null })]);
        fixture.Ccos.Score = 90;

        var bundle = await Evaluate(fixture);

        Assert.Equal(DefenseDisposition.DefenseReview, bundle.DefenseEvaluation.Disposition);
        Assert.Equal(RollCandidateEvaluationState.InsufficientData,
            Assert.Single(bundle.RollEvaluation!.Candidates).State);
        Assert.Equal(1, fixture.MarketData.ChainCalls);
        Assert.Equal(0, fixture.Ccos.Calls);
        Assert.Contains(RollMissingInputCode.CandidateAsk, bundle.RollEvaluation.MissingInputs);
    }

    [Fact]
    public async Task AllRejectedHardTriggerProducesReviewWithoutResolvingCcos()
    {
        var fixture = Fixture(Current(delta: .50, underlyingPrice: 105, ask: 2),
            [Chain(Candidate() with { Strike = 100 })]);

        var bundle = await Evaluate(fixture);

        Assert.Equal(DefenseDisposition.DefenseReview, bundle.DefenseEvaluation.Disposition);
        Assert.Equal(RollCandidateEvaluationState.Rejected,
            Assert.Single(bundle.RollEvaluation!.Candidates).State);
        Assert.Equal(0, fixture.Ccos.Calls);
    }

    [Fact]
    public async Task AllRejectedDrsOnlyActivationProducesCloseWait()
    {
        var position = Position(expiration: new DateOnly(2026, 9, 25));
        var fixture = Fixture(Current(delta: .35, underlyingPrice: 98, ask: 2,
            expiration: position.Expiration), [Chain(Candidate() with { Strike = 100 })], position);

        var bundle = await Evaluate(fixture);

        Assert.Equal(HardDefenseStatus.Clear, bundle.DefenseEvaluation.HardDefenseStatus);
        Assert.True(bundle.DefenseEvaluation.RollEngineRequired);
        Assert.Equal(DefenseDisposition.CloseWait, bundle.DefenseEvaluation.Disposition);
        Assert.Equal(0, fixture.Ccos.Calls);
    }

    [Fact]
    public async Task MissingCurrentObservationRemainsAnalyticalRatherThanARequestFailure()
    {
        var fixture = Fixture(null, []);

        var bundle = await Evaluate(fixture);

        Assert.Equal(DefenseDisposition.DefenseReview, bundle.DefenseEvaluation.Disposition);
        Assert.Null(bundle.DefenseEvaluation.CurrentOptionObservation);
        Assert.Contains(DefenseMissingInputCode.CurrentAsk, bundle.DefenseEvaluation.MissingInputs);
        Assert.Contains(DefenseMissingInputCode.CurrentDelta, bundle.DefenseEvaluation.MissingInputs);
    }

    [Fact]
    public async Task InvalidCurrentStateFailsBeforeMarketOrStrategyWork()
    {
        var fixture = Fixture(Current(), []);
        fixture.Positions.Position = null;
        await Assert.ThrowsAsync<OpenShortCallPositionNotFoundException>(() => Evaluate(fixture));
        Assert.Equal(0, fixture.MarketData.CurrentCalls);

        fixture.Positions.Position = fixture.Position with { Expiration = new DateOnly(2026, 9, 19) };
        await Assert.ThrowsAsync<InvalidOpenShortCallPositionException>(() => Evaluate(fixture));
        Assert.Equal(0, fixture.MarketData.CurrentCalls);
    }

    [Fact]
    public async Task MissingAndDisabledHoldingsAreDistinctCurrentStateFailures()
    {
        var fixture = Fixture(Current(), []);
        fixture.Holdings.Holding = null;
        await Assert.ThrowsAsync<HoldingNotFoundException>(() => Evaluate(fixture));
        Assert.Equal(0, fixture.MarketData.CurrentCalls);

        fixture.Holdings.Holding = Holding(isEnabled: false);
        await Assert.ThrowsAsync<HoldingDisabledException>(() =>
            fixture.Orchestrator.EvaluateAsync(fixture.Holdings.Holding.HoldingId,
                fixture.Position.OpenShortCallPositionId, EvaluationAt));
        Assert.Equal(0, fixture.MarketData.CurrentCalls);
    }

    [Fact]
    public async Task CandidateExpirationRequestUsesApprovedDteAndLaterExpirationUniverse()
    {
        var position = Position(expiration: new DateOnly(2026, 10, 1));
        var fixture = Fixture(Current(delta: .50, underlyingPrice: 105, ask: 2,
            expiration: position.Expiration), [], position);

        var bundle = await Evaluate(fixture);

        Assert.Equal((new DateOnly(2026, 10, 11), new DateOnly(2026, 11, 19), EvaluationAt),
            fixture.MarketData.LastChainRequest);
        Assert.Empty(bundle.RollEvaluation!.SelectedChainSnapshots);
        Assert.Empty(bundle.RollEvaluation.Candidates);
    }

    private static Task<DefenseEvaluationBundle> Evaluate(FixtureState fixture) =>
        fixture.Orchestrator.EvaluateAsync(fixture.Holding.HoldingId,
            fixture.Position.OpenShortCallPositionId, EvaluationAt);

    private static FixtureState Fixture(DefenseOptionObservation? current,
        IReadOnlyList<SelectedRollChainSnapshot> chains,
        OpenShortCallPositionSnapshot? suppliedPosition = null)
    {
        var holding = Holding();
        var position = suppliedPosition ?? Position();
        position = position with { HoldingId = holding.HoldingId };
        var holdings = new FakeHoldingRepository { Holding = holding };
        var positions = new FakePositionRepository { Position = position };
        var market = new FakeDefenseMarketDataRepository
        {
            Current = current is null ? null : current with
            {
                OptionSymbol = position.OptionSymbol,
                Expiration = position.Expiration,
                Strike = position.Strike
            },
            Previous = current is null ? null : (current with
            {
                OptionSymbol = position.OptionSymbol,
                Expiration = position.Expiration,
                Strike = position.Strike,
                ObservationTimestampUtc = EvaluationAt.AddDays(-1),
                Delta = current.Delta
            }),
            Chains = chains
        };
        var ccos = new FakeCcosResolver();
        var configuration = new DefenseRollConfiguration
        {
            Defense = new DefenseConfiguration { Version = new ConfigurationVersion(1) },
            Roll = new RollConfiguration
            {
                Version = new ConfigurationVersion(1), MaximumRollDebitPerShare = 2
            },
            DefenseStrategyVersion = new DefenseStrategyVersion("6.0.0"),
            RollStrategyVersion = new RollStrategyVersion("6.0.0")
        };
        var orchestrator = new DefenseEvaluationOrchestrator(holdings, positions, market,
            new FakeEarningsDateSource(), ccos, new FakeProvider(), configuration,
            new FixedTimeProvider(CalculatedAt));
        return new FixtureState(holding, position, holdings, positions, market, ccos, orchestrator);
    }

    private static Holding Holding(bool isEnabled = true) => new(new Account("Test", Broker.Other,
        AccountType.Taxable, false), "MSFT", AssetType.Stock, 100,
        AssignmentSensitivity.Level3, TaxSensitivity.Moderate, 1, .25, .12, .18,
        70, 80, .1m, .1, .25, isEnabled);

    private static OpenShortCallPositionSnapshot Position(DateOnly? expiration = null) => new(
        42, Guid.NewGuid(), "MSFT260930C00100000", 1, 100,
        expiration ?? new DateOnly(2026, 9, 30), 2, EvaluationAt.AddMonths(-1));

    private static DefenseOptionObservation Current(double? delta = .10,
        decimal? underlyingPrice = 90, decimal? ask = 1, DateOnly? expiration = null) => new(
        "MSFT260930C00100000", "MSFT", EvaluationAt, expiration ?? new DateOnly(2026, 9, 30),
        100, OptionContractType.Call, .9m, ask, .95m, 500, .30, delta, .02, -.01,
        .10, underlyingPrice, "FakeProvider");

    private static DefenseOptionObservation Candidate() => new("MSFT261020C00110000", "MSFT",
        EvaluationAt, new DateOnly(2026, 10, 20), 110, OptionContractType.Call,
        2, 2.1m, 2.05m, 500, .30, .20, .02, -.01, .10, 105, "FakeProvider");

    private static SelectedRollChainSnapshot Chain(DefenseOptionObservation candidate) => new(
        "MSFT", candidate.Expiration, candidate.ObservationTimestampUtc, "FakeProvider", [candidate]);

    private sealed record FixtureState(Holding Holding, OpenShortCallPositionSnapshot Position,
        FakeHoldingRepository Holdings, FakePositionRepository Positions,
        FakeDefenseMarketDataRepository MarketData,
        FakeCcosResolver Ccos, DefenseEvaluationOrchestrator Orchestrator);

    private sealed class FakeHoldingRepository : IHoldingRepository
    {
        public Holding? Holding { get; set; }
        public Task<Holding?> GetByIdAsync(Guid holdingId,
            CancellationToken cancellationToken = default) => Task.FromResult(
            Holding?.HoldingId == holdingId ? Holding : null);
    }

    private sealed class FakePositionRepository : ICurrentOpenShortCallPositionRepository
    {
        public OpenShortCallPositionSnapshot? Position { get; set; }
        public Task<OpenShortCallPositionSnapshot?> GetByIdAsync(Guid holdingId,
            long openShortCallPositionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Position is { } value && value.HoldingId == holdingId &&
                value.OpenShortCallPositionId == openShortCallPositionId ? value : null);
    }

    private sealed class FakeDefenseMarketDataRepository : IDefenseMarketDataRepository
    {
        public DefenseOptionObservation? Current { get; init; }
        public DefenseOptionObservation? Previous { get; init; }
        public IReadOnlyList<SelectedRollChainSnapshot> Chains { get; init; } = [];
        public int CurrentCalls { get; private set; }
        public int ChainCalls { get; private set; }
        public (DateOnly Minimum, DateOnly Maximum, DateTimeOffset Cutoff)? LastChainRequest { get; private set; }

        public Task<DefenseOptionObservation?> GetLatestOptionObservationAsync(string optionSymbol,
            string provider, DateTimeOffset evaluationTimestampUtc,
            CancellationToken cancellationToken = default)
        {
            CurrentCalls++;
            return Task.FromResult(Current);
        }

        public Task<DefenseOptionObservation?> GetPreviousTradingDateOptionObservationAsync(
            string optionSymbol, string provider, DateTimeOffset currentObservationTimestampUtc,
            DateTimeOffset evaluationTimestampUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(Previous);

        public Task<IReadOnlyList<SelectedRollChainSnapshot>> GetLatestOptionChainsAsync(
            string underlyingSymbol, string provider, DateOnly minimumExpiration,
            DateOnly maximumExpiration, DateTimeOffset evaluationTimestampUtc,
            CancellationToken cancellationToken = default)
        {
            ChainCalls++;
            LastChainRequest = (minimumExpiration, maximumExpiration, evaluationTimestampUtc);
            return Task.FromResult(Chains);
        }
    }

    private sealed class FakeCcosResolver : ICurrentCcosResolver
    {
        public double? Score { get; set; } = 55;
        public int Calls { get; private set; }
        public Task<CurrentCcosContext?> ResolveAsync(Holding holding, EarningsContext earnings,
            DateTimeOffset evaluationTimestampUtc, ConfigurationVersion configurationVersion,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Score is null) return Task.FromResult<CurrentCcosContext?>(null);
            var result = new ScoreResult(ScoreStatus.Available, Score, 100, "TEST", 70,
                Score >= 70, [], [], "Test");
            var version = new ConfigurationVersion(1);
            var indicators = new IndicatorContext(holding.Symbol,
                DefenseEvaluator.EvaluationDate(evaluationTimestampUtc),
                Missing<double>(), Missing<double>(), Missing<double>(), Missing<double>(),
                Missing<double>(), Missing<double>(), Missing<decimal>(), Missing<decimal>(),
                Missing<decimal>(), Missing<double>(), Missing<decimal>(), Missing<double>(),
                Missing<int>(), Missing<int>(), ResistanceUnavailableReason.InsufficientData,
                MarketRegime.InsufficientData, MarketRegime.InsufficientData,
                new IndicatorCalculationVersion("3.0.0"), version, evaluationTimestampUtc);
            var context = new EvaluationContext(new HoldingContext(holding.HoldingId,
                holding.Symbol, holding.AssetType, holding.AssignmentSensitivity,
                holding.TaxSensitivity, holding.MaximumInitialDelta,
                holding.PreferredDeltaMinimum, holding.PreferredDeltaMaximum,
                holding.MinimumCcos, holding.MinimumContractScore, holding.MinimumPremium,
                holding.MinimumAnnualizedYield), indicators, earnings, indicators.AsOfDate,
                evaluationTimestampUtc, new EntryStrategyConfiguration { Version = version },
                new StrategyVersion("4.0.0"));
            return Task.FromResult<CurrentCcosContext?>(new CurrentCcosContext(context, result));
        }

        private static IndicatorValue<T> Missing<T>() where T : struct =>
            IndicatorValue<T>.InsufficientData();
    }

    private sealed class FakeEarningsDateSource : IEarningsDateSource
    {
        public Task<DateOnly?> GetNextEarningsDateAsync(string symbol,
            DateTimeOffset evaluationTimestampUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<DateOnly?>(new DateOnly(2027, 1, 1));
    }

    private sealed class FakeProvider : IMarketDataProvider
    {
        public string ProviderName => "FakeProvider";
        public Task<MarketQuote> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<HistoricalBar>> GetHistoricalPricesAsync(string symbol, DateOnly start,
            DateOnly end, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DateOnly>> GetOptionExpirationsAsync(string symbol,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OptionChain> GetOptionChainAsync(string symbol, DateOnly expiration,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}

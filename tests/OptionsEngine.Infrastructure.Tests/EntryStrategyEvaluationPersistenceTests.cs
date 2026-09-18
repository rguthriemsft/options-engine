using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using OptionsEngine.Application.EntryStrategy;
using OptionsEngine.Domain.Accounts;
using OptionsEngine.Infrastructure.Persistence;
using OptionsEngine.MarketData.Models;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Infrastructure.Tests;

public sealed class EntryStrategyEvaluationPersistenceTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"options-engine-entry-evaluation-{Guid.NewGuid():N}.db");
    private static readonly Guid HoldingId = Guid.Parse("7B2D7E4C-0F7E-4C77-91B6-0C5B16B3A7D2");
    private static readonly DateTimeOffset EvaluationAt = new(2026, 9, 18, 15, 0, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync()
    {
        File.Delete(path);
        File.Delete($"{path}-shm");
        File.Delete($"{path}-wal");
        return Task.CompletedTask;
    }

    [Fact]
    public async Task InsertRoundTripsCompletePayloadIncludingEmptyChainMissingValuesAndExplainability()
    {
        var first = Persisted(calculatedAt: EvaluationAt.AddMinutes(1));
        await using (var db = CreateContext())
            await new SqliteEntryStrategyEvaluationRepository(db).InsertAsync(first);

        await using (var rawDb = CreateContext())
        {
            var json = (await rawDb.EntryStrategyEvaluations.SingleAsync()).EvaluationJson;
            Assert.Contains("\"InsufficientData\"", json);
            Assert.Contains("\"BreakoutVeto\"", json);
            Assert.Contains("\"CcosRsi\"", json);
            Assert.DoesNotContain("\"CcosRsi\":0", json);
        }

        await using var verify = CreateContext();
        var actual = await new SqliteEntryStrategyEvaluationRepository(verify).GetByIdAsync(first.Evaluation.EntryStrategyEvaluationId);

        Assert.NotNull(actual);
        Assert.Equal(first.Evaluation.EntryStrategyEvaluationId, actual!.Evaluation.EntryStrategyEvaluationId);
        Assert.Equal(first.Evaluation.Context.Holding, actual.Evaluation.Context.Holding);
        Assert.Equal(first.Evaluation.Context.Indicators, actual.Evaluation.Context.Indicators);
        Assert.Equal(first.Evaluation.Context.Earnings, actual.Evaluation.Context.Earnings);
        Assert.Equal(JsonSerializer.Serialize(first.Evaluation.Context.Configuration),
            JsonSerializer.Serialize(actual.Evaluation.Context.Configuration));
        Assert.Equal(first.Evaluation.Context.StrategyVersion, actual.Evaluation.Context.StrategyVersion);
        Assert.Equal(first.Evaluation.CalculatedAtUtc, actual.Evaluation.CalculatedAtUtc);
        Assert.Equal(first.Evaluation.UnderlyingGates.Count, actual.Evaluation.UnderlyingGates.Count);
        Assert.Equal(first.Evaluation.Contracts.Count, actual.Evaluation.Contracts.Count);
        var expectedContract = Assert.Single(first.Evaluation.Contracts);
        var actualContract = Assert.Single(actual.Evaluation.Contracts);
        Assert.Equal(expectedContract.Contract, actualContract.Contract);
        Assert.Equal(expectedContract.DerivedMetrics, actualContract.DerivedMetrics);
        Assert.Equal((expectedContract.HardGateEligible, expectedContract.EntryAcceptable, expectedContract.Rank),
            (actualContract.HardGateEligible, actualContract.EntryAcceptable, actualContract.Rank));
        Assert.Equal(expectedContract.MissingInputs, actualContract.MissingInputs);
        Assert.Equal(expectedContract.Explanations, actualContract.Explanations);
        Assert.Equal(first.Evaluation.PreferredInitialOptionSymbol, actual.Evaluation.PreferredInitialOptionSymbol);
        Assert.Equal(first.SelectedOptionChains.Select(x => (x.Expiration, x.Timestamp, x.Contracts.Count)),
            actual.SelectedOptionChains.Select(x => (x.Expiration, x.Timestamp, x.Contracts.Count)));
        Assert.Empty(actual.SelectedOptionChains.Single(x => x.Expiration == new DateOnly(2026, 10, 23)).Contracts);

        var component = Assert.Single(actual.Evaluation.Ccos!.Components);
        Assert.Equal(ScoreComponentCode.CcosRsi, component.Code);
        Assert.Equal("RSI", component.Name);
        Assert.Equal(ScoreStatus.Unavailable, component.Status);
        Assert.Null(component.Score);
        Assert.Equal(15d, component.MaximumScore);
        Assert.Equal(new[] { MissingInputCode.Rsi14 }, component.MissingInputs);
        Assert.Equal("RSI unavailable", component.Explanation);
        var gate = Assert.Single(actual.Evaluation.UnderlyingGates);
        Assert.Equal(GateCode.BreakoutVeto, gate.Code);
        Assert.Equal(GateStatus.Unavailable, gate.Status);
        Assert.Equal(RejectionReasonCode.InsufficientData, gate.ReasonCode);
        Assert.Equal(new[] { MissingInputCode.Rsi14 }, gate.MissingInputs);
    }

    [Fact]
    public async Task SuccessfulEntryCandidateRoundTripsCompleteScoresRankAndDisposition()
    {
        var expected = Successful();
        await using (var db = CreateContext())
            await new SqliteEntryStrategyEvaluationRepository(db).InsertAsync(expected);

        await using var verify = CreateContext();
        var actual = (await new SqliteEntryStrategyEvaluationRepository(verify)
            .GetByIdAsync(expected.Evaluation.EntryStrategyEvaluationId))!;
        var contract = Assert.Single(actual.Evaluation.Contracts);
        Assert.NotNull(contract.ContractScore);
        var score = contract.ContractScore!;

        Assert.Equal((ScoreStatus.Available, 84d, "Good", 80d, true),
            (score.Status, score.Score, score.Classification, score.ConfiguredMinimum, score.MeetsConfiguredMinimum));
        var expectedScore = expected.Evaluation.Contracts[0].ContractScore!;
        Assert.Equal(expectedScore.Components.Count, score.Components.Count);
        foreach (var (expectedComponent, actualComponent) in expectedScore.Components.Zip(score.Components))
        {
            Assert.Equal(expectedComponent.Code, actualComponent.Code);
            Assert.Equal(expectedComponent.Name, actualComponent.Name);
            Assert.Equal(expectedComponent.Status, actualComponent.Status);
            Assert.Equal(expectedComponent.Score, actualComponent.Score);
            Assert.Equal(expectedComponent.MaximumScore, actualComponent.MaximumScore);
            Assert.Equal(expectedComponent.Inputs, actualComponent.Inputs);
            Assert.Equal(expectedComponent.MissingInputs, actualComponent.MissingInputs);
            Assert.Equal(expectedComponent.Explanation, actualComponent.Explanation);
        }
        Assert.Equal(1, contract.Rank);
        Assert.True(contract.HardGateEligible);
        Assert.True(contract.EntryAcceptable);
        Assert.True(actual.Evaluation.EntryCandidateExists);
        Assert.Equal(DispositionReasonCode.EntryCandidate, actual.Evaluation.DispositionReason);
        Assert.Equal("MSFT-C", actual.Evaluation.PreferredInitialOptionSymbol);
        Assert.Equal(105m, actual.Evaluation.PreferredInitialStrike);
        Assert.Equal(new DateOnly(2026, 10, 16), actual.Evaluation.PreferredInitialExpiration);
        Assert.Equal(2m, actual.Evaluation.PreferredInitialReferencePremium);
    }

    [Fact]
    public async Task InsertsAreAppendOnlyAndHistoryIsNewestFirstAndHoldingScoped()
    {
        var first = Persisted(calculatedAt: EvaluationAt.AddMinutes(1));
        var second = Persisted(Guid.NewGuid(), EvaluationAt.AddMinutes(2));
        var otherHolding = Persisted(Guid.NewGuid(), EvaluationAt.AddMinutes(3), holdingId: Guid.NewGuid());
        await using (var db = CreateContext())
        {
            var repository = new SqliteEntryStrategyEvaluationRepository(db);
            await repository.InsertAsync(first);
            await repository.InsertAsync(second);
            await repository.InsertAsync(otherHolding);
            var history = await repository.GetHistoryByHoldingAsync(HoldingId);
            Assert.Equal(new[] { second.Evaluation.EntryStrategyEvaluationId, first.Evaluation.EntryStrategyEvaluationId },
                history.Select(x => x.EntryStrategyEvaluationId));
            Assert.Equal(first.Evaluation.Context.StrategyVersion.Value, history[0].StrategyVersion);
            Assert.Equal(first.Evaluation.Context.Indicators.IndicatorCalculationVersion.Value, history[0].IndicatorCalculationVersion);
            Assert.Equal(first.Evaluation.Context.Configuration.Version, history[0].ConfigurationVersion);
        }

        await using var countDb = CreateContext();
        Assert.Equal(3, await countDb.EntryStrategyEvaluations.CountAsync());
    }

    [Fact]
    public async Task ReusingAnExistingEvaluationIdCannotReplaceHistoricalContents()
    {
        var first = Persisted(calculatedAt: EvaluationAt.AddMinutes(1));
        var replacement = first with
        {
            Evaluation = first.Evaluation with { Explanations = ["different historical content"] }
        };
        await using var db = CreateContext();
        var repository = new SqliteEntryStrategyEvaluationRepository(db);
        await repository.InsertAsync(first);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.InsertAsync(replacement));
        var actual = await repository.GetByIdAsync(first.Evaluation.EntryStrategyEvaluationId);
        Assert.Equal(first.Evaluation.Explanations, actual!.Evaluation.Explanations);
    }

    [Fact]
    public async Task MutatingCurrentHoldingIndicatorsMarketAndEarningsConfigurationCannotChangeHistory()
    {
        var expected = Persisted();
        await using (var db = CreateContext())
        {
            await new SqliteEntryStrategyEvaluationRepository(db).InsertAsync(expected);
            var account = new Account("Changed", Broker.Other, AccountType.Taxable, false);
            db.Add(new Holding(account, "MSFT", AssetType.Stock, 100m, AssignmentSensitivity.Level1,
                TaxSensitivity.High, 1m, .1, .01, .05, 20, 30, 0m, .01, 0));
            db.IndicatorSnapshots.Add(new IndicatorSnapshotEntity
            {
                Symbol = "MSFT", AsOfDate = expected.Evaluation.Context.IndicatorAsOfDate,
                IndicatorCalculationVersion = expected.Evaluation.Context.Indicators.IndicatorCalculationVersion.Value,
                ConfigurationVersion = expected.Evaluation.Context.Configuration.Version.Value,
                CalculatedAt = EvaluationAt.AddHours(1), Iv30Value = 999, SnapshotJson = "{\"mutated\":true}"
            });
            await db.SaveChangesAsync();
            await new SqliteMarketDataCache(db).SaveOptionChainAsync(new OptionChain("MSFT", new DateOnly(2026, 10, 16),
                EvaluationAt.AddHours(1), [ToSnapshot(expected.SelectedContracts[0])], "Tradier"));
        }

        await using var verify = CreateContext();
        var actual = (await new SqliteEntryStrategyEvaluationRepository(verify)
            .GetByIdAsync(expected.Evaluation.EntryStrategyEvaluationId))!;

        Assert.Equal(expected.Evaluation.Context.Holding, actual.Evaluation.Context.Holding);
        Assert.Equal(expected.Evaluation.Context.Indicators, actual.Evaluation.Context.Indicators);
        Assert.Equal(expected.Evaluation.Context.Earnings, actual.Evaluation.Context.Earnings);
        Assert.Equal(JsonSerializer.Serialize(expected.Evaluation.Context.Configuration),
            JsonSerializer.Serialize(actual.Evaluation.Context.Configuration));
        Assert.Equal(expected.SelectedOptionChains.Select(x => (x.Expiration, x.Timestamp, x.Contracts.Count)),
            actual.SelectedOptionChains.Select(x => (x.Expiration, x.Timestamp, x.Contracts.Count)));
        Assert.Equal(expected.Evaluation.Contracts[0].Rank, actual.Evaluation.Contracts[0].Rank);
        Assert.Equal(expected.Evaluation.DispositionReason, actual.Evaluation.DispositionReason);
    }

    private OptionsEngineDbContext CreateContext() => new(new DbContextOptionsBuilder<OptionsEngineDbContext>()
        .UseSqlite($"Data Source={path}").Options);

    private static PersistedEntryStrategyEvaluation Persisted(Guid? id = null, DateTimeOffset? calculatedAt = null,
        Guid? holdingId = null)
    {
        var holding = new HoldingContext(holdingId ?? EntryStrategyEvaluationPersistenceTests.HoldingId, "MSFT", AssetType.Stock,
            AssignmentSensitivity.Level3, TaxSensitivity.Moderate, .25, .12, .18, 70, 80, .1m, .1);
        var indicators = new IndicatorContext("MSFT", new DateOnly(2026, 9, 17), Available(1d), Available(50d),
            Available(.8d), Missing<double>(), Available(.95d), Available(.1d), Available(100m), Available(99m),
            Available(95m), Missing<double>(), Available(105m), Available(.02d), Available(1), Available(20), null,
            MarketRegime.Neutral, MarketRegime.Neutral, new IndicatorCalculationVersion("3.0.0"), new ConfigurationVersion(1),
            EvaluationAt.AddMinutes(-5)) { IvRank = Available(50d) };
        var configuration = new EntryStrategyConfiguration { Version = new ConfigurationVersion(1) };
        var context = new EvaluationContext(holding, indicators, new EarningsContext(AvailabilityStatus.Unavailable, null),
            indicators.AsOfDate, EvaluationAt, configuration, new StrategyVersion("4.0.0"));
        var input = new ScoreInput("RSI14", AvailabilityStatus.Unavailable, null);
        var component = new ScoreComponentResult(ScoreComponentCode.CcosRsi, "RSI", ScoreStatus.Unavailable, null, 15,
            [input], [MissingInputCode.Rsi14], "RSI unavailable");
        var ccos = new ScoreResult(ScoreStatus.Unavailable, null, 100, null, 70, null, [component], [MissingInputCode.Rsi14], "CCOS unavailable");
        var gate = new GateResult(GateCode.BreakoutVeto, GateStatus.Unavailable, RejectionReasonCode.InsufficientData,
            [input], [MissingInputCode.Rsi14], "Breakout inputs unavailable");
        var contract = new OptionContractContext("MSFT-C", "MSFT", EvaluationAt.AddMinutes(-1), new DateOnly(2026, 10, 16),
            105m, OptionContractType.Call, 2m, 2.1m, 500, .25, .2, -.06, 100m, 2m, 10, "Tradier");
        var contractEvaluation = new ContractEvaluation(contract, new ContractDerivedMetrics(28, 2m, 2.05m, .0488, 5m,
            .05, .02, .0007, .25), [gate], null, false, false, null, [MissingInputCode.Rsi14], ["Rejected"]);
        var evaluation = new EntryStrategyEvaluation(id ?? Guid.NewGuid(), calculatedAt ?? EvaluationAt.AddMinutes(1), context, ccos,
            [gate], [contractEvaluation], false, null, null, null, null, DispositionReasonCode.InsufficientData,
            [MissingInputCode.Rsi14], ["Insufficient data"]);
        var selected = new[]
        {
            new OptionChain("MSFT", new DateOnly(2026, 10, 16), contract.ObservationTimestampUtc, [ToSnapshot(contract)], "Tradier"),
            new OptionChain("MSFT", new DateOnly(2026, 10, 23), EvaluationAt, [], "Tradier")
        };
        return new PersistedEntryStrategyEvaluation(evaluation, selected, [contract]);
    }

    private static PersistedEntryStrategyEvaluation Successful()
    {
        var baseValue = Persisted();
        var componentCodes = new[]
        {
            ScoreComponentCode.CcosVolatility, ScoreComponentCode.CcosRsi, ScoreComponentCode.CcosBollinger,
            ScoreComponentCode.CcosTrendMomentum, ScoreComponentCode.CcosResistanceStructure, ScoreComponentCode.CcosMarketSectorRegime
        };
        var ccosComponents = componentCodes.Select(code => new ScoreComponentResult(code, code.ToString(), ScoreStatus.Available,
            14d, code == ScoreComponentCode.CcosVolatility ? 25d : 15d, [new ScoreInput(code.ToString(), AvailabilityStatus.Available, "1")], [], "Available")).ToArray();
        var ccos = new ScoreResult(ScoreStatus.Available, 84, 100, "Strong", 70, true, ccosComponents, [], "Strong CCOS");
        var gates = new[] { GateCode.Dte, GateCode.StrikeOtM, GateCode.MaximumDelta, GateCode.Earnings,
            GateCode.Liquidity, GateCode.PremiumFloor, GateCode.AnnualizedYieldFloor }
            .Select(code => new GateResult(code, GateStatus.Passed, null, [], [], "Passed")).ToArray();
        var scoreCodes = new[]
        {
            (ScoreComponentCode.ContractDelta, "Delta", 25d, 20d),
            (ScoreComponentCode.ContractStrikeSafety, "Strike Safety", 20d, 16d),
            (ScoreComponentCode.ContractPremiumEfficiency, "Premium Efficiency", 20d, 18d),
            (ScoreComponentCode.ContractDteEfficiency, "DTE Efficiency", 10d, 9d),
            (ScoreComponentCode.ContractVolatilityEdge, "Volatility Edge", 10d, 8d),
            (ScoreComponentCode.ContractLiquidity, "Liquidity", 10d, 9d),
            (ScoreComponentCode.ContractThetaEfficiency, "Theta Efficiency", 5d, 4d)
        };
        var scoreComponents = scoreCodes.Select(x => new ScoreComponentResult(x.Item1, x.Item2, ScoreStatus.Available,
            x.Item4, x.Item3, [new ScoreInput(x.Item2, AvailabilityStatus.Available, x.Item4.ToString("0"))], [], "Scored")).ToArray();
        var contractScore = new ScoreResult(ScoreStatus.Available, 84, 100, "Good", 80, true, scoreComponents, [], "Good contract score");
        var contract = baseValue.Evaluation.Contracts[0] with
        {
            Gates = gates, ContractScore = contractScore, HardGateEligible = true, EntryAcceptable = true, Rank = 1,
            MissingInputs = [], Explanations = ["Accepted"]
        };
        var context = baseValue.Evaluation.Context with { Earnings = new EarningsContext(AvailabilityStatus.Available, new DateOnly(2027, 1, 1)) };
        var evaluation = baseValue.Evaluation with
        {
            Context = context, Ccos = ccos, UnderlyingGates = [new GateResult(GateCode.BreakoutVeto, GateStatus.Passed, null, [], [], "Passed")],
            Contracts = [contract], EntryCandidateExists = true, PreferredInitialOptionSymbol = "MSFT-C",
            PreferredInitialStrike = 105m, PreferredInitialExpiration = new DateOnly(2026, 10, 16),
            PreferredInitialReferencePremium = 2m, DispositionReason = DispositionReasonCode.EntryCandidate,
            MissingInputs = [], Explanations = ["Entry candidate"]
        };
        return baseValue with { Evaluation = evaluation };
    }

    private static OptionContractSnapshot ToSnapshot(OptionContractContext x) => new(x.OptionSymbol, x.UnderlyingSymbol,
        x.ObservationTimestampUtc, x.Expiration, x.Strike, OptionType.Call, x.Bid, x.Ask, x.Last, x.Volume, x.OpenInterest,
        x.ImpliedVolatility, x.Delta, null, x.Theta, null, x.UnderlyingPrice, x.Provider);
    private static IndicatorValue<T> Available<T>(T value) where T : struct => IndicatorValue<T>.Available(value);
    private static IndicatorValue<T> Missing<T>() where T : struct => IndicatorValue<T>.InsufficientData();
}

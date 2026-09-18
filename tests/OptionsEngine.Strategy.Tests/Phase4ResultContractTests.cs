using OptionsEngine.Domain.Accounts;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Strategy.Tests;

public sealed class Phase4ResultContractTests
{
    [Fact]
    public void ScoreThresholdsAreSeparateFromApprovedHardGates()
    {
        Assert.DoesNotContain("CcosMinimum", Enum.GetNames<GateCode>());
        Assert.DoesNotContain("ContractScoreMinimum", Enum.GetNames<GateCode>());
        Assert.Equal(new[] { GateCode.BreakoutVeto, GateCode.Dte, GateCode.StrikeOtM,
            GateCode.MaximumDelta, GateCode.Earnings, GateCode.Liquidity, GateCode.PremiumFloor,
            GateCode.AnnualizedYieldFloor }, Enum.GetValues<GateCode>());
        Assert.Equal(new[] { DispositionReasonCode.EntryCandidate, DispositionReasonCode.CcosBelowMinimum,
            DispositionReasonCode.BreakoutVeto, DispositionReasonCode.InsufficientData,
            DispositionReasonCode.NoAcceptableContract }, Enum.GetValues<DispositionReasonCode>());

        var score = new ScoreResult(ScoreStatus.Available, 75, 100, "SELL CANDIDATE", 80, false, [], [], "Below holding threshold.");
        Assert.False(score.MeetsConfiguredMinimum);
        Assert.Equal(80, score.ConfiguredMinimum);
    }

    [Fact]
    public void ContractAndEvaluationRetainDecisionsAndDistinctObservationTimes()
    {
        var indicatorAsOf = new DateOnly(2026, 9, 17);
        var indicatorCalculatedAt = new DateTimeOffset(2026, 9, 18, 1, 0, 0, TimeSpan.Zero);
        var evaluationTimestamp = new DateTimeOffset(2026, 9, 18, 15, 0, 0, TimeSpan.Zero);
        var calculatedAt = evaluationTimestamp.AddMinutes(3);
        var observationTimestamp = evaluationTimestamp.AddMinutes(-2);
        var indicator = new IndicatorContext("MSFT", indicatorAsOf,
            Missing<double>(), Missing<double>(), Missing<double>(), Missing<double>(), Missing<double>(), Missing<double>(),
            Missing<decimal>(), Missing<decimal>(), Missing<decimal>(), Missing<double>(), Missing<decimal>(), Missing<double>(),
            Missing<int>(), Missing<int>(), ResistanceUnavailableReason.InsufficientData,
            MarketRegime.InsufficientData, MarketRegime.InsufficientData,
            new IndicatorCalculationVersion("3.0.0"), new ConfigurationVersion(1), indicatorCalculatedAt);
        var holding = new HoldingContext(Guid.Parse("1B539C36-CA1A-4FB0-B34A-84C821258AAB"), "MSFT", AssetType.Stock, AssignmentSensitivity.Level3,
            TaxSensitivity.Moderate, .25, .12, .18, 70, 80, .10m, .10);
        var context = new EvaluationContext(holding, indicator, new EarningsContext(AvailabilityStatus.Unavailable, null),
            indicatorAsOf, evaluationTimestamp, new EntryStrategyConfiguration { Version = new ConfigurationVersion(1) },
            new StrategyVersion("1.0.0"));
        context.Validate();

        var contract = new OptionContractContext("MSFT261023C00500000", "MSFT", observationTimestamp,
            new DateOnly(2026, 10, 23), 500m, OptionContractType.Call, 2.15m, 2.25m, 250,
            .28, .12, -.04, 450m, 2.20m, 125, "TestProvider");
        var contractResult = new ContractEvaluation(contract,
            new ContractDerivedMetrics(null, null, null, null, null, null, null, null, null),
            [], null, true, false, null, [], []);
        var result = new EntryStrategyEvaluation(Guid.Parse("50E726D6-18B6-4E41-92B0-D33FD50116D2"), calculatedAt, context, null, [], [contractResult],
            false, null, null, null, null, DispositionReasonCode.NoAcceptableContract, [], []);

        Assert.Equal(indicatorCalculatedAt, result.Context.Indicators.IndicatorCalculatedAtUtc);
        Assert.Equal(evaluationTimestamp, result.Context.EvaluationTimestampUtc);
        Assert.Equal(calculatedAt, result.CalculatedAtUtc);
        Assert.Equal(observationTimestamp, result.Contracts[0].Contract.ObservationTimestampUtc);
        Assert.Equal(2.20m, result.Contracts[0].Contract.Last);
        Assert.Equal(125, result.Contracts[0].Contract.Volume);
        Assert.Equal("TestProvider", result.Contracts[0].Contract.Provider);
        Assert.True(result.Contracts[0].HardGateEligible);
        Assert.False(result.Contracts[0].EntryAcceptable);
    }

    private static IndicatorValue<T> Missing<T>() where T : struct => IndicatorValue<T>.InsufficientData();
}

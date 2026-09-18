using OptionsEngine.Domain.Accounts;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Strategy.Tests;

public sealed class ContractStrategyCalculatorTests
{
    private static readonly ContractStrategyCalculator Calculator = new();
    private static readonly DateOnly EvaluationDate = new(2026, 9, 18);
    private static readonly DateOnly Expiration = new(2026, 10, 16);

    [Fact]
    public void DerivedMetricsUseBidAndChainUnderlyingPrice()
    {
        var contract = Contract() with { Bid = 2m, Ask = 2.2m, Last = 9m, UnderlyingPrice = 100m, Strike = 105m };
        var metrics = ContractStrategyCalculator.Derive(contract, EvaluationDate);

        Assert.Equal(28, metrics.Dte);
        Assert.Equal(2m, metrics.ReferencePremium);
        Assert.Equal(2.1m, metrics.Mid);
        Assert.Equal((double)(.2m / 2.1m), metrics.BidAskSpreadPercent);
        Assert.Equal(5m, metrics.StrikeDistance);
        Assert.Equal(.05, metrics.OtmPercent);
        Assert.Equal(.02, metrics.PremiumYield);
        Assert.Equal(.02 / 28, metrics.DailyPremiumYield);
        Assert.Equal(.02 / 28 * 365, metrics.AnnualizedPremiumYield);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, -1)]
    [InlineData(28, 28)]
    public void DteUsesProvidedEvaluationDate(int offset, int expected)
    {
        Assert.Equal(expected, ContractStrategyCalculator.Derive(Contract() with { Expiration = EvaluationDate.AddDays(offset) }, EvaluationDate).Dte);
    }

    [Fact]
    public void EvaluationDateUsesNewYorkCalendarAndDaylightSaving()
    {
        var context = Context() with { EvaluationTimestampUtc = new DateTimeOffset(2026, 9, 19, 1, 0, 0, TimeSpan.Zero) };
        var contract = Contract() with { Expiration = EvaluationDate.AddDays(14) };

        Assert.Equal(14, Only(Calculator.Evaluate(context, [contract])).DerivedMetrics.Dte);
    }

    [Fact]
    public void EvaluationDateUsesNewYorkCalendarInStandardTime()
    {
        var context = Context() with { EvaluationTimestampUtc = new DateTimeOffset(2027, 1, 3, 2, 0, 0, TimeSpan.Zero) };
        var contract = Contract() with { Expiration = new DateOnly(2027, 1, 16) };

        Assert.Equal(14, Only(Calculator.Evaluate(context, [contract])).DerivedMetrics.Dte);
    }

    [Theory]
    [InlineData("price")]
    [InlineData("bid")]
    [InlineData("zero-price")]
    public void MissingOrInvalidRawValuesDoNotBecomeDerivedZero(string field)
    {
        var contract = field switch
        {
            "price" => Contract() with { UnderlyingPrice = null },
            "bid" => Contract() with { Bid = null },
            _ => Contract() with { UnderlyingPrice = 0m }
        };
        var metrics = ContractStrategyCalculator.Derive(contract, EvaluationDate);
        if (field == "bid")
        {
            Assert.Null(metrics.ReferencePremium);
            Assert.Null(metrics.Mid);
            Assert.Null(metrics.PremiumYield);
        }
        else
        {
            Assert.Null(metrics.StrikeDistance);
            Assert.Null(metrics.OtmPercent);
            Assert.Null(metrics.PremiumYield);
            Assert.Null(metrics.AnnualizedPremiumYield);
        }
    }

    [Theory]
    [InlineData(13, GateStatus.Failed)]
    [InlineData(14, GateStatus.Passed)]
    [InlineData(45, GateStatus.Passed)]
    [InlineData(46, GateStatus.Failed)]
    public void DteGateIsInclusive(int dte, GateStatus status)
    {
        var gate = Gate(Only(Evaluate(Contract() with { Expiration = EvaluationDate.AddDays(dte) })), GateCode.Dte);
        Assert.Equal(status, gate.Status);
        Assert.Equal(status == GateStatus.Failed ? RejectionReasonCode.DteOutsideRange : null, gate.ReasonCode);
    }

    [Theory]
    [InlineData(99.99, GateStatus.Failed)]
    [InlineData(100, GateStatus.Failed)]
    [InlineData(100.01, GateStatus.Passed)]
    public void StrikeGateRequiresStrictlyOtm(decimal strike, GateStatus status)
    {
        var gate = Gate(Only(Evaluate(Contract() with { Strike = strike })), GateCode.StrikeOtM);
        Assert.Equal(status, gate.Status);
        Assert.Equal(status == GateStatus.Failed ? RejectionReasonCode.StrikeNotOtm : null, gate.ReasonCode);
    }

    [Theory]
    [InlineData(.2499, GateStatus.Passed)]
    [InlineData(.25, GateStatus.Passed)]
    [InlineData(.2501, GateStatus.Failed)]
    [InlineData(0, GateStatus.Passed)]
    [InlineData(-.0001, GateStatus.Unavailable)]
    [InlineData(1.0001, GateStatus.Unavailable)]
    public void MaximumDeltaGateUsesInclusiveLimitAndRejectsInvalidValues(double delta, GateStatus status)
    {
        var gate = Gate(Only(Evaluate(Contract() with { Delta = delta })), GateCode.MaximumDelta);
        Assert.Equal(status, gate.Status);
        Assert.Equal(status == GateStatus.Failed ? RejectionReasonCode.DeltaExceedsMaximum
            : status == GateStatus.Unavailable ? RejectionReasonCode.InsufficientData : null, gate.ReasonCode);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteDeltaIsUnavailable(double delta)
    {
        var gate = Gate(Only(Evaluate(Contract() with { Delta = delta })), GateCode.MaximumDelta);
        Assert.Equal(GateStatus.Unavailable, gate.Status);
        Assert.Contains(MissingInputCode.OptionDelta, gate.MissingInputs);
    }

    [Fact]
    public void HoldingAndHighTaxCapsTightenDeltaWithoutChangingTheGlobalMaximum()
    {
        var contract = Contract() with { Delta = .21 };
        Assert.Equal(GateStatus.Passed, Gate(Only(Evaluate(contract)), GateCode.MaximumDelta).Status);
        Assert.Equal(GateStatus.Failed, Gate(Only(Evaluate(contract, Context() with { Holding = Holding() with { MaximumInitialDelta = .20 } })), GateCode.MaximumDelta).Status);
        Assert.Equal(GateStatus.Failed, Gate(Only(Evaluate(contract, Context() with { Holding = Holding() with { TaxSensitivity = TaxSensitivity.High } })), GateCode.MaximumDelta).Status);
        Assert.Equal(GateStatus.Passed, Gate(Only(Evaluate(contract with { Delta = .20 }, Context() with { Holding = Holding() with { TaxSensitivity = TaxSensitivity.High } })), GateCode.MaximumDelta).Status);
        Assert.Equal(GateStatus.Passed, Gate(Only(Evaluate(contract with { Delta = 1 }, Context() with
        {
            Holding = Holding() with { MaximumInitialDelta = 1, PreferredDeltaMinimum = .12, PreferredDeltaMaximum = .18 },
            Configuration = Configuration() with { ContractEligibility = new ContractEligibilityConfiguration { GlobalMaximumInitialDelta = 1 } }
        })), GateCode.MaximumDelta).Status);
    }

    [Theory]
    [InlineData(-1, GateStatus.Failed)]
    [InlineData(0, GateStatus.Failed)]
    [InlineData(1, GateStatus.Passed)]
    public void StockEarningsOnOrBeforeExpirationFail(int dayOffset, GateStatus status)
    {
        var context = Context() with { Earnings = new EarningsContext(AvailabilityStatus.Available, Expiration.AddDays(dayOffset)) };
        Assert.Equal(status, Gate(Only(Evaluate(Contract(), context)), GateCode.Earnings).Status);
    }

    [Fact]
    public void MissingStockEarningsAndEtfNotApplicableAreDistinct()
    {
        var missing = Gate(Only(Evaluate(Contract(), Context() with { Earnings = new EarningsContext(AvailabilityStatus.Unavailable, null) })), GateCode.Earnings);
        var etfContext = Context() with { Holding = Holding() with { AssetType = AssetType.ExchangeTradedFund },
            Earnings = new EarningsContext(AvailabilityStatus.NotApplicable, null) };
        var etf = Gate(Only(Evaluate(Contract(), etfContext)), GateCode.Earnings);
        Assert.Equal(GateStatus.Unavailable, missing.Status);
        Assert.Equal(RejectionReasonCode.InsufficientData, missing.ReasonCode);
        Assert.Contains(MissingInputCode.EarningsDate, missing.MissingInputs);
        Assert.Equal(GateStatus.NotApplicable, etf.Status);
    }

    [Theory]
    [InlineData(0, 2.1, 500, GateStatus.Failed)]
    [InlineData(-.01, 2.1, 500, GateStatus.Failed)]
    [InlineData(2, 2, 500, GateStatus.Failed)]
    [InlineData(2, 1.9, 500, GateStatus.Failed)]
    [InlineData(2, 2.1, 99, GateStatus.Failed)]
    [InlineData(2, 2.1, 100, GateStatus.Passed)]
    [InlineData(2, 2.4444, 500, GateStatus.Passed)]
    [InlineData(2, 2.5, 500, GateStatus.Failed)]
    public void LiquidityGateRetainsThresholdResults(decimal bid, decimal ask, long oi, GateStatus status)
    {
        var gate = Gate(Only(Evaluate(Contract() with { Bid = bid, Ask = ask, OpenInterest = oi })), GateCode.Liquidity);
        Assert.Equal(status, gate.Status);
        Assert.Equal(status == GateStatus.Failed ? RejectionReasonCode.InsufficientLiquidity : null, gate.ReasonCode);
    }

    [Theory]
    [InlineData(.1999, GateStatus.Passed)]
    [InlineData(.20, GateStatus.Passed)]
    [InlineData(.2001, GateStatus.Failed)]
    public void LiquiditySpreadTwentyPercentBoundaryIsInclusive(decimal spread, GateStatus status)
    {
        var contract = Contract() with { Bid = 2m - spread, Ask = 2m + spread };
        var gate = Gate(Only(Evaluate(contract)), GateCode.Liquidity);
        Assert.Equal(status, gate.Status);
    }

    [Theory]
    [InlineData("bid", MissingInputCode.OptionBid)]
    [InlineData("ask", MissingInputCode.OptionAsk)]
    [InlineData("oi", MissingInputCode.OptionOpenInterest)]
    public void MissingLiquidityInputsAreReportedPrecisely(string field, MissingInputCode code)
    {
        var contract = field switch { "bid" => Contract() with { Bid = null }, "ask" => Contract() with { Ask = null },
            _ => Contract() with { OpenInterest = null } };
        var gate = Gate(Only(Evaluate(contract)), GateCode.Liquidity);
        Assert.Equal(GateStatus.Unavailable, gate.Status);
        Assert.Contains(code, gate.MissingInputs);
    }

    [Fact]
    public void InvalidUnderlyingPriceMakesStrikeAndYieldGatesUnavailableWithExactCode()
    {
        var result = Only(Evaluate(Contract() with { UnderlyingPrice = 0m }));
        foreach (var code in new[] { GateCode.StrikeOtM, GateCode.AnnualizedYieldFloor })
        {
            var gate = Gate(result, code);
            Assert.Equal(GateStatus.Unavailable, gate.Status);
            Assert.Contains(MissingInputCode.OptionUnderlyingPrice, gate.MissingInputs);
        }
    }

    [Fact]
    public void MultipleDeterminableFailuresAndUnavailableGatesAreRetained()
    {
        var contract = Contract() with { Strike = 99m, Delta = .3, Ask = null, Expiration = EvaluationDate.AddDays(13) };
        var result = Only(Evaluate(contract));
        Assert.Equal(7, result.Gates.Count);
        Assert.Equal(3, result.Gates.Count(x => x.Status == GateStatus.Failed));
        Assert.Contains(result.Gates, x => x.Status == GateStatus.Unavailable);
        Assert.False(result.HardGateEligible);
        Assert.Null(result.Rank);
    }

    [Theory]
    [InlineData(2.001, GateStatus.Failed)]
    [InlineData(2, GateStatus.Passed)]
    [InlineData(1.999, GateStatus.Passed)]
    public void PremiumFloorIncludesEquality(decimal minimum, GateStatus status)
    {
        var result = Only(Evaluate(Contract(), Context() with { Holding = Holding() with { MinimumPremium = minimum } }));
        Assert.Equal(status, Gate(result, GateCode.PremiumFloor).Status);
    }

    [Fact]
    public void YieldFloorIncludesEquality()
    {
        var annualized = ContractStrategyCalculator.Derive(Contract(), EvaluationDate).AnnualizedPremiumYield!.Value;
        foreach (var (minimum, status) in new[] { (Math.BitIncrement(annualized), GateStatus.Failed),
                     (annualized, GateStatus.Passed), (Math.BitDecrement(annualized), GateStatus.Passed) })
            Assert.Equal(status, Gate(Only(Evaluate(Contract(), Context() with { Holding = Holding() with { MinimumAnnualizedYield = minimum } })), GateCode.AnnualizedYieldFloor).Status);
    }

    [Theory]
    [InlineData(.1199, 20)]
    [InlineData(.12, 25)]
    [InlineData(.1201, 25)]
    [InlineData(.15, 25)]
    [InlineData(.1799, 25)]
    [InlineData(.18, 25)]
    [InlineData(.1801, 10)]
    public void DeltaScoreUsesConfiguredPreferredRange(double delta, double expected)
    {
        Assert.Equal(expected, Component(Only(Evaluate(Contract() with { Delta = delta })), ScoreComponentCode.ContractDelta).Score);
    }

    [Fact]
    public void EffectivePreferredMaximumIsCappedByTheEffectiveHardGateMaximum()
    {
        var context = Context() with { Holding = Holding() with
        {
            PreferredDeltaMaximum = .25, TaxSensitivity = TaxSensitivity.High
        } };
        var eligible = Only(Evaluate(Contract() with { Delta = .20 }, context));
        var rejected = Only(Evaluate(Contract() with { Delta = .2001 }, context));
        Assert.Equal(25, Component(eligible, ScoreComponentCode.ContractDelta).Score);
        Assert.Equal(GateStatus.Failed, Gate(rejected, GateCode.MaximumDelta).Status);
    }

    [Theory]
    [InlineData(1.00, 0)]
    [InlineData(1.0999, 0)] [InlineData(1.10, 5)] [InlineData(1.1001, 5)]
    [InlineData(1.2499, 5)] [InlineData(1.25, 10)] [InlineData(1.2501, 10)]
    [InlineData(1.4999, 10)] [InlineData(1.50, 15)] [InlineData(1.5001, 15)]
    [InlineData(1.9999, 15)] [InlineData(2.00, 20)] [InlineData(2.0001, 20)]
    public void PremiumEfficiencyScoresConfiguredRatios(double targetRatio, double expected)
    {
        var annualized = ContractStrategyCalculator.Derive(Contract(), EvaluationDate).AnnualizedPremiumYield!.Value;
        var minimum = annualized / targetRatio;
        var result = Only(Evaluate(Contract(), Context() with { Holding = Holding() with { MinimumAnnualizedYield = minimum } }));
        Assert.Equal(expected, Component(result, ScoreComponentCode.ContractPremiumEfficiency).Score);
    }

    [Theory]
    [MemberData(nameof(StrikeCases))]
    public void StrikeSafetyScoresEveryBoundary(decimal otm, double expected)
    {
        var contract = Contract() with { Strike = 100m * (1m + otm) };
        Assert.Equal(expected, Component(Only(Evaluate(contract)), ScoreComponentCode.ContractStrikeSafety).Score);
    }

    [Theory]
    [InlineData(14, 5)] [InlineData(15, 5)] [InlineData(19, 5)] [InlineData(20, 5)]
    [InlineData(21, 10)] [InlineData(22, 10)] [InlineData(34, 10)] [InlineData(35, 10)]
    [InlineData(36, 8)] [InlineData(37, 8)] [InlineData(44, 8)] [InlineData(45, 8)]
    public void DteEfficiencyMatchesConfiguredBands(int dte, double expected)
    {
        Assert.Equal(expected, Component(Only(Evaluate(Contract() with { Expiration = EvaluationDate.AddDays(dte) })), ScoreComponentCode.ContractDteEfficiency).Score);
    }

    [Theory]
    [MemberData(nameof(VolatilityCases))]
    public void VolatilityEdgeScoresAllBoundaries(double ratio, double expected)
    {
        Assert.Equal(expected, Component(Only(Evaluate(Contract() with { ImpliedVolatility = ratio * .2 })), ScoreComponentCode.ContractVolatilityEdge).Score);
    }

    [Theory]
    [InlineData(null, .2)] [InlineData(0d, .2)] [InlineData(-.1, .2)]
    [InlineData(double.NaN, .2)] [InlineData(double.PositiveInfinity, .2)]
    [InlineData(.3, null)] [InlineData(.3, 0d)] [InlineData(.3, -.1)]
    [InlineData(.3, double.NaN)] [InlineData(.3, double.PositiveInfinity)]
    public void InvalidContractIvOrRv30MakesOnlyThatContractScoreUnavailable(double? iv, double? rv30)
    {
        var indicators = Indicators() with { RealizedVolatility30 = rv30 is { } value ? Available(value) : Missing<double>() };
        var result = Only(Evaluate(Contract() with { ImpliedVolatility = iv }, Context() with { Indicators = indicators }));
        Assert.True(result.HardGateEligible);
        Assert.Equal(ScoreStatus.Unavailable, result.ContractScore?.Status);
        Assert.Null(result.ContractScore?.Score);
        Assert.Null(result.Rank);
        Assert.Contains(iv is null or <= 0 || iv is { } ivValue && !double.IsFinite(ivValue)
                ? MissingInputCode.OptionImpliedVolatility : MissingInputCode.RealizedVolatility30,
            result.ContractScore!.MissingInputs);
        Assert.Equal(ScoreStatus.Available, Component(result, ScoreComponentCode.ContractDelta).Status);
    }

    [Theory]
    [MemberData(nameof(LiquiditySpreadCases))]
    public void LiquiditySpreadScoreMatchesEveryBoundary(decimal spread, double expected)
    {
        Assert.Equal(expected, Component(Only(Evaluate(Contract() with { Bid = 2m - spread, Ask = 2m + spread, OpenInterest = 100 })), ScoreComponentCode.ContractLiquidity).Score);
    }

    [Theory]
    [InlineData(100, 6)] [InlineData(101, 6)] [InlineData(249, 6)]
    [InlineData(250, 7)] [InlineData(251, 7)] [InlineData(499, 7)]
    [InlineData(500, 8)] [InlineData(501, 8)] [InlineData(999, 8)]
    [InlineData(1000, 9)] [InlineData(1001, 9)] [InlineData(1999, 9)]
    [InlineData(2000, 10)] [InlineData(2001, 10)] [InlineData(2147483648L, 10)]
    public void LiquidityOpenInterestScoreMatchesBands(long oi, double expected)
    {
        Assert.Equal(expected, Component(Only(Evaluate(Contract() with { OpenInterest = oi })), ScoreComponentCode.ContractLiquidity).Score);
    }

    [Theory]
    [InlineData(null)] [InlineData(0d)] [InlineData(.01)] [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidThetaMakesContractScoreUnavailable(double? theta)
    {
        var result = Only(Evaluate(Contract() with { Theta = theta }));
        var component = Component(result, ScoreComponentCode.ContractThetaEfficiency);
        Assert.Equal(ScoreStatus.Unavailable, component.Status);
        Assert.Null(result.ContractScore?.Score);
        Assert.Contains(MissingInputCode.OptionTheta, result.ContractScore!.MissingInputs);
        var input = Assert.Single(component.Inputs, x => x.Code == "THETA");
        Assert.Equal(AvailabilityStatus.Unavailable, input.Status);
        Assert.Equal(theta?.ToString("G17", System.Globalization.CultureInfo.InvariantCulture), input.Value);
    }

    [Theory]
    [InlineData(.0099, 0)] [InlineData(.01, 1)] [InlineData(.0101, 1)]
    [InlineData(.0199, 1)] [InlineData(.02, 2)] [InlineData(.0201, 2)]
    [InlineData(.0299, 2)] [InlineData(.03, 3)] [InlineData(.0301, 3)]
    [InlineData(.0399, 3)] [InlineData(.04, 4)] [InlineData(.0401, 4)]
    [InlineData(.0499, 4)] [InlineData(.05, 5)] [InlineData(.0501, 5)]
    public void ThetaEfficiencyScoresAllBoundaries(double ratio, double expected)
    {
        var result = Only(Evaluate(Contract() with { Theta = -2 * ratio }));
        Assert.Equal(expected, Component(result, ScoreComponentCode.ContractThetaEfficiency).Score);
    }

    [Theory]
    [MemberData(nameof(ClassificationCases))]
    public void ContractScoreClassificationUsesConfiguredBoundaries(double score, string expected)
    {
        Assert.Equal(expected, ContractStrategyCalculator.Classify(score, new ContractScoreClassificationConfiguration()));
    }

    [Fact]
    public void PutsAreExcludedAndRankedCallsAreIndependentOfInputOrder()
    {
        var a = Contract() with { OptionSymbol = "A", Delta = .12 };
        var b = Contract() with { OptionSymbol = "B", Delta = .13 };
        var put = Contract() with { OptionSymbol = "PUT", OptionType = OptionContractType.Put };
        var forward = Calculator.Evaluate(Context(), [b, put, a]);
        var reverse = Calculator.Evaluate(Context(), [a, b, put]);
        Assert.Equal(new[] { "A", "B" }, forward.Contracts.OrderBy(x => x.Rank).Select(x => x.Contract.OptionSymbol));
        Assert.Equal(forward.Contracts.OrderBy(x => x.Rank).Select(x => x.Contract.OptionSymbol),
            reverse.Contracts.OrderBy(x => x.Rank).Select(x => x.Contract.OptionSymbol));
        Assert.DoesNotContain(forward.Contracts, x => x.Contract.OptionType == OptionContractType.Put);
    }

    [Theory]
    [InlineData("score")] [InlineData("delta")] [InlineData("otm")] [InlineData("yield")]
    [InlineData("spread")] [InlineData("oi")] [InlineData("dte")] [InlineData("symbol")]
    public void RankingUsesEachApprovedKeyInOrder(string key)
    {
        var baseline = Only(Evaluate(Contract())) with { Rank = null };
        var left = baseline with { Contract = baseline.Contract with { OptionSymbol = "A" } };
        var right = baseline with { Contract = baseline.Contract with { OptionSymbol = "B" } };
        // Make symbol tie until its isolated comparison; other fields are identical.
        if (key != "symbol") right = right with { Contract = right.Contract with { OptionSymbol = "A" } };
        left = key switch
        {
            "score" => left with { ContractScore = left.ContractScore! with { Score = left.ContractScore.Score + 1 } },
            "delta" => left with { Contract = left.Contract with { Delta = .14 } },
            "otm" => left with { DerivedMetrics = left.DerivedMetrics with { OtmPercent = .06 } },
            "yield" => left with { DerivedMetrics = left.DerivedMetrics with { AnnualizedPremiumYield = .27 } },
            "spread" => left with { DerivedMetrics = left.DerivedMetrics with { BidAskSpreadPercent = .04 } },
            "oi" => left with { Contract = left.Contract with { OpenInterest = 600 } },
            "dte" => left with { DerivedMetrics = left.DerivedMetrics with { Dte = 27 } },
            _ => left
        };
        var ranked = ContractStrategyCalculator.Rank([right, left]);
        Assert.Equal(1, ranked[1].Rank);
        Assert.Equal(2, ranked[0].Rank);
    }

    [Fact]
    public void RankingExcludesFailedAndUnavailableScoresButKeepsBelowMinimumScores()
    {
        var available = Only(Evaluate(Contract())) with { EntryAcceptable = false };
        var failed = available with { Contract = available.Contract with { OptionSymbol = "FAILED" }, HardGateEligible = false };
        var unavailable = available with { Contract = available.Contract with { OptionSymbol = "UNAVAILABLE" },
            ContractScore = available.ContractScore! with { Status = ScoreStatus.Unavailable, Score = null } };
        var ranked = ContractStrategyCalculator.Rank([failed, unavailable, available]);
        Assert.Null(ranked[0].Rank);
        Assert.Null(ranked[1].Rank);
        Assert.Equal(1, ranked[2].Rank);
    }

    [Fact]
    public void InvalidCallDoesNotPoisonAnIndependentEligibleCall()
    {
        var rejected = Contract() with { OptionSymbol = "REJECTED", Bid = null };
        var valid = Contract() with { OptionSymbol = "VALID" };
        var result = Calculator.Evaluate(Context(), [rejected, valid]);

        Assert.Equal(2, result.Contracts.Count);
        Assert.Null(Assert.Single(result.Contracts, x => x.Contract.OptionSymbol == "REJECTED").Rank);
        Assert.Equal(1, Assert.Single(result.Contracts, x => x.Contract.OptionSymbol == "VALID").Rank);
        Assert.Equal("VALID", result.PreferredInitialOptionSymbol);
    }

    [Fact]
    public void DeferredInputsAndMinimumDollarPremiumDoNotAlterContractScore()
    {
        var baseline = Only(Evaluate(Contract()));
        var modifiedIndicators = Indicators() with { Iv30 = Missing<double>(), IvRank = Available(99d),
            ResistanceUnavailableReason = ResistanceUnavailableReason.NoQualifiedResistance };
        var modified = Only(Evaluate(Contract() with { Volume = 999999 }, Context() with
        {
            Indicators = modifiedIndicators,
            Holding = Holding() with { MinimumPremium = 1.9m }
        }));

        Assert.Equal(baseline.ContractScore!.Score, modified.ContractScore!.Score);
        Assert.Equal(baseline.ContractScore.Components.Select(x => x.Score), modified.ContractScore.Components.Select(x => x.Score));
    }

    [Fact]
    public void StrongEntryGoldenScenarioHasPreferredContractAndExactComponents()
    {
        var result = Evaluate(Contract());
        var candidate = Only(result);
        Assert.Equal(96, result.Ccos.Score);
        Assert.Equal(91, candidate.ContractScore!.Score);
        Assert.Equal(new double[] { 25, 15, 20, 10, 10, 8, 3 }, candidate.ContractScore.Components.Select(x => x.Score!.Value));
        Assert.True(result.EntryCandidateExists);
        Assert.Equal(DispositionReasonCode.EntryCandidate, result.DispositionReason);
        Assert.Equal(candidate.Contract.Strike, result.PreferredInitialStrike);
        Assert.Equal(candidate.Contract.OptionSymbol, result.PreferredInitialOptionSymbol);
        Assert.Equal(candidate.DerivedMetrics.ReferencePremium, result.PreferredInitialReferencePremium);
        Assert.Equal(1, candidate.Rank);
    }

    [Fact]
    public void HighAssignmentRiskGoldenScenarioRetainsRejectedCall()
    {
        var candidate = Only(Evaluate(Contract() with { Delta = .2501 }));
        Assert.Equal(RejectionReasonCode.DeltaExceedsMaximum, Gate(candidate, GateCode.MaximumDelta).ReasonCode);
        Assert.Null(candidate.Rank);
        Assert.False(candidate.HardGateEligible);
    }

    [Fact]
    public void TaxSensitivePositionGoldenScenarioAppliesHighTaxCap()
    {
        var context = Context() with { Holding = Holding() with { TaxSensitivity = TaxSensitivity.High } };
        var result = Evaluate(Contract() with { Delta = .2001 }, context);
        Assert.Equal(RejectionReasonCode.DeltaExceedsMaximum, Gate(Only(result), GateCode.MaximumDelta).ReasonCode);
        Assert.Equal(DispositionReasonCode.NoAcceptableContract, result.DispositionReason);
    }

    [Fact]
    public void IlliquidContractGoldenScenarioRetainsSpreadFailure()
    {
        var result = Evaluate(Contract() with { Ask = 2.6m });
        Assert.Equal(RejectionReasonCode.InsufficientLiquidity, Gate(Only(result), GateCode.Liquidity).ReasonCode);
        Assert.Equal(DispositionReasonCode.NoAcceptableContract, result.DispositionReason);
    }

    [Fact]
    public void EarningsBeforeExpirationGoldenScenarioRetainsFailure()
    {
        var result = Evaluate(Contract(), Context() with { Earnings = new EarningsContext(AvailabilityStatus.Available, Expiration) });
        Assert.Equal(RejectionReasonCode.EarningsBeforeExpiration, Gate(Only(result), GateCode.Earnings).ReasonCode);
        Assert.Equal(DispositionReasonCode.NoAcceptableContract, result.DispositionReason);
    }

    [Fact]
    public void NoAcceptableContractGoldenScenarioKeepsBelowMinimumRanked()
    {
        var result = Evaluate(Contract(), Context() with { Holding = Holding() with { MinimumContractScore = 95 } });
        Assert.Equal(DispositionReasonCode.NoAcceptableContract, result.DispositionReason);
        Assert.Equal(1, Only(result).Rank);
        Assert.False(Only(result).EntryAcceptable);
        Assert.Null(result.PreferredInitialOptionSymbol);
    }

    [Fact]
    public void AllUnderlyingDispositionsRetainIndependentContractAnalysis()
    {
        var below = Evaluate(Contract(), Context() with { Holding = Holding() with { MinimumCcos = 97 } });
        var veto = Evaluate(Contract(), Context() with { Indicators = Indicators() with { BollingerPercentB = Available(1.16), Rsi14 = Available(75d) } });
        var insufficient = Evaluate(Contract(), Context() with { Indicators = Indicators() with { IvPercentile = Missing<double>() } });
        Assert.Equal(DispositionReasonCode.CcosBelowMinimum, below.DispositionReason);
        Assert.Equal(DispositionReasonCode.BreakoutVeto, veto.DispositionReason);
        Assert.Equal(DispositionReasonCode.InsufficientData, insufficient.DispositionReason);
        foreach (var result in new[] { below, veto, insufficient })
        {
            Assert.False(result.EntryCandidateExists);
            Assert.Null(result.PreferredInitialOptionSymbol);
            Assert.Equal(1, Only(result).Rank);
        }
    }

    public static IEnumerable<object[]> StrikeCases()
    {
        foreach (var (boundary, below, at, above) in new[]
        {
            (.01m, 0d, 4d, 4d), (.02m, 4d, 8d, 8d), (.03m, 8d, 12d, 12d),
            (.04m, 12d, 15d, 15d), (.06m, 15d, 18d, 18d), (.08m, 18d, 18d, 20d)
        })
        {
            yield return [boundary - .0001m, below]; yield return [boundary, at]; yield return [boundary + .0001m, above];
        }
    }

    public static IEnumerable<object[]> VolatilityCases()
    {
        foreach (var (boundary, below, at, above) in new[]
        {
            (.90d, 0d, 2d, 2d), (1d, 2d, 4d, 4d), (1.10d, 4d, 6d, 6d),
            (1.20d, 6d, 8d, 8d), (1.35d, 8d, 8d, 10d)
        })
        {
            yield return [boundary - .0001d, below]; yield return [boundary, at]; yield return [boundary + .0001d, above];
        }
    }

    public static IEnumerable<object[]> LiquiditySpreadCases()
    {
        // Bid=2-s, ask=2+s gives mid=2 and an exact target spread fraction s.
        foreach (var (boundary, below, at, above) in new[]
        {
            (.05m, 5d, 5d, 4d), (.10m, 4d, 4d, 2d), (.15m, 2d, 2d, 1d)
        })
        {
            foreach (var (spread, score) in new[] { (boundary - .0001m, below), (boundary, at), (boundary + .0001m, above) })
                yield return [spread, score + 1];
        }
        yield return [.1999m, 2d]; yield return [.20m, 2d];
    }

    public static IEnumerable<object[]> ClassificationCases()
    {
        foreach (var (boundary, below, at) in new[] { (60d, "REJECT", "WEAK"), (70d, "WEAK", "ACCEPTABLE"),
                     (80d, "ACCEPTABLE", "GOOD"), (90d, "GOOD", "EXCELLENT") })
        {
            yield return [boundary - .0001d, below]; yield return [boundary, at]; yield return [boundary + .0001d, at];
        }
    }

    private static ContractStrategyResult Evaluate(OptionContractContext contract, EvaluationContext? context = null) =>
        Calculator.Evaluate(context ?? Context(), [contract]);
    private static ContractEvaluation Only(ContractStrategyResult result) => Assert.Single(result.Contracts);
    private static GateResult Gate(ContractEvaluation result, GateCode code) => Assert.Single(result.Gates, x => x.Code == code);
    private static ScoreComponentResult Component(ContractEvaluation result, ScoreComponentCode code) =>
        Assert.Single(result.ContractScore!.Components, x => x.Code == code);

    private static OptionContractContext Contract() => new("MSFT261016C00105000", "MSFT",
        new DateTimeOffset(2026, 9, 18, 14, 55, 0, TimeSpan.Zero), Expiration, 105m, OptionContractType.Call,
        2m, 2.1m, 500, .3, .15, -.06, 100m, 20m, 0, "TestProvider");
    private static HoldingContext Holding() => new(Guid.Parse("1B539C36-CA1A-4FB0-B34A-84C821258AAB"), "MSFT",
        AssetType.Stock, AssignmentSensitivity.Level3, TaxSensitivity.Moderate, .25, .12, .18, 70, 80, .1m, .1);
    private static EntryStrategyConfiguration Configuration() => new() { Version = new ConfigurationVersion(1) };
    private static EvaluationContext Context() => new(Holding(), Indicators(), new EarningsContext(AvailabilityStatus.Available, new DateOnly(2027, 1, 1)),
        new DateOnly(2026, 9, 17), new DateTimeOffset(2026, 9, 18, 15, 0, 0, TimeSpan.Zero), Configuration(), new StrategyVersion("1.0.0"));
    private static IndicatorContext Indicators() => new("MSFT", new DateOnly(2026, 9, 17),
        Available(1d), Available(50d), Available(.2d), Available(65d), Available(.95d), Available(.1d),
        Available(3m), Available(2m), Available(1m), Available(1d), Available(105m), Available(.02d), Available(4), Available(20), null,
        MarketRegime.Neutral, MarketRegime.Neutral, new IndicatorCalculationVersion("3.0.0"), new ConfigurationVersion(1),
        new DateTimeOffset(2026, 9, 18, 1, 0, 0, TimeSpan.Zero));
    private static IndicatorValue<T> Available<T>(T value) where T : struct => IndicatorValue<T>.Available(value);
    private static IndicatorValue<T> Missing<T>() where T : struct => IndicatorValue<T>.InsufficientData();
}

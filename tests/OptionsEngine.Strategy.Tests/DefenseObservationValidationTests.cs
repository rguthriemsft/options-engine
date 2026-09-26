using OptionsEngine.Domain.Accounts;
using OptionsEngine.Strategy.Defense;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Strategy.Tests;

public sealed class DefenseObservationValidationTests
{
    private static readonly DateTimeOffset Cutoff =
        new(2026, 9, 20, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CoherentCurrentAndPreviousObservationsValidate()
    {
        var input = Input();

        input.Validate();

        var result = new DefenseEvaluator().Evaluate(input);
        Assert.Equal(.10, result.DeltaVelocity!.Value, 10);
    }

    [Theory]
    [InlineData(ObservationMismatch.OptionSymbol)]
    [InlineData(ObservationMismatch.UnderlyingSymbol)]
    [InlineData(ObservationMismatch.Expiration)]
    [InlineData(ObservationMismatch.Strike)]
    [InlineData(ObservationMismatch.OptionType)]
    public void CurrentObservationMustMatchPositionContract(ObservationMismatch mismatch)
    {
        var input = Input();
        var mismatched = Mismatch(input.CurrentOptionObservation!, mismatch);

        Assert.Throws<ArgumentException>(() => (input with { CurrentOptionObservation = mismatched }).Validate());
    }

    [Fact]
    public void CurrentObservationAfterEvaluationCutoffIsRejected()
    {
        var input = Input();
        var future = input.CurrentOptionObservation! with { ObservationTimestampUtc = Cutoff.AddTicks(1) };

        Assert.Throws<ArgumentException>(() => (input with { CurrentOptionObservation = future }).Validate());
    }

    [Theory]
    [InlineData(ObservationMismatch.OptionSymbol)]
    [InlineData(ObservationMismatch.UnderlyingSymbol)]
    [InlineData(ObservationMismatch.Expiration)]
    [InlineData(ObservationMismatch.Strike)]
    [InlineData(ObservationMismatch.OptionType)]
    public void PreviousObservationMustMatchPositionContract(ObservationMismatch mismatch)
    {
        var input = Input();
        var mismatched = Mismatch(input.PreviousDeltaObservation!, mismatch);

        Assert.Throws<ArgumentException>(() => (input with { PreviousDeltaObservation = mismatched }).Validate());
    }

    [Fact]
    public void PreviousObservationAfterEvaluationCutoffIsRejectedWithoutCurrentObservation()
    {
        var input = Input() with { CurrentOptionObservation = null };
        var future = input.PreviousDeltaObservation! with { ObservationTimestampUtc = Cutoff.AddTicks(1) };

        Assert.Throws<ArgumentException>(() => (input with { PreviousDeltaObservation = future }).Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void PreviousObservationMustBeStrictlyEarlierThanCurrentObservation(long additionalTicks)
    {
        var input = Input();
        var previous = input.PreviousDeltaObservation! with
        {
            ObservationTimestampUtc = input.CurrentOptionObservation!.ObservationTimestampUtc.AddTicks(additionalTicks)
        };

        Assert.Throws<ArgumentException>(() => (input with { PreviousDeltaObservation = previous }).Validate());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PresentObservationsReceiveStructuralValidation(bool current)
    {
        var input = Input();
        var malformed = (current ? input.CurrentOptionObservation! : input.PreviousDeltaObservation!) with
        {
            Provider = " "
        };
        input = current
            ? input with { CurrentOptionObservation = malformed }
            : input with { PreviousDeltaObservation = malformed };

        Assert.Throws<ArgumentException>(input.Validate);
    }

    [Fact]
    public void MissingCurrentObservationRemainsAllowed()
    {
        var input = Input() with { CurrentOptionObservation = null };

        input.Validate();
        var result = new DefenseEvaluator().Evaluate(input);

        Assert.Equal(EvaluationValueStatus.InsufficientData, result.ProfitTaking.Status);
        Assert.Equal(EvaluationValueStatus.InsufficientData, result.Drs.Status);
    }

    [Fact]
    public void MissingPreviousObservationRemainsAllowedAndVelocityIsUnavailable()
    {
        var input = Input() with { PreviousDeltaObservation = null };

        input.Validate();
        var result = new DefenseEvaluator().Evaluate(input);

        Assert.Null(result.DeltaVelocity);
        var rapidDelta = Assert.Single(result.HardTriggers,
            trigger => trigger.Code == HardTriggerCode.RapidDeltaIncrease);
        Assert.Equal(HardTriggerStatus.InsufficientData, rapidDelta.Status);
        Assert.Contains(DefenseMissingInputCode.PreviousDelta, rapidDelta.MissingInputs);
    }

    [Fact]
    public void UnderlyingSymbolUsesCaseInsensitiveTickerComparison()
    {
        var input = Input() with
        {
            CurrentOptionObservation = Input().CurrentOptionObservation! with { UnderlyingSymbol = "msft" },
            PreviousDeltaObservation = Input().PreviousDeltaObservation! with { UnderlyingSymbol = "MsFt" }
        };

        input.Validate();
    }

    private static DefenseEvaluationInput Input()
    {
        var version = new ConfigurationVersion(1);
        var holdingId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var expiration = new DateOnly(2026, 10, 16);
        var position = new OpenShortCallPositionSnapshot(42, holdingId, "MSFT261016C00100000", 1,
            100m, expiration, 1m, null);
        return new DefenseEvaluationInput(position,
            new DefenseHoldingContext(holdingId, "MSFT", AssetType.Stock, AssignmentSensitivity.Level3,
                TaxSensitivity.Moderate),
            Observation(expiration, Cutoff.AddMinutes(-1), .30),
            Observation(expiration, Cutoff.AddDays(-1), .20),
            null, new EarningsContext(AvailabilityStatus.Unavailable, null), Cutoff,
            new DefenseConfiguration { Version = version },
            new RollConfiguration { Version = version, MaximumRollDebitPerShare = 0 }, version,
            new DefenseStrategyVersion("6.0.0"), new RollStrategyVersion("6.0.0"));
    }

    private static DefenseOptionObservation Observation(DateOnly expiration, DateTimeOffset timestamp,
        double delta) => new("MSFT261016C00100000", "MSFT", timestamp, expiration, 100m,
        OptionContractType.Call, .20m, .25m, .22m, 500, .30, delta, .02, -.01, .10, 80m, "Test");

    private static DefenseOptionObservation Mismatch(DefenseOptionObservation observation,
        ObservationMismatch mismatch) => mismatch switch
    {
        ObservationMismatch.OptionSymbol => observation with { OptionSymbol = "WRONG" },
        ObservationMismatch.UnderlyingSymbol => observation with { UnderlyingSymbol = "AAPL" },
        ObservationMismatch.Expiration => observation with { Expiration = observation.Expiration.AddDays(1) },
        ObservationMismatch.Strike => observation with { Strike = observation.Strike + 1 },
        ObservationMismatch.OptionType => observation with { OptionType = OptionContractType.Put },
        _ => throw new ArgumentOutOfRangeException(nameof(mismatch))
    };

    public enum ObservationMismatch
    {
        OptionSymbol,
        UnderlyingSymbol,
        Expiration,
        Strike,
        OptionType
    }
}

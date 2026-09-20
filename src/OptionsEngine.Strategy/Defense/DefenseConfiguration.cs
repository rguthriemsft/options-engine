using System.Collections.Immutable;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Strategy.Defense;

public sealed record DefenseScoreBand(double Minimum, bool IncludesMinimum, double Maximum, bool IncludesMaximum, double Score);
public sealed record DrsClassificationBand(double Minimum, bool IncludesMinimum, double Maximum, bool IncludesMaximum,
    DrsClassification Classification);

public sealed record ProfitTakingConfiguration
{
    public double MonitorRatio { get; init; } = .50;
    public double CloseRatio { get; init; } = .70;
    public double StrongCloseRatio { get; init; } = .80;
    public void Validate()
    {
        RequireFinite(MonitorRatio, nameof(MonitorRatio)); RequireFinite(CloseRatio, nameof(CloseRatio)); RequireFinite(StrongCloseRatio, nameof(StrongCloseRatio));
        if (!(MonitorRatio < CloseRatio && CloseRatio < StrongCloseRatio))
            throw new ArgumentException("Profit-taking thresholds must be ordered Monitor < Close < StrongClose.");
    }
    private static void RequireFinite(double value, string name) { if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(name); }
}

public sealed record DrsConfiguration
{
    public ImmutableArray<DefenseScoreBand> DeltaBands { get; init; } =
        [new(0, true, .15, false, 0), new(.15, true, .20, false, 4), new(.20, true, .25, false, 9), new(.25, true, .30, false, 16), new(.30, true, .35, false, 24), new(.35, true, .40, false, 31), new(.40, true, .50, true, 36), new(.50, false, 1, true, 40)];
    public ImmutableArray<DefenseScoreBand> StrikeProximityBands { get; init; } =
        [new(double.NegativeInfinity, false, 0, true, 25), new(0, false, .01, true, 25), new(.01, false, .02, true, 20), new(.02, false, .03, true, 15), new(.03, false, .04, true, 10), new(.04, false, .06, true, 6), new(.06, false, .08, true, 3), new(.08, false, double.PositiveInfinity, false, 0)];
    public ImmutableArray<DefenseScoreBand> DteBands { get; init; } =
        [new(0, true, 4, false, 15), new(4, true, 7, false, 12), new(7, true, 10, false, 9), new(10, true, 15, false, 6), new(15, true, 22, false, 3), new(22, true, double.PositiveInfinity, false, 0)];
    public ImmutableArray<DefenseScoreBand> PremiumExpansionBands { get; init; } =
        [new(double.NegativeInfinity, false, .50, false, 0), new(.50, true, 1, false, 2), new(1, true, 1.25, false, 6), new(1.25, true, 1.50, false, 10), new(1.50, true, 2, false, 14), new(2, true, 3, false, 18), new(3, true, double.PositiveInfinity, false, 20)];
    public ImmutableArray<DrsClassificationBand> ClassificationBands { get; init; } =
        [new(0, true, 20, false, DrsClassification.Safe), new(20, true, 35, false, DrsClassification.Normal), new(35, true, 50, false, DrsClassification.Watch), new(50, true, 65, false, DrsClassification.Defend), new(65, true, 80, false, DrsClassification.HighRisk), new(80, true, 100, true, DrsClassification.Critical)];
    public void Validate()
    {
        ValidateScoreBands(DeltaBands, 40, nameof(DeltaBands)); ValidateScoreBands(StrikeProximityBands, 25, nameof(StrikeProximityBands));
        ValidateScoreBands(DteBands, 15, nameof(DteBands)); ValidateScoreBands(PremiumExpansionBands, 20, nameof(PremiumExpansionBands));
        if (ClassificationBands.IsDefaultOrEmpty || ClassificationBands[0].Minimum != 0 || ClassificationBands[^1].Maximum != 100 ||
            ClassificationBands.Zip(ClassificationBands.Skip(1)).Any(x => x.First.Maximum != x.Second.Minimum || x.First.IncludesMaximum == x.Second.IncludesMinimum))
            throw new ArgumentException("DRS classification bands must continuously cover 0 through 100.", nameof(ClassificationBands));
    }
    private static void ValidateScoreBands(ImmutableArray<DefenseScoreBand> bands, double maximum, string name)
    {
        if (bands.IsDefaultOrEmpty || bands.Any(x => double.IsNaN(x.Minimum) || double.IsNaN(x.Maximum) || !double.IsFinite(x.Score) || x.Minimum >= x.Maximum || x.Score < 0 || x.Score > maximum) || bands.Zip(bands.Skip(1)).Any(x => x.First.Maximum != x.Second.Minimum || x.First.IncludesMaximum == x.Second.IncludesMinimum))
            throw new ArgumentException($"{name} contains invalid, unordered, or discontinuous score bands.", name);
    }
}

public sealed record HardTriggerConfiguration
{
    public double HighDelta { get; init; } = .40;
    public double StrikeProximityRatio { get; init; } = .01;
    public double StrikeProximityMinimumDelta { get; init; } = .30;
    public int LowDteMaximum { get; init; } = 3;
    public double LowDteMinimumDelta { get; init; } = .25;
    public double RapidDeltaIncrease { get; init; } = .15;
    public void Validate()
    {
        foreach (var value in new[] { HighDelta, StrikeProximityMinimumDelta, LowDteMinimumDelta, RapidDeltaIncrease }) RequireRatio(value);
        RequireRatio(StrikeProximityRatio); if (LowDteMaximum < 0) throw new ArgumentOutOfRangeException(nameof(LowDteMaximum));
    }
    private static void RequireRatio(double value) { if (!double.IsFinite(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(value)); }
}

public sealed record DefenseConfiguration
{
    public required ConfigurationVersion Version { get; init; }
    public ProfitTakingConfiguration ProfitTaking { get; init; } = new();
    public DrsConfiguration Drs { get; init; } = new();
    public HardTriggerConfiguration HardTriggers { get; init; } = new();
    public void Validate()
    {
        if (Version.Value < 1) throw new ArgumentOutOfRangeException(nameof(Version));
        ArgumentNullException.ThrowIfNull(ProfitTaking); ArgumentNullException.ThrowIfNull(Drs); ArgumentNullException.ThrowIfNull(HardTriggers);
        ProfitTaking.Validate(); Drs.Validate(); HardTriggers.Validate();
    }
}

public sealed record RollConfiguration
{
    public required ConfigurationVersion Version { get; init; }
    public double ActivationDrs { get; init; } = 50;
    public int MinimumReplacementDte { get; init; } = 21;
    public int MaximumReplacementDte { get; init; } = 60;
    public int PreferredMinimumDte { get; init; } = 21;
    public int PreferredMaximumDte { get; init; } = 45;
    public double PreferredMinimumDelta { get; init; } = .12;
    public double PreferredMaximumDelta { get; init; } = .18;
    public double NormalMaximumDelta { get; init; } = .25;
    public double HighTaxMaximumDelta { get; init; } = .20;
    public double MaximumProjectedDrs { get; init; } = 40;
    public double FullStrikeImprovementRatio { get; init; } = .10;
    public required decimal MaximumRollDebitPerShare { get; init; }
    public double FullCreditEconomicsRatio { get; init; } = .25;
    public double CurrentCcosRollThreshold { get; init; } = 55;
    public ContractLiquidityEligibilityConfiguration LiquidityEligibility { get; init; } = new();
    public ContractLiquidityScoringConfiguration LiquidityScoring { get; init; } = new();

    public RollConfiguration WithLiquidityFrom(EntryStrategyConfiguration entryStrategyConfiguration)
    {
        ArgumentNullException.ThrowIfNull(entryStrategyConfiguration);
        return this with
        {
            LiquidityEligibility = entryStrategyConfiguration.ContractEligibility.LiquidityEligibility,
            LiquidityScoring = entryStrategyConfiguration.ContractScore.Liquidity
        };
    }

    public void Validate()
    {
        if (Version.Value < 1) throw new ArgumentOutOfRangeException(nameof(Version));
        if (!double.IsFinite(ActivationDrs) || ActivationDrs < 0 || ActivationDrs > 100 || !double.IsFinite(MaximumProjectedDrs) || MaximumProjectedDrs < 0 || MaximumProjectedDrs > 100 || !double.IsFinite(CurrentCcosRollThreshold) || CurrentCcosRollThreshold < 0 || CurrentCcosRollThreshold > 100) throw new ArgumentOutOfRangeException(nameof(ActivationDrs));
        if (MinimumReplacementDte < 0 || MaximumReplacementDte < MinimumReplacementDte || PreferredMinimumDte < MinimumReplacementDte || PreferredMaximumDte < PreferredMinimumDte || PreferredMaximumDte > MaximumReplacementDte) throw new ArgumentException("Replacement and preferred DTE ranges must be valid and nested.");
        RequireDelta(PreferredMinimumDelta, nameof(PreferredMinimumDelta)); RequireDelta(PreferredMaximumDelta, nameof(PreferredMaximumDelta)); RequireDelta(NormalMaximumDelta, nameof(NormalMaximumDelta)); RequireDelta(HighTaxMaximumDelta, nameof(HighTaxMaximumDelta));
        if (PreferredMinimumDelta > PreferredMaximumDelta || PreferredMaximumDelta > NormalMaximumDelta || HighTaxMaximumDelta > NormalMaximumDelta) throw new ArgumentException("Replacement Delta ranges and maximums are inconsistent.");
        if (MaximumRollDebitPerShare < 0) throw new ArgumentOutOfRangeException(nameof(MaximumRollDebitPerShare));
        if (!double.IsFinite(FullStrikeImprovementRatio) || FullStrikeImprovementRatio <= 0 || !double.IsFinite(FullCreditEconomicsRatio) || FullCreditEconomicsRatio <= 0) throw new ArgumentOutOfRangeException(nameof(FullStrikeImprovementRatio));
        ArgumentNullException.ThrowIfNull(LiquidityEligibility); ArgumentNullException.ThrowIfNull(LiquidityScoring);
        LiquidityEligibility.Validate(); LiquidityScoring.Validate(10, LiquidityEligibility.MaximumBidAskSpreadPercent, LiquidityEligibility.MinimumOpenInterest);
    }
    private static void RequireDelta(double value, string name) { if (!double.IsFinite(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(name); }
}

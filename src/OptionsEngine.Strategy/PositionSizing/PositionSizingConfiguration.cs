using System.Collections.Immutable;
using OptionsEngine.Domain.Accounts;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Strategy.PositionSizing;

/// <summary>
/// A continuous Position Sizing interval. Adjacent intervals must assign their shared boundary to exactly one band.
/// </summary>
public sealed record PositionSizingBand(
    double Minimum,
    bool IncludesMinimum,
    double Maximum,
    bool IncludesMaximum,
    double Value);

public sealed record PositionSizingBandTable
{
    public required ImmutableArray<PositionSizingBand> Bands { get; init; }

    internal void Validate(
        string name,
        double domainMinimum,
        double domainMaximum,
        bool valueMustBeRatio)
    {
        if (Bands.IsDefaultOrEmpty)
            throw new ArgumentException($"{name} must contain at least one band.", name);
        if (Bands[0].Minimum != domainMinimum || !Bands[0].IncludesMinimum ||
            Bands[^1].Maximum != domainMaximum || !Bands[^1].IncludesMaximum)
            throw new ArgumentException($"{name} must cover the complete [{domainMinimum}, {domainMaximum}] domain.", name);

        for (var index = 0; index < Bands.Length; index++)
        {
            var band = Bands[index];
            if (!double.IsFinite(band.Minimum) || !double.IsFinite(band.Maximum) ||
                band.Minimum < domainMinimum || band.Maximum > domainMaximum || band.Minimum >= band.Maximum)
                throw new ArgumentException($"{name} contains an invalid or unordered boundary.", name);
            if (!double.IsFinite(band.Value) || band.Value < 0 || valueMustBeRatio && band.Value > 1)
                throw new ArgumentOutOfRangeException(name,
                    valueMustBeRatio
                        ? $"{name} values must be finite ratios between 0 and 1."
                        : $"{name} values must be finite and non-negative.");

            if (index == 0) continue;

            var previous = Bands[index - 1];
            if (previous.Maximum != band.Minimum || previous.IncludesMaximum == band.IncludesMinimum)
                throw new ArgumentException($"{name} contains a gap or overlapping boundary.", name);
        }
    }
}

public sealed record AssignmentSensitivitySizingConfiguration(
    double Modifier,
    double MaximumCoverageRatio);

/// <summary>All resolved, versioned Position Sizing numeric configuration.</summary>
public sealed record PositionSizingConfiguration
{
    public required ConfigurationVersion Version { get; init; }

    public PositionSizingBandTable CcosBaseCoverage { get; init; } = new()
    {
        Bands =
        [
            new(0, true, 70, false, 0),
            new(70, true, 75, false, .20),
            new(75, true, 80, false, .30),
            new(80, true, 85, false, .40),
            new(85, true, 90, false, .50),
            new(90, true, 95, false, .60),
            new(95, true, 100, true, .70)
        ]
    };

    public ImmutableDictionary<AssignmentSensitivity, AssignmentSensitivitySizingConfiguration>
        AssignmentSensitivitySettings { get; init; } = CreateDefaultAssignmentSensitivitySettings();

    public PositionSizingBandTable ContractQualityModifiers { get; init; } = new()
    {
        Bands =
        [
            new(0, true, 80, false, 0),
            new(80, true, 85, false, .90),
            new(85, true, 90, false, 1.00),
            new(90, true, 95, false, 1.10),
            new(95, true, 100, true, 1.15)
        ]
    };

    public PositionSizingBandTable StockConcentrationModifiers { get; init; } = new()
    {
        Bands =
        [
            new(0, true, .05, false, 1.10),
            new(.05, true, .15, false, 1.00),
            new(.15, true, .25, false, .90),
            new(.25, true, .40, true, .80),
            new(.40, false, 1.00, true, .70)
        ]
    };

    public void Validate()
    {
        if (Version.Value < 1)
            throw new ArgumentOutOfRangeException(nameof(Version), "Configuration version must be positive.");

        ArgumentNullException.ThrowIfNull(CcosBaseCoverage);
        ArgumentNullException.ThrowIfNull(AssignmentSensitivitySettings);
        ArgumentNullException.ThrowIfNull(ContractQualityModifiers);
        ArgumentNullException.ThrowIfNull(StockConcentrationModifiers);

        CcosBaseCoverage.Validate(nameof(CcosBaseCoverage), 0, 100, valueMustBeRatio: true);
        ContractQualityModifiers.Validate(nameof(ContractQualityModifiers), 0, 100, valueMustBeRatio: false);
        StockConcentrationModifiers.Validate(nameof(StockConcentrationModifiers), 0, 1, valueMustBeRatio: false);
        ValidateAssignmentSensitivitySettings();
    }

    public void Validate(PositionSizingHoldingContext holding)
    {
        ArgumentNullException.ThrowIfNull(holding);
        Validate();
        holding.Validate();
    }

    private void ValidateAssignmentSensitivitySettings()
    {
        var requiredLevels = Enum.GetValues<AssignmentSensitivity>();
        if (AssignmentSensitivitySettings.Count != requiredLevels.Length ||
            requiredLevels.Any(level => !AssignmentSensitivitySettings.ContainsKey(level)) ||
            AssignmentSensitivitySettings.Keys.Any(level => !Enum.IsDefined(level)))
            throw new ArgumentException("Every supported Assignment Sensitivity level must be configured exactly once.",
                nameof(AssignmentSensitivitySettings));

        foreach (var (level, setting) in AssignmentSensitivitySettings)
        {
            ArgumentNullException.ThrowIfNull(setting);
            if (!double.IsFinite(setting.Modifier) || setting.Modifier < 0)
                throw new ArgumentOutOfRangeException(nameof(AssignmentSensitivitySettings),
                    $"The {level} modifier must be finite and non-negative.");
            if (!double.IsFinite(setting.MaximumCoverageRatio) ||
                setting.MaximumCoverageRatio < 0 || setting.MaximumCoverageRatio > 1)
                throw new ArgumentOutOfRangeException(nameof(AssignmentSensitivitySettings),
                    $"The {level} maximum coverage ratio must be finite and between 0 and 1.");
        }
    }

    private static ImmutableDictionary<AssignmentSensitivity, AssignmentSensitivitySizingConfiguration>
        CreateDefaultAssignmentSensitivitySettings() =>
        new Dictionary<AssignmentSensitivity, AssignmentSensitivitySizingConfiguration>
        {
            [AssignmentSensitivity.Level1] = new(1.25, 1.00),
            [AssignmentSensitivity.Level2] = new(1.10, .90),
            [AssignmentSensitivity.Level3] = new(1.00, .80),
            [AssignmentSensitivity.Level4] = new(.85, .70),
            [AssignmentSensitivity.Level5] = new(.70, .50)
        }.ToImmutableDictionary();
}

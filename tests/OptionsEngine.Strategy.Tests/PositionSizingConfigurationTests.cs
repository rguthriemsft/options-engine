using System.Collections.Immutable;
using OptionsEngine.Domain.Accounts;
using OptionsEngine.Strategy.Indicators;
using OptionsEngine.Strategy.PositionSizing;

namespace OptionsEngine.Strategy.Tests;

public sealed class PositionSizingConfigurationTests
{
    private static readonly PositionSizingConfiguration Defaults = new()
    {
        Version = new ConfigurationVersion(1)
    };

    [Fact]
    public void DefaultsMatchEveryApprovedPhase5SizingTable()
    {
        Defaults.Validate();

        Assert.Equal(
        [
            new(0, true, 70, false, 0),
            new(70, true, 75, false, .20),
            new(75, true, 80, false, .30),
            new(80, true, 85, false, .40),
            new(85, true, 90, false, .50),
            new(90, true, 95, false, .60),
            new(95, true, 100, true, .70)
        ], Defaults.CcosBaseCoverage.Bands.ToArray());

        Assert.Equal(new AssignmentSensitivitySizingConfiguration(1.25, 1.00),
            Defaults.AssignmentSensitivitySettings[AssignmentSensitivity.Level1]);
        Assert.Equal(new AssignmentSensitivitySizingConfiguration(1.10, .90),
            Defaults.AssignmentSensitivitySettings[AssignmentSensitivity.Level2]);
        Assert.Equal(new AssignmentSensitivitySizingConfiguration(1.00, .80),
            Defaults.AssignmentSensitivitySettings[AssignmentSensitivity.Level3]);
        Assert.Equal(new AssignmentSensitivitySizingConfiguration(.85, .70),
            Defaults.AssignmentSensitivitySettings[AssignmentSensitivity.Level4]);
        Assert.Equal(new AssignmentSensitivitySizingConfiguration(.70, .50),
            Defaults.AssignmentSensitivitySettings[AssignmentSensitivity.Level5]);

        Assert.Equal(
        [
            new(0, true, 80, false, 0),
            new(80, true, 85, false, .90),
            new(85, true, 90, false, 1.00),
            new(90, true, 95, false, 1.10),
            new(95, true, 100, true, 1.15)
        ], Defaults.ContractQualityModifiers.Bands.ToArray());

        Assert.Equal(
        [
            new(0, true, .05, false, 1.10),
            new(.05, true, .15, false, 1.00),
            new(.15, true, .25, false, .90),
            new(.25, true, .40, true, .80),
            new(.40, false, 1.00, true, .70)
        ], Defaults.StockConcentrationModifiers.Bands.ToArray());
    }

    [Fact]
    public void DefaultConfigurationVersionIsInvalid()
    {
        var configuration = Defaults with { Version = default };

        Assert.Throws<ArgumentOutOfRangeException>(configuration.Validate);
    }

    [Fact]
    public void NonFiniteBoundariesAndValuesFailClearly()
    {
        Assert.Throws<ArgumentException>(() => WithCcosBand(0,
            Defaults.CcosBaseCoverage.Bands[0] with { Maximum = double.NaN }).Validate());
        Assert.Throws<ArgumentException>(() => WithCcosBand(0,
            Defaults.CcosBaseCoverage.Bands[0] with { Maximum = double.PositiveInfinity }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => WithCcosBand(0,
            Defaults.CcosBaseCoverage.Bands[0] with { Value = double.NaN }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => WithContractQualityBand(0,
            Defaults.ContractQualityModifiers.Bands[0] with { Value = double.NegativeInfinity }).Validate());
    }

    [Fact]
    public void CoverageRatiosOutsideZeroToOneFailClearly()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WithCcosBand(0,
            Defaults.CcosBaseCoverage.Bands[0] with { Value = -.0001 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => WithCcosBand(0,
            Defaults.CcosBaseCoverage.Bands[0] with { Value = 1.0001 }).Validate());
    }

    [Fact]
    public void ModifiersMustBeFiniteAndNonNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WithContractQualityBand(0,
            Defaults.ContractQualityModifiers.Bands[0] with { Value = -.0001 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => WithConcentrationBand(0,
            Defaults.StockConcentrationModifiers.Bands[0] with { Value = double.PositiveInfinity }).Validate());

        var invalidAsl = Defaults.AssignmentSensitivitySettings.SetItem(
            AssignmentSensitivity.Level3, new(double.NaN, .80));
        Assert.Throws<ArgumentOutOfRangeException>(() => (Defaults with
        {
            AssignmentSensitivitySettings = invalidAsl
        }).Validate());
    }

    [Fact]
    public void MissingIncompleteOverlappingAndNonDeterministicBandsFailClearly()
    {
        Assert.Throws<ArgumentException>(() => (Defaults with
        {
            CcosBaseCoverage = new PositionSizingBandTable { Bands = default }
        }).Validate());

        Assert.Throws<ArgumentException>(() => WithCcosBand(1,
            Defaults.CcosBaseCoverage.Bands[1] with { Minimum = 71 }).Validate());
        Assert.Throws<ArgumentException>(() => WithCcosBand(1,
            Defaults.CcosBaseCoverage.Bands[1] with { Minimum = 69 }).Validate());
        Assert.Throws<ArgumentException>(() => WithCcosBand(1,
            Defaults.CcosBaseCoverage.Bands[1] with { IncludesMinimum = false }).Validate());
        Assert.Throws<ArgumentException>(() => (Defaults with
        {
            StockConcentrationModifiers = new PositionSizingBandTable
            {
                Bands = Defaults.StockConcentrationModifiers.Bands.RemoveAt(0)
            }
        }).Validate());
    }

    [Fact]
    public void EveryAssignmentSensitivityLevelIsRequired()
    {
        Assert.Throws<ArgumentException>(() => (Defaults with
        {
            AssignmentSensitivitySettings = Defaults.AssignmentSensitivitySettings.Remove(AssignmentSensitivity.Level5)
        }).Validate());

        Assert.Throws<ArgumentException>(() => (Defaults with
        {
            AssignmentSensitivitySettings = Defaults.AssignmentSensitivitySettings
                .Remove(AssignmentSensitivity.Level5)
                .Add((AssignmentSensitivity)99, new(.70, .50))
        }).Validate());
    }

    [Fact]
    public void AssignmentSensitivityMaximumsMustBeRatios()
    {
        foreach (var maximum in new[] { -.0001, 1.0001, double.NaN, double.PositiveInfinity })
        {
            var invalid = Defaults.AssignmentSensitivitySettings.SetItem(
                AssignmentSensitivity.Level5, new(.70, maximum));
            Assert.Throws<ArgumentOutOfRangeException>(() => (Defaults with
            {
                AssignmentSensitivitySettings = invalid
            }).Validate());
        }
    }

    [Fact]
    public void CompleteVersionedBandChangesCanBeValidated()
    {
        var changedBands = Defaults.CcosBaseCoverage.Bands
            .SetItem(0, Defaults.CcosBaseCoverage.Bands[0] with { Maximum = 69 })
            .SetItem(1, Defaults.CcosBaseCoverage.Bands[1] with { Minimum = 69, Value = .15 });

        (Defaults with
        {
            Version = new ConfigurationVersion(2),
            CcosBaseCoverage = new PositionSizingBandTable { Bands = changedBands }
        }).Validate();
    }

    private static PositionSizingConfiguration WithCcosBand(int index, PositionSizingBand band) => Defaults with
    {
        CcosBaseCoverage = new PositionSizingBandTable
        {
            Bands = Defaults.CcosBaseCoverage.Bands.SetItem(index, band)
        }
    };

    private static PositionSizingConfiguration WithContractQualityBand(int index, PositionSizingBand band) => Defaults with
    {
        ContractQualityModifiers = new PositionSizingBandTable
        {
            Bands = Defaults.ContractQualityModifiers.Bands.SetItem(index, band)
        }
    };

    private static PositionSizingConfiguration WithConcentrationBand(int index, PositionSizingBand band) => Defaults with
    {
        StockConcentrationModifiers = new PositionSizingBandTable
        {
            Bands = Defaults.StockConcentrationModifiers.Bands.SetItem(index, band)
        }
    };
}

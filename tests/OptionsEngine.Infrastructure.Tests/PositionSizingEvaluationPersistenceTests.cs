using System.Collections.Immutable;
using Microsoft.EntityFrameworkCore;
using OptionsEngine.Application.EntryStrategy;
using OptionsEngine.Application.PositionSizing;
using OptionsEngine.Domain.Accounts;
using OptionsEngine.Infrastructure.Persistence;
using OptionsEngine.Strategy.Indicators;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.PositionSizing;

namespace OptionsEngine.Infrastructure.Tests;

public sealed class PositionSizingEvaluationPersistenceTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"options-engine-position-sizing-{Guid.NewGuid():N}.db");
    private static readonly Guid SourceId = Guid.Parse("A6E8F6B2-1BCA-45B4-BE41-5DD5C9A66A10");
    private static readonly Guid HoldingId = Guid.Parse("3ACBF57D-EE13-4E56-88F1-3FB45FDD1CD6");
    private static readonly Guid AccountId = Guid.Parse("21D477F9-5D0A-4EB9-9D5E-04A857294089");
    private static readonly DateTimeOffset At = new(2026, 9, 19, 15, 0, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        db.EntryStrategyEvaluations.Add(Source());
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        File.Delete(path); File.Delete($"{path}-shm"); File.Delete($"{path}-wal");
        return Task.CompletedTask;
    }

    [Fact]
    public async Task AvailableEvaluationRoundTripsCompleteReproducibilityPayload()
    {
        var expected = Available();
        await using (var db = CreateContext())
            await new SqlitePositionSizingEvaluationRepository(db).InsertAsync(expected);

        await using var verify = CreateContext();
        var actual = await new SqlitePositionSizingEvaluationRepository(verify).GetByIdAsync(expected.PositionSizingEvaluationId);
        Assert.NotNull(actual);
        Assert.Equal(expected.PositionSizingEvaluationId, actual!.PositionSizingEvaluationId);
        Assert.Equal(expected.EntryStrategyEvaluationId, actual.EntryStrategyEvaluationId);
        Assert.Equal(expected.CalculatedAtUtc, actual.CalculatedAtUtc);
        Assert.Equal(expected.SizingTimestampUtc, actual.SizingTimestampUtc);
        Assert.Equal(expected.HoldingContext, actual.HoldingContext);
        Assert.Equal(expected.ExistingShortCallExposure.ToArray(), actual.ExistingShortCallExposure.ToArray());
        Assert.Equal(expected.ExistingShortCallDeltaObservations.ToArray(), actual.ExistingShortCallDeltaObservations.ToArray());
        Assert.NotNull(actual.PortfolioConcentrationContext);
        Assert.Equal(expected.PortfolioConcentrationContext!.TargetHoldingId, actual.PortfolioConcentrationContext!.TargetHoldingId);
        Assert.Equal(expected.PortfolioConcentrationContext.Holdings.ToArray(), actual.PortfolioConcentrationContext.Holdings.ToArray());
        Assert.Equal(expected.ResolvedConfiguration.Version, actual.ResolvedConfiguration.Version);
        Assert.Equal(expected.StrategyVersion, actual.StrategyVersion);
        Assert.Equal(expected.Result.Status, actual.Result.Status);
        Assert.Equal(expected.Result.ReasonCodes.ToArray(), actual.Result.ReasonCodes.ToArray());
        Assert.Equal(expected.Result.MissingInputs.ToArray(), actual.Result.MissingInputs.ToArray());
        Assert.Equal(expected.Result.AdditionalContracts, actual.Result.AdditionalContracts);
        Assert.Equal(expected.Result.ExistingDer, actual.Result.ExistingDer);
    }

    [Fact]
    public async Task NotApplicableRoundTripsNullSnapshotsWithoutFabrication()
    {
        var expected = NotApplicable();
        await using (var db = CreateContext())
            await new SqlitePositionSizingEvaluationRepository(db).InsertAsync(expected);

        await using var verify = CreateContext();
        var actual = (await new SqlitePositionSizingEvaluationRepository(verify)
            .GetByIdAsync(expected.PositionSizingEvaluationId))!;
        Assert.Equal(PositionSizingStatus.NotApplicable, actual.Result.Status);
        Assert.Null(actual.HoldingContext);
        Assert.Null(actual.PortfolioConcentrationContext);
        Assert.Empty(actual.ExistingShortCallExposure);
        Assert.Empty(actual.ExistingShortCallDeltaObservations);
        Assert.Equal(0, actual.Result.AdditionalContracts);
    }

    [Fact]
    public async Task InsufficientDataPreservesNullFinancialValuesAndMissingInputs()
    {
        var expected = Available() with
        {
            PositionSizingEvaluationId = Guid.NewGuid(),
            Result = Result(PositionSizingStatus.InsufficientData, [PositionSizingMissingInputCode.PortfolioPrice]) with
            {
                PortfolioWeight = null, AdditionalContracts = null, ResultingTotalContracts = null
            }
        };
        await using (var db = CreateContext())
            await new SqlitePositionSizingEvaluationRepository(db).InsertAsync(expected);

        await using var verify = CreateContext();
        var actual = (await new SqlitePositionSizingEvaluationRepository(verify)
            .GetByIdAsync(expected.PositionSizingEvaluationId))!;
        Assert.Equal(PositionSizingStatus.InsufficientData, actual.Result.Status);
        Assert.Contains(PositionSizingMissingInputCode.PortfolioPrice, actual.Result.MissingInputs);
        Assert.Null(actual.Result.PortfolioWeight);
        Assert.Null(actual.Result.AdditionalContracts);
    }

    [Fact]
    public async Task InsertsAreAppendOnlyAndHistoricalPayloadDoesNotFollowMutableCurrentState()
    {
        var first = Available();
        var second = Available() with { PositionSizingEvaluationId = Guid.NewGuid(), CalculatedAtUtc = At.AddMinutes(1) };
        await using (var db = CreateContext())
        {
            var repository = new SqlitePositionSizingEvaluationRepository(db);
            await repository.InsertAsync(first);
            await repository.InsertAsync(second);
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.InsertAsync(first with
            {
                Result = first.Result with { Explanation = "replacement" }
            }));
            db.OpenShortCallPositions.Add(new OpenShortCallPositionEntity
            {
                HoldingId = HoldingId, OptionSymbol = "MSFT-CHANGED", Contracts = 99, Strike = 1m,
                Expiration = new DateOnly(2027, 1, 1)
            });
            await db.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        var actual = (await new SqlitePositionSizingEvaluationRepository(verify).GetByIdAsync(first.PositionSizingEvaluationId))!;
        Assert.Equal(2, await verify.PositionSizingEvaluations.CountAsync());
        Assert.Equal("MSFT-C", Assert.Single(actual.ExistingShortCallExposure).OptionSymbol);
        Assert.Equal(1, actual.Result.AdditionalContracts);
    }

    [Fact]
    public async Task OpenShortCallRepositoryReturnsOnlyRequestedHoldingInStableOrder()
    {
        await using var db = CreateContext();
        db.OpenShortCallPositions.AddRange(
            new OpenShortCallPositionEntity { HoldingId = HoldingId, OptionSymbol = "MSFT-B", Contracts = 1, Strike = 110m, Expiration = new(2026, 11, 20) },
            new OpenShortCallPositionEntity { HoldingId = HoldingId, OptionSymbol = "MSFT-A", Contracts = 2, Strike = 105m, Expiration = new(2026, 10, 16) },
            new OpenShortCallPositionEntity { HoldingId = Guid.NewGuid(), OptionSymbol = "OTHER", Contracts = 1, Strike = 1m, Expiration = new(2026, 10, 16) });
        await db.SaveChangesAsync();
        var values = await new SqliteOpenShortCallPositionRepository(db).GetOpenShortCallsByHoldingAsync(HoldingId);
        Assert.Equal(["MSFT-A", "MSFT-B"], values.Select(x => x.OptionSymbol));
        Assert.Equal([2, 1], values.Select(x => x.Contracts));
    }

    [Fact]
    public async Task WriterInvokesOrchestrationOnceAndPersistsAvailableInsufficientAndNotApplicableBundles()
    {
        foreach (var expected in new[] { Available(), Available() with { PositionSizingEvaluationId = Guid.NewGuid(), Result = Result(PositionSizingStatus.InsufficientData, [PositionSizingMissingInputCode.Ccos]) }, NotApplicable() })
        {
            var input = new PositionSizingInput(null, expected.HoldingContext, expected.ExistingShortCallExposure,
                expected.ExistingShortCallDeltaObservations, expected.PortfolioConcentrationContext, expected.SizingTimestampUtc,
                expected.ResolvedConfiguration, expected.ConfigurationVersion, expected.StrategyVersion);
            var orchestrator = new RecordingOrchestrator(new PositionSizingEvaluationBundle(BundleSource(SourceId), input, expected.Result));
            var repository = new CapturingRepository();
            var writer = new PositionSizingEvaluationPersistenceService(orchestrator, repository, new FixedTimeProvider(At.AddHours(1)));
            var actual = await writer.CreateAsync(SourceId, expected.SizingTimestampUtc);
            Assert.Equal(1, orchestrator.CallCount);
            Assert.Same(actual, repository.Value);
            Assert.Equal(1, repository.InsertCallCount);
            Assert.Equal(SourceId, actual.EntryStrategyEvaluationId);
            Assert.Equal(At.AddHours(1), actual.CalculatedAtUtc);
            Assert.Equal(expected.HoldingContext, actual.HoldingContext);
            Assert.Equal(expected.PortfolioConcentrationContext, actual.PortfolioConcentrationContext);
            Assert.Equal(expected.Result, actual.Result);
        }
    }

    [Fact]
    public async Task WriterRejectsMismatchedBundleSourceIdWithoutPersisting()
    {
        var expected = Available();
        var requestedId = SourceId;
        var actualSourceId = Guid.NewGuid();
        var input = new PositionSizingInput(null, expected.HoldingContext, expected.ExistingShortCallExposure,
            expected.ExistingShortCallDeltaObservations, expected.PortfolioConcentrationContext, expected.SizingTimestampUtc,
            expected.ResolvedConfiguration, expected.ConfigurationVersion, expected.StrategyVersion);
        var orchestrator = new RecordingOrchestrator(new PositionSizingEvaluationBundle(
            BundleSource(actualSourceId), input, expected.Result));
        var repository = new CapturingRepository();
        var writer = new PositionSizingEvaluationPersistenceService(orchestrator, repository, new FixedTimeProvider(At));

        await Assert.ThrowsAsync<PositionSizingSourceInconsistencyException>(() =>
            writer.CreateAsync(requestedId, expected.SizingTimestampUtc));

        Assert.Equal(1, orchestrator.CallCount);
        Assert.Equal(0, repository.InsertCallCount);
        Assert.Null(repository.Value);
    }

    [Fact]
    public async Task PopulatedPhaseFourDatabaseUpgradesToPhaseFiveWithoutChangingSourceEvaluation()
    {
        var upgradePath = Path.Combine(Path.GetTempPath(), $"options-engine-phase4-to-phase5-{Guid.NewGuid():N}.db");
        try
        {
            await using (var phaseFour = CreateContext(upgradePath))
            {
                await phaseFour.Database.MigrateAsync("20260918170325_AddEntryStrategyEvaluations");
                phaseFour.EntryStrategyEvaluations.Add(Source());
                await phaseFour.SaveChangesAsync();
            }
            await using (var phaseFive = CreateContext(upgradePath))
            {
                await phaseFive.Database.MigrateAsync();
                Assert.Contains(await phaseFive.Database.GetAppliedMigrationsAsync(),
                    x => x.EndsWith("_AddPositionSizing", StringComparison.Ordinal));
                var source = await phaseFive.EntryStrategyEvaluations.SingleAsync();
                Assert.Equal(SourceId, source.EntryStrategyEvaluationId);
                Assert.Equal("{}", source.EvaluationJson);
                Assert.Empty(await phaseFive.PositionSizingEvaluations.ToListAsync());
                Assert.Empty(await phaseFive.OpenShortCallPositions.ToListAsync());
                Assert.Empty(await phaseFive.Database.GetPendingMigrationsAsync());
            }
        }
        finally
        {
            File.Delete(upgradePath); File.Delete($"{upgradePath}-shm"); File.Delete($"{upgradePath}-wal");
        }
    }

    private OptionsEngineDbContext CreateContext() => new(new DbContextOptionsBuilder<OptionsEngineDbContext>()
        .UseSqlite($"Data Source={path}").Options);

    private static OptionsEngineDbContext CreateContext(string databasePath) => new(new DbContextOptionsBuilder<OptionsEngineDbContext>()
        .UseSqlite($"Data Source={databasePath}").Options);

    private static EntryStrategyEvaluationEntity Source() => new()
    {
        EntryStrategyEvaluationId = SourceId, HoldingId = HoldingId, Symbol = "MSFT", IndicatorAsOfDate = new(2026, 9, 18),
        EvaluationTimestampUtc = At, CalculatedAtUtc = At, IndicatorCalculationVersion = "3.0.0", ConfigurationVersion = 1,
        StrategyVersion = "4.0.0", CcosStatus = "Available", EntryCandidateExists = true,
        DispositionReason = "EntryCandidate", EvaluationJson = "{}"
    };

    private static PersistedEntryStrategyEvaluation BundleSource(Guid evaluationId) => new(
        new EntryStrategyEvaluation(evaluationId, At, null!, null, [], [], false, null, null, null, null,
            DispositionReasonCode.InsufficientData, [], []), [], []);

    private static PositionSizingEvaluation Available() => new(Guid.NewGuid(), SourceId, At, At.AddMinutes(-5),
        new PositionSizingHoldingContext(HoldingId, AccountId, "MSFT", AssetType.Stock, 250m, AssignmentSensitivity.Level3,
            TaxSensitivity.Moderate, .8m, .3),
        [new ExistingShortCallExposure(HoldingId, "MSFT-C", 1, 105m, new(2026, 10, 16))],
        [new ExistingShortCallDeltaObservation("MSFT-C", .24, At.AddMinutes(-6))],
        new PortfolioConcentrationContext(HoldingId, AccountId, new(2026, 9, 18),
            [new PortfolioConcentrationHolding(HoldingId, AccountId, "MSFT", 250m, 100m, new(2026, 9, 18))]),
        new PositionSizingConfiguration { Version = new ConfigurationVersion(1) }, new ConfigurationVersion(1),
        new PositionSizingStrategyVersion("5.0.0"), Result(PositionSizingStatus.Available, []));

    private static PositionSizingEvaluation NotApplicable() => new(Guid.NewGuid(), SourceId, At, At.AddMinutes(-5), null, [], [], null,
        new PositionSizingConfiguration { Version = new ConfigurationVersion(1) }, new ConfigurationVersion(1),
        new PositionSizingStrategyVersion("5.0.0"), new PositionSizingResult
        {
            Status = PositionSizingStatus.NotApplicable, ReasonCodes = [PositionSizingReasonCode.NoEntryCandidate], MissingInputs = [],
            AdditionalContracts = 0, ResultingTotalContracts = 0, LimitingFactors = [], Explanation = "No entry candidate."
        });

    private static PositionSizingResult Result(PositionSizingStatus status,
        ImmutableArray<PositionSizingMissingInputCode> missingInputs) => new()
    {
        Status = status, ReasonCodes = status == PositionSizingStatus.Available ? [] : [PositionSizingReasonCode.InsufficientData],
        MissingInputs = missingInputs, SharesOwned = 250m, PhysicalCapacityContracts = 2, ExistingCoveredContracts = 1,
        ExistingCoveredShares = 100m, AvailableShares = 150m, AvailableContracts = 1, Ccos = 90, CcosBaseCoverageRatio = .6,
        AssignmentSensitivityModifier = 1, AssignmentSensitivityMaximumRatio = .8, PreferredContractScore = 90,
        ContractQualityModifier = 1, PortfolioConcentrationStatus = PortfolioConcentrationStatus.Available, PortfolioWeight = .2,
        ConcentrationModifier = 1, RawCoverageRatio = .6, DesiredCoverageRatio = .6, DesiredTotalContracts = 2,
        DesiredAdditionalContracts = 1, PhysicalLimitedAdditionalContracts = 1, ExistingDeltaShares = 24, ExistingDer = .096,
        MaximumDer = .3, DerLimitedAdditionalContracts = 1, AdditionalContracts = status == PositionSizingStatus.Available ? 1 : null,
        ResultingTotalContracts = status == PositionSizingStatus.Available ? 2 : null, LimitingFactors = [], Explanation = "Sized."
    };

    private sealed class RecordingOrchestrator(PositionSizingEvaluationBundle bundle) : IPositionSizingEvaluationOrchestrator
    {
        public int CallCount { get; private set; }
        public Task<PositionSizingEvaluationBundle> EvaluateAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken = default)
        { CallCount++; return Task.FromResult(bundle); }
    }

    private sealed class CapturingRepository : IPositionSizingEvaluationRepository
    {
        public PositionSizingEvaluation? Value { get; private set; }
        public int InsertCallCount { get; private set; }
        public Task InsertAsync(PositionSizingEvaluation evaluation, CancellationToken cancellationToken = default)
        { InsertCallCount++; Value = evaluation; return Task.CompletedTask; }
        public Task<PositionSizingEvaluation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Value);
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}

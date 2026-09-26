using System.Collections.Immutable;
using Microsoft.EntityFrameworkCore;
using OptionsEngine.Application.Defense;
using OptionsEngine.Domain.Accounts;
using OptionsEngine.Infrastructure.Persistence;
using OptionsEngine.Strategy.Defense;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Infrastructure.Tests;

public sealed class DefenseEvaluationPersistenceTests : IAsyncLifetime
{
    private static readonly Guid HoldingId = Guid.Parse("AD15DEB1-E06F-4D72-9284-E9743D7FB321");
    private static readonly DateTimeOffset EvaluationAt =
        new(2026, 9, 20, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CalculatedAt = EvaluationAt.AddMinutes(5);
    private static readonly ConfigurationVersion ConfigurationVersion = new(3);
    private readonly string path = Path.Combine(Path.GetTempPath(),
        $"options-engine-defense-evaluations-{Guid.NewGuid():N}.db");

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
    public async Task CompleteBundleRoundTripsAllDefenseRollAndCcosMeaning()
    {
        var expected = CompleteBundle();
        await using (var db = CreateContext())
            await new SqliteDefenseEvaluationRepository(db).InsertAsync(expected);

        await using var verify = CreateContext();
        var repository = new SqliteDefenseEvaluationRepository(verify);
        var defense = await repository.GetDefenseEvaluationByIdAsync(
            expected.DefenseEvaluation.DefenseEvaluationId);
        var roll = await repository.GetRollEvaluationByIdAsync(
            expected.RollEvaluation!.RollEvaluationId);

        Assert.NotNull(defense);
        Assert.NotNull(roll);
        Assert.Equal(expected.DefenseEvaluation.DefenseEvaluationId, defense!.DefenseEvaluationId);
        Assert.Equal(expected.DefenseEvaluation.PositionSnapshot, defense.PositionSnapshot);
        Assert.Equal(expected.DefenseEvaluation.HoldingContext, defense.HoldingContext);
        Assert.Equal(expected.DefenseEvaluation.CurrentOptionObservation, defense.CurrentOptionObservation);
        Assert.Equal(expected.DefenseEvaluation.PreviousDeltaObservation, defense.PreviousDeltaObservation);
        Assert.Equal(-.5, defense.ProfitTaking.GrossPremiumCapturedRatio);
        Assert.Equal(4, defense.Drs.Components.Length);
        Assert.Equal(5, defense.HardTriggers.Length);
        Assert.Equal(expected.DefenseEvaluation.ReasonCodes.ToArray(), defense.ReasonCodes.ToArray());
        Assert.Equal(expected.DefenseEvaluation.MissingInputs.ToArray(), defense.MissingInputs.ToArray());
        Assert.Equal(ConfigurationVersion, defense.ResolvedDefenseConfiguration.Version);
        Assert.Equal(new DefenseStrategyVersion("6.1.0"), defense.DefenseStrategyVersion);
        Assert.Equal(new RollStrategyVersion("6.2.0"), defense.RollStrategyVersion);
        Assert.NotNull(defense.CurrentCcosContext);
        Assert.Equal("4.3.0", defense.CurrentCcosContext!.EvaluationContext.StrategyVersion.Value);
        Assert.Equal("3.2.0", defense.CurrentCcosContext.EvaluationContext.Indicators
            .IndicatorCalculationVersion.Value);
        Assert.Equal(60, defense.CurrentCcos);

        Assert.Equal(2, roll!.SelectedChainSnapshots.Length);
        Assert.Equal(3, roll.Candidates.Length);
        Assert.Equal([
                RollCandidateEvaluationState.Rankable,
                RollCandidateEvaluationState.Rejected,
                RollCandidateEvaluationState.InsufficientData
            ], roll.Candidates.Select(x => x.State).ToArray());
        Assert.Equal(6, roll.Candidates[0].Rqs!.Components.Length);
        Assert.Contains(RollReasonCode.StrikeNotImproved, roll.Candidates[1].ReasonCodes);
        Assert.Contains(RollMissingInputCode.CandidateBid, roll.Candidates[2].MissingInputs);
        Assert.Equal("MSFT261020C00110000", roll.PreferredOptionSymbol);
        Assert.Equal(110m, roll.PreferredStrike);
        Assert.Equal(82d, roll.PreferredRqs);
        Assert.Equal(expected.RollEvaluation.Explanations.ToArray(), roll.Explanations.ToArray());

        var defenseRow = await verify.DefenseEvaluations.SingleAsync();
        var rollRow = await verify.RollEvaluations.SingleAsync();
        Assert.Equal(defense.DefenseEvaluationId, defenseRow.DefenseEvaluationId);
        Assert.Equal(defense.Disposition.ToString(), defenseRow.Disposition);
        Assert.Equal(roll.RollEvaluationId, defenseRow.RollEvaluationId);
        Assert.Equal(roll.DefenseEvaluationId, rollRow.DefenseEvaluationId);
        Assert.Equal(1, rollRow.RankableCandidateCount);
        Assert.Equal(1, rollRow.InsufficientCandidateCount);
    }

    [Fact]
    public async Task NonActivatedBundlePersistsDefenseOnly()
    {
        var expected = NoRollBundle();
        await using var db = CreateContext();

        await new SqliteDefenseEvaluationRepository(db).InsertAsync(expected);

        Assert.Single(await db.DefenseEvaluations.ToListAsync());
        Assert.Empty(await db.RollEvaluations.ToListAsync());
        var actual = await new SqliteDefenseEvaluationRepository(db)
            .GetDefenseEvaluationByIdAsync(expected.DefenseEvaluation.DefenseEvaluationId);
        Assert.NotNull(actual);
        Assert.False(actual!.RollEngineRequired);
        Assert.Null(actual.RollEvaluationId);
        Assert.Null(actual.CurrentCcosContext);
        Assert.Null(actual.CurrentCcos);
        Assert.Null(await new SqliteDefenseEvaluationRepository(db)
            .GetDefenseEvaluationByIdAsync(Guid.NewGuid()));
        Assert.Null(await new SqliteDefenseEvaluationRepository(db)
            .GetRollEvaluationByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task WriterRejectsInconsistentBundlesBeforeCallingRepository()
    {
        var complete = CompleteBundle();
        var invalid = new[]
        {
            complete with
            {
                DefenseEvaluation = complete.DefenseEvaluation with { RollEngineRequired = false }
            },
            complete with { RollEvaluation = null },
            complete with
            {
                DefenseEvaluation = complete.DefenseEvaluation with { RollEvaluationId = Guid.NewGuid() }
            },
            complete with
            {
                RollEvaluation = complete.RollEvaluation! with { DefenseEvaluationId = Guid.NewGuid() }
            }
        };
        foreach (var bundle in invalid)
        {
            var repository = new CapturingRepository();
            var writer = new DefenseEvaluationPersistenceService(repository);
            await Assert.ThrowsAsync<ArgumentException>(() => writer.PersistAsync(bundle));
            Assert.Equal(0, repository.InsertCalls);
        }
    }

    [Fact]
    public async Task DuplicateIdentitiesNeverReplaceExistingPayloadAndRollFailureIsAtomic()
    {
        var first = CompleteBundle();
        await using var db = CreateContext();
        var repository = new SqliteDefenseEvaluationRepository(db);
        await repository.InsertAsync(first);
        var originalJson = (await db.DefenseEvaluations.AsNoTracking().SingleAsync()).EvaluationJson;

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.InsertAsync(first with
        {
            DefenseEvaluation = first.DefenseEvaluation with { Explanations = ["replacement"] }
        }));

        var second = CompleteBundle(Guid.NewGuid(), first.RollEvaluation!.RollEvaluationId,
            CalculatedAt.AddMinutes(1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.InsertAsync(second));

        db.ChangeTracker.Clear();
        Assert.Single(await db.DefenseEvaluations.ToListAsync());
        Assert.Single(await db.RollEvaluations.ToListAsync());
        Assert.Equal(originalJson,
            (await db.DefenseEvaluations.AsNoTracking().SingleAsync()).EvaluationJson);
        Assert.Null(await repository.GetDefenseEvaluationByIdAsync(
            second.DefenseEvaluation.DefenseEvaluationId));
    }

    [Fact]
    public async Task DatabaseFailureWhileInsertingRollRollsBackDefenseRow()
    {
        var bundle = CompleteBundle();
        await using var db = CreateContext();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER RejectRollEvaluation
            BEFORE INSERT ON RollEvaluations
            BEGIN
                SELECT RAISE(ABORT, 'forced roll persistence failure');
            END;
            """);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            new SqliteDefenseEvaluationRepository(db).InsertAsync(bundle));

        db.ChangeTracker.Clear();
        Assert.Empty(await db.DefenseEvaluations.ToListAsync());
        Assert.Empty(await db.RollEvaluations.ToListAsync());
    }

    [Fact]
    public async Task WriterPersistsTheExactBundleOnceWithoutMutation()
    {
        var bundle = CompleteBundle();
        var repository = new CapturingRepository();
        var writer = new DefenseEvaluationPersistenceService(repository);

        var actual = await writer.PersistAsync(bundle);

        Assert.Same(bundle, actual);
        Assert.Same(bundle, repository.Bundle);
        Assert.Equal(1, repository.InsertCalls);
    }

    [Fact]
    public async Task WriterRejectsInvalidResolvedConfigurationWithoutClampingOrPersistence()
    {
        var bundle = CompleteBundle();
        var invalidConfiguration = bundle.DefenseEvaluation.ResolvedRollConfiguration with
        {
            MaximumRollDebitPerShare = -1m
        };
        bundle = bundle with
        {
            DefenseEvaluation = bundle.DefenseEvaluation with
            {
                ResolvedRollConfiguration = invalidConfiguration
            },
            RollEvaluation = bundle.RollEvaluation! with
            {
                ResolvedConfiguration = invalidConfiguration
            }
        };
        var repository = new CapturingRepository();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new DefenseEvaluationPersistenceService(repository).PersistAsync(bundle));

        Assert.Equal(0, repository.InsertCalls);
    }

    [Fact]
    public async Task HistoryIsLightweightNewestFirstAndUsesStableDatabaseTieBreak()
    {
        var first = NoRollBundle(Guid.NewGuid(), CalculatedAt);
        var second = NoRollBundle(Guid.NewGuid(), CalculatedAt.AddMinutes(1));
        var third = NoRollBundle(Guid.NewGuid(), CalculatedAt.AddMinutes(1));
        await using var db = CreateContext();
        var repository = new SqliteDefenseEvaluationRepository(db);
        await repository.InsertAsync(first);
        await repository.InsertAsync(second);
        await repository.InsertAsync(third);
        var firstRow = await db.DefenseEvaluations.SingleAsync(x =>
            x.DefenseEvaluationId == first.DefenseEvaluation.DefenseEvaluationId);
        firstRow.EvaluationJson = "not valid json";
        await db.SaveChangesAsync();

        var history = await repository.GetDefenseEvaluationHistoryAsync(HoldingId, 42);
        var empty = await repository.GetDefenseEvaluationHistoryAsync(Guid.NewGuid(), 42);

        Assert.Equal([
                third.DefenseEvaluation.DefenseEvaluationId,
                second.DefenseEvaluation.DefenseEvaluationId,
                first.DefenseEvaluation.DefenseEvaluationId
            ], history.Select(x => x.DefenseEvaluationId).ToArray());
        Assert.All(history, x => Assert.Equal(42, x.OpenShortCallPositionId));
        Assert.All(history, x => Assert.Equal(DefenseDisposition.NoAction, x.Disposition));
        Assert.Empty(empty);
    }

    [Fact]
    public async Task HistoricalJsonDoesNotFollowMutableHoldingPositionOrMarketState()
    {
        var account = new Account("Original", Broker.Other, AccountType.Taxable, false);
        var holding = new Holding(account, "MSFT", AssetType.Stock, 100,
            AssignmentSensitivity.Level3, TaxSensitivity.Moderate, 1, .25, .12, .18,
            70, 80, .1m, .1, .25, true);
        var position = new OpenShortCallPositionEntity
        {
            OpenShortCallPositionId = 42,
            HoldingId = holding.HoldingId,
            OptionSymbol = "MSFT260930C00100000",
            Contracts = 1,
            Strike = 100,
            Expiration = new DateOnly(2026, 9, 30),
            OpeningPremiumPerShare = 1,
            OpenedAtUtc = EvaluationAt.AddMonths(-1)
        };
        var bundle = CompleteBundle();
        await using (var db = CreateContext())
        {
            db.AddRange(account, holding, position);
            await db.SaveChangesAsync();
            await new SqliteDefenseEvaluationRepository(db).InsertAsync(bundle);
            var persistedJson = (await db.DefenseEvaluations.AsNoTracking().SingleAsync()).EvaluationJson;
            var unchangedPosition = await db.OpenShortCallPositions.AsNoTracking().SingleAsync();
            Assert.Equal((1, 100m, 1m), (unchangedPosition.Contracts,
                unchangedPosition.Strike, unchangedPosition.OpeningPremiumPerShare));

            position.Contracts = 9;
            position.Strike = 1;
            position.OpeningPremiumPerShare = 99;
            db.OptionContractSnapshots.Add(new OptionContractSnapshotEntity
            {
                OptionSymbol = position.OptionSymbol,
                UnderlyingSymbol = "MSFT",
                Provider = "Changed",
                Timestamp = CalculatedAt.AddDays(1),
                TimestampUtcTicks = CalculatedAt.AddDays(1).UtcTicks,
                Expiration = position.Expiration,
                Strike = 1,
                OptionType = OptionsEngine.MarketData.Models.OptionType.Call
            });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var actual = await new SqliteDefenseEvaluationRepository(db)
                .GetDefenseEvaluationByIdAsync(bundle.DefenseEvaluation.DefenseEvaluationId);
            Assert.Equal(bundle.DefenseEvaluation.PositionSnapshot, actual!.PositionSnapshot);
            Assert.Equal(persistedJson,
                (await db.DefenseEvaluations.AsNoTracking().SingleAsync()).EvaluationJson);
        }
    }

    [Fact]
    public async Task MissingAndZeroRemainDistinctAcrossDefenseCcosAndCandidateFields()
    {
        var missing = MissingOrZeroBundle(useZero: false);
        var zero = MissingOrZeroBundle(useZero: true);
        await using var db = CreateContext();
        var repository = new SqliteDefenseEvaluationRepository(db);
        await repository.InsertAsync(missing);
        await repository.InsertAsync(zero);

        var missingDefense = (await repository.GetDefenseEvaluationByIdAsync(
            missing.DefenseEvaluation.DefenseEvaluationId))!;
        var missingRoll = (await repository.GetRollEvaluationByIdAsync(
            missing.RollEvaluation!.RollEvaluationId))!;
        var zeroDefense = (await repository.GetDefenseEvaluationByIdAsync(
            zero.DefenseEvaluation.DefenseEvaluationId))!;
        var zeroRoll = (await repository.GetRollEvaluationByIdAsync(
            zero.RollEvaluation!.RollEvaluationId))!;

        Assert.Null(missingDefense.PositionSnapshot.OpeningPremiumPerShare);
        Assert.Null(missingDefense.CurrentCcos);
        Assert.Null(missingRoll.Candidates[2].Candidate.Bid);
        Assert.Null(missingRoll.Candidates[2].Metrics.ProjectedDrs);
        Assert.Equal(0m, zeroDefense.PositionSnapshot.OpeningPremiumPerShare);
        Assert.Equal(0d, zeroDefense.CurrentCcos);
        Assert.Equal(0m, zeroRoll.Candidates[2].Candidate.Bid);
        Assert.Equal(0d, zeroRoll.Candidates[2].Metrics.ProjectedDrs);
    }

    [Fact]
    public async Task CleanDatabaseHasLatestDefenseSchemaAndNoPendingMigrations()
    {
        await using var db = CreateContext();
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.EndsWith("_PersistDefenseEvaluations", applied[^1], StringComparison.Ordinal);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Empty(await db.DefenseEvaluations.ToListAsync());
        Assert.Empty(await db.RollEvaluations.ToListAsync());
    }

    [Fact]
    public async Task PopulatedPreviousPhaseSixDatabaseUpgradesWithoutChangingExistingArtifacts()
    {
        var upgradePath = Path.Combine(Path.GetTempPath(),
            $"options-engine-phase6e-to-phase6f-{Guid.NewGuid():N}.db");
        try
        {
            var sourceId = Guid.NewGuid();
            await using (var before = CreateContext(upgradePath))
            {
                await before.Database.MigrateAsync(
                    "20260920052344_ExtendOpenShortCallPositionsForDefense");
                var account = new Account("Upgrade", Broker.Other, AccountType.Taxable,
                    false);
                var holding = new Holding(account, "MSFT", AssetType.Stock, 100,
                    AssignmentSensitivity.Level3, TaxSensitivity.Moderate, 1, .25, .12,
                    .18, 70, 80, .1m, .1, .25, true);
                before.AddRange(account, holding);
                before.OpenShortCallPositions.Add(new OpenShortCallPositionEntity
                {
                    OpenShortCallPositionId = 42,
                    HoldingId = holding.HoldingId,
                    OptionSymbol = "MSFT-C",
                    Contracts = 1,
                    Strike = 100,
                    Expiration = new DateOnly(2026, 10, 16),
                    OpeningPremiumPerShare = 1.25m,
                    OpenedAtUtc = EvaluationAt.AddMonths(-1)
                });
                before.EntryStrategyEvaluations.Add(SourceEvaluation(sourceId, holding.HoldingId));
                before.PositionSizingEvaluations.Add(SourceSizing(sourceId, holding.HoldingId));
                await before.SaveChangesAsync();
            }

            await using var upgraded = CreateContext(upgradePath);
            await upgraded.Database.MigrateAsync();
            Assert.Equal("MSFT", (await upgraded.Holdings.SingleAsync()).Symbol);
            var position = await upgraded.OpenShortCallPositions.SingleAsync();
            Assert.Equal(42, position.OpenShortCallPositionId);
            Assert.Equal(1.25m, position.OpeningPremiumPerShare);
            Assert.Equal(EvaluationAt.AddMonths(-1), position.OpenedAtUtc);
            Assert.Equal(sourceId,
                (await upgraded.EntryStrategyEvaluations.SingleAsync()).EntryStrategyEvaluationId);
            Assert.Equal(sourceId,
                (await upgraded.PositionSizingEvaluations.SingleAsync()).EntryStrategyEvaluationId);
            Assert.Empty(await upgraded.DefenseEvaluations.ToListAsync());
            Assert.Empty(await upgraded.RollEvaluations.ToListAsync());
            Assert.Empty(await upgraded.Database.GetPendingMigrationsAsync());
        }
        finally
        {
            File.Delete(upgradePath);
            File.Delete($"{upgradePath}-shm");
            File.Delete($"{upgradePath}-wal");
        }
    }

    private OptionsEngineDbContext CreateContext() => CreateContext(path);

    private static OptionsEngineDbContext CreateContext(string databasePath) => new(
        new DbContextOptionsBuilder<OptionsEngineDbContext>()
            .UseSqlite($"Data Source={databasePath}").Options);

    private static DefenseEvaluationBundle CompleteBundle(Guid? defenseId = null,
        Guid? rollId = null, DateTimeOffset? calculatedAt = null)
    {
        var actualDefenseId = defenseId ?? Guid.NewGuid();
        var actualRollId = rollId ?? Guid.NewGuid();
        var actualCalculatedAt = calculatedAt ?? CalculatedAt;
        var position = Position(1m);
        var holding = new DefenseHoldingContext(HoldingId, "MSFT", AssetType.Stock,
            AssignmentSensitivity.Level3, TaxSensitivity.Moderate);
        var current = Observation("MSFT260930C00100000", new DateOnly(2026, 9, 30),
            100, 1.4m, 1.5m, .50, 105m, EvaluationAt.AddMinutes(-1));
        var previous = current with
        {
            ObservationTimestampUtc = EvaluationAt.AddDays(-3),
            Delta = .30
        };
        var defenseConfiguration = DefenseConfiguration();
        var rollConfiguration = RollConfiguration();
        var ccos = CurrentCcos(60);
        var projected = Drs(20, DrsClassification.Normal);
        var rankableObservation = Observation("MSFT261020C00110000",
            new DateOnly(2026, 10, 20), 110, 2m, 2.1m, .20, 105m, EvaluationAt);
        var rejectedObservation = Observation("MSFT261020C00100000",
            new DateOnly(2026, 10, 20), 100, 2.2m, 2.3m, .22, 105m, EvaluationAt);
        var insufficientObservation = Observation("MSFT261027C00112000",
            new DateOnly(2026, 10, 27), 112, null, 2.4m, .18, 105m, EvaluationAt);
        var rankable = Candidate(rankableObservation, RollCandidateEvaluationState.Rankable,
            projected, [], [], Rqs(), 1, ["Rankable candidate."]);
        var rejected = Candidate(rejectedObservation, RollCandidateEvaluationState.Rejected,
            Drs(65, DrsClassification.HighRisk), [RollReasonCode.StrikeNotImproved], [],
            null, null, ["Strike did not improve."]);
        var insufficient = Candidate(insufficientObservation,
            RollCandidateEvaluationState.InsufficientData,
            Drs(null, null), [RollReasonCode.InsufficientData],
            [RollMissingInputCode.CandidateBid], null, null, ["Candidate Bid is missing."]);
        ImmutableArray<SelectedRollChainSnapshot> chains =
        [
            new("MSFT", rankableObservation.Expiration, EvaluationAt, "TestProvider",
                [rankableObservation, rejectedObservation]),
            new("MSFT", insufficientObservation.Expiration, EvaluationAt, "TestProvider",
                [insufficientObservation])
        ];
        ImmutableArray<RollCandidateEvaluation> candidates = [rankable, rejected, insufficient];
        var roll = new RollEvaluation(actualRollId, actualDefenseId, EvaluationAt,
            actualCalculatedAt, position, ccos, 60, 1.5m, chains, candidates,
            rankableObservation.OptionSymbol, rankableObservation.Strike,
            rankableObservation.Expiration, 82, rollConfiguration, ConfigurationVersion,
            new RollStrategyVersion("6.2.0"), [RollMissingInputCode.CandidateBid],
            ["Candidate Bid is missing.", "Preferred candidate selected."]);
        var defense = new DefenseEvaluation(actualDefenseId, position.OpenShortCallPositionId,
            HoldingId, "MSFT", position.OptionSymbol, EvaluationAt, actualCalculatedAt,
            position, holding, current, previous, ccos, 60,
            new EarningsContext(AvailabilityStatus.Available, new DateOnly(2026, 11, 1)),
            defenseConfiguration, rollConfiguration, ConfigurationVersion,
            new DefenseStrategyVersion("6.1.0"), new RollStrategyVersion("6.2.0"),
            new ProfitTakingResult(EvaluationValueStatus.Available, ProfitTakingSignal.None,
                100m, 150m, -50m, -.5, [], "Premium capture is negative."),
            Drs(70, DrsClassification.HighRisk), HardTriggers(), HardDefenseStatus.Triggered,
            true, actualRollId, DefenseDisposition.Roll,
            [DefenseReasonCode.HardTriggerActivation], [DefenseMissingInputCode.EarningsDate],
            ["Hard defense activated.", "Preferred roll selected."]);
        return new DefenseEvaluationBundle(defense, roll);
    }

    private static DefenseEvaluationBundle NoRollBundle(Guid? defenseId = null,
        DateTimeOffset? calculatedAt = null)
    {
        var complete = CompleteBundle(defenseId, calculatedAt: calculatedAt);
        var defense = complete.DefenseEvaluation with
        {
            CurrentCcosContext = null,
            CurrentCcos = null,
            RollEngineRequired = false,
            RollEvaluationId = null,
            Disposition = DefenseDisposition.NoAction,
            ReasonCodes = [DefenseReasonCode.NoDefenseActivation],
            MissingInputs = [],
            Explanations = ["No defense activation."]
        };
        return new DefenseEvaluationBundle(defense, null);
    }

    private static DefenseEvaluationBundle MissingOrZeroBundle(bool useZero)
    {
        var bundle = CompleteBundle();
        var position = Position(useZero ? 0m : null);
        var context = useZero ? CurrentCcos(0) : null;
        var candidates = bundle.RollEvaluation!.Candidates.ToBuilder();
        var insufficient = candidates[2];
        var projected = Drs(useZero ? 0 : null,
            useZero ? DrsClassification.Safe : null);
        candidates[2] = insufficient with
        {
            Candidate = insufficient.Candidate with { Bid = useZero ? 0m : null },
            Metrics = insufficient.Metrics with { ProjectedDrsResult = projected }
        };
        var roll = bundle.RollEvaluation with
        {
            CurrentPositionSnapshot = position,
            CurrentCcosContext = context,
            CurrentCcos = useZero ? 0 : null,
            Candidates = candidates.ToImmutable()
        };
        var defense = bundle.DefenseEvaluation with
        {
            PositionSnapshot = position,
            CurrentCcosContext = context,
            CurrentCcos = useZero ? 0 : null
        };
        return new DefenseEvaluationBundle(defense, roll);
    }

    private static OpenShortCallPositionSnapshot Position(decimal? openingPremium) => new(
        42, HoldingId, "MSFT260930C00100000", 1, 100,
        new DateOnly(2026, 9, 30), openingPremium, EvaluationAt.AddMonths(-1));

    private static DefenseOptionObservation Observation(string optionSymbol, DateOnly expiration,
        decimal strike, decimal? bid, decimal? ask, double? delta, decimal? underlying,
        DateTimeOffset observedAt) => new(optionSymbol, "MSFT", observedAt, expiration,
        strike, OptionContractType.Call, bid, ask, bid, 500, .30, delta, .02, -.01,
        .10, underlying, "TestProvider");

    private static DefenseConfiguration DefenseConfiguration() => new()
    {
        Version = ConfigurationVersion
    };

    private static RollConfiguration RollConfiguration() => new()
    {
        Version = ConfigurationVersion,
        MaximumRollDebitPerShare = 2m
    };

    private static CurrentCcosContext CurrentCcos(double score)
    {
        var indicatorDate = new DateOnly(2026, 9, 18);
        var indicators = new IndicatorContext("MSFT", indicatorDate, Available(.30),
            Available(60d), Available(.25), Available(55d), Available(.6), Available(.1),
            Available(100m), Available(99m), Available(95m), Available(.2), Available(110m),
            Available(.05), Available(3), Available(20), null, MarketRegime.Neutral,
            MarketRegime.Bullish, new IndicatorCalculationVersion("3.2.0"),
            ConfigurationVersion, EvaluationAt.AddMinutes(-10)) { IvRank = Available(50d) };
        var context = new EvaluationContext(new HoldingContext(HoldingId, "MSFT",
            AssetType.Stock, AssignmentSensitivity.Level3, TaxSensitivity.Moderate,
            .25, .12, .18, 70, 80, .1m, .1), indicators,
            new EarningsContext(AvailabilityStatus.Available, new DateOnly(2026, 11, 1)),
            indicatorDate, EvaluationAt,
            new EntryStrategyConfiguration { Version = ConfigurationVersion },
            new StrategyVersion("4.3.0"));
        return new CurrentCcosContext(context, new ScoreResult(ScoreStatus.Available,
            score, 100, "TEST", 55, score >= 55,
            [new ScoreComponentResult(ScoreComponentCode.CcosVolatility, "Volatility",
                ScoreStatus.Available, 20, 25,
                [new ScoreInput("IV30", AvailabilityStatus.Available, ".30")], [],
                "Volatility context.")], [], "CCOS calculated."));
    }

    private static DrsResult Drs(double? score, DrsClassification? classification)
    {
        ImmutableArray<ExplanationComponent> components =
        [
            Component(DrsComponentCode.Delta, .5, score is null ? null : 36, 40),
            Component(DrsComponentCode.StrikeProximity, -.05, score is null ? null : 25, 25),
            Component(DrsComponentCode.Dte, 10, score is null ? null : 6, 15),
            Component(DrsComponentCode.PremiumExpansion, 1.5, score is null ? null : 3, 20)
        ];
        return new DrsResult(score is null ? EvaluationValueStatus.InsufficientData :
            EvaluationValueStatus.Available, score, classification, components,
            score is null ? [DefenseMissingInputCode.CurrentDelta] : [],
            score is null ? "DRS unavailable." : "DRS calculated.");
    }

    private static ExplanationComponent Component(DrsComponentCode code, double observed,
        double? score, double maximum) => new(code,
        score is null ? EvaluationValueStatus.InsufficientData : EvaluationValueStatus.Available,
        observed, score, maximum, score is null ? [DefenseMissingInputCode.CurrentDelta] : [],
        $"{code} component.");

    private static ImmutableArray<HardTriggerResult> HardTriggers() =>
    [
        Trigger(HardTriggerCode.HighDelta, HardTriggerStatus.Triggered, "Delta", .5),
        Trigger(HardTriggerCode.StrikeProximityWithDelta, HardTriggerStatus.NotTriggered,
            "StrikeDistanceRatio", .05),
        Trigger(HardTriggerCode.InTheMoney, HardTriggerStatus.Triggered, "UnderlyingPrice", 105),
        Trigger(HardTriggerCode.LowDteWithDelta, HardTriggerStatus.NotTriggered, "Dte", 10),
        Trigger(HardTriggerCode.RapidDeltaIncrease, HardTriggerStatus.Triggered,
            "DeltaVelocity", .2)
    ];

    private static HardTriggerResult Trigger(HardTriggerCode code, HardTriggerStatus status,
        string name, double value) => new(code, status,
        ImmutableDictionary<string, double?>.Empty.Add(name, value), [], $"{code} evaluated.");

    private static RollCandidateEvaluation Candidate(DefenseOptionObservation observation,
        RollCandidateEvaluationState state, DrsResult projected,
        ImmutableArray<RollReasonCode> reasons, ImmutableArray<RollMissingInputCode> missing,
        RqsResult? rqs, int? rank, ImmutableArray<string> explanations) => new(observation,
        state, new RollCandidateDerivedMetrics(30, 20, ReplacementDteWindow.Preferred,
            true, observation.Strike - 100, (double)((observation.Strike - 100) / 100),
            observation.Delta is null ? null : .5 - observation.Delta, .05, 1, 1.5m,
            observation.Bid, observation.Bid is null ? null : observation.Bid - 1.5m,
            observation.Bid is null ? null : (observation.Bid - 1.5m) * 100,
            observation.Bid is null ? null : Math.Max(1.5m - observation.Bid.Value, 0),
            projected, projected.Score is null ? null : 70 - projected.Score),
        reasons, missing, rqs, rank, explanations);

    private static RqsResult Rqs()
    {
        ImmutableArray<RqsComponentResult> components =
        [
            RqsComponent(RqsComponentCode.DrsReduction, 25, 30),
            RqsComponent(RqsComponentCode.DeltaReduction, 18, 20),
            RqsComponent(RqsComponentCode.StrikeImprovement, 18, 20),
            RqsComponent(RqsComponentCode.RollEconomics, 12, 15),
            RqsComponent(RqsComponentCode.ReplacementLiquidity, 6, 10),
            RqsComponent(RqsComponentCode.TimeEfficiency, 3, 5)
        ];
        return new RqsResult(EvaluationValueStatus.Available, 82, components, [],
            "RQS calculated.");
    }

    private static RqsComponentResult RqsComponent(RqsComponentCode code, double score,
        double maximum) => new(code, EvaluationValueStatus.Available, score, maximum,
        ImmutableDictionary<string, double?>.Empty.Add("Raw", score / maximum), [],
        $"{code} scored.");

    private static IndicatorValue<T> Available<T>(T value) where T : struct =>
        IndicatorValue<T>.Available(value);

    private static EntryStrategyEvaluationEntity SourceEvaluation(Guid sourceId, Guid holdingId) => new()
    {
        EntryStrategyEvaluationId = sourceId,
        HoldingId = holdingId,
        Symbol = "MSFT",
        IndicatorAsOfDate = new DateOnly(2026, 9, 18),
        EvaluationTimestampUtc = EvaluationAt,
        CalculatedAtUtc = CalculatedAt,
        IndicatorCalculationVersion = "3.2.0",
        ConfigurationVersion = ConfigurationVersion.Value,
        StrategyVersion = "4.3.0",
        CcosStatus = "Available",
        CcosScore = 60,
        EntryCandidateExists = true,
        DispositionReason = "EntryCandidate",
        EvaluationJson = "{}"
    };

    private static PositionSizingEvaluationEntity SourceSizing(Guid sourceId, Guid holdingId) => new()
    {
        PositionSizingEvaluationId = Guid.NewGuid(),
        EntryStrategyEvaluationId = sourceId,
        HoldingId = holdingId,
        Symbol = "MSFT",
        CalculatedAtUtc = CalculatedAt,
        SizingTimestampUtc = EvaluationAt,
        Status = "Available",
        ConfigurationVersion = ConfigurationVersion.Value,
        PositionSizingStrategyVersion = "5.0.0",
        AdditionalContracts = 1,
        EvaluationJson = "{}"
    };

    private sealed class CapturingRepository : IDefenseEvaluationRepository
    {
        public int InsertCalls { get; private set; }
        public DefenseEvaluationBundle? Bundle { get; private set; }
        public Task InsertAsync(DefenseEvaluationBundle bundle,
            CancellationToken cancellationToken = default)
        {
            InsertCalls++;
            Bundle = bundle;
            return Task.CompletedTask;
        }
        public Task<DefenseEvaluation?> GetDefenseEvaluationByIdAsync(Guid id,
            CancellationToken cancellationToken = default) => Task.FromResult<DefenseEvaluation?>(null);
        public Task<RollEvaluation?> GetRollEvaluationByIdAsync(Guid id,
            CancellationToken cancellationToken = default) => Task.FromResult<RollEvaluation?>(null);
        public Task<IReadOnlyList<DefenseEvaluationHistoryItem>> GetDefenseEvaluationHistoryAsync(
            Guid holdingId, long positionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DefenseEvaluationHistoryItem>>([]);
    }
}

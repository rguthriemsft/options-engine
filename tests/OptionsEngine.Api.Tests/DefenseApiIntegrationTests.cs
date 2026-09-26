using System.Collections.Immutable;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OptionsEngine.Application.Defense;
using OptionsEngine.Application.EntryStrategy;
using OptionsEngine.Domain.Accounts;
using OptionsEngine.Strategy.Defense;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Api.Tests;

public sealed class DefenseApiIntegrationTests
{
    private static readonly Guid HoldingId = Guid.Parse("30E4B1A3-20DE-4AE8-A113-0C1F69A74353");
    private static readonly DateTimeOffset EvaluationAt =
        new(2026, 9, 26, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PostUsesServerTimePersistsNoDefenseOutcomeAndReturnsCreatedArtifact()
    {
        var bundle = Bundle(withRoll: false);
        var orchestrator = new StubOrchestrator(bundle);
        var writer = new CapturingWriter();
        using var factory = Factory(orchestrator, writer, new MemoryRepository(),
            new FixedTimeProvider(EvaluationAt));
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/holdings/{HoldingId}/positions/42/defense-evaluations", null);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(EvaluationAt, orchestrator.Timestamp);
        Assert.Equal(1, writer.Calls);
        Assert.Same(bundle, writer.Bundle);
        Assert.Equal(bundle.DefenseEvaluation.DefenseEvaluationId,
            json.RootElement.GetProperty("defenseEvaluationId").GetGuid());
        Assert.Equal("NoAction", json.RootElement.GetProperty("disposition").GetString());
        Assert.Equal("NoDefenseActivation",
            json.RootElement.GetProperty("reasonCodes")[0].GetString());
        Assert.Equal(JsonValueKind.Array,
            json.RootElement.GetProperty("hardTriggers").ValueKind);
        Assert.EndsWith(
            $"/api/defense-evaluations/{bundle.DefenseEvaluation.DefenseEvaluationId}",
            response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task DefensivePostReturnsDefenseArtifactAndCreatesRollOnlyThroughOrchestration()
    {
        var bundle = Bundle(withRoll: true);
        var writer = new CapturingWriter();
        using var factory = Factory(new StubOrchestrator(bundle), writer,
            new MemoryRepository(), new FixedTimeProvider(EvaluationAt));
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/holdings/{HoldingId}/positions/42/defense-evaluations", null);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(bundle.RollEvaluation!.RollEvaluationId,
            json.RootElement.GetProperty("rollEvaluationId").GetGuid());
        Assert.Equal("Roll", json.RootElement.GetProperty("disposition").GetString());
        Assert.Equal(1, writer.Calls);
        Assert.Contains((await client.PostAsync("/api/roll-evaluations", null)).StatusCode,
            new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
    }

    [Fact]
    public async Task PostUsesApplicationWriterAndRepositoryThenArtifactIsPassivelyReadable()
    {
        var bundle = Bundle(withRoll: true);
        var repository = new MemoryRepository();
        var writer = new DefenseEvaluationPersistenceService(repository);
        using var factory = Factory(new StubOrchestrator(bundle), writer, repository,
            new FixedTimeProvider(EvaluationAt));
        using var client = factory.CreateClient();

        var created = await client.PostAsync(
            $"/api/holdings/{HoldingId}/positions/42/defense-evaluations", null);
        var retrieved = await client.GetAsync(
            $"/api/defense-evaluations/{bundle.DefenseEvaluation.DefenseEvaluationId}");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(1, repository.InsertCalls);
        Assert.Equal(HttpStatusCode.OK, retrieved.StatusCode);
        using var json = JsonDocument.Parse(await retrieved.Content.ReadAsStringAsync());
        Assert.Equal(bundle.DefenseEvaluation.DefenseEvaluationId,
            json.RootElement.GetProperty("defenseEvaluationId").GetGuid());
    }

    [Fact]
    public async Task AnalyticalInsufficientDataIsStillPersistedAndReturnsCreated()
    {
        var source = Bundle(withRoll: false);
        var defense = source.DefenseEvaluation with
        {
            Drs = new DrsResult(EvaluationValueStatus.InsufficientData, null, null, [],
                [DefenseMissingInputCode.CurrentDelta], "Current Delta is missing."),
            HardDefenseStatus = HardDefenseStatus.PartiallyEvaluated,
            Disposition = DefenseDisposition.DefenseReview,
            ReasonCodes = [DefenseReasonCode.InsufficientData],
            MissingInputs = [DefenseMissingInputCode.CurrentDelta]
        };
        var writer = new CapturingWriter();
        using var factory = Factory(new StubOrchestrator(new DefenseEvaluationBundle(defense, null)), writer,
            new MemoryRepository(), new FixedTimeProvider(EvaluationAt));
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/holdings/{HoldingId}/positions/42/defense-evaluations", null);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, writer.Calls);
        Assert.Equal("InsufficientData",
            json.RootElement.GetProperty("drs").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null,
            json.RootElement.GetProperty("drs").GetProperty("score").ValueKind);
    }

    [Theory]
    [InlineData("holding", HttpStatusCode.NotFound, "HOLDING_NOT_FOUND")]
    [InlineData("position", HttpStatusCode.NotFound, "OPEN_SHORT_CALL_POSITION_NOT_FOUND")]
    [InlineData("disabled", HttpStatusCode.Conflict, "HOLDING_DISABLED")]
    [InlineData("expired", HttpStatusCode.BadRequest, "INVALID_OPEN_SHORT_CALL_POSITION")]
    public async Task InvalidCurrentStateMapsToStableTransportErrorWithoutPersistence(
        string failure, HttpStatusCode expectedStatus, string expectedCode)
    {
        Exception exception = failure switch
        {
            "holding" => new HoldingNotFoundException(HoldingId),
            "position" => new OpenShortCallPositionNotFoundException(HoldingId, 42),
            "disabled" => new HoldingDisabledException(HoldingId),
            _ => new InvalidOpenShortCallPositionException(42,
                "Its expiration precedes the defense evaluation date.")
        };
        var writer = new CapturingWriter();
        using var factory = Factory(new StubOrchestrator(exception), writer,
            new MemoryRepository(), new FixedTimeProvider(EvaluationAt));
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/holdings/{HoldingId}/positions/42/defense-evaluations", null);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(expectedCode, json.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, writer.Calls);
    }

    [Fact]
    public async Task ReadsArePassiveCompleteAndRemainAvailableWhenNewEvaluationIsDisabled()
    {
        var bundle = Bundle(withRoll: true);
        var older = History(bundle.DefenseEvaluation, EvaluationAt.AddMinutes(-5));
        var newer = History(bundle.DefenseEvaluation with { DefenseEvaluationId = Guid.NewGuid() },
            EvaluationAt);
        var repository = new MemoryRepository(bundle.DefenseEvaluation, bundle.RollEvaluation,
            [newer, older]);
        var orchestrator = new StubOrchestrator(new HoldingDisabledException(HoldingId));
        var writer = new CapturingWriter();
        using var factory = Factory(orchestrator, writer, repository,
            new FixedTimeProvider(EvaluationAt));
        using var client = factory.CreateClient();

        var defenseResponse = await client.GetAsync(
            $"/api/defense-evaluations/{bundle.DefenseEvaluation.DefenseEvaluationId}");
        using var defenseJson = JsonDocument.Parse(await defenseResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, defenseResponse.StatusCode);
        Assert.Equal("Triggered", defenseJson.RootElement.GetProperty("hardDefenseStatus").GetString());
        Assert.Equal(5, defenseJson.RootElement.GetProperty("hardTriggers").GetArrayLength());
        Assert.Equal("HighDelta", defenseJson.RootElement.GetProperty("hardTriggers")[0]
            .GetProperty("code").GetString());

        var rollResponse = await client.GetAsync(
            $"/api/roll-evaluations/{bundle.RollEvaluation!.RollEvaluationId}");
        using var rollJson = JsonDocument.Parse(await rollResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, rollResponse.StatusCode);
        Assert.Equal(["Rankable", "Rejected", "InsufficientData"],
            rollJson.RootElement.GetProperty("candidates").EnumerateArray()
                .Select(x => x.GetProperty("state").GetString()!).ToArray());
        Assert.Equal("StrikeNotImproved", rollJson.RootElement.GetProperty("candidates")[1]
            .GetProperty("reasonCodes")[0].GetString());
        Assert.Equal("CandidateBid", rollJson.RootElement.GetProperty("candidates")[2]
            .GetProperty("missingInputs")[0].GetString());

        var historyResponse = await client.GetAsync(
            $"/api/holdings/{HoldingId}/positions/42/defense-evaluations");
        using var historyJson = JsonDocument.Parse(await historyResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        Assert.Equal(newer.DefenseEvaluationId,
            historyJson.RootElement[0].GetProperty("defenseEvaluationId").GetGuid());
        Assert.False(historyJson.RootElement[0].TryGetProperty("resolvedDefenseConfiguration", out _));
        Assert.Equal(0, orchestrator.Calls);
        Assert.Equal(0, writer.Calls);

        var disabledPost = await client.PostAsync(
            $"/api/holdings/{HoldingId}/positions/42/defense-evaluations", null);
        Assert.Equal(HttpStatusCode.Conflict, disabledPost.StatusCode);
        Assert.Equal(0, writer.Calls);
    }

    [Fact]
    public async Task MissingReadsReturnStableNotFoundAndMutationRoutesAreNotExposed()
    {
        using var factory = Factory(new StubOrchestrator(Bundle(false)), new CapturingWriter(),
            new MemoryRepository(), new FixedTimeProvider(EvaluationAt));
        using var client = factory.CreateClient();
        var id = Guid.NewGuid();

        var defense = await client.GetAsync($"/api/defense-evaluations/{id}");
        using var defenseJson = JsonDocument.Parse(await defense.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, defense.StatusCode);
        Assert.Equal("DEFENSE_EVALUATION_NOT_FOUND",
            defenseJson.RootElement.GetProperty("code").GetString());
        var roll = await client.GetAsync($"/api/roll-evaluations/{id}");
        using var rollJson = JsonDocument.Parse(await roll.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, roll.StatusCode);
        Assert.Equal("ROLL_EVALUATION_NOT_FOUND",
            rollJson.RootElement.GetProperty("code").GetString());

        foreach (var path in new[] { $"/api/defense-evaluations/{id}", $"/api/roll-evaluations/{id}" })
        foreach (var method in new[] { HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
            Assert.Contains((await client.SendAsync(new HttpRequestMessage(method, path))).StatusCode,
                new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task NonPositivePositionIdIsAStableBadRequest(long positionId)
    {
        var orchestrator = new StubOrchestrator(Bundle(false));
        using var factory = Factory(orchestrator, new CapturingWriter(),
            new MemoryRepository(), new FixedTimeProvider(EvaluationAt));
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/holdings/{HoldingId}/positions/{positionId}/defense-evaluations", null);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_OPEN_SHORT_CALL_POSITION_ID",
            json.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, orchestrator.Calls);
    }

    private static WebApplicationFactory<Program> Factory(IDefenseEvaluationOrchestrator orchestrator,
        IDefenseEvaluationWriter writer, IDefenseEvaluationRepository repository, TimeProvider time) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:OptionsEngine",
                $"Data Source={Path.Combine(Path.GetTempPath(), $"phase6-api-{Guid.NewGuid():N}.db")}");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDefenseEvaluationOrchestrator>();
                services.AddSingleton(orchestrator);
                services.RemoveAll<IDefenseEvaluationWriter>();
                services.AddSingleton(writer);
                services.RemoveAll<IDefenseEvaluationRepository>();
                services.AddSingleton(repository);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(time);
            });
        });

    private static DefenseEvaluationBundle Bundle(bool withRoll)
    {
        var defenseId = Guid.NewGuid();
        var rollId = withRoll ? Guid.NewGuid() : (Guid?)null;
        var version = new ConfigurationVersion(1);
        var position = new OpenShortCallPositionSnapshot(42, HoldingId,
            "MSFT261016C00100000", 1, 100m, new DateOnly(2026, 10, 16), 2m,
            EvaluationAt.AddMonths(-1));
        var holding = new DefenseHoldingContext(HoldingId, "MSFT", AssetType.Stock,
            AssignmentSensitivity.Level3, TaxSensitivity.Moderate);
        var observation = new DefenseOptionObservation(position.OptionSymbol, "MSFT",
            EvaluationAt.AddMinutes(-1), position.Expiration, position.Strike,
            OptionContractType.Call, 2.9m, 3m, 2.95m, 500, .30, .45, .02, -.01, .1,
            104m, "TestProvider");
        var drs = new DrsResult(EvaluationValueStatus.Available, withRoll ? 70 : 10,
            withRoll ? DrsClassification.HighRisk : DrsClassification.Safe,
            [new(DrsComponentCode.Delta, EvaluationValueStatus.Available, .45,
                withRoll ? 36 : 4, 40, [], "Delta component.")], [], "DRS calculated.");
        ImmutableArray<HardTriggerResult> triggers =
        [
            Trigger(HardTriggerCode.HighDelta,
                withRoll ? HardTriggerStatus.Triggered : HardTriggerStatus.NotTriggered),
            Trigger(HardTriggerCode.StrikeProximityWithDelta, HardTriggerStatus.NotTriggered),
            Trigger(HardTriggerCode.InTheMoney, HardTriggerStatus.NotTriggered),
            Trigger(HardTriggerCode.LowDteWithDelta, HardTriggerStatus.NotTriggered),
            Trigger(HardTriggerCode.RapidDeltaIncrease, HardTriggerStatus.NotTriggered)
        ];
        var defenseConfiguration = new DefenseConfiguration { Version = version };
        var rollConfiguration = new RollConfiguration
        {
            Version = version,
            MaximumRollDebitPerShare = 2m
        };
        var defense = new DefenseEvaluation(defenseId, position.OpenShortCallPositionId,
            HoldingId, "MSFT", position.OptionSymbol, EvaluationAt, EvaluationAt.AddSeconds(1),
            position, holding, observation, null, null, null,
            new EarningsContext(AvailabilityStatus.Available, new DateOnly(2026, 11, 1)),
            defenseConfiguration, rollConfiguration, version,
            new DefenseStrategyVersion("6.1.0"), new RollStrategyVersion("6.2.0"),
            new ProfitTakingResult(EvaluationValueStatus.Available, ProfitTakingSignal.None,
                200m, 300m, -100m, -.5, [], "No profit close."),
            drs, triggers, withRoll ? HardDefenseStatus.Triggered : HardDefenseStatus.Clear,
            withRoll, rollId, withRoll ? DefenseDisposition.Roll : DefenseDisposition.NoAction,
            [withRoll ? DefenseReasonCode.HardTriggerActivation : DefenseReasonCode.NoDefenseActivation],
            [], [withRoll ? "Roll selected." : "No defense activation."]);
        if (!withRoll)
            return new(defense, null);

        var candidateObservation = observation with
        {
            OptionSymbol = "MSFT261120C00110000",
            Expiration = new DateOnly(2026, 11, 20),
            Strike = 110m,
            Bid = 3.5m,
            Ask = 3.6m,
            Delta = .2
        };
        var projectedDrs = drs with { Score = 20, Classification = DrsClassification.Normal };
        var rqs = new RqsResult(EvaluationValueStatus.Available, 80,
            [new(RqsComponentCode.DrsReduction, EvaluationValueStatus.Available, 25, 30,
                ImmutableDictionary<string, double?>.Empty.Add("Reduction", 50), [],
                "DRS reduction scored.")], [], "RQS calculated.");
        var candidate = new RollCandidateEvaluation(candidateObservation,
            RollCandidateEvaluationState.Rankable,
            new RollCandidateDerivedMetrics(55, 35, ReplacementDteWindow.Preferred, true,
                10m, .1, .25, .03, 1, 3m, 3.5m, .5m, 50m, 0m,
                projectedDrs, 50), [], [], rqs, 1, ["Preferred candidate."]);
        var rejected = candidate with
        {
            Candidate = candidateObservation with { OptionSymbol = "REJECTED" },
            State = RollCandidateEvaluationState.Rejected,
            ReasonCodes = [RollReasonCode.StrikeNotImproved],
            Rqs = null,
            Rank = null,
            Explanations = ["Strike did not improve."]
        };
        var insufficient = candidate with
        {
            Candidate = candidateObservation with { OptionSymbol = "INSUFFICIENT", Bid = null },
            State = RollCandidateEvaluationState.InsufficientData,
            ReasonCodes = [RollReasonCode.InsufficientData],
            MissingInputs = [RollMissingInputCode.CandidateBid],
            Rqs = null,
            Rank = null,
            Explanations = ["Candidate Bid is missing."]
        };
        var chain = new SelectedRollChainSnapshot("MSFT", candidateObservation.Expiration,
            candidateObservation.ObservationTimestampUtc, "TestProvider",
            [candidateObservation, rejected.Candidate, insufficient.Candidate]);
        var roll = new RollEvaluation(rollId!.Value, defenseId, EvaluationAt,
            EvaluationAt.AddSeconds(1), position, null, null, 3m, [chain],
            [candidate, rejected, insufficient],
            candidateObservation.OptionSymbol, candidateObservation.Strike,
            candidateObservation.Expiration, 80, rollConfiguration, version,
            new RollStrategyVersion("6.2.0"), [], ["Preferred candidate selected."]);
        return new(defense, roll);
    }

    private static HardTriggerResult Trigger(HardTriggerCode code, HardTriggerStatus status) =>
        new(code, status, ImmutableDictionary<string, double?>.Empty.Add("Observed", .1), [],
            $"{code} evaluated.");

    private static DefenseEvaluationHistoryItem History(DefenseEvaluation value,
        DateTimeOffset calculatedAt) => new(value.DefenseEvaluationId,
        value.OpenShortCallPositionId, value.HoldingId, value.Symbol, value.OptionSymbol,
        value.DefenseEvaluationTimestampUtc, calculatedAt, value.Drs.Score,
        value.Drs.Classification, value.ProfitTaking.Signal, value.HardDefenseStatus,
        value.RollEngineRequired, value.Disposition, value.RollEvaluationId, value.CurrentCcos);

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class StubOrchestrator : IDefenseEvaluationOrchestrator
    {
        private readonly DefenseEvaluationBundle? bundle;
        private readonly Exception? exception;
        public int Calls { get; private set; }
        public DateTimeOffset Timestamp { get; private set; }
        public StubOrchestrator(DefenseEvaluationBundle bundle) => this.bundle = bundle;
        public StubOrchestrator(Exception exception) => this.exception = exception;
        public Task<DefenseEvaluationBundle> EvaluateAsync(Guid holdingId, long positionId,
            DateTimeOffset timestamp, CancellationToken cancellationToken = default)
        {
            Calls++;
            Timestamp = timestamp;
            return exception is null ? Task.FromResult(bundle!) : Task.FromException<DefenseEvaluationBundle>(exception);
        }
    }

    private sealed class CapturingWriter : IDefenseEvaluationWriter
    {
        public int Calls { get; private set; }
        public DefenseEvaluationBundle? Bundle { get; private set; }
        public Task<DefenseEvaluationBundle> PersistAsync(DefenseEvaluationBundle bundle,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Bundle = bundle;
            return Task.FromResult(bundle);
        }
    }

    private sealed class MemoryRepository : IDefenseEvaluationRepository
    {
        private DefenseEvaluation? defense;
        private RollEvaluation? roll;
        private IReadOnlyList<DefenseEvaluationHistoryItem> history;
        public int InsertCalls { get; private set; }

        public MemoryRepository(DefenseEvaluation? defense = null, RollEvaluation? roll = null,
            IReadOnlyList<DefenseEvaluationHistoryItem>? history = null)
        {
            this.defense = defense;
            this.roll = roll;
            this.history = history ?? [];
        }

        public Task InsertAsync(DefenseEvaluationBundle bundle,
            CancellationToken cancellationToken = default)
        {
            InsertCalls++;
            defense = bundle.DefenseEvaluation;
            roll = bundle.RollEvaluation;
            history = [History(bundle.DefenseEvaluation, bundle.DefenseEvaluation.CalculatedAtUtc)];
            return Task.CompletedTask;
        }
        public Task<DefenseEvaluation?> GetDefenseEvaluationByIdAsync(Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(defense?.DefenseEvaluationId == id ? defense : null);
        public Task<RollEvaluation?> GetRollEvaluationByIdAsync(Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(roll?.RollEvaluationId == id ? roll : null);
        public Task<IReadOnlyList<DefenseEvaluationHistoryItem>> GetDefenseEvaluationHistoryAsync(
            Guid holdingId, long positionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(history);
    }
}

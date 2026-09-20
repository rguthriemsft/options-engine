using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OptionsEngine.Application.EntryStrategy;
using OptionsEngine.Application.PositionSizing;
using OptionsEngine.Infrastructure.Persistence;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;
using OptionsEngine.Strategy.PositionSizing;

namespace OptionsEngine.Api.Tests;

public sealed class PositionSizingApiIntegrationTests
{
    [Fact]
    public async Task PostUsesServerTimeReturnsCreatedAndSerializesStableCodes()
    {
        var now = new DateTimeOffset(2026, 9, 19, 20, 0, 0, TimeSpan.Zero);
        var value = Evaluation(PositionSizingStatus.Available);
        var writer = new CapturingWriter(value);
        using var factory = Factory(writer, new MemoryRepository(value), new FixedTimeProvider(now));
        using var client = factory.CreateClient();
        var response = await client.PostAsync($"/api/entry-evaluations/{value.EntryStrategyEvaluationId}/position-sizing-evaluations", null);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(now, writer.Timestamp); Assert.Equal(1, writer.Calls);
        Assert.Equal(value.PositionSizingEvaluationId, json.RootElement.GetProperty("positionSizingEvaluationId").GetGuid());
        Assert.Equal("Available", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, json.RootElement.GetProperty("reasonCodes")[0].ValueKind);
        Assert.EndsWith($"/api/position-sizing-evaluations/{value.PositionSizingEvaluationId}", response.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData(PositionSizingStatus.NotApplicable)]
    [InlineData(PositionSizingStatus.InsufficientData)]
    public async Task ValidStrategyOutcomesReturnCreated(PositionSizingStatus status)
    {
        var value = Evaluation(status); using var factory = Factory(new CapturingWriter(value), new MemoryRepository(value), TimeProvider.System);
        using var client = factory.CreateClient();
        var response = await client.PostAsync($"/api/entry-evaluations/{value.EntryStrategyEvaluationId}/position-sizing-evaluations", null);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); Assert.Equal(status.ToString(), json.RootElement.GetProperty("status").GetString());
        if (status == PositionSizingStatus.NotApplicable)
        { Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("holdingContext").ValueKind); Assert.Equal(0, json.RootElement.GetProperty("additionalContracts").GetInt32()); }
        else { Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("portfolioWeight").ValueKind); Assert.Equal(JsonValueKind.String, json.RootElement.GetProperty("missingInputs")[0].ValueKind); }
    }

    [Fact]
    public async Task MissingSourceMapsToNotFoundWithoutPersisting()
    {
        using var factory = Factory(new MissingSourceWriter(), new MemoryRepository(), TimeProvider.System); using var client = factory.CreateClient();
        var response = await client.PostAsync($"/api/entry-evaluations/{Guid.NewGuid()}/position-sizing-evaluations", null);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); Assert.Equal("ENTRY_STRATEGY_EVALUATION_NOT_FOUND", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetIsPassiveAndMissingAndMutationMethodsAreNotSupported()
    {
        var value = Evaluation(PositionSizingStatus.InsufficientData);
        using var factory = Factory(new ThrowingWriter(), new MemoryRepository(value), TimeProvider.System); using var client = factory.CreateClient();
        var get = await client.GetAsync($"/api/position-sizing-evaluations/{value.PositionSizingEvaluationId}");
        using var json = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, get.StatusCode); Assert.Equal("InsufficientData", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("portfolioWeight").ValueKind);
        var missing = await client.GetAsync($"/api/position-sizing-evaluations/{Guid.NewGuid()}");
        using var missingJson = JsonDocument.Parse(await missing.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode); Assert.Equal("POSITION_SIZING_EVALUATION_NOT_FOUND", missingJson.RootElement.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsync($"/api/position-sizing-evaluations/{value.PositionSizingEvaluationId}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PatchAsync($"/api/position-sizing-evaluations/{value.PositionSizingEvaluationId}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync($"/api/position-sizing-evaluations/{value.PositionSizingEvaluationId}")).StatusCode);
    }

    [Fact]
    public async Task PostWithRealWriterAndRepositoryPersistsExactlyOneRow()
    {
        var database = Path.Combine(Path.GetTempPath(), $"phase5-api-{Guid.NewGuid():N}.db");
        var sourceId = Guid.NewGuid(); var value = Evaluation(PositionSizingStatus.Available, sourceId);
        try
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => { b.UseEnvironment("Testing"); b.UseSetting("ConnectionStrings:OptionsEngine", $"Data Source={database}"); b.ConfigureServices(s => { s.RemoveAll<IPositionSizingEvaluationOrchestrator>(); s.AddSingleton<IPositionSizingEvaluationOrchestrator>(new BundleOrchestrator(value)); s.RemoveAll<TimeProvider>(); s.AddSingleton<TimeProvider>(new FixedTimeProvider(value.SizingTimestampUtc)); }); });
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<OptionsEngineDbContext>();
                db.EntryStrategyEvaluations.Add(SourceEntity(sourceId)); await db.SaveChangesAsync();
            }
            using var client = factory.CreateClient();
            var response = await client.PostAsync($"/api/entry-evaluations/{sourceId}/position-sizing-evaluations", null);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var verify = factory.Services.CreateScope(); var persisted = verify.ServiceProvider.GetRequiredService<OptionsEngineDbContext>();
            var rows = await persisted.PositionSizingEvaluations.ToListAsync(); Assert.Single(rows); Assert.Equal(sourceId, rows[0].EntryStrategyEvaluationId); Assert.Equal("Available", rows[0].Status);
        }
        finally { File.Delete(database); File.Delete($"{database}-shm"); File.Delete($"{database}-wal"); }
    }

    private static WebApplicationFactory<Program> Factory(IPositionSizingEvaluationWriter writer,
        IPositionSizingEvaluationRepository repository, TimeProvider time) => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        { b.UseEnvironment("Testing"); b.UseSetting("ConnectionStrings:OptionsEngine", $"Data Source={Path.Combine(Path.GetTempPath(), $"phase5-api-{Guid.NewGuid():N}.db")}"); b.ConfigureServices(s => { s.RemoveAll<IPositionSizingEvaluationWriter>(); s.AddSingleton(writer); s.RemoveAll<IPositionSizingEvaluationRepository>(); s.AddSingleton(repository); s.RemoveAll<TimeProvider>(); s.AddSingleton(time); }); });

    private static PositionSizingEvaluation Evaluation(PositionSizingStatus status, Guid? sourceId = null)
    {
        var source = sourceId ?? Guid.NewGuid(); var config = new PositionSizingConfiguration { Version = new ConfigurationVersion(1) };
        var result = new PositionSizingResult { Status = status, ReasonCodes = status == PositionSizingStatus.NotApplicable ? [PositionSizingReasonCode.NoEntryCandidate] : [PositionSizingReasonCode.InsufficientData], MissingInputs = status == PositionSizingStatus.InsufficientData ? [PositionSizingMissingInputCode.PortfolioPrice] : [], SharesOwned = status == PositionSizingStatus.InsufficientData ? null : 100m, AdditionalContracts = status == PositionSizingStatus.NotApplicable ? 0 : null, LimitingFactors = [], Explanation = "test" };
        return new(Guid.NewGuid(), source, DateTimeOffset.UtcNow, new DateTimeOffset(2026, 9, 19, 20, 0, 0, TimeSpan.Zero), null, [], [], null, config, config.Version, new PositionSizingStrategyVersion("5.0.0"), result);
    }

    private static PersistedEntryStrategyEvaluation Source(Guid id) => new(new EntryStrategyEvaluation(id, DateTimeOffset.UtcNow, null!, null, [], [], false, null, null, null, null, DispositionReasonCode.InsufficientData, [], []), [], []);
    private static EntryStrategyEvaluationEntity SourceEntity(Guid id) => new() { EntryStrategyEvaluationId = id, HoldingId = Guid.NewGuid(), Symbol = "MSFT", IndicatorAsOfDate = new(2026, 9, 18), EvaluationTimestampUtc = DateTimeOffset.UtcNow, CalculatedAtUtc = DateTimeOffset.UtcNow, IndicatorCalculationVersion = "3.0.0", ConfigurationVersion = 1, StrategyVersion = "4.0.0", CcosStatus = "Available", EntryCandidateExists = true, DispositionReason = "EntryCandidate", EvaluationJson = "{}" };
    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider { public override DateTimeOffset GetUtcNow() => value; }
    private sealed class CapturingWriter(PositionSizingEvaluation value) : IPositionSizingEvaluationWriter { public int Calls { get; private set; } public DateTimeOffset Timestamp { get; private set; } public Task<PositionSizingEvaluation> CreateAsync(Guid id, DateTimeOffset timestamp, CancellationToken c = default) { Calls++; Timestamp = timestamp; return Task.FromResult(value); } }
    private sealed class MissingSourceWriter : IPositionSizingEvaluationWriter { public Task<PositionSizingEvaluation> CreateAsync(Guid id, DateTimeOffset t, CancellationToken c = default) => throw new EntryStrategyEvaluationNotFoundException(id); }
    private sealed class ThrowingWriter : IPositionSizingEvaluationWriter { public Task<PositionSizingEvaluation> CreateAsync(Guid id, DateTimeOffset t, CancellationToken c = default) => throw new InvalidOperationException("GET must not write"); }
    private sealed class MemoryRepository(params PositionSizingEvaluation[] values) : IPositionSizingEvaluationRepository { private readonly Dictionary<Guid, PositionSizingEvaluation> data = values.ToDictionary(x => x.PositionSizingEvaluationId); public Task InsertAsync(PositionSizingEvaluation value, CancellationToken c = default) { data.Add(value.PositionSizingEvaluationId, value); return Task.CompletedTask; } public Task<PositionSizingEvaluation?> GetByIdAsync(Guid id, CancellationToken c = default) => Task.FromResult(data.GetValueOrDefault(id)); }
    private sealed class BundleOrchestrator(PositionSizingEvaluation value) : IPositionSizingEvaluationOrchestrator { public Task<PositionSizingEvaluationBundle> EvaluateAsync(Guid id, DateTimeOffset t, CancellationToken c = default) { var input = new PositionSizingInput(null, value.HoldingContext, value.ExistingShortCallExposure, value.ExistingShortCallDeltaObservations, value.PortfolioConcentrationContext, value.SizingTimestampUtc, value.ResolvedConfiguration, value.ConfigurationVersion, value.StrategyVersion); return Task.FromResult(new PositionSizingEvaluationBundle(Source(value.EntryStrategyEvaluationId), input, value.Result)); } }
}

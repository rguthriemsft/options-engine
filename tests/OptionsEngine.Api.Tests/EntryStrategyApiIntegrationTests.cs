using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OptionsEngine.Application.EntryStrategy;
using OptionsEngine.Domain.Accounts;
using OptionsEngine.MarketData.Models;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Api.Tests;

public sealed class EntryStrategyApiIntegrationTests
{
    [Fact]
    public async Task PostUsesServerTimeAndReturnsPersistedCandidate()
    {
        var id = Guid.NewGuid(); var now = new DateTimeOffset(2026, 9, 18, 20, 0, 0, TimeSpan.Zero);
        var value = ApiEvaluationFixture.Create(id, DispositionReasonCode.EntryCandidate, true);
        var writer = new CapturingWriter(value);
        using var factory = CreateFactory(writer, new FixedTimeProvider(now), new InMemoryEvaluationRepository(value));
        using var client = factory.CreateClient();
        var response = await client.PostAsync($"/api/holdings/{value.Evaluation.Context.Holding.HoldingId}/entry-evaluations", null);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); Assert.Equal(1, writer.Calls);
        Assert.Equal(now, writer.Timestamp); Assert.Equal(value.Evaluation.EntryStrategyEvaluationId, json.RootElement.GetProperty("entryStrategyEvaluationId").GetGuid());
        Assert.Equal("EntryCandidate", json.RootElement.GetProperty("dispositionReason").GetString()); Assert.True(json.RootElement.GetProperty("entryCandidateExists").GetBoolean());
        Assert.EndsWith($"/api/entry-evaluations/{value.Evaluation.EntryStrategyEvaluationId}", response.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData(DispositionReasonCode.InsufficientData)]
    [InlineData(DispositionReasonCode.NoAcceptableContract)]
    public async Task NegativeOutcomesRemainSuccessfulPosts(DispositionReasonCode reason)
    {
        var value = ApiEvaluationFixture.Create(Guid.NewGuid(), reason, false); var writer = new CapturingWriter(value);
        using var factory = CreateFactory(writer, TimeProvider.System, new InMemoryEvaluationRepository(value)); using var client = factory.CreateClient();
        var response = await client.PostAsync($"/api/holdings/{value.Evaluation.Context.Holding.HoldingId}/entry-evaluations", null);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); Assert.Equal(1, writer.Calls); Assert.False(json.RootElement.GetProperty("entryCandidateExists").GetBoolean()); Assert.Equal(reason.ToString(), json.RootElement.GetProperty("dispositionReason").GetString());
    }

    [Fact]
    public async Task GetByIdIsPassiveAndPreservesEmptySelectedChain()
    {
        var value = ApiEvaluationFixture.Create(Guid.NewGuid(), DispositionReasonCode.EntryCandidate, true); var repo = new InMemoryEvaluationRepository(value);
        using var factory = CreateFactory(new ThrowingWriter(), TimeProvider.System, repo); using var client = factory.CreateClient();
        var response = await client.GetAsync($"/api/entry-evaluations/{value.Evaluation.EntryStrategyEvaluationId}"); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal(value.Evaluation.EntryStrategyEvaluationId, json.RootElement.GetProperty("entryStrategyEvaluationId").GetGuid()); Assert.Equal("EntryCandidate", json.RootElement.GetProperty("dispositionReason").GetString()); Assert.Contains(json.RootElement.GetProperty("selectedOptionChains").EnumerateArray(), x => x.GetProperty("contracts").GetArrayLength() == 0);
    }

    [Fact]
    public async Task HistoryIsScopedNewestFirstAndEmptyHistoryIsReadable()
    {
        var a = Guid.NewGuid(); var older = ApiEvaluationFixture.Create(a, DispositionReasonCode.EntryCandidate, true) with { Evaluation = ApiEvaluationFixture.Create(a, DispositionReasonCode.EntryCandidate, true).Evaluation with { CalculatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2) } };
        var newer = ApiEvaluationFixture.Create(a, DispositionReasonCode.NoAcceptableContract, false) with { Evaluation = ApiEvaluationFixture.Create(a, DispositionReasonCode.NoAcceptableContract, false).Evaluation with { CalculatedAtUtc = DateTimeOffset.UtcNow } };
        var emptyHolding = Guid.NewGuid(); var repo = new InMemoryEvaluationRepository(older, newer); var holdings = new FakeHoldingRepository(a, emptyHolding);
        using var factory = CreateFactory(new ThrowingWriter(), TimeProvider.System, repo, holdings); using var client = factory.CreateClient();
        var response = await client.GetAsync($"/api/holdings/{a}/entry-evaluations"); var rows = JsonSerializer.Deserialize<JsonElement[]>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal(2, rows!.Length); Assert.Equal(newer.Evaluation.EntryStrategyEvaluationId, rows[0].GetProperty("entryStrategyEvaluationId").GetGuid());
        var empty = await client.GetAsync($"/api/holdings/{emptyHolding}/entry-evaluations"); Assert.Equal(HttpStatusCode.OK, empty.StatusCode); Assert.Equal("[]", await empty.Content.ReadAsStringAsync());
    }

    private static WebApplicationFactory<Program> CreateFactory(IEntryStrategyEvaluationWriter writer, TimeProvider time, InMemoryEvaluationRepository repo, IHoldingRepository? holdings = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b => { b.UseEnvironment("Testing"); b.ConfigureServices(s => { s.RemoveAll<IEntryStrategyEvaluationWriter>(); s.AddSingleton(writer); s.RemoveAll<IEntryStrategyEvaluationRepository>(); s.AddSingleton<IEntryStrategyEvaluationRepository>(repo); s.RemoveAll<TimeProvider>(); s.AddSingleton(time); if (holdings is not null) { s.RemoveAll<IHoldingRepository>(); s.AddSingleton(holdings); } }); });

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private sealed class CapturingWriter(PersistedEntryStrategyEvaluation value) : IEntryStrategyEvaluationWriter { public int Calls { get; private set; } public DateTimeOffset Timestamp { get; private set; } public Task<PersistedEntryStrategyEvaluation> CreateAsync(Guid holdingId, DateTimeOffset evaluationTimestampUtc, CancellationToken cancellationToken = default) { Calls++; Timestamp = evaluationTimestampUtc; return Task.FromResult(value); } }
    private sealed class ThrowingWriter : IEntryStrategyEvaluationWriter { public Task<PersistedEntryStrategyEvaluation> CreateAsync(Guid h, DateTimeOffset t, CancellationToken c = default) => throw new InvalidOperationException("GET must not write"); }
    private sealed class InMemoryEvaluationRepository(params PersistedEntryStrategyEvaluation[] seed) : IEntryStrategyEvaluationRepository
    {
        private readonly Dictionary<Guid, PersistedEntryStrategyEvaluation> values = seed.ToDictionary(x => x.Evaluation.EntryStrategyEvaluationId);
        public Task InsertAsync(PersistedEntryStrategyEvaluation e, CancellationToken c = default) { values[e.Evaluation.EntryStrategyEvaluationId] = e; return Task.CompletedTask; }
        public Task<PersistedEntryStrategyEvaluation?> GetByIdAsync(Guid id, CancellationToken c = default) => Task.FromResult(values.GetValueOrDefault(id));
        public Task<IReadOnlyList<EntryStrategyEvaluationHistoryItem>> GetHistoryByHoldingAsync(Guid h, CancellationToken c = default) => Task.FromResult<IReadOnlyList<EntryStrategyEvaluationHistoryItem>>(values.Values.Where(x => x.Evaluation.Context.Holding.HoldingId == h).OrderByDescending(x => x.Evaluation.CalculatedAtUtc).Select(x => new EntryStrategyEvaluationHistoryItem(x.Evaluation.EntryStrategyEvaluationId, h, x.Evaluation.Context.Holding.Symbol, x.Evaluation.Context.IndicatorAsOfDate, x.Evaluation.Context.EvaluationTimestampUtc, x.Evaluation.CalculatedAtUtc, x.Evaluation.Ccos?.Status ?? ScoreStatus.Unavailable, x.Evaluation.Ccos?.Score, x.Evaluation.Ccos?.Classification, x.Evaluation.EntryCandidateExists, x.Evaluation.PreferredInitialOptionSymbol, x.Evaluation.PreferredInitialStrike, x.Evaluation.PreferredInitialExpiration, x.Evaluation.DispositionReason, x.Evaluation.Context.Indicators.IndicatorCalculationVersion.Value, x.Evaluation.Context.Configuration.Version, x.Evaluation.Context.StrategyVersion.Value)).ToArray());
    }
    private sealed class FakeHoldingRepository(params Guid[] ids) : IHoldingRepository
    {
        private readonly Account account = new("test", Broker.Fidelity, AccountType.Taxable, false);
        public Task<Holding?> GetByIdAsync(Guid id, CancellationToken c = default) =>
            Task.FromResult<Holding?>(ids.Contains(id) ? new Holding(account, "MSFT", AssetType.Stock, 100, AssignmentSensitivity.Level3, TaxSensitivity.Moderate, 1, .25, .12, .18, 70, 80, .1m, .1, 1) : null);
    }
}
internal static class ApiEvaluationFixture
{
    public static PersistedEntryStrategyEvaluation Create(Guid holdingId, DispositionReasonCode reason, bool candidate)
    {
        var h = new HoldingContext(holdingId, "MSFT", AssetType.Stock, AssignmentSensitivity.Level3, TaxSensitivity.Moderate, .25, .12, .18, 70, 80, .1m, .1);
        var i = new IndicatorContext("MSFT", new DateOnly(2026, 9, 17), Missing<double>(), Missing<double>(), Missing<double>(), Missing<double>(), Missing<double>(), Missing<double>(), Missing<decimal>(), Missing<decimal>(), Missing<decimal>(), Missing<double>(), Missing<decimal>(), Missing<double>(), Missing<int>(), Missing<int>(), null, MarketRegime.InsufficientData, MarketRegime.InsufficientData, new IndicatorCalculationVersion("3.0.0"), new ConfigurationVersion(1), DateTimeOffset.UtcNow);
        var c = new EvaluationContext(h, i, new EarningsContext(AvailabilityStatus.Unavailable, null), i.AsOfDate, DateTimeOffset.UtcNow, new EntryStrategyConfiguration { Version = new ConfigurationVersion(1) }, new StrategyVersion("4.0.0"));
        var option = new OptionContractContext("MSFT-C", "MSFT", DateTimeOffset.UtcNow, new DateOnly(2026, 10, 16), 105m, OptionContractType.Call, 2m, 2.1m, 500, .2, .2, -.05, 100m, 2m, 10, "Tradier");
        var input = new ScoreInput("RSI14", AvailabilityStatus.Available, "50"); var component = new ScoreComponentResult(ScoreComponentCode.CcosRsi, "RSI", ScoreStatus.Available, 15, 15, [input], [], "ok"); var score = new ScoreResult(ScoreStatus.Available, 85, 100, "Strong", 70, true, [component], [], "ok"); var gate = new GateResult(GateCode.Dte, GateStatus.Passed, null, [], [], "ok"); var ce = new ContractEvaluation(option, new ContractDerivedMetrics(28, 2, 2.05m, .05, 5, .05, .02, .001, .25), [gate], score, true, candidate, candidate ? 1 : null, [], []);
        var e = new EntryStrategyEvaluation(Guid.NewGuid(), DateTimeOffset.UtcNow, c, score, [gate], [ce], candidate, candidate ? option.OptionSymbol : null, candidate ? option.Strike : null, candidate ? option.Expiration : null, candidate ? 2m : null, reason, [], []); var chains = new[] { new OptionChain("MSFT", option.Expiration, option.ObservationTimestampUtc, candidate ? [new OptionContractSnapshot(option.OptionSymbol, option.UnderlyingSymbol, option.ObservationTimestampUtc, option.Expiration, option.Strike, OptionType.Call, option.Bid, option.Ask, option.Last, option.Volume, option.OpenInterest, option.ImpliedVolatility, option.Delta, null, option.Theta, null, option.UnderlyingPrice, option.Provider)] : [], "Tradier"), new OptionChain("MSFT", option.Expiration.AddDays(7), option.ObservationTimestampUtc, [], "Tradier") }; return new(e, chains, [option]);
    }
    private static IndicatorValue<T> Missing<T>() where T : struct => IndicatorValue<T>.InsufficientData();
}

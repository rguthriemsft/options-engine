using System.Text.Json;
using System.Text.Json.Serialization;
using OptionsEngine.Application.EntryStrategy;
using OptionsEngine.MarketData.Models;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Api.EntryStrategy;

/// <summary>HTTP projections for immutable Phase 4 evaluations.</summary>
public static class EntryStrategyEndpoints
{
    public static IEndpointRouteBuilder MapEntryStrategyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var evaluations = endpoints.MapGroup("/api").AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (HoldingNotFoundException)
            {
                return ApiErrors.NotFound("HOLDING_NOT_FOUND", "Holding was not found.");
            }
            catch (HoldingDisabledException)
            {
                return ApiErrors.Conflict("HOLDING_DISABLED", "Holding is disabled.");
            }
        });
        evaluations.MapPost("/holdings/{holdingId:guid}/entry-evaluations", Create)
            .WithName("CreateEntryStrategyEvaluation");
        evaluations.MapGet("/entry-evaluations/{entryStrategyEvaluationId:guid}", GetById)
            .WithName("GetEntryStrategyEvaluation");
        evaluations.MapGet("/holdings/{holdingId:guid}/entry-evaluations", GetHistory)
            .WithName("GetHoldingEntryStrategyEvaluationHistory");
        return endpoints;
    }

    private static async Task<IResult> Create(Guid holdingId, IEntryStrategyEvaluationWriter service,
        TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var evaluationTimestamp = timeProvider.GetUtcNow();
        var persisted = await service.CreateAsync(holdingId, evaluationTimestamp, cancellationToken);
        var response = EntryStrategyEvaluationResponse.From(persisted);
        return Results.CreatedAtRoute("GetEntryStrategyEvaluation",
            new { entryStrategyEvaluationId = response.EntryStrategyEvaluationId }, response);
    }

    private static async Task<IResult> GetById(Guid entryStrategyEvaluationId,
        IEntryStrategyEvaluationRepository repository, CancellationToken cancellationToken)
    {
        var persisted = await repository.GetByIdAsync(entryStrategyEvaluationId, cancellationToken);
        return persisted is null
            ? ApiErrors.NotFound("ENTRY_STRATEGY_EVALUATION_NOT_FOUND", "Entry strategy evaluation was not found.")
            : Results.Ok(EntryStrategyEvaluationResponse.From(persisted));
    }

    private static async Task<IResult> GetHistory(Guid holdingId, IHoldingRepository holdingRepository,
        IEntryStrategyEvaluationRepository repository, CancellationToken cancellationToken)
    {
        if (await holdingRepository.GetByIdAsync(holdingId, cancellationToken) is null)
            return ApiErrors.NotFound("HOLDING_NOT_FOUND", "Holding was not found.");

        var history = await repository.GetHistoryByHoldingAsync(holdingId, cancellationToken);
        return Results.Ok(history.Select(EntryStrategyEvaluationHistoryResponse.From).ToArray());
    }
}

public sealed record EntryStrategyEvaluationResponse(
    Guid EntryStrategyEvaluationId,
    Guid HoldingId,
    string Symbol,
    DateOnly IndicatorAsOfDate,
    DateTimeOffset EvaluationTimestampUtc,
    DateTimeOffset CalculatedAtUtc,
    string IndicatorCalculationVersion,
    int ConfigurationVersion,
    string StrategyVersion,
    HoldingContext Holding,
    IndicatorContext Indicators,
    EarningsContext Earnings,
    EntryStrategyConfiguration Configuration,
    ScoreResult? Ccos,
    IReadOnlyList<GateResult> UnderlyingGates,
    IReadOnlyList<OptionChain> SelectedOptionChains,
    IReadOnlyList<OptionContractContext> SelectedContracts,
    IReadOnlyList<ContractEvaluation> Contracts,
    bool EntryCandidateExists,
    string? PreferredInitialOptionSymbol,
    decimal? PreferredInitialStrike,
    DateOnly? PreferredInitialExpiration,
    decimal? PreferredInitialReferencePremium,
    DispositionReasonCode DispositionReason,
    IReadOnlyList<MissingInputCode> MissingInputs,
    IReadOnlyList<string> Explanations)
{
    public static EntryStrategyEvaluationResponse From(PersistedEntryStrategyEvaluation value)
    {
        var evaluation = value.Evaluation;
        var context = evaluation.Context;
        return new(evaluation.EntryStrategyEvaluationId, context.Holding.HoldingId, context.Holding.Symbol,
            context.IndicatorAsOfDate, context.EvaluationTimestampUtc, evaluation.CalculatedAtUtc,
            context.Indicators.IndicatorCalculationVersion.Value, context.Configuration.Version.Value,
            context.StrategyVersion.Value, context.Holding, context.Indicators, context.Earnings,
            context.Configuration, evaluation.Ccos, evaluation.UnderlyingGates, value.SelectedOptionChains,
            value.SelectedContracts, evaluation.Contracts, evaluation.EntryCandidateExists,
            evaluation.PreferredInitialOptionSymbol, evaluation.PreferredInitialStrike,
            evaluation.PreferredInitialExpiration, evaluation.PreferredInitialReferencePremium,
            evaluation.DispositionReason, evaluation.MissingInputs, evaluation.Explanations);
    }
}

public sealed record EntryStrategyEvaluationHistoryResponse(
    Guid EntryStrategyEvaluationId,
    Guid HoldingId,
    string Symbol,
    DateOnly IndicatorAsOfDate,
    DateTimeOffset EvaluationTimestampUtc,
    DateTimeOffset CalculatedAtUtc,
    ScoreStatus CcosStatus,
    double? CcosScore,
    string? CcosClassification,
    bool EntryCandidateExists,
    string? PreferredInitialOptionSymbol,
    decimal? PreferredInitialStrike,
    DateOnly? PreferredInitialExpiration,
    DispositionReasonCode DispositionReason,
    string IndicatorCalculationVersion,
    int ConfigurationVersion,
    string StrategyVersion)
{
    public static EntryStrategyEvaluationHistoryResponse From(EntryStrategyEvaluationHistoryItem value) =>
        new(value.EntryStrategyEvaluationId, value.HoldingId, value.Symbol, value.IndicatorAsOfDate,
            value.EvaluationTimestampUtc, value.CalculatedAtUtc, value.CcosStatus, value.CcosScore,
            value.CcosClassification, value.EntryCandidateExists, value.PreferredInitialOptionSymbol,
            value.PreferredInitialStrike, value.PreferredInitialExpiration, value.DispositionReason,
            value.IndicatorCalculationVersion, value.ConfigurationVersion.Value, value.StrategyVersion);
}

internal static class ApiErrors
{
    public static IResult NotFound(string code, string title) => Error(StatusCodes.Status404NotFound, code, title);
    public static IResult Conflict(string code, string title) => Error(StatusCodes.Status409Conflict, code, title);

    public static IResult Error(int status, string code, string title) =>
        Results.Json(new { type = "about:blank", title, status, code }, statusCode: status);
}

internal sealed class ConfigurationVersionHttpJsonConverter : JsonConverter<ConfigurationVersion>
{
    public override ConfigurationVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetInt32());

    public override void Write(Utf8JsonWriter writer, ConfigurationVersion value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.Value);
}

internal sealed class IndicatorCalculationVersionHttpJsonConverter : JsonConverter<IndicatorCalculationVersion>
{
    public override IndicatorCalculationVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetString() ?? throw new JsonException("Indicator calculation version is required."));

    public override void Write(Utf8JsonWriter writer, IndicatorCalculationVersion value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

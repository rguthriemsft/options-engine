using OptionsEngine.Application.Defense;
using OptionsEngine.Application.EntryStrategy;
using OptionsEngine.Api.EntryStrategy;

namespace OptionsEngine.Api.Defense;

/// <summary>HTTP surface for immutable Phase 6 defense and roll evaluations.</summary>
public static class DefenseEndpoints
{
    public static IEndpointRouteBuilder MapDefenseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var evaluations = endpoints.MapGroup("/api").AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (HoldingNotFoundException)
            {
                return ApiErrors.NotFound("HOLDING_NOT_FOUND", "Holding was not found.");
            }
            catch (OpenShortCallPositionNotFoundException)
            {
                return ApiErrors.NotFound("OPEN_SHORT_CALL_POSITION_NOT_FOUND",
                    "Open short-call position was not found for the Holding.");
            }
            catch (HoldingDisabledException)
            {
                return ApiErrors.Conflict("HOLDING_DISABLED", "Holding is disabled.");
            }
            catch (InvalidOpenShortCallPositionException)
            {
                return ApiErrors.BadRequest("INVALID_OPEN_SHORT_CALL_POSITION",
                    "Open short-call position is not valid for a current defense evaluation.");
            }
        });

        evaluations.MapPost(
                "/holdings/{holdingId:guid}/positions/{positionId:long}/defense-evaluations", Create)
            .WithName("CreateDefenseEvaluation");
        evaluations.MapGet("/defense-evaluations/{defenseEvaluationId:guid}", GetDefenseById)
            .WithName("GetDefenseEvaluation");
        evaluations.MapGet(
                "/holdings/{holdingId:guid}/positions/{positionId:long}/defense-evaluations", GetHistory)
            .WithName("GetPositionDefenseEvaluationHistory");
        evaluations.MapGet("/roll-evaluations/{rollEvaluationId:guid}", GetRollById)
            .WithName("GetRollEvaluation");
        return endpoints;
    }

    private static async Task<IResult> Create(Guid holdingId, long positionId,
        IDefenseEvaluationOrchestrator orchestrator, IDefenseEvaluationWriter writer,
        TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (positionId <= 0)
            return ApiErrors.BadRequest("INVALID_OPEN_SHORT_CALL_POSITION_ID",
                "Open short-call position ID must be positive.");

        var bundle = await orchestrator.EvaluateAsync(holdingId, positionId,
            timeProvider.GetUtcNow(), cancellationToken);
        var persisted = await writer.PersistAsync(bundle, cancellationToken);
        return Results.CreatedAtRoute("GetDefenseEvaluation",
            new { defenseEvaluationId = persisted.DefenseEvaluation.DefenseEvaluationId },
            persisted.DefenseEvaluation);
    }

    private static async Task<IResult> GetDefenseById(Guid defenseEvaluationId,
        IDefenseEvaluationRepository repository, CancellationToken cancellationToken)
    {
        var evaluation = await repository.GetDefenseEvaluationByIdAsync(
            defenseEvaluationId, cancellationToken);
        return evaluation is null
            ? ApiErrors.NotFound("DEFENSE_EVALUATION_NOT_FOUND", "Defense evaluation was not found.")
            : Results.Ok(evaluation);
    }

    private static async Task<IResult> GetRollById(Guid rollEvaluationId,
        IDefenseEvaluationRepository repository, CancellationToken cancellationToken)
    {
        var evaluation = await repository.GetRollEvaluationByIdAsync(rollEvaluationId, cancellationToken);
        return evaluation is null
            ? ApiErrors.NotFound("ROLL_EVALUATION_NOT_FOUND", "Roll evaluation was not found.")
            : Results.Ok(evaluation);
    }

    private static async Task<IResult> GetHistory(Guid holdingId, long positionId,
        IDefenseEvaluationRepository repository, CancellationToken cancellationToken)
    {
        if (positionId <= 0)
            return ApiErrors.BadRequest("INVALID_OPEN_SHORT_CALL_POSITION_ID",
                "Open short-call position ID must be positive.");

        var history = await repository.GetDefenseEvaluationHistoryAsync(
            holdingId, positionId, cancellationToken);
        return Results.Ok(history.Select(DefenseEvaluationHistoryResponse.From).ToArray());
    }
}

public sealed record DefenseEvaluationHistoryResponse(
    Guid DefenseEvaluationId,
    long OpenShortCallPositionId,
    Guid HoldingId,
    string Symbol,
    string OptionSymbol,
    DateTimeOffset DefenseEvaluationTimestampUtc,
    DateTimeOffset CalculatedAtUtc,
    double? Drs,
    string? DrsClassification,
    string ProfitTakingSignal,
    string HardDefenseStatus,
    bool RollEngineRequired,
    string Disposition,
    Guid? RollEvaluationId,
    double? CurrentCcos)
{
    public static DefenseEvaluationHistoryResponse From(DefenseEvaluationHistoryItem value) =>
        new(value.DefenseEvaluationId, value.OpenShortCallPositionId, value.HoldingId,
            value.Symbol, value.OptionSymbol, value.DefenseEvaluationTimestampUtc,
            value.CalculatedAtUtc, value.Drs, value.DrsClassification?.ToString(),
            value.ProfitTakingSignal.ToString(), value.HardDefenseStatus.ToString(),
            value.RollEngineRequired, value.Disposition.ToString(), value.RollEvaluationId,
            value.CurrentCcos);
}

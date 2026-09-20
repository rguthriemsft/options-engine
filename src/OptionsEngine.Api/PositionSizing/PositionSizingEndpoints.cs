using OptionsEngine.Application.EntryStrategy;
using OptionsEngine.Application.PositionSizing;
using OptionsEngine.Api.EntryStrategy;
using OptionsEngine.Strategy.PositionSizing;

namespace OptionsEngine.Api.PositionSizing;

/// <summary>Thin HTTP surface for immutable Phase 5 Position Sizing evaluations; history is deferred beyond V1.</summary>
public static class PositionSizingEndpoints
{
    public static IEndpointRouteBuilder MapPositionSizingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api").AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (EntryStrategyEvaluationNotFoundException)
            { return ApiErrors.NotFound("ENTRY_STRATEGY_EVALUATION_NOT_FOUND", "Entry strategy evaluation was not found."); }
            catch (HoldingNotFoundException)
            { return ApiErrors.NotFound("HOLDING_NOT_FOUND", "Holding was not found."); }
        });
        group.MapPost("/entry-evaluations/{entryStrategyEvaluationId:guid}/position-sizing-evaluations", Create)
            .WithName("CreatePositionSizingEvaluation");
        group.MapGet("/position-sizing-evaluations/{positionSizingEvaluationId:guid}", GetById)
            .WithName("GetPositionSizingEvaluation");
        return endpoints;
    }

    private static async Task<IResult> Create(Guid entryStrategyEvaluationId, IPositionSizingEvaluationWriter writer,
        TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var evaluation = await writer.CreateAsync(entryStrategyEvaluationId, timeProvider.GetUtcNow(), cancellationToken);
        var response = PositionSizingEvaluationResponse.From(evaluation);
        return Results.CreatedAtRoute("GetPositionSizingEvaluation",
            new { positionSizingEvaluationId = response.PositionSizingEvaluationId }, response);
    }

    private static async Task<IResult> GetById(Guid positionSizingEvaluationId,
        IPositionSizingEvaluationRepository repository, CancellationToken cancellationToken)
    {
        var evaluation = await repository.GetByIdAsync(positionSizingEvaluationId, cancellationToken);
        return evaluation is null
            ? ApiErrors.NotFound("POSITION_SIZING_EVALUATION_NOT_FOUND", "Position Sizing evaluation was not found.")
            : Results.Ok(PositionSizingEvaluationResponse.From(evaluation));
    }
}

public sealed record PositionSizingHoldingResponse(Guid HoldingId, Guid AccountId, string Symbol, string AssetType,
    decimal SharesOwned, string AssignmentSensitivity, string TaxSensitivity, decimal MaximumCoverageRatio,
    double MaximumDeltaExposureRatio)
{
    public static PositionSizingHoldingResponse From(PositionSizingHoldingContext x) => new(x.HoldingId, x.AccountId,
        x.Symbol, x.AssetType.ToString(), x.SharesOwned, x.AssignmentSensitivity.ToString(), x.TaxSensitivity.ToString(),
        x.MaximumCoverageRatio, x.MaximumDeltaExposureRatio);
}

public sealed record ExistingShortCallExposureResponse(Guid HoldingId, string OptionSymbol, int Contracts, decimal Strike,
    DateOnly Expiration)
{
    public static ExistingShortCallExposureResponse From(ExistingShortCallExposure x) => new(x.HoldingId, x.OptionSymbol,
        x.Contracts, x.Strike, x.Expiration);
}

public sealed record ExistingShortCallDeltaObservationResponse(string OptionSymbol, double? Delta,
    DateTimeOffset ObservationTimestampUtc)
{
    public static ExistingShortCallDeltaObservationResponse From(ExistingShortCallDeltaObservation x) =>
        new(x.OptionSymbol, x.Delta, x.ObservationTimestampUtc);
}

public sealed record PortfolioConcentrationHoldingResponse(Guid HoldingId, Guid AccountId, string Symbol,
    decimal SharesOwned, decimal? AsOfPrice, DateOnly? PriceObservationDate)
{
    public static PortfolioConcentrationHoldingResponse From(PortfolioConcentrationHolding x) => new(x.HoldingId,
        x.AccountId, x.Symbol, x.SharesOwned, x.AsOfPrice, x.PriceObservationDate);
}

public sealed record PortfolioConcentrationResponse(Guid TargetHoldingId, Guid AccountId, DateOnly IndicatorAsOfDate,
    IReadOnlyList<PortfolioConcentrationHoldingResponse> Holdings)
{
    public static PortfolioConcentrationResponse From(PortfolioConcentrationContext x) => new(x.TargetHoldingId,
        x.AccountId, x.IndicatorAsOfDate, x.Holdings.Select(PortfolioConcentrationHoldingResponse.From).ToArray());
}

public sealed record PositionSizingLimitingFactorResponse(string Code, string Explanation)
{
    public static PositionSizingLimitingFactorResponse From(PositionSizingLimitingFactor x) => new(x.Code.ToString(), x.Explanation);
}

public sealed record PositionSizingEvaluationResponse(
    Guid PositionSizingEvaluationId, Guid EntryStrategyEvaluationId, DateTimeOffset CalculatedAtUtc,
    DateTimeOffset SizingTimestampUtc, PositionSizingHoldingResponse? HoldingContext,
    IReadOnlyList<ExistingShortCallExposureResponse> ExistingShortCallExposure,
    IReadOnlyList<ExistingShortCallDeltaObservationResponse> ExistingShortCallDeltaObservations,
    PortfolioConcentrationResponse? PortfolioConcentrationContext, int ConfigurationVersion,
    string PositionSizingStrategyVersion, PositionSizingConfiguration ResolvedConfiguration, string Status,
    IReadOnlyList<string> ReasonCodes, IReadOnlyList<string> MissingInputs, decimal? SharesOwned,
    int? PhysicalCapacityContracts, int? ExistingCoveredContracts, decimal? ExistingCoveredShares,
    decimal? AvailableShares, int? AvailableContracts, double? Ccos, double? CcosBaseCoverageRatio,
    double? AssignmentSensitivityModifier, double? AssignmentSensitivityMaximumRatio,
    double? PreferredContractScore, double? ContractQualityModifier, string? PortfolioConcentrationStatus,
    double? PortfolioWeight, double? ConcentrationModifier, double? RawCoverageRatio, double? DesiredCoverageRatio,
    int? DesiredTotalContracts, int? DesiredAdditionalContracts, int? PhysicalLimitedAdditionalContracts,
    double? ExistingDeltaShares, double? ExistingDER, double? MaximumDER, int? DERLimitedAdditionalContracts,
    int? AdditionalContracts, int? ResultingTotalContracts,
    IReadOnlyList<PositionSizingLimitingFactorResponse> LimitingFactors, string Explanation)
{
    public static PositionSizingEvaluationResponse From(PositionSizingEvaluation x)
    {
        var r = x.Result;
        return new(x.PositionSizingEvaluationId, x.EntryStrategyEvaluationId, x.CalculatedAtUtc, x.SizingTimestampUtc,
            x.HoldingContext is null ? null : PositionSizingHoldingResponse.From(x.HoldingContext),
            x.ExistingShortCallExposure.Select(ExistingShortCallExposureResponse.From).ToArray(),
            x.ExistingShortCallDeltaObservations.Select(ExistingShortCallDeltaObservationResponse.From).ToArray(),
            x.PortfolioConcentrationContext is null ? null : PortfolioConcentrationResponse.From(x.PortfolioConcentrationContext),
            x.ConfigurationVersion.Value, x.StrategyVersion.Value, x.ResolvedConfiguration, r.Status.ToString(),
            r.ReasonCodes.Select(y => y.ToString()).ToArray(), r.MissingInputs.Select(y => y.ToString()).ToArray(),
            r.SharesOwned, r.PhysicalCapacityContracts, r.ExistingCoveredContracts, r.ExistingCoveredShares,
            r.AvailableShares, r.AvailableContracts, r.Ccos, r.CcosBaseCoverageRatio, r.AssignmentSensitivityModifier,
            r.AssignmentSensitivityMaximumRatio, r.PreferredContractScore, r.ContractQualityModifier,
            r.PortfolioConcentrationStatus?.ToString(), r.PortfolioWeight, r.ConcentrationModifier, r.RawCoverageRatio,
            r.DesiredCoverageRatio, r.DesiredTotalContracts, r.DesiredAdditionalContracts,
            r.PhysicalLimitedAdditionalContracts, r.ExistingDeltaShares, r.ExistingDer, r.MaximumDer,
            r.DerLimitedAdditionalContracts, r.AdditionalContracts, r.ResultingTotalContracts,
            r.LimitingFactors.Select(PositionSizingLimitingFactorResponse.From).ToArray(), r.Explanation);
    }
}

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

public sealed record HoldingResponse(Guid HoldingId, string Symbol, string AssetType, string AssignmentSensitivity,
    string TaxSensitivity, double MaximumInitialDelta, double PreferredDeltaMinimum, double PreferredDeltaMaximum,
    double MinimumCcos, double MinimumContractScore, decimal MinimumPremium, double MinimumAnnualizedYield)
{
    public static HoldingResponse From(HoldingContext x) => new(x.HoldingId, x.Symbol, x.AssetType.ToString(), x.AssignmentSensitivity.ToString(), x.TaxSensitivity.ToString(), x.MaximumInitialDelta, x.PreferredDeltaMinimum, x.PreferredDeltaMaximum, x.MinimumCcos, x.MinimumContractScore, x.MinimumPremium, x.MinimumAnnualizedYield);
}
public sealed record IndicatorValueResponse<T>(T? Value, string Status) where T : struct
{
    public static IndicatorValueResponse<T> From(IndicatorValue<T> x) => new(x.Value, x.Status.ToString());
}
public sealed record IndicatorResponse(string Symbol, DateOnly AsOfDate, IndicatorValueResponse<double> Iv30,
    IndicatorValueResponse<double> IvRank, IndicatorValueResponse<double> IvPercentile, IndicatorValueResponse<double> RealizedVolatility30,
    IndicatorValueResponse<double> Rsi14, IndicatorValueResponse<double> BollingerPercentB, IndicatorValueResponse<double> BollingerBandwidth,
    IndicatorValueResponse<decimal> Sma20, IndicatorValueResponse<decimal> Sma50, IndicatorValueResponse<decimal> Sma200,
    IndicatorValueResponse<double> MacdHistogram, IndicatorValueResponse<decimal> ResistancePrice,
    IndicatorValueResponse<double> DistanceToResistancePercent, IndicatorValueResponse<int> ResistanceTouchCount,
    IndicatorValueResponse<int> ResistanceAgeTradingDays, string? ResistanceUnavailableReason, string MarketRegime, string SectorRegime,
    string IndicatorCalculationVersion, int ConfigurationVersion, DateTimeOffset IndicatorCalculatedAtUtc)
{
    public static IndicatorResponse From(IndicatorContext x) => new(x.Symbol, x.AsOfDate, IndicatorValueResponse<double>.From(x.Iv30), IndicatorValueResponse<double>.From(x.IvRank), IndicatorValueResponse<double>.From(x.IvPercentile), IndicatorValueResponse<double>.From(x.RealizedVolatility30), IndicatorValueResponse<double>.From(x.Rsi14), IndicatorValueResponse<double>.From(x.BollingerPercentB), IndicatorValueResponse<double>.From(x.BollingerBandwidth), IndicatorValueResponse<decimal>.From(x.Sma20), IndicatorValueResponse<decimal>.From(x.Sma50), IndicatorValueResponse<decimal>.From(x.Sma200), IndicatorValueResponse<double>.From(x.MacdHistogram), IndicatorValueResponse<decimal>.From(x.ResistancePrice), IndicatorValueResponse<double>.From(x.DistanceToResistancePercent), IndicatorValueResponse<int>.From(x.ResistanceTouchCount), IndicatorValueResponse<int>.From(x.ResistanceAgeTradingDays), x.ResistanceUnavailableReason?.ToString(), x.MarketRegime.ToString(), x.SectorRegime.ToString(), x.IndicatorCalculationVersion.Value, x.ConfigurationVersion.Value, x.IndicatorCalculatedAtUtc);
}
public sealed record EarningsResponse(string Status, DateOnly? NextEarningsDate)
{
    public static EarningsResponse From(EarningsContext x) => new(x.Status.ToString(), x.NextEarningsDate);
}
public sealed record ScoreInputResponse(string Code, string Status, string? Value)
{
    public static ScoreInputResponse From(ScoreInput x) => new(x.Code, x.Status.ToString(), x.Value);
}
public sealed record ScoreComponentResponse(string Code, string Name, string Status, double? Score, double MaximumScore, IReadOnlyList<ScoreInputResponse> Inputs, IReadOnlyList<string> MissingInputs, string Explanation)
{
    public static ScoreComponentResponse From(ScoreComponentResult x) => new(x.Code.ToString(), x.Name, x.Status.ToString(), x.Score, x.MaximumScore, x.Inputs.Select(ScoreInputResponse.From).ToArray(), x.MissingInputs.Select(y => y.ToString()).ToArray(), x.Explanation);
}
public sealed record ScoreResponse(string Status, double? Score, double MaximumScore, string? Classification, double ConfiguredMinimum, bool? MeetsConfiguredMinimum, IReadOnlyList<ScoreComponentResponse> Components, IReadOnlyList<string> MissingInputs, string Explanation)
{
    public static ScoreResponse? From(ScoreResult? x) => x is null ? null : new(x.Status.ToString(), x.Score, x.MaximumScore, x.Classification, x.ConfiguredMinimum, x.MeetsConfiguredMinimum, x.Components.Select(ScoreComponentResponse.From).ToArray(), x.MissingInputs.Select(y => y.ToString()).ToArray(), x.Explanation);
}
public sealed record GateResponse(string Code, string Status, string? ReasonCode, IReadOnlyList<ScoreInputResponse> Inputs, IReadOnlyList<string> MissingInputs, string Explanation)
{
    public static GateResponse From(GateResult x) => new(x.Code.ToString(), x.Status.ToString(), x.ReasonCode?.ToString(), x.Inputs.Select(ScoreInputResponse.From).ToArray(), x.MissingInputs.Select(y => y.ToString()).ToArray(), x.Explanation);
}
public sealed record OptionResponse(string OptionSymbol, string UnderlyingSymbol, DateTimeOffset ObservationTimestampUtc, DateOnly Expiration, decimal Strike, string OptionType, decimal? Bid, decimal? Ask, decimal? Last, long? Volume, long? OpenInterest, double? ImpliedVolatility, double? Delta, double? Theta, decimal? UnderlyingPrice, string Provider)
{
    public static OptionResponse From(OptionContractContext x) => new(x.OptionSymbol, x.UnderlyingSymbol, x.ObservationTimestampUtc, x.Expiration, x.Strike, x.OptionType.ToString(), x.Bid, x.Ask, x.Last, x.Volume, x.OpenInterest, x.ImpliedVolatility, x.Delta, x.Theta, x.UnderlyingPrice, x.Provider);
    public static OptionResponse From(OptionContractSnapshot x) => new(x.OptionSymbol, x.UnderlyingSymbol, x.Timestamp, x.Expiration, x.Strike, x.OptionType.ToString(), x.Bid, x.Ask, x.Last, x.Volume, x.OpenInterest, x.ImpliedVolatility, x.Delta, x.Theta, x.UnderlyingPrice, x.Provider);
}
public sealed record OptionChainResponse(string UnderlyingSymbol, DateOnly Expiration, DateTimeOffset ObservationTimestampUtc, string Provider, IReadOnlyList<OptionResponse> Contracts)
{
    public static OptionChainResponse From(OptionChain x) => new(x.UnderlyingSymbol, x.Expiration, x.Timestamp, x.Provider, x.Contracts.Select(OptionResponse.From).ToArray());
}
public sealed record ContractResponse(OptionResponse Contract, ContractDerivedMetrics DerivedMetrics, IReadOnlyList<GateResponse> Gates, bool HardGateEligible, ScoreResponse? ContractScore, int? Rank, bool EntryAcceptable, IReadOnlyList<string> MissingInputs, IReadOnlyList<string> Explanations)
{
    public static ContractResponse From(ContractEvaluation x) => new(OptionResponse.From(x.Contract), x.DerivedMetrics, x.Gates.Select(GateResponse.From).ToArray(), x.HardGateEligible, ScoreResponse.From(x.ContractScore), x.Rank, x.EntryAcceptable, x.MissingInputs.Select(y => y.ToString()).ToArray(), x.Explanations);
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
    HoldingResponse Holding,
    IndicatorResponse Indicators,
    EarningsResponse Earnings,
    ConfigurationResponse Configuration,
    ScoreResponse? Ccos,
    IReadOnlyList<GateResponse> UnderlyingGates,
    IReadOnlyList<OptionChainResponse> SelectedOptionChains,
    IReadOnlyList<OptionResponse> SelectedContracts,
    IReadOnlyList<ContractResponse> Contracts,
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
            context.StrategyVersion.Value, HoldingResponse.From(context.Holding), IndicatorResponse.From(context.Indicators),
            EarningsResponse.From(context.Earnings), ConfigurationResponse.From(context.Configuration), ScoreResponse.From(evaluation.Ccos),
            evaluation.UnderlyingGates.Select(GateResponse.From).ToArray(), value.SelectedOptionChains.Select(OptionChainResponse.From).ToArray(),
            value.SelectedContracts.Select(OptionResponse.From).ToArray(), evaluation.Contracts.Select(ContractResponse.From).ToArray(), evaluation.EntryCandidateExists,
            evaluation.PreferredInitialOptionSymbol, evaluation.PreferredInitialStrike,
            evaluation.PreferredInitialExpiration, evaluation.PreferredInitialReferencePremium,
            evaluation.DispositionReason, evaluation.MissingInputs, evaluation.Explanations);
    }
}

public sealed record ConfigurationResponse(int Version, CcosConfiguration Ccos, ContractScoreConfiguration ContractScore, ContractEligibilityConfiguration ContractEligibility)
{
    public static ConfigurationResponse From(EntryStrategyConfiguration x) => new(x.Version.Value, x.Ccos, x.ContractScore, x.ContractEligibility);
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

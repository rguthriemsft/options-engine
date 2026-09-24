using System.Collections.Immutable;
using OptionsEngine.Application.EntryStrategy;
using OptionsEngine.Domain.Accounts;
using OptionsEngine.MarketData;
using OptionsEngine.Strategy.Defense;
using OptionsEngine.Strategy.EntryStrategy;

namespace OptionsEngine.Application.Defense;

/// <summary>Assembles one complete, non-persisted Phase 6 analytical evaluation.</summary>
public sealed class DefenseEvaluationOrchestrator(
    IHoldingRepository holdingRepository,
    ICurrentOpenShortCallPositionRepository positionRepository,
    IDefenseMarketDataRepository marketDataRepository,
    IEarningsDateSource earningsDateSource,
    ICurrentCcosResolver currentCcosResolver,
    IMarketDataProvider marketDataProvider,
    DefenseRollConfiguration configuration,
    TimeProvider timeProvider) : IDefenseEvaluationOrchestrator
{
    private readonly DefenseEvaluator defenseEvaluator = new();
    private readonly RollCandidateEvaluator rollCandidateEvaluator = new();
    private readonly DefenseDispositionEvaluator dispositionEvaluator = new();

    public async Task<DefenseEvaluationBundle> EvaluateAsync(Guid holdingId,
        long openShortCallPositionId, DateTimeOffset defenseEvaluationTimestampUtc,
        CancellationToken cancellationToken = default)
    {
        if (defenseEvaluationTimestampUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Defense evaluation timestamp must be UTC.",
                nameof(defenseEvaluationTimestampUtc));
        configuration.Validate();

        var holding = await holdingRepository.GetByIdAsync(holdingId, cancellationToken)
            ?? throw new HoldingNotFoundException(holdingId);
        if (!holding.IsEnabled)
            throw new HoldingDisabledException(holdingId);

        var position = await positionRepository.GetByIdAsync(holdingId, openShortCallPositionId,
            cancellationToken)
            ?? throw new OpenShortCallPositionNotFoundException(holdingId, openShortCallPositionId);
        var evaluationDate = DefenseEvaluator.EvaluationDate(defenseEvaluationTimestampUtc);
        if (position.Expiration < evaluationDate)
            throw new InvalidOpenShortCallPositionException(openShortCallPositionId,
                "its expiration precedes the New York defense evaluation date.");

        var provider = marketDataProvider.ProviderName;
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        var currentObservation = await marketDataRepository.GetLatestOptionObservationAsync(
            position.OptionSymbol, provider, defenseEvaluationTimestampUtc, cancellationToken);
        var previousObservation = currentObservation is null
            ? null
            : await marketDataRepository.GetPreviousTradingDateOptionObservationAsync(
                position.OptionSymbol, provider, currentObservation.ObservationTimestampUtc,
                defenseEvaluationTimestampUtc, cancellationToken);
        var earnings = await EntryStrategyContextMapper.ResolveEarningsAsync(holding,
            defenseEvaluationTimestampUtc, earningsDateSource, cancellationToken);
        var holdingContext = new DefenseHoldingContext(holding.HoldingId, holding.Symbol,
            holding.AssetType, holding.AssignmentSensitivity, holding.TaxSensitivity);
        var defenseInput = new DefenseEvaluationInput(position, holdingContext, currentObservation,
            previousObservation, null, earnings, defenseEvaluationTimestampUtc,
            configuration.Defense, configuration.Roll, configuration.Defense.Version,
            configuration.DefenseStrategyVersion, configuration.RollStrategyVersion);
        var defenseResult = defenseEvaluator.Evaluate(defenseInput);

        RollCandidateStrategyResult? rollCandidates = null;
        CurrentCcosContext? currentCcosContext = null;
        if (defenseResult.RollEngineRequired)
        {
            var minimumExpiration = evaluationDate.AddDays(configuration.Roll.MinimumReplacementDte);
            if (minimumExpiration <= position.Expiration)
                minimumExpiration = position.Expiration.AddDays(1);
            var maximumExpiration = evaluationDate.AddDays(configuration.Roll.MaximumReplacementDte);
            IReadOnlyList<SelectedRollChainSnapshot> selectedChains = minimumExpiration <= maximumExpiration
                ? await marketDataRepository.GetLatestOptionChainsAsync(holding.Symbol, provider,
                    minimumExpiration, maximumExpiration, defenseEvaluationTimestampUtc, cancellationToken)
                : [];
            var candidateInput = new RollCandidateEvaluationInput(position, holdingContext,
                currentObservation, defenseResult, earnings, defenseEvaluationTimestampUtc,
                [.. selectedChains], configuration.Defense.Drs, configuration.Roll);
            rollCandidates = rollCandidateEvaluator.Evaluate(candidateInput);

            if (rollCandidates.Candidates.Any(candidate =>
                    candidate.State == RollCandidateEvaluationState.Rankable))
                currentCcosContext = await currentCcosResolver.ResolveAsync(holding, earnings,
                    defenseEvaluationTimestampUtc, configuration.Defense.Version, cancellationToken);
        }

        var currentCcos = currentCcosContext?.Score;
        var disposition = dispositionEvaluator.Evaluate(new DefenseDispositionEvaluationInput(
            defenseResult, rollCandidates, currentCcos, configuration.Roll));
        var calculatedAtUtc = timeProvider.GetUtcNow();
        if (calculatedAtUtc.Offset != TimeSpan.Zero)
            throw new InvalidOperationException("TimeProvider.GetUtcNow() must return a UTC value.");

        var defenseEvaluationId = Guid.NewGuid();
        Guid? rollEvaluationId = defenseResult.RollEngineRequired ? Guid.NewGuid() : null;
        var rollEvaluation = rollCandidates is null || rollEvaluationId is null
            ? null
            : AssembleRollEvaluation(rollEvaluationId.Value, defenseEvaluationId,
                defenseEvaluationTimestampUtc, calculatedAtUtc, position, currentObservation,
                currentCcosContext, rollCandidates);
        var defenseEvaluation = AssembleDefenseEvaluation(defenseEvaluationId, rollEvaluationId,
            defenseEvaluationTimestampUtc, calculatedAtUtc, holding, position, holdingContext,
            currentObservation, previousObservation, earnings, currentCcosContext, defenseResult,
            disposition, rollCandidates);
        return new DefenseEvaluationBundle(defenseEvaluation, rollEvaluation);
    }

    private RollEvaluation AssembleRollEvaluation(Guid rollEvaluationId, Guid defenseEvaluationId,
        DateTimeOffset evaluationTimestampUtc, DateTimeOffset calculatedAtUtc,
        OpenShortCallPositionSnapshot position, DefenseOptionObservation? currentObservation,
        CurrentCcosContext? currentCcosContext, RollCandidateStrategyResult candidates)
    {
        var missing = candidates.Candidates.SelectMany(candidate => candidate.MissingInputs)
            .Distinct().ToImmutableArray();
        var explanations = candidates.Candidates.SelectMany(candidate => candidate.Explanations)
            .Distinct(StringComparer.Ordinal).ToImmutableArray();
        return new RollEvaluation(rollEvaluationId, defenseEvaluationId, evaluationTimestampUtc,
            calculatedAtUtc, position, currentCcosContext, currentCcosContext?.Score,
            currentObservation?.Ask is > 0 ? currentObservation.Ask : null,
            candidates.SelectedChainSnapshots, candidates.Candidates,
            candidates.PreferredOptionSymbol, candidates.PreferredStrike,
            candidates.PreferredExpiration, candidates.PreferredRqs, configuration.Roll,
            configuration.Roll.Version, configuration.RollStrategyVersion, missing, explanations);
    }

    private DefenseEvaluation AssembleDefenseEvaluation(Guid defenseEvaluationId,
        Guid? rollEvaluationId, DateTimeOffset evaluationTimestampUtc, DateTimeOffset calculatedAtUtc,
        Holding holding, OpenShortCallPositionSnapshot position, DefenseHoldingContext holdingContext,
        DefenseOptionObservation? currentObservation, DefenseOptionObservation? previousObservation,
        EarningsContext earnings, CurrentCcosContext? currentCcosContext,
        DefenseStrategyResult defense, DefenseDispositionResult disposition,
        RollCandidateStrategyResult? candidates)
    {
        var missing = defense.ProfitTaking.MissingInputs.Concat(defense.Drs.MissingInputs)
            .Concat(defense.HardTriggers.SelectMany(trigger => trigger.MissingInputs))
            .Concat(disposition.MissingInputs).ToList();
        if (candidates?.Candidates.Any(candidate =>
                candidate.MissingInputs.Contains(RollMissingInputCode.EarningsDate)) == true)
            missing.Add(DefenseMissingInputCode.EarningsDate);
        var explanations = new[] { defense.ProfitTaking.Explanation, defense.Drs.Explanation }
            .Concat(defense.HardTriggers.Select(trigger => trigger.Explanation))
            .Concat(disposition.Explanations)
            .Distinct(StringComparer.Ordinal).ToImmutableArray();
        return new DefenseEvaluation(defenseEvaluationId, position.OpenShortCallPositionId,
            holding.HoldingId, holding.Symbol, position.OptionSymbol, evaluationTimestampUtc,
            calculatedAtUtc, position, holdingContext, currentObservation, previousObservation,
            currentCcosContext, currentCcosContext?.Score, earnings, configuration.Defense,
            configuration.Roll, configuration.Defense.Version, configuration.DefenseStrategyVersion,
            configuration.RollStrategyVersion, defense.ProfitTaking, defense.Drs,
            defense.HardTriggers, defense.HardDefenseStatus, defense.RollEngineRequired,
            rollEvaluationId, disposition.Disposition, disposition.ReasonCodes,
            missing.Distinct().ToImmutableArray(), explanations);
    }
}

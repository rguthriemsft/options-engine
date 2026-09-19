# Phase 5 — Position Sizing Acceptance Checklist

## Objective

Implement deterministic Position Sizing that consumes an immutable Phase 4 entry evaluation and determines:

```text
desired total covered-call contracts
desired additional contracts
final additional contracts allowed now
resulting total covered-call contracts
```

while respecting:

```text
physical share capacity
existing short-call exposure
CCOS sizing
Assignment Sensitivity
Contract Quality
portfolio concentration
Holding maximum coverage
Delta Exposure Ratio
```

`SPECIFICATION.md` is authoritative for product/strategy behavior.

`AGENTS.md` remains authoritative for repository engineering guidance.

The detailed approved design is:

```text
docs/design/PHASE-5-POSITION-SIZING-DESIGN.md
```

Phase 5 shall not implement strike ladder allocation, DRS, profit-taking actions, roll scoring, campaign accounting, the full transaction ledger, final Recommendation lifecycle, brokerage synchronization, pending-order reservation, or automatic execution.

---

# 1. Governing Architecture

The architecture shall preserve:

```text
Immutable Phase 4 EntryStrategyEvaluation
        +
PositionSizingHoldingContext
        +
Existing short-call exposure snapshot
        +
Same-account concentration context
        +
Resolved PositionSizingConfiguration
        |
        v
Pure Phase 5 Strategy
        |
        v
PositionSizingEvaluation
        |
        v
Immutable persistence / API
```

Strategy must not directly depend on:

```text
Tradier/provider DTOs
HTTP
JSON transport
EF Core
SQLite
ASP.NET
Excel
current wall-clock time
```

Acceptance criteria:

- [ ] Given identical inputs, configuration, and strategy version, sizing is deterministic.
- [ ] Strategy is provider-independent.
- [ ] Strategy does not fetch current data.
- [ ] Application owns persistence/provider/current-state orchestration.
- [ ] Phase 4 evaluations are never modified by Phase 5.

---

# 2. Phase Boundary

Phase 5 owns Position Sizing only.

It shall produce a separate immutable:

```text
PositionSizingEvaluation
```

It shall not create the final `Recommendation`.

Acceptance criteria:

- [ ] Every persisted sizing evaluation references exactly one source `EntryStrategyEvaluationId`.
- [ ] Phase 5 never changes `EntryStrategyEvaluation`.
- [ ] No final SELL Recommendation lifecycle is introduced.
- [ ] No brokerage execution is introduced.
- [ ] No DRS, Roll Engine, RQS, campaign accounting, or profit-taking action is introduced.
- [ ] No strike-ladder allocation is implemented.

---

# 3. Phase 5A — Foundations and Contracts

Implement strongly typed, provider-independent contracts for at least:

```text
PositionSizingStrategyVersion
PositionSizingConfiguration
PositionSizingStatus
PositionSizingReasonCode
PositionSizingMissingInputCode
PositionSizingHoldingContext
ExistingShortCallExposure
ExistingShortCallDeltaObservation
PortfolioConcentrationContext
PositionSizingInput
PositionSizingResult
PositionSizingEvaluation
limiting-factor representation
```

The exact C# decomposition may differ if architecture remains equivalent.

Acceptance criteria:

- [x] `PositionSizingStatus` distinguishes Available, InsufficientData, and NotApplicable.
- [x] Stable machine-readable reason codes exist separately from human explanation text.
- [x] Missing inputs are represented explicitly.
- [x] No numeric sentinel represents unavailable financial data.
- [ ] Configuration is strongly typed and validates at startup/load time.
- [x] Invalid Holding sizing ratios fail clearly.
- [x] Strategy project has no persistence/provider/HTTP dependency.

---

# 4. Source Phase 4 Evaluation

Phase 5 consumes an immutable Phase 4 evaluation.

V1 sizes only:

```text
PreferredInitialContract
```

Acceptance criteria:

- [x] Phase 5 never recalculates CCOS.
- [x] Phase 5 never recalculates Contract Score.
- [x] Phase 5 never reruns contract hard gates.
- [x] Phase 5 never reranks contracts.
- [x] Phase 5 never selects an alternate contract.
- [x] Preferred Contract Score comes from persisted Phase 4 output.
- [ ] Preferred-contract Delta comes from the persisted Phase 4 option observation.

If:

```text
EntryCandidateExists == false
```

acceptance criteria:

- [x] Status is NotApplicable.
- [x] `AdditionalContracts = 0`.
- [x] Reason includes `NO_ENTRY_CANDIDATE`.
- [x] Result is not classified as InsufficientData merely because Phase 4 found no entry candidate.

---

# 5. Authoritative Shares and Physical Capacity

Phase 5 V1 uses:

```text
SharesOwned = Holding.Shares
```

```text
PhysicalCapacityContracts =
    floor(SharesOwned / 100)
```

Acceptance criteria:

- [x] 0 shares -> 0 physical contracts.
- [x] 99 shares -> 0.
- [x] 100 shares -> 1.
- [x] 199 shares -> 1.
- [x] 200 shares -> 2.
- [x] 250 shares -> 2.
- [x] Fractional shares never create fractional contracts.
- [x] Negative shares are invalid.
- [x] Tax-lot sum does not silently replace `Holding.Shares`.

---

# 6. Ratio Semantics and Holding Limits

V1 uses fractional ratios:

```text
0.20 = 20%
0.70 = 70%
1.00 = 100%
```

Acceptance criteria:

- [x] `MaximumCoveragePercent` must be finite and within [0,1].
- [x] `MaximumDeltaExposureRatio` must be finite and within [0,1].
- [x] Existing property names do not change percentage representation.
- [x] Invalid values fail validation rather than clamp silently.

---

# 7. Existing Covered-Call Exposure

Phase 5 requires a minimal current-state open-short-call representation.

This is not the Phase 7 transaction ledger or Campaign model.

Existing exposure counts:

> Net currently open short call contracts associated with the same Holding.

Acceptance criteria:

- [ ] Long calls do not count.
- [ ] Closed calls do not count.
- [ ] Expired calls do not count.
- [ ] Another Holding's calls do not count.
- [ ] Unexecuted recommendations do not count.
- [ ] Pending brokerage orders do not count or reserve shares.
- [ ] Phase 5 introduces no brokerage synchronization requirement.

Derived values:

```text
ExistingCoveredShares =
    ExistingCoveredContracts * 100

AvailableShares =
    max(0, SharesOwned - ExistingCoveredShares)

AvailableContracts =
    floor(AvailableShares / 100)
```

Acceptance criteria:

- [x] Existing coverage is deducted before new physical capacity is allocated.
- [x] Available shares never become negative.
- [x] Existing exposure above physical capacity produces zero additional contracts plus explicit limiting/anomaly reason.
- [x] Zero shares with existing short calls produces an inconsistent-exposure outcome rather than DER division.

---

# 8. Phase 5B — CCOS Base Coverage

Approved table:

```text
CCOS < 70         0.00
70 <= CCOS < 75   0.20
75 <= CCOS < 80   0.30
80 <= CCOS < 85   0.40
85 <= CCOS < 90   0.50
90 <= CCOS < 95   0.60
95 <= CCOS <=100  0.70
```

Acceptance criteria:

- [x] Each threshold has immediately-below / exact / immediately-above tests.
- [x] 70 enters the 0.20 band.
- [x] 75 enters the 0.30 band.
- [x] 80 enters the 0.40 band.
- [x] 85 enters the 0.50 band.
- [x] 90 enters the 0.60 band.
- [x] 95 enters the 0.70 band.
- [x] A valid Phase 4 candidate below CCOS 70 receives base coverage 0 rather than an invented band.

---

# 9. Assignment Sensitivity

Approved modifier and maximum table:

```text
ASL1  modifier 1.25   maximum 1.00
ASL2  modifier 1.10   maximum 0.90
ASL3  modifier 1.00   maximum 0.80
ASL4  modifier 0.85   maximum 0.70
ASL5  modifier 0.70   maximum 0.50
```

Acceptance criteria:

- [x] Both modifier and maximum are applied.
- [x] ASL5 default maximum is exactly 0.50.
- [x] The old 50–60% ambiguity does not remain.
- [x] Assignment Sensitivity values are configuration-backed.
- [x] Maximum acts as a total-coverage cap after multiplicative modifiers.

---

# 10. Contract Quality Modifier

Use the persisted Phase 4 preferred-contract Contract Score.

Approved table:

```text
Contract Score < 80         0.00
80 <= score < 85            0.90
85 <= score < 90            1.00
90 <= score < 95            1.10
95 <= score <= 100          1.15
```

Acceptance criteria:

- [x] Each 80/85/90/95 boundary has below/exact/above tests.
- [x] A preferred contract below 80 under custom Phase 4 thresholds yields modifier 0 and a valid zero-size result.
- [x] Phase 5 does not recalculate Contract Score.

---

# 11. Phase 5C — Portfolio Concentration

V1 concentration scope is the target Holding's own Account.

All tracked holdings in that Account participate regardless of strategy-enabled state.

Cash/untracked assets are excluded.

As-of price:

```text
latest persisted daily Close
at or before Phase4.IndicatorAsOfDate
```

Formula:

```text
HoldingMarketValue =
    Holding.Shares * AsOfPrice

PortfolioWeight =
    TargetHoldingMarketValue
    /
    Sum(MarketValue of tracked Holdings in same Account)
```

Stock table:

```text
weight < 5%           1.10
5% <= weight < 15%    1.00
15% <= weight < 25%   0.90
25% <= weight <= 40%  0.80
weight > 40%          0.70
```

Acceptance criteria:

- [x] Exactly 5% -> 1.00.
- [x] Exactly 15% -> 0.90.
- [x] Exactly 25% -> 0.80.
- [x] Exactly 40% -> 0.80.
- [x] Immediately above 40% -> 0.70.
- [x] Input order cannot affect denominator/result.
- [x] Disabled strategy holdings still participate in same-account concentration.
- [x] Cash/untracked assets do not participate.

Missing-data criteria:

- [x] Missing required price for any participating Holding makes stock concentration unavailable.
- [x] Missing price is never zero-filled.
- [x] Missing holding is never silently excluded from denominator.
- [x] Weight is never renormalized around missing data.

ETF criteria:

- [x] ETF concentration status is NotApplicable.
- [x] Effective ETF concentration modifier is 1.00.
- [x] ETF NotApplicable does not fabricate a portfolio weight of zero.

Other asset criteria:

- [x] `AssetType.Other` is unsupported/insufficient for V1 concentration.
- [x] Stock rules are not silently applied to Other.

---

# 12. Exact Coverage Calculation Order

Raw coverage:

```text
RawCoverageRatio =
    CcosBaseCoverageRatio
    * AssignmentSensitivityModifier
    * ContractQualityModifier
    * ConcentrationModifier
```

Caps:

```text
DesiredCoverageRatio =
    min(
        RawCoverageRatio,
        AssignmentSensitivityMaximumRatio,
        Holding.MaximumCoveragePercent
    )
```

Whole contracts:

```text
DesiredTotalContracts =
    floor(
        SharesOwned
        * DesiredCoverageRatio
        / 100
    )
```

Additional target:

```text
DesiredAdditionalContracts =
    max(
        0,
        DesiredTotalContracts - ExistingCoveredContracts
    )
```

Physical limit:

```text
PhysicalLimitedAdditionalContracts =
    min(
        DesiredAdditionalContracts,
        AvailableContracts
    )
```

Final action:

```text
AdditionalContracts =
    min(
        PhysicalLimitedAdditionalContracts,
        DERLimitedAdditionalContracts
    )

ResultingTotalContracts =
    ExistingCoveredContracts + AdditionalContracts
```

Acceptance criteria:

- [ ] Calculation follows this exact order.
- [x] Caps apply after multiplicative modifiers.
- [x] Whole-contract conversion floors.
- [x] Existing contracts are subtracted from desired total before computing action.
- [x] Final additional count cannot exceed available physical capacity.
- [ ] Final additional count cannot exceed DER capacity.

---

# 13. Rounding and Tax Sensitivity

All contract counts floor.

Acceptance criteria:

- [x] A value just below a whole contract floors down.
- [x] An exact whole-contract result is retained.
- [x] A value just above a whole contract still floors to that whole integer.
- [x] No ceiling or nearest-integer rounding exists.
- [x] No additional tax-sensitive contract rounding penalty exists.
- [ ] Phase 4 tax-sensitive Delta behavior remains unchanged.

---

# 14. Phase 5D — Delta Exposure Ratio

Existing exposure:

```text
ExistingDeltaShares =
    SUM(
        ExistingContracts_i
        * ExistingCallDelta_i
        * 100
    )

ExistingDER =
    ExistingDeltaShares / SharesOwned
```

Proposed exposure:

```text
ProposedDER(N) =
    (
        ExistingDeltaShares
        + N * PreferredContractDelta * 100
    )
    / SharesOwned
```

DER limits only the already-calculated physically eligible action.

DER-limited count is the largest integer `N` in:

```text
0 <= N <= PhysicalLimitedAdditionalContracts
```

satisfying:

```text
ProposedDER(N)
    <= Holding.MaximumDeltaExposureRatio
```

If `PreferredContractDelta == 0` and existing DER is within the configured maximum:

```text
DERLimitedAdditionalContracts =
    PhysicalLimitedAdditionalContracts
```

Acceptance criteria:

- [ ] DER sums different existing positions using their own Deltas.
- [ ] DER evaluates only `0 <= N <= PhysicalLimitedAdditionalContracts`.
- [ ] Preferred contract Delta = 0 with existing DER within maximum returns `DERLimitedAdditionalContracts = PhysicalLimitedAdditionalContracts`.
- [ ] DER never represents an unbounded/infinite theoretical contract capacity.
- [ ] Equality with MaximumDeltaExposureRatio is allowed.
- [ ] Proposed DER immediately above maximum rejects that additional count.
- [ ] Existing DER at maximum permits zero additional contracts.
- [ ] Existing DER above maximum permits zero additional contracts.
- [ ] Phase 5 does not generate a close recommendation because DER is already high.
- [ ] Preferred new-call Delta comes from persisted Phase 4 preferred-contract observation.
- [ ] Delta must be finite and within [0,1].
- [ ] Malformed Delta is not repaired with absolute value.
- [ ] Missing required Delta produces InsufficientData.

Existing-call observation semantics:

- [ ] Use latest persisted normalized option observation for the option symbol at/before `SizingTimestampUtc`.
- [ ] Future observations are excluded.
- [ ] Actual Delta observation/timestamp consumed is retained in the immutable sizing evaluation.
- [ ] Strategy does not fetch provider data.
- [ ] No undocumented freshness threshold exists.

---

# 15. Scaling Semantics

Phase 5 returns target state and immediate action separately:

```text
DesiredCoverageRatio
DesiredTotalContracts
DesiredAdditionalContracts
AdditionalContracts
ResultingTotalContracts
```

Acceptance criteria:

- [x] Existing below desired -> additional target is desired minus existing.
- [x] Existing equal desired -> zero additional.
- [x] Existing above desired -> zero additional.
- [x] Existing above desired includes stable reason such as `EXISTING_COVERAGE_ABOVE_TARGET`.
- [x] Phase 5 never recommends closing existing calls because the new target is lower.
- [x] Profit taking/defensive close logic remains downstream.

---

# 16. Zero-Result and Status Semantics

Top-level status:

```text
Available
InsufficientData
NotApplicable
```

Valid Available zero-size examples:

```text
CCOS base coverage = 0
Contract Quality modifier = 0
target rounds below one contract
existing coverage meets target
existing coverage exceeds target
no available shares
Holding maximum reached
Assignment Sensitivity maximum reached
DER maximum reached
```

Acceptance criteria:

- [x] Zero additional contracts is not automatically an error.
- [x] Limiting factors/reasons explain valid zero results.
- [x] No entry candidate -> NotApplicable.
- [x] Missing required calculation data -> InsufficientData.
- [x] Missing is never converted to zero.

---

# 17. Explainability

A persisted Position Sizing evaluation must expose enough structure that API/UI consumers do not recompute business logic.

At minimum preserve:

```text
status
reason codes
missing inputs

holding sizing snapshot

physical capacity

existing covered contracts/shares
available shares/contracts

CCOS and base coverage
ASL modifier and maximum
preferred Contract Score and modifier
portfolio concentration status/weight/modifier

raw coverage
desired coverage

desired total contracts
desired additional contracts

ExistingDER
MaximumDER
DER-limited additional contracts

final AdditionalContracts
ResultingTotalContracts

limiting factors
versions
human explanation
```

Acceptance criteria:

- [ ] Stable codes are persisted.
- [x] Human text is not parsed to reconstruct logic.
- [x] Missing inputs are explicit.
- [x] Limiting constraints are observable.
- [ ] All consumed current-state exposure/concentration observations are reproducible.

---

# 18. Evaluation-Time Semantics

Phase 5 owns:

```text
SizingTimestampUtc
```

It remains distinct from Phase 4:

```text
EvaluationTimestampUtc
IndicatorAsOfDate
```

Acceptance criteria:

- [x] SizingTimestampUtc is explicit/injectable.
- [x] Tests do not depend on machine local time.
- [x] Phase 4 CCOS/contract facts are not refreshed during sizing.
- [ ] Mutable Holding/exposure state is snapshotted at sizing time.
- [ ] Concentration prices are anchored to Phase 4 IndicatorAsOfDate.
- [x] No hidden maximum age between Phase 4 and Phase 5 is introduced.

---

# 19. Versioning

Persist:

```text
ConfigurationVersion
PositionSizingStrategyVersion
```

and preserve the source Phase 4 versions through the referenced/source payload.

Acceptance criteria:

- [ ] Sizing table/threshold changes require ConfigurationVersion review/change.
- [ ] Formula/order/missing-data/DER/concentration semantic changes require PositionSizingStrategyVersion review/change.
- [x] Phase 3 IndicatorCalculationVersion is not used as Position Sizing algorithm identity.
- [ ] Complete resolved Position Sizing configuration is persisted.
- [ ] Historical evaluations are never reinterpreted under newer config/strategy.

---

# 20. Phase 5E — Application Orchestration

Application shall assemble a reproducible bundle containing:

```text
source immutable EntryStrategyEvaluation
PositionSizingHoldingContext
same-account concentration context
existing open-short-call state
existing-call Delta observations
SizingTimestampUtc
resolved PositionSizingConfiguration
ConfigurationVersion
PositionSizingStrategyVersion
```

Acceptance criteria:

- [ ] Application loads source Phase 4 evaluation passively.
- [ ] Application does not recalculate Phase 4.
- [ ] Application performs persistence/current-state/market-observation access.
- [ ] Strategy remains pure and synchronous.
- [ ] Future option observations after SizingTimestampUtc are excluded from existing-call Delta lookup.
- [ ] Missing required orchestration data remains explicit.

---

# 21. Phase 5F — Immutable Persistence

Persist `PositionSizingEvaluation` as append-only history.

At minimum preserve:

```text
identity
source EntryStrategyEvaluationId
timestamps
holding sizing context
existing exposure snapshot
Delta observations
concentration inputs/prices
resolved configuration
versions
all intermediate sizing values
DER values
target/additional/final contract counts
reason codes
missing inputs
limiting factors
explanations
```

A relational summary plus structured immutable payload is acceptable if no required reproducibility information is discarded.

Acceptance criteria:

- [ ] Each calculation inserts a new immutable record.
- [ ] Later evaluation never overwrites earlier evaluation.
- [ ] Later Holding edits do not change historical retrieval.
- [ ] Later open-call-state changes do not change historical retrieval.
- [ ] Later price/Delta observations do not change historical retrieval.
- [ ] Later configuration/strategy changes do not change historical retrieval.
- [ ] Source Phase 4 evaluation remains unchanged.
- [ ] EF migration is included for new persistence state.
- [ ] Clean database migration succeeds.
- [ ] Phase 4 database upgrades forward successfully.

---

# 22. Phase 5G — HTTP API

Conceptual immutable resource:

```http
POST /api/entry-evaluations/{entryStrategyEvaluationId}/position-sizing-evaluations

GET /api/position-sizing-evaluations/{positionSizingEvaluationId}
```

A lightweight history route may be included if implementation design requires it.

Acceptance criteria:

- [ ] POST uses server-owned SizingTimestampUtc/current-state selection.
- [ ] Successful POST persists exactly one immutable sizing evaluation.
- [ ] GET by ID performs passive retrieval only.
- [ ] GET does not refresh or recalculate.
- [ ] Missing source Phase 4 evaluation returns 404.
- [ ] Phase 4 evaluation with no entry candidate may persist a NotApplicable sizing result rather than return a strategy HTTP error.
- [ ] InsufficientData sizing outcomes are successful persisted evaluations, not provider/business HTTP failures.
- [ ] Existing Phase 4 POST semantics are unchanged.
- [ ] No PUT/PATCH/DELETE exists for PositionSizingEvaluation.
- [ ] API DTOs do not expose EF entities.

Exact history-route shape, if any, must be locked before implementation of that route.

---

# 23. Golden Phase 5 Scenarios

Maintain deterministic regression scenarios.

## InitialSingleContract

- [ ] No existing calls.
- [ ] Valid Phase 4 candidate.
- [ ] Target permits exactly one call.
- [ ] AdditionalContracts = 1.
- [ ] ResultingTotalContracts = 1.

## IncrementalScaleUp

- [ ] Existing calls are below new target.
- [ ] DesiredAdditionalContracts equals target minus existing.
- [ ] Physical and DER capacity permit scale-up.
- [ ] Exact additional/resulting counts asserted.

## AlreadyAtTarget

- [ ] Existing contracts equal desired total.
- [ ] AdditionalContracts = 0.
- [ ] Result is Available.

## ExistingCoverageAboveTarget

- [ ] Existing contracts exceed desired total.
- [ ] AdditionalContracts = 0.
- [ ] Reason includes `EXISTING_COVERAGE_ABOVE_TARGET`.
- [ ] No close action is emitted.

## HoldingCoverageLimited

- [ ] Raw coverage exceeds Holding.MaximumCoveragePercent.
- [ ] DesiredCoverageRatio equals Holding maximum.
- [ ] Limiting factor identifies Holding maximum.

## AssignmentSensitivityLimited

- [ ] Raw coverage exceeds ASL maximum.
- [ ] DesiredCoverageRatio equals ASL maximum.
- [ ] Limiting factor identifies ASL cap.

## ConcentrationLimited

- [ ] Stock concentration modifier reduces raw coverage.
- [ ] Exact PortfolioWeight/modifier asserted.

## DeltaExposureLimited

- [ ] Physical/coverage target permits more calls than DER.
- [ ] DER limit reduces final additional count.
- [ ] Result remains Available.

## NoAvailableShares

- [ ] Existing calls consume physical capacity.
- [ ] AdditionalContracts = 0.
- [ ] Reason identifies no available shares/capacity.

## TargetRoundsToZero

- [ ] Nonzero coverage ratio produces fewer than 100 target covered shares.
- [ ] DesiredTotalContracts = 0.
- [ ] Valid Available zero result.

## EtfNeutralConcentration

- [ ] ETF concentration status is NotApplicable.
- [ ] Effective modifier is 1.00.
- [ ] No fabricated stock weight is required.

## MissingPortfolioPrice

- [ ] At least one participating same-account stock holding lacks required price.
- [ ] Status = InsufficientData.
- [ ] Exact missing input asserted.
- [ ] No zero substitution.

## MissingExistingCallDelta

- [ ] Existing short call lacks usable Delta observation.
- [ ] Status = InsufficientData.
- [ ] Exact missing input asserted.
- [ ] No zero/abs substitute.

## NoEntryCandidate

- [ ] Source Phase 4 EntryCandidateExists=false.
- [ ] Status = NotApplicable.
- [ ] AdditionalContracts = 0.
- [ ] Reason = NO_ENTRY_CANDIDATE.

---

# 24. Threshold Test Matrix

Every applicable threshold requires deterministic below/exact/above coverage.

At minimum:

```text
CCOS:
70
75
80
85
90
95

Contract Score:
80
85
90
95

Portfolio concentration:
5%
15%
25%
40%

Holding.MaximumCoveragePercent:
selected configured boundary

Assignment Sensitivity maximum:
each configured maximum where behavior differs

MaximumDeltaExposureRatio:
immediately below
exact
immediately above

Shares:
0
99
100
199
200
fractional quantities

Coverage-to-contract conversion:
just below integer contract
exact integer contract
just above integer contract
```

---

# 25. Test Project Ownership

`OptionsEngine.Strategy.Tests` should own:

```text
CCOS sizing curve
ASL modifier/max
Contract Quality modifier
stock concentration modifier math
coverage formula/order
coverage caps
flooring
target/additional semantics
DER math
zero-result semantics
missing-data strategy behavior
pure golden scenarios
threshold boundaries
```

Application/Infrastructure tests should own:

```text
source Phase 4 retrieval
holding snapshot assembly
same-account price assembly
existing open-short-call state
existing-call Delta observation selection
as-of/cutoff behavior
configuration/version capture
immutable persistence
migration paths
```

`OptionsEngine.Api.Tests` should own:

```text
POST creation
GET passive retrieval
404 semantics
NotApplicable/InsufficientData as persisted strategy outcomes
HTTP immutability surface
Phase 4 route non-regression
```

Routine tests must not require:

```text
live Tradier
internet
brokerage credentials
current market price
current date/time
```

---

# 26. Merge Gate

Before Phase 5 is complete:

- [ ] `dotnet restore` succeeds.
- [ ] `dotnet build --configuration Release --no-restore` succeeds.
- [ ] Release build has zero warnings/errors unless an explicitly documented repository-wide exception exists.
- [ ] `dotnet test --configuration Release --no-build` passes.
- [ ] Clean EF migration path succeeds.
- [ ] Phase 4 database upgrades to Phase 5 successfully.
- [ ] Phase 4 API behavior remains unchanged.
- [ ] Phase 4 immutable evaluation history remains readable.
- [ ] No credentials or personal financial data are introduced.
- [ ] Architecture dependency rules remain intact.
- [ ] No Phase 6+ behavior is implemented.
- [ ] No strike laddering is implemented.
- [ ] No final Recommendation lifecycle is introduced.
- [ ] Documentation matches implementation.
- [ ] Every Phase 5 numeric threshold has deterministic boundary coverage.
- [ ] All Phase 5 golden scenarios pass.
- [ ] `git diff --check` passes.

---

# 27. Work Packet Discipline

Implementation proceeds in this order:

```text
5A — Position Sizing foundations
5B — Base coverage and caps
5C — Portfolio concentration
5D — Existing exposure and DER
5E — Application orchestration
5F — Immutable persistence
5G — API and merge gate
```

No packet may introduce a formula, threshold, financial rule, freshness rule, allocation rule, rounding rule, exposure assumption, or missing-data fallback not already approved in `SPECIFICATION.md`.

If implementation reveals an ambiguity, stop that strategy change and reconcile the specification/design before coding the behavior.

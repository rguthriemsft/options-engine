# Phase 6 — Defense and Roll Design

## 1. Purpose

Phase 6 monitors an existing open short-call position, measures assignment/defense risk, evaluates profit-taking, and, when defense is warranted, evaluates deterministic replacement-call candidates.

The governing questions are:

> How urgently does this open short call require defensive action?

> Is buying back the existing call economically attractive because most of the opening premium has already been captured?

> If defense is required, is there an acceptable roll candidate that materially reduces assignment risk without violating approved roll constraints?

Phase 6 is analytical decision support only. It does not execute trades, create transactions, create campaigns, or perform campaign accounting.

---

## 2. Governing Principles

Phase 6 shall be:

- deterministic for fixed inputs, configuration, and strategy versions;
- provider-independent inside Strategy;
- explicit about missing or unavailable financial data;
- reproducible from persisted inputs and selected market observations;
- append-only for persisted evaluations;
- isolated from brokerage execution;
- isolated from Phase 7 campaign/transaction accounting;
- explicit about the distinction between severity scoring, hard triggers, candidate eligibility, candidate ranking, and final Phase 6 disposition.

Missing financial data is never converted to zero, neutral, a passing hard gate, or a fabricated score.

No formula, fallback, threshold, score band, freshness threshold, override, or execution assumption may be introduced beyond this approved design and the corresponding specification/acceptance documents.

---

## 3. Product-Concept Boundaries

The strategy concepts remain distinct:

```text
CCOS
    attractiveness of the underlying for selling calls now

Contract Score
    quality of an initial entry contract

Position Sizing
    how much of the Holding to cover

DRS
    urgency/risk of an existing short-call position

Profit Taking
    economic attractiveness of buying back the existing call

RQS
    quality of one specific replacement contract

DefenseDisposition
    Phase 6 analytical action state
```

Phase 6 does not create the final cross-phase `Recommendation` lifecycle.

---

## 4. Phase Boundary

### 4.1 Phase 6 Owns

Phase 6 owns:

```text
current-position defense evaluation
profit-taking calculation
Defense Risk Score
DRS classification
hard-defense triggers
hard-trigger aggregation
roll-engine activation
replacement-contract candidate generation
roll hard gates
roll economics
maximum roll debit
projected DRS
Roll Quality Score
deterministic roll ranking
Phase 6 defense disposition
immutable DefenseEvaluation
conditional immutable RollEvaluation
Phase 6 API
```

### 4.2 Phase 6 Does Not Own

Phase 6 does not own:

```text
automatic trade execution
brokerage synchronization
transaction ledger
campaign lifecycle
campaign P&L
cumulative campaign premium
fees
realized P&L
assignment-tax-dollar estimation
tax-lot disposal decisions
dividend/ex-dividend ingestion
technical-breakout defense logic
expected-move defense
scheduled/background monitoring
notifications
final Recommendation execution state
```

Daily monitoring in Phase 6 V1 means the evaluation capability is suitable for daily invocation. Phase 6 V1 does not introduce a background scheduler.

---

## 5. Open Short-Call Position Identity and Current State

Phase 5 introduced `OpenShortCallPositionEntity` with a stable database identity:

```text
OpenShortCallPositionId
```

Phase 6 uses that stable identity as the current-position identity.

One persisted open short-call position receives one DefenseEvaluation.

Multiple open short-call rows associated with the same Holding are evaluated independently. Holding-level exposure may be context, but DRS is not combined across open positions.

The minimal Phase 6 current-state position must expose:

```text
OpenShortCallPositionId
HoldingId
OptionSymbol
Contracts
Strike
Expiration
OpeningPremiumPerShare?
OpenedAtUtc?
```

`OpeningPremiumPerShare` is the authoritative gross executed STO credit per option share for the currently open position. It shall never silently fall back to the Phase 4 reference premium, Phase 4 Bid, current market price, or another estimate.

For several fills belonging to the same current open position, the stored opening premium is the weighted-average gross per-share opening credit. Phase 6 consumes that value; it does not reconstruct executions.

Imported positions may have unavailable opening premium.

This current-state model is not the Phase 7 transaction ledger.

---

## 6. Evaluation Time Semantics

Phase 6 uses one authoritative logical evaluation timestamp:

```text
DefenseEvaluationTimestampUtc
```

The corresponding calendar evaluation date is:

```text
DefenseEvaluationDate =
America/New_York calendar date corresponding
to DefenseEvaluationTimestampUtc
```

DTE is:

```text
DTE =
ExpirationDate - DefenseEvaluationDate
```

`CalculatedAtUtc` records when the immutable artifact was produced.

All persisted timestamps intended to be UTC must have zero UTC offset.

---

## 7. Market-Observation Selection

### 7.1 Existing Call

Application selects one complete normalized option observation for the existing `OptionSymbol` at or before `DefenseEvaluationTimestampUtc`.

Values consumed from that same observation may include:

```text
Bid
Ask
Last
OpenInterest
ImpliedVolatility
Delta
Gamma
Theta
Vega
UnderlyingPrice
ObservationTimestampUtc
Provider
```

Phase 6 shall not synthesize one current state by independently mixing Ask, Delta, and underlying price from different observations.

### 7.2 Replacement Universe

For each candidate expiration, Application selects the latest complete normalized option-chain observation whose timestamp is at or before `DefenseEvaluationTimestampUtc`.

Contracts from different timestamps must never be mixed within one expiration.

Different expirations may legitimately use different selected chain timestamps. The RollEvaluation preserves the selected timestamp for every expiration it consumed.

### 7.3 Freshness

Market-data freshness and refresh behavior remain Application/MarketData responsibilities.

Application may use the existing cache freshness policies to decide whether to refresh provider data for a current evaluation.

Strategy receives selected normalized observations and introduces no additional hidden Phase 6 age-based freshness threshold.

Historical deterministic evaluation uses the persisted observations selected at its supplied cutoff.

---

## 8. Profit Taking

Profit taking is independent from DRS.

### 8.1 Current BTC Reference Price

For the existing short call:

```text
CurrentBtcReferencePrice = Ask
```

No Mid, Last, or Bid fallback is used.

A required current Ask must be strictly positive for V1 profit-taking calculation. Missing or non-positive Ask produces unavailable profit-taking input.

### 8.2 Gross Premium Captured

```text
GrossOpeningPremium =
    OpeningPremiumPerShare
    * 100
    * Contracts

EstimatedCurrentBtcCost =
    CurrentBtcReferencePrice
    * 100
    * Contracts

GrossPremiumCaptured =
    GrossOpeningPremium
    - EstimatedCurrentBtcCost

GrossPremiumCapturedRatio =
    GrossPremiumCaptured
    / GrossOpeningPremium
```

Equivalent per-share formula:

```text
GrossPremiumCapturedRatio =
    (OpeningPremiumPerShare - CurrentBtcReferencePrice)
    / OpeningPremiumPerShare
```

The ratio is not clamped.

A current Ask above the opening premium therefore produces a valid negative captured ratio.

Fees are excluded in Phase 6 V1.

### 8.3 Profit-Taking Bands

```text
ratio < 0.50
    -> None

0.50 <= ratio < 0.70
    -> Monitor

0.70 <= ratio < 0.80
    -> CloseCandidate

ratio >= 0.80
    -> StrongCloseCandidate
```

An unavailable/invalid opening premium or current Ask produces `InsufficientData` for profit taking, not a zero ratio.

---

## 9. Defense Risk Score

DRS answers:

> How urgently does this existing short call require defensive action?

Range:

```text
0 <= DRS <= 100
Higher = greater risk
```

Phase 6 V1 uses exactly four components:

| Component | Maximum |
|---|---:|
| Delta | 40 |
| Strike proximity | 25 |
| DTE | 15 |
| Premium expansion | 20 |
| **Total** | **100** |

The previously proposed Momentum/Breakout, Expected Move, and Dividend/Event weighted components are deferred from V1.

DRS is all-or-nothing. If any required component is unavailable, top-level DRS is unavailable, while successfully calculated component results remain preserved.

---

## 10. Delta Defense Component — 40

Normalized call Delta consumed by Phase 6 must be finite and in the inclusive range:

```text
0 <= Delta <= 1
```

Malformed negative Delta is not repaired with `abs()`.

Scoring:

```text
Delta < .15             ->  0
.15 <= Delta < .20      ->  4
.20 <= Delta < .25      ->  9
.25 <= Delta < .30      -> 16
.30 <= Delta < .35      -> 24
.35 <= Delta < .40      -> 31
.40 <= Delta <= .50     -> 36
Delta > .50             -> 40
```

Delta velocity is not added to numeric DRS.

---

## 11. Strike-Proximity Component — 25

```text
StrikeDistanceRatio =
    (Strike - UnderlyingPrice)
    / UnderlyingPrice
```

Interpretation:

```text
positive -> OTM
zero     -> ATM
negative -> ITM
```

Requirements:

```text
UnderlyingPrice > 0
Strike > 0
```

Scoring:

```text
distance > 8%                ->  0
6% < distance <= 8%          ->  3
4% < distance <= 6%          ->  6
3% < distance <= 4%          -> 10
2% < distance <= 3%          -> 15
1% < distance <= 2%          -> 20
0% < distance <= 1%          -> 25
distance <= 0%               -> 25
```

---

## 12. DTE Component — 15

Using the New York `DefenseEvaluationDate`:

```text
DTE > 21     ->  0
15–21        ->  3
10–14        ->  6
7–9          ->  9
4–6          -> 12
1–3          -> 15
0            -> 15
DTE < 0      -> invalid open-position state
```

There is no additional DTE/Delta numeric bonus or penalty in V1.

---

## 13. Premium-Expansion Component — 20

```text
PremiumMultiple =
    CurrentBtcReferencePrice
    / OpeningPremiumPerShare
```

Scoring:

```text
PremiumMultiple < 0.50             ->  0
0.50 <= multiple < 1.00            ->  2
1.00 <= multiple < 1.25            ->  6
1.25 <= multiple < 1.50            -> 10
1.50 <= multiple < 2.00            -> 14
2.00 <= multiple < 3.00            -> 18
multiple >= 3.00                    -> 20
```

Premium expansion does not independently prescribe closure.

---

## 14. DRS Classification

```text
0  <= DRS < 20    SAFE
20 <= DRS < 35    NORMAL
35 <= DRS < 50    WATCH
50 <= DRS < 65    DEFEND
65 <= DRS < 80    HIGH_RISK
80 <= DRS <=100   CRITICAL
```

No classification exists when DRS is unavailable.

---

## 15. Delta Velocity

When a valid previous trading observation exists:

```text
DeltaVelocity =
    CurrentDelta
    - PreviousTradingDayDelta
```

The previous observation is the latest eligible observation for the same `OptionSymbol` from the immediately preceding trading date for which that contract has an observation.

Missing previous observation makes DeltaVelocity unavailable. It is not zero.

Delta velocity is a hard-trigger input only in V1.

---

## 16. Hard-Defense Triggers

Hard triggers are independent from numeric DRS.

Each V1 trigger has a structured result:

```text
TriggerCode
Status:
    Triggered
    NotTriggered
    InsufficientData
    NotApplicable

ObservedValues
Explanation
```

V1 trigger codes:

```text
HIGH_DELTA
STRIKE_PROXIMITY_WITH_DELTA
IN_THE_MONEY
LOW_DTE_WITH_DELTA
RAPID_DELTA_INCREASE
```

### 16.1 High Delta

```text
Delta >= .40
```

### 16.2 Strike Proximity with Delta

```text
0 <= StrikeDistanceRatio <= .01
AND
Delta >= .30
```

This rule applies to ATM/OTM proximity. ITM is separately represented by `IN_THE_MONEY`.

### 16.3 In the Money

```text
UnderlyingPrice > Strike
```

Strict equality is ATM, not ITM.

### 16.4 Low DTE with Delta

```text
0 <= DTE <= 3
AND
Delta >= .25
```

### 16.5 Rapid Delta Increase

```text
DeltaVelocity >= .15
```

### 16.6 Deferred Triggers

Deferred from Phase 6 V1:

```text
TECHNICAL_BREAKOUT
EARLY_ASSIGNMENT_RISK / dividend-event trigger
```

No dividend/ex-dividend source currently exists. ITM remains protected by its own V1 hard trigger.

### 16.7 Aggregate Hard-Defense Status

```text
if ANY trigger == Triggered
    -> Triggered

else if ALL applicable V1 triggers == NotTriggered
    -> Clear

else
    -> PartiallyEvaluated
```

A triggered condition wins even if another trigger is unavailable.

---

## 17. Roll-Engine Activation

Roll Engine evaluation occurs when:

```text
HardDefenseStatus == Triggered
OR
DRS >= 50
```

Profit taking alone does not activate the Roll Engine.

If:

```text
DRS unavailable
AND HardDefenseStatus == Triggered
```

the Roll Engine still runs.

If DRS is unavailable and hard-defense status is partially evaluated, the defensive disposition becomes `DEFENSE_REVIEW`.

Current CCOS affects the eventual disposition after roll candidates are evaluated. It does not determine whether candidate analysis runs.

---

## 18. Replacement Candidate Universe

Phase 6 V1 evaluates call replacements only.

### 18.1 Expiration

```text
21 <= NewDTE <= 60
NewExpiration > ExistingExpiration
```

Preference:

```text
21–45 DTE -> PreferredWindow
46–60 DTE -> ExtendedWindow
```

### 18.2 Delta

```text
Preferred Delta: .12–.18
Normal hard maximum: .25
TaxSensitivity.High hard maximum: .20
```

There is no minimum hard Delta.

Candidate Delta must be finite and within `[0,1]`.

A defensive replacement must also satisfy:

```text
NewDelta < CurrentDelta
```

### 18.3 Strike Improvement and Strict OTM

V1 hard gates require:

```text
NewStrike > ExistingStrike
AND
NewStrike > CurrentUnderlyingPrice
```

Equivalently:

```text
NewStrike >
    max(ExistingStrike, CurrentUnderlyingPrice)
```

Equality fails.

### 18.4 Liquidity

Phase 6 reuses the already-approved Phase 4 liquidity gate:

```text
Ask > Bid
OpenInterest >= 100
BidAskSpreadPercent <= 20%
```

Missing Bid, Ask, or OpenInterest is unavailable data, not a liquidity failure.

Daily option volume is not a V1 hard-gate input.

Phase 6 does not import Phase 4 entry eligibility wholesale.

### 18.5 Earnings

For a stock with a known next earnings date, reject a candidate only when it introduces a new crossing:

```text
ExistingExpiration < EarningsDate <= NewExpiration
```

If the existing call already spans the known earnings date, the replacement is not rejected merely for continuing to span that same event.

For an individual stock, missing required earnings data makes candidate evaluation insufficient.

For an ETF, earnings is `NotApplicable` and does not block the candidate.

---

## 19. Roll Economics

Phase 6 V1 preserves contract count:

```text
ReplacementContracts = ExistingContracts
```

No defensive resizing occurs.

Pricing:

```text
ExistingBtcPerShare = ExistingCallAsk
ReplacementStoPerShare = CandidateBid

NetRollPerShare =
    ReplacementStoPerShare
    - ExistingBtcPerShare

NetRollTotal =
    NetRollPerShare
    * 100
    * ExistingContracts
```

Positive is a credit; negative is a debit.

No Mid or Last fallback is used.

---

## 20. Maximum Roll Debit

V1 uses one absolute configurable hard gate:

```text
MaximumRollDebitPerShare >= 0
```

Candidate eligibility:

```text
NetRollPerShare >= -MaximumRollDebitPerShare
```

`MaximumRollDebitPerShare = 0` is valid and means only even-or-credit rolls are allowed.

Campaign-income debit limits and tax-dollar exceptions are deferred to Phase 7 or later because Phase 6 has no authoritative campaign accounting or assignment-tax-dollar model.

No V1 override may exceed the configured absolute debit limit.

---

## 21. Projected DRS

Every otherwise evaluable candidate receives projected DRS using the same DRS algorithm and the current defense context with replacement-contract terms substituted.

At the hypothetical instant of opening the replacement:

```text
ProjectedOpeningPremium = CandidateBid
ProjectedCurrentPrice = CandidateBid
ProjectedPremiumMultiple = 1.0
```

Eligibility requires:

```text
ProjectedDRS < CurrentDRS
AND
ProjectedDRS < 40
```

If required projected-DRS inputs are unavailable, candidate evaluation is insufficient rather than failed.

---

## 22. Roll Quality Score

RQS ranks eligible defensive replacement candidates.

Range:

```text
0 <= RQS <= 100
Higher = better
```

Weights:

| Component | Maximum |
|---|---:|
| DRS reduction | 30 |
| Delta reduction | 20 |
| Strike improvement | 20 |
| Roll economics | 15 |
| Replacement liquidity | 10 |
| Time efficiency | 5 |
| **Total** | **100** |

RQS requires every component. Missing any required component makes RQS unavailable.

### 22.1 DRS Reduction — 30

```text
DRSReduction =
    CurrentDRS - ProjectedDRS

DRSReductionRatio =
    max(DRSReduction, 0)
    / CurrentDRS

DrsReductionScore =
    30 * DRSReductionRatio
```

A positive current DRS is required for an RQS.

### 22.2 Delta Reduction — 20

```text
DeltaReduction =
    CurrentDelta - NewDelta

DeltaReductionRatio =
    max(DeltaReduction, 0)
    / CurrentDelta

DeltaReductionScore =
    20 * DeltaReductionRatio
```

A positive current Delta is required for this ratio.

### 22.3 Strike Improvement — 20

```text
StrikeImprovement =
    NewStrike - ExistingStrike

StrikeImprovementRatio =
    StrikeImprovement / ExistingStrike

NormalizedStrikeImprovement =
    min(
        StrikeImprovementRatio
        / FullStrikeImprovementRatio,
        1)

StrikeImprovementScore =
    20 * NormalizedStrikeImprovement
```

Default approved configuration intent:

```text
FullStrikeImprovementRatio = 0.10
```

The value is configuration-backed.

### 22.4 Roll Economics — 15

For a debit:

```text
RollDebitPerShare =
    max(-NetRollPerShare, 0)

DebitUtilization =
    RollDebitPerShare
    / MaximumRollDebitPerShare

EconomicsScore =
    10 * (1 - DebitUtilization)
```

For `MaximumRollDebitPerShare = 0`, debit candidates fail the hard gate and no division occurs.

An even roll receives:

```text
EconomicsScore = 10
```

For a credit:

```text
CreditRatio =
    NetRollPerShare
    / ReplacementStoPerShare

EconomicsScore =
    10
    + 5 * min(
        CreditRatio
        / FullCreditEconomicsRatio,
        1)
```

Default approved configuration intent:

```text
FullCreditEconomicsRatio = 0.25
```

The value is configuration-backed.

### 22.5 Replacement Liquidity — 10

Reuse the existing Phase 4 Liquidity Score unchanged:

```text
LiquidityScore =
    SpreadScore
    + OpenInterestScore
```

The Phase 4 liquidity score already has a maximum of 10, so:

```text
ReplacementLiquidityScore =
    LiquidityScore
```

No Phase 4 Contract Score or entry acceptability is imported.

### 22.6 Time Efficiency — 5

For eligible `21 <= NewDTE <= 60`:

```text
TimeEfficiencyRatio =
    (60 - NewDTE)
    / (60 - 21)

TimeEfficiencyScore =
    5 * TimeEfficiencyRatio
```

This rewards achieving the defensive objective with less time extension.

---

## 23. Candidate Evaluation State

Every evaluated replacement candidate is retained, including rejected or insufficient candidates.

Candidate state distinguishes:

```text
Rejected
    one or more known hard gates failed

Rankable
    every hard gate passed
    and complete RQS exists

InsufficientData
    eligibility or RQS could not be established
```

A known hard-gate failure is not the same as unavailable data.

Rejected candidates may preserve calculable metrics and score components but receive no rank.

---

## 24. Candidate Ranking

Only Rankable candidates are ranked.

Ordering:

```text
1. RQS descending
2. ProjectedDRS ascending
3. NewDelta ascending
4. NewStrike descending
5. NetRollPerShare descending
6. NewExpiration ascending
7. OptionSymbol ordinal ascending
```

There is no minimum RQS threshold in V1. Hard gates determine acceptability; RQS orders acceptable candidates.

Rank 1 is the preferred roll candidate.

---

## 25. Phase 6 Defense Disposition

Phase 6 owns this analytical disposition vocabulary:

```text
NO_ACTION
MONITOR
PROFIT_CLOSE
DEFENSE_REVIEW
ROLL
CLOSE_WAIT
```

These values are not the final cross-phase `RecommendationType`.

### 25.1 No Defensive Activation

When the Roll Engine is not required:

```text
StrongCloseCandidate -> PROFIT_CLOSE
CloseCandidate       -> PROFIT_CLOSE
Monitor              -> MONITOR
None                 -> NO_ACTION
```

### 25.2 Defensive Activation

Known defensive state takes precedence over ordinary profit-taking classification.

If at least one Rankable candidate exists:

```text
CurrentCCOS >= 55
    -> ROLL

CurrentCCOS < 55
    -> CLOSE_WAIT

CurrentCCOS unavailable
    -> DEFENSE_REVIEW
```

The preferred roll is the Rank 1 candidate.

### 25.3 No Rankable Candidate

If no candidate is Rankable and at least one candidate is `InsufficientData`:

```text
-> DEFENSE_REVIEW
```

If every candidate was conclusively rejected:

```text
HardDefenseStatus == Triggered
    -> DEFENSE_REVIEW

HardDefenseStatus != Triggered
AND DRS >= 50
    -> CLOSE_WAIT
```

### 25.4 Partially Evaluated Hard Defense

A partially evaluated hard-defense state must never silently become `NO_ACTION`.

If hard-defense status is `PartiallyEvaluated` and DRS does not independently activate the Roll Engine:

```text
-> DEFENSE_REVIEW
```

---

## 26. Current CCOS

When defensive candidate analysis is required, Application resolves current CCOS using the existing Phase 4 CCOS calculation semantics and current approved inputs/configuration.

Current CCOS is decision context; it is not used to suppress candidate analysis.

The final Phase 6 threshold is:

```text
CurrentCCOS >= 55
    replacement environment acceptable for ROLL
```

No separate behavior difference is introduced between 55–69 and 70+ beyond explanation/context.

Unavailable current CCOS produces `DEFENSE_REVIEW` when a defensive disposition requires that choice.

---

## 27. Immutable Evaluation Contracts

### 27.1 DefenseEvaluation

Conceptually:

```text
DefenseEvaluation
    DefenseEvaluationId
    OpenShortCallPositionId
    HoldingId
    Symbol
    OptionSymbol

    DefenseEvaluationTimestampUtc
    CalculatedAtUtc

    PositionSnapshot
    HoldingContext
    CurrentOptionObservation
    PreviousDeltaObservation?
    CurrentCCOSContext?
    EarningsContext

    ResolvedDefenseConfiguration
    ConfigurationVersion
    DefenseStrategyVersion

    ProfitTakingResult
    DRSResult
    HardTriggerResults
    HardDefenseStatus

    RollEngineRequired
    RollEvaluationId?
    Disposition

    MissingInputs
    Explanations
```

DefenseEvaluation is append-only and immutable.

### 27.2 RollEvaluation

Created only when the Roll Engine runs:

```text
RollEvaluation
    RollEvaluationId
    DefenseEvaluationId

    DefenseEvaluationTimestampUtc
    CalculatedAtUtc

    CurrentPositionSnapshot
    CurrentCCOS
    ExistingBtcPerShare

    SelectedChainSnapshots[]
    Candidates[]

    PreferredOptionSymbol?
    PreferredStrike?
    PreferredExpiration?
    PreferredRQS?

    ResolvedRollConfiguration
    ConfigurationVersion
    RollStrategyVersion

    MissingInputs
    Explanations
```

No empty RollEvaluation is created when the Roll Engine does not run.

---

## 28. Persistence

Use the established relational-summary plus immutable JSON-payload pattern.

Suggested DefenseEvaluation relational summary:

```text
DefenseEvaluationId
OpenShortCallPositionId
HoldingId
Symbol
OptionSymbol
DefenseEvaluationTimestampUtc
CalculatedAtUtc
DRS
DRSClassification
HardDefenseStatus
ProfitTakingSignal
Disposition
RollEvaluationId?
ConfigurationVersion
DefenseStrategyVersion
EvaluationJson
```

Suggested RollEvaluation relational summary:

```text
RollEvaluationId
DefenseEvaluationId
DefenseEvaluationTimestampUtc
CalculatedAtUtc
CurrentCCOS
EligibleCandidateCount
InsufficientCandidateCount
PreferredOptionSymbol?
PreferredStrike?
PreferredExpiration?
PreferredRQS?
ConfigurationVersion
RollStrategyVersion
EvaluationJson
```

Historical evaluation retrieval never recalculates current data.

Updating current open-position state never rewrites old DefenseEvaluation or RollEvaluation records.

---

## 29. Configuration and Versioning

Phase 6 uses strongly typed, startup/load-time validated configuration.

At minimum the resolved configuration must cover:

```text
profit-taking thresholds
DRS score bands
DRS classification bands
hard-trigger thresholds
roll activation threshold
candidate DTE window
preferred DTE window
preferred Delta window
normal/high-tax maximum Delta
liquidity thresholds / scoring reused from Phase 4
projected DRS maximum
FullStrikeImprovementRatio
MaximumRollDebitPerShare
FullCreditEconomicsRatio
CurrentCCOS roll threshold
```

The system continues to persist the shared `ConfigurationVersion` and complete resolved values consumed by the evaluation.

Algorithm identities are distinct:

```text
DefenseStrategyVersion
RollStrategyVersion
```

Changing formulas, component composition, hard-gate meaning, missing-data policy, ranking order, or decision flow requires the applicable strategy-version change.

---

## 30. Application Orchestration

Application owns:

```text
Holding/current-position lookup
current-state validation
market-data selection
freshness/refresh behavior
previous-trading-observation selection
earnings context resolution
current indicator/CCOS context
configuration resolution
strategy-version resolution
Strategy invocation
immutable persistence
```

Strategy remains pure and must not access:

```text
Tradier/provider DTOs
HTTP
EF Core
SQLite
ASP.NET
current wall-clock time
```

---

## 31. API

Phase 6 V1 conceptually exposes:

```http
POST /api/holdings/{holdingId}/positions/{positionId}/defense-evaluations

GET /api/defense-evaluations/{defenseEvaluationId}

GET /api/holdings/{holdingId}/positions/{positionId}/defense-evaluations

GET /api/roll-evaluations/{rollEvaluationId}
```

There is no independent RollEvaluation POST. Roll evaluation is a consequence of DefenseEvaluation.

POST creates an immutable analytical result only.

Expected current-state errors:

```text
Holding missing       -> 404
Position missing      -> 404
Holding disabled      -> 409 HOLDING_DISABLED
Expired open position -> invalid current-position request
```

Historical evaluations remain readable after a Holding is disabled.

No Phase 6 PUT/PATCH/DELETE semantics exist for immutable evaluations.

---

## 32. No Execution Side Effects

A Phase 6 evaluation never:

```text
buys to close
sells a replacement call
updates contract count because of a hypothetical roll
creates a transaction
marks a recommendation executed
creates/updates a campaign
calculates realized campaign P&L
```

`Disposition = ROLL` plus a preferred candidate remains decision support only.

---

## 33. Explicit V1 Deferrals

The following remain outside Phase 6 V1:

```text
Momentum/Breakout weighted DRS component
Expected Move DRS component
Dividend/Event weighted DRS component
technical-breakout hard trigger
early-assignment dividend trigger
scheduled background monitoring
notifications
same-expiration roll-up
roll-down
replacement sizing changes
campaign-relative debit limits
tax-dollar debit overrides
fees/net campaign P&L
campaign premium after roll
final Recommendation lifecycle
automatic execution
```

---

## 34. Implementation Packet Boundary

The approved implementation sequence is:

```text
6A — Defense foundations and current-position contract
6B — Profit taking, DRS, and hard triggers
6C — Roll candidate universe, hard gates, and economics
6D — RQS, ranking, and Phase 6 disposition
6E — Application orchestration and market-data/current-context assembly
6F — Immutable persistence
6G — API and merge-gate validation
```

No packet may introduce a new formula, threshold, fallback, override, or Phase 7 accounting behavior beyond the approved design.

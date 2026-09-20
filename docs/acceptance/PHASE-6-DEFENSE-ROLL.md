# Phase 6 — Defense and Roll Acceptance Checklist

## Objective

Implement deterministic defense and roll analysis for one persisted open short-call position while preserving the Phase 7 boundary.

Phase 6 shall provide:

```text
profit-taking evaluation
Defense Risk Score
DRS classification
hard-defense triggers
roll-engine activation
replacement candidate analysis
roll hard gates
roll economics
Roll Quality Score
deterministic candidate ranking
Phase 6 DefenseDisposition
immutable DefenseEvaluation persistence
conditional immutable RollEvaluation persistence
Phase 6 API
```

`SPECIFICATION.md` is authoritative for product/strategy behavior.

The detailed approved design is:

```text
docs/design/PHASE-6-DEFENSE-ROLL-DESIGN.md
```

Phase 6 shall not implement automatic execution, transaction/campaign accounting, campaign-relative debit rules, assignment-tax-dollar overrides, dividend/event logic, technical-breakout defense logic, scheduled background monitoring, notifications, or the final Recommendation lifecycle.

---

# 1. Governing Architecture

```text
Holding
OpenShortCallPosition
Current option observation
Previous Delta observation
Current indicators / CCOS context
Earnings context
Resolved configuration
        |
        v
Pure Defense Strategy
  Profit Taking
  DRS
  Hard Triggers
        |
        +---- no defense activation
        |
        +---- defense activation
                    |
                    v
               Pure Roll Strategy
               candidate gates
               economics
               projected DRS
               RQS / ranking
        |
        v
DefenseDisposition
        |
        v
immutable persistence / API
```

Acceptance criteria:

- [ ] Strategy is deterministic for fixed inputs, configuration, and strategy versions.
- [ ] Strategy is provider-independent.
- [ ] Strategy does not fetch provider data.
- [ ] Application owns current-state, market-data, and persistence orchestration.
- [ ] Phase 4/5 immutable evaluations are never modified by Phase 6.
- [ ] No Phase 7 ledger/campaign model is introduced.
- [ ] No automatic trade execution is introduced.

---

# 2. Phase 6A — Foundations and Current-Position Contract

Implement provider-independent contracts for at least:

```text
DefenseStrategyVersion
RollStrategyVersion
DefenseConfiguration
RollConfiguration
DefenseEvaluationInput
DefenseEvaluation
DefenseDisposition
ProfitTakingResult
DrsResult
HardTriggerResult
HardDefenseStatus
RollEvaluation
RollCandidateEvaluation
candidate evaluation state
RQS component results
stable reason/missing-input codes
```

Extend the narrow current open-short-call state with:

```text
OpenShortCallPositionId
OpeningPremiumPerShare?
OpenedAtUtc?
```

Acceptance criteria:

- [x] `OpenShortCallPositionId` is carried across Infrastructure/Application/Strategy boundaries.
- [x] One open-position row is the unit of DefenseEvaluation.
- [x] Several positions on one Holding remain separate defense evaluations.
- [x] Opening premium is authoritative gross executed per-share STO credit when known.
- [x] Opening premium never silently falls back to Phase 4 Bid/reference premium.
- [x] Weighted-average opening credit may represent multiple fills in the same current position.
- [x] Unknown imported opening premium remains unavailable.
- [x] Current open-position state remains mutable; historical evaluations are immutable.
- [x] Strongly typed configuration validates at startup/load time.
- [x] No numeric sentinel represents unavailable financial data.

---

# 3. Time and Observation Semantics

```text
DefenseEvaluationDate =
America/New_York calendar date corresponding
to DefenseEvaluationTimestampUtc
```

Acceptance criteria:

- [x] DTE uses the New York evaluation date.
- [ ] `CalculatedAtUtc` is distinct from the logical evaluation timestamp.
- [x] Current existing-call Ask, Delta, and underlying price come from one selected normalized option observation.
- [x] Strategy never synthesizes an observation by mixing timestamps.
- [ ] For each candidate expiration, the latest complete chain at/before the evaluation cutoff is selected.
- [ ] Contracts from different timestamps are never mixed within one expiration.
- [ ] Different expirations may use different selected chain timestamps.
- [ ] Every selected observation/timestamp used for reproducibility is preserved.
- [ ] Cache freshness/refresh remains Application/MarketData behavior.
- [ ] No hidden Phase 6 strategy freshness threshold exists.

---

# 4. Phase 6B — Profit Taking

```text
CurrentBtcReferencePrice = Ask

GrossPremiumCapturedRatio =
    (OpeningPremiumPerShare - Ask)
    / OpeningPremiumPerShare
```

Bands:

```text
<50%          None
50%–<70%      Monitor
70%–<80%      CloseCandidate
>=80%         StrongCloseCandidate
```

Acceptance criteria:

- [x] Ask, not Mid/Last/Bid, is the BTC reference price.
- [x] Missing/non-positive opening premium produces InsufficientData.
- [x] Missing/non-positive current Ask produces InsufficientData.
- [x] Captured ratio is not clamped.
- [x] A current Ask above opening premium produces a valid negative captured ratio.
- [x] Fees are excluded from Phase 6 V1.
- [x] 50%, 70%, and 80% have below/exact/above boundary tests.
- [x] Profit-taking result remains independent from DRS.

---

# 5. DRS Composition

V1 DRS:

```text
Delta                40
Strike Proximity     25
DTE                  15
Premium Expansion    20
                    ---
                    100
```

Acceptance criteria:

- [x] Momentum/Breakout is not a V1 weighted component.
- [x] Expected Move is not a V1 weighted component.
- [x] Dividend/Event is not a V1 weighted component.
- [x] No missing component is replaced with zero.
- [x] No score reweighting occurs around missing components.
- [x] Any required missing component makes top-level DRS unavailable.
- [x] Available component results remain preserved when top-level DRS is unavailable.

---

# 6. Delta Component

```text
Delta < .15             0
.15 <= Delta < .20      4
.20 <= Delta < .25      9
.25 <= Delta < .30     16
.30 <= Delta < .35     24
.35 <= Delta < .40     31
.40 <= Delta <= .50    36
Delta > .50            40
```

Acceptance criteria:

- [x] Delta must be finite and within `[0,1]`.
- [x] Negative Delta is invalid/unavailable.
- [x] Strategy never repairs malformed Delta with `abs()`.
- [x] Every boundary has below/exact/above tests.

---

# 7. Strike-Proximity Component

```text
StrikeDistanceRatio =
    (Strike - UnderlyingPrice)
    / UnderlyingPrice
```

Scores:

```text
>8%                0
>6% <=8%           3
>4% <=6%           6
>3% <=4%          10
>2% <=3%          15
>1% <=2%          20
>0% <=1%          25
<=0%              25
```

Acceptance criteria:

- [x] UnderlyingPrice and Strike must be positive.
- [x] Positive distance is OTM; zero ATM; negative ITM.
- [x] Every boundary has deterministic below/exact/above coverage.

---

# 8. DTE Component

```text
DTE >21      0
15–21        3
10–14        6
7–9          9
4–6         12
1–3         15
0           15
DTE <0      invalid open-position state
```

Acceptance criteria:

- [x] DTE uses the New York defense evaluation date.
- [x] No undefined DTE/Delta numeric penalty exists.
- [x] All DTE boundaries are tested.
- [x] Negative DTE does not generate a normal DRS.

---

# 9. Premium Expansion

```text
PremiumMultiple =
    CurrentBtcReferencePrice
    / OpeningPremiumPerShare
```

Scores:

```text
<.50          0
.50–<1.00     2
1.00–<1.25    6
1.25–<1.50   10
1.50–<2.00   14
2.00–<3.00   18
>=3.00       20
```

Acceptance criteria:

- [x] Exact boundaries are non-overlapping.
- [x] Missing opening premium or Ask makes the component unavailable.
- [x] Premium expansion alone does not prescribe closure.

---

# 10. DRS Classification

```text
0–<20      SAFE
20–<35     NORMAL
35–<50     WATCH
50–<65     DEFEND
65–<80     HIGH_RISK
80–100     CRITICAL
```

Acceptance criteria:

- [x] 20 enters NORMAL.
- [x] 35 enters WATCH.
- [x] 50 enters DEFEND.
- [x] 65 enters HIGH_RISK.
- [x] 80 enters CRITICAL.
- [x] No classification exists for unavailable DRS.

---

# 11. Hard-Defense Triggers

V1:

```text
HIGH_DELTA
    Delta >= .40

STRIKE_PROXIMITY_WITH_DELTA
    0 <= StrikeDistanceRatio <= .01
    AND Delta >= .30

IN_THE_MONEY
    UnderlyingPrice > Strike

LOW_DTE_WITH_DELTA
    0 <= DTE <= 3
    AND Delta >= .25

RAPID_DELTA_INCREASE
    DeltaVelocity >= .15
```

Acceptance criteria:

- [x] Each trigger is represented independently.
- [x] Trigger status distinguishes Triggered, NotTriggered, InsufficientData, NotApplicable.
- [x] Exact .40 Delta triggers.
- [x] Exact 1% proximity and .30 Delta trigger.
- [x] Strike equality is ATM, not ITM.
- [x] Exact 3 DTE and .25 Delta trigger.
- [x] Exact .15 Delta velocity triggers.
- [x] Missing previous Delta makes velocity unavailable, not zero.
- [x] Technical breakout hard trigger is deferred.
- [x] Dividend/early-assignment hard trigger is deferred.

Aggregate:

```text
any Triggered              -> Triggered
all applicable NotTriggered -> Clear
otherwise                   -> PartiallyEvaluated
```

Acceptance criteria:

- [x] A known trigger wins over another unavailable trigger.
- [x] PartiallyEvaluated never silently becomes Clear.

---

# 12. Roll Activation

```text
HardDefenseStatus == Triggered
OR
DRS >= 50
```

Acceptance criteria:

- [x] Profit-taking alone does not activate Roll Engine.
- [x] Hard trigger activates Roll Engine even if DRS is unavailable.
- [x] DRS <50 with fully clear hard triggers does not activate Roll Engine.
- [ ] Partially evaluated hard triggers cannot silently produce NO_ACTION.
- [x] CCOS does not suppress candidate analysis.

---

# 13. Phase 6C — Roll Candidate Universe and Hard Gates

Candidate universe:

```text
Call only
21 <= NewDTE <= 60
NewExpiration > ExistingExpiration
NewStrike > ExistingStrike
NewStrike > CurrentUnderlyingPrice
NewDelta < CurrentDelta
NewDelta <= .25
TaxSensitivity.High -> NewDelta <= .20
```

Acceptance criteria:

- [ ] 21 and 60 DTE are included.
- [ ] 20 and 61 DTE reject.
- [ ] 21–45 is recorded as preferred window; 46–60 extended window.
- [ ] Same expiration rejects.
- [ ] Higher expiration is required.
- [ ] Same/lower strike rejects.
- [ ] ATM/ITM replacement rejects.
- [ ] Same/higher Delta rejects.
- [ ] Exact .25 normal Delta passes.
- [ ] Above .25 rejects.
- [ ] Exact .20 high-tax Delta passes.
- [ ] Above .20 high-tax rejects.
- [ ] There is no minimum hard Delta.

---

# 14. Roll Liquidity

Reuse Phase 4:

```text
Ask > Bid
OpenInterest >= 100
BidAskSpreadPercent <= 20%
```

Acceptance criteria:

- [ ] Ask equal to Bid rejects.
- [ ] OI 100 passes.
- [ ] OI 99 rejects.
- [ ] Spread exactly 20% passes.
- [ ] Spread above 20% rejects.
- [ ] Missing Bid/Ask/OI produces InsufficientData rather than hard-gate failure.
- [ ] Daily option volume is not a V1 hard-gate input.
- [ ] Phase 4 entry eligibility is not imported wholesale.

---

# 15. Earnings Roll Gate

```text
ExistingExpiration < EarningsDate <= NewExpiration
    -> reject
```

Acceptance criteria:

- [ ] A candidate that introduces a new known earnings crossing rejects.
- [ ] Earnings exactly on NewExpiration rejects.
- [ ] An event already inside ExistingExpiration does not newly reject the replacement.
- [ ] Missing required stock earnings context produces candidate InsufficientData.
- [ ] ETF earnings is NotApplicable and does not block a candidate.

---

# 16. Roll Economics

```text
ReplacementContracts = ExistingContracts

ExistingBtcPerShare = ExistingCallAsk
ReplacementStoPerShare = CandidateBid

NetRollPerShare =
    CandidateBid - ExistingCallAsk

NetRollTotal =
    NetRollPerShare * 100 * ExistingContracts
```

Acceptance criteria:

- [ ] Same contract count is preserved.
- [ ] Phase 6 never performs defensive resizing.
- [ ] BTC uses Ask.
- [ ] STO uses Bid.
- [ ] Mid/Last are never hidden fallbacks.
- [ ] Positive NetRoll is credit.
- [ ] Negative NetRoll is debit.

---

# 17. Maximum Roll Debit

```text
MaximumRollDebitPerShare >= 0

NetRollPerShare >= -MaximumRollDebitPerShare
```

Acceptance criteria:

- [ ] Exact maximum debit passes.
- [ ] One increment beyond maximum rejects.
- [ ] Zero maximum debit is valid and means even/credit only.
- [ ] Negative configuration is invalid.
- [ ] No campaign-income debit gate exists in Phase 6 V1.
- [ ] No tax-dollar override exists in Phase 6 V1.
- [ ] No override can exceed the absolute V1 maximum.

---

# 18. Projected DRS

Acceptance criteria:

- [ ] Projected DRS uses the same DRS engine.
- [ ] Replacement opening/current price is CandidateBid for the hypothetical initial projected state.
- [ ] Projected premium multiple therefore begins at 1.0.
- [ ] Candidate requires ProjectedDRS < CurrentDRS.
- [ ] Candidate requires ProjectedDRS < 40.
- [ ] Exact 40 rejects.
- [ ] Missing projected-DRS input produces candidate InsufficientData.

---

# 19. Phase 6D — RQS

Weights:

```text
DRS Reduction          30
Delta Reduction        20
Strike Improvement     20
Roll Economics         15
Replacement Liquidity  10
Time Efficiency         5
                      ---
                      100
```

Acceptance criteria:

- [ ] No Phase 4 Contract Score is required for RQS.
- [ ] Replacement Liquidity reuses the existing Phase 4 0–10 Liquidity Score.
- [ ] All RQS components are required.
- [ ] Missing one component makes RQS unavailable.
- [ ] No reweighting occurs around missing components.

---

# 20. RQS Component Mathematics

DRS reduction:

```text
30 * max(CurrentDRS - ProjectedDRS, 0) / CurrentDRS
```

Delta reduction:

```text
20 * max(CurrentDelta - NewDelta, 0) / CurrentDelta
```

Strike improvement:

```text
20 * min(
    ((NewStrike - ExistingStrike) / ExistingStrike)
    / FullStrikeImprovementRatio,
    1)
```

Debit economics:

```text
10 * (1 - DebitUtilization)
```

Even roll:

```text
10
```

Credit economics:

```text
10
+ 5 * min(
    CreditRatio / FullCreditEconomicsRatio,
    1)
```

Time efficiency:

```text
5 * (60 - NewDTE) / 39
```

Acceptance criteria:

- [ ] Each ratio validates its denominator.
- [ ] FullStrikeImprovementRatio is positive configuration.
- [ ] Default approved value is 0.10.
- [ ] FullCreditEconomicsRatio is positive configuration.
- [ ] Default approved value is 0.25.
- [ ] Debit-utilization scoring reaches 0 at the exact debit limit.
- [ ] Even roll scores 10 economics points.
- [ ] Credit score saturates at 15.
- [ ] Time efficiency is 5 at 21 DTE.
- [ ] Time efficiency is 0 at 60 DTE.
- [ ] RQS remains within [0,100].

---

# 21. Candidate State and Ranking

Candidate state:

```text
Rejected
Rankable
InsufficientData
```

Acceptance criteria:

- [ ] Known gate failure produces Rejected.
- [ ] Unknown required gate input produces InsufficientData, not Rejected.
- [ ] Complete eligible candidate with complete RQS is Rankable.
- [ ] Rejected candidates are retained with reasons.
- [ ] Insufficient candidates are retained with missing inputs.
- [ ] Only Rankable candidates receive Rank.

Ranking:

```text
RQS DESC
ProjectedDRS ASC
NewDelta ASC
NewStrike DESC
NetRollPerShare DESC
NewExpiration ASC
OptionSymbol ordinal ASC
```

Acceptance criteria:

- [ ] Every tie-break level is deterministic.
- [ ] Final symbol tie-break is ordinal.
- [ ] Rank 1 is the preferred roll candidate.
- [ ] No minimum RQS threshold exists.

---

# 22. Phase 6 Disposition

Vocabulary:

```text
NO_ACTION
MONITOR
PROFIT_CLOSE
DEFENSE_REVIEW
ROLL
CLOSE_WAIT
```

Acceptance criteria:

- [ ] These are Phase 6 analytical dispositions, not final Recommendation lifecycle values.
- [ ] No-defense StrongCloseCandidate -> PROFIT_CLOSE.
- [ ] No-defense CloseCandidate -> PROFIT_CLOSE.
- [ ] No-defense Monitor -> MONITOR.
- [ ] No-defense None -> NO_ACTION.
- [ ] Defensive activation takes precedence over ordinary profit-taking state.
- [ ] Rankable candidate + CurrentCCOS >=55 -> ROLL.
- [ ] Rankable candidate + CurrentCCOS <55 -> CLOSE_WAIT.
- [ ] Rankable candidate + unavailable CurrentCCOS -> DEFENSE_REVIEW.
- [ ] No Rankable candidate + any candidate InsufficientData -> DEFENSE_REVIEW.
- [ ] All candidates rejected + hard trigger -> DEFENSE_REVIEW.
- [ ] All candidates rejected + DRS-only activation -> CLOSE_WAIT.
- [ ] Partially evaluated hard-defense state never silently resolves to NO_ACTION.

---

# 23. Current CCOS

Acceptance criteria:

- [ ] Current CCOS uses existing Phase 4 CCOS semantics.
- [ ] Application resolves current approved indicator/context inputs.
- [ ] Current CCOS is not reused from an unrelated historical Phase 4 evaluation.
- [ ] CCOS does not decide whether candidate analysis runs.
- [ ] Exact 55 enters the ROLL side when a Rankable candidate exists.
- [ ] Unavailable CCOS preserves explicit missing data.

---

# 24. Phase 6E — Application Orchestration

Application assembles:

```text
Holding snapshot
OpenShortCallPosition snapshot
current existing-call option observation
previous trading Delta observation
current indicator/CCOS context
earnings context
selected replacement chains per expiration
resolved Defense/Roll configuration
ConfigurationVersion
DefenseStrategyVersion
RollStrategyVersion
DefenseEvaluationTimestampUtc
```

Acceptance criteria:

- [ ] Application, not Strategy, accesses repositories/providers.
- [ ] Current state is snapshotted into the evaluation.
- [ ] Selected market observations are reproducible.
- [ ] Current CCOS context is resolved only when required by the disposition path.
- [ ] No provider DTO reaches Strategy.
- [ ] No strategy calculation reads wall-clock time directly.
- [ ] Phase 6 introduces no scheduler/background service requirement.
- [ ] No trade side effects occur.

---

# 25. Phase 6F — Immutable Persistence

Persist append-only:

```text
DefenseEvaluation
RollEvaluation when activated
```

Acceptance criteria:

- [ ] DefenseEvaluation has a permanent Guid ID.
- [ ] RollEvaluation has a permanent Guid ID.
- [ ] RollEvaluation references exactly one DefenseEvaluation.
- [ ] DefenseEvaluation references exactly one current open-position identity.
- [ ] No empty RollEvaluation is persisted when Roll Engine does not run.
- [ ] Complete structured payload preserves all consumed inputs/results/configuration/version data.
- [ ] Rejected and insufficient roll candidates remain persisted.
- [ ] Relational summaries support efficient history/read queries.
- [ ] Historical retrieval never recalculates current data.
- [ ] Mutating Holding, current position, market data, or configuration cannot change historical payloads.
- [ ] Schema changes use EF Core migrations.
- [ ] Existing migrations remain unchanged.

---

# 26. Phase 6G — API

Conceptual V1 routes:

```http
POST /api/holdings/{holdingId}/positions/{positionId}/defense-evaluations
GET  /api/defense-evaluations/{defenseEvaluationId}
GET  /api/holdings/{holdingId}/positions/{positionId}/defense-evaluations
GET  /api/roll-evaluations/{rollEvaluationId}
```

Acceptance criteria:

- [ ] POST creates one immutable DefenseEvaluation and returns 201 on successful analytical outcomes.
- [ ] RollEvaluation is created only as part of DefenseEvaluation orchestration.
- [ ] There is no independent public Roll POST.
- [ ] GET by ID is passive historical retrieval.
- [ ] Position history is newest first and lightweight.
- [ ] Missing Holding returns 404.
- [ ] Missing position returns 404.
- [ ] Disabled Holding rejects new evaluation with 409 HOLDING_DISABLED.
- [ ] Historical evaluation remains readable after Holding is disabled.
- [ ] Expired row still represented as open is rejected as invalid current-position state.
- [ ] Analytical InsufficientData results remain successful persisted evaluations where current-state request itself is valid.
- [ ] No PUT/PATCH/DELETE exists for immutable Phase 6 evaluations.

---

# 27. Explainability

Acceptance criteria:

- [ ] Stable machine-readable codes exist separately from human explanations.
- [ ] DRS component inputs/scores/maxima are preserved.
- [ ] Every hard-trigger result is preserved, not only triggered ones.
- [ ] Profit-taking inputs and gross captured ratio are preserved.
- [ ] Every roll hard gate is observable.
- [ ] Every candidate state is explicit.
- [ ] Roll economics preserves BTC Ask, replacement Bid, NetRollPerShare, NetRollTotal, debit utilization, and credit ratio where applicable.
- [ ] RQS component values are preserved.
- [ ] Candidate rejection/missing-data reasons are preserved.
- [ ] Preferred candidate and tie-break ordering are reproducible.
- [ ] Disposition reason is available without consumers reconstructing strategy logic.

---

# 28. Versioning and Configuration

Acceptance criteria:

- [ ] Complete resolved Defense/Roll configuration is persisted.
- [ ] Shared ConfigurationVersion is persisted.
- [ ] DefenseStrategyVersion is persisted.
- [ ] RollStrategyVersion is persisted.
- [ ] Numeric-only approved parameter changes are configuration-version changes.
- [ ] Formula/component/gate/missing-data/ranking/decision-flow changes require strategy-version change.
- [ ] Historical evaluations retain original versions and resolved configuration.
- [ ] Invalid configuration fails clearly rather than clamping.

---

# 29. Golden Scenarios

Maintain deterministic canonical scenarios at minimum:

```text
SafeHold
ProfitClose
HighDeltaDefense
ItmDefense
RapidDeltaDefense
RollCredit
RollDebitWithinLimit
RollDebitRejected
HighTaxDeltaRejected
NewEarningsCrossingRejected
StrictOtmReplacementRequired
PoorCcosCloseWait
NoValidRollHardTriggerReview
CandidateInsufficientDataReview
DefenseInsufficientData
```

Acceptance criteria:

- [ ] SafeHold yields no defensive activation and NO_ACTION or MONITOR according to profit-taking state.
- [ ] ProfitClose demonstrates low defense risk with >=70% gross capture.
- [ ] HighDeltaDefense demonstrates exact .40 trigger semantics.
- [ ] ItmDefense triggers regardless of Delta availability where ITM can be established.
- [ ] RapidDeltaDefense uses previous trading observation, not 24-hour subtraction.
- [ ] RollCredit produces deterministic positive economics.
- [ ] RollDebitWithinLimit passes at the exact maximum.
- [ ] RollDebitRejected fails immediately beyond the maximum.
- [ ] HighTaxDeltaRejected proves .20 cap.
- [ ] NewEarningsCrossingRejected proves only newly introduced crossing rejects.
- [ ] StrictOtmReplacementRequired rejects a higher strike that is still ATM/ITM.
- [ ] PoorCcosCloseWait uses a Rankable candidate but CurrentCCOS <55.
- [ ] NoValidRollHardTriggerReview yields DEFENSE_REVIEW.
- [ ] CandidateInsufficientDataReview does not misclassify unknown candidate eligibility as rejection.
- [ ] DefenseInsufficientData preserves partial components/triggers without fabricated zero values.

---

# 30. Merge Gate

Before Phase 6 merge:

- [ ] Release build passes with 0 warnings and 0 errors.
- [ ] Entire test suite passes with 0 failures and 0 skips unless a skip is explicitly justified.
- [ ] Every numeric strategy threshold has below/exact/above tests.
- [ ] All five hard triggers have deterministic boundary/missing-data tests.
- [ ] Profit-taking thresholds have deterministic boundary tests.
- [ ] Roll candidate DTE/Delta/debit/CCOS boundaries have deterministic tests.
- [ ] DRS and RQS maximum/minimum behavior is validated.
- [ ] Candidate ranking/tie-breaking is deterministic.
- [ ] Persistence is append-only and historically stable.
- [ ] Migration from the Phase 5 database lineage succeeds.
- [ ] No pending migrations remain after upgrade.
- [ ] Existing Phase 1–5 API behavior remains unchanged.
- [ ] No Phase 7 Campaign/Transaction implementation is introduced.
- [ ] No automatic execution exists.
- [ ] `git diff --check` passes.
- [ ] Working tree is clean after final commit.

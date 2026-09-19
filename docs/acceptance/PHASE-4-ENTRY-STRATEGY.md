# Phase 4 — Entry Strategy Acceptance Checklist

## Objective

Implement the deterministic covered-call entry-strategy layer that consumes Phase 3 market/indicator facts and normalized option observations, evaluates whether an entry opportunity exists, ranks eligible call contracts, and persists an immutable, explainable Phase 4 evaluation.

`SPECIFICATION.md` is authoritative for product and strategy behavior.

`AGENTS.md` remains authoritative for repository engineering guidance.

Phase 4 shall implement:

1. Entry-strategy input and result contracts.
2. Covered Call Opportunity Score (CCOS).
3. Underlying breakout veto.
4. Contract candidate derivation and hard gates.
5. Contract Score.
6. Deterministic contract ranking.
7. Preferred initial contract/strike selection.
8. Reproducible evaluation orchestration.
9. Immutable Phase 4 persistence.
10. Phase 4 HTTP API.

Phase 4 shall not implement position sizing, coverage allocation, strike laddering, DRS, roll scoring, final SELL recommendations, brokerage execution, or backtesting/optimization.

---

# 1. Governing Boundary

The architecture shall preserve:

```text
Persisted normalized market data
        +
Phase 3 IndicatorSnapshot
        +
Holding strategy settings
        +
Earnings context
        |
        v
Application evaluation orchestration
        |
        v
Pure Phase 4 Strategy
        |
        v
EntryStrategyEvaluation
        |
        v
Immutable persistence / API
```

Strategy calculations shall not directly depend on:

```text
Tradier
HTTP
JSON
EF Core
SQLite
ASP.NET
Excel
current wall-clock time
```

Given identical inputs, versions, and configuration, Phase 4 must produce identical results.

---

# 2. Phase Boundary

Phase 4 stops before position sizing.

Phase 4 may produce:

```text
EntryCandidateExists
PreferredInitialContract
PreferredInitialStrike
```

Phase 4 must not produce:

```text
RecommendedContracts
coverage percentage
available-share allocation
existing-call exposure allocation
Delta Exposure Ratio sizing
strike ladder
final SELL action
brokerage order
```

`Holding.IsEnabled == false` is an Application-level exclusion before Strategy evaluation.

Acceptance criteria:

- [x] Disabled holding is not passed to the Phase 4 strategy engine.
- [x] Disabled holding POST returns `409 HOLDING_DISABLED`.
- [x] No Phase 4 class or API returns a final recommended contract count.

---

# 3. Phase 4A — Foundations and Input Contracts

Implement provider-independent, immutable calculation contracts for at least:

```text
HoldingContext
IndicatorContext
EarningsContext
EvaluationContext
Strategy configuration
ScoreComponentResult
ScoreInput
GateResult
ContractEvaluation
EntryStrategyEvaluation result
stable reason codes
stable missing-input codes
StrategyVersion
```

The implementation shall also refine the Phase 3 resistance output so unavailable resistance distinguishes:

```text
NoQualifiedResistance
InsufficientData
```

The resistance algorithm itself shall not change in Phase 4A.

Acceptance criteria:

- [x] Phase 4 configuration is strongly typed.
- [x] Invalid configuration fails clearly at startup/load time.
- [x] `PreferredDeltaMinimum > EffectivePreferredDeltaMaximum` is invalid configuration.
- [x] `MinimumAnnualizedYield <= 0` is invalid configuration.
- [x] Result contracts represent Available/Unavailable explicitly.
- [x] Gate result states include Passed, Failed, Unavailable, and NotApplicable.
- [x] Missing data is never represented as numeric zero.
- [x] `NoQualifiedResistance` is distinct from `InsufficientData`.
- [x] Strategy project contains no provider, persistence, or HTTP dependency.

---

# 4. Evaluation Time and As-Of Semantics

Every Phase 4 evaluation shall contain both:

```text
IndicatorAsOfDate
EvaluationTimestampUtc
```

`IndicatorAsOfDate` is the daily market/indicator context.

`EvaluationTimestampUtc` is the intraday cutoff for eligible option-chain observations.

They need not represent the same trading date.

Calendar DTE shall use:

```text
EvaluationDate =
America/New_York calendar date corresponding to EvaluationTimestampUtc

DTE =
ExpirationDate - EvaluationDate
```

Acceptance criteria:

- [x] Monday evaluation may consume a Friday IndicatorAsOfDate.
- [x] DTE uses New York calendar date, not UTC date subtraction.
- [x] Historical evaluation never consumes a Phase 3 fact after IndicatorAsOfDate.
- [x] Historical evaluation never consumes an option observation after EvaluationTimestampUtc.
- [x] Tests do not depend on machine local timezone.

---

# 5. Option-Chain Snapshot Selection

For each expiration, Application orchestration shall select the latest complete persisted normalized option-chain observation with:

```text
ChainTimestamp <= EvaluationTimestampUtc
```

Acceptance criteria:

- [x] Contracts from different chain timestamps are never mixed within one expiration.
- [x] A newer empty chain supersedes an older populated chain.
- [x] No fallback occurs from a newer empty chain to an older populated chain.
- [x] Future chain observations are excluded.
- [x] Input enumeration order cannot change the selected chain.
- [x] Contract calculations use the selected chain's provider-supplied `UnderlyingPrice`.
- [x] Missing `UnderlyingPrice` is never replaced with daily Close, a quote, Last, or zero.

---

# 6. Global Missing-Data Policy

Required Phase 4 data shall never be converted to:

```text
0
neutral
passing gate
estimated substitute
renormalized score
```

Acceptance criteria:

- [x] Missing required CCOS component input makes CCOS unavailable.
- [x] CCOS weights are never renormalized around missing components.
- [x] Missing required Contract Score component input makes that contract's score unavailable.
- [x] Contract Score weights are never renormalized.
- [x] One unavailable contract does not poison independent contracts.
- [x] Missing gate input produces `INSUFFICIENT_DATA`, not the threshold failure code.
- [x] `INSUFFICIENT_DATA` records exact machine-readable MissingInputs.
- [x] NotApplicable is distinct from Unavailable.

---

# 7. Phase 4B — CCOS

CCOS shall contain exactly these V1 weighted components:

| Component | Maximum |
|---|---:|
| Volatility | 25 |
| RSI | 15 |
| Bollinger | 15 |
| Trend/Momentum | 15 |
| Resistance/Structure | 15 |
| Market/Sector Regime | 15 |
| **Total** | **100** |

Classification:

```text
0–39    NO TRADE
40–54   WEAK
55–69   WATCH
70–79   SELL CANDIDATE
80–89   STRONG
90–100  EXCEPTIONAL
```

The actual entry threshold is the snapshotted `Holding.MinimumCcos`.

## 7.1 Scoring-Band Boundaries

Interior shared boundaries belong to the band beginning at that boundary unless explicitly stated otherwise.

Explicit terminal `<` and `>` endpoints remain strict.

Acceptance criteria:

- [x] Every CCOS numeric threshold has tests immediately below, exactly at, and immediately above.
- [x] Classification boundaries have deterministic tests.
- [x] Classification does not itself determine entry eligibility.

## 7.2 Volatility — 25

Acceptance criteria:

- [x] `VolatilityScore = IVPercentileScore + IV30ToRV30Score`.
- [x] IV Percentile points match SPECIFICATION Section 23 exactly.
- [x] IV30/RV30 points match SPECIFICATION Section 23 exactly.
- [x] `IV30/RV30 = 1.35` receives 8, while a value above 1.35 receives 10.
- [x] Missing IV30, IVPercentile, or RV30 makes the component unavailable.
- [x] `RV30 <= 0` makes the component unavailable.
- [x] IVRank does not contribute to CCOS V1.

## 7.3 RSI — 15

Acceptance criteria:

- [x] RSI scoring matches SPECIFICATION Section 24 exactly.
- [x] RSI 70 enters the 70–75 band.
- [x] RSI 75 enters the 75–80 band.
- [x] RSI 80 remains in the 75–80 band.
- [x] RSI above 80 receives the >80 score.
- [x] RSI outside 0–100 is invalid/unavailable.

## 7.4 Bollinger — 15

Acceptance criteria:

- [x] %B scoring matches SPECIFICATION Section 25.1 exactly.
- [x] %B 1.15 receives 7 points; a value above 1.15 receives 3.
- [x] Available Bollinger bandwidth contributes exactly 5 points.
- [x] Phase 4 does not calculate bandwidth slope/expansion rate.
- [x] Missing %B or bandwidth makes the component unavailable.

## 7.5 Trend/Momentum — 15

Acceptance criteria:

- [x] `SMA20 > SMA50` contributes 5.
- [x] `SMA50 > SMA200` contributes 5.
- [x] `MACDHistogram > 0` contributes 5.
- [x] Equality contributes zero for the applicable check.
- [x] Missing any required input makes the entire component unavailable.

## 7.6 Resistance/Structure — 15

Acceptance criteria:

- [x] `NoQualifiedResistance` is a valid state and scores 0/15.
- [x] `InsufficientData` makes the component unavailable.
- [x] Distance scoring matches SPECIFICATION Section 25.3.
- [x] A qualified one-touch resistance scores 0 for touches while its distance and recency remain scorable.
- [x] Touch scoring matches SPECIFICATION Section 25.3.
- [x] Recency scoring matches SPECIFICATION Section 25.3.
- [x] Resistance facts are consumed from Phase 3 as-of context.
- [x] Phase 4 does not recompute resistance distance from intraday chain UnderlyingPrice.

## 7.7 Market/Sector Regime — 15

Acceptance criteria:

- [x] Market NEUTRAL/BULLISH/BEARISH scores 8/4/0.
- [x] Sector NEUTRAL/BULLISH/BEARISH scores 7/3/0.
- [x] Missing either regime makes the entire component unavailable.
- [x] Phase 4 does not recalculate regime from benchmark SMA data.

---

# 8. Breakout Veto

V1 breakout veto is exactly:

```text
BollingerPercentB > 1.15
AND RSI14 >= 75
AND MACDHistogram > 0
```

Acceptance criteria:

- [x] All three conditions are required.
- [x] %B exactly 1.15 does not veto.
- [x] RSI exactly 75 satisfies the RSI condition.
- [x] MACDHistogram exactly 0 does not veto.
- [x] Veto produces `BREAKOUT_VETO`.
- [x] Available CCOS remains retained for explanation.
- [x] Veto prevents EntryCandidateExists.
- [x] No additional V1 breakout rule exists.

---

# 9. Candidate Contract Universe and Derived Metrics

Phase 4 candidates are calls only.

Do not silently prefilter calls for delta, strike, liquidity, earnings, premium, or yield before gate evaluation.

Approved V1 metrics:

```text
ReferencePremium = Bid
Mid = (Bid + Ask) / 2
BidAskSpreadPercent = (Ask - Bid) / Mid
StrikeDistance = Strike - UnderlyingPrice
OTMPercent = (Strike - UnderlyingPrice) / UnderlyingPrice
PremiumYield = ReferencePremium / UnderlyingPrice
DailyPremiumYield = PremiumYield / DTE
AnnualizedPremiumYield = DailyPremiumYield * 365
```

Acceptance criteria:

- [x] Puts are excluded from the candidate universe.
- [x] Rejected calls remain observable with reasons.
- [x] ReferencePremium is Bid, not Mid or Last.
- [x] Equality/rounding behavior is deterministic.
- [x] Missing required price/premium inputs produces explicit unavailable state.
- [x] Phase 4 V1 does not require DeltaAdjustedYield, ExpectedMove, ExpectedMoveRatio, or StrikeVsResistance.

---

# 10. Contract Hard Gates

A call is hard-gate eligible only if every required gate passes.

## 10.1 DTE

```text
14 <= DTE <= 45
```

Tests:

- [x] 13 rejects.
- [x] 14 passes.
- [x] 45 passes.
- [x] 46 rejects.
- [x] Failure code is `DTE_OUTSIDE_RANGE`.

## 10.2 Strike

```text
Strike > UnderlyingPrice
```

Tests:

- [x] Strike below underlying rejects.
- [x] Strike equal to underlying rejects.
- [x] Strike strictly above underlying passes.
- [x] Failure code is `STRIKE_NOT_OTM`.

## 10.3 Effective Maximum Delta

```text
EffectiveMaximumDelta =
min(
    configured global maximum,
    Holding.MaximumInitialDelta,
    configured high-tax maximum when TaxSensitivity == High
)
```

Default global maximum = .25.

Default High tax maximum = .20.

Tests:

- [x] Holding may tighten the global limit.
- [x] Holding cannot loosen the global limit.
- [x] TaxSensitivity.High applies the .20 configured cap.
- [x] Delta equal to EffectiveMaximumDelta passes.
- [x] Delta immediately above rejects with `DELTA_EXCEEDS_MAXIMUM`.
- [x] Delta must be finite and in 0–1.
- [x] Invalid/missing Delta yields `INSUFFICIENT_DATA`.
- [x] Strategy never applies `abs(Delta)`.

## 10.4 Earnings

Tests:

- [x] Individual-stock earnings before expiration rejects.
- [x] Earnings on expiration date rejects.
- [x] Earnings after expiration passes.
- [x] Failure code is `EARNINGS_BEFORE_EXPIRATION`.
- [x] Missing required stock earnings produces `INSUFFICIENT_DATA`.
- [x] ETF earnings status is NotApplicable.
- [x] Dividend/ex-dividend/material-event rules do not affect Phase 4 V1 entry.

## 10.5 Liquidity

Require:

```text
Bid > 0
Ask > Bid
OpenInterest >= 100
BidAskSpreadPercent <= 20%
```

Tests:

- [x] OI 99 rejects.
- [x] OI 100 passes.
- [x] Spread immediately below 20% passes.
- [x] Spread exactly 20% passes.
- [x] Spread above 20% rejects.
- [x] Ask equal to Bid rejects.
- [x] Present values that fail thresholds use `INSUFFICIENT_LIQUIDITY`.
- [x] Missing Bid/Ask/OI uses `INSUFFICIENT_DATA`.

## 10.6 Premium and Yield Floors

Tests:

- [x] ReferencePremium below Holding.MinimumPremium rejects.
- [x] Equality passes.
- [x] Failure code is `PREMIUM_BELOW_MINIMUM`.
- [x] AnnualizedPremiumYield below Holding.MinimumAnnualizedYield rejects.
- [x] Equality passes.
- [x] Failure code is `ANNUALIZED_YIELD_BELOW_MINIMUM`.

## 10.7 Multiple Failures

- [x] All determinable gate failures are retained.
- [x] Gate evaluation does not stop at the first failure.
- [x] Preferred delta range is not a hard gate.
- [x] Coverage/share-count/DER constraints are absent from Phase 4 hard gates.

---

# 11. Phase 4C — Contract Score

Contract Score shall contain exactly:

| Component | Maximum |
|---|---:|
| Delta | 25 |
| Strike Safety | 20 |
| Premium Efficiency | 20 |
| DTE Efficiency | 10 |
| IV/Volatility Edge | 10 |
| Liquidity | 10 |
| Theta Efficiency | 5 |
| **Total** | **100** |

Classification:

```text
<60      REJECT
60–69    WEAK
70–79    ACCEPTABLE
80–89    GOOD
90–100   EXCELLENT
```

Actual entry acceptability uses the snapshotted `Holding.MinimumContractScore`.

Every scoring threshold shall have below/at/above tests.

## 11.1 Delta — 25

Tests:

- [x] Below preferred range scores 20.
- [x] Preferred range, inclusive, scores 25.
- [x] Above preferred but within EffectiveMaximumDelta scores 10.
- [x] EffectivePreferredDeltaMaximum is capped by EffectiveMaximumDelta.
- [x] Invalid preferred range is rejected as configuration error.

## 11.2 Strike Safety — 20

Tests:

- [x] OTM bands score 0/4/8/12/15/18/20 exactly as specified.
- [x] OTM 8% remains in the 18-point band.
- [x] Above 8% scores 20.
- [x] Resistance does not affect Contract Score V1.

## 11.3 Premium Efficiency — 20

```text
PremiumEfficiencyRatio =
AnnualizedPremiumYield / Holding.MinimumAnnualizedYield
```

Tests:

- [x] Ratio bands score 0/5/10/15/20 exactly as specified.
- [x] Hard-gate-passing ratio exactly 1.00 scores 0.
- [x] Ratio exactly 2.00 scores 20.
- [x] MinimumPremium contributes no additional score.
- [x] MinimumAnnualizedYield must be > 0.

## 11.4 DTE Efficiency — 10

Tests:

- [x] 14–20 scores 5.
- [x] 21–35 scores 10.
- [x] 36–45 scores 8.
- [x] No holding-specific preferred-DTE setting is added.

## 11.5 IV/Volatility Edge — 10

```text
ContractVolatilityEdge =
Contract.ImpliedVolatility / RV30
```

Tests:

- [x] Bands score 0/2/4/6/8/10 exactly as specified.
- [x] Ratio exactly 1.35 scores 8.
- [x] Ratio above 1.35 scores 10.
- [x] Contract IV and RV30 must be finite and > 0.
- [x] IV30 and IVRank do not contribute to this component.
- [x] Phase 4 never reconstructs IV30.

## 11.6 Liquidity — 10

```text
LiquidityScore =
SpreadScore + OpenInterestScore
```

Tests:

- [x] Spread bands score 5/4/2/1 exactly as specified.
- [x] OI bands score 1/2/3/4/5 exactly as specified.
- [x] Every gate-eligible contract receives at least 2/10 liquidity points.
- [x] Daily option volume has no effect on eligibility or Contract Score.

## 11.7 Theta Efficiency — 5

```text
ThetaEfficiencyRatio =
(-Theta) / ReferencePremium
```

Tests:

- [x] Ratio bands score 0/1/2/3/4/5 exactly as specified.
- [x] Theta must be finite and strictly negative.
- [x] Theta zero, positive, missing, or non-finite makes Contract Score unavailable.
- [x] Strategy never repairs Theta using absolute value.

---

# 12. Contract Ranking

Rank all hard-gate-passing contracts with complete Contract Scores, including contracts below MinimumContractScore.

Primary:

```text
ContractScore DESC
```

Tie-break order:

```text
Delta ASC
OTMPercent DESC
AnnualizedPremiumYield DESC
BidAskSpreadPercent ASC
OpenInterest DESC
DTE ASC
OptionSymbol ordinal ASC
```

Acceptance criteria:

- [x] Contract Score is primary.
- [x] Each tie-break key has an isolated deterministic test.
- [x] Input/provider enumeration order cannot affect rank.
- [x] Hard-gate failures are not ranked.
- [x] Contracts with unavailable Contract Score are not ranked.
- [x] A ranked contract below MinimumContractScore is retained but EntryAcceptable=false.
- [x] Contract ranking may still be produced when CCOS is below its entry threshold.

---

# 13. Preferred Initial Contract and Disposition

No separate strike optimizer exists.

```text
PreferredInitialContract =
highest-ranked EntryAcceptable contract

PreferredInitialStrike =
PreferredInitialContract.Strike
```

`EntryCandidateExists=true` requires:

```text
CCOS available
no underlying veto
CCOS >= Holding.MinimumCcos
at least one hard-gate-passing contract
complete Contract Score
ContractScore >= Holding.MinimumContractScore
```

Acceptance criteria:

- [x] Preferred strike always equals preferred contract strike.
- [x] CCOS below threshold produces no preferred contract.
- [x] Breakout veto produces no preferred contract.
- [x] CCOS pass with no acceptable contract uses `NO_ACCEPTABLE_CONTRACT`.
- [x] Missing CCOS data uses `INSUFFICIENT_DATA`.
- [x] Contract details remain available where independently calculable.
- [x] Phase 4 uses terminology PreferredInitialContract/PreferredInitialStrike rather than final Recommendation/SELL semantics.

---

# 14. Explainability

CCOS and Contract Score component results shall expose:

```text
Code
Name
Status
Score
MaximumScore
Inputs
Explanation
```

Gate results shall expose:

```text
Code
Status
Inputs
Explanation
```

Acceptance criteria:

- [x] All evaluated gate results are retained, not only failures.
- [x] Gate passes/failures/unavailable/not-applicable are distinguishable.
- [x] MissingInputs uses stable machine-readable codes.
- [x] Stable gate/component/reason codes are not reconstructed from human text.
- [x] Human explanations are persisted with the immutable evaluation.
- [x] API/UI does not need to recalculate score, eligibility, rank, or disposition.
- [x] A missing value never masquerades as a threshold failure.

---

# 15. Phase 4D — Orchestration

Application shall assemble a reproducible evaluation bundle containing:

```text
snapshotted HoldingContext
Phase 3 IndicatorContext
IndicatorAsOfDate
EvaluationTimestampUtc
selected option-chain observations
EarningsContext
IndicatorCalculationVersion
ConfigurationVersion
StrategyVersion
resolved Phase 4 configuration
```

Acceptance criteria:

- [x] Application, not Strategy, performs persistence/provider access.
- [x] Strategy remains pure and synchronous.
- [x] Market freshness policy remains an Application/MarketData concern.
- [x] Phase 4 strategy does not independently refresh provider data.
- [x] Current evaluation uses server time supplied explicitly/injectably.
- [x] Historical deterministic orchestration can be tested without current time.
- [x] Earnings input is supplied through a provider-independent boundary.
- [x] V1 production current evaluation uses validated configuration-backed earnings dates; a provider-backed
  source is deferred pending an authoritative provider contract or sanitized fixture.
- [x] The configured source does not claim to reconstruct historical event knowledge; Phase 4E persists the
  exact consumed EarningsContext.
- [x] V1 orchestration introduces no dividend/material-event strategy rule.

---

# 16. Versioning

Persist independently:

```text
IndicatorCalculationVersion
ConfigurationVersion
StrategyVersion
```

Acceptance criteria:

- [x] Phase 3 algorithm changes require IndicatorCalculationVersion review/change.
- [x] Numeric strategy parameter changes require ConfigurationVersion change.
- [x] Formula/component/gate/missing-data/ranking semantic changes require StrategyVersion change.
- [x] Holding-specific values are captured in HoldingContext, not treated as ConfigurationVersion.
- [x] Complete resolved configuration used by the evaluation is persisted.
- [x] V1 uses one shared ConfigurationVersion identity across Phase 3 and Phase 4.
- [x] Historical evaluations are not reinterpreted under newer versions.

---

# 17. Phase 4E — Immutable Persistence

Persist `EntryStrategyEvaluation` as append-only history.

At minimum preserve:

```text
identity/timestamps
HoldingContext
IndicatorContext facts/statuses
EarningsContext
actual selected normalized option observations
resolved configuration
all three version identities
CCOS result/components/gates
all evaluated contract metrics/gates/scores
ranking
EntryCandidateExists
preferred contract/strike
disposition
MissingInputs
explanations
```

A hybrid relational + structured-JSON representation is acceptable if required data remains queryable/reproducible and no required input/result is discarded.

Acceptance criteria:

- [x] New evaluation inserts a new immutable record.
- [x] Later evaluation never overwrites an earlier record.
- [x] Editing Holding after evaluation does not change historical evaluation retrieval.
- [x] Recalculating canonical Phase 3 snapshot does not change historical Phase 4 evaluation retrieval.
- [x] Newer option observations do not change historical Phase 4 evaluation retrieval.
- [x] Resolved configuration and versions round-trip exactly.
- [x] Rejected and insufficient-data contracts are retained.
- [x] Persistence stores more than final CCOS/ContractScore totals.
- [x] EF Core migration is included.
- [x] Clean database migration succeeds.
- [x] Existing Phase 3 database migrates forward successfully.

---

# 18. Phase 4F — HTTP API

Create:

```http
POST /api/holdings/{holdingId}/entry-evaluations
GET /api/entry-evaluations/{entryStrategyEvaluationId}
GET /api/holdings/{holdingId}/entry-evaluations
```

Acceptance criteria:

- [x] POST evaluates current server-owned context and returns `201 Created`.
- [x] Successful POST persists exactly one immutable evaluation.
- [x] GET by ID is passive retrieval and performs no refresh/recalculation.
- [x] Holding history is lightweight and newest first.
- [x] Missing holding returns 404.
- [x] Disabled holding returns 409 `HOLDING_DISABLED`.
- [x] Strategy outcomes such as `INSUFFICIENT_DATA`, `CCOS_BELOW_MINIMUM`, `BREAKOUT_VETO`, or `NO_ACCEPTABLE_CONTRACT` remain successful persisted evaluations.
- [x] Public V1 POST does not expose arbitrary as-of/evaluation/version parameters.
- [x] No PUT, PATCH, or DELETE endpoint exists for EntryStrategyEvaluation.
- [x] API DTOs do not expose EF entities.

---

# 19. Golden Phase 4 Scenarios

Maintain fixed deterministic regression scenarios.

## StrongEntry

Must prove:

- [x] CCOS meets threshold.
- [x] No underlying veto.
- [x] Multiple contracts are evaluated.
- [x] At least one contract meets MinimumContractScore.
- [x] Exact CCOS and all component scores are asserted.
- [x] Exact Contract Scores and all component scores are asserted.
- [x] Exact rank order is asserted.
- [x] Preferred contract/strike is deterministic.
- [x] `EntryCandidateExists=true`.

## BreakoutVeto

- [x] Otherwise valid/high CCOS retained.
- [x] Exact breakout formula fires.
- [x] `BREAKOUT_VETO` retained.
- [x] `EntryCandidateExists=false`.

## InsufficientData

- [x] Remove at least one required CCOS input.
- [x] CCOS is unavailable.
- [x] Exact MissingInputs asserted.
- [x] `INSUFFICIENT_DATA`.
- [x] No preferred contract.

## HighAssignmentRisk

- [x] Above-max Delta contract rejects with `DELTA_EXCEEDS_MAXIMUM`.
- [x] Independent lower-delta alternatives remain evaluable.

## TaxSensitivePosition

- [x] TaxSensitivity.High applies configured .20 default cap.
- [x] Boundary at the effective maximum is tested.

## IlliquidContract

- [x] Otherwise-attractive contract fails liquidity.
- [x] `INSUFFICIENT_LIQUIDITY`.
- [x] Rejected contract is retained but not ranked.

## EarningsBeforeExpiration

- [x] Earnings before expiration rejects.
- [x] Same-day earnings rejects.
- [x] `EARNINGS_BEFORE_EXPIRATION`.

## NoAcceptableContract

- [x] CCOS passes.
- [x] Contract analysis is produced.
- [x] No contract meets complete entry requirements.
- [x] `NO_ACCEPTABLE_CONTRACT`.
- [x] Detailed per-contract results are retained.

---

# 20. Test Project Ownership

Use existing test-project boundaries.

`OptionsEngine.Strategy.Tests` shall own:

```text
CCOS math
breakout veto
contract derived metrics
hard gates
Contract Score math
ranking/tie-breaks
preferred selection
missing-data semantics
golden pure-strategy scenarios
all threshold boundary tests
```

Application/Infrastructure tests shall own:

```text
as-of orchestration
chain snapshot selection
earnings input assembly
version/config capture
immutable persistence
migration paths
```

`OptionsEngine.Api.Tests` shall own:

```text
POST creation
GET passive retrieval
history
404/409 semantics
negative strategy outcomes as successful evaluations
HTTP immutability surface
```

Routine CI tests must not require:

```text
live Tradier
internet
brokerage credentials
current market price
current date/time
```

---

# 21. Merge Gate

Before Phase 4 is complete:

- [x] `dotnet restore` succeeds.
- [x] `dotnet build --configuration Release --no-restore` succeeds.
- [x] Release build contains zero warnings/errors unless an explicitly documented repository-wide exception exists.
- [x] `dotnet test --configuration Release --no-build` passes.
- [x] Clean EF migration path succeeds.
- [x] Phase 3 database upgrades to Phase 4 successfully.
- [x] No credentials or personal financial data are introduced.
- [x] Architecture dependency rules remain intact.
- [x] No Phase 5+ behavior is implemented.
- [x] Documentation matches implementation.
- [x] Every Phase 4 threshold has deterministic below/at/above coverage.
- [x] All eight Phase 4 golden scenarios pass.

---

# 22. Work Packet Discipline

Phase 4 implementation shall proceed in this order:

```text
4A — Foundations and input/result contracts
4B — CCOS and underlying eligibility
4C — Contract gates/scoring/ranking/selection
4D — Evaluation orchestration and earnings/market inputs
4E — Immutable persistence
4F — HTTP API and merge-gate validation
```

No packet may introduce a formula, threshold, trading rule, score fallback, or missing-data substitute not already approved in `SPECIFICATION.md`.

If implementation exposes an ambiguity, stop that strategy change and resolve the specification instead of guessing in code.

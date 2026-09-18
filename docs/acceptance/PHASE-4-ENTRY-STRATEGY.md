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

- [ ] Disabled holding is not passed to the Phase 4 strategy engine.
- [ ] Disabled holding POST returns `409 HOLDING_DISABLED`.
- [ ] No Phase 4 class or API returns a final recommended contract count.

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

- [ ] Phase 4 configuration is strongly typed.
- [ ] Invalid configuration fails clearly at startup/load time.
- [ ] `PreferredDeltaMinimum > EffectivePreferredDeltaMaximum` is invalid configuration.
- [ ] `MinimumAnnualizedYield <= 0` is invalid configuration.
- [ ] Result contracts represent Available/Unavailable explicitly.
- [ ] Gate result states include Passed, Failed, Unavailable, and NotApplicable.
- [ ] Missing data is never represented as numeric zero.
- [ ] `NoQualifiedResistance` is distinct from `InsufficientData`.
- [ ] Strategy project contains no provider, persistence, or HTTP dependency.

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

- [ ] Monday evaluation may consume a Friday IndicatorAsOfDate.
- [ ] DTE uses New York calendar date, not UTC date subtraction.
- [ ] Historical evaluation never consumes a Phase 3 fact after IndicatorAsOfDate.
- [ ] Historical evaluation never consumes an option observation after EvaluationTimestampUtc.
- [ ] Tests do not depend on machine local timezone.

---

# 5. Option-Chain Snapshot Selection

For each expiration, Application orchestration shall select the latest complete persisted normalized option-chain observation with:

```text
ChainTimestamp <= EvaluationTimestampUtc
```

Acceptance criteria:

- [ ] Contracts from different chain timestamps are never mixed within one expiration.
- [ ] A newer empty chain supersedes an older populated chain.
- [ ] No fallback occurs from a newer empty chain to an older populated chain.
- [ ] Future chain observations are excluded.
- [ ] Input enumeration order cannot change the selected chain.
- [ ] Contract calculations use the selected chain's provider-supplied `UnderlyingPrice`.
- [ ] Missing `UnderlyingPrice` is never replaced with daily Close, a quote, Last, or zero.

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

- [ ] Missing required CCOS component input makes CCOS unavailable.
- [ ] CCOS weights are never renormalized around missing components.
- [ ] Missing required Contract Score component input makes that contract's score unavailable.
- [ ] Contract Score weights are never renormalized.
- [ ] One unavailable contract does not poison independent contracts.
- [ ] Missing gate input produces `INSUFFICIENT_DATA`, not the threshold failure code.
- [ ] `INSUFFICIENT_DATA` records exact machine-readable MissingInputs.
- [ ] NotApplicable is distinct from Unavailable.

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

- [ ] Every CCOS numeric threshold has tests immediately below, exactly at, and immediately above.
- [ ] Classification boundaries have deterministic tests.
- [ ] Classification does not itself determine entry eligibility.

## 7.2 Volatility — 25

Acceptance criteria:

- [ ] `VolatilityScore = IVPercentileScore + IV30ToRV30Score`.
- [ ] IV Percentile points match SPECIFICATION Section 23 exactly.
- [ ] IV30/RV30 points match SPECIFICATION Section 23 exactly.
- [ ] `IV30/RV30 = 1.35` receives 8, while a value above 1.35 receives 10.
- [ ] Missing IV30, IVPercentile, or RV30 makes the component unavailable.
- [ ] `RV30 <= 0` makes the component unavailable.
- [ ] IVRank does not contribute to CCOS V1.

## 7.3 RSI — 15

Acceptance criteria:

- [ ] RSI scoring matches SPECIFICATION Section 24 exactly.
- [ ] RSI 70 enters the 70–75 band.
- [ ] RSI 75 enters the 75–80 band.
- [ ] RSI 80 remains in the 75–80 band.
- [ ] RSI above 80 receives the >80 score.
- [ ] RSI outside 0–100 is invalid/unavailable.

## 7.4 Bollinger — 15

Acceptance criteria:

- [ ] %B scoring matches SPECIFICATION Section 25.1 exactly.
- [ ] %B 1.15 receives 7 points; a value above 1.15 receives 3.
- [ ] Available Bollinger bandwidth contributes exactly 5 points.
- [ ] Phase 4 does not calculate bandwidth slope/expansion rate.
- [ ] Missing %B or bandwidth makes the component unavailable.

## 7.5 Trend/Momentum — 15

Acceptance criteria:

- [ ] `SMA20 > SMA50` contributes 5.
- [ ] `SMA50 > SMA200` contributes 5.
- [ ] `MACDHistogram > 0` contributes 5.
- [ ] Equality contributes zero for the applicable check.
- [ ] Missing any required input makes the entire component unavailable.

## 7.6 Resistance/Structure — 15

Acceptance criteria:

- [ ] `NoQualifiedResistance` is a valid state and scores 0/15.
- [ ] `InsufficientData` makes the component unavailable.
- [ ] Distance scoring matches SPECIFICATION Section 25.3.
- [ ] A qualified one-touch resistance scores 0 for touches while its distance and recency remain scorable.
- [ ] Touch scoring matches SPECIFICATION Section 25.3.
- [ ] Recency scoring matches SPECIFICATION Section 25.3.
- [ ] Resistance facts are consumed from Phase 3 as-of context.
- [ ] Phase 4 does not recompute resistance distance from intraday chain UnderlyingPrice.

## 7.7 Market/Sector Regime — 15

Acceptance criteria:

- [ ] Market NEUTRAL/BULLISH/BEARISH scores 8/4/0.
- [ ] Sector NEUTRAL/BULLISH/BEARISH scores 7/3/0.
- [ ] Missing either regime makes the entire component unavailable.
- [ ] Phase 4 does not recalculate regime from benchmark SMA data.

---

# 8. Breakout Veto

V1 breakout veto is exactly:

```text
BollingerPercentB > 1.15
AND RSI14 >= 75
AND MACDHistogram > 0
```

Acceptance criteria:

- [ ] All three conditions are required.
- [ ] %B exactly 1.15 does not veto.
- [ ] RSI exactly 75 satisfies the RSI condition.
- [ ] MACDHistogram exactly 0 does not veto.
- [ ] Veto produces `BREAKOUT_VETO`.
- [ ] Available CCOS remains retained for explanation.
- [ ] Veto prevents EntryCandidateExists.
- [ ] No additional V1 breakout rule exists.

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

- [ ] Puts are excluded from the candidate universe.
- [ ] Rejected calls remain observable with reasons.
- [ ] ReferencePremium is Bid, not Mid or Last.
- [ ] Equality/rounding behavior is deterministic.
- [ ] Missing required price/premium inputs produces explicit unavailable state.
- [ ] Phase 4 V1 does not require DeltaAdjustedYield, ExpectedMove, ExpectedMoveRatio, or StrikeVsResistance.

---

# 10. Contract Hard Gates

A call is hard-gate eligible only if every required gate passes.

## 10.1 DTE

```text
14 <= DTE <= 45
```

Tests:

- [ ] 13 rejects.
- [ ] 14 passes.
- [ ] 45 passes.
- [ ] 46 rejects.
- [ ] Failure code is `DTE_OUTSIDE_RANGE`.

## 10.2 Strike

```text
Strike > UnderlyingPrice
```

Tests:

- [ ] Strike below underlying rejects.
- [ ] Strike equal to underlying rejects.
- [ ] Strike strictly above underlying passes.
- [ ] Failure code is `STRIKE_NOT_OTM`.

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

- [ ] Holding may tighten the global limit.
- [ ] Holding cannot loosen the global limit.
- [ ] TaxSensitivity.High applies the .20 configured cap.
- [ ] Delta equal to EffectiveMaximumDelta passes.
- [ ] Delta immediately above rejects with `DELTA_EXCEEDS_MAXIMUM`.
- [ ] Delta must be finite and in 0–1.
- [ ] Invalid/missing Delta yields `INSUFFICIENT_DATA`.
- [ ] Strategy never applies `abs(Delta)`.

## 10.4 Earnings

Tests:

- [ ] Individual-stock earnings before expiration rejects.
- [ ] Earnings on expiration date rejects.
- [ ] Earnings after expiration passes.
- [ ] Failure code is `EARNINGS_BEFORE_EXPIRATION`.
- [ ] Missing required stock earnings produces `INSUFFICIENT_DATA`.
- [ ] ETF earnings status is NotApplicable.
- [ ] Dividend/ex-dividend/material-event rules do not affect Phase 4 V1 entry.

## 10.5 Liquidity

Require:

```text
Bid > 0
Ask > Bid
OpenInterest >= 100
BidAskSpreadPercent <= 20%
```

Tests:

- [ ] OI 99 rejects.
- [ ] OI 100 passes.
- [ ] Spread immediately below 20% passes.
- [ ] Spread exactly 20% passes.
- [ ] Spread above 20% rejects.
- [ ] Ask equal to Bid rejects.
- [ ] Present values that fail thresholds use `INSUFFICIENT_LIQUIDITY`.
- [ ] Missing Bid/Ask/OI uses `INSUFFICIENT_DATA`.

## 10.6 Premium and Yield Floors

Tests:

- [ ] ReferencePremium below Holding.MinimumPremium rejects.
- [ ] Equality passes.
- [ ] Failure code is `PREMIUM_BELOW_MINIMUM`.
- [ ] AnnualizedPremiumYield below Holding.MinimumAnnualizedYield rejects.
- [ ] Equality passes.
- [ ] Failure code is `ANNUALIZED_YIELD_BELOW_MINIMUM`.

## 10.7 Multiple Failures

- [ ] All determinable gate failures are retained.
- [ ] Gate evaluation does not stop at the first failure.
- [ ] Preferred delta range is not a hard gate.
- [ ] Coverage/share-count/DER constraints are absent from Phase 4 hard gates.

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

- [ ] Below preferred range scores 20.
- [ ] Preferred range, inclusive, scores 25.
- [ ] Above preferred but within EffectiveMaximumDelta scores 10.
- [ ] EffectivePreferredDeltaMaximum is capped by EffectiveMaximumDelta.
- [ ] Invalid preferred range is rejected as configuration error.

## 11.2 Strike Safety — 20

Tests:

- [ ] OTM bands score 0/4/8/12/15/18/20 exactly as specified.
- [ ] OTM 8% remains in the 18-point band.
- [ ] Above 8% scores 20.
- [ ] Resistance does not affect Contract Score V1.

## 11.3 Premium Efficiency — 20

```text
PremiumEfficiencyRatio =
AnnualizedPremiumYield / Holding.MinimumAnnualizedYield
```

Tests:

- [ ] Ratio bands score 0/5/10/15/20 exactly as specified.
- [ ] Hard-gate-passing ratio exactly 1.00 scores 0.
- [ ] Ratio exactly 2.00 scores 20.
- [ ] MinimumPremium contributes no additional score.
- [ ] MinimumAnnualizedYield must be > 0.

## 11.4 DTE Efficiency — 10

Tests:

- [ ] 14–20 scores 5.
- [ ] 21–35 scores 10.
- [ ] 36–45 scores 8.
- [ ] No holding-specific preferred-DTE setting is added.

## 11.5 IV/Volatility Edge — 10

```text
ContractVolatilityEdge =
Contract.ImpliedVolatility / RV30
```

Tests:

- [ ] Bands score 0/2/4/6/8/10 exactly as specified.
- [ ] Ratio exactly 1.35 scores 8.
- [ ] Ratio above 1.35 scores 10.
- [ ] Contract IV and RV30 must be finite and > 0.
- [ ] IV30 and IVRank do not contribute to this component.
- [ ] Phase 4 never reconstructs IV30.

## 11.6 Liquidity — 10

```text
LiquidityScore =
SpreadScore + OpenInterestScore
```

Tests:

- [ ] Spread bands score 5/4/2/1 exactly as specified.
- [ ] OI bands score 1/2/3/4/5 exactly as specified.
- [ ] Every gate-eligible contract receives at least 2/10 liquidity points.
- [ ] Daily option volume has no effect on eligibility or Contract Score.

## 11.7 Theta Efficiency — 5

```text
ThetaEfficiencyRatio =
(-Theta) / ReferencePremium
```

Tests:

- [ ] Ratio bands score 0/1/2/3/4/5 exactly as specified.
- [ ] Theta must be finite and strictly negative.
- [ ] Theta zero, positive, missing, or non-finite makes Contract Score unavailable.
- [ ] Strategy never repairs Theta using absolute value.

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

- [ ] Contract Score is primary.
- [ ] Each tie-break key has an isolated deterministic test.
- [ ] Input/provider enumeration order cannot affect rank.
- [ ] Hard-gate failures are not ranked.
- [ ] Contracts with unavailable Contract Score are not ranked.
- [ ] A ranked contract below MinimumContractScore is retained but EntryAcceptable=false.
- [ ] Contract ranking may still be produced when CCOS is below its entry threshold.

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

- [ ] Preferred strike always equals preferred contract strike.
- [ ] CCOS below threshold produces no preferred contract.
- [ ] Breakout veto produces no preferred contract.
- [ ] CCOS pass with no acceptable contract uses `NO_ACCEPTABLE_CONTRACT`.
- [ ] Missing CCOS data uses `INSUFFICIENT_DATA`.
- [ ] Contract details remain available where independently calculable.
- [ ] Phase 4 uses terminology PreferredInitialContract/PreferredInitialStrike rather than final Recommendation/SELL semantics.

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

- [ ] All evaluated gate results are retained, not only failures.
- [ ] Gate passes/failures/unavailable/not-applicable are distinguishable.
- [ ] MissingInputs uses stable machine-readable codes.
- [ ] Stable gate/component/reason codes are not reconstructed from human text.
- [ ] Human explanations are persisted with the immutable evaluation.
- [ ] API/UI does not need to recalculate score, eligibility, rank, or disposition.
- [ ] A missing value never masquerades as a threshold failure.

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

- [ ] Application, not Strategy, performs persistence/provider access.
- [ ] Strategy remains pure and synchronous.
- [ ] Market freshness policy remains an Application/MarketData concern.
- [ ] Phase 4 strategy does not independently refresh provider data.
- [ ] Current evaluation uses server time supplied explicitly/injectably.
- [ ] Historical deterministic orchestration can be tested without current time.
- [ ] Earnings input is supplied through a provider-independent boundary.
- [ ] V1 production current evaluation uses validated configuration-backed earnings dates; a provider-backed
  source is deferred pending an authoritative provider contract or sanitized fixture.
- [ ] The configured source does not claim to reconstruct historical event knowledge; Phase 4E persists the
  exact consumed EarningsContext.
- [ ] V1 orchestration introduces no dividend/material-event strategy rule.

---

# 16. Versioning

Persist independently:

```text
IndicatorCalculationVersion
ConfigurationVersion
StrategyVersion
```

Acceptance criteria:

- [ ] Phase 3 algorithm changes require IndicatorCalculationVersion review/change.
- [ ] Numeric strategy parameter changes require ConfigurationVersion change.
- [ ] Formula/component/gate/missing-data/ranking semantic changes require StrategyVersion change.
- [ ] Holding-specific values are captured in HoldingContext, not treated as ConfigurationVersion.
- [ ] Complete resolved configuration used by the evaluation is persisted.
- [ ] V1 uses one shared ConfigurationVersion identity across Phase 3 and Phase 4.
- [ ] Historical evaluations are not reinterpreted under newer versions.

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

- [ ] POST evaluates current server-owned context and returns `201 Created`.
- [ ] Successful POST persists exactly one immutable evaluation.
- [ ] GET by ID is passive retrieval and performs no refresh/recalculation.
- [ ] Holding history is lightweight and newest first.
- [ ] Missing holding returns 404.
- [ ] Disabled holding returns 409 `HOLDING_DISABLED`.
- [ ] Strategy outcomes such as `INSUFFICIENT_DATA`, `CCOS_BELOW_MINIMUM`, `BREAKOUT_VETO`, or `NO_ACCEPTABLE_CONTRACT` remain successful persisted evaluations.
- [ ] Public V1 POST does not expose arbitrary as-of/evaluation/version parameters.
- [ ] No PUT, PATCH, or DELETE endpoint exists for EntryStrategyEvaluation.
- [ ] API DTOs do not expose EF entities.

---

# 19. Golden Phase 4 Scenarios

Maintain fixed deterministic regression scenarios.

## StrongEntry

Must prove:

- [ ] CCOS meets threshold.
- [ ] No underlying veto.
- [ ] Multiple contracts are evaluated.
- [ ] At least one contract meets MinimumContractScore.
- [ ] Exact CCOS and all component scores are asserted.
- [ ] Exact Contract Scores and all component scores are asserted.
- [ ] Exact rank order is asserted.
- [ ] Preferred contract/strike is deterministic.
- [ ] `EntryCandidateExists=true`.

## BreakoutVeto

- [ ] Otherwise valid/high CCOS retained.
- [ ] Exact breakout formula fires.
- [ ] `BREAKOUT_VETO` retained.
- [ ] `EntryCandidateExists=false`.

## InsufficientData

- [ ] Remove at least one required CCOS input.
- [ ] CCOS is unavailable.
- [ ] Exact MissingInputs asserted.
- [ ] `INSUFFICIENT_DATA`.
- [ ] No preferred contract.

## HighAssignmentRisk

- [ ] Above-max Delta contract rejects with `DELTA_EXCEEDS_MAXIMUM`.
- [ ] Independent lower-delta alternatives remain evaluable.

## TaxSensitivePosition

- [ ] TaxSensitivity.High applies configured .20 default cap.
- [ ] Boundary at the effective maximum is tested.

## IlliquidContract

- [ ] Otherwise-attractive contract fails liquidity.
- [ ] `INSUFFICIENT_LIQUIDITY`.
- [ ] Rejected contract is retained but not ranked.

## EarningsBeforeExpiration

- [ ] Earnings before expiration rejects.
- [ ] Same-day earnings rejects.
- [ ] `EARNINGS_BEFORE_EXPIRATION`.

## NoAcceptableContract

- [ ] CCOS passes.
- [ ] Contract analysis is produced.
- [ ] No contract meets complete entry requirements.
- [ ] `NO_ACCEPTABLE_CONTRACT`.
- [ ] Detailed per-contract results are retained.

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

- [ ] `dotnet restore` succeeds.
- [ ] `dotnet build --configuration Release --no-restore` succeeds.
- [ ] Release build contains zero warnings/errors unless an explicitly documented repository-wide exception exists.
- [ ] `dotnet test --configuration Release --no-build` passes.
- [ ] Clean EF migration path succeeds.
- [ ] Phase 3 database upgrades to Phase 4 successfully.
- [ ] No credentials or personal financial data are introduced.
- [ ] Architecture dependency rules remain intact.
- [ ] No Phase 5+ behavior is implemented.
- [ ] Documentation matches implementation.
- [ ] Every Phase 4 threshold has deterministic below/at/above coverage.
- [ ] All eight Phase 4 golden scenarios pass.

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

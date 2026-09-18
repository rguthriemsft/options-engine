# Options Engine --- Durable Design Decision Register

This file records decisions that emerged during requirements/design
discussions and should not be rediscovered or silently changed by an
implementation agent.

## Product / Risk

-   Primary objective: sustainable covered-call income with very low
    assignment probability.
-   Preserving highly appreciated shares takes priority over maximizing
    option premium.
-   Assignment risk is treated as a hard constraint.
-   Initial covered-call sale horizon: 14--45 DTE.
-   Rolling is supported.
-   Every sell recommendation should eventually include a defense plan.
-   Automatic execution is out of V1.

## Data / Provider

-   Tradier is the V1 market-data provider.
-   V1 uses Tradier PRODUCTION API only.
-   Tradier sandbox is explicitly out of scope.
-   Automated tests use mocked HTTP and sanitized production-shaped
    fixtures.
-   Core/domain/strategy code remains provider-independent.
-   Provider-specific DTOs remain at the provider boundary.
-   Missing provider values remain unavailable/null; never substitute
    zero.
-   Historical market observations should be retained to support
    reproducibility and research.

## Architecture

-   C# is authoritative for calculations and strategy logic.
-   SQLite is V1 persistence.
-   Excel is presentation/configuration/audit, not the strategy engine.
-   Domain must not depend on EF Core, SQLite, ASP.NET, Tradier, HTTP,
    or Excel.
-   MarketData owns provider abstractions and mapping.
-   Infrastructure owns EF Core/SQLite/repositories/cache.
-   Application orchestrates.
-   API remains thin.
-   Statistical calculations may use double; monetary values use
    decimal.

## Reproducibility

-   Explicit AsOfDate is mandatory for historical calculations.
-   No observation after AsOfDate may affect the result.
-   Future data appended after a historical AsOfDate must not alter that
    historical calculation.
-   Missing data is not zero.
-   Missing regime is not neutral.
-   Calculation, configuration, and strategy versions are separate:
    -   IndicatorCalculationVersion
    -   ConfigurationVersion
    -   StrategyVersion
-   Historical results must not silently change under newer
    algorithms/configuration.

## Canonical Phase 3 Indicator Snapshots

-   One canonical row is identified by `Symbol`, `AsOfDate`,
    `IndicatorCalculationVersion`, and `ConfigurationVersion`; a
    database unique constraint/index enforces this identity.
-   V1 persistence atomically inserts a new identity or replaces the
    calculated facts of an existing identity. Retries do not create
    duplicate canonical rows; replacement preserves all identity fields.
-   `CalculatedAt` is the calculation time of the current canonical
    values and updates on successful replacement.
-   Corrected historical source data eligible for `AsOfDate` may change
    the canonical result. Future/ineligible data cannot backfill or
    change a historical recalculation merely because it runs later.
-   Material algorithm and configuration changes use new calculation
    and configuration versions, respectively, and coexist as distinct
    canonical identities rather than overwriting previous versions.
-   V1 does not retain every recalculation as an immutable history.
    Any later calculation audit belongs in a separate model, not
    duplicate canonical snapshot rows. Recorded historical
    recommendations must still remain explainable and unchanged.

## Phase 3 V1 Indicator API

-   Expose one GET calculation/read-through route:
    `/api/indicators/{symbol}` with optional `?asOf=YYYY-MM-DD`.
-   Read-only describes the HTTP method surface; a successful GET
    atomically upserts the derived canonical `IndicatorSnapshot` but must
    not mutate source market observations.
-   Explicit `asOf` is honored exactly and retains historical
    no-look-ahead behavior. Without it, resolve the latest applicable
    persisted underlying trading-observation date for the configured
    provider at or before the request boundary; do not use today's UTC
    calendar date as a substitute. No applicable persisted price
    observation yields `404 Not Found`.
-   The server owns the currently supported
    `IndicatorCalculationVersion` and currently configured
    `IndicatorConfiguration.Version`; client attempts to specify either
    version through this V1 route are invalid, not silently honored or
    ignored. Composition-root configuration/DI supplies both, and the
    response exposes both.
-   Exact version-selectable snapshot retrieval remains an internal
    Application/repository capability. A separate HTTP audit/history
    retrieval route is deferred beyond Phase 3 V1.

## Phase 3 Indicator Math

-   SMA: arithmetic mean; no partial windows.
-   RSI14: Wilder formulation; initial arithmetic average; Wilder
    smoothing thereafter; requires 15 closes.
-   Bollinger: 20 periods, 2 sigma; population standard deviation; %B
    unclamped.
-   MACD: 12/26/9; EMA seeded by first complete-period SMA.
-   ATR14: standard True Range; Wilder smoothing.
-   RV20/RV30: close-to-close log returns; sample standard deviation;
    annualized by sqrt(252); decimal-ratio representation.
-   Market regime: SPY default; SMA50/SMA200/close plus 20-trading-day
    SMA50 direction.
-   Sector regime: same regime algorithm using configured sector
    benchmark.

## Resistance

-   Phase 3 resistance is an objective market-structure observation, not
    a strategy score.
-   Deterministic confirmed swing highs.
-   Default swing window = 3.
-   Historical swing high must have its complete future-side
    confirmation window on/before AsOfDate.
-   Default lookback = 120 trading observations.
-   V1 uses one-dimensional complete-linkage clustering after sorting
    confirmed swing highs by price ascending, then TradingDate ascending.
-   One fixed threshold for the requested AsOfDate is
    `ClusterDistanceAtrMultiplier × ATR14(AsOfDate)`; the default
    multiplier is `0.75`.
-   A candidate joins the current cluster when its price minus the
    cluster minimum is less than or equal to that threshold. The
    boundary is inclusive, and every cluster's maximum-minus-minimum
    range is at most the threshold. Transitive chaining is not allowed.
-   Cluster representative = arithmetic mean of member swing-high
    prices; it does not control cluster membership.
-   Configurable `MinimumResistanceTouches` defaults to `2` and must
    be at least `1`. Only clusters meeting this minimum qualify.
-   Primary resistance = nearest qualified cluster representative
    strictly above current close; equality is not overhead resistance.
-   No qualified level =\> unavailable.
-   Do not fabricate resistance.
-   `ResistanceStrength` was intentionally removed because its formula
    was not defined and it mixed structural evidence with later strategy
    interpretation.
-   Phase 3 instead exposes:
    -   ResistancePrice
    -   DistanceToResistance
    -   DistanceToResistancePercent
    -   ResistanceTouchCount
    -   ResistanceLastTouchDate
    -   ResistanceAgeTradingDays
-   ResistanceAgeTradingDays counts trading observations, not calendar
    days.
-   Phase 4 later determines how resistance evidence contributes to the
    15-point Resistance/Structure CCOS component.

## IV Context

-   Phase 2 owns normalized contract-level option observations.
-   Phase 3 owns provider-independent underlying-level IV context.
-   Phase 3 outputs include IV30, IVRank, and IVPercentile.
-   Phase 4 consumes those values for CCOS.
-   Phase 4 must not independently reconstruct them from provider data.
-   V1 consumes normalized provider-supplied contract IV; it does not
    invert Black-Scholes or another option-pricing model.
-   IV30 targets 30 calendar DTE. For each eligible expiration, use
    the nearest ATM strike (lower strike wins an equal-distance tie),
    within configurable `MaxAtmStrikeDistanceRatio = 0.05` by default.
-   Both call and put IV are required at that strike; expiration IV is
    their arithmetic mean. V1 applies no liquidity filter.
-   An exact 30-DTE expiration supplies IV30 directly. Otherwise,
    linearly interpolate between qualifying expirations bracketing 30
    DTE; never extrapolate.
-   IV Rank and IV Percentile use the most recent 252 valid IV30
    observations through AsOfDate, with at least 126 required. Missing
    observations are ignored, never treated as zero.
-   Historical calculations use only data available through AsOfDate;
    future observations cannot alter past results.

## Phase 4 Boundary and Output

- Phase 4 stops before position sizing.
- Coverage, available shares, existing call exposure, Delta Exposure Ratio,
  maximum coverage, strike laddering, scaling, and recommended contract count
  belong to Phase 5.
- Phase 4 produces a reproducible entry-strategy assessment containing CCOS,
  component explanations, underlying gates, per-contract gates and scores,
  deterministic ranking, and a preferred initial contract/strike when one
  exists.
- Phase 4 does not create the final persisted Recommendation or a SELL action.
- `EntryCandidateExists` means a valid Phase 4 entry candidate exists; it does
  not mean "sell N contracts."
- `Holding.IsEnabled == false` is an Application-level exclusion before
  Phase 4, not a strategy hard gate.

## Phase 4 Reproducible Evaluation Context

- Every evaluation carries both `IndicatorAsOfDate` and
  `EvaluationTimestampUtc`.
- `IndicatorAsOfDate` supplies daily indicator/volatility context;
  `EvaluationTimestampUtc` is the intraday option-observation cutoff.
- These values need not identify the same trading date.
- DTE uses the America/New_York calendar date corresponding to
  `EvaluationTimestampUtc`.
- For each expiration, use the latest complete normalized chain observation
  whose timestamp is at or before the evaluation timestamp.
- Never mix contracts from different chain timestamps within one expiration.
- A newer empty chain supersedes an older populated chain; do not fall back.
- Contract calculations use the selected chain's provider-supplied
  `UnderlyingPrice`; do not substitute daily Close, a separate quote, Last,
  or zero.
- Market-data freshness remains an Application/MarketData responsibility.
- Phase 4 snapshots the actual holding-level settings used instead of rereading
  mutable Holding state for historical replay.
- Historical Phase 4 records preserve the actual Phase 3 facts consumed, not
  merely a pointer to a current canonical indicator row.
- Phase 4 records `IndicatorCalculationVersion`, `ConfigurationVersion`,
  and `StrategyVersion`.
- V1 uses one shared `ConfigurationVersion` identity across Phase 3 and
  Phase 4.

## Phase 4 Earnings Gate

- V1 supports only earnings as a corporate-event entry gate.
- Dividend, ex-dividend, and generic material-event entry rules are deferred.
- Reject an individual-stock contract when
  `NextEarningsDate <= Expiration`, including same-day earnings.
- Failure code: `EARNINGS_BEFORE_EXPIRATION`.
- Missing required individual-stock earnings data produces
  `INSUFFICIENT_DATA`.
- ETF earnings status is `NotApplicable`.
- Earnings is a contract-level gate, not a duplicate CCOS gate.
- Persist the actual EarningsContext status/date used.

## Phase 4 Breakout Veto

V1 breakout veto is exactly:

```text
BollingerPercentB > 1.15
AND RSI14 >= 75
AND MACDHistogram > 0
```

- Failure code: `BREAKOUT_VETO`.
- CCOS remains calculable/retained for explanation when its inputs are
  available.
- A breakout veto prevents entry; contract scoring may stop.
- No additional breakout indicator, volume rule, ATR rule, bandwidth slope,
  acceleration rule, consecutive-close rule, or resistance-crossing rule is
  part of V1.

## Phase 4 Liquidity Eligibility

Liquidity is contract-level only.

V1 requires:

```text
Bid > 0
Ask > Bid
OpenInterest >= 100
BidAskSpreadPercent <= 20%
```

where:

```text
Mid = (Bid + Ask) / 2
BidAskSpreadPercent = (Ask - Bid) / Mid
```

- Present values that violate the rule produce
  `INSUFFICIENT_LIQUIDITY`.
- Missing Bid, Ask, or OpenInterest produces `INSUFFICIENT_DATA`.
- Daily option volume is not a V1 hard-gate input and does not contribute to
  the V1 Liquidity Score.
- `MinimumOpenInterest = 100` and
  `MaximumBidAskSpreadPercent = 0.20` are configurable defaults.

## Global Missing-Data Policy

- Missing required input never becomes zero, neutral, a passing gate, or an
  estimated substitute.
- If any required CCOS component cannot be calculated, overall CCOS is
  unavailable. Weights are not renormalized.
- If any required Contract Score component cannot be calculated, that
  contract's Contract Score is unavailable and it is excluded from ranking.
- `NotApplicable` is distinct from `Unavailable`.
- `INSUFFICIENT_DATA` records the specific missing inputs.
- A missing gate input does not become the corresponding threshold failure.
- One bad contract does not poison other independent contracts.

## CCOS Boundary Semantics

- Interior shared boundaries belong to the band beginning at that boundary.
- Explicit terminal `<` and `>` endpoints remain strict.
- Every threshold requires below/at/above deterministic tests.

## CCOS

Weights:

- Implied volatility: 25
- RSI: 15
- Bollinger: 15
- Trend/Momentum: 15
- Resistance/Structure: 15
- Market/Sector Regime: 15

Default open threshold: CCOS >= 70, while an evaluation uses the snapshotted
`Holding.MinimumCcos`.

Classifications remain:

```text
0–39    NO TRADE
40–54   WEAK
55–69   WATCH
70–79   SELL CANDIDATE
80–89   STRONG
90–100  EXCEPTIONAL
```

Classification is descriptive; threshold/gate eligibility remains explicit.

### CCOS Volatility — 25

```text
VolatilityScore =
    IVPercentileScore
    + IV30ToRV30Score
```

IV Percentile:

```text
<20             0
>=20 <30        3
>=30 <40        6
>=40 <50        9
>=50 <60       11
>=60 <=70      13
>70            15
```

IV30/RV30:

```text
<.90            0
>=.90 <1.00     2
>=1.00 <1.10    4
>=1.10 <1.20    6
>=1.20 <=1.35   8
>1.35          10
```

- IV30, IVPercentile, and RV30 are required.
- `RV30 <= 0` is invalid/unavailable.
- IVRank is retained as context but does not contribute to V1 CCOS.

### CCOS RSI — 15

```text
<40             0
>=40 <50        2
>=50 <55        5
>=55 <60        8
>=60 <65       11
>=65 <70       15
>=70 <75       13
>=75 <=80       9
>80             5
```

RSI outside 0–100 is invalid/unavailable.

### CCOS Bollinger — 15

%B:

```text
<.40             0
>=.40 <.60       2
>=.60 <.75       5
>=.75 <.90       8
>=.90 <1.05     10
>=1.05 <=1.15    7
>1.15             3
```

If both %B and bandwidth are available, bandwidth contributes the remaining
five points:

```text
BollingerScore = PercentBScore + 5
```

V1 does not calculate bandwidth expansion rate or absolute bandwidth bands.

### CCOS Trend/Momentum — 15

Award five points for each:

```text
SMA20 > SMA50
SMA50 > SMA200
MACDHistogram > 0
```

Equality receives zero for that check. Missing any required value makes the
component unavailable.

### CCOS Resistance/Structure — 15

Phase 3 shall distinguish:

```text
NoQualifiedResistance
InsufficientData
```

- `NoQualifiedResistance` is a valid state worth 0/15.
- `InsufficientData` makes the component unavailable.

Qualified resistance scoring:

Distance, max 5:

```text
<=2%       5
>2% <=5%   3
>5%        1
```

Touches, max 5:

```text
2      1
3      3
>=4    5
```

Recency, max 5:

```text
<=20 trading days   5
21–60               3
>60                 1
```

Use Phase 3 as-of facts. Do not recompute resistance distance from intraday
chain UnderlyingPrice.

### CCOS Market/Sector Regime — 15

Market, max 8:

```text
NEUTRAL  8
BULLISH  4
BEARISH  0
```

Sector, max 7:

```text
NEUTRAL  7
BULLISH  3
BEARISH  0
```

Either regime unavailable makes the component unavailable.

## Candidate Contract Universe and Derived Metrics

- EvaluationDate is the America/New_York calendar date corresponding to
  `EvaluationTimestampUtc`.
- DTE is calendar days from EvaluationDate to ExpirationDate.
- Application should retrieve expirations in the configured 14–45 DTE search
  window; Strategy validates DTE defensively.
- V1 candidates are calls only.
- After puts are discarded, do not silently prefilter calls before gate
  evaluation; rejected calls remain explainable.
- `ReferencePremium = Bid`.

Approved V1 metrics:

```text
Mid = (Bid + Ask) / 2
BidAskSpreadPercent = (Ask - Bid) / Mid
StrikeDistance = Strike - UnderlyingPrice
OTMPercent = (Strike - UnderlyingPrice) / UnderlyingPrice
PremiumYield = ReferencePremium / UnderlyingPrice
DailyPremiumYield = PremiumYield / DTE
AnnualizedPremiumYield = DailyPremiumYield * 365
```

Deferred from Phase 4 V1:

```text
DeltaAdjustedYield
ExpectedMove
ExpectedMoveRatio
StrikeVsResistance
```

## Contract Hard Gates

True contract hard gates are:

- DTE 14–45 inclusive: `DTE_OUTSIDE_RANGE`.
- Strict OTM strike: `Strike > UnderlyingPrice`;
  `STRIKE_NOT_OTM`.
- Effective maximum delta:
  `min(global max, Holding.MaximumInitialDelta, high-tax cap when High)`;
  `DELTA_EXCEEDS_MAXIMUM`.
- Earnings on/before expiration: `EARNINGS_BEFORE_EXPIRATION`.
- Liquidity rule above: `INSUFFICIENT_LIQUIDITY`.
- `ReferencePremium >= Holding.MinimumPremium`;
  `PREMIUM_BELOW_MINIMUM`.
- `AnnualizedPremiumYield >= Holding.MinimumAnnualizedYield`;
  `ANNUALIZED_YIELD_BELOW_MINIMUM`.

Defaults:

```text
GlobalMaximumInitialDelta = .25
HighTaxMaximumDelta = .20
```

- `TaxSensitivity.High` maps to the high-tax cap.
- Call Delta must be finite and within 0–1. Invalid/missing Delta is
  `INSUFFICIENT_DATA`; do not use absolute value.
- Preferred delta min/max affect scoring only, not hard gating.
- `MinimumCcos` and `MinimumContractScore` are score thresholds, not hard
  gates.
- Evaluate and retain every determinable gate failure rather than stopping at
  the first.
- Coverage/DER/share-count constraints remain Phase 5.

## Contract Score

Weights:

- Delta: 25
- Strike safety: 20
- Premium efficiency: 20
- DTE efficiency: 10
- IV/volatility edge: 10
- Liquidity: 10
- Theta efficiency: 5

Classifications:

```text
<60      REJECT
60–69    WEAK
70–79    ACCEPTABLE
80–89    GOOD
90–100   EXCELLENT
```

Actual entry acceptability uses `Holding.MinimumContractScore`.

### Delta — 25

```text
EffectivePreferredDeltaMaximum =
min(Holding.PreferredDeltaMaximum, EffectiveMaximumDelta)

Delta < PreferredDeltaMinimum                              20
preferred range inclusive                                 25
above preferred but <= EffectiveMaximumDelta              10
```

A preferred minimum above the effective preferred maximum is invalid
configuration.

### Strike Safety — 20

```text
0% < OTM <1%      0
>=1% <2%          4
>=2% <3%          8
>=3% <4%         12
>=4% <6%         15
>=6% <=8%        18
>8%              20
```

Resistance does not contribute to V1 Contract Score.

### Premium Efficiency — 20

```text
PremiumEfficiencyRatio =
AnnualizedPremiumYield / Holding.MinimumAnnualizedYield

>=1.00 <1.10    0
>=1.10 <1.25    5
>=1.25 <1.50   10
>=1.50 <2.00   15
>=2.00         20
```

`MinimumAnnualizedYield` must be greater than zero. `MinimumPremium`
remains a hard dollar floor only.

### DTE Efficiency — 10

```text
14–20   5
21–35  10
36–45   8
```

No holding-specific preferred-DTE setting is added in V1.

### IV/Volatility Edge — 10

```text
ContractVolatilityEdge =
Contract.ImpliedVolatility / RV30

<.90            0
>=.90 <1.00     2
>=1.00 <1.10    4
>=1.10 <1.20    6
>=1.20 <=1.35   8
>1.35          10
```

Contract IV and RV30 must be finite and strictly positive. Phase 4 does not
reconstruct IV30. IV30 and IVRank do not contribute to this Contract Score
component.

### Liquidity — 10

```text
LiquidityScore = SpreadScore + OpenInterestScore
```

Spread score:

```text
<=5%          5
>5% <=10%     4
>10% <=15%    2
>15% <=20%    1
```

Open-interest score:

```text
100–249        1
250–499        2
500–999        3
1,000–1,999    4
>=2,000        5
```

Daily option volume has no V1 score effect.

### Theta Efficiency — 5

```text
ThetaEfficiencyRatio =
(-Theta) / ReferencePremium

<1%          0
>=1% <2%     1
>=2% <3%     2
>=3% <4%     3
>=4% <5%     4
>=5%         5
```

Theta must be finite and strictly negative. Do not use `abs(Theta)`.

## Contract Ranking

Rank every hard-gate-passing contract with a complete score, including those
below `MinimumContractScore`.

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

Rejected or insufficient contracts are not ranked.

Contract ranking may still be calculated when CCOS is below its entry
threshold for explainability.

## Preferred Initial Contract / Strike

There is no separate strike optimizer.

```text
PreferredInitialContract =
highest-ranked EntryAcceptable contract

PreferredInitialStrike =
PreferredInitialContract.Strike
```

`EntryCandidateExists=true` requires:

- CCOS available.
- no underlying veto.
- CCOS >= `Holding.MinimumCcos`.
- at least one hard-gate-passing contract with complete score.
- Contract Score >= `Holding.MinimumContractScore`.

Overall Phase 4 dispositions:

```text
ENTRY_CANDIDATE
CCOS_BELOW_MINIMUM
BREAKOUT_VETO
NO_ACCEPTABLE_CONTRACT
INSUFFICIENT_DATA
```

Phase 4 terminology is PreferredInitialContract/PreferredInitialStrike; final
RecommendedContract(s)/SELL semantics remain downstream.

## Phase 4 Persistence

- Persist an immutable `EntryStrategyEvaluation` with a permanent ID.
- It is not a final Recommendation.
- Preserve actual HoldingContext, Phase 3 facts/statuses, EarningsContext,
  normalized option observations, resolved configuration, all versions, scores,
  gates, ranking, missing inputs, explanations, and preferred-contract
  disposition.
- Historical evaluations are append-only and never canonically replaced.
- A hybrid relational + structured JSON schema is acceptable if no required
  audit/reproducibility information is lost.
- Later Holding edits, Phase 3 recalculation, market observations,
  configuration changes, or strategy changes must not alter historical
  evaluation retrieval.

## Phase 4 API

Create:

```text
POST /api/holdings/{holdingId}/entry-evaluations
GET /api/entry-evaluations/{id}
GET /api/holdings/{holdingId}/entry-evaluations
```

- POST calculates "now", persists one immutable evaluation, and returns 201.
- GET by ID is passive historical retrieval with no refresh/recalculation.
- Holding history is lightweight and newest first.
- Public V1 does not expose arbitrary historical timestamps/as-of/version
  selectors.
- Missing holding -> 404.
- Disabled holding -> 409 `HOLDING_DISABLED`.
- Strategy outcomes such as insufficient data or no acceptable contract are
  successful persisted evaluations, not HTTP failures.
- No PUT/PATCH/DELETE for EntryStrategyEvaluation.

## Phase 4 Versioning

Three independent identities remain authoritative:

```text
IndicatorCalculationVersion
ConfigurationVersion
StrategyVersion
```

- IndicatorCalculationVersion owns Phase 3 fact-calculation algorithms.
- ConfigurationVersion owns tunable numeric values: weights, thresholds,
  score bands, gate limits, DTE/liquidity settings, etc.
- StrategyVersion owns formulas, component structure, gate semantics,
  missing-data policy, ranking logic, and decision flow.
- Holding-specific settings are snapshotted HoldingContext data, not
  ConfigurationVersion values.
- Every evaluation persists the complete resolved configuration values it used
  in addition to ConfigurationVersion.
- V1 keeps one shared ConfigurationVersion identity across Phase 3 and Phase 4.

## Phase 4 Explainability

Use structured score components and separate gates.

Conceptually:

```text
ScoreComponentResult
    Code
    Name
    Status
    Score
    MaximumScore
    Inputs
    Explanation

GateResult
    Code
    Status:
        Passed
        Failed
        Unavailable
        NotApplicable
    Inputs
    Explanation
```

- Preserve all evaluated gate outcomes, not only failures.
- `INSUFFICIENT_DATA` carries stable machine-readable MissingInputs.
- Missing values do not masquerade as threshold failures.
- CCOS and Contract Score explicitly expose status, classification, configured
  minimum, and whether the minimum was met.
- Stable component/gate/reason codes are API contracts; human explanation text
  is not parsed for logic.
- UI/API consumers do not reconstruct score, gates, ranking, eligibility, or
  disposition.

## Phase 4 Acceptance and Work Packets

Approved golden scenarios:

```text
StrongEntry
BreakoutVeto
InsufficientData
HighAssignmentRisk
TaxSensitivePosition
IlliquidContract
EarningsBeforeExpiration
NoAcceptableContract
```

Every numeric boundary receives deterministic below/at/above tests.

Implementation packets:

```text
4A — strategy/config/result foundations + ResistanceUnavailableReason refinement
4B — pure CCOS and underlying eligibility
4C — pure contract gates/scoring/ranking/selection
4D — Application/MarketData orchestration and simple earnings input
4E — immutable persistence
4F — API and merge-gate validation
```

No packet may invent new formulas, thresholds, trading rules, or missing-data
fallbacks.

See `docs/acceptance/PHASE-4-ENTRY-STRATEGY.md` for the complete acceptance
matrix.

## Phase Boundaries

Phase 3 calculates facts/classifications.

Phase 4 interprets those facts and normalized option observations for entry
timing and contract selection.

Phase 5 owns sizing and coverage allocation.

Phase 3 must not calculate CCOS, Contract Score, trade eligibility, or entry
selection.

Phase 4 must not calculate recommended contract count, coverage/DER sizing,
DRS, roll recommendations, or execute trades.

## Explicitly Deferred

- Dividend/ex-dividend/generic material-event Phase 4 entry rules.
- DeltaAdjustedYield.
- ExpectedMove and ExpectedMoveRatio.
- StrikeVsResistance as a Contract Score input.
- Bollinger bandwidth expansion-rate scoring.
- Additional breakout indicators/rules.
- Holding-specific preferred DTE.
- Arbitrary historical replay/version selection through the public Phase 4 V1
  API.
- Position sizing, strike laddering, DRS, roll logic, and final SELL
  Recommendation semantics.


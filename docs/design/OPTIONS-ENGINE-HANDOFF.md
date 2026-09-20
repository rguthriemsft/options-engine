# Options Engine / CCDSS --- New Chat Handoff

## Purpose

This document is a durable handoff for continuing design and
implementation of the Options Engine / Covered Call Decision Support
System (CCDSS) in a fresh ChatGPT conversation.

Repository: `rguthriemsft/options-engine`

Current working branch: `phase5`

At the original design handoff, `phase3` was 3 commits ahead of `main`
and 0 behind. At that time, the only branch changes relative to `main`
were:

-   `SPECIFICATION.md`
-   `docs/acceptance/PHASE-3-INDICATORS.md`

The repository files are authoritative. This handoff explains the
reasoning, decisions, boundaries, unresolved questions, and recommended
next step so a new conversation does not need to reconstruct them from
chat history.

## Product Objective

Build a Covered Call Decision Support System whose primary objective is
sustainable covered-call income while maintaining a very strong
preference against assignment of appreciated long-term holdings.

Optimization hierarchy:

1.  Preserve shares / minimize assignment.
2.  Generate sustainable income.
3.  Maximize risk-adjusted premium.
4.  Minimize unnecessary rolling and transaction costs.
5.  Measure whether the strategy adds value versus buy-and-hold.

Assignment risk is a hard constraint rather than merely another score
component.

Initial covered-call sale horizon: 14--45 DTE.

The system should support rolling and must provide a defense plan with
every sell recommendation.

## V1 Architecture

``` text
Tradier Production API
        |
        v
C# / .NET Market Data Layer
        |
        v
Normalized Provider-Independent Market Data
        |
        +--------------------+
        |                    |
        v                    v
      SQLite          Indicator / Market Context Engine
                              |
                              v
                        Strategy Engines
                              |
                              v
                         Application/API
                              |
                              v
                         Excel Dashboard
```

C# owns calculations and strategy decisions. Excel is a presentation,
configuration, audit, and analysis surface.

Tradier is the V1 market-data provider, but provider-specific types must
remain at the provider boundary.

Fidelity and Schwab are initial execution venues. V1 execution is
manual.

Automatic trading is out of scope.

## Provider Policy

Tradier V1 uses the PRODUCTION API only.

Tradier sandbox is explicitly out of scope.

Automated tests must not require a Tradier token or internet access.
External API behavior is tested with mocked HTTP and sanitized
production-shaped fixtures.

Provider abstraction must remain intact for future providers such as
Schwab or Massive.

## Core Engineering Principles

-   `SPECIFICATION.md` is authoritative.
-   `AGENTS.md` contains repository engineering guidance.
-   No strategy rule should be silently invented.
-   Domain remains independent from EF Core, SQLite, ASP.NET, Tradier,
    HTTP, and Excel.
-   Strategy/calculation logic should be pure where practical.
-   MarketData owns provider abstractions and provider DTO mapping.
-   Infrastructure owns EF Core, SQLite, repositories, and caching.
-   Application orchestrates.
-   API is thin.
-   Provider DTOs do not escape the provider boundary.
-   Money uses `decimal`; statistical/quantitative calculations may use
    `double`.
-   Missing data is not zero.
-   Critical missing data should remain unavailable or explicitly
    `INSUFFICIENT_DATA`.
-   Configuration is strongly typed and versioned.
-   Historical observations and recommendations must be reproducible.
-   Tests are deterministic and do not depend on live external systems.
-   No automatic trading in V1.

## Strategy Pipeline

``` text
Holding
  -> Hard Gates
  -> CCOS
  -> Contract Score
  -> Position Sizing
  -> Recommendation
  -> User Executes
  -> Campaign
  -> DRS
  -> Roll Engine / RQS when required
  -> Campaign Accounting
  -> Performance / Research
```

Separate concepts must remain separate:

-   CCOS: whether the underlying is attractive for selling calls now.
-   Contract Score: which eligible option contract is best.
-   Position Sizing: how much of the holding to cover.
-   DRS: urgency of defensive action on an existing short call.
-   Roll Engine / RQS: quality of defensive roll candidates.

## Important Strategy Decisions Already Locked

### CCOS

Range: 0--100.

Weights:

  Component                  Weight
  ------------------------ --------
  Implied volatility             25
  RSI                            15
  Bollinger Bands                15
  Trend / Momentum               15
  Resistance / Structure         15
  Market / Sector Regime         15

Default opening threshold: `CCOS >= 70`.

### Contract Score

Weights:

  Component                Weight
  ---------------------- --------
  Delta                        25
  Strike safety                20
  Premium efficiency           20
  DTE efficiency               10
  IV / volatility edge         10
  Liquidity                    10
  Theta efficiency              5

Default initial entry requires both:

``` text
CCOS >= 70
ContractScore >= 80
```

Initial hard constraints include 14--45 DTE and delta \<= .25; highly
tax-sensitive positions default to maximum delta .20.

### Position Sizing

Position sizing is not equivalent to covering 100% of shares.

It considers:

-   CCOS base coverage
-   Assignment Sensitivity
-   Contract Quality
-   Concentration
-   Holding maximum coverage
-   Available shares
-   Existing short-call exposure
-   Delta Exposure Ratio

``` text
DER =
SUM(Contracts × Delta × 100)
/
SharesOwned
```

### Defense / Rolling

Profit-taking is separate from DRS.

Initial profit-taking concepts:

``` text
50% captured -> Monitor
70% captured -> Close candidate
80%+ captured -> Strong close candidate
```

Initial delta defense concepts:

``` text
.25 -> warning
.30 -> roll evaluation
.40 -> hard defense
```

Preferred replacement calls generally target lower delta and more strike
safety.

Roll Quality Score is separate from DRS.

## Phase Status

### Phase 1 --- Complete

Repository/foundation:

-   solution/project structure
-   domain foundations
-   application boundaries
-   persistence foundations
-   SQLite
-   initial EF migration
-   logging
-   configuration foundations
-   testing infrastructure
-   health endpoint
-   repository engineering guidance

### Phase 2 --- Complete and Merged

Tradier market data:

-   production authentication
-   provider-independent market-data abstraction
-   quotes
-   historical daily prices
-   expirations
-   option chains
-   Greeks / IV mapping
-   rate-limit handling
-   caching
-   historical persistence
-   append-only option snapshots
-   mocked provider fixtures
-   market-data API

Important Phase 2 implementation lessons:

-   Preserve EF migration/snapshot lineage; do not regenerate migrations
    against a corrupted snapshot.
-   Provider identity comes from the provider abstraction, not a
    hard-coded Tradier value.
-   SQLite `DateTimeOffset` ordering requires care.
-   Tradier rate-limit expiry is Unix milliseconds.
-   Missing provider values remain unavailable/null, never zero.
-   Live production API verification is not part of normal automated
    testing.

### Phase 3 --- Complete and Merged

Phase 3 is complete on `main`.

Authoritative Phase 3 documents:

- `SPECIFICATION.md`
- `docs/acceptance/PHASE-3-INDICATORS.md`
- `docs/design/OPTIONS-ENGINE-DESIGN-DECISIONS.md`

Phase 3 owns derived market facts and classifications, not strategy scoring.

Implemented Phase 3 capabilities include:

- SMA20 / SMA50 / SMA200
- RSI14 using Wilder smoothing
- Bollinger Bands / %B / bandwidth
- MACD 12/26/9
- ATR14 / ATR percent using Wilder smoothing
- RV20 / RV30
- resistance detection
- market regime
- sector regime
- IV30 / IVRank / IVPercentile
- historical as-of calculation
- look-ahead-bias protection
- calculation/configuration versioning
- canonical indicator persistence
- read-through indicator API

The V1 indicator route is `GET /api/indicators/{symbol}` with optional
`?asOf=YYYY-MM-DD`.

Phase 3 explicitly does NOT own CCOS, Contract Score, trade eligibility,
contract ranking, position sizing, DRS, Roll Engine / RQS, recommendations,
or trade execution.

### Phase 4 --- Complete and Merged

Phase 4 is complete, its merge gate passed, and it is merged into `main`.

Authoritative Phase 4 documents:

- `SPECIFICATION.md`
- `docs/acceptance/PHASE-4-ENTRY-STRATEGY.md`
- `docs/design/OPTIONS-ENGINE-DESIGN-DECISIONS.md`

Phase 4 owns:

- CCOS and all six deterministic component formulas;
- breakout veto;
- contract candidate derivation and hard gates;
- all seven Contract Score component formulas;
- deterministic contract ranking/tie-breaking;
- preferred initial contract/strike selection;
- reproducible evaluation orchestration;
- immutable EntryStrategyEvaluation persistence;
- Phase 4 API.

Phase 4 stops before position sizing. Coverage, available-share constraints,
existing-call exposure, DER, maximum coverage, strike laddering, scaling, and
recommended contract count belong to Phase 5.

Phase 4A, 4B, 4C, 4D, 4E, and 4F are complete. The Phase 4 merge gate is
passed. Phase 4 stops before position sizing.

## Phase 3 Mathematical Decisions Already Locked

### As-Of Semantics

For `AsOfDate = D`, only observations with `TradingDate <= D` may be
consumed.

Appending future observations must not change a result calculated as of
D.

This applies to standard indicators, resistance, market regime, and
sector regime.

### Version Separation

Three independent version concepts:

``` text
IndicatorCalculationVersion
ConfigurationVersion
StrategyVersion
```

-   Calculation version = mathematical/algorithmic implementation.
-   Configuration version = parameter values.
-   Strategy version = recommendation/scoring logic consuming calculated
    facts.

### SMA

Arithmetic mean.

No partial windows.

### RSI

Wilder RSI.

Initial average gain/loss = arithmetic mean of first N changes.

Subsequent values use Wilder smoothing.

N-period RSI requires N+1 closes.

### Bollinger Bands

Default 20 periods and 2 standard deviations.

Use population standard deviation for the rolling price window.

%B is not clamped.

Zero-width bands =\> %B unavailable.

Bandwidth:

``` text
(Upper - Lower) / Middle
```

### MACD

12/26/9.

EMA multiplier:

``` text
2 / (period + 1)
```

EMA seed = arithmetic SMA of the first complete period.

MACD may be available while signal/histogram remain unavailable during
warm-up.

### ATR

True Range:

``` text
max(
    High[t] - Low[t],
    abs(High[t] - Close[t-1]),
    abs(Low[t] - Close[t-1])
)
```

ATR14 uses Wilder smoothing.

ATR percent:

``` text
ATR / Close
```

### Realized Volatility

Daily log return:

``` text
ln(Close[t] / Close[t-1])
```

RV(N):

``` text
sample standard deviation of last N log returns
× sqrt(252)
```

Default RV20 and RV30.

N returns require N+1 closes.

Representation is a decimal ratio: `0.25 = 25%`.

### Resistance

Use deterministic confirmed swing-high clustering.

Default swing window = 3.

Candidate:

``` text
High[t] > previous 3 highs
AND
High[t] >= next 3 highs
```

The complete confirmation window must exist on or before the AsOfDate.

Default lookback = 120 trading observations.

Default clustering distance = `0.75 × ATR14`.

V1 uses price-ascending, then TradingDate-ascending, one-dimensional
complete-linkage clustering. A candidate joins the current cluster
only when its price minus the cluster minimum is at most the fixed
`0.75 × ATR14(AsOfDate)` threshold (multiplier configurable). This
prevents transitive chaining. The threshold boundary is inclusive.

Cluster representative = arithmetic mean of clustered swing-high prices.

`MinimumResistanceTouches` defaults to 2 and is configurable (minimum
1). Primary resistance = nearest qualified cluster representative
strictly above current close.

Do not fabricate resistance when none exists.

A prior proposal for `ResistanceStrength` was deliberately removed
because it was underspecified and risked mixing Phase 3 market facts
with Phase 4 scoring.

Phase 3 should expose objective evidence instead:

``` text
ResistancePrice
DistanceToResistance
DistanceToResistancePercent
ResistanceTouchCount
ResistanceLastTouchDate
ResistanceAgeTradingDays
```

`ResistanceAgeTradingDays` is based on actual trading observations, not
calendar-day subtraction.

Phase 4 may later translate this evidence into the Resistance/Structure
CCOS component.

### Market Regime

Default benchmark = SPY, configurable.

Classifications:

``` text
BULLISH
NEUTRAL
BEARISH
INSUFFICIENT_DATA
```

Default bullish:

``` text
Close > SMA200
AND SMA50 > SMA200
AND SMA50 rising
```

Default bearish:

``` text
Close < SMA200
AND SMA50 < SMA200
AND SMA50 falling
```

Otherwise neutral.

SMA50 direction uses a default 20-trading-observation lookback.

### Sector Regime

Same algorithm as market regime but uses a configured sector benchmark.

Potential benchmark ETFs include XLK, XLF, XLE, XLV, XLY, XLP, XLI, XLB,
XLU, XLRE, XLC.

The mapping belongs to configuration/application context, not the
indicator calculator.

No mapping =\> unavailable, not neutral.

## IV Context Decision

Phase 3C implements provider-independent IV30, IVRank, and IVPercentile
from normalized provider-supplied contract IV. The approved V1 formulas,
eligibility rules, missing-data treatment, and historical AsOfDate rules
are in `SPECIFICATION.md` §12.10. Phase 4 consumes these outputs for
CCOS; it must not reconstruct them from provider-specific data.

## Phase 4 Design Status

The previously deferred Phase 4 scoring gaps are resolved.

Do not reopen or invent alternatives for:

- Trend/Momentum CCOS scoring;
- Bollinger bandwidth CCOS treatment;
- Resistance/Structure CCOS scoring;
- Market/Sector regime scoring;
- contract hard gates;
- Contract Score component formulas;
- ranking/tie-breaking;
- Phase 4 persistence/API/versioning semantics.

The locked rules are in `SPECIFICATION.md` and
`docs/design/OPTIONS-ENGINE-DESIGN-DECISIONS.md`.

## Phase 4 V1 Earnings Input Decision

Phase 4 V1 retains the earnings hard gate but uses the provider-independent
`IEarningsDateSource` with validated configuration-backed current-evaluation dates.
Do not implement an undocumented Tradier corporate-calendar/fundamentals request.
This source does not recreate historical event knowledge; Phase 4E persists the
actual `EarningsContext` used. A provider-backed adapter is deferred until an
authoritative contract or sanitized captured fixture is available.


### Phase 5 --- Complete and Merged

Phase 5 Position Sizing is complete and merged into `main`.

Authoritative Phase 5 documents:

- `SPECIFICATION.md`
- `docs/design/PHASE-5-POSITION-SIZING-DESIGN.md`
- `docs/design/OPTIONS-ENGINE-DESIGN-DECISIONS.md`
- `docs/acceptance/PHASE-5-POSITION-SIZING.md`

Implemented Phase 5 boundaries include:

- separate immutable `PositionSizingEvaluation`;
- no final Recommendation creation;
- one preferred initial contract sized per evaluation;
- authoritative Holding shares;
- physical-capacity and existing-open-call constraints;
- approved CCOS/Assignment Sensitivity/Contract Quality/concentration sizing;
- DER constraints;
- minimal current-state `OpenShortCallPositionEntity`;
- immutable persistence and API;
- no DRS, Roll Engine, campaign ledger, or automatic execution.

The Phase 5 current-state open-call persistence has a stable database identity:

```text
OpenShortCallPositionId
HoldingId
OptionSymbol
Contracts
Strike
Expiration
```

Phase 6 extends that narrow current-state record only with the additional opening economics/timestamp required for defense analysis; this remains distinct from the Phase 7 transaction ledger.

### Phase 6 --- Design and Acceptance Ready

Phase 6 Defense and Roll design is approved and reconciled.

Authoritative Phase 6 documents:

- `SPECIFICATION.md`
- `docs/design/PHASE-6-DEFENSE-ROLL-DESIGN.md`
- `docs/acceptance/PHASE-6-DEFENSE-ROLL.md`
- `docs/design/OPTIONS-ENGINE-DESIGN-DECISIONS.md`
- this handoff

Core Phase 6 V1 decisions:

- one DefenseEvaluation per current open short-call position;
- immutable `DefenseEvaluation` plus conditional immutable `RollEvaluation`;
- no final Recommendation lifecycle or trade execution;
- authoritative opening premium is actual/weighted-average gross executed credit when known;
- BTC reference price is existing-call Ask;
- profit-taking thresholds 50% / 70% / 80%;
- four-component DRS at 40/25/15/20;
- call Delta remains normalized `[0,1]` and is never repaired with `abs()`;
- five V1 hard-defense triggers;
- breakout and dividend/early-assignment triggers deferred;
- Roll Engine activates on hard trigger or DRS >=50;
- replacement search is 21–60 DTE, with 21–45 preferred;
- replacement must expire later, raise strike, remain strictly OTM, and lower Delta;
- normal replacement Delta max .25; high-tax max .20; preferred .12–.18;
- reuse Phase 4 liquidity hard gate and 0–10 Liquidity Score, not Phase 4 overall Contract Score/entry eligibility;
- reject only newly introduced known earnings crossings;
- conservative roll pricing uses BTC Ask and replacement STO Bid;
- replacement contract count equals current contract count;
- configurable absolute maximum debit is the only Phase 6 V1 debit hard limit;
- campaign-relative debit and assignment-tax-dollar exceptions deferred;
- projected DRS must improve current DRS and remain below 40;
- RQS weights 30/20/20/15/10/5;
- deterministic candidate states: Rejected / Rankable / InsufficientData;
- deterministic tie-break ranking;
- dispositions: NO_ACTION, MONITOR, PROFIT_CLOSE, DEFENSE_REVIEW, ROLL, CLOSE_WAIT;
- CurrentCCOS >=55 selects ROLL when a Rankable candidate exists; lower CCOS selects CLOSE_WAIT;
- unavailable candidate-critical data leads to DEFENSE_REVIEW rather than fabricated rejection;
- coherent existing-call observations and per-expiration complete chain selection;
- no hidden Phase 6 strategy freshness threshold;
- explicit DefenseStrategyVersion and RollStrategyVersion;
- Phase 6 is suitable for daily invocation but does not add a scheduler/background worker.

Approved Phase 6 packet sequence:

```text
6A — Defense foundations and current-position contract
6B — Profit taking, DRS, and hard triggers
6C — Roll candidate universe, hard gates, and economics
6D — RQS, ranking, and DefenseDisposition
6E — Application orchestration and current-context assembly
6F — Immutable persistence
6G — API and merge-gate validation
```

## Current Phase Status

Phase 1 through Phase 5 are complete and merged into `main`.

Phase 6 design is complete on the `phase6` branch. No Phase 6 implementation code should be written outside the approved 6A–6G packet boundaries.

Phase 3 canonical indicator snapshots remain replaceable.

Phase 4 EntryStrategyEvaluation and Phase 5 PositionSizingEvaluation history remain append-only immutable records.

Phase 6 will add separate append-only DefenseEvaluation and RollEvaluation history without rewriting Phase 4/5 artifacts.

## Recommended Next Conversation

Begin **Phase 6A — Defense foundations and current-position contract**.

Read, in order:

1. `AGENTS.md`
2. `SPECIFICATION.md`, especially Sections 16.3, 20, 36–52, 61–63, 70, 74, 76, and 81
3. `docs/design/PHASE-6-DEFENSE-ROLL-DESIGN.md`
4. `docs/acceptance/PHASE-6-DEFENSE-ROLL.md`
5. `docs/design/OPTIONS-ENGINE-DESIGN-DECISIONS.md`
6. `docs/design/PHASE-5-POSITION-SIZING-DESIGN.md`
7. `docs/acceptance/PHASE-5-POSITION-SIZING.md`

Before implementation:

- inspect the existing `OpenShortCallPositionEntity` and repository projection;
- preserve `OpenShortCallPositionId` across new Phase 6 boundaries;
- do not alter existing Phase 5 migration history;
- preserve Phase 4/5 immutable evaluation contracts;
- keep all quantitative formulas in Strategy;
- keep provider/persistence/current-state assembly in Application/Infrastructure;
- do not introduce Phase 7 transactions/campaigns;
- do not add breakout/dividend/expected-move rules;
- do not invent a Phase 6 freshness threshold;
- do not import Phase 4 entry eligibility wholesale into Roll analysis;
- do not create execution side effects.

Implementation should proceed only through the approved Phase 6 packets and their acceptance criteria.

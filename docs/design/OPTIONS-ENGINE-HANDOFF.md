# Options Engine / CCDSS --- New Chat Handoff

## Purpose

This document is a durable handoff for continuing design and
implementation of the Options Engine / Covered Call Decision Support
System (CCDSS) in a fresh ChatGPT conversation.

Repository: `rguthriemsft/options-engine`

Current working branch: `phase3`

As of the handoff, `phase3` is 3 commits ahead of `main` and 0 behind.
The only branch changes relative to `main` are:

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

### Phase 3 --- Design in Progress

Authoritative documents on branch:

-   `SPECIFICATION.md`
-   `docs/acceptance/PHASE-3-INDICATORS.md`

Phase 3 owns derived market facts and classifications, not strategy
scoring.

Currently designed Phase 3 calculations:

-   SMA20 / SMA50 / SMA200
-   RSI14 using Wilder smoothing
-   Bollinger Bands / %B / bandwidth
-   MACD 12/26/9
-   ATR14 / ATR percent using Wilder smoothing
-   RV20 / RV30
-   resistance detection
-   market regime
-   sector regime
-   IV30 / IVRank / IVPercentile ownership, with detailed methodology
    intentionally still unresolved
-   historical as-of calculation
-   look-ahead-bias protection
-   calculation versioning
-   indicator persistence
-   read-only indicator API

Phase 3 explicitly does NOT own:

-   CCOS
-   Contract Score
-   trade eligibility
-   contract ranking
-   position sizing
-   DRS
-   Roll Engine / RQS
-   recommendations
-   trade execution

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

Cluster representative = arithmetic mean of clustered swing-high prices.

Primary resistance = nearest qualified resistance above current close.

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

## Critical Open Design Question: IV Context

This is the NEXT design task.

Phase 4 CCOS requires:

``` text
IV Percentile
IV30 / RV30
```

Phase 2 supplies normalized contract-level option observations including
implied volatility.

The architecture decision is now:

``` text
Phase 2
    contract-level normalized option observations
        |
        v
Phase 3
    provider-independent underlying-level IV context
        IV30
        IVRank
        IVPercentile
        |
        v
Phase 4
    CCOS volatility scoring
```

Phase 4 must not reconstruct IV metrics from provider-specific option
data.

The detailed V1 formulas are intentionally NOT defined yet. They must be
designed before implementation.

The IV design must explicitly decide:

1.  What exactly a daily underlying `IV30` observation means.
2.  Eligible option contracts.
3.  ATM / strike-selection methodology.
4.  Whether/how calls and puts are combined.
5.  Expiration eligibility.
6.  How expirations around 30 days are selected.
7.  Whether and how interpolation creates a constant 30-day maturity.
8.  Minimum required expirations/contracts.
9.  Missing-data behavior.
10. Historical storage semantics.
11. IV Rank lookback and exact formula.
12. IV Percentile lookback and exact formula.
13. Behavior when historical IV coverage is incomplete.
14. Numerical representation and precision.
15. Deterministic tests/reference fixtures.

Do not allow Codex to infer these from Tradier behavior or generic
industry conventions.

## Known Later Phase 4 Design Gaps

Do not solve these accidentally during Phase 3:

-   exact Trend/Momentum CCOS scoring
-   exact Bollinger bandwidth CCOS scoring
-   exact Resistance/Structure CCOS scoring from Phase 3 resistance
    evidence

These require explicit Phase 4 design.

## Recommended Next Conversation

Start a fresh ChatGPT conversation and attach/reference:

1.  this handoff document;
2.  `SPECIFICATION.md` from the current `phase3` branch;
3.  `docs/acceptance/PHASE-3-INDICATORS.md`;
4.  optionally `AGENTS.md`.

Then use the bootstrap prompt in `NEW-CHAT-IV30-PROMPT.md`.

The first task should be design only: define the authoritative V1 IV30
methodology. Do not ask Codex to implement Phase 3 until that design is
incorporated into `SPECIFICATION.md` and the Phase 3 acceptance
checklist.

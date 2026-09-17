# Phase 3 — Indicators and Market Context Acceptance Checklist

## Objective

Implement the deterministic technical-indicator and market-context layer consumed by later Options Engine strategy components.

Phase 3 transforms normalized historical market data into reproducible, provider-independent calculated observations.

Phase 3 shall implement:

1. Simple Moving Average (SMA).
2. Relative Strength Index (RSI).
3. Bollinger Bands.
4. Moving Average Convergence Divergence (MACD).
5. Average True Range (ATR).
6. Realized volatility.
7. Resistance detection.
8. Market and sector regime classification.

`SPECIFICATION.md` remains the authoritative product specification.

`AGENTS.md` remains the authoritative repository engineering guidance.

Phase 3 must not implement CCOS, Contract Score, position sizing, DRS, roll scoring, recommendations, Excel integration, or trade execution.

---

# 1. Governing Design Principle

Indicators are deterministic calculations over normalized market observations.

The architecture shall preserve this boundary:

```text
Market Data Provider
        |
        v
Normalized Market Data
        |
        v
Historical Persistence
        |
        v
Indicator Engine
        |
        v
Normalized Indicator Results
        |
        +----------------------+
        |                      |
        v                      v
Historical Persistence    Future Strategy Engines
```

Indicator calculations shall not depend directly upon:

```text
Tradier
HTTP
JSON
Provider DTOs
EF Core entities
ASP.NET
Excel
Brokerage APIs
```

Given identical:

```text
Historical observations
Indicator configuration
Calculation version
As-of date
```

the engine must produce identical results.

---

# 2. Phase 3 Scope

Phase 3 includes:

```text
SMA
RSI
Bollinger Bands
MACD
ATR
Realized Volatility
Resistance Detection
Market Regime
Sector Regime
Indicator persistence
Indicator retrieval
Indicator calculation tests
Application orchestration
Read-only indicator API
```

Phase 3 does not include:

```text
CCOS
CCOS scoring
Contract Score
Contract selection
Position sizing
DRS
Roll Engine
RQS
Recommendations
Campaign accounting
Trading
Brokerage integration
Excel dashboard
Machine learning
Backtesting strategy performance
Parameter optimization
```

Phase 3 calculates facts and classifications.

Phase 4 decides what those facts mean for a covered-call entry.

---

# 3. Indicator Calculation Boundary

Create an explicit provider-independent indicator calculation boundary.

Conceptually:

```csharp
IIndicatorCalculator
```

or a small set of cohesive calculators.

The calculation layer shall operate on normalized chronological price observations rather than persistence entities.

A calculation request shall explicitly identify:

```text
Symbol
AsOfDate
Historical observations
Indicator configuration/version
```

Calculators should be pure wherever practical.

No calculator may query Tradier or SQLite directly.

Data retrieval belongs in Application/Infrastructure orchestration.

---

# 4. Input Data

Phase 3 primarily consumes normalized daily historical OHLCV observations introduced in Phase 2.

At minimum:

```text
Symbol
TradingDate
Open
High
Low
Close
Volume
Provider
```

Indicator calculation shall use observations ordered by trading date.

Calendar days with no trading observation shall not be synthesized as zero-price or zero-volume trading days.

Weekends and market holidays shall therefore not create artificial observations.

Duplicate logical daily observations must be resolved by the Phase 2 persistence contract before indicator calculation.

---

# 5. As-Of-Date Semantics

Every indicator calculation shall have explicit as-of semantics.

For:

```text
AsOfDate = D
```

the calculation may consume observations with:

```text
TradingDate <= D
```

and must never consume observations with:

```text
TradingDate > D
```

This is a hard requirement.

Historical indicator calculation must not introduce look-ahead bias.

A calculation for a historical date must produce the result that could have been calculated using information available as of that date.

Tests shall explicitly prove this behavior.

---

# 6. Missing Data

Missing data shall never silently become zero.

Examples:

```text
Missing Close != Close of 0
Missing Volume != Volume of 0
Missing Indicator != Indicator of 0
```

If insufficient observations exist to calculate an indicator, its result shall be explicitly unavailable.

Conceptually:

```text
Value = null
Status = InsufficientData
```

or an equivalent strongly typed representation.

The implementation shall distinguish:

```text
valid numeric zero
```

from:

```text
indicator cannot be calculated
```

---

# 7. Indicator Result Model

Define a provider-independent calculated indicator snapshot.

Conceptually:

```text
IndicatorSnapshot

Symbol
AsOfDate

Sma20
Sma50
Sma200

Rsi14

BollingerMiddle
BollingerUpper
BollingerLower
BollingerPercentB
BollingerBandwidth

Macd
MacdSignal
MacdHistogram

Atr14
AtrPercent

RealizedVolatility20
RealizedVolatility30

NearestResistance
ResistanceDistancePercent
ResistanceStrength

MarketRegime
SectorRegime

CalculationVersion
ConfigurationVersion
CalculatedAt
```

Exact persistence decomposition may differ, but equivalent information must be representable.

Unavailable values shall remain nullable.

Do not substitute sentinel numeric values.

---

# 8. Configuration

Indicator parameters shall be strongly typed and configurable.

Default Phase 3 configuration:

```text
SMA:
    Fast = 20
    Medium = 50
    Long = 200

RSI:
    Period = 14

Bollinger:
    Period = 20
    StandardDeviations = 2

MACD:
    FastPeriod = 12
    SlowPeriod = 26
    SignalPeriod = 9

ATR:
    Period = 14

RealizedVolatility:
    ShortPeriod = 20
    StandardPeriod = 30
    AnnualizationTradingDays = 252
```

Resistance and regime parameters shall also be configurable once their algorithms are defined below.

No important numerical threshold shall be buried as an unexplained magic constant.

Configuration must be versionable.

---

# 9. Simple Moving Average

Implement arithmetic Simple Moving Average.

For period `N`:

```text
SMA(N) =
SUM(last N closing prices) / N
```

Default periods:

```text
20
50
200
```

The current observation counts as one of the N observations.

At least N valid closing observations are required.

If fewer than N valid observations exist:

```text
SMA(N) = unavailable
```

Do not calculate a partial-window SMA.

---

# 10. SMA Derived Context

Phase 3 shall expose sufficient information for later trend evaluation.

At minimum:

```text
Close vs SMA20
Close vs SMA50
Close vs SMA200
SMA20 vs SMA50
SMA50 vs SMA200
```

These may be calculated later from the persisted values and do not necessarily require separate persisted columns.

Phase 3 shall not assign CCOS points to these relationships.

---

# 11. Relative Strength Index

Implement RSI using the standard 14-period Wilder formulation.

Default:

```text
Period = 14
```

Price change:

```text
Change =
CurrentClose - PreviousClose
```

Separate into:

```text
Gain = max(Change, 0)
Loss = max(-Change, 0)
```

Initial average gain/loss shall use the arithmetic mean across the initial RSI period.

Subsequent averages shall use Wilder smoothing:

```text
AverageGain =
((PreviousAverageGain × (N - 1)) + CurrentGain) / N

AverageLoss =
((PreviousAverageLoss × (N - 1)) + CurrentLoss) / N
```

Then:

```text
RS =
AverageGain / AverageLoss

RSI =
100 - (100 / (1 + RS))
```

Handle zero-loss and zero-gain cases explicitly and deterministically.

RSI shall remain within:

```text
0 <= RSI <= 100
```

Phase 3 shall calculate RSI only.

The CCOS RSI scoring bands defined by the specification belong to Phase 4.

---

# 12. RSI Warm-Up

RSI requires sufficient price changes, not merely N prices.

For a period of 14, at least 15 closing observations are required to produce the initial RSI value.

The implementation shall document and test this distinction.

Partial-window RSI values shall not be emitted.

---

# 13. Bollinger Bands

Implement standard Bollinger Bands using:

```text
Period = 20
StandardDeviations = 2
```

Middle band:

```text
Middle = SMA20
```

Upper band:

```text
Upper =
Middle + (2 × StandardDeviation)
```

Lower band:

```text
Lower =
Middle - (2 × StandardDeviation)
```

The standard deviation shall be calculated over the same closing-price window as the middle band.

The implementation shall explicitly choose and document population standard deviation for the rolling observed-price window.

Tests shall lock this definition.

---

# 14. Bollinger %B

Calculate:

```text
%B =
(Price - LowerBand)
/
(UpperBand - LowerBand)
```

The current closing price shall be used as `Price`.

%B may legitimately be:

```text
< 0
> 1
```

Do not clamp the value.

If:

```text
UpperBand == LowerBand
```

%B shall be unavailable rather than producing division-by-zero behavior.

Phase 3 shall not apply CCOS %B scoring.

---

# 15. Bollinger Bandwidth

Calculate normalized bandwidth:

```text
Bandwidth =
(UpperBand - LowerBand)
/
MiddleBand
```

If the middle band is zero or unavailable, bandwidth shall be unavailable.

Bandwidth shall be retained as a continuous numeric indicator.

Phase 3 shall not determine the later CCOS bandwidth score.

---

# 16. MACD

Implement standard MACD using:

```text
Fast EMA = 12
Slow EMA = 26
Signal EMA = 9
```

Calculate:

```text
MACD =
EMA12 - EMA26

Signal =
EMA9(MACD)

Histogram =
MACD - Signal
```

EMA smoothing:

```text
Multiplier =
2 / (Period + 1)

EMA =
(CurrentValue × Multiplier)
+
(PreviousEMA × (1 - Multiplier))
```

EMA initialization shall be deterministic and documented.

Use an SMA of the first complete period as the initial EMA seed.

Do not initialize the EMA from an arbitrary first price.

---

# 17. MACD Warm-Up

MACD values shall not be emitted until the slow EMA can be established.

Signal and histogram values require additional MACD observations sufficient to establish the signal EMA.

The engine shall therefore allow:

```text
MACD available
Signal unavailable
Histogram unavailable
```

during the valid intermediate warm-up period.

Missing components shall not become zero.

---

# 18. True Range

For trading observation `t`, calculate True Range as:

```text
TR =
max(
    High[t] - Low[t],
    abs(High[t] - Close[t-1]),
    abs(Low[t] - Close[t-1])
)
```

The first observation has no previous close and therefore cannot contribute a standard previous-close True Range observation.

---

# 19. Average True Range

Implement ATR using Wilder smoothing.

Default:

```text
Period = 14
```

Initial ATR:

```text
ATR14 =
Arithmetic mean of first 14 valid True Range values
```

Subsequent ATR:

```text
ATR =
((PreviousATR × 13) + CurrentTR) / 14
```

Also calculate:

```text
ATRPercent =
ATR / Close
```

ATRPercent is unavailable if Close is unavailable or zero.

---

# 20. Realized Volatility

Implement annualized close-to-close realized volatility.

For each trading day:

```text
Return[t] =
ln(Close[t] / Close[t-1])
```

For an N-period realized-volatility window:

```text
RV(N) =
StandardDeviation(last N log returns)
× sqrt(252)
```

Default windows:

```text
RV20
RV30
```

Use sample standard deviation for the return series.

Annualization trading days shall default to:

```text
252
```

and shall be configurable.

Values shall be represented consistently as decimal ratios:

```text
0.25 = 25% annualized volatility
```

Do not mix percentage-point and decimal representations internally.

---

# 21. Realized Volatility Warm-Up

An N-period realized-volatility calculation requires N valid returns.

Therefore it requires at least:

```text
N + 1
```

valid closing-price observations.

Partial-window realized volatility shall not be emitted.

---

# 22. Resistance Detection Objective

Resistance detection answers:

> What meaningful recent price level above the current price may impede further upward movement?

This is a structural market observation.

It shall not answer:

> Should a covered call be sold?

That decision belongs to later strategy phases.

Resistance detection must be deterministic and must not use future observations.

---

# 23. Resistance Detection Algorithm

Phase 3 shall implement a deterministic swing-high clustering algorithm.

Step 1 — Identify swing highs.

A price observation is a swing-high candidate when its `High` is greater than the highs of a configurable number of observations immediately before and after it.

Default:

```text
SwingWindow = 3
```

Conceptually:

```text
High[t] >
High[t-3..t-1]

AND

High[t] >=
High[t+1..t+3]
```

For historical as-of calculations, only swing highs whose complete confirmation window occurs on or before the as-of date may be used.

This prevents look-ahead bias.

Step 2 — Restrict lookback.

Default:

```text
ResistanceLookbackTradingDays = 120
```

Step 3 — Cluster nearby swing highs.

Levels within:

```text
0.75 × ATR14
```

of one another shall be considered part of the same candidate resistance zone.

The clustering distance shall be configurable.

Step 4 — Candidate level.

Represent each cluster using a deterministic central price, such as the arithmetic mean of the clustered swing-high prices.

Step 5 — Candidate eligibility.

The primary resistance level shall normally be the nearest qualified resistance level above the current closing price.

Step 6 — Strength.

Resistance strength shall be based on objective characteristics including:

```text
Number of confirmed touches
Recency
```

Strength shall be represented separately from the resistance price.

Do not combine resistance strength into CCOS during Phase 3.

---

# 24. Resistance Result

At minimum expose:

```text
NearestResistance
ResistanceDistance
ResistanceDistancePercent
ResistanceTouchCount
ResistanceStrength
```

Calculate:

```text
ResistanceDistance =
NearestResistance - CurrentClose

ResistanceDistancePercent =
ResistanceDistance / CurrentClose
```

If no qualified resistance exists:

```text
NearestResistance = unavailable
```

Do not fabricate a resistance level.

---

# 25. Resistance Edge Cases

Tests shall cover:

```text
No swing highs
One valid swing high
Multiple distinct levels
Multiple clustered touches
Resistance immediately above price
Current price above all detected historical resistance
ATR unavailable
Insufficient confirmation window
Insufficient lookback history
```

Resistance detection shall remain deterministic in every case.

---

# 26. Market Regime Objective

Market regime provides broad directional context for later covered-call decisions.

Phase 3 shall classify the market environment without assigning CCOS points.

V1 market benchmark:

```text
SPY
```

The benchmark symbol shall be configurable.

The classification shall use only normalized historical market data.

---

# 27. Market Regime Classification

V1 shall use a simple deterministic trend regime based primarily on:

```text
Close
SMA50
SMA200
SMA50 direction
```

Default classifications:

```text
BULLISH
NEUTRAL
BEARISH
INSUFFICIENT_DATA
```

Default rules:

```text
BULLISH:
    Close > SMA200
    AND SMA50 > SMA200
    AND SMA50 is rising

BEARISH:
    Close < SMA200
    AND SMA50 < SMA200
    AND SMA50 is falling

Otherwise:
    NEUTRAL
```

SMA50 direction shall compare the current SMA50 with a configurable prior SMA50 observation.

Default slope lookback:

```text
20 trading days
```

If the required history is unavailable:

```text
INSUFFICIENT_DATA
```

Phase 3 shall not attach covered-call desirability scores to these regimes.

---

# 28. Sector Regime

Sector regime shall use the same classification algorithm as market regime.

The difference is the benchmark.

A holding may be associated with a configurable sector benchmark ETF.

Examples may include:

```text
XLK
XLF
XLE
XLV
XLY
XLP
XLI
XLB
XLU
XLRE
XLC
```

The architecture shall not hard-code a particular sector ETF into the indicator calculator.

The mapping:

```text
Underlying -> Sector Benchmark
```

belongs to configuration/application context.

If no sector benchmark is configured:

```text
SectorRegime = unavailable
```

rather than assuming a neutral regime.

---

# 29. Regime Separation

Market regime and sector regime shall remain separate values.

Example:

```text
MarketRegime = BULLISH
SectorRegime = NEUTRAL
```

Phase 3 shall not combine them into a single score.

Phase 4 may determine how they contribute to CCOS.

---

# 30. Indicator Snapshot Persistence

Calculated indicators shall be persistable as historical observations.

A persisted indicator snapshot shall be uniquely associated with at least:

```text
Symbol
AsOfDate
CalculationVersion
ConfigurationVersion
```

Recalculating today's indicators must not silently rewrite historical results generated under a different calculation/configuration version.

Persistence shall support later recommendation reproducibility.

---

# 31. Calculation Version

Indicator implementation behavior shall have an explicit calculation version.

Example:

```text
IndicatorCalculationVersion = "1.0.0"
```

This differs conceptually from:

```text
StrategyVersion
ConfigurationVersion
```

Changing an algorithm in a way that can change historical output requires a calculation-version change.

Examples:

```text
Changing RSI smoothing
Changing EMA initialization
Changing standard-deviation definition
Changing resistance algorithm
Changing regime classification
```

A simple bug fix that changes historical numerical results must likewise be treated as calculation-version relevant.

---

# 32. Reproducibility

A historical indicator result must be traceable to:

```text
Symbol
AsOfDate
Historical input observations
Indicator configuration
Calculation version
Calculation timestamp
```

The system shall not require current market data to explain a historical indicator result.

Phase 3 shall preserve the foundation needed for later Recommendation reproducibility.

---

# 33. Application Service

Application shall expose provider-independent indicator orchestration.

Conceptually:

```text
GetIndicatorsAsync(
    symbol,
    asOfDate,
    cancellationToken)
```

Responsibilities:

```text
Normalize symbol
Determine required history window
Retrieve normalized persisted historical data
Refresh historical data through the Phase 2 abstraction when appropriate
Invoke indicator calculation
Obtain market benchmark history
Obtain configured sector benchmark history
Calculate regimes
Persist indicator snapshot
Return normalized result
```

The Application layer may orchestrate these operations.

The indicator calculation layer itself must remain free of provider and persistence dependencies.

---

# 34. Historical Data Window

The Application layer shall request sufficient historical data to satisfy the longest configured indicator warm-up.

Because SMA200 and market/sector regime calculations require substantial history, the request must include adequate trading observations before the as-of date.

Do not assume:

```text
200 calendar days == 200 trading observations
```

The implementation may request a conservative calendar lookback and validate that sufficient trading observations were actually returned.

If history remains insufficient, affected indicators shall report unavailable/insufficient data.

---

# 35. Current vs Historical Calculation

The same indicator engine shall support:

```text
Current calculation
Historical as-of calculation
```

Do not create separate mathematical implementations for current and historical indicators.

Historical calculation must simply constrain the available observation set by the requested as-of date.

This prevents divergence between research and production calculations.

---

# 36. API

Expose a read-only Phase 3 API endpoint.

Recommended:

```http
GET /api/indicators/{symbol}
```

Optional historical query:

```http
GET /api/indicators/{symbol}?asOf=YYYY-MM-DD
```

If `asOf` is omitted, use the latest applicable market observation.

The response shall expose normalized indicator results and classifications.

It shall not expose:

```text
Tradier DTOs
EF entities
internal persistence keys
CCOS
trade recommendations
```

---

# 37. API Response

Conceptually:

```json
{
  "symbol": "MSFT",
  "asOfDate": "2026-09-17",
  "sma20": null,
  "sma50": null,
  "sma200": null,
  "rsi14": null,
  "bollinger": {
    "middle": null,
    "upper": null,
    "lower": null,
    "percentB": null,
    "bandwidth": null
  },
  "macd": {
    "value": null,
    "signal": null,
    "histogram": null
  },
  "atr14": null,
  "atrPercent": null,
  "realizedVolatility20": null,
  "realizedVolatility30": null,
  "resistance": {
    "price": null,
    "distancePercent": null,
    "touchCount": null,
    "strength": null
  },
  "marketRegime": "INSUFFICIENT_DATA",
  "sectorRegime": null,
  "calculationVersion": "1.0.0",
  "configurationVersion": "..."
}
```

This is conceptual rather than a mandatory serialization shape.

The implementation may use idiomatic API DTOs.

---

# 38. Numerical Precision

Indicator calculations shall use numerical types appropriate to quantitative calculations.

Money/price values shall continue to respect the repository's monetary precision rules.

Statistical calculations may use `double`.

Conversion between `decimal` and `double` shall occur deliberately at calculation boundaries.

Tests should use tolerances appropriate to floating-point calculations rather than requiring inappropriate bit-for-bit equality.

Persisted precision shall be sufficient to reproduce downstream scoring decisions.

---

# 39. External Indicator Libraries

Phase 3 may use a mature .NET technical-analysis library only if it materially reduces implementation risk.

However:

- the repository must own the indicator interfaces;
- external library types must not leak into Domain/Application/API contracts;
- formulas and initialization behavior must match this acceptance document;
- library behavior must be locked with deterministic tests;
- resistance and regime semantics remain Options Engine responsibilities;
- introducing a library must not create provider or infrastructure coupling.

If library behavior conflicts with the approved definitions, the approved definitions win.

---

# 40. Deterministic Reference Fixtures

Create deterministic historical price fixtures covering:

```text
Steady rise
Steady decline
Flat prices
Volatile prices
Gap up
Gap down
Trend reversal
Insufficient history
Missing optional fields
Resistance clusters
No resistance
```

Fixtures shall be synthetic or sanitized.

Automated tests shall not depend on current market prices.

---

# 41. SMA Tests

At minimum test:

```text
Exact warm-up boundary
One observation below warm-up
Known SMA result
Rolling window behavior
Historical as-of behavior
```

---

# 42. RSI Tests

At minimum test:

```text
Known reference sequence
Initial RSI
Wilder smoothing after initialization
All gains
All losses
Flat prices
Exact warm-up boundary
Insufficient history
0 <= RSI <= 100
```

Tests should compare against independently verified reference values.

---

# 43. Bollinger Tests

At minimum test:

```text
Known middle band
Known standard deviation
Upper/lower bands
%B inside bands
%B above 1
%B below 0
Bandwidth
Flat-price zero-width bands
Insufficient history
```

---

# 44. MACD Tests

At minimum test:

```text
EMA initialization
Fast EMA
Slow EMA
MACD
Signal
Histogram
MACD available before signal warm-up
Insufficient history
Trend reversal
```

---

# 45. ATR Tests

At minimum test:

```text
High-low range dominates
Gap-up previous-close range dominates
Gap-down previous-close range dominates
Initial ATR
Wilder smoothing
ATR percent
Insufficient history
```

---

# 46. Realized Volatility Tests

At minimum test:

```text
Known log returns
Known sample standard deviation
Annualization
RV20
RV30
Flat-price volatility = 0
Exact warm-up boundary
Insufficient history
```

A valid calculated volatility of zero must remain distinguishable from unavailable volatility.

---

# 47. Resistance Tests

Test the deterministic resistance algorithm independently from Application and persistence.

At minimum:

```text
Confirmed swing high
Unconfirmed recent swing high excluded
Future observation never used
Clustered levels
Distinct levels
Nearest qualified resistance selected
Resistance touch count
No resistance
ATR unavailable
Historical as-of calculation
```

---

# 48. Regime Tests

At minimum test:

```text
Bullish market regime
Bearish market regime
Neutral regime
Insufficient SMA200 history
Rising SMA50
Falling SMA50
Market and sector classifications differ
Missing sector mapping
Historical as-of classification
```

---

# 49. Look-Ahead Bias Tests

Create explicit tests designed to fail if future observations enter historical calculations.

Example:

1. Build historical observations through date D.
2. Calculate indicators as of D.
3. Append an extreme price movement after D.
4. Recalculate as of D.
5. Assert every Phase 3 result is unchanged.

Perform this for at least:

```text
Standard indicators
Resistance
Market regime
Sector regime
```

This is a Phase 3 hard acceptance requirement.

---

# 50. Persistence Tests

At minimum verify:

```text
Indicator snapshot can be persisted
Indicator snapshot can be retrieved
Nullable indicators remain null
Calculation version preserved
Configuration version preserved
Historical as-of date preserved
Different calculation versions may coexist
Different as-of dates may coexist
```

Do not allow a current calculation to destroy historical calculation records.

---

# 51. Application Tests

Use test doubles around persistence/market-data boundaries.

Verify:

```text
Symbol normalization
Correct historical window requested
As-of date propagated
Indicator calculator invoked with chronological data
Market benchmark requested
Configured sector benchmark requested
Missing sector benchmark handled
Insufficient history propagated
Result persisted
Cancellation propagated
```

No Application test shall require Tradier or internet access.

---

# 52. API Integration Tests

At minimum:

```text
GET /api/indicators/MSFT
GET /api/indicators/MSFT?asOf=YYYY-MM-DD
```

Verify:

```text
Routing
Dependency injection
Serialization
Nullable values
Regime serialization
Invalid asOf handling
Insufficient-data response behavior
No provider-specific data leakage
```

Tests shall be deterministic and require no API token.

---

# 53. Performance

Phase 3 calculations operate on daily observations and do not require premature optimization.

However:

- a normal single-symbol calculation should not repeatedly query the database once per indicator;
- historical observations should be retrieved as a coherent chronological set;
- the same input series should be reused across calculators;
- market/sector histories should not be repeatedly fetched within one request;
- calculations should remain practical for a portfolio of multiple holdings.

Correctness and reproducibility take priority over micro-optimization.

---

# 54. Logging

Structured logging shall capture useful calculation context without excessive noise.

Useful fields include:

```text
Symbol
AsOfDate
ObservationCount
CalculationVersion
ConfigurationVersion
InsufficientData indicators
MarketBenchmark
SectorBenchmark
Elapsed calculation time
```

Do not log entire historical datasets under normal operation.

---

# 55. Error Handling

Phase 3 shall distinguish:

```text
Invalid request
Insufficient historical data
Historical data unavailable
Calculation failure
Configuration failure
```

Insufficient data is an expected domain condition, not necessarily an application exception.

A missing indicator caused by insufficient warm-up shall not produce a fabricated numeric result.

---

# 56. Documentation

Update repository documentation to describe:

```text
Indicator architecture
Default periods
RSI smoothing definition
Bollinger standard-deviation definition
MACD initialization
ATR formulation
Realized-volatility formulation
Resistance algorithm
Market-regime algorithm
Sector-regime configuration
As-of semantics
Look-ahead-bias protection
Calculation versioning
```

Formulas must be sufficiently explicit that another implementation could reproduce the same result.

---

# 57. Clean Repository Verification

Before Phase 3 is considered complete:

```bash
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
```

must succeed from a clean repository without:

```text
Tradier token
Internet connectivity
Pre-existing SQLite database
Manual test setup
```

Release build shall contain zero warnings and zero errors unless an explicitly documented repository-wide exception exists.

---

# 58. Database Migration

# 58. Database Migration

Phase 3 shall persist indicator snapshots and shall create a new EF Core migration for the required Phase 3 schema.

The Phase 1 and Phase 2 migrations must remain unchanged.

Verify:

Empty DB -> Phase 1 -> Phase 2 -> Phase 3

and:

Populated Phase 2 DB -> Phase 3

Existing:

Account
Holding
TaxLot
HistoricalPriceBars
MarketQuoteSnapshots
OptionContractSnapshots
OptionExpirationCaches
HistoricalPriceCoverages

must remain intact.

Add automated migration/data-preservation coverage.

Verify no pending EF model changes remain.

---

# 59. Phase 3 Exit Criteria

Phase 3 is complete when:

- [ ] SMA20, SMA50, and SMA200 are implemented.
- [ ] RSI14 with Wilder smoothing is implemented.
- [ ] Bollinger Bands, %B, and bandwidth are implemented.
- [ ] MACD 12/26/9 is implemented.
- [ ] ATR14 with Wilder smoothing is implemented.
- [ ] RV20 and RV30 annualized realized volatility are implemented.
- [ ] Deterministic resistance detection is implemented.
- [ ] Market regime classification is implemented.
- [ ] Sector regime classification is implemented.
- [ ] Indicator calculations are provider-independent.
- [ ] Indicator calculations are persistence-independent.
- [ ] Missing values remain explicitly unavailable.
- [ ] Warm-up behavior is explicit and tested.
- [ ] Historical as-of calculations cannot use future observations.
- [ ] Look-ahead-bias tests pass.
- [ ] Indicator snapshots are reproducible/versioned.
- [ ] Indicator snapshot persistence is implemented.
- [ ] Phase 3 EF migration is implemented.
- [ ] Clean-database migration verification passes.
- [ ] Populated Phase 2 -> Phase 3 migration/data-preservation verification passes.
- [ ] No pending EF model changes remain.
- [ ] Read-only indicator API is implemented.
- [ ] Deterministic unit tests cover mathematical boundaries.
- [ ] Application orchestration tests pass.
- [ ] API integration tests pass.
- [ ] Documentation is updated.
- [ ] Release build succeeds with zero warnings/errors.
- [ ] All automated tests pass.
- [ ] No Phase 4 functionality has been introduced.

---

# 60. Required PR Evidence

The Phase 3 pull request shall report actual evidence for:

```text
Release build result
Test passed/failed/skipped count
Indicator algorithms implemented
Reference fixtures used
Look-ahead-bias verification
Persistence/migration verification
Pending EF model verification
API integration verification
Documentation updates
```

Do not claim validation that was not performed.

If any acceptance criterion remains incomplete, identify it explicitly in the PR description.

---

# 61. Definition of Done

Phase 3 is done when Options Engine can take normalized historical market observations and deterministically produce a reproducible indicator snapshot containing the approved technical indicators, resistance context, and market/sector regimes for any supported historical as-of date without relying on future information.

The output shall be suitable for direct consumption by Phase 4's entry-strategy and CCOS implementation without requiring Phase 4 to reinterpret provider data or independently recalculate technical indicators.
# Covered Call Decision Support System

## V1 Engineering Specification

**Status:** Approved for implementation planning
**Strategy Version:** 1\.0\.0
**Target Platform:** \.NET 10 / C\# / SQLite / Excel
**Primary Market Data Provider:** Tradier
**Initial Execution Brokers:** Fidelity and Charles Schwab
**Trade Execution:** Manual
**Primary Objective:** Generate sustainable covered\-call income while maintaining a very low probability of assignment and preserving long\-term appreciated positions\.

---

# 1\. Purpose

The Covered Call Decision Support System &#40;CCDSS&#41; is a quantitative decision\-support platform for managing covered\-call strategies across stocks and ETFs\.

The system shall identify favorable covered\-call opportunities, rank option contracts, determine appropriate position size, monitor open positions for assignment risk, recommend defensive rolls, track complete covered\-call campaigns, and measure strategy performance against a buy\-and\-hold benchmark\.

The system shall initially support manually entered holdings and manually confirmed executions at Fidelity and Charles Schwab\.

Market and options data shall initially be obtained through Tradier\.

The system shall not execute trades automatically in V1\.

---

# 2\. Strategy Objective

The strategy optimization hierarchy is:

1. Preserve underlying shares and minimize assignment risk\.
2. Generate sustainable option income\.
3. Maximize risk\-adjusted premium\.
4. Minimize unnecessary rolling and transaction costs\.
5. Measure whether the strategy adds value relative to simply holding the underlying securities\.

Assignment risk is a **hard constraint**, not simply another weighted optimization variable\.

The system is specifically designed to support holdings with substantial embedded capital gains where assignment may have significant tax consequences\.

---

# 3\. V1 Scope

V1 shall support:

- Multiple stocks and ETFs\.
- Multiple brokerage accounts\.
- Holdings at Fidelity and Schwab\.
- Manual holdings/tax\-lot entry\.
- Tradier market\-data integration\.
- Historical underlying price storage\.
- Option\-chain retrieval\.
- Greeks and implied volatility\.
- Technical indicator calculation\.
- Covered Call Opportunity Score &#40;CCOS&#41;\.
- Contract Score &#40;CS&#41;\.
- Position Sizing Engine\.
- Defense Risk Score &#40;DRS&#41;\.
- Roll Engine\.
- Roll Quality Score &#40;RQS&#41;\.
- Covered\-call campaign accounting\.
- Transaction ledger\.
- Performance reporting\.
- Buy\-and\-hold benchmarking\.
- Excel dashboard\.
- SQLite historical database\.
- Strategy configuration\.
- Strategy versioning\.
- Recommendation history\.
- Counterfactual recommendation history\.

---

# 4\. Explicitly Out of Scope for V1

V1 shall not provide:

- Automatic brokerage execution\.
- Automatic rolling\.
- Automated tax\-return preparation\.
- Machine\-learning\-generated trading decisions\.
- Intraday tick processing\.
- High\-frequency trading\.
- Mobile application\.
- Automatic Fidelity synchronization\.
- Automatic Schwab synchronization\.
- Portfolio\-wide mathematical optimization\.
- Fully automated tax\-lot selection\.
- Investment or tax advice\.

The system recommends actions\. The user decides whether to execute them\.

---

# 5\. High\-Level Architecture

```text
                         +------------------+
                         |    Tradier API   |
                         +---------+--------+
                                   |
                                   v
                    +-----------------------------+
                    | TradierMarketDataProvider   |
                    +--------------+--------------+
                                   |
                                   v
                    +-----------------------------+
                    | Normalized Domain Models    |
                    +--------------+--------------+
                                   |
                    +--------------+--------------+
                    |                             |
                    v                             v
            +---------------+             +--------------+
            | SQLite Store  |             | Indicators   |
            +-------+-------+             +------+-------+
                    |                            |
                    +-------------+--------------+
                                  |
                                  v
                    +-----------------------------+
                    |      Strategy Engines       |
                    |                             |
                    | CCOS                        |
                    | Contract Score              |
                    | Position Sizing             |
                    | DRS                         |
                    | Roll Engine / RQS           |
                    +--------------+--------------+
                                   |
                                   v
                    +-----------------------------+
                    | Application Service / API   |
                    +--------------+--------------+
                                   |
                                   v
                    +-----------------------------+
                    |       Excel Dashboard       |
                    +-----------------------------+
```

The C\# application shall be the authoritative implementation of all strategy calculations\.

Excel shall primarily provide:

- configuration;
- visualization;
- filtering;
- reporting;
- recommendations;
- manual transaction entry;
- analytical exploration\.

Critical strategy logic shall not be duplicated in Excel formulas\.

---

# 6\. Architectural Principles

## 6\.1 Provider Independence

Strategy code shall never depend directly upon Tradier\-specific DTOs\.

Provider\-specific data shall be translated into normalized domain objects before entering the application domain\.

The architecture shall permit future implementations such as:

```text
IMarketDataProvider
    TradierMarketDataProvider
    MassiveMarketDataProvider
    SchwabMarketDataProvider
```

without modifying strategy engines\.

## 6.2 Strategy and Calculation Reproducibility

Every recommendation shall record or be traceable to:

- the normalized market observations used;
- calculated indicators;
- component scores;
- configuration values or configuration version;
- indicator calculation version;
- strategy version;
- recommendation timestamp.

Historical recommendations shall never silently change because:

- current market data changed;
- current strategy configuration changed;
- indicator configuration changed;
- an indicator algorithm changed;
- a market-data provider changed.

Historical indicator calculations shall have explicit as-of semantics.

For an indicator calculation with:

```text
AsOfDate = D
```

the calculation may consume only market observations with:

```text
TradingDate <= D
```

and must never consume observations after `D`.

This requirement applies to:

```text
Technical indicators
Resistance detection
Market regime
Sector regime
```

Historical calculations must therefore be protected from look-ahead bias.

Indicator implementations shall have an explicit calculation version independent from the strategy version.

Conceptually:

```text
IndicatorCalculationVersion
ConfigurationVersion
StrategyVersion
```

These values represent different concerns:

```text
IndicatorCalculationVersion
    Mathematical and algorithmic implementation used
    to derive indicators and market context.

ConfigurationVersion
    Parameter values supplied to calculations and
    strategy engines.

StrategyVersion
    Strategy and recommendation logic consuming
    those calculated values.
```

Any change to an indicator algorithm that can materially alter historical results shall require an indicator calculation-version change.

Examples include:

```text
RSI smoothing method
EMA initialization
Standard-deviation convention
Realized-volatility calculation
Resistance-detection algorithm
Market-regime algorithm
Sector-regime algorithm
```

Stored historical recommendations and indicator snapshots shall retain sufficient version information to reproduce and explain their original results.

Recalculating a canonical indicator snapshot from corrected historical inputs may change that canonical snapshot under the same calculation and configuration versions, as specified in Section 12.15. This shall not silently mutate an already recorded historical recommendation or erase the inputs and calculated values needed to explain its original decision.

## 6\.3 Immutable Transactions

Executed transactions shall be represented as immutable ledger entries\.

A roll shall never modify the original transaction\.

A roll consists of:

1. BTC existing call\.
2. STO replacement call\.

Both transactions belong to the same Campaign\.

## 6\.4 Raw Data Preservation

Raw market observations shall be retained whenever practical\.

Derived scores alone are insufficient for historical analysis\.

## 6\.5 Explainability

Every recommendation shall include both:

- numerical score;
- human\-readable reasons\.

Example:

```text
SELL CANDIDATE

CCOS: 84

Positive factors:
+ IV percentile 68
+ RSI 64
+ Price near resistance
+ Bullish momentum slowing

Risks:
- Broad market strongly bullish

Position recommendation:
3 contracts
```

---

# 7\. Proposed Solution Structure

```text
CoveredCall.sln

/src
    CoveredCall.Domain
    CoveredCall.Strategy
    CoveredCall.MarketData
    CoveredCall.Infrastructure
    CoveredCall.Application
    CoveredCall.Api

/tests
    CoveredCall.Domain.Tests
    CoveredCall.Strategy.Tests
    CoveredCall.MarketData.Tests
    CoveredCall.Infrastructure.Tests
    CoveredCall.Api.Tests

/tools
    CoveredCall.DataImporter

/docs
    engineering-spec.md
    strategy-model.md
    tradier-integration.md
    database-schema.md
```

---

# 8\. Technology Stack

## Backend

- \.NET 10
- C\#
- ASP\.NET Core Minimal API
- HttpClientFactory
- Dependency Injection
- System\.Text\.Json

## Persistence

- SQLite
- Entity Framework Core
- EF Core migrations

## Testing

- xUnit
- deterministic strategy fixtures
- integration tests
- mocked Tradier HTTP responses

## Presentation

- Microsoft Excel

## Future Presentation Layer

The architecture shall allow Excel to be replaced or supplemented by a React/web application without changing strategy logic\.

---

# 9\. Core Domain Entities

## 9\.1 Account

```text
AccountId
Name
Broker
AccountType
TaxDeferred
Enabled
```

Example brokers:

```text
Fidelity
Schwab
Other
```

Example account types:

```text
Taxable
TraditionalIRA
RothIRA
Other
```

Tax sensitivity may differ substantially between taxable and retirement accounts and shall therefore be configurable by holding/account\.

---

# 10\. Holding

```text
HoldingId
AccountId
Symbol
AssetType
Shares
AssignmentSensitivity
TaxSensitivity
MaximumCoveragePercent
MaximumInitialDelta
PreferredDeltaMinimum
PreferredDeltaMaximum
MinimumCCOS
MinimumContractScore
MinimumPremium
MinimumAnnualizedYield
MaximumDeltaExposureRatio
Enabled
```

For Position Sizing V1:

- `Holding.Shares` is the authoritative owned-share quantity.
- `MaximumCoveragePercent` is represented as a fractional ratio in the inclusive range `[0,1]`; for example, `0.70 = 70%`.
- `MaximumDeltaExposureRatio` is represented as a fractional ratio in the inclusive range `[0,1]`; for example, `0.15 = 15%`.
- Both values must be finite and validated within `[0,1]`.
- The existing `MaximumCoveragePercent` property name is retained in V1; no naming-only schema migration is required.

---

# 11\. Tax Lot

```text
TaxLotId
HoldingId
AcquisitionDate
Shares
CostBasisPerShare
TotalCostBasis
HoldingPeriodClassification
```

Derived values include:

```text
CurrentMarketValue
UnrealizedGain
UnrealizedGainPercent
EstimatedGainIfAssigned
```

V1 shall treat tax calculations as informational estimates only\.

---

# 12. Market and Indicator Snapshot

A Market Snapshot represents normalized market context and calculated technical indicators for a symbol as of a specific trading date.

The snapshot shall remain provider-independent.

Conceptually:

```text
MarketSnapshotId
Symbol
AsOfDate
CalculatedAt

Price
Open
High
Low
Close
Volume

SMA20
SMA50
SMA200

RSI14

BollingerUpper
BollingerMiddle
BollingerLower
BollingerPercentB
BollingerBandwidth

MACD
MACDSignal
MACDHistogram

ATR14
ATRPercent

RV20
RV30

IV30
IVRank
IVPercentile

ResistancePrice
DistanceToResistance
DistanceToResistancePercent
ResistanceTouchCount
ResistanceLastTouchDate
ResistanceAgeTradingDays
ResistanceUnavailableReason

MarketRegime
SectorRegime

IndicatorCalculationVersion
ConfigurationVersion
```

Not every field must be available for every snapshot.

Missing or insufficient data shall remain explicitly unavailable.

The following distinctions are mandatory:

```text
Missing indicator != indicator value of 0

Missing volatility != volatility of 0

Missing resistance != resistance price of 0

Missing regime != NEUTRAL regime
```

The implementation shall not silently substitute zero or a neutral classification when required input data is unavailable.

## 12.1 Indicator Calculation Principles

Technical indicators shall be deterministic calculations over normalized historical market observations.

Indicator calculation code shall not depend directly upon:

```text
Tradier
Provider DTOs
HTTP
EF Core entities
ASP.NET
Excel
Brokerage APIs
```

Given identical:

```text
Historical observations
AsOfDate
Indicator configuration
Indicator calculation version
```

the indicator engine shall produce equivalent results within the documented numerical tolerance.

Historical observations shall be ordered chronologically by trading date.

Calendar days without trading observations shall not be synthesized as zero-price or zero-volume observations.

The same calculation implementation shall support both current and historical as-of calculations.

Separate mathematical implementations for current and historical calculations shall not be maintained.

## 12.2 As-Of Semantics and Look-Ahead Protection

Every indicator calculation shall have an explicit as-of date.

For:

```text
AsOfDate = D
```

only observations satisfying:

```text
TradingDate <= D
```

may contribute to the result.

Future observations must never influence a historical result.

This applies to conventional rolling indicators as well as:

```text
Resistance detection
Market regime
Sector regime
```

A historical calculation shall produce the result that could have been calculated using information available as of that historical date.

Automated tests shall explicitly verify that appending observations after the as-of date does not change previously calculated results.

Look-ahead protection is a hard requirement because historical indicator data will later support:

```text
Recommendation reconstruction
Backtesting
Parameter analysis
Counterfactual analysis
Strategy validation
```

## 12.3 Indicator Configuration

Indicator parameters shall be strongly typed, configurable, and versioned.

Default V1 parameters:

```text
SMA
    FastPeriod = 20
    MediumPeriod = 50
    LongPeriod = 200

RSI
    Period = 14

BollingerBands
    Period = 20
    StandardDeviations = 2

MACD
    FastPeriod = 12
    SlowPeriod = 26
    SignalPeriod = 9

ATR
    Period = 14

RealizedVolatility
    ShortPeriod = 20
    StandardPeriod = 30
    AnnualizationTradingDays = 252

ImpliedVolatility
    TargetDteCalendarDays = 30
    MaxAtmStrikeDistanceRatio = 0.05
    HistoricalLookbackValidObservations = 252
    MinimumHistoricalValidObservations = 126

Resistance
    SwingWindow = 3
    LookbackTradingDays = 120
    ClusterDistanceAtrMultiplier = 0.75
    MinimumResistanceTouches = 2

Regime
    FastSmaPeriod = 50
    LongSmaPeriod = 200
    SlopeLookbackTradingDays = 20
    MarketBenchmark = SPY
```

Important numerical parameters shall not be buried as unexplained implementation constants.

## 12.4 Simple Moving Average

For period `N`:

```text
SMA(N) =
SUM(last N valid closing prices) / N
```

V1 shall calculate:

```text
SMA20
SMA50
SMA200
```

The current observation counts toward the rolling window.

At least `N` valid closing observations are required.

If fewer than `N` observations are available:

```text
SMA(N) = unavailable
```

Partial-window SMA values shall not be emitted.

## 12.5 Relative Strength Index

V1 shall calculate:

```text
RSI14
```

using the standard Wilder smoothing formulation.

For each price transition:

```text
Change =
CurrentClose - PreviousClose

Gain =
max(Change, 0)

Loss =
max(-Change, 0)
```

The initial average gain and loss shall be arithmetic means over the configured RSI period.

Subsequent values shall use Wilder smoothing:

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

Zero-loss and zero-gain cases shall be handled explicitly and deterministically.

A valid RSI shall satisfy:

```text
0 <= RSI <= 100
```

An RSI period of `N` requires at least:

```text
N + 1
```

closing-price observations because `N` price changes are required.

Partial-window RSI values shall not be emitted.

CCOS interpretation of RSI belongs to the Entry Strategy phase and shall not be implemented by the indicator calculator.

## 12.6 Bollinger Bands

V1 Bollinger Bands shall use:

```text
Period = 20
StandardDeviations = 2
```

Calculate:

```text
MiddleBand =
SMA20

UpperBand =
MiddleBand + (2 × StandardDeviation)

LowerBand =
MiddleBand - (2 × StandardDeviation)
```

Standard deviation shall use the same rolling closing-price observations used by the middle band.

The rolling observed-price window shall use population standard deviation.

This convention shall be documented and locked by deterministic tests.

Calculate Bollinger Percent B:

```text
%B =
(Close - LowerBand)
/
(UpperBand - LowerBand)
```

%B shall not be clamped.

Values below zero and above one are valid.

If:

```text
UpperBand == LowerBand
```

%B shall be unavailable.

Calculate normalized bandwidth:

```text
Bandwidth =
(UpperBand - LowerBand)
/
MiddleBand
```

If the middle band is zero or unavailable, bandwidth shall be unavailable.

Phase 3 calculates these values but does not assign CCOS points.

## 12.7 MACD

V1 MACD shall use:

```text
FastPeriod = 12
SlowPeriod = 26
SignalPeriod = 9
```

Calculate:

```text
MACD =
EMA12 - EMA26

MACDSignal =
EMA9(MACD)

MACDHistogram =
MACD - MACDSignal
```

EMA smoothing shall use:

```text
Multiplier =
2 / (Period + 1)

EMA =
(CurrentValue × Multiplier)
+
(PreviousEMA × (1 - Multiplier))
```

EMA initialization shall be deterministic.

The initial EMA for a period shall be seeded using the arithmetic SMA of the first complete period.

It shall not be initialized from an arbitrary first observation.

MACD may become available before sufficient MACD observations exist to initialize the signal EMA.

During that valid warm-up state:

```text
MACD = available
MACDSignal = unavailable
MACDHistogram = unavailable
```

Unavailable components shall not be represented as zero.

## 12.8 Average True Range

True Range for observation `t` shall be:

```text
TR =
max(
    High[t] - Low[t],
    abs(High[t] - Close[t-1]),
    abs(Low[t] - Close[t-1])
)
```

V1 shall calculate:

```text
ATR14
```

using Wilder smoothing.

Initial ATR:

```text
ATR14 =
Arithmetic mean of the first 14 valid True Range observations
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

ATRPercent shall be unavailable when Close is unavailable or zero.

## 12.9 Realized Volatility

V1 shall calculate annualized close-to-close realized volatility.

Daily log return:

```text
Return[t] =
ln(Close[t] / Close[t-1])
```

For an `N`-return window:

```text
RV(N) =
SampleStandardDeviation(last N log returns)
× sqrt(AnnualizationTradingDays)
```

V1 shall calculate:

```text
RV20
RV30
```

with:

```text
AnnualizationTradingDays = 252
```

by default.

The annualization value shall be configurable.

An `N`-period realized-volatility calculation requires:

```text
N valid returns
N + 1 valid closing-price observations
```

Partial-window realized volatility shall not be emitted.

Realized volatility shall use decimal-ratio representation:

```text
0.25 = 25% annualized volatility
```

A legitimate realized volatility of zero shall remain distinguishable from unavailable volatility.

## 12.10 Implied Volatility Context

Phase 3 derives provider-independent IV30, IVRank, and IVPercentile from normalized contract-level implied volatility captured by Phase 2. Phase 4 consumes these values and does not reconstruct them from provider-specific option data.

### IV30

V1 IV30 is constant-30-calendar-day ATM implied volatility derived from normalized provider-supplied contract IV.

For AsOfDate:
- DTE is calendar days to expiration.
- Use positive-DTE expirations available as of the calculation.
- Select the strike nearest the associated underlying price; equal-distance ties select the lower strike.
- Require abs(Strike - UnderlyingPrice) / UnderlyingPrice <= MaxAtmStrikeDistanceRatio. Default is 0.05 (5%) and is configurable.
- Require both call and put IV at the selected strike. V1 applies no liquidity, volume, open-interest, or bid/ask filter.
- ExpirationIV = (CallIV + PutIV) / 2.
- An exact 30-DTE expiration supplies IV30 directly.
- Otherwise linearly interpolate between the qualifying expirations nearest below and above 30 DTE:

```text
IV30 = IVNear
     + ((30 - DteNear) / (DteFar - DteNear))
       * (IVFar - IVNear)
```

V1 does not extrapolate. IV30 is unavailable if 30 DTE cannot be bracketed. IV uses double decimal-ratio representation, so 0.30 means 30%.

Historical IV30 uses the latest eligible option-chain observation available on AsOfDate. Later snapshots must never backfill or alter an earlier result. Phase 3 consumes normalized provider IV and does not invert an option-pricing model to recalculate contract IV.

Missing IV30 is explicitly unavailable with a reason. Zero, RV30, one-sided IV, and extrapolated IV are not substitutes.

### IV Rank and IV Percentile

Both metrics use the most recent 252 valid IV30 observations through and including current AsOfDate. At least 126 valid observations are required. Missing observations are ignored and never treated as zero.

```text
IVRank =
((CurrentIV30 - MinIV30) / (MaxIV30 - MinIV30)) * 100
```

If MaxIV30 equals MinIV30, IV Rank is unavailable.

```text
IVPercentile =
100 * (
    count(valid IV30 observations strictly below CurrentIV30)
    / count(valid IV30 observations in lookback)
)
```

Current IV30 is included in the denominator. Values equal to current IV30 are not counted as below it.

If current IV30 is unavailable or fewer than 126 valid observations exist, the applicable historical IV context is unavailable. Future observations must not change historical results.

Approved defaults:

```text
TargetDteCalendarDays = 30
MaxAtmStrikeDistanceRatio = 0.05
HistoricalLookbackValidObservations = 252
MinimumHistoricalValidObservations = 126
```

Material IV parameters are strongly typed and versioned through ConfigurationVersion. Material algorithm changes require IndicatorCalculationVersion review. Deterministic fixed-data reference fixtures shall lock this methodology.


## 12.11 Resistance Detection

Resistance detection answers:

> What meaningful recent price level above the current price may impede further upward movement?

Resistance is market structure information.

Resistance detection shall not determine whether a covered call should be sold.

V1 shall use deterministic confirmed swing-high clustering.

### Swing High Detection

An observation is a confirmed swing-high candidate when its `High` is greater than the highs of the configured number of observations immediately before it and greater than or equal to the highs of the configured number of observations immediately after it.

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

For a historical as-of calculation, the complete confirmation window must occur on or before the as-of date.

An apparent swing high that requires future observations for confirmation must not be used.

### Lookback

Default resistance lookback:

```text
120 trading observations
```

### Clustering

V1 shall use deterministic one-dimensional complete-linkage clustering of confirmed swing-high prices within the configured resistance lookback. Calculate one threshold for the requested AsOfDate:

```text
ClusteringDistance = ClusterDistanceAtrMultiplier × ATR14(AsOfDate)
```

The default multiplier is `0.75`. This threshold is fixed for the entire resistance calculation; do not use a separate historical ATR for each swing high.

Sort candidates by swing-high price ascending, then TradingDate ascending. Start a cluster with the first candidate and process the remaining candidates in that order. A candidate joins the current cluster only when its price is within the threshold of every existing member. In ascending price order, this is equivalent to:

```text
CandidatePrice - CurrentClusterMinimumPrice <= ClusteringDistance
```

The boundary is inclusive. Otherwise, close the current cluster and start a new one with the candidate. Every resulting cluster must satisfy `MaxClusterPrice - MinClusterPrice <= ClusteringDistance`. Sorting makes the result independent of input enumeration order; transitive/single-linkage chaining is not permitted.

For example, at a distance of `0.75`, prices `100.00`, `100.60`, and `101.20` form `{100.00, 100.60}` and `{101.20}`, because `101.20 - 100.00 > 0.75`.

The V1 representative price is the arithmetic mean of all confirmed swing-high prices in the cluster. Representative price does not determine cluster membership.

### Primary Resistance

`MinimumResistanceTouches` is configurable, defaults to `2`, and must be at least `1`. A cluster qualifies only when its confirmed swing-high touch count is at least this minimum. Under the default, an isolated one-touch swing high is structural information but does not qualify as primary resistance.

The primary resistance level is the qualified cluster with the smallest representative price strictly above CurrentClose:

```text
ResistanceTouchCount >= MinimumResistanceTouches
AND RepresentativePrice > CurrentClose
```

If no qualified resistance exists above the current price:

```text
ResistancePrice = unavailable
ResistanceUnavailableReason = NoQualifiedResistance
```

This is a valid structural state, not a data failure. If resistance cannot be evaluated because required inputs are unavailable, use:

```text
ResistancePrice = unavailable
ResistanceUnavailableReason = InsufficientData
```

The system shall not fabricate a resistance level. Sentinel values shall not be used. Phase 4 must distinguish `NoQualifiedResistance` from `InsufficientData`.

### Resistance Metrics

At minimum calculate:

```text
ResistancePrice

DistanceToResistance =
ResistancePrice - CurrentClose

DistanceToResistancePercent =
DistanceToResistance / CurrentClose

ResistanceTouchCount

ResistanceLastTouchDate

ResistanceAgeTradingDays
```

ResistanceLastTouchDate shall be the trading date of the most recent confirmed swing-high observation belonging to the selected resistance cluster.

ResistanceAgeTradingDays shall be the number of actual trading observations between the most recent confirmed touch and the snapshot AsOfDate. Calendar-day subtraction shall not be used.

Phase 3 shall expose objective structural evidence rather than a strategy-oriented resistance-strength score.

Phase 4 may use `DistanceToResistancePercent`, `ResistanceTouchCount`, `ResistanceAgeTradingDays`, and other approved structural inputs when calculating the Resistance/Structure component of CCOS. Phase 3 shall not determine the CCOS interpretation of those observations.

## 12.12 Market Regime

Market regime provides broad directional market context.

V1 shall use a configurable broad-market benchmark.

Default:

```text
SPY
```

V1 regime classifications:

```text
BULLISH
NEUTRAL
BEARISH
INSUFFICIENT_DATA
```

The default regime model shall use:

```text
Close
SMA50
SMA200
SMA50 direction
```

Default classification:

```text
BULLISH:

    Close > SMA200
    AND
    SMA50 > SMA200
    AND
    SMA50 is rising


BEARISH:

    Close < SMA200
    AND
    SMA50 < SMA200
    AND
    SMA50 is falling


Otherwise:

    NEUTRAL
```

SMA50 direction shall compare the current SMA50 against SMA50 from a configurable prior trading observation.

Default:

```text
SlopeLookbackTradingDays = 20
```

Conceptually:

```text
Rising:
CurrentSMA50 > SMA50[20 trading observations ago]

Falling:
CurrentSMA50 < SMA50[20 trading observations ago]
```

If required history is unavailable:

```text
MarketRegime = INSUFFICIENT_DATA
```

Phase 3 shall classify the regime but shall not assign CCOS points to it.

## 12.13 Sector Regime

Sector regime shall use the same deterministic classification algorithm as market regime.

The difference is the benchmark symbol.

A holding may be associated with a configurable sector benchmark.

V1 may use sector ETFs such as:

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

These examples do not constitute a hard-coded mapping inside the indicator engine.

The mapping:

```text
Underlying Symbol
    ->
Sector Benchmark Symbol
```

shall belong to configuration/application context.

If no sector benchmark is configured for an underlying:

```text
SectorRegime = unavailable
```

The system shall not silently assume:

```text
SectorRegime = NEUTRAL
```

Market and sector regimes shall remain separate observations.

Example:

```text
MarketRegime = BULLISH
SectorRegime = NEUTRAL
```

Phase 4 may determine how the two classifications contribute to CCOS.

## 12.14 Indicator Calculation Version

Indicator algorithms shall have an explicit calculation version.

Example:

```text
IndicatorCalculationVersion = 1.0.0
```

This version identifies the mathematical and algorithmic behavior used to produce the snapshot.

Changes that can alter historical indicator results require a calculation-version change.

Examples include:

```text
RSI smoothing
EMA initialization
Bollinger standard-deviation convention
ATR formulation
Realized-volatility formulation
Resistance algorithm
Regime algorithm
```

Historical indicator snapshots produced under different calculation versions may coexist.

Recalculating a current indicator must not silently rewrite a historical result generated under a different calculation version.

## 12.15 Indicator Snapshot Persistence

Calculated indicator snapshots shall be retained when necessary for recommendation reproducibility, auditing, historical research, and future backtesting.

A canonical persisted indicator snapshot has exactly one row per identity:

```text
Symbol
AsOfDate
IndicatorCalculationVersion
ConfigurationVersion
```

The database shall enforce a unique constraint or index over these four fields. Persistence shall use deterministic, atomic insert-or-replace/update semantics: insert when the identity does not exist; when it does exist, replace the persisted calculated facts with the newly calculated facts without creating a second canonical row. Preserve all four identity fields during replacement. `CalculatedAt` represents when the current canonical values were calculated and shall update to the new calculation timestamp after a successful replacement.

Corrected historical source data eligible for `AsOfDate` may legitimately change a recalculated canonical result. Recalculation shall not weaken AsOfDate or no-look-ahead rules: source observations ineligible for the requested historical AsOfDate shall not affect the result merely because calculation runs later.

A material algorithm change requires a new `IndicatorCalculationVersion`, and a material configuration change requires a new `ConfigurationVersion`. Each therefore creates a distinct canonical identity rather than replacing the prior version. V1 does not require immutable history of every recalculation; any future calculation audit/history belongs in a separate model, not duplicate canonical snapshot rows.

Persistence shall preserve nullable/unavailable values.

Historical snapshots shall remain traceable to the normalized market observations from which they were calculated.

The persistence design shall support multiple calculation or configuration versions without destroying prior historical results.

## 12.16 Numerical Precision

Prices and monetary values shall follow the repository's monetary precision rules.

Statistical and quantitative calculations may use `double`.

Conversions between monetary `decimal` values and statistical `double` values shall occur deliberately at calculation boundaries.

Floating-point calculations shall be validated using appropriate numerical tolerances.

Persisted indicator precision shall be sufficient to reproduce downstream scoring and classification decisions.

## 12.17 Indicator Libraries

A mature external .NET technical-analysis library may be used where it materially reduces implementation risk.

If an external library is used:

- repository-owned interfaces shall remain authoritative;
- external library types shall not leak into Domain, Application, or API contracts;
- the library's formulas and initialization behavior must match this specification;
- behavior shall be locked with deterministic reference tests;
- resistance and regime semantics remain Options Engine responsibilities.

If library behavior conflicts with this specification, this specification wins.

## 12.18 Phase Boundary

Phase 3 calculates market facts and classifications.

It shall not determine:

```text
CCOS
Contract Score
Trade eligibility
Recommended contract
Recommended contract count
DRS
Roll recommendation
```

Those decisions belong to later strategy phases.

---

# 13\. Historical Price Bar

```text
Symbol
Timestamp
Interval
Open
High
Low
Close
Volume
Provider
```

At least two years of daily underlying history should initially be retained\.

---

# 14\. Option Contract Snapshot

```text
OptionSnapshotId
Symbol
OptionSymbol
Timestamp

Expiration
Strike
OptionType

Bid
Ask
Last

Volume
OpenInterest

ImpliedVolatility
Delta
Gamma
Theta
Vega

UnderlyingPrice
Provider
```

Option observations shall be timestamped so the database naturally develops its own historical option dataset.

Phase 4 derives calendar DTE from the America/New_York calendar date corresponding to the evaluation timestamp:

```text
DTE = ExpirationDate - EvaluationDate
```

For Phase 4 V1, the approved derived contract metrics are:

```text
ReferencePremium = Bid

Mid =
(Bid + Ask) / 2

BidAskSpreadPercent =
(Ask - Bid) / Mid

StrikeDistance =
Strike - UnderlyingPrice

OTMPercent =
(Strike - UnderlyingPrice) / UnderlyingPrice

PremiumYield =
ReferencePremium / UnderlyingPrice

DailyPremiumYield =
PremiumYield / DTE

AnnualizedPremiumYield =
DailyPremiumYield * 365
```

The selected option-chain observation's provider-supplied `UnderlyingPrice` is authoritative for these contract calculations. Phase 4 shall not substitute the daily close, a separate quote, `Last`, or zero when required values are missing.

The following conceptual fields are deferred from Phase 4 V1 until an approved methodology requires them:

```text
DeltaAdjustedYield
ExpectedMove
ExpectedMoveRatio
StrikeVsResistance
```

# 15\. Corporate Event

```text
CorporateEventId
Symbol
EventType
EventDate
Amount
Description
Source
```

The broader domain may represent:

```text
Earnings
ExDividend
Dividend
OtherMaterialEvent
```

Phase 4 V1 uses only earnings as an entry gate.

The Phase 4 strategy input is conceptually:

```text
EarningsContext

Status:
    Available
    Unavailable
    NotApplicable

NextEarningsDate
```

For an individual stock, missing required earnings data is `INSUFFICIENT_DATA`. For an ETF, earnings is `NotApplicable`.

For Phase 4 V1 production current evaluations, Application obtains the next earnings date through the provider-independent
`IEarningsDateSource` boundary using validated configuration-backed dates. This is not a reconstruction of what was known
historically; Phase 4E persists the exact `EarningsContext` consumed. A provider-backed source is deferred until an
authoritative provider contract or sanitized captured fixture is approved.

Dividend, ex-dividend, and generic material-event entry rules are deferred from Phase 4 V1. Ex-dividend information may be used by later defense/early-assignment logic.

# 16\. Recommendation

Every generated recommendation shall receive a permanent `RecommendationId`.

```text
RecommendationId
Timestamp
HoldingId
Symbol

RecommendationType
RecommendationStatus

CCOS
ContractScore
DRS
RQS

RecommendedContracts
RecommendedStrike
RecommendedExpiration
RecommendedPremium

EstimatedGrossIncome

AssignmentSensitivity
TaxSensitivity

StrategyVersion
ConfigurationVersion

Executed
ExecutionTransactionId

HumanReadableReason
```

Possible recommendation types:

```text
NO_TRADE
WATCH
SELL
HOLD
PROFIT_CLOSE
DEFENSE_REVIEW
ROLL
CLOSE_WAIT
URGENT_DEFENSE
```

Recommendations shall be retained whether or not they are executed.

---

## 16.1 Phase 4 Entry Strategy Evaluation

Phase 4 shall not create the final `Recommendation`. Position sizing belongs to Phase 5.

Every Phase 4 calculation that is persisted shall receive a permanent:

```text
EntryStrategyEvaluationId
```

An `EntryStrategyEvaluation` is an immutable historical strategy decision record. It shall preserve the actual inputs and outputs used at calculation time rather than relying on mutable current state.

At minimum it shall preserve:

```text
EntryStrategyEvaluationId
HoldingId
Symbol

IndicatorAsOfDate
EvaluationTimestampUtc
CalculatedAtUtc

IndicatorCalculationVersion
ConfigurationVersion
StrategyVersion

HoldingContext
IndicatorContext
EarningsContext
ResolvedStrategyConfiguration

CCOS result and component results
Underlying gate results

All evaluated call-contract observations
Derived contract metrics
Contract gate results
Contract Score component results
Contract ranking

EntryCandidateExists
PreferredInitialOptionSymbol
PreferredInitialStrike
PreferredInitialExpiration
PreferredInitialReferencePremium
DispositionReason

MissingInputs
Explanations
```

The captured HoldingContext shall include the actual Phase 4 holding-level settings consumed, including:

```text
AssetType
AssignmentSensitivity
TaxSensitivity
MaximumInitialDelta
PreferredDeltaMinimum
PreferredDeltaMaximum
MinimumCcos
MinimumContractScore
MinimumPremium
MinimumAnnualizedYield
```

Historical evaluation records are append-only. Later holding edits, market observations, indicator recalculations, configuration changes, or strategy-version changes shall not rewrite an earlier evaluation.

Detailed reproducibility payloads may use structured JSON, while high-value query fields may be relational columns. The persistence representation must not discard required inputs, component scores, gates, ranking, versions, or explanations.

### Phase 4 V1 HTTP Surface

Create a new immutable evaluation:

```http
POST /api/holdings/{holdingId}/entry-evaluations
```

The server owns market-input selection, indicator/configuration/strategy versions, and all calculations. A successful calculation is persisted and returns `201 Created`.

Retrieve an immutable historical evaluation:

```http
GET /api/entry-evaluations/{entryStrategyEvaluationId}
```

This is a passive lookup and shall not refresh data or recalculate any strategy result.

Retrieve lightweight holding history:

```http
GET /api/holdings/{holdingId}/entry-evaluations
```

History shall be newest first.

Phase 4 V1 public POST semantics are "evaluate now." Arbitrary historical evaluation timestamps, as-of dates, and client-selected versions are not exposed through the V1 HTTP surface; deterministic historical evaluation remains available internally for tests and future backtesting.

An unknown holding returns `404`. A disabled holding returns `409` with stable code `HOLDING_DISABLED` and does not create an evaluation.

A valid strategy outcome such as `INSUFFICIENT_DATA`, `CCOS_BELOW_MINIMUM`, `BREAKOUT_VETO`, or `NO_ACCEPTABLE_CONTRACT` is not an HTTP failure; it is persisted as a successful evaluation.

There are no Phase 4 V1 PUT, PATCH, or DELETE endpoints for an EntryStrategyEvaluation.

## 16.2 Phase 5 Position Sizing Evaluation

Phase 5 creates a separate immutable `PositionSizingEvaluation`. It does not rewrite the source `EntryStrategyEvaluation` and does not create the final `Recommendation`.

A later recommendation-assembly layer may combine:

```text
EntryStrategyEvaluation
+
PositionSizingEvaluation
```

into final SELL/recommended-contract semantics.

Every persisted Phase 5 sizing calculation shall receive a permanent:

```text
PositionSizingEvaluationId
```

and shall permanently reference:

```text
EntryStrategyEvaluationId
```

At minimum the sizing record shall preserve or be traceable to:

```text
PositionSizingEvaluationId
EntryStrategyEvaluationId

CalculatedAtUtc
SizingTimestampUtc

PositionSizingStatus
ReasonCodes
MissingInputs

PositionSizingHoldingContext

PhysicalCapacityContracts
ExistingCoveredContracts
ExistingCoveredShares
AvailableShares
AvailableContracts

CCOS
CCOSBaseCoverageRatio

AssignmentSensitivityModifier
AssignmentSensitivityMaximumRatio

PreferredContractScore
ContractQualityModifier

PortfolioConcentrationStatus
PortfolioWeight
ConcentrationModifier

RawCoverageRatio
DesiredCoverageRatio

DesiredTotalContracts
DesiredAdditionalContracts

ExistingDER
MaximumDER
DERLimitedAdditionalContracts

AdditionalContracts
ResultingTotalContracts

LimitingFactors

ConfigurationVersion
PositionSizingStrategyVersion

ResolvedPositionSizingConfiguration
Existing-call exposure snapshot
Existing-call Delta observations
Portfolio concentration input snapshot
Explanations
```

Historical sizing evaluations are append-only. Later changes to holdings, current short-call state, market observations, configuration, strategy versions, or the source Phase 4 evaluation shall not alter historical retrieval.

---

# 17\. Transaction Ledger

```text
TransactionId
CampaignId
RecommendationId
AccountId
HoldingId

Timestamp

Action
Contracts
OptionSymbol
Strike
Expiration

FillPrice
Fees

UnderlyingPriceAtExecution

Notes
```

Actions:

```text
STO
BTC
EXPIRE
ASSIGN
```

---

# 18\. Campaign

A Campaign represents an entire sequence of covered\-call activity originating from an initial STO\.

```text
CampaignId
HoldingId
AccountId
Symbol

StartDate
EndDate

InitialRecommendationId

InitialContracts
InitialStrike
InitialExpiration

Status

GrossPremium
BuyToCloseCost
RollCredits
RollDebits
Fees
NetOptionIncome

RollCount
MaximumDRS

Assigned
Expired

StrategyVersion
```

Campaign states:

```text
OPEN
ROLLED
CLOSED
EXPIRED
ASSIGNED
```

---

# 19\. Daily Position Snapshot

Every active short\-call position shall receive periodic snapshots\.

```text
PositionSnapshotId
CampaignId
Timestamp

UnderlyingPrice
OptionPrice

Strike
Expiration
DTE

Delta
Gamma
Theta
Vega
IV

StrikeDistance
ExpectedMoveRatio

UnrealizedPnL
PremiumCapturedPercent

DRS
DRSChange
DeltaVelocity

RecommendedAction

StrategyVersion
```

These records are critical for later evaluating defense rules\.

---

# 20. Strategy and Calculation Configuration

No important strategy or indicator constants shall be hard-coded.

Configuration shall be strongly typed and versioned.

At minimum:

```text
StrategyConfiguration
    ConfigurationVersion
    EffectiveDate
```

Configuration categories include:

```text
Indicators
CCOS
ContractScore
PositionSizing
DRS
RollEngine
ProfitTaking
TaxSensitivity
```

The Indicators category shall include parameters governing at least:

```text
SMA periods
RSI period
Bollinger period and standard-deviation multiplier
MACD periods
ATR period
Realized-volatility periods
Volatility annualization days
IV30 calculation parameters
IV historical lookback
IV Rank parameters
IV Percentile parameters
Resistance swing window
Resistance lookback
Resistance clustering
Market benchmark
Regime SMA periods
Regime slope lookback
Sector benchmark mappings
```

The PositionSizing category shall include at least:

```text
CCOS base-coverage bands
Assignment Sensitivity modifiers
Assignment Sensitivity maximum coverage
Contract Quality modifiers
stock concentration modifiers
```

IV30 calculation parameters, IV historical lookback, IV Rank parameters, and IV Percentile parameters become required once the V1 implied-volatility methodology is approved. No defaults are approved before that methodology is specified.

Indicator algorithm identity shall be represented separately from configuration values.

Conceptually:

```text
IndicatorCalculationVersion
ConfigurationVersion
StrategyVersion
```

Changing a parameter such as:

```text
RSI period 14 -> 21
```

is a configuration change.

Changing the RSI mathematical implementation from Wilder smoothing to another smoothing algorithm is a calculation-version change.

Changing how RSI contributes to CCOS is a strategy-version change.

For Position Sizing V1, tunable sizing tables and thresholds use the shared `ConfigurationVersion`. Position Sizing algorithmic behavior has explicit strategy-version identity and does not use or overload `IndicatorCalculationVersion`.

Every persisted indicator snapshot shall record or be traceable to:

```text
IndicatorCalculationVersion
ConfigurationVersion
```

Every persisted strategy evaluation/recommendation shall record or be traceable to its applicable configuration and strategy identities.

Historical records shall not be silently reinterpreted using newer configuration, calculation, or strategy versions.

---

# 21\. Covered Call Opportunity Score

CCOS answers:

> Should calls be sold on this underlying now?

Range:

```text
0–100
Higher = better opportunity
```

Components:

|Component           |Weight|
|--------------------|-----:|
|Implied volatility  |25    |
|RSI                 |15    |
|Bollinger Bands     |15    |
|Trend/Momentum      |15    |
|Resistance/Structure|15    |
|Market/Sector Regime|15    |

Classification:

```text
0–39    NO TRADE
40–54   WEAK
55–69   WATCH
70–79   SELL CANDIDATE
80–89   STRONG
90–100  EXCEPTIONAL
```

The default opening threshold is 70, but a Phase 4 evaluation uses the snapshotted `Holding.MinimumCcos`.

CCOS classification is descriptive. Entry eligibility is determined separately from classification by the holding threshold and approved hard gates.

## 21.1 Global Missing-Data Policy

Missing required financial data shall never become zero, neutral, an estimated substitute, or a passing gate.

If any required CCOS component cannot be calculated:

```text
CCOS = unavailable
```

Weights shall not be renormalized.

If any required Contract Score component cannot be calculated, that contract's Contract Score is unavailable and the contract is excluded from ranking.

A missing gate input produces `INSUFFICIENT_DATA`, with the specific missing input recorded. A present value that violates a business threshold produces the corresponding threshold failure code.

`NotApplicable` is distinct from `Unavailable`.

One bad contract shall not make otherwise independent contracts unavailable.

## 21.2 Scoring-Band Boundary Semantics

For scoring tables, an interior shared boundary belongs to the band beginning at that boundary unless the table explicitly states otherwise.

Explicit terminal `<` and `>` endpoints remain strict.

Every numeric boundary shall have deterministic tests immediately below, exactly at, and immediately above the boundary.

---

# 22\. Phase 4 Underlying Entry Gate

Phase 4 V1 has one underlying-level technical veto:

```text
BREAKOUT_VETO when:

BollingerPercentB > 1.15
AND RSI14 >= 75
AND MACDHistogram > 0
```

When this veto fires:

```text
EntryCandidateExists = false
```

CCOS is still calculated and retained for explainability when its required inputs are available. Contract scoring may stop after the veto.

Phase 4 V1 does not add volume breakouts, ATR breakout rules, bandwidth-expansion rates, acceleration rules, consecutive-close rules, or resistance-crossing rules.

Earnings, liquidity, delta, DTE, strike, premium, and yield are contract-level gates defined in Section 27.

Coverage, available shares, existing call exposure, Delta Exposure Ratio, maximum coverage, strike laddering, scaling, and recommended contract count belong to Phase 5.

`Holding.IsEnabled == false` is an Application-level exclusion before Phase 4 strategy evaluation, not a CCOS gate.

---

# 23\. CCOS Volatility Component

Maximum: 25.

```text
VolatilityScore =
    IVPercentileScore
    + IV30ToRV30Score
```

### IV Percentile — 15

```text
< 20             0
>= 20 and < 30   3
>= 30 and < 40   6
>= 40 and < 50   9
>= 50 and < 60  11
>= 60 and <= 70 13
> 70             15
```

### IV30 / RV30 — 10

```text
Ratio = IV30 / RV30

< 0.90                  0
>= 0.90 and < 1.00      2
>= 1.00 and < 1.10      4
>= 1.10 and < 1.20      6
>= 1.20 and <= 1.35     8
> 1.35                  10
```

IV30, IVPercentile, and RV30 are required. `RV30 <= 0` is invalid/unavailable for this calculation.

IVRank remains available as context/research data but does not contribute to CCOS V1.

---

# 24\. CCOS RSI Component

Maximum: 15.

```text
RSI < 40              0
>= 40 and < 50        2
>= 50 and < 55        5
>= 55 and < 60        8
>= 60 and < 65       11
>= 65 and < 70       15
>= 70 and < 75       13
>= 75 and <= 80       9
> 80                   5
```

RSI outside `0 <= RSI <= 100` is invalid/unavailable rather than scored.

---

# 25\. CCOS Bollinger, Trend, Resistance, and Regime Components

## 25.1 Bollinger Bands — 15

%B contributes up to 10:

```text
< .40                   0
>= .40 and < .60        2
>= .60 and < .75        5
>= .75 and < .90        8
>= .90 and < 1.05      10
>= 1.05 and <= 1.15     7
> 1.15                   3
```

If both Bollinger %B and Bollinger bandwidth are available, bandwidth contributes the remaining five points:

```text
BollingerScore =
    PercentBScore + 5
```

Phase 4 V1 does not calculate a bandwidth expansion rate or apply absolute bandwidth bands. Missing %B or bandwidth makes this component unavailable.

## 25.2 Trend/Momentum — 15

Award five points for each true condition:

```text
SMA20 > SMA50      +5
SMA50 > SMA200     +5
MACDHistogram > 0  +5
```

Equality receives zero for that check.

All three inputs are required. Missing any required value makes the component unavailable.

## 25.3 Resistance/Structure — 15

When Phase 3 reports:

```text
ResistanceUnavailableReason = NoQualifiedResistance
```

this is a valid structural state and scores `0 / 15`.

When Phase 3 reports `InsufficientData`, the component is unavailable.

For a qualified resistance level:

Distance score, maximum 5:

```text
DistanceToResistancePercent <= 2%             5
> 2% and <= 5%                                3
> 5%                                           1
```

Touch score, maximum 5:

```text
1 touch         0
2 touches       1
3 touches       3
>= 4 touches    5
```

A qualified one-touch resistance is valid when Phase 3 is configured with
`MinimumResistanceTouches = 1`. Its distance and recency subcomponents remain
scorable. It is neither `NoQualifiedResistance` nor `InsufficientData`.

Recency score, maximum 5:

```text
ResistanceAgeTradingDays <= 20      5
21–60                                3
> 60                                 1
```

The Phase 4 resistance component uses the Phase 3 facts as of `IndicatorAsOfDate`. It shall not recompute distance from the intraday option-chain underlying price.

## 25.4 Market/Sector Regime — 15

Market regime, maximum 8:

```text
NEUTRAL  8
BULLISH  4
BEARISH  0
```

Sector regime, maximum 7:

```text
NEUTRAL  7
BULLISH  3
BEARISH  0
```

If either regime is unavailable, the entire component is unavailable.

Phase 4 consumes the Phase 3 classifications; it does not recalculate regime from benchmark moving averages.

---

# 26\. Contract Score

Contract Score answers:

> Which eligible call is the best Phase 4 entry candidate?

Range:

```text
0–100
Higher = better
```

Weights:

|Component         |Weight|
|------------------|-----:|
|Delta             |25    |
|Strike safety     |20    |
|Premium efficiency|20    |
|DTE efficiency    |10    |
|IV/volatility edge|10    |
|Liquidity         |10    |
|Theta efficiency  |5     |

Classification:

```text
< 60     REJECT
60–69    WEAK
70–79    ACCEPTABLE
80–89    GOOD
90–100   EXCELLENT
```

Classification is descriptive. Entry acceptability uses the snapshotted `Holding.MinimumContractScore`.

## 26.1 Delta — 25

After the delta hard gate passes:

```text
EffectivePreferredDeltaMaximum =
    min(
        Holding.PreferredDeltaMaximum,
        EffectiveMaximumDelta
    )
```

Score:

```text
Delta < PreferredDeltaMinimum                         20
PreferredDeltaMinimum <= Delta <= EffectivePreferredDeltaMaximum 25
EffectivePreferredDeltaMaximum < Delta <= EffectiveMaximumDelta  10
```

The effective preferred range is inclusive.

Configuration is invalid if `PreferredDeltaMinimum > EffectivePreferredDeltaMaximum`.

## 26.2 Strike Safety — 20

Use:

```text
OTMPercent =
(Strike - UnderlyingPrice) / UnderlyingPrice
```

After the strict-OTM hard gate passes:

```text
0% < OTM < 1%       0
>= 1% and < 2%      4
>= 2% and < 3%      8
>= 3% and < 4%     12
>= 4% and < 6%     15
>= 6% and <= 8%    18
> 8%                20
```

Resistance and `StrikeVsResistance` do not contribute to Contract Score V1.

## 26.3 Premium Efficiency — 20

Use:

```text
PremiumEfficiencyRatio =
AnnualizedPremiumYield / Holding.MinimumAnnualizedYield
```

The holding minimum must be greater than zero.

Only contracts that pass the minimum-yield hard gate are scored:

```text
>= 1.00 and < 1.10    0
>= 1.10 and < 1.25    5
>= 1.25 and < 1.50   10
>= 1.50 and < 2.00   15
>= 2.00               20
```

`MinimumPremium` remains a hard dollar floor and does not add Premium Efficiency points.

## 26.4 DTE Efficiency — 10

Within the inclusive 14–45 DTE entry window:

```text
14–20   5
21–35  10
36–45   8
```

Phase 4 V1 adds no holding-specific preferred-DTE setting.

## 26.5 IV/Volatility Edge — 10

Use the candidate contract's normalized provider-supplied IV against Phase 3 RV30:

```text
ContractVolatilityEdge =
Contract.ImpliedVolatility / RV30
```

Score:

```text
< 0.90                  0
>= 0.90 and < 1.00      2
>= 1.00 and < 1.10      4
>= 1.10 and < 1.20      6
>= 1.20 and <= 1.35     8
> 1.35                  10
```

Contract IV and RV30 must be finite and strictly positive.

Phase 4 shall not reconstruct IV30. IV30 and IVRank do not contribute to this Contract Score component.

## 26.6 Liquidity — 10

```text
LiquidityScore =
SpreadScore + OpenInterestScore
```

Spread score, maximum 5:

```text
BidAskSpreadPercent <= 5%              5
> 5% and <= 10%                         4
> 10% and <= 15%                        2
> 15% and <= 20%                        1
```

Open-interest score, maximum 5:

```text
100–249       1
250–499       2
500–999       3
1,000–1,999   4
>= 2,000      5
```

Daily option volume does not affect Phase 4 V1 liquidity eligibility or Contract Score. It may still be retained for research.

## 26.7 Theta Efficiency — 5

Use:

```text
ThetaEfficiencyRatio =
(-Theta) / ReferencePremium
```

Score:

```text
< 1%                 0
>= 1% and < 2%       1
>= 2% and < 3%       2
>= 3% and < 4%       3
>= 4% and < 5%       4
>= 5%                5
```

Theta must be finite and strictly negative. Phase 4 shall not repair an unexpected sign with `abs(Theta)`.

The ratio is a Greek-based decay characteristic, not a forecast of realized daily option P&L.

---

# 27\. Contract Hard Gates, Ranking, and Initial Selection

## 27.1 Candidate Universe and Observation Selection

For a Phase 4 evaluation:

```text
EvaluationDate =
America/New_York calendar date corresponding to EvaluationTimestampUtc

DTE =
ExpirationDate - EvaluationDate
```

Application orchestration should retrieve expirations within the configured 14–45 DTE entry-search window, while Strategy shall validate DTE defensively.

For each expiration, select the latest complete persisted normalized option-chain observation with:

```text
ChainTimestamp <= EvaluationTimestampUtc
```

Never mix contract observations across chain timestamps within one expiration.

A newer empty chain supersedes an older populated chain; do not fall back.

Phase 4 V1 candidates are calls only. After puts are discarded, do not silently prefilter calls before hard-gate evaluation. Rejected calls shall remain observable with reasons.

For contract calculations, use the selected chain's provider-supplied `UnderlyingPrice`.

## 27.2 Contract Hard Gates

A call is contract-eligible only when all required gates pass.

### DTE

```text
14 <= DTE <= 45
```

Failure code:

```text
DTE_OUTSIDE_RANGE
```

### Strike

Require strictly OTM:

```text
Strike > UnderlyingPrice
```

Failure code:

```text
STRIKE_NOT_OTM
```

### Delta

```text
EffectiveMaximumDelta =
min(
    configured global maximum,
    Holding.MaximumInitialDelta,
    configured high-tax maximum when TaxSensitivity == High
)
```

Default configured values:

```text
GlobalMaximumInitialDelta = .25
HighTaxMaximumDelta = .20
```

Require call Delta to be finite and within `0 <= Delta <= 1`.

A valid Delta greater than `EffectiveMaximumDelta` fails with:

```text
DELTA_EXCEEDS_MAXIMUM
```

Missing, non-finite, or invalid Delta produces `INSUFFICIENT_DATA`.

### Earnings

For an individual stock with available earnings context, reject when:

```text
NextEarningsDate <= Expiration
```

including same-day earnings.

Failure code:

```text
EARNINGS_BEFORE_EXPIRATION
```

Missing required stock earnings data produces `INSUFFICIENT_DATA`. ETF earnings is `NotApplicable`.

### Liquidity

Require:

```text
Bid > 0
Ask > Bid
OpenInterest >= 100
BidAskSpreadPercent <= 20%
```

The open-interest minimum and maximum spread are configurable.

Present values that fail these thresholds produce:

```text
INSUFFICIENT_LIQUIDITY
```

Missing Bid, Ask, or OpenInterest produces `INSUFFICIENT_DATA`.

### Premium Floor

Require:

```text
ReferencePremium >= Holding.MinimumPremium
```

Failure code:

```text
PREMIUM_BELOW_MINIMUM
```

Equality passes.

### Annualized Yield Floor

Require:

```text
AnnualizedPremiumYield >= Holding.MinimumAnnualizedYield
```

Failure code:

```text
ANNUALIZED_YIELD_BELOW_MINIMUM
```

Equality passes.

All determinable gate failures shall be retained. Do not stop after the first failure.

`PreferredDeltaMinimum` and `PreferredDeltaMaximum` influence Contract Score only; they are not hard gates.

Coverage, available shares, existing call exposure, DER, and maximum coverage are Phase 5 concerns and are not Phase 4 contract gates.

## 27.3 Ranking

Rank every hard-gate-passing contract with a complete Contract Score, including contracts below the holding's minimum Contract Score.

Primary order:

```text
ContractScore DESC
```

Tie-break in this exact order:

```text
Delta ASC
OTMPercent DESC
AnnualizedPremiumYield DESC
BidAskSpreadPercent ASC
OpenInterest DESC
DTE ASC
OptionSymbol ordinal ASC
```

Contracts with a hard-gate failure or unavailable Contract Score are not ranked.

Provider/input enumeration order shall not influence ranking.

Contract analysis may still be produced when CCOS is below its minimum threshold, provided the contract's own required inputs are available. Such analysis does not make the holding entry-eligible.

## 27.4 Preferred Initial Contract and Overall Disposition

Phase 4 does not run a separate initial-strike optimizer.

```text
PreferredInitialContract =
highest-ranked EntryAcceptable contract

PreferredInitialStrike =
PreferredInitialContract.Strike
```

`EntryCandidateExists = true` only when:

```text
CCOS is available
AND no underlying-level veto is present
AND CCOS >= Holding.MinimumCcos
AND at least one contract:
    passes every hard gate
    has a complete Contract Score
    ContractScore >= Holding.MinimumContractScore
```

Phase 4 overall disposition reasons include:

```text
ENTRY_CANDIDATE
CCOS_BELOW_MINIMUM
BREAKOUT_VETO
NO_ACCEPTABLE_CONTRACT
INSUFFICIENT_DATA
```

If CCOS is below its threshold, no preferred contract is selected, but ranked contract analysis may still be returned.

If CCOS passes but no contract qualifies, use `NO_ACCEPTABLE_CONTRACT` and retain per-contract failure/score details.

If CCOS is unavailable, use `INSUFFICIENT_DATA`; independently calculable contract analysis may still be retained.

Phase 4 terminology is:

```text
EntryCandidateExists
PreferredInitialContract
PreferredInitialStrike
```

Final `Recommendation`, `RecommendedContracts`, and SELL semantics remain downstream of Phase 5.

## 27.5 Explainability Result Contract

Scores shall expose structured component results rather than opaque totals.

Conceptually:

```text
ScoreComponentResult
    Code
    Name
    Status
    Score
    MaximumScore
    Inputs[]
    Explanation
```

Gate results are separate:

```text
GateResult
    Code
    Status:
        Passed
        Failed
        Unavailable
        NotApplicable
    Inputs[]
    Explanation
```

Preserve all evaluated gate outcomes, not merely failures.

`INSUFFICIENT_DATA` shall include stable machine-readable `MissingInputs` identifying the actual unavailable inputs.

CCOS and Contract Score shall expose:

```text
Status
Score
MaximumScore
Classification
MinimumRequiredScore
MeetsMinimumScore
Components
MissingInputs
Explanation
```

Stable component, gate, reason, and missing-input codes are API contracts. Human-readable explanations accompany them but shall not be parsed to reconstruct strategy behavior.

API/UI consumers shall not recalculate scoring, gates, eligibility, ranking, or disposition.

# 28\. Position Sizing Engine

The Position Sizing Engine answers two related questions:

> What total covered-call position should exist?

and:

> How many additional contracts may be added now?

Phase 5 consumes an immutable Phase 4 `EntryStrategyEvaluation`.

V1 sizes only:

```text
Phase4.PreferredInitialContract
```

It does not recalculate CCOS, Contract Score, contract gates, ranking, or preferred-contract selection.

If `EntryCandidateExists == false`, Phase 5 may create an auditable sizing evaluation with:

```text
Status = NotApplicable
AdditionalContracts = 0
Reason = NO_ENTRY_CANDIDATE
```

This is not an error and not `InsufficientData`.

Authoritative share count:

```text
SharesOwned = Holding.Shares
```

Maximum physical capacity:

```text
PhysicalCapacityContracts =
    floor(SharesOwned / 100)
```

Fractional shares and nonmultiples of 100 do not create fractional contracts.

`SharesOwned < 0` is invalid input.

`SharesOwned == 0` is a valid zero-capacity state when no open short-call obligation exists. Zero shares with existing short calls is an inconsistent exposure state.

Base CCOS sizing:

```text
CCOS < 70         0.00
70 <= CCOS < 75   0.20
75 <= CCOS < 80   0.30
80 <= CCOS < 85   0.40
85 <= CCOS < 90   0.50
90 <= CCOS < 95   0.60
95 <= CCOS <=100  0.70
```

Interior boundaries enter the band beginning at that boundary.

If a custom Phase 4 holding threshold permits an entry candidate with CCOS below 70, the Position Sizing base coverage remains zero.

The engine shall not default to 100% coverage.

### Existing Covered-Call Exposure

Phase 5 V1 requires a minimal current-state representation of net open short call positions for the same Holding.

It does not require Phase 7 transaction-ledger or campaign accounting.

Existing exposure includes only currently open short call contracts associated with the same Holding. It excludes long calls, closed/expired calls, other Holdings, unexecuted recommendations, pending brokerage orders, and hypothetical future orders.

```text
ExistingCoveredShares =
    ExistingCoveredContracts * 100

AvailableShares =
    max(
        0,
        SharesOwned - ExistingCoveredShares
    )

AvailableContracts =
    floor(AvailableShares / 100)
```

If existing obligations exceed physical capacity, Phase 5 returns zero additional contracts with an explicit limiting/anomaly reason.

Pending brokerage orders are not modeled and do not reserve shares in Phase 5 V1.

---

# 29\. Assignment Sensitivity

Both an Assignment Sensitivity sizing modifier and a maximum total coverage cap apply.

```text
ASL1  modifier 1.25   maximum 1.00
ASL2  modifier 1.10   maximum 0.90
ASL3  modifier 1.00   maximum 0.80
ASL4  modifier 0.85   maximum 0.70
ASL5  modifier 0.70   maximum 0.50
```

The former `ASL5 = 50–60%` ambiguity is resolved to:

```text
ASL5 maximum = 0.50
```

as the V1 default.

Assignment Sensitivity values are configurable through Position Sizing configuration.

The modifier is applied in raw coverage. The maximum is subsequently applied as a total-coverage cap.

---

# 30\. Contract Quality Sizing Modifier

Phase 5 uses the persisted Contract Score of the Phase 4 preferred contract.

```text
Contract Score < 80         0.00
80 <= score < 85            0.90
85 <= score < 90            1.00
90 <= score < 95            1.10
95 <= score <= 100          1.15
```

If custom Phase 4 thresholds produce an entry candidate whose preferred Contract Score is below 80, Position Sizing returns a valid zero-size outcome from the zero Contract Quality modifier rather than inventing a new band.

---

# 31\. Concentration Modifier

V1 concentration is tracked-account equity concentration.

Portfolio scope is the target Holding's own Account.

All tracked holdings in the same Account participate in the denominator whether or not covered-call strategy is enabled for them.

Cash and untracked assets are excluded.

For reproducibility, each participating holding uses the latest persisted daily closing price at or before:

```text
Phase4.IndicatorAsOfDate
```

```text
HoldingMarketValue =
    Holding.Shares * AsOfPrice

PortfolioWeight =
    TargetHoldingMarketValue
    /
    Sum(MarketValue of tracked Holdings in the same Account)
```

Stock rules:

```text
weight < 5%           1.10
5% <= weight < 15%    1.00
15% <= weight < 25%   0.90
25% <= weight <= 40%  0.80
weight > 40%          0.70
```

Boundary semantics:

```text
5%  -> 1.00
15% -> 0.90
25% -> 0.80
40% -> 0.80
>40% -> 0.70
```

If any participating tracked holding lacks the required as-of price, concentration is unavailable and stock Position Sizing becomes `InsufficientData`.

The engine shall not ignore the missing holding, substitute zero, choose an unapproved substitute date, or renormalize around missing data.

For `AssetType.ExchangeTradedFund`:

```text
ConcentrationStatus = NotApplicable
ConcentrationModifier = 1.00
```

This is not represented as a fabricated zero portfolio weight.

`AssetType.Other` has no approved V1 concentration rule and therefore produces an unsupported/insufficient sizing outcome rather than silently applying stock or ETF behavior.

---

# 32\. Position Sizing Formula

All Position Sizing percentages are fractional ratios:

```text
0.20 = 20%
0.70 = 70%
1.00 = 100%
```

The exact calculation sequence is:

```text
RawCoverageRatio =
    CcosBaseCoverageRatio
    * AssignmentSensitivityModifier
    * ContractQualityModifier
    * ConcentrationModifier

DesiredCoverageRatio =
    min(
        RawCoverageRatio,
        AssignmentSensitivityMaximumRatio,
        Holding.MaximumCoveragePercent
    )

DesiredTotalContracts =
    floor(
        SharesOwned
        * DesiredCoverageRatio
        / 100
    )

DesiredAdditionalContracts =
    max(
        0,
        DesiredTotalContracts - ExistingCoveredContracts
    )

PhysicalLimitedAdditionalContracts =
    min(
        DesiredAdditionalContracts,
        AvailableContracts
    )

AdditionalContracts =
    min(
        PhysicalLimitedAdditionalContracts,
        DERLimitedAdditionalContracts
    )

ResultingTotalContracts =
    ExistingCoveredContracts
    + AdditionalContracts
```

Coverage-to-contract conversion always floors.

The prior statement that tax-sensitive positions shall round contract counts downward is removed as redundant. All Position Sizing contract conversion already rounds downward.

Phase 5 introduces no additional tax-specific rounding penalty.

---

# 33\. Delta Exposure Ratio

Simple covered percentage is insufficient.

For existing short calls:

```text
ExistingDeltaShares =
    SUM(
        ExistingContracts_i
        * ExistingCallDelta_i
        * 100
    )

ExistingDER =
    ExistingDeltaShares
    / SharesOwned
```

For `N` proposed additional preferred contracts:

```text
ProposedDER(N) =
    (
        ExistingDeltaShares
        + N * PreferredContractDelta * 100
    )
    / SharesOwned
```

`DERLimitedAdditionalContracts` is the largest non-negative integer `N` within the already physically eligible action range:

```text
0 <= N <= PhysicalLimitedAdditionalContracts
```

that satisfies:

```text
ProposedDER(N)
    <= Holding.MaximumDeltaExposureRatio
```

Equality is allowed.

DER constrains the already-calculated physically eligible action; it does not calculate an unbounded theoretical contract capacity.

If `PreferredContractDelta == 0` and existing DER is within the configured maximum, then:

```text
DERLimitedAdditionalContracts =
    PhysicalLimitedAdditionalContracts
```

because adding any physically eligible preferred contracts does not increase DER.

Call Delta consumed by Phase 5 must be finite and within `[0,1]`.

The engine shall not repair malformed Delta with `abs(Delta)`.

Different existing short-call positions use their own Deltas.

For the proposed new position, Phase 5 uses the Delta persisted in the immutable Phase 4 preferred-contract observation.

Existing short-call exposure uses the latest persisted normalized option observation for that option symbol at or before `SizingTimestampUtc`. The actual observation and timestamp consumed shall be preserved in the immutable sizing evaluation.

Strategy does not fetch provider data.

Phase 5 V1 defines no hidden Delta freshness threshold.

Missing or invalid required Delta produces `InsufficientData`.

If existing DER is above the maximum:

```text
AdditionalContracts = 0
```

with an explicit limiting factor.

If existing DER equals the maximum, equality remains allowed. Therefore:

- when `PreferredContractDelta > 0`, no positive additional count can remain within the maximum and `AdditionalContracts = 0`;
- when `PreferredContractDelta == 0`, the approved zero-Delta rule applies and DER does not further reduce `PhysicalLimitedAdditionalContracts`.

Phase 5 does not recommend closing an existing call merely because DER or target coverage is already exceeded.

Zero shares with existing short calls is an inconsistent exposure state rather than a DER division.

---

# 34\. Strike Laddering

Strike laddering is deferred from initial Phase 5 V1.

Phase 5 V1 sizes only:

```text
Phase4.PreferredInitialContract
```

The previously proposed future tiers remain product intent only:

```text
Conservative    .10–.13
Core            .14–.17
Income          .18–.20
```

No V1 allocation algorithm exists for percentages, minimum lot sizes, remainder distribution, missing tiers, Contract Score interaction, or DER interaction.

Expiration laddering also remains deferred.

---

# 35\. Scaling

Position Sizing distinguishes desired target state from immediate additional action.

It returns at least:

```text
DesiredCoverageRatio
DesiredTotalContracts
DesiredAdditionalContracts
AdditionalContracts
ResultingTotalContracts
```

Semantics:

```text
existing < desired
    -> add contracts subject to physical and DER limits

existing == desired
    -> AdditionalContracts = 0

existing > desired
    -> AdditionalContracts = 0
       and report EXISTING_COVERAGE_ABOVE_TARGET
```

A lower newly calculated target never causes Phase 5 to recommend BTC/close behavior.

Declining CCOS combined with profitable or risky existing calls belongs to later profit-taking/defense behavior.

### Status Semantics

Top-level sizing status distinguishes:

```text
Available
InsufficientData
NotApplicable
```

An `Available` result may legitimately contain zero additional contracts.

Valid zero-size examples include:

```text
CCOS base coverage is zero
Contract Quality modifier is zero
target rounds below one contract
existing coverage meets/exceeds target
no remaining physical capacity
Holding maximum reached
Assignment Sensitivity maximum reached
DER maximum reached
```

`EntryCandidateExists == false` produces `NotApplicable`.

Missing required sizing inputs produce `InsufficientData`; missing values never become zero.

---

# 36\. Defense Risk Score

DRS answers:

> How urgently does this open call require defensive action?

Range:

```text
0–100
Higher = greater risk
```

Weights:

|Component        |Weight|
|-----------------|-----:|
|Delta            |30    |
|Strike proximity |20    |
|Momentum/breakout|15    |
|DTE              |10    |
|Premium expansion|10    |
|Expected move    |10    |
|Dividend/event   |5     |

---

# 37\. DRS Classification

```text
0–19     SAFE
20–34    NORMAL
35–49    WATCH
50–64    DEFEND
65–79    HIGH RISK
80–100   CRITICAL
```

---

# 38\. Delta Defense Component

```text
Delta <.15       0
.15–.20          3
.20–.25          7
.25–.30         12
.30–.35         18
.35–.40         23
.40–.50         27
>.50            30
```

Delta velocity:

```text
CurrentDelta - PreviousTradingDayDelta
```

Delta increase of at least \.10 in one trading day adds a configurable risk penalty\.

---

# 39\. Strike Proximity Defense

```text
Distance >8%       0
6–8%               2
4–6%               5
3–4%               8
2–3%              12
1–2%              16
0–1%              20
ITM               20
```

---

# 40\. DTE Defense

```text
>21 DTE      0
15–21        2
10–14        4
7–9          6
4–6          8
1–3         10
Expiration  10
```

Additional risk penalty:

```text
DTE <= 7 AND Delta >= .30
```

---

# 41\. Premium Expansion

Calculate:

```text
PremiumMultiple =
CurrentOptionPrice / OriginalSalePrice
```

Score:

```text
<.50        0
.50–1.00    1
1.00–1.25   3
1.25–1.50   5
1.50–2.00   7
2.00–3.00   9
>3.00      10
```

Premium expansion shall not independently trigger automatic closure\.

---

# 42\. Expected\-Move Defense

Calculate remaining expected move through expiration\.

Then:

```text
StrikeDistance / ExpectedMove
```

Score:

```text
>2.0       0
1.5–2.0    2
1.25–1.5   4
1.0–1.25   6
.75–1.0    8
<.75      10
```

---

# 43\. Defense Hard Triggers

Mandatory defense evaluation occurs if:

```text
Delta >= .40

Underlying within 1% of strike
AND Delta >= .30

Call becomes ITM

DTE <= 3
AND Delta >= .25

Technical breakout toward strike

Material early-assignment condition

Delta increases >= .15 in one trading session
```

These conditions do not automatically mean BTC\.

They activate the Roll Engine\.

---

# 44\. Profit Taking

Profit\-taking shall remain independent from DRS\.

Default initial thresholds:

```text
50% captured -> Monitor
70% captured -> Close candidate
80% captured -> Strong close candidate
```

Example:

```text
DRS = 5
Premium captured = 82%
```

Recommended action may still be:

```text
PROFIT_CLOSE
```

because remaining premium does not justify continuing exposure\.

---

# 45\. Roll Engine

The Roll Engine answers:

> What action best reduces assignment risk while preserving acceptable campaign economics?

When defense is triggered, first calculate current CCOS\.

Initial decision rules:

```text
Current CCOS >=70
    Roll candidates favored

Current CCOS 55–69
    Evaluate Roll vs Close/Wait

Current CCOS <55
    Close/Wait favored
```

The system shall not blindly replace a call when the current covered\-call environment is poor\.

---

# 46\. Roll Candidate Universe

Initial defensive roll search:

```text
Preferred new DTE: 21–45
Maximum defensive DTE: 60

Preferred Delta: .12–.18
Normal maximum Delta: .25
Highly tax-sensitive maximum: .20
```

Preferred defensive action:

```text
ROLL UP
ROLL OUT
ROLL UP & OUT
```

For assignment\-sensitive holdings, roll\-up\-and\-out shall generally be favored\.

---

# 47\. Roll Economics

For each candidate:

```text
BTC = Current call close cost

STO = Replacement call proceeds

NetRoll = STO - BTC
```

Positive:

```text
Credit
```

Negative:

```text
Debit
```

Debit rolls shall not automatically be rejected\.

---

# 48\. Roll Candidate Metrics

Every candidate shall calculate:

```text
NewStrike
StrikeImprovement
StrikeImprovementPercent

NewDelta
DeltaReduction
DeltaReductionPercent

NewDTE
AdditionalDTE

NetRollCreditDebit

ProjectedDRS
DRSReduction

NewContractScore

IncrementalPremiumPerAdditionalDay

CampaignPremiumAfterRoll
```

---

# 49\. Roll Quality Score

RQS ranks eligible defensive roll candidates\.

Range:

```text
0–100
Higher = better
```

Weights:

|Component           |Weight|
|--------------------|-----:|
|DRS reduction       |30    |
|Delta reduction     |20    |
|Strike improvement  |20    |
|Roll economics      |15    |
|New contract quality|10    |
|Time efficiency     |5     |

---

# 50\. Roll Hard Gates

Normally reject if:

```text
New Delta > .25

Highly tax-sensitive new Delta > .20

Projected DRS >=40

New strike <= existing strike
    when performing defensive roll

New expiration crosses unacceptable earnings event

Liquidity unacceptable
```

Exceptions shall require explicit override and shall be recorded\.

---

# 51\. Maximum Roll Debit

Configuration shall support three limits\.

## Absolute Debit

Example:

```text
Maximum = $2/share
```

## Campaign Income Limit

Example:

```text
RollDebit <=
50% of cumulative campaign premium
```

## Tax\-Defense Exception

If estimated assignment tax exposure materially exceeds the roll debit, the system may recommend exceeding normal debit limits\.

The recommendation must explicitly explain the tradeoff\.

---

# 52\. Early Assignment Alert

For an ITM call:

```text
IntrinsicValue =
MAX(UnderlyingPrice - Strike, 0)

ExtrinsicValue =
OptionPrice - IntrinsicValue
```

When an upcoming ex\-dividend event exists, the system shall evaluate remaining extrinsic value relative to the dividend and generate an early\-assignment warning when warranted\.

This shall be treated as a hard defense condition rather than merely another weighted score\.

---

# 53\. Campaign Accounting

Campaign P&L:

```text
NetCampaignIncome =
STO Premiums
- BTC Costs
- Fees
```

Example:

```text
Initial STO       +600
BTC              -1100
Replacement STO   +850
BTC               -400
Replacement STO   +500
Expiration           0
----------------------
Net                +450
```

All rolls remain associated with the same CampaignId\.

---

# 54\. Performance Metrics

Portfolio reporting shall include:

```text
Gross Premium
Net Option Income
BTC Costs
Roll Credits
Roll Debits
Fees

Monthly Income
Annualized Income Yield
Income per $100,000 Underlying

Contracts Sold
Campaign Count

Expiration Rate
Early Close Rate
Roll Rate
Assignment Rate

Average Entry DTE
Average Entry Delta
Average Premium Capture
Average Campaign Duration

Best Campaign
Worst Campaign

Maximum DRS
Average DRS
```

---

# 55\. Buy\-and\-Hold Benchmark

The system shall compare:

```text
Underlying-only return
```

against:

```text
Underlying return
+ net covered-call income
- relevant strategy costs
```

Primary comparison:

```text
StrategyContribution =
CoveredCallStrategyReturn
- BuyAndHoldReturn
```

The system shall expose cases where the covered\-call strategy reduced total return\.

---

# 56\. Research Dataset

All recommendations shall be retained, including recommendations the user does not execute\.

This creates both:

- executed observations;
- counterfactual observations\.

Store raw features including:

```text
Price
CCOS
RSI14
BB%B
BBWidth
IV30
IVPercentile
IVRank
RV30
IV/RV

SMA20
SMA50
SMA200

MACD

Resistance
DistanceToResistance

MarketRegime
SectorRegime

Volume
ATR
```

Option data:

```text
Strike
DTE
Delta
Gamma
Theta
Vega
IV
Bid
Ask
Volume
OpenInterest
```

Future research shall support:

- feature correlation;
- regression;
- indicator ablation;
- parameter sensitivity;
- delta\-bucket analysis;
- DTE\-bucket analysis;
- walk\-forward testing;
- out\-of\-sample testing\.

Weights shall not be changed merely because an indicator performs well in\-sample\.

---

# 57\. Market Data Provider Interface

Conceptual interface:

```text
IMarketDataProvider

GetQuoteAsync(symbol)

GetHistoricalPricesAsync(
    symbol,
    start,
    end)

GetOptionExpirationsAsync(symbol)

GetOptionChainAsync(
    symbol,
    expiration)
```

Tradier shall implement this interface\.

Provider APIs shall be asynchronous and cancellation\-aware\.

---

# 58\. Tradier Integration

Tradier shall be the V1 market-data provider.

V1 shall integrate exclusively with the Tradier production market-data API.

Tradier sandbox integration is explicitly out of scope.

`TradierMarketDataProvider` shall be responsible for:

- authentication;
- HTTP communication;
- rate-limit handling;
- provider DTO deserialization;
- retries where appropriate;
- error translation;
- mapping Tradier data to normalized provider-independent models.

Tradier-specific objects shall remain inside the MarketData/Infrastructure boundary.

Strategy and domain code shall never depend upon Tradier-specific DTOs or APIs.

Tradier authentication shall use a personal API access token supplied through secure application configuration.

Secrets shall not be stored in source control.

Automated tests shall not require access to the live Tradier API.

Provider integration tests shall use mocked HTTP responses and sanitized production-shaped Tradier response fixtures.

A limited set of explicitly invoked read-only production smoke tests may be supported for development verification, but they shall:

- be disabled by default;
- require explicit developer configuration;
- never execute during normal automated testing or CI;
- never access brokerage positions or accounts;
- never submit, modify, or cancel orders.

The architecture shall continue to preserve the `IMarketDataProvider` abstraction so additional providers can be introduced later without changing strategy engines\.

---

# 59\. Market Data Caching

SQLite shall serve as both application database and market\-data cache\.

The system shall avoid unnecessary API requests\.

Example behavior:

```text
Request chain
    |
Check recent cached snapshot
    |
Fresh?
 /    \
Yes    No
 |      |
Use    Query Tradier
        |
        Save snapshot
        |
        Return
```

Cache freshness shall be configurable by data type\.

---

# 60. Historical Market Data and Snapshot Strategy

The system shall persist sufficient historical market data to:

- reproduce historical recommendations;
- analyze strategy decisions;
- evaluate strategy performance;
- perform future backtesting;
- compare executed and unexecuted recommendations;
- perform parameter sensitivity analysis;
- evaluate alternative contract-selection rules.

SQLite shall serve as both the V1 operational database and the V1 historical market-data store.

Historical market observations shall preserve the provider and observation time necessary to establish where and when the data originated.

## 60.1 Historical Underlying Price Data

The system shall persist historical daily OHLCV price bars for supported underlying securities.

Each historical daily bar shall preserve at minimum:

```text
Symbol
Trading Date
Open
High
Low
Close
Volume
Provider
```

The initial system should maintain at least two years of daily price history when available from the market-data provider.

Historical daily bars shall be idempotent.

A logical historical daily observation is identified by:

```text
Symbol
Trading Date
Provider
```

Repeated retrieval of the same historical bar must not create uncontrolled duplicate records.

The persistence layer may update an existing daily bar when the provider returns corrected or more complete data, provided the behavior is deterministic.

Historical price data must not be assumed to be dividend-adjusted unless that characteristic is explicitly known from the provider data.

## 60.2 Quote Snapshots

Current underlying quotes shall be persisted as timestamped market observations where useful for recommendation reproducibility and auditing.

A quote observation shall preserve at minimum:

```text
Symbol
Observation Timestamp
Provider
Available Quote Values
```

New quote observations shall not silently overwrite historical quote observations required to reproduce previous recommendations.

Cached quotes must preserve their original observation timestamp.

Returning a cached quote must not cause the system to represent the cached data as newly observed market data.

## 60.3 Option-Chain Snapshots

Option-chain snapshots shall be treated as append-only historical market observations.

Each option-chain retrieval may contain many individual option contract observations.

The system shall retain these observations across time for:

- recommendation reconstruction;
- strategy research;
- contract-selection analysis;
- assignment-risk research;
- roll analysis;
- backtesting;
- future statistical analysis.

The OCC option symbol identifies an option contract.

It does not uniquely identify a market observation.

A persisted option observation shall therefore be uniquely distinguishable by at least:

```text
Option Contract
Provider
Observation Timestamp
```

Multiple observations of the same option contract at different times must coexist.

A new option-chain retrieval must not overwrite the previous observation of the same OCC option contract.

Each option contract snapshot shall preserve at minimum:

```text
Underlying Symbol
OCC Option Symbol
Observation Timestamp
Expiration
Strike
Option Type
Bid
Ask
Last
Volume
Open Interest
Implied Volatility
Delta
Gamma
Theta
Vega
Underlying Price
Provider
```

Fields unavailable from the provider shall remain explicitly unavailable.

Missing financial values shall never be silently converted to zero.

In particular:

```text
Missing Delta != Delta of 0

Missing Implied Volatility != Implied Volatility of 0

Missing Open Interest != Open Interest of 0
```

This distinction must survive normalization, persistence, and API serialization where relevant.

## 60.4 Historical Option Dataset

The accumulated option snapshots shall form the application's own historical options research dataset.

This dataset is considered a long-term system asset.

Option snapshots shall therefore be retained even after:

- an option expires;
- the underlying recommendation is no longer active;
- a campaign closes;
- a recommendation was never executed.

Historical option observations shall not depend upon the continued availability of the same historical information from the external provider.

This enables future research such as:

```text
Delta bucket performance
DTE bucket performance
Premium efficiency
Strike-distance analysis
Expected-move analysis
Contract Score validation
DRS validation
Roll candidate analysis
Parameter sensitivity
Counterfactual contract selection
```

No such strategy research is required during Phase 2; Phase 2 is responsible only for collecting and preserving the data required to support it later.

## 60.5 Normalized Data

Historical persistence shall store normalized provider-independent market-data models.

Provider-specific DTOs shall not become the authoritative historical data model.

The normalized historical representation must permit additional market-data providers to be introduced later without requiring strategy engines to understand provider-specific response formats.

Provider identity shall nevertheless be retained on observations for provenance and auditing.

## 60.6 Raw Provider Responses

Raw provider-response retention is optional in V1.

If raw responses are retained, they shall:

- be stored separately from normalized market observations;
- contain no authentication tokens or authorization headers;
- not be required for normal strategy execution;
- be configurable so storage can be disabled.

Failure to retain raw responses shall not prevent implementation of normalized historical persistence.

Normalized historical observations are mandatory.

## 60.7 Data Freshness and Caching

Market-data freshness requirements shall vary by data type.

The system shall support configurable freshness policies for at least:

```text
Underlying Quotes
Historical Daily Bars
Option Expirations
Option Chains
```

SQLite may serve as the V1 market-data cache.

When cached data remains within its configured freshness period, the application may use it without requesting the provider again.

When cached data is stale or absent, the application should refresh it from the configured market-data provider.

Caching shall never alter the observation timestamp of the underlying market data.

The system must be able to distinguish:

```text
Time data was observed
```

from:

```text
Time cached data was retrieved by the application
```

where that distinction affects reproducibility or data freshness.

## 60.8 Recommendation Reproducibility

Historical market-data retention shall support reconstruction of the market context used to generate a recommendation.

A recommendation shall eventually be traceable to the relevant:

```text
Underlying market observation
Historical price data
Option contract observations
Calculated indicators
Strategy version
Configuration version
Recommendation timestamp
```

The persistence design shall not require historical recommendations to be recalculated using current market observations.

## 60.9 Retention

The following records shall be treated as long-term historical data:

```text
Daily Historical Price Bars
Option Contract Snapshots
Recommendation Inputs
Recommendations
Daily Position Snapshots
Transactions
Campaigns
Configuration Versions
```

Quote snapshots may use a more selective retention policy in the future if storage volume becomes significant, provided recommendation reproducibility is preserved.

Option contract snapshots shall be retained for research unless an explicit future data-retention policy supersedes this requirement.

## 60.10 V1 Storage Strategy

SQLite is sufficient for V1.

The persistence architecture shall avoid unnecessary coupling to SQLite-specific behavior so that a larger database system such as PostgreSQL can be introduced later if historical market-data volume requires it.

Database optimization shall initially prioritize:

1. correctness;
2. historical reproducibility;
3. deterministic persistence;
4. efficient symbol/date queries;
5. reasonable storage efficiency.

Premature implementation of a specialized time-series database, data lake, distributed cache, or warehouse is out of scope for V1.

---

# 61\. Application Services

Recommended application-level services:

```text
MarketDataService
IndicatorService
OpportunityService
ContractSelectionService
PositionSizingService
PositionMonitoringService
RollAnalysisService
CampaignService
TransactionService
PerformanceService
RecommendationService
```

For Phase 5, `PositionSizingService` / Application orchestration owns assembly of mutable current-state inputs such as Holding share/settings snapshots, same-account concentration inputs, and existing short-call exposure/Delta observations.

Quantitative Position Sizing formulas remain in Strategy.

---

# 62\. Strategy Interfaces

Conceptually:

```text
ICoveredCallOpportunityScorer

IContractScorer

IPositionSizingEngine

IDefenseRiskScorer

IRollEngine

IRollQualityScorer
```

Strategy implementations shall be deterministic given:

- inputs;
- configuration version;
- strategy version.

The Phase 5 Position Sizing Strategy is pure and synchronous. It receives provider-independent immutable inputs and must not depend on EF Core, SQLite, HTTP, ASP.NET, Tradier/provider DTOs, or current wall-clock time.

---

# 63\. Local Application API

The ASP\.NET Core API shall expose a local interface suitable for Excel and future clients\.

## Phase 3 Indicator API

V1 exposes one indicator route: `GET /api/indicators/{symbol}`, optionally with
`?asOf=YYYY-MM-DD`. This is a read-only HTTP surface: clients do not create,
update, or delete indicator snapshots through HTTP. GET is nevertheless a
calculation/read-through operation. On a successful request, the API shall
normalize and validate the symbol, resolve the as-of date and the server's
current indicator configuration and calculation version, invoke Application
indicator orchestration, calculate/recalculate and canonically upsert the
derived snapshot, and return the resulting normalized `IndicatorSnapshot`.
This internal derived-data write must not mutate source market observations.

An explicit `asOf` is used exactly as requested, including on non-trading
calendar dates; the returned snapshot retains that requested `AsOfDate` and
historical no-look-ahead rules apply. Without `asOf`, Application/Infrastructure
shall resolve the latest persisted trading-observation date for the requested
underlying symbol and configured provider at or before the applicable request
boundary. That persisted trading date, not the current UTC calendar date or a
fabricated trading day, becomes the returned `AsOfDate`. If no applicable
persisted price observation exists to resolve an omitted `asOf`, return a
not-found/unavailable response (`404 Not Found`); do not calculate using today's
date. An explicit historical `asOf` may still yield a valid snapshot with
individual indicators unavailable for insufficient history.

`IndicatorCalculationVersion` and `IndicatorConfiguration.Version` are
server-owned for this V1 calculation endpoint. The composition root shall
provide the currently supported calculation version and currently configured,
validated `IndicatorConfiguration` through configuration/dependency injection;
missing or invalid server configuration must fail clearly, not fall back to an
implicit version. Endpoint logic shall not hard-code version identities.
Requests attempting to supply calculation or configuration versions through
query parameters are invalid rather than silently selecting or ignoring them.
The response shall expose both version identifiers and preserve available versus
unavailable indicator values, including the IV30 unavailable reason. The
existing exact-identity Application/repository retrieval operation remains
required internally, but V1 exposes no second, version-selectable indicator
HTTP retrieval route.

## Phase 5 Position Sizing API

Phase 5 uses a separate immutable resource boundary and does not alter the established semantics of the Phase 4 entry-evaluation POST.

Conceptually:

```http
POST /api/entry-evaluations/{entryStrategyEvaluationId}/position-sizing-evaluations
GET /api/position-sizing-evaluations/{positionSizingEvaluationId}
```

A lightweight history endpoint may be added if required by the Phase 5 acceptance design.

The Phase 5 acceptance document shall lock exact creation, retrieval, history, not-found, not-applicable, insufficient-data, and immutability semantics before API implementation.

There are no Phase 5 PUT/PATCH/DELETE semantics for an immutable PositionSizingEvaluation.

Representative endpoints:

```text
GET /api/indicators/{symbol}

GET /api/holdings

GET /api/opportunities

GET /api/opportunities/{symbol}

GET /api/options/{symbol}

GET /api/recommendations

GET /api/positions

GET /api/positions/{id}

GET /api/positions/{id}/rolls

GET /api/campaigns

GET /api/campaigns/{id}

GET /api/performance

GET /api/configuration
```

Mutation endpoints may include:

```text
POST /api/holdings

POST /api/transactions

POST /api/recommendations/{id}/execute

PUT /api/configuration
```

The exact REST surface may evolve during implementation\.

---

# 64\. Excel Workbook

The initial workbook shall contain these user\-facing areas:

## Dashboard

Primary daily decision screen\.

## Holdings

Portfolio configuration\.

## Opportunities

CCOS and new\-call opportunities\.

## Contract Analysis

Ranked contracts\.

## Positions

Current covered calls and DRS\.

## Roll Analyzer

Ranked roll candidates\.

## Campaigns

Campaign history\.

## Transactions

Trade ledger\.

## Performance

Strategy reporting\.

## Configuration

Strategy parameters\.

## Research

Historical analytical data\.

---

# 65\. Dashboard Requirements

The dashboard shall answer within approximately 30–60 seconds:

1. Which holdings are currently attractive for covered calls?
2. Which contracts are preferred?
3. How many contracts should be sold?
4. What defense plan accompanies each proposed trade?
5. Which existing positions require attention?
6. Which calls should be closed for profit?
7. Which positions should be rolled?
8. What is the best roll candidate?
9. How much income is the strategy generating?
10. Is the strategy outperforming buy\-and\-hold?

---

# 66\. Recommendation Presentation

Example:

```text
MSFT

ACTION: SELL

CCOS: 87 / 100
Contract Score: 93 / 100

Shares: 1,000
Recommended Contracts: 3
Coverage: 30%

Strike: $XXX
Expiration: YYYY-MM-DD
DTE: 31
Delta: .16

Estimated Premium:
$X.XX/share

Estimated Income:
$X,XXX

DEFENSE PLAN

Profit Close:
70%

Delta Warning:
.25

Roll Evaluation:
.30

Hard Defense:
.40

Strike Proximity:
2%

Premium Multiple:
2.0x

Time Trigger:
7 DTE

Technical Trigger:
Breakout

Preferred Roll:
Up and Out
Target New Delta <= .20
```

---

# 67\. Roll Recommendation Presentation

Example:

```text
ACTION: ROLL UP & OUT

Current DRS:
68 HIGH RISK

Current:
$535 strike
18 DTE
Delta .36

Replacement:
$550 strike
38 DTE
Delta .17

Net Roll:
-$0.10/share

Strike Improvement:
+$15/share

Delta:
.36 -> .17

DRS:
68 -> 21

DRS Reduction:
47

RQS:
94 / 100

Reason:
Large assignment-risk reduction for minimal debit while
substantially increasing strike protection.
```

---

# 68\. Daily Refresh Workflow

Expected V1 workflow:

```text
Start application
        |
Refresh market data
        |
Persist market snapshots
        |
Calculate indicators
        |
Calculate CCOS
        |
Retrieve eligible option chains
        |
Calculate Contract Scores
        |
Calculate Position Sizes
        |
Generate new recommendations
        |
Refresh open positions
        |
Calculate DRS
        |
Run Roll Engine where required
        |
Update performance
        |
Refresh Excel
```

---

# 69\. Manual Trade Workflow

```text
System recommendation
        |
User reviews Fidelity/Schwab live quote
        |
User places trade manually
        |
User records actual fill
        |
Transaction created
        |
Campaign created/updated
        |
Position monitoring begins
```

The recommendation price and actual fill price shall both be retained\.

This permits execution\-quality analysis later\.

---

# 70\. Error Handling

The system shall distinguish:

```text
Provider unavailable
Authentication failure
Rate limit
Invalid symbol
Missing option chain
Missing Greek
Stale market data
Database error
Strategy calculation error
Configuration error
```

A missing data field shall not silently become zero\.

Where critical data is unavailable, the recommendation shall be:

```text
INSUFFICIENT_DATA
```

rather than manufacturing a score\.

---

# 71\. Logging

Structured logging shall capture:

```text
timestamp
operation
symbol
provider
duration
result
error
correlation ID
```

Strategy decisions should have separate decision/audit logging\.

Secrets and authentication tokens shall never be logged\.

---

# 72\. Security

Tradier credentials shall be stored using appropriate local secret management\.

Development may use \.NET user secrets\.

Secrets shall never be committed to Git\.

The Excel workbook shall not contain API credentials\.

The local API should initially bind only to localhost unless explicitly configured otherwise\.

---

# 73\. Testing Strategy

Strategy calculations require extensive deterministic unit testing\.

For every scoring engine, tests shall cover:

- every threshold boundary;
- hard gates;
- maximum/minimum scores;
- missing data;
- configuration changes;
- tax\-sensitive overrides\.

Example:

```text
Given:
Delta = .26
MaximumDelta = .25

Then:
Contract rejected

Regardless of:
Calculated ContractScore
```

---

# 74\. Golden Strategy Scenarios

Maintain a set of canonical scenarios\.

Examples:

## Strong Entry

```text
High IV
RSI 65
Near resistance
Momentum slowing
Liquid chain
```

Expected:

```text
CCOS >=70
```

## Breakout Veto

```text
RSI 76
Price above upper BB
Strong volume
Accelerating momentum
New high
```

Expected:

```text
NO TRADE
Breakout veto
```

## Defensive Roll

```text
Delta .38
Stock within 1.5% strike
DRS >65
```

Expected:

```text
ROLL evaluation
```

Golden scenarios shall prevent accidental strategy changes during refactoring\.

---

# 75\. Integration Testing

Tradier integration tests shall use captured/mocked provider responses whenever practical\.

Tests shall verify:

```text
Tradier DTO
    ->
Normalized domain model
    ->
Persistence
    ->
Strategy calculation
    ->
API result
```

Tests shall not require live Tradier access for routine CI execution\.

---

# 76\. Strategy Versioning

Phase 4 preserves three independent identities:

```text
IndicatorCalculationVersion
ConfigurationVersion
StrategyVersion
```

### IndicatorCalculationVersion

Changes when Phase 3 fact-calculation algorithms change, such as IV30 interpolation, RSI mathematics, resistance clustering, or regime calculation.

Phase 4 records the indicator calculation version of the facts it consumed.

### ConfigurationVersion

Owns tunable numeric values and score parameters, including:

```text
CCOS weights and score bands
CCOS classification thresholds
Contract Score weights and score bands
Contract Score classification thresholds
maximum/preferred delta settings
DTE ranges
liquidity limits and bands
premium/yield thresholds where server-configured
other approved numeric strategy parameters
```

Changing a numeric value without changing the algorithm's meaning requires a configuration-version change.

### StrategyVersion

Owns algorithmic structure and semantics, including:

```text
formulas
component composition
hard-gate meaning
missing-data policy
ranking/tie-breaking
decision flow
```

Changing those semantics requires a strategy-version change.

Holding-specific values are not ConfigurationVersion values. They are snapshotted in the evaluation's HoldingContext.

Every immutable Phase 4 evaluation shall persist both `ConfigurationVersion` and the complete resolved strategy-configuration values actually used.

V1 uses one shared `ConfigurationVersion` identity across Phase 3 and Phase 4. It does not introduce separate indicator and strategy configuration-version systems.

Historical evaluations retain their original versions and resolved configuration and shall never be silently reinterpreted using newer logic or values.

# 77\. Auditability

For any historical recommendation, the system shall be able to answer:

```text
What did the system recommend?

When?

What market data did it have?

What indicators were calculated?

What configuration was active?

What scores were generated?

Which gates passed/failed?

Why was the action recommended?

Was it executed?

At what actual price?

What happened afterward?
```

This is a core system requirement\.

---

# 78\. Performance Requirements

V1 is not latency sensitive\.

Targets:

```text
Dashboard API:
<2 seconds when using cached data

Single-symbol strategy calculation:
<250 ms excluding provider calls

Portfolio recalculation:
<5 seconds for normal personal portfolio
excluding external market-data retrieval
```

Correctness and reproducibility take priority over latency\.

---

# 79\. Data Retention

Unless storage becomes problematic:

- Transactions: permanent\.
- Campaigns: permanent\.
- Recommendations: permanent\.
- Daily position snapshots: permanent\.
- Daily underlying bars: permanent\.
- Option\-chain snapshots: retained for research\.
- Strategy/configuration versions: permanent\.

SQLite should be sufficient for V1\.

The repository abstraction shall permit migration to PostgreSQL or another relational database later\.

---

# 80\. V1 Acceptance Criteria

V1 is successful when the user can:

1. Configure Fidelity and Schwab holdings\.
2. Configure assignment/tax sensitivity per holding\.
3. Refresh Tradier data\.
4. View technical and volatility indicators\.
5. Receive a CCOS for each holding\.
6. See why the CCOS was generated\.
7. View eligible option contracts\.
8. See rejected contracts and rejection reasons\.
9. Receive ranked Contract Scores\.
10. Receive a recommended number of contracts\.
11. Receive strike\-ladder recommendations\.
12. Receive a complete defense plan before opening\.
13. Record an actual Fidelity/Schwab fill\.
14. Automatically begin monitoring the resulting position\.
15. View current DRS\.
16. Receive profit\-close alerts\.
17. Receive defense alerts\.
18. Generate ranked roll alternatives\.
19. Compare current and projected DRS\.
20. Record rolls as linked campaign transactions\.
21. Calculate complete campaign P&L\.
22. View portfolio covered\-call income\.
23. Compare results with buy\-and\-hold\.
24. Analyze results by CCOS, delta, DTE, IV, RSI and other factors\.
25. Reproduce every historical recommendation from its stored inputs and strategy/configuration version\.

---

# 81. Implementation Phases

## Phase 1 — Repository/Foundation

Build:

```text
Solution and project structure
Domain foundations
Application boundaries
Persistence foundations
SQLite
Initial EF migration
Logging
Configuration foundations
Testing infrastructure
Health endpoint
Repository engineering guidance
```

Phase 1 shall establish architecture and engineering conventions without implementing market-data providers or strategy logic.

## Phase 2 — Tradier Data

Build:

```text
Tradier production API authentication
Provider-independent market-data abstraction
Quotes
Historical daily prices
Option expirations
Option chains
Greeks and implied-volatility mapping
Rate-limit handling
Caching
Historical market-data persistence
Append-only option snapshots
Mocked provider fixtures
Market-data API
```

Tradier sandbox and brokerage/trading functionality remain out of scope.

## Phase 3 — Indicators and Market Context

Implement:

```text
SMA20
SMA50
SMA200

RSI14 using Wilder smoothing

Bollinger Bands
Bollinger %B
Bollinger bandwidth

MACD 12/26/9

ATR14 using Wilder smoothing
ATR percent

RV20
RV30

Underlying implied-volatility context:
    IV30
    IVRank
    IVPercentile

Resistance detection

Market regime
Sector regime

Historical as-of calculations
Look-ahead-bias protection
Indicator calculation versioning
Indicator snapshot persistence
Read-only indicator API
```

Phase 3 shall produce deterministic, provider-independent indicator observations suitable for direct consumption by later strategy engines.

The V1 IV30, IVRank, and IVPercentile methodology is approved in Section 12.10 and shall be implemented exactly as specified. Changes require explicit specification and calculation-version review.

Phase 3 shall not implement CCOS or other recommendation logic.

## Phase 4 — Entry Strategy

Phase 4 stops before position sizing.

It produces a reproducible entry-strategy assessment containing:

```text
CCOS and component explanation
Underlying hard-gate results
Per-contract hard-gate results
Per-contract Contract Scores and components
Deterministic ranking
EntryCandidateExists
PreferredInitialContract
PreferredInitialStrike
Immutable EntryStrategyEvaluation
```

Phase 4 does not determine:

```text
coverage percentage
available-share constraints
existing-call exposure
Delta Exposure Ratio
recommended contract count
strike laddering
scaling
final SELL Recommendation
```

Those belong to Phase 5 or later recommendation layers.

Implementation shall be split into reviewable packets:

### Phase 4A — Strategy Foundations and Input Contracts

```text
Strongly typed Phase 4 configuration and validation
StrategyVersion representation
HoldingContext
IndicatorContext
EarningsContext
EvaluationContext
ScoreComponentResult
GateResult
stable reason/missing-input codes
contract/evaluation result models
ResistanceUnavailableReason refinement
```

No CCOS or Contract Score implementation belongs in 4A.

### Phase 4B — CCOS and Underlying Eligibility

Implement the six CCOS components, classifications, holding threshold evaluation, breakout veto, and global missing-data policy as pure Strategy logic.

### Phase 4C — Contract Eligibility, Scoring, Ranking, and Selection

Implement:

```text
derived contract metrics
contract hard gates
effective maximum delta
earnings gate consumption
liquidity
minimum premium/yield
all seven Contract Score components
Contract Score classification
deterministic ranking/tie-breaking
EntryAcceptable
preferred initial contract/strike
overall Phase 4 disposition
```

as pure Strategy logic.

### Phase 4D — Evaluation Orchestration and Market/Event Inputs

Application/MarketData shall assemble the approved reproducible evaluation bundle:

```text
HoldingContext snapshot
IndicatorContext
IndicatorAsOfDate
EvaluationTimestampUtc
latest eligible complete chain per expiration
EarningsContext
StrategyVersion
ConfigurationVersion
```

`IEarningsDateSource` supplies provider-independent earnings input. Phase 4 V1 production current evaluations use validated
configuration-backed dates because an authoritative Tradier corporate-calendar contract is not available in approved sources.
A future provider-backed implementation may replace that source without changing Application orchestration or Strategy.
Phase 4 V1 consumes only Earnings; no dividend or generic material-event entry rule is added.

### Phase 4E — Immutable Persistence

Persist append-only EntryStrategyEvaluation history, all evaluated contracts, actual consumed inputs, resolved configuration, versions, scores, gates, ranking, explanations, and preferred-contract disposition.

Schema changes shall use EF Core migrations.

### Phase 4F — API and Merge-Gate Validation

Implement the Phase 4 V1 HTTP surface defined in Section 16.1 and complete full release build, test-suite, and clean/upgrade migration validation.

No Phase 4 packet may introduce a new formula, threshold, trading rule, or missing-data fallback beyond this specification.

## Phase 5 — Position Sizing

Phase 5 consumes an immutable Phase 4 EntryStrategyEvaluation and produces a separate immutable PositionSizingEvaluation.

It owns:

```text
physical capacity
existing covered-call exposure
available shares/contracts
CCOS base coverage
Assignment Sensitivity modifier and maximum
Contract Quality modifier
same-account stock concentration
Delta Exposure Ratio
Holding coverage constraints
desired total contracts
additional contracts
structured sizing explainability
immutable sizing persistence
Position Sizing API
```

Phase 5 V1 sizes only the Phase 4 PreferredInitialContract.

Strike laddering, multiple-candidate allocation, final Recommendation lifecycle, closing existing calls, transaction ledger, campaign accounting, and brokerage execution are deferred.

Implementation shall be split into reviewable packets:

### Phase 5A — Position Sizing Foundations

```text
strongly typed Position Sizing configuration
Position Sizing strategy-version identity
status/reason/missing-input codes
PositionSizingHoldingContext
existing-exposure input contracts
portfolio-concentration input contracts
PositionSizing result contracts
validation
```

### Phase 5B — Base Coverage and Caps

Implement:

```text
physical capacity
CCOS base-coverage curve
Assignment Sensitivity modifier
Assignment Sensitivity maximum
Contract Quality modifier
Holding maximum coverage
coverage-to-contract flooring
target/additional contract semantics
```

as pure Strategy logic.

### Phase 5C — Portfolio Concentration

Implement deterministic same-account tracked-equity concentration using persisted as-of daily prices, stock modifier bands, ETF NotApplicable behavior, and missing-data semantics.

### Phase 5D — Existing Exposure and DER

Implement the minimal current-state open-short-call representation, available-share constraints, existing Delta observations, ExistingDER, proposed DER, and DER-limited additional contracts.

This packet does not implement the Phase 7 transaction ledger or campaign accounting.

### Phase 5E — Application Orchestration

Application assembles:

```text
immutable Phase 4 evaluation
PositionSizingHoldingContext
same-account concentration context
existing short-call state
existing-call Delta observations
resolved PositionSizingConfiguration
SizingTimestampUtc
ConfigurationVersion
PositionSizingStrategyVersion
```

and invokes pure Strategy.

### Phase 5F — Immutable Persistence

Persist append-only PositionSizingEvaluation history and complete reproducibility payloads.

Schema changes use EF Core migrations.

### Phase 5G — API and Merge-Gate Validation

Implement the immutable Position Sizing HTTP surface defined in Section 63 and complete release build, test-suite, migration, golden-scenario, and documentation validation.

No Phase 5 packet may introduce a new formula, threshold, allocation rule, rounding rule, missing-data fallback, or exposure assumption beyond the approved specification.

## Phase 6 — Defense and Roll Engine

Implement:

```text
Daily position monitoring
DRS
DRS classification
Defense hard triggers
Profit-taking evaluation
Roll candidate generation
Roll economics
RQS
Roll hard gates
Maximum roll debit
```

## Phase 7 — Campaign Accounting and Performance

Implement:

```text
Transaction ledger
Campaign lifecycle
Roll-linked campaign accounting
Premium accounting
Daily position snapshots
Performance measurement
Buy-and-hold benchmark
Counterfactual history
Research outputs
```

## Phase 8 — Excel Dashboard

Implement:

```text
Portfolio view
Market-data refresh
Indicator display
Opportunity display
Contract candidates
Defense monitoring
Roll candidates
Campaign history
Performance reporting
Manual transaction entry
Configuration interface
```

Excel shall remain a presentation, configuration, and analytical layer.

Authoritative strategy and indicator calculations shall remain in C#.

---

# 82\. Future Roadmap

Potential post\-V1 capabilities:

## V2

- Automated Fidelity/Schwab position synchronization\.
- Additional market\-data provider\.
- Better historical options dataset\.
- Automated corporate\-event ingestion\.
- Intraday monitoring\.
- Notifications\.

## V3

- Full backtesting\.
- Walk\-forward optimization\.
- Feature importance\.
- Parameter optimization\.
- Monte Carlo campaign simulation\.
- Expected\-assignment modeling\.
- Correlation\-aware portfolio coverage\.

## V4

- Web application\.
- React frontend\.
- Real\-time dashboard\.
- Automated brokerage synchronization\.
- Mobile\-friendly interface\.

Automatic trade execution should remain a separate, explicitly approved project even if brokerage APIs eventually permit it\.

---

# 83\. Core Decision Pipeline

The final V1 strategy pipeline is:

```text
                    HOLDING
                       |
                       v
                  HARD GATES
                       |
                       v
                     CCOS
              Should we sell?
                       |
                 CCOS >= 70
                       |
                       v
               CONTRACT SCORE
               Which contract?
                       |
                  CS >= 80
                       |
                       v
               POSITION SIZING
               How many calls?
                       |
                       v
          PositionSizingEvaluation
                       |
                       v
                RECOMMENDATION
                       |
                 User executes
                       |
                       v
                   CAMPAIGN
                       |
                       v
                      DRS
              Is call dangerous?
                 /           \
              Low             High
               |               |
             HOLD         ROLL ENGINE
                               |
                               v
                              RQS
                       Which defense?
                               |
                  +------------+-----------+
                  |            |           |
                CLOSE       ROLL UP    ROLL UP/OUT
                  |            |           |
                  +------------+-----------+
                               |
                               v
                           CAMPAIGN
                               |
                               v
                         PERFORMANCE
                               |
                               v
                           RESEARCH
```

---

# 84\. Governing Design Principle

The system shall optimize for:

> **Sustainable covered-call income subject to strict preservation-of-shares constraints.**

Premium generation shall never be optimized independently of:

- assignment risk;
- embedded gains;
- market regime;
- option liquidity;
- position concentration;
- roll economics;
- campaign performance\.

The ultimate measure of success is not premium collected\.

It is:

> **Whether the complete covered-call strategy improves risk-adjusted economic results relative to holding the underlying shares while maintaining the user’s desired level of assignment protection.**

---

# 85\. Definition of Ready for Implementation

The project is ready for repository scaffolding when:

- Tradier developer credentials are available\.
- Strategy V1\.0 rules in this specification are accepted\.
- The normalized domain model is accepted\.
- SQLite is accepted as V1 persistence\.
- Excel is accepted as V1 presentation\.
- Manual Fidelity/Schwab execution is accepted\.
- Automatic trading remains explicitly out of scope\.

Once these conditions are satisfied, implementation should begin with **Phase 1 — Repository/Foundation**, followed immediately by the Tradier integration\.

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

## 6\.2 Strategy Reproducibility

Every recommendation shall record:

- raw inputs;
- calculated indicators;
- component scores;
- configuration values or configuration version;
- strategy version;
- timestamp\.

Historical recommendations shall never silently change because the current strategy configuration changed\.

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

# 12\. Market Snapshot

```text
MarketSnapshotId
Symbol
Timestamp
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

RV30

IV30
IVRank
IVPercentile

ResistancePrice
DistanceToResistance

MarketRegime
SectorRegime

StrategyVersion
```

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
DTE
Strike
OptionType

Bid
Ask
Mid
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

Derived fields:

```text
OTMPercent
BidAskSpreadPercent
PremiumYield
DailyPremiumYield
AnnualizedPremiumYield
DeltaAdjustedYield
ExpectedMove
ExpectedMoveRatio
StrikeDistance
StrikeVsResistance
```

Option observations shall be timestamped so the database naturally develops its own historical option dataset\.

---

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

Types initially include:

```text
Earnings
ExDividend
Dividend
OtherMaterialEvent
```

---

# 16\. Recommendation

Every generated recommendation shall receive a permanent `RecommendationId`\.

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

Recommendations shall be retained whether or not they are executed\.

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

# 20\. Strategy Configuration

No important strategy constants shall be hard\-coded\.

Configuration shall be versioned\.

```text
StrategyConfiguration
    ConfigurationVersion
    EffectiveDate
```

Configuration categories include:

```text
CCOS
ContractScore
PositionSizing
DRS
RollEngine
ProfitTaking
TaxSensitivity
```

Every recommendation records the configuration version used to generate it\.

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

Default opening requirement:

```text
CCOS >= 70
```

---

# 22\. CCOS Hard Gates

Regardless of score:

- Earnings before expiration normally prevents an individual\-stock trade\.
- Known material event may prevent trade\.
- Strong technical breakout can veto entry\.
- Insufficient option liquidity prevents entry\.
- Maximum coverage constraints prevent additional contracts\.

A hard\-gate failure shall include a machine\-readable reason code\.

---

# 23\. CCOS Volatility Component

Maximum: 25\.

## IV Percentile — 15

```text
<20       0
20–30     3
30–40     6
40–50     9
50–60    11
60–70    13
>70      15
```

## IV30 / RV30 — 10

```text
<0.90       0
0.90–1.00   2
1.00–1.10   4
1.10–1.20   6
1.20–1.35   8
>1.35      10
```

---

# 24\. CCOS RSI Component

Maximum: 15\.

```text
RSI <40       0
40–50         2
50–55         5
55–60         8
60–65        11
65–70        15
70–75        13
75–80         9
>80           5
```

Extremely high RSI receives a lower score because it may represent accelerating breakout conditions rather than simple overextension\.

---

# 25\. Bollinger Component

Standard:

```text
20-period SMA
+/- 2 standard deviations
```

%B:

```text
(Price - LowerBand) /
(UpperBand - LowerBand)
```

%B scoring:

```text
<.40       0
.40–.60    2
.60–.75    5
.75–.90    8
.90–1.05  10
1.05–1.15  7
>1.15      3
```

Bandwidth contributes an additional five points\.

Rapidly expanding bands reduce attractiveness because they may indicate breakout acceleration\.

---

# 26\. Contract Score

Contract Score answers:

> Which eligible call should be sold?

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
<60      Reject
60–69    Weak
70–79    Acceptable
80–89    Good
90–100   Excellent
```

Default requirement:

```text
CCOS >= 70
AND
ContractScore >= 80
```

---

# 27\. Contract Hard Gates

Reject if:

```text
Delta > .25
DTE < 14
DTE > 45
Strike <= underlying price
Earnings occurs before expiration
Liquidity unacceptable
Coverage constraint violated
```

For highly tax\-sensitive holdings:

```text
Maximum Delta = .20
```

All gates shall be configurable\.

---

# 28\. Position Sizing Engine

The Position Sizing Engine answers:

> How many contracts should be sold?

Maximum physical capacity:

```text
Floor(Shares / 100)
```

The engine shall not default to 100% coverage\.

Base CCOS sizing:

```text
CCOS <70      0%
70–74        20%
75–79        30%
80–84        40%
85–89        50%
90–94        60%
95–100       70%
```

---

# 29\. Assignment Sensitivity

Assignment Sensitivity Level:

|ASL|Meaning                            |Baseline maximum|
|--:|-----------------------------------|---------------:|
|1  |Comfortable assignment             |100%            |
|2  |Prefer retaining                   |90%             |
|3  |Strong preference                  |80%             |
|4  |Highly sensitive                   |70%             |
|5  |Assignment effectively unacceptable|50–60%          |

Sizing modifiers:

```text
ASL1  1.25
ASL2  1.10
ASL3  1.00
ASL4   .85
ASL5   .70
```

---

# 30\. Contract Quality Sizing Modifier

```text
CS <80      No trade
80–84       .90
85–89      1.00
90–94      1.10
>=95       1.15
```

---

# 31\. Concentration Modifier

Initial stock\-holding rules:

```text
Portfolio weight <5%       1.10
5–15%                      1.00
15–25%                      .90
25–40%                      .80
>40%                        .70
```

ETF\-specific concentration rules may be introduced later\.

---

# 32\. Position Sizing Formula

Conceptually:

```text
TargetCoverage =
    CCOSBaseCoverage
    × AssignmentSensitivityModifier
    × ContractQualityModifier
    × ConcentrationModifier
```

subject to:

```text
HoldingMaximumCoverage
AvailableShares
DeltaExposureLimit
ExistingCallExposure
```

Tax\-sensitive positions shall round contract counts downward\.

---

# 33\. Delta Exposure Ratio

Simple covered percentage is insufficient\.

Calculate:

```text
DER =
SUM(Contracts × Delta × 100)
/
SharesOwned
```

Example:

Eight \.10\-delta calls against 1,000 shares:

```text
DER = 8%
```

Eight \.24\-delta calls:

```text
DER = 19.2%
```

Maximum DER shall be configurable by holding\.

---

# 34\. Strike Laddering

V1 shall support distributing contracts across delta tiers\.

Example:

```text
Conservative    .10–.13
Core            .14–.17
Income          .18–.20
```

Expiration laddering shall be deferred as an experimental feature\.

---

# 35\. Scaling

The engine shall support incremental coverage\.

Example:

```text
CCOS 74 -> 2 contracts
CCOS 82 -> +2
CCOS 91 -> +2
```

Likewise, declining CCOS combined with profitable positions may reduce coverage by closing calls without replacement\.

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

GetCorporateEventsAsync(symbol)
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

Recommended application\-level services:

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
- strategy version\.

---

# 63\. Local Application API

The ASP\.NET Core API shall expose a local interface suitable for Excel and future clients\.

Representative endpoints:

```text
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

Every production strategy change shall update:

```text
StrategyVersion
```

Example:

```text
1.0.0
1.1.0
2.0.0
```

Historical recommendations retain their original strategy version\.

Configuration changes shall separately update:

```text
ConfigurationVersion
```

This permits comparisons such as:

```text
Strategy 1.0
vs
Strategy 1.1
```

without contaminating historical results\.

---

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

# 81\. Implementation Phases

## Phase 1 — Repository/Foundation

Build:

```text
Tradier production API authentication
Quotes
Price history
Expirations
Option chains
Greeks/IV mapping
Rate-limit handling
Caching
Historical snapshot persistence
Mocked provider test fixtures
```

## Phase 2 — Tradier Data

Build:

```text
Tradier authentication
Quotes
Price history
Expirations
Option chains
Greeks/IV mapping
Caching
Historical persistence
```

## Phase 3 — Indicators

Implement:

```text
SMA
RSI
Bollinger Bands
MACD
ATR
Realized volatility
Resistance detection
Market/sector regime
```

## Phase 4 — Entry Strategy

Implement:

```text
Hard gates
CCOS
Contract Score
Position Sizing
Strike laddering
Recommendation generation
```

## Phase 5 — Position Management

Implement:

```text
Transactions
Campaigns
Position snapshots
Profit capture
DRS
Defense triggers
```

## Phase 6 — Roll Engine

Implement:

```text
Candidate generation
Roll economics
Projected DRS
RQS
Hard gates
Roll recommendations
```

## Phase 7 — Excel UI

Build:

```text
Dashboard
Holdings
Opportunities
Contract Analysis
Positions
Roll Analyzer
Campaigns
Performance
Configuration
```

## Phase 8 — Research & Validation

Build:

```text
Performance attribution
Buy-and-hold benchmark
Signal analysis
Parameter analysis
Counterfactual tracking
Backtesting framework
```

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

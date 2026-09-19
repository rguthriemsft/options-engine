# Phase 5 — Position Sizing Design

## 1. Purpose

Phase 5 determines how many covered-call contracts should be added after a Phase 4 entry evaluation.

The governing question is:

> Given an immutable Phase 4 entry evaluation with a preferred initial contract, what covered-call position size should the system target, and how many additional contracts may be added now?

Phase 5 is a deterministic sizing layer. It does not recalculate entry strategy, select a different contract, manage defensive positions, execute trades, or create campaign accounting.

This document resolves the previously ambiguous Position Sizing behavior in `SPECIFICATION.md` Sections 28–35 and establishes the architectural and mathematical boundary required before implementation.

---

# 2. Governing Principles

Phase 5 shall be:

- deterministic for fixed inputs, configuration, and strategy version;
- provider-independent inside Strategy;
- explicit about unavailable or missing data;
- explicit about valid zero-size results;
- reproducible from persisted inputs;
- append-only when persisted;
- isolated from brokerage execution;
- isolated from DRS, roll logic, campaign accounting, and transaction accounting;
- based on immutable Phase 4 output rather than recalculating Phase 4 decisions.

Missing financial data is never converted to zero, neutral, or a passing condition.

No formula, fallback, threshold, modifier, rounding rule, freshness rule, or exposure assumption may be introduced beyond the approved rules in this design and the corresponding specification/acceptance documents.

---

# 3. Phase Boundary

## 3.1 Phase 5 Owns

Phase 5 owns:

```text
physical covered-call capacity
existing covered-call exposure
remaining physical capacity

CCOS-based base coverage
Assignment Sensitivity sizing modifier
Assignment Sensitivity maximum coverage
Contract Quality sizing modifier
portfolio concentration sizing modifier

Holding maximum coverage

coverage-ratio to whole-contract conversion

Delta Exposure Ratio
holding-level MaximumDeltaExposureRatio

desired total contract count
desired additional contract count
DER-limited additional contract count
final additional contract count
resulting total contract count

limiting factors
zero-size reason codes
missing-input semantics
structured sizing explainability

immutable PositionSizingEvaluation persistence
Position Sizing API
```

## 3.2 Phase 5 Consumes but Does Not Recalculate

Phase 5 consumes the immutable Phase 4 `EntryStrategyEvaluation`.

It shall not recalculate:

```text
CCOS
CCOS components
breakout veto
contract hard gates
Contract Score
Contract Score components
contract ranking
preferred initial contract
preferred initial strike
preferred initial expiration
preferred initial reference premium
```

The persisted Phase 4 result is authoritative for those facts.

## 3.3 Phase 5 Does Not Own

Phase 5 does not own:

```text
final Recommendation lifecycle
SELL execution semantics
profit taking
Defense Risk Score
Roll Engine
Roll Quality Score
close/replace decisions
campaign accounting
transaction ledger
brokerage synchronization
pending brokerage orders
automatic execution
```

If existing covered-call exposure exceeds a newly calculated target, Phase 5 reports that condition and recommends no additional contracts. It does not recommend closing calls.

---

# 4. Architectural Output

Phase 5 creates a separate immutable calculation artifact:

```text
PositionSizingEvaluation
```

It does not extend or rewrite `EntryStrategyEvaluation`.

It does not create the first final `Recommendation`.

The intended boundary is:

```text
EntryStrategyEvaluation
        |
        v
PositionSizingEvaluation
        |
        v
Recommendation assembly
        |
        v
User action / later campaign lifecycle
```

The later Recommendation layer may combine immutable Phase 4 and Phase 5 decisions.

---

# 5. Phase 5 Invocation Semantics

## 5.1 Successful Phase 4 Candidate

A normal sizing calculation requires an immutable Phase 4 evaluation for which:

```text
EntryCandidateExists == true
```

The preferred initial contract from that evaluation is the only contract sized in Phase 5 V1.

## 5.2 No Phase 4 Entry Candidate

If:

```text
EntryCandidateExists == false
```

Phase 5 may still create a sizing evaluation for auditability, but its status is:

```text
NotApplicable
```

with:

```text
AdditionalContracts = 0
```

and stable reason code:

```text
NO_ENTRY_CANDIDATE
```

This is not an error and is not `InsufficientData`.

---

# 6. Single-Contract V1

Phase 5 V1 sizes only:

```text
Phase4.PreferredInitialContract
```

It does not allocate contracts among multiple ranked candidates.

The Contract Score used for Position Sizing is the persisted Contract Score for the preferred Phase 4 contract.

The contract Delta used for proposed new DER is the persisted normalized Delta belonging to that preferred-contract observation.

No fresh ranking or contract selection occurs in Phase 5.

---

# 7. Strike Laddering

Strike laddering is deferred from initial Phase 5 V1.

The prior concept:

```text
Conservative    .10–.13
Core            .14–.17
Income          .18–.20
```

does not define allocation percentages, minimum lot sizes, ordering, remainder distribution, missing-tier behavior, Contract Score interaction, DER interaction, or incremental-scaling behavior.

Phase 5 V1 shall not invent those rules.

---

# 8. Authoritative Share Count and Physical Capacity

## 8.1 Authoritative Shares

Phase 5 V1 uses:

```text
Holding.Shares
```

as the authoritative owned-share quantity.

Tax-lot totals do not replace `Holding.Shares` during sizing.

## 8.2 Physical Capacity

```text
PhysicalCapacityContracts =
    floor(SharesOwned / 100)
```

Examples:

```text
99 shares    -> 0 contracts
100 shares   -> 1 contract
199 shares   -> 1 contract
250 shares   -> 2 contracts
1,000 shares -> 10 contracts
```

Fractional shares and nonmultiples of 100 never create a fractional covered-call contract.

## 8.3 Invalid Share State

`SharesOwned < 0` is invalid input.

`SharesOwned == 0` is a valid zero-capacity state when no open short-call obligation exists.

If zero shares coexist with existing short calls, Phase 5 reports an inconsistent exposure state rather than attempting DER division.

---

# 9. Existing Covered-Call Exposure

## 9.1 Required Phase 5 State

The repository currently has no implemented Transaction, Campaign, ShortCall, or OptionPosition model.

Phase 5 therefore requires a minimal current-state representation for existing short calls.

This is not the Phase 7 transaction ledger and not campaign accounting.

Conceptually:

```text
OpenShortCallPosition
    HoldingId
    OptionSymbol
    Contracts
    Strike
    Expiration
    OpenedAt / known position timestamp where applicable
```

The exact persistence shape is finalized during Phase 5 foundations/exposure work, but it must provide deterministic current exposure.

## 9.2 Existing Exposure Definition

Phase 5 V1 counts:

> Net currently open short call contracts associated with the same Holding.

It does not count:

- long calls;
- expired calls;
- closed calls;
- calls associated with another Holding;
- unexecuted recommendations;
- pending brokerage orders;
- hypothetical future orders.

Brokerage synchronization is out of scope.

## 9.3 Existing Covered Shares

```text
ExistingCoveredShares =
    ExistingCoveredContracts * 100
```

## 9.4 Available Shares

```text
AvailableShares =
    max(
        0,
        SharesOwned - ExistingCoveredShares
    )
```

## 9.5 Available Contracts

```text
AvailableContracts =
    floor(AvailableShares / 100)
```

If existing obligations exceed physical capacity, Phase 5 returns zero additional contracts and an explicit anomaly/limiting reason.

## 9.6 Pending Orders

Pending brokerage orders are not modeled in Phase 5 V1 and do not reserve shares.

---

# 10. Position Sizing Configuration

Position Sizing configuration is strongly typed, validated, and versioned.

Conceptually:

```text
PositionSizingConfiguration
    CcosBaseCoverage
    AssignmentSensitivityModifiers
    AssignmentSensitivityMaximums
    ContractQualityModifiers
    StockConcentrationModifiers
```

V1 continues using the shared `ConfigurationVersion`.

Algorithmic behavior is independently identified by the applicable Position Sizing strategy version.

Every immutable evaluation persists the complete resolved Position Sizing configuration it consumed.

---

# 11. Ratio Representation

All Position Sizing percentages are represented internally as fractional ratios:

```text
0.20 = 20%
0.50 = 50%
0.70 = 70%
1.00 = 100%
```

This applies to:

```text
CCOS base coverage
Assignment Sensitivity maximum
Holding.MaximumCoveragePercent
PortfolioWeight
DesiredCoverageRatio
MaximumDeltaExposureRatio
DER
```

The existing property name `MaximumCoveragePercent` is retained in V1 to avoid a naming-only migration, but its semantic range is `[0,1]`.

`MaximumDeltaExposureRatio` likewise uses `[0,1]`.

Both must be finite and validated within that range.

---

# 12. CCOS Base Coverage

Approved V1 coverage curve:

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

If Phase 4 produces an entry candidate under a custom Holding threshold below 70, the Position Sizing base coverage remains zero.

Phase 5 does not invent a coverage level below the approved sizing curve.

---

# 13. Assignment Sensitivity

Both the sizing modifier and the Assignment Sensitivity maximum apply.

## 13.1 Modifiers

```text
ASL1  1.25
ASL2  1.10
ASL3  1.00
ASL4  0.85
ASL5  0.70
```

## 13.2 Maximum Coverage

```text
ASL1  1.00
ASL2  0.90
ASL3  0.80
ASL4  0.70
ASL5  0.50
```

The prior ambiguous `ASL5 = 50–60%` range is resolved to a V1 default maximum of:

```text
0.50
```

The value is configurable through Position Sizing configuration.

Example for ASL4:

```text
RawCoverage =
    prior modifiers
    * 0.85

DesiredCoverage =
    min(
        RawCoverage,
        0.70,
        Holding.MaximumCoveragePercent
    )
```

---

# 14. Contract Quality Modifier

Phase 5 uses the persisted Contract Score of the Phase 4 preferred contract.

Approved curve:

```text
Contract Score < 80         0.00
80 <= score < 85            0.90
85 <= score < 90            1.00
90 <= score < 95            1.10
95 <= score <= 100          1.15
```

If Phase 4 has a custom `MinimumContractScore` below 80 and still produces an entry candidate, the Position Sizing modifier is zero below Contract Score 80.

That produces a valid zero-size result rather than inventing a new sizing band.

---

# 15. Portfolio Concentration

## 15.1 Scope

V1 concentration is calculated within the Holding's own Account.

It is not a household-level, cross-account, brokerage-wide, or net-liquidation portfolio measure.

## 15.2 Included Holdings

All tracked holdings in the same Account participate in the denominator, regardless of whether the covered-call strategy is enabled for those holdings.

Cash and untracked assets are excluded.

The resulting metric should be described as tracked-account equity concentration.

## 15.3 Price Source

For reproducibility, V1 uses the latest persisted daily closing price at or before:

```text
Phase4.IndicatorAsOfDate
```

for each participating holding.

Phase 5 does not use an uncontrolled fresh live quote merely to calculate concentration.

## 15.4 Market Value

```text
HoldingMarketValue =
    Holding.Shares * AsOfPrice
```

## 15.5 Portfolio Weight

```text
PortfolioWeight =
    TargetHoldingMarketValue
    /
    Sum(MarketValue of tracked Holdings in the same Account)
```

## 15.6 Stock Modifier

Approved stock bands:

```text
weight < 5%           1.10
5% <= weight < 15%    1.00
15% <= weight < 25%   0.90
25% <= weight <= 40%  0.80
weight > 40%          0.70
```

Thus:

- exactly 5% enters the 5–15 band;
- exactly 15% enters the 15–25 band;
- exactly 25% enters the 25–40 band;
- exactly 40% remains in the 25–40 band.

## 15.7 Missing Price

If any participating tracked holding lacks the required as-of price, concentration is unavailable.

Phase 5 shall not:

- ignore that holding;
- substitute zero;
- substitute another date without the approved as-of rule;
- renormalize the denominator around missing holdings.

Because concentration is a required stock sizing input, the sizing evaluation becomes `InsufficientData`.

## 15.8 ETFs

For `AssetType.ExchangeTradedFund`, concentration is:

```text
NotApplicable
```

and the effective concentration modifier is:

```text
1.00
```

This is not represented as a fake zero portfolio weight.

## 15.9 Other Asset Types

`AssetType.Other` has no approved concentration rule in V1.

A Position Sizing evaluation requiring concentration behavior for an unsupported asset type is unavailable/unsupported rather than silently applying stock or ETF rules.

---

# 16. Coverage Calculation Sequence

The calculation order is explicit.

## 16.1 Raw Coverage

```text
RawCoverageRatio =
    CcosBaseCoverageRatio
    * AssignmentSensitivityModifier
    * ContractQualityModifier
    * ConcentrationModifier
```

## 16.2 Coverage Caps

```text
DesiredCoverageRatio =
    min(
        RawCoverageRatio,
        AssignmentSensitivityMaximumRatio,
        Holding.MaximumCoveragePercent
    )
```

The Assignment Sensitivity maximum and Holding maximum are total-coverage caps applied after multiplicative modifiers.

## 16.3 Desired Total Contracts

```text
DesiredTotalContracts =
    floor(
        SharesOwned
        * DesiredCoverageRatio
        / 100
    )
```

No fractional contract may be returned.

## 16.4 Desired Additional Contracts

```text
DesiredAdditionalContracts =
    max(
        0,
        DesiredTotalContracts - ExistingCoveredContracts
    )
```

## 16.5 Physical Capacity Limit

```text
PhysicalLimitedAdditionalContracts =
    min(
        DesiredAdditionalContracts,
        AvailableContracts
    )
```

## 16.6 DER Limit

DER is then applied to the proposed additional contracts.

## 16.7 Final Additional Contracts

```text
AdditionalContracts =
    min(
        PhysicalLimitedAdditionalContracts,
        DERLimitedAdditionalContracts
    )
```

## 16.8 Resulting Total Contracts

```text
ResultingTotalContracts =
    ExistingCoveredContracts
    + AdditionalContracts
```

---

# 17. Whole-Contract Rounding

Coverage-to-contract conversion always floors.

Examples:

```text
750 shares * 40% = 300 covered shares -> 3 contracts
750 shares * 35% = 262.5 covered shares -> 2 contracts
250 shares * 70% = 175 covered shares -> 1 contract
```

The prior statement:

```text
Tax-sensitive positions shall round contract counts downward.
```

is removed as redundant.

All Position Sizing contract counts already round downward.

Phase 5 introduces no additional tax-specific rounding penalty.

Tax-sensitive entry constraints already exist in Phase 4 through its effective maximum initial Delta behavior.

---

# 18. Delta Exposure Ratio

## 18.1 Existing DER

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

Each existing short-call position is evaluated independently because positions may have different Deltas.

## 18.2 Proposed DER

For `N` proposed additional contracts:

```text
ProposedDER(N) =
    (
        ExistingDeltaShares
        + N * PreferredContractDelta * 100
    )
    / SharesOwned
```

## 18.3 Maximum

DER limits only the already-calculated physically eligible action.

The DER-limited additional count is the largest integer `N` in:

```text
0 <= N <= PhysicalLimitedAdditionalContracts
```

satisfying:

```text
ProposedDER(N)
    <= Holding.MaximumDeltaExposureRatio
```

Equality is allowed.

A proposed contract that would move DER above the maximum is not allowed.

The DER calculation never needs to represent an unbounded theoretical contract capacity.

If:

```text
PreferredContractDelta == 0
```

and existing DER is within the configured maximum, then:

```text
DERLimitedAdditionalContracts =
    PhysicalLimitedAdditionalContracts
```

because adding any physically eligible preferred contracts does not increase DER.

## 18.4 Delta Semantics

Call Delta used by Phase 5 must be:

```text
finite
0 <= Delta <= 1
```

Phase 5 does not repair malformed Delta with `abs(Delta)`.

Missing, non-finite, or invalid Delta is unavailable input.

## 18.5 Preferred New Contract Delta

For additional contracts, Phase 5 uses the Delta persisted in the Phase 4 preferred-contract observation.

It does not refresh the preferred contract or substitute a new Delta for that Phase 4 decision.

## 18.6 Existing Call Delta

Existing short-call exposure uses the latest persisted normalized option observation for that option symbol at or before the Phase 5 sizing cutoff.

The actual observation and timestamp used must be retained in the immutable sizing evaluation.

Strategy itself does not fetch provider data.

## 18.7 Freshness

Phase 5 V1 defines no new hidden Delta freshness threshold.

If a later product requirement introduces maximum acceptable age, that becomes explicit configuration/application policy.

## 18.8 DER Already at or Above Maximum

If existing DER is above the configured maximum:

```text
AdditionalContracts = 0
```

with an explicit limiting factor.

If existing DER equals the configured maximum, equality remains allowed:

- `PreferredContractDelta > 0` means no positive additional count can remain within the maximum, so `AdditionalContracts = 0`;
- `PreferredContractDelta == 0` means DER does not further reduce the physically eligible action, so `DERLimitedAdditionalContracts = PhysicalLimitedAdditionalContracts`.

Phase 5 does not recommend closing an existing short call.

---

# 19. Scaling Semantics

Phase 5 computes both desired state and immediate additional action.

It exposes at least:

```text
DesiredCoverageRatio
DesiredTotalContracts
DesiredAdditionalContracts
AdditionalContracts
ResultingTotalContracts
```

This supports incremental coverage without confusing total target state with action size.

Examples:

```text
Existing = 2
Desired = 4
Additional may be 2

Existing = 4
Desired = 4
Additional = 0

Existing = 6
Desired = 4
Additional = 0
Reason includes EXISTING_COVERAGE_ABOVE_TARGET
```

A lower newly calculated target never causes Position Sizing to produce a BTC/close recommendation.

Profit taking and defensive closure belong downstream.

---

# 20. Status and Zero-Result Semantics

Top-level calculation status shall distinguish:

```text
Available
InsufficientData
NotApplicable
```

## 20.1 Valid Zero-Size Results

An `Available` sizing result may legitimately contain:

```text
AdditionalContracts = 0
```

Examples:

- CCOS base coverage is zero;
- Contract Quality modifier is zero;
- target coverage rounds below one contract;
- existing contracts already meet the target;
- existing contracts exceed the target;
- no remaining physical capacity;
- Holding maximum is reached;
- Assignment Sensitivity maximum is reached;
- DER maximum is reached.

These are strategy outcomes, not calculation failures.

## 20.2 Not Applicable

`EntryCandidateExists == false` produces `NotApplicable`.

## 20.3 Insufficient Data

Examples include:

```text
invalid Shares
invalid MaximumCoveragePercent
invalid MaximumDeltaExposureRatio
preferred Phase 4 contract unexpectedly lacks valid Delta
existing short call lacks a usable Delta observation
required stock concentration price missing
portfolio denominator invalid
unsupported AssetType concentration semantics
```

Missing input is never treated as zero.

---

# 21. Stable Reason and Limiting-Factor Codes

The exact enum names may be finalized in Phase 5A, but the result model must have stable machine-readable codes.

The approved semantic categories include at least:

```text
NO_ENTRY_CANDIDATE
CCOS_SIZING_BELOW_MINIMUM
CONTRACT_QUALITY_BELOW_SIZING_MINIMUM
TARGET_ROUNDS_BELOW_ONE_CONTRACT
ASSIGNMENT_SENSITIVITY_LIMIT
HOLDING_MAXIMUM_COVERAGE_LIMIT
NO_AVAILABLE_SHARES
EXISTING_COVERAGE_ABOVE_TARGET
EXISTING_EXPOSURE_EXCEEDS_PHYSICAL_CAPACITY
DELTA_EXPOSURE_LIMIT
EXISTING_DER_AT_OR_ABOVE_MAXIMUM
INCONSISTENT_ZERO_SHARE_EXPOSURE
INSUFFICIENT_DATA
UNSUPPORTED_ASSET_TYPE
```

Human-readable explanations accompany these codes but are not parsed for business logic.

---

# 22. Position Sizing Input Snapshot

Phase 4 `HoldingContext` intentionally contains only Phase 4 inputs.

Phase 5 therefore creates a separate immutable sizing context rather than expanding/reinterpreting historical Phase 4 records.

Conceptually:

```text
PositionSizingHoldingContext
    HoldingId
    AccountId
    Symbol
    AssetType
    SharesOwned
    AssignmentSensitivity
    TaxSensitivity
    MaximumCoverageRatio
    MaximumDeltaExposureRatio
```

The sizing evaluation additionally snapshots:

```text
source EntryStrategyEvaluation
existing short-call exposure
existing call Delta observations
portfolio concentration inputs
resolved PositionSizingConfiguration
SizingTimestampUtc
ConfigurationVersion
PositionSizingStrategyVersion
```

---

# 23. Evaluation Time Semantics

Phase 5 has its own:

```text
SizingTimestampUtc
```

This is distinct from Phase 4:

```text
EvaluationTimestampUtc
IndicatorAsOfDate
```

Phase 5 uses immutable Phase 4 facts for:

- CCOS;
- preferred contract;
- Contract Score;
- preferred-contract Delta.

Mutable Phase 5 facts such as current owned shares and existing short-call exposure are snapshotted at sizing time.

Concentration uses prices anchored to the Phase 4 `IndicatorAsOfDate` so the concentration calculation remains reproducible and aligned with the market context underlying the entry evaluation.

No hidden maximum allowed age between the Phase 4 evaluation and Phase 5 sizing evaluation exists in V1.

---

# 24. PositionSizingEvaluation Result

The conceptual result shape is:

```text
PositionSizingEvaluation
    PositionSizingEvaluationId
    EntryStrategyEvaluationId

    CalculatedAtUtc
    SizingTimestampUtc

    Status
    ReasonCodes[]
    MissingInputs[]

    HoldingContext
        HoldingId
        AccountId
        Symbol
        AssetType
        SharesOwned
        AssignmentSensitivity
        TaxSensitivity
        MaximumCoverageRatio
        MaximumDeltaExposureRatio

    PhysicalCapacityContracts

    ExistingCoveredContracts
    ExistingCoveredShares
    AvailableShares
    AvailableContracts

    Ccos
    CcosBaseCoverageRatio

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

    LimitingFactors[]

    ConfigurationVersion
    PositionSizingStrategyVersion

    Explanation
```

The exact C# record decomposition may differ, but no required reproducibility or explainability data may be discarded.

---

# 25. Persistence

Phase 5 persists a separate immutable:

```text
PositionSizingEvaluation
```

It does not rewrite:

```text
EntryStrategyEvaluation
```

A Position Sizing evaluation must permanently reference its source:

```text
EntryStrategyEvaluationId
```

Historical Position Sizing evaluations are append-only.

Later changes to:

- Holding shares/settings;
- open short-call state;
- market prices;
- option Delta;
- configuration;
- strategy version;
- Phase 4 evaluations;

must not alter historical retrieval.

As in Phase 4, a relational summary plus immutable structured payload is acceptable.

The payload must preserve the actual exposure, Delta observations, concentration inputs, configuration, versions, calculations, limiting factors, missing inputs, and explanations used.

---

# 26. Version Ownership

V1 continues the existing shared:

```text
ConfigurationVersion
```

for tunable strategy parameters.

Position Sizing also has explicit strategy/calculation identity, conceptually:

```text
PositionSizingStrategyVersion
```

A configuration change includes changes such as:

- ASL5 maximum 0.50 -> another configured value;
- concentration bands;
- sizing multipliers;
- CCOS base-coverage percentages.

A Position Sizing strategy-version change includes changes such as:

- changing calculation order;
- changing floor semantics;
- changing DER formula;
- changing concentration definition;
- changing target-vs-incremental semantics;
- changing missing-data behavior.

Phase 3 `IndicatorCalculationVersion` is not overloaded for Position Sizing.

---

# 27. API Boundary

Phase 5 should use a separate immutable resource boundary.

Conceptually:

```http
POST /api/entry-evaluations/{entryStrategyEvaluationId}/position-sizing-evaluations

GET /api/position-sizing-evaluations/{positionSizingEvaluationId}
```

A lightweight holding/source-evaluation history endpoint may also be added if required by acceptance design.

The existing Phase 4:

```http
POST /api/holdings/{holdingId}/entry-evaluations
```

must not silently begin running Phase 5.

That endpoint retains its established Phase 4 meaning.

No Phase 5 PUT/PATCH/DELETE endpoint should mutate an immutable sizing evaluation.

Exact API semantics are locked in the Phase 5 acceptance document before implementation.

---

# 28. Preliminary Work Packets

Implementation should proceed in focused packets:

```text
5A — Position Sizing foundations
     configuration
     strategy version
     statuses/reason codes
     sizing contexts
     input/result contracts
     validation

5B — Base coverage and caps
     physical capacity
     CCOS base curve
     ASL modifier
     ASL maximum
     Contract Quality modifier
     Holding maximum
     floor conversion

5C — Portfolio concentration
     same-account portfolio context
     as-of prices
     stock modifier
     ETF NotApplicable behavior
     missing-data semantics

5D — Existing exposure and DER
     minimal open-short-call current-state model
     available shares/contracts
     existing Delta observations
     ExistingDER
     proposed DER
     DER-limited additional contracts

5E — Application orchestration
     load immutable Phase 4 evaluation
     snapshot Holding sizing inputs
     load portfolio concentration context
     load existing short-call state/Delta
     invoke pure Strategy

5F — Immutable persistence
     PositionSizingEvaluation
     repository
     relational summary + immutable payload
     EF migration
     clean/upgrade migration tests

5G — API and merge gate
     immutable POST/GET/history surface
     API tests
     golden sizing scenarios
     full release build/test/migration validation
```

No packet may introduce a new financial rule that is not in the approved design/specification.

---

# 29. Testing Requirements

Every numerical boundary requires below/exact/above tests.

At minimum, Phase 5 acceptance tests must cover:

```text
CCOS:
70
75
80
85
90
95

Contract Score:
80
85
90
95

Concentration:
5%
15%
25%
40%

MaximumCoveragePercent:
equality and immediately above/below

Assignment Sensitivity maximum:
equality and immediately above/below

DER:
exact maximum
immediately below
proposed value immediately above

Shares:
0
99
100
199
200
fractional shares

Target contract flooring:
just below whole contract
exact whole contract
just above whole contract
```

Golden scenarios should include at least:

```text
InitialSingleContract
IncrementalScaleUp
AlreadyAtTarget
ExistingCoverageAboveTarget
HoldingCoverageLimited
AssignmentSensitivityLimited
ConcentrationLimited
DeltaExposureLimited
NoAvailableShares
TargetRoundsToZero
EtfNeutralConcentration
MissingPortfolioPrice
MissingExistingCallDelta
NoEntryCandidate
```

Strategy tests must remain free from live market providers, current wall-clock dependence, EF Core, HTTP, and brokerage APIs.

---

# 30. Explicitly Deferred

The following are explicitly deferred beyond initial Phase 5 V1:

```text
strike ladder allocation
multiple-contract-type allocation
expiration laddering
pending brokerage-order reservation
brokerage synchronization
automatic execution
closing existing calls when target declines
profit-taking actions
defensive actions
DRS
Roll Engine
RQS
campaign accounting
transaction ledger
final Recommendation lifecycle
household/cross-account concentration
cash/net-liquidation-value portfolio weighting
ETF-specific concentration penalties
```

---

# 31. Required Repository Documentation Updates

The approved Phase 5 decisions require updates to existing repository documents before implementation begins.

No implementation packet should begin until these changes and the Phase 5 acceptance document are committed.

## 31.1 SPECIFICATION.md — Section 10 Holding

Keep the existing fields, but explicitly add the following semantics after the Holding field list:

```text
For Position Sizing V1:

Holding.Shares is the authoritative owned-share quantity.

MaximumCoveragePercent is represented as a fractional ratio in the range [0,1]:
0.70 = 70%.

MaximumDeltaExposureRatio is represented as a fractional ratio in the range [0,1]:
0.15 = 15%.

Both values must be finite and validated within [0,1].
```

No naming-only schema migration is required for `MaximumCoveragePercent`.

## 31.2 SPECIFICATION.md — Section 16 Recommendation

Replace the Phase 4-only boundary sentence with a Phase 4/5 boundary that states:

```text
Phase 4 does not create the final Recommendation.

Phase 5 creates a separate immutable PositionSizingEvaluation and also does not create the final Recommendation.

A later recommendation-assembly layer may combine the immutable EntryStrategyEvaluation and PositionSizingEvaluation into final SELL/recommended-contract semantics.
```

Add a new subsection after Phase 4 Entry Strategy Evaluation describing `PositionSizingEvaluation` as an immutable historical sizing record referencing `EntryStrategyEvaluationId`.

## 31.3 SPECIFICATION.md — Section 20 Strategy and Calculation Configuration

Add `PositionSizing` configuration details:

```text
PositionSizing
    CCOS base-coverage bands
    Assignment Sensitivity modifiers
    Assignment Sensitivity maximum coverage
    Contract Quality modifiers
    stock concentration modifiers
```

Clarify that Position Sizing tunable values use the shared `ConfigurationVersion`, while algorithmic sizing behavior has separate strategy-version identity and does not use `IndicatorCalculationVersion`.

## 31.4 SPECIFICATION.md — Replace Section 28 Position Sizing Engine

Replace the current short conceptual section with deterministic semantics from this design:

- source must be an immutable Phase 4 evaluation;
- V1 sizes only `PreferredInitialContract`;
- `Holding.Shares` is authoritative;
- physical capacity is `floor(Shares / 100)`;
- distinguish existing covered contracts, available shares, available contracts;
- output desired total and additional contract counts;
- zero contracts is a valid sizing outcome;
- no close/BTC recommendation is produced.

## 31.5 SPECIFICATION.md — Replace Section 29 Assignment Sensitivity

Replace the ambiguous baseline maximum table with:

```text
ASL1  maximum 1.00   modifier 1.25
ASL2  maximum 0.90   modifier 1.10
ASL3  maximum 0.80   modifier 1.00
ASL4  maximum 0.70   modifier 0.85
ASL5  maximum 0.50   modifier 0.70
```

State explicitly that both the multiplier and maximum apply.

Remove the `50–60%` ambiguity.

## 31.6 SPECIFICATION.md — Replace Section 30 Contract Quality Sizing Modifier

Change integer-looking bands into explicit continuous boundaries:

```text
Contract Score < 80         0.00
80 <= score < 85            0.90
85 <= score < 90            1.00
90 <= score < 95            1.10
95 <= score <= 100          1.15
```

Clarify that the score is the persisted Contract Score of the Phase 4 preferred contract.

A preferred contract below 80 under custom Phase 4 thresholds produces a valid zero-size result.

## 31.7 SPECIFICATION.md — Replace Section 31 Concentration Modifier

Define:

```text
PortfolioWeight =
    TargetHoldingMarketValue
    /
    Sum(MarketValue of tracked Holdings in the same Account)
```

Add:

- same-account scope;
- all tracked holdings participate regardless of strategy enabled state;
- cash/untracked assets excluded;
- latest persisted daily Close at/before Phase 4 IndicatorAsOfDate;
- missing any required participating price => InsufficientData;
- stock bands with explicit 5/15/25/40 boundaries;
- ETFs => NotApplicable, effective modifier 1.00;
- AssetType.Other => unsupported/insufficient for V1.

## 31.8 SPECIFICATION.md — Replace Section 32 Position Sizing Formula

Replace the conceptual formula with the exact sequence:

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

AdditionalContracts =
    min(
        DesiredAdditionalContracts,
        AvailableContracts,
        DERLimitedAdditionalContracts
    )

ResultingTotalContracts =
    ExistingCoveredContracts
    + AdditionalContracts
```

Remove the special tax-rounding statement as redundant because all contract conversion floors.

## 31.9 SPECIFICATION.md — Replace Section 33 Delta Exposure Ratio

Retain the core DER concept but define:

```text
ExistingDeltaShares =
    SUM(ExistingContracts_i * ExistingCallDelta_i * 100)

ExistingDER =
    ExistingDeltaShares / SharesOwned

ProposedDER(N) =
    (
        ExistingDeltaShares
        + N * PreferredContractDelta * 100
    )
    / SharesOwned
```

Add these rules:

- Delta finite and within [0,1];
- do not apply `abs()` to malformed Delta;
- different existing positions use their own Deltas;
- equality with MaximumDeltaExposureRatio is allowed;
- missing existing-call Delta => InsufficientData;
- preferred new-call Delta comes from immutable Phase 4 observation;
- existing call Delta comes from latest persisted normalized observation at/before SizingTimestampUtc;
- persist the observation/timestamp consumed;
- no hidden freshness threshold in V1;
- DER at/above maximum => zero additional contracts, not close action;
- zero shares with existing calls is inconsistent exposure.

## 31.10 SPECIFICATION.md — Replace Section 34 Strike Laddering

Replace the current statement that V1 shall support strike laddering with:

```text
Strike laddering is deferred from initial Phase 5 V1.

Phase 5 V1 sizes only the Phase 4 PreferredInitialContract.

The previously proposed Conservative/Core/Income delta tiers remain future product intent until allocation, remainder, missing-tier, Contract Score, and DER semantics are explicitly specified.
```

## 31.11 SPECIFICATION.md — Replace Section 35 Scaling

Define target state separately from action:

```text
Position Sizing returns:
    DesiredCoverageRatio
    DesiredTotalContracts
    DesiredAdditionalContracts
    AdditionalContracts
    ResultingTotalContracts
```

State:

- existing below target => add subject to capacity/DER;
- existing at target => zero additional;
- existing above target => zero additional plus explicit reason;
- Position Sizing never recommends closing calls because the target declined.

Remove the current statement that declining CCOS may close calls without replacement from Phase 5; that belongs to later profit-taking/defense behavior.

## 31.12 SPECIFICATION.md — Sections 61 and 62

Retain `PositionSizingService` and `IPositionSizingEngine`, but clarify:

- Application assembles mutable current-state inputs;
- Strategy calculation is pure and synchronous;
- Strategy receives no EF/provider/HTTP dependencies.

## 31.13 SPECIFICATION.md — Section 63 API

Add the conceptual immutable resource:

```http
POST /api/entry-evaluations/{entryStrategyEvaluationId}/position-sizing-evaluations
GET /api/position-sizing-evaluations/{positionSizingEvaluationId}
```

Do not change the established semantics of the Phase 4 entry-evaluation POST.

The Phase 5 acceptance document should lock exact history/error semantics before implementation.

## 31.14 SPECIFICATION.md — Section 81 Phase 5

Replace the current short Phase 5 list with the approved boundary and work packets:

```text
5A — Position Sizing foundations
5B — Base coverage and caps
5C — Portfolio concentration
5D — Existing exposure and DER
5E — Application orchestration
5F — Immutable persistence
5G — API and merge gate
```

Explicitly state strike laddering and final Recommendation are deferred.

## 31.15 SPECIFICATION.md — Section 83 Core Decision Pipeline

Clarify the artifact boundary:

```text
EntryStrategyEvaluation
        |
        v
PositionSizingEvaluation
        |
        v
Recommendation
```

Do not imply Phase 5 itself creates the final Recommendation.

---

## 31.16 docs/design/OPTIONS-ENGINE-DESIGN-DECISIONS.md

Add a new durable section titled:

```text
## Phase 5 Position Sizing
```

It should record the approved decisions from this design, including:

- separate immutable `PositionSizingEvaluation`;
- no final Recommendation in Phase 5;
- single preferred-contract sizing;
- strike laddering deferred;
- `Holding.Shares` authoritative;
- minimal existing-short-call current-state model;
- pending orders excluded;
- exact CCOS base curve;
- both ASL modifier and maximum;
- ASL5 maximum = 0.50 default/configurable;
- persisted preferred Contract Score;
- same-account tracked-equity concentration;
- ETF concentration NotApplicable / modifier 1.00;
- exact formula ordering;
- floor semantics;
- tax rounding rule removed;
- existing/proposed DER formulas;
- persisted Delta semantics;
- target vs incremental output;
- no Phase 5 close recommendation;
- status and zero-result semantics;
- shared ConfigurationVersion plus Position Sizing strategy identity.

---

## 31.17 docs/design/OPTIONS-ENGINE-HANDOFF.md

Update the stale repository status.

Specifically:

1. Change the working branch from `phase4` to `phase5`.
2. State Phase 4 is merged into `main`.
3. Add Phase 5 design status and link this document.
4. List the approved Position Sizing decisions.
5. Add the 5A–5G packet sequence.
6. State that no implementation begins until:
   - `SPECIFICATION.md` is reconciled;
   - `OPTIONS-ENGINE-DESIGN-DECISIONS.md` is updated;
   - `docs/acceptance/PHASE-5-POSITION-SIZING.md` exists and is approved.

---

## 31.18 New Acceptance Document

Create before implementation:

```text
docs/acceptance/PHASE-5-POSITION-SIZING.md
```

It should lock:

- every Phase 5 boundary;
- exact input/output contracts;
- reason/missing-input codes;
- all below/exact/above threshold tests;
- golden scenarios;
- persistence immutability;
- migration requirements;
- API behavior;
- packet ownership;
- merge gate.

This acceptance document is required before Phase 5A implementation begins.

---

# 32. Definition of Ready for Phase 5 Implementation

Phase 5 implementation is ready only when:

- this design is approved;
- `SPECIFICATION.md` is reconciled with this design;
- the durable design-decision register records the approved Phase 5 rules;
- the handoff reflects Phase 5 status;
- `docs/acceptance/PHASE-5-POSITION-SIZING.md` exists and is approved;
- no unresolved sizing formula, threshold, allocation rule, rounding rule, exposure rule, or missing-data rule remains.

Until then, no Position Sizing Strategy implementation, migration, or API route should be added.

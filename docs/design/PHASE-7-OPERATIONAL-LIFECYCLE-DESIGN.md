# Phase 7 — Operational Trade and Position Lifecycle Design

## 1. Status and Authority

This document is the approved Phase 7 design. It defines the operational truth layer that connects manually executed covered-call activity to immutable history, campaigns, and the mutable current-position projection consumed by Phases 5 and 6.

`SPECIFICATION.md` remains authoritative. This design introduces no entry, sizing, defense, roll-selection, tax, or profit-optimization rule.

Phase 7 remains decision support with manual execution. It never places, changes, or cancels a brokerage order.

## 2. Objective and Boundary

The governing objective remains lexicographic:

1. Avoid assignment and preserve the underlying shares.
2. Subject to that protection, maximize cumulative net covered-call profit.

Phase 7 does not optimize either objective. It records what actually happened and maintains authoritative operational state.

```text
Analytical evaluation
        |
User executes manually
        |
Record actual lifecycle operation
        |
Immutable operation / transaction / fill history
        |
Campaign and current-position projection updated atomically
        |
Phases 5 and 6 consume authoritative current state
```

Phase 7 owns:

- manual recording of STO, BTC, roll, expiration, and assignment;
- imported already-open positions;
- immutable operations, option transactions, and execution fills;
- campaign identity and lifecycle;
- current open-short-call projection maintenance;
- idempotency and auditable correction;
- optional links to the analytical artifacts that informed execution;
- minimal lifecycle command and read APIs.

Phase 7 does not own performance reporting, tax accounting, brokerage synchronization, early-assignment formulas, the Daily Decision API, or Excel.

## 3. Existing Baseline

The implemented baseline has:

- `Account`, `Holding`, and `TaxLot` identities as `Guid`;
- authoritative `Holding.Shares` as `decimal`;
- immutable Phase 4–6 analytical artifacts identified by `Guid`;
- a narrow `OpenShortCallPosition` with a `long` identity;
- one open-position row as the Phase 6 evaluation unit;
- nullable opening premium and opening time for imported positions;
- read-only current-position repositories;
- no public transaction, campaign, or position-lifecycle API.

Phase 7 extends this baseline rather than replacing Phase 4–6 artifacts.

## 4. Operation, Transaction, and Fill Vocabulary

The user-facing Application commands are:

```text
RecordSto
ImportOpenPosition
RecordBtc
RecordRoll
RecordExpiration
RecordAssignment
CorrectOperation
```

Each accepted command creates an immutable `LifecycleOperation` audit envelope. Economic option records use only:

```text
STO
BTC
EXPIRE
ASSIGN
```

`ROLL` is an Application operation, not an economic transaction action. One roll operation contains exactly:

```text
one BTC transaction for the old option
+
one STO transaction for the replacement option
```

The derived roll presentation is:

```text
NetRollCreditOrDebit =
    Replacement STO Credit
    - Existing BTC Cost
```

It is not a third transaction or an additional campaign-profit input.

## 5. LifecycleOperation

`LifecycleOperation` is the command-level audit and idempotency record.

```text
LifecycleOperationId       Guid, server generated
ClientOperationId          Guid, client supplied, unique
OperationType              STO | IMPORT | BTC | ROLL | EXPIRE | ASSIGN | CORRECTION
CampaignId                 Guid
HoldingId                  Guid
AccountId                  Guid
RecordedAtUtc              DateTimeOffset, server time
RequestFingerprint         stable canonical payload fingerprint
CorrectsOperationId?       Guid

EntryStrategyEvaluationId?     Guid
PositionSizingEvaluationId?    Guid
DefenseEvaluationId?           Guid
RollEvaluationId?              Guid

Notes?                     string
WarningCodes               stable machine-readable codes
```

The operation stores command intent and audit relationships. It does not replace its economic transactions.

`ClientOperationId` supplies idempotency:

```text
same ClientOperationId + identical canonical request
    -> return the original result without another write

same ClientOperationId + different canonical request
    -> conflict
```

The uniqueness rule applies across every lifecycle command type.

### 5.1 Lifecycle Timestamps

All client-supplied Phase 7 lifecycle timestamps use `DateTimeOffset` and must
have `Offset == TimeSpan.Zero`. This includes fill execution timestamps,
effective timestamps for assignment and expiration, and a reported historical
opening timestamp when one is supplied during import. A non-UTC value is a
validation error; the command layer does not convert local, Eastern, offset, or
unspecified timestamps to UTC.

`RecordedAtUtc` is server-owned, comes from the injected time source, and must
also have a zero offset.

## 6. OptionTransaction

`OptionTransaction` is one immutable covered-call economic leg or terminal option event.

```text
TransactionId              Guid
LifecycleOperationId       Guid
CampaignId                 Guid
HoldingId                  Guid
AccountId                  Guid

Action                     STO | BTC | EXPIRE | ASSIGN
OptionSymbol               string
Contracts                  positive int
Strike                     decimal
Expiration                 DateOnly
EffectiveTimestampUtc      DateTimeOffset

WeightedAverageFillPricePerShare? decimal
Fees?                      decimal
UnderlyingPriceAtExecution? decimal
```

Rules:

- quantities are always positive; `Action` determines direction;
- money and prices use `decimal`;
- STO/BTC require at least one fill and a non-negative weighted-average fill price;
- EXPIRE/ASSIGN have no option fill and therefore a null fill price;
- fees are the total fees for the transaction leg, are non-negative when known, and remain null when unavailable;
- known zero fees are stored as zero, never inferred from null;
- underlying price is optional context and is never fabricated;
- option symbol, strike, and expiration are required for every action;
- the transaction is never updated or deleted.

For STO/BTC, `EffectiveTimestampUtc` is the latest contributing fill timestamp. For EXPIRE/ASSIGN, it is the supplied broker-effective event timestamp.

## 7. ExecutionFill

Every STO/BTC transaction has one or more immutable fills:

```text
ExecutionFillId            Guid
TransactionId              Guid
Contracts                  positive int
FillPricePerShare          decimal, non-negative
ExecutionTimestampUtc      DateTimeOffset
```

All fills in one transaction share its option identity and action. The transaction summaries must reconcile exactly:

```text
Transaction.Contracts = Sum(Fill.Contracts)

WeightedAverageFillPricePerShare =
    Sum(FillPricePerShare * Fill.Contracts)
    / Sum(Fill.Contracts)
```

Fees are recorded at the transaction-leg level because brokerage statements may report one total fee for several fills. Phase 7 retains fill-level quantity, price, and time without inventing fee allocation.

An aggregate broker execution may be recorded as one fill. Multiple fills must not be collapsed when their details are available.

## 8. Campaign

A Campaign groups one initial STO exposure and all later lifecycle activity that remains connected through rolls.

```text
CampaignId                 Guid
HoldingId                  Guid
AccountId                  Guid
Symbol                     string
Origin                     Executed | Imported
State                      Open | Closed
TerminalOutcome?           BoughtToClose | Expired | Assigned | Mixed
StartedAtUtc?              DateTimeOffset
ImportedAtUtc?             DateTimeOffset
ClosedAtUtc?               DateTimeOffset
EconomicsCompleteness      Complete | Incomplete
```

Campaign rules:

- `RecordSto` creates an `Executed` campaign;
- `ImportOpenPosition` creates an `Imported` campaign with incomplete economics;
- `StartedAtUtc` is the earliest initial-STO fill time for an executed campaign;
- an imported campaign may have an unknown start time and must not use import time as a fabricated execution time;
- a campaign remains `Open` while any current position exists;
- it becomes `Closed` when no current position remains;
- it cannot reopen; a later standalone STO starts a new campaign;
- a roll preserves `CampaignId`;
- `ROLLED` is not a campaign state;
- roll count and all economic totals are derived from effective immutable history;
- Campaign does not persist mutable GrossPremium, BtcCost, RollCredit, RollDebit, or NetProfit totals.

`EconomicsCompleteness` is Complete only when the Campaign has an authoritative initial STO and every effective transaction fee and other required Phase 7 economic fact is known. Imported history or any unknown required value makes it Incomplete. Correction/replay recomputes this projection.

Terminal outcome is assigned when the campaign closes:

- `BoughtToClose` when all terminal contract quantities ended by standalone BTC;
- `Expired` when all terminal contract quantities ended by expiration;
- `Assigned` when all terminal contract quantities ended by assignment;
- `Mixed` when terminal outcomes differ.

All quantity in a valid roll continues the Campaign and is not a terminal outcome.

## 9. Current Open-Position Projection

`OpenShortCallPosition` remains narrow mutable operational state:

```text
OpenShortCallPositionId    long
CampaignId                 Guid
HoldingId                  Guid
OptionSymbol               string
Contracts                  positive int
Strike                     decimal
Expiration                 DateOnly
OpeningPremiumPerShare?    decimal
OpenedAtUtc?               DateTimeOffset
```

Rules:

- one row remains one Phase 6 evaluation unit;
- every Phase 7-created row is traceable to a Campaign;
- active exposure is consolidated by Campaign and option identity;
- separate campaigns are never merged even when they use the same option;
- `OpeningPremiumPerShare` is the contract-weighted average gross STO credit for the fills contributing to that current position;
- `OpenedAtUtc` is the earliest contributing opening fill timestamp;
- imported values may remain null;
- the row contains no campaign accounting totals;
- a zero-contract row is never retained;
- when exposure becomes zero, the projection row is removed while immutable history remains;
- historical DefenseEvaluation records retain their captured position identity and payload after projection removal.

A Holding may have multiple current positions and multiple campaigns. A campaign may have multiple current positions after a partial roll because unrolled old contracts may remain alongside replacement contracts.

After every command:

```text
Sum(CurrentPosition.Contracts * 100 for Holding)
    <= floor(Holding.Shares / 100) * 100
```

This is an operational covered-share invariant. Strategy limits such as maximum coverage, DER, preferred delta, or a recommended quantity do not prevent reality from being recorded when the physical invariant remains valid.

## 10. RecordSto

`RecordSto` records a completed manually executed initial sale.

Required input:

- client operation ID;
- Holding identity;
- option symbol, strike, and expiration;
- one or more actual fills;
- fees, including explicit zero when known or null when unavailable;
- optional underlying price, notes, and analytical links.

It atomically creates:

```text
LifecycleOperation(STO)
OptionTransaction(STO)
ExecutionFill(s)
Campaign(Executed, Open)
OpenShortCallPosition
```

The actual fills are authoritative. A Phase 4 Bid/reference premium is never substituted for execution price.

A standalone STO always creates a new Campaign. Adding to an existing campaign is allowed only as the STO leg of `RecordRoll`.

Holding/Account enabled flags control analytical eligibility, not whether executed reality can be recorded. An existing disabled Holding may receive lifecycle updates; missing identity or invalid ownership still fails.

## 11. ImportOpenPosition

Phase 7 supports already-open short calls whose authoritative historical STO execution is unavailable.

`ImportOpenPosition` atomically creates:

```text
LifecycleOperation(IMPORT)
Campaign(Imported, Open, Incomplete)
OpenShortCallPosition
```

It creates no transaction and no fill. It may accept reported opening premium/time, but absent values remain null. Import time is stored separately and never substituted for execution time.

Imported positions participate in Phase 5/6 current-state analysis. Phase 11 must be able to identify their economics as incomplete.

### 11.1 Existing Phase 5/6 Rows During Upgrade

Every pre-Phase-7 `OpenShortCallPosition` row shall be preserved and associated with its own Imported Campaign during schema upgrade or an explicitly versioned upgrade step. Because prior rows contain no reliable campaign grouping, the migration must not infer that two rows share a Campaign.

The backfill creates no transaction, fill, or fabricated execution time. Its Campaign has `Origin = Imported`, `EconomicsCompleteness = Incomplete`, and unknown start time. A migration/backfill audit marker distinguishes it from an interactive `ImportOpenPosition` command. No existing opening premium or opening timestamp is changed.

## 12. RecordBtc

`RecordBtc` targets one current position and records one completed BTC transaction with fills.

- partial BTC is allowed;
- quantity must not exceed current contracts;
- the current row is reduced by actual quantity;
- when quantity reaches zero, the row is removed;
- Campaign remains open while another current position exists;
- Campaign closes with the appropriate terminal outcome when none remain;
- a source DefenseEvaluation is optional;
- BTC may be recorded regardless of the latest analytical disposition.

The ledger records reality, not compliance with a recommendation.

## 13. RecordRoll

`RecordRoll` is one atomic user operation recorded after both executed legs are known.

It creates:

```text
LifecycleOperation(ROLL)
OptionTransaction(BTC old option) + fills
OptionTransaction(STO replacement option) + fills
```

and atomically:

- reduces or removes the old current position;
- creates or updates the replacement current position;
- preserves CampaignId;
- keeps the Campaign open;
- persists both legs or neither.

Rules:

- the BTC quantity must not exceed the old current quantity;
- both leg quantities are positive;
- BTC contracts must equal replacement STO contracts;
- a partial roll is valid and need not replace the whole old position;
- exposure added or closed beyond the one-for-one replacement is recorded through separate lifecycle operations, not embedded in `RecordRoll`;
- the resulting Holding exposure must satisfy physical covered-share capacity;
- replacement option identity must differ from the old option identity;
- leg fills retain their own execution timestamps and fees;
- no Phase 6 hypothetical quantity, strike, delta, expiration, RQS, or disposition is imposed on actual execution;
- optional Defense/Roll evaluation links describe the analysis consulted, not a mandate that execution match it.

If only one broker leg actually executes, the user records only that completed reality as a standalone BTC or STO workflow. Phase 7 never fabricates the missing leg. A later standalone STO begins a new Campaign because a closed Campaign cannot reopen.

## 14. RecordExpiration

`RecordExpiration` records that some or all contracts expired worthless.

- quantity is positive and cannot exceed current contracts;
- fill price is null, not zero;
- there are no execution fills;
- broker-effective timestamp is required;
- fees may be known zero, known non-zero, or unavailable;
- current exposure is reduced or removed;
- Campaign closes when no current position remains.

Expiration is a lifecycle event, not a cash-flow transaction with a fabricated zero fill.

## 15. RecordAssignment

`RecordAssignment` records assignment of some or all current contracts.

- quantity is positive and cannot exceed current contracts;
- assigned shares are exactly `Contracts * 100`;
- assignment price per share is the snapshotted option strike;
- fill price is null and there are no option execution fills;
- broker-effective timestamp is required;
- current option exposure is reduced or removed;
- `Holding.Shares` is reduced by assigned shares in the same database transaction;
- insufficient Holding shares rejects the operation;
- other current calls must remain physically covered after the share reduction;
- Campaign closes when no current position remains.

Phase 7 does not calculate underlying-sale P&L, tax, gain, or tax-lot selection. It does not mutate TaxLot records. Assignment persists and returns an explicit `TAX_LOT_RECONCILIATION_REQUIRED` operation warning so the system never implies tax-lot state is current.

STO, BTC, roll, and expiration do not change `Holding.Shares`.

## 16. Analytical Artifact Links

Analytical links are optional and belong to `LifecycleOperation`.

When supplied:

- EntryStrategyEvaluation must exist and belong to the same Holding;
- PositionSizingEvaluation must exist, belong to that Entry evaluation, and resolve to the same Holding;
- DefenseEvaluation must exist and refer to the targeted Holding/current-position identity;
- RollEvaluation must exist and belong to the supplied DefenseEvaluation;
- a Sizing link requires its Entry link;
- a Roll link requires its Defense link.

Suggested use:

```text
STO     -> EntryStrategyEvaluationId?, PositionSizingEvaluationId?
BTC     -> DefenseEvaluationId?
ROLL    -> DefenseEvaluationId?, RollEvaluationId?
EXPIRE  -> optional DefenseEvaluationId
ASSIGN  -> optional DefenseEvaluationId
IMPORT  -> no analytical link required
```

Option, quantity, price, and timing may differ from the analytical artifact. Such divergence is retained and remains recordable. Recording execution never mutates a source evaluation.

## 17. Atomicity and Concurrency

Each lifecycle command is one database transaction.

```text
STO:
    operation + transaction + fills + campaign + current position

IMPORT:
    operation + imported campaign + current position

BTC:
    operation + transaction + fills + position update/removal + campaign transition

ROLL:
    operation + two transactions + fills + old/new projections + campaign update

EXPIRE / ASSIGN:
    operation + event transaction + projection update/removal + campaign transition
    + Holding share update for ASSIGN

CORRECTION:
    correction link + replacement operation/history + rebuilt projections
```

All writes succeed or none do. Validation and idempotency resolution occur within the same unit of work. Conflicting current state returns a structured conflict rather than partially persisting.

## 18. Correction Policy

No operation, transaction, or fill is edited or deleted.

`CorrectOperation`:

1. creates a new idempotent correction command;
2. references the operation being corrected;
3. appends a replacement operation of the same lifecycle type and its immutable records;
4. marks the relationship through append-only correction metadata;
5. treats the original operation as superseded for effective projection/accounting;
6. replays the effective Campaign history;
7. atomically rebuilds current-position and Holding-share projections.

The original remains visible in audit history. Corrections do not add artificial reversal cash-flow legs. Phase 11 shall consume only effective, non-superseded transactions while retaining the correction chain for audit.

A correction is rejected when:

- the target does not exist or belongs to another Campaign;
- the target is already superseded;
- the replacement changes operation type, Holding, Account, or Campaign;
- replay would produce negative quantity, uncovered exposure, an invalid closed/reopened Campaign, or another invalid lifecycle;
- analytical-link validation fails.

Corrections to imported state remain imports and never manufacture historical transactions.

## 19. Campaign Economics Boundary

Phase 7 stores authoritative facts:

- STO/BTC fills;
- transaction-leg fees;
- timestamps;
- option identity and quantity;
- lifecycle outcomes;
- effective correction relationships.

It does not persist mutable campaign totals. Phase 11 calculates:

```text
NetCampaignProfit =
    Sum(effective STO premium credits)
    - Sum(effective BTC costs)
    - Sum(effective known fees)
    - Sum(other explicitly approved strategy costs)
```

A derived roll credit/debit is never added again. Unknown fees or imported history must remain explicit completeness limitations rather than becoming zero.

## 20. API Boundary

The Phase 7 HTTP surface shall expose thin use-case commands corresponding to the approved Application operations. It shall not expose generic table CRUD or automatic brokerage operations.

Exact REST paths are intentionally deferred to the Phase 7 API implementation packet. The command semantics in this design are authoritative; paths are not.

Expected transport behavior:

- successful new command: `201 Created` or appropriate success result;
- identical idempotent replay: the original success result;
- reused ClientOperationId with different payload: `409 Conflict`;
- missing Holding, Campaign, position, or analytical artifact: `404 Not Found`;
- stale/invalid lifecycle transition: `409 Conflict`;
- malformed values: structured `400 Bad Request`;
- no PUT/PATCH/DELETE mutation of immutable history.

## 21. Minimal Phase 7 Reads

Phase 7 shall expose enough read capability to record and audit lifecycle operations:

```text
current open positions, optionally filtered by Holding
campaign by CampaignId
campaign operation/transaction/fill history
transaction by TransactionId
lifecycle operation by LifecycleOperationId or ClientOperationId
```

Reads expose effective/superseded status where corrections exist. They do not compose the Phase 9 `CoveredCallDecision`.

Phase 7 does not add general Account/Holding management APIs. Those remain separate application/presentation work.

## 22. Error Categories

Stable error categories shall distinguish at least:

```text
HOLDING_NOT_FOUND
CAMPAIGN_NOT_FOUND
OPEN_SHORT_CALL_POSITION_NOT_FOUND
ANALYTICAL_ARTIFACT_NOT_FOUND
ANALYTICAL_ARTIFACT_MISMATCH
IDEMPOTENCY_CONFLICT
INVALID_CONTRACT_QUANTITY
INSUFFICIENT_OPEN_CONTRACTS
INSUFFICIENT_COVERED_SHARES
INVALID_CAMPAIGN_STATE
INVALID_ROLL_LEGS
INVALID_CORRECTION
TAX_LOT_RECONCILIATION_REQUIRED (warning, not command failure)
```

Human messages accompany codes but are not logic contracts.

## 23. Key Invariants

1. Operations, transactions, and fills are immutable.
2. Actual fills are authoritative.
3. Missing financial data is never zero.
4. A roll has one BTC economic leg and one replacement STO economic leg.
5. A roll's BTC and replacement STO contract quantities are equal.
6. Roll credit/debit is derived and never a third cash-flow item.
7. Every client-supplied lifecycle timestamp and server-owned `RecordedAtUtc` has a zero UTC offset.
8. Executed current-position quantity cannot exceed effective ledger-backed quantity; imported quantity must match its effective import operation.
9. Closed, expired, or assigned quantity cannot remain active.
10. Every current position is traceable to one Campaign.
11. A roll preserves CampaignId.
12. One current-position row remains one Phase 6 evaluation unit.
13. Historical Phase 4–6 evaluations never mutate because execution was recorded.
14. Lifecycle commands are atomic and idempotent.
15. Reality may differ from analysis and remains recordable.
16. Campaign totals are derived, not mutable financial truth.
17. Superseded history remains auditable but is excluded from effective projections.

## 24. Implementation Packets

Phase 7 implementation shall be split into reviewable packets:

```text
7A — lifecycle contracts, validation codes, and persistence model design
7B — migration, immutable ledger/campaign persistence, and idempotent unit of work
7C — initial STO and imported-position lifecycle
7D — BTC, expiration, assignment, and Holding-share projection
7E — atomic roll lifecycle and multi-position campaign behavior
7F — correction/replay and audit semantics
7G — command/read API and merge-gate validation
```

No packet may introduce a new strategy formula, execution automation, tax-lot disposal algorithm, campaign-performance calculation, or Phase 9 composition behavior.

## 25. Explicitly Out of Scope

- brokerage order placement, modification, cancellation, or synchronization;
- pending orders and order-state tracking;
- automatic trade execution or rolling;
- tax-lot selection, tax accounting, and after-tax P&L;
- campaign performance reporting or buy-and-hold comparison;
- dividend ingestion and early-assignment formulas;
- Daily Decision API and `CoveredCallDecision` composition;
- Excel workbook behavior;
- research, backtesting, and optimization;
- new entry, sizing, defense, or roll strategy rules.

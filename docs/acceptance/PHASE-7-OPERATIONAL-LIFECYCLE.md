# Phase 7 — Operational Trade and Position Lifecycle Acceptance

## Purpose

Phase 7 is complete only when manually executed covered-call activity can be recorded as immutable operational truth and the resulting Campaign, current-position, and Holding-share projections are correct, atomic, idempotent, and auditable.

This checklist is prospective. No checked item is implied until implementation and validation are complete.

Authoritative design: `docs/design/PHASE-7-OPERATIONAL-LIFECYCLE-DESIGN.md`.

## Scope Boundary

- [ ] Phase 7 introduces no entry, sizing, defense, roll-selection, tax, or profit-optimization rule.
- [ ] All brokerage execution remains manual.
- [ ] No brokerage order is submitted, changed, or cancelled.
- [ ] Phase 4–6 immutable evaluations are never rewritten.
- [ ] Phase 9 Daily Decision API and Phase 10 Excel work are not introduced.
- [ ] Phase 11 performance calculations are not introduced.

## 7A — Lifecycle Contracts and Validation

### Identity and Vocabulary

- [ ] Campaign, lifecycle operation, transaction, and fill use stable `Guid` identities.
- [ ] Existing `long OpenShortCallPositionId` identity remains compatible with Phase 5/6.
- [ ] User operations are `RecordSto`, `ImportOpenPosition`, `RecordBtc`, `RecordRoll`, `RecordExpiration`, `RecordAssignment`, and `CorrectOperation`.
- [ ] Persisted economic actions are only `STO`, `BTC`, `EXPIRE`, and `ASSIGN`.
- [ ] No monetary `ROLL` transaction action exists.
- [ ] Every quantity is a positive integer; action supplies direction.

### Monetary and Missing-Data Semantics

- [ ] Prices and fees use `decimal`.
- [ ] STO/BTC require one or more actual fills.
- [ ] EXPIRE/ASSIGN have null fill price and no execution fills.
- [ ] Unknown fees remain null.
- [ ] Known zero fees remain explicit zero.
- [ ] Missing underlying price remains null.
- [ ] Validation never converts missing financial data to zero.

### Validation Tests

- [ ] Zero and negative contract quantities are rejected.
- [ ] Negative prices and fees are rejected.
- [ ] Every client-supplied lifecycle timestamp uses DateTimeOffset and has `Offset == TimeSpan.Zero`.
- [ ] Non-UTC lifecycle timestamps are rejected as validation errors and are never normalized by the command layer.
- [ ] Local, Eastern, other offset, and unspecified timestamps are rejected.
- [ ] Server-owned RecordedAtUtc comes from the injected time source and has `Offset == TimeSpan.Zero`.
- [ ] Required option identity, strike, and expiration are validated.
- [ ] Stable machine-readable lifecycle error codes are returned.

## 7B — Persistence, Campaign, and Idempotency

### Immutable History

- [ ] LifecycleOperation rows are append-only.
- [ ] OptionTransaction rows are append-only.
- [ ] ExecutionFill rows are append-only.
- [ ] Existing operation, transaction, and fill rows have no update/delete command path.
- [ ] All persisted relationships use restrictive deletion behavior.

### Fill Reconciliation

- [ ] Transaction contract count equals the sum of its fill contract counts.
- [ ] Weighted-average fill price uses contract quantity as the weight.
- [ ] Multiple known fills remain individually retrievable.
- [ ] An aggregate broker fill may be represented as one fill.
- [ ] Transaction-leg fees are retained without fabricated per-fill allocation.

### Campaign

- [ ] Campaign belongs to exactly one Holding and Account.
- [ ] Campaign origin distinguishes Executed from Imported.
- [ ] Campaign state is Open or Closed.
- [ ] Terminal outcome distinguishes BoughtToClose, Expired, Assigned, and Mixed.
- [ ] `ROLLED` is not a Campaign state.
- [ ] A closed Campaign cannot reopen.
- [ ] Roll count and economic totals are derived rather than mutable Campaign columns.
- [ ] Imported Campaign economics are explicitly incomplete.
- [ ] Unknown required fees/economic facts make Campaign economics incomplete rather than becoming zero.
- [ ] Correction/replay recomputes economics completeness.
- [ ] All quantity in a valid one-for-one roll continues the Campaign and is not a terminal outcome.

### Idempotency

- [ ] Every command requires a client-supplied `ClientOperationId`.
- [ ] ClientOperationId has a database uniqueness constraint.
- [ ] Canonical request fingerprinting is deterministic.
- [ ] Identical replay returns the original operation/result without new rows.
- [ ] Reusing an ID with a different payload returns `IDEMPOTENCY_CONFLICT`.
- [ ] Concurrent duplicate submissions cannot create duplicate history.

## 7C — Initial STO and Import

### RecordSto

- [ ] RecordSto atomically creates operation, STO transaction, fills, Campaign, and current position.
- [ ] Any failure leaves none of those records persisted.
- [ ] Actual fill price is authoritative.
- [ ] Phase 4 reference premium is never substituted for actual fill.
- [ ] OpeningPremiumPerShare is the contract-weighted average gross STO credit.
- [ ] OpenedAtUtc is the earliest contributing fill timestamp.
- [ ] A standalone STO creates a new Campaign.
- [ ] Resulting exposure cannot exceed covered-share capacity.

### Analytical Links

- [ ] Entry and Position Sizing links are optional.
- [ ] Supplied Entry evaluation exists and belongs to the same Holding.
- [ ] Supplied Position Sizing evaluation belongs to the supplied Entry evaluation.
- [ ] Execution may differ from evaluated option, quantity, price, and time.
- [ ] Recording execution does not mutate either evaluation.
- [ ] Disabled Holding/Account analytical eligibility does not prevent recording actual lifecycle events.

### ImportOpenPosition

- [ ] Import creates operation, Imported Campaign, and current position atomically.
- [ ] Import creates no fabricated STO transaction or fill.
- [ ] Unknown opening premium remains null.
- [ ] Unknown opening time remains null.
- [ ] Import timestamp is not substituted for execution time.
- [ ] Imported position is available to Phase 5 and Phase 6 reads.
- [ ] Imported economics remain explicitly incomplete after later operations.

## 7D — BTC, Expiration, Assignment, and Shares

### RecordBtc

- [ ] Full BTC closes/removes the targeted current-position projection.
- [ ] Partial BTC reduces it by the actual quantity.
- [ ] BTC quantity above current quantity is rejected.
- [ ] BTC may be recorded without a DefenseEvaluation.
- [ ] BTC is not constrained by DefenseDisposition.
- [ ] Campaign remains open while any position remains.
- [ ] Campaign closes when the final position is removed.

### RecordExpiration

- [ ] Expiration may reduce all or part of a current position.
- [ ] Expiration has null fill price and no fills.
- [ ] Expiration never fabricates zero fill price.
- [ ] Effective timestamp is required.
- [ ] Expired quantity is removed from current exposure.

### RecordAssignment

- [ ] Assignment may reduce all or part of a current position.
- [ ] Assigned shares equal assigned contracts times 100.
- [ ] Assignment price is the snapshotted strike, not a fabricated option fill.
- [ ] Holding.Shares decreases atomically with the assignment event.
- [ ] Insufficient Holding shares rejects the operation.
- [ ] Remaining calls must remain physically covered.
- [ ] TaxLot records are not automatically selected or disposed.
- [ ] Assignment returns and persists `TAX_LOT_RECONCILIATION_REQUIRED`.
- [ ] No underlying-sale P&L or tax calculation is introduced.

### Share Mutation Matrix

- [ ] STO does not change Holding.Shares.
- [ ] BTC does not change Holding.Shares.
- [ ] Roll does not change Holding.Shares.
- [ ] Expiration does not change Holding.Shares.
- [ ] Assignment reduces Holding.Shares.

## 7E — Roll Lifecycle

- [ ] RecordRoll is one atomic Application operation.
- [ ] It creates exactly one BTC transaction leg and one STO transaction leg.
- [ ] Each leg retains its own fills, fees, and timestamps.
- [ ] Both legs belong to the same Campaign.
- [ ] CampaignId is preserved.
- [ ] The old position is reduced or removed.
- [ ] The replacement position is created or updated.
- [ ] Both ledger legs and projection changes commit together or not at all.
- [ ] BTC quantity cannot exceed current old-option quantity.
- [ ] BTC and replacement STO contract quantities are equal.
- [ ] A roll may replace fewer contracts than the old position currently contains.
- [ ] Added or closed exposure beyond the one-for-one roll is rejected and must use a separate lifecycle operation.
- [ ] Resulting aggregate exposure must remain physically covered.
- [ ] Replacement option identity differs from old option identity.
- [ ] Actual execution is not forced to match Phase 6 quantity, option, strike, expiration, delta, or rank.
- [ ] Optional DefenseEvaluation belongs to the targeted Holding/position.
- [ ] Optional RollEvaluation belongs to the supplied DefenseEvaluation.
- [ ] A Roll link requires its Defense link.
- [ ] Net roll credit/debit is derived from the two legs.
- [ ] No third roll cash-flow transaction is persisted.
- [ ] An incomplete broker roll never fabricates its missing leg.

### Multi-Position Campaign Behavior

- [ ] Partial roll may leave old and replacement position rows in one Campaign.
- [ ] Unrolled old contracts remain in the same Campaign alongside the replacement position.
- [ ] One current-position row remains one Phase 6 evaluation unit.
- [ ] Active exposure is consolidated by Campaign and option identity.
- [ ] Same option identity in different Campaigns remains separate.

## 7F — Correction and Replay

- [ ] CorrectOperation is itself idempotent.
- [ ] Original operations, transactions, and fills remain unchanged.
- [ ] Correction appends a replacement operation of the same lifecycle type.
- [ ] Append-only metadata links original, correction, and replacement.
- [ ] Original history remains visible as superseded.
- [ ] Effective reads distinguish superseded from active history.
- [ ] No artificial reversal cash-flow transaction is created.
- [ ] Effective Campaign history is replayed after correction.
- [ ] Current positions and Holding shares are rebuilt atomically.
- [ ] Corrections changing Holding, Account, Campaign, or operation type are rejected.
- [ ] Corrections that make subsequent lifecycle invalid are rejected.
- [ ] A previously superseded operation cannot be corrected again directly.
- [ ] Imported-state correction remains an import and creates no historical STO.

## 7G — API, Reads, and Merge Gate

### Command API

- [ ] API handlers are thin and invoke Application use cases.
- [ ] Command DTOs do not expose EF entities.
- [ ] Exact routes are approved in the implementation packet before coding.
- [ ] Successful new commands return stable created resource identities.
- [ ] Identical idempotent replay returns the original result.
- [ ] Missing resources return structured 404 responses.
- [ ] Invalid transitions and idempotency conflicts return structured 409 responses.
- [ ] Malformed values return structured 400 responses.
- [ ] No PUT/PATCH/DELETE mutation surface exists for immutable history.

### Read API

- [ ] Current open positions can be retrieved and filtered by Holding.
- [ ] Campaign can be retrieved by CampaignId.
- [ ] Campaign operation/transaction/fill history can be retrieved.
- [ ] Transaction can be retrieved by TransactionId.
- [ ] Lifecycle operation can be retrieved by lifecycle or client operation ID.
- [ ] Reads expose effective and superseded status after corrections.
- [ ] Phase 9 CoveredCallDecision composition is not introduced.

### Persistence and Migration Validation

- [ ] Schema changes use a narrowly scoped EF Core migration.
- [ ] Existing Phase 5/6 position rows upgrade safely.
- [ ] Each preexisting Phase 5/6 position is preserved and assigned its own Imported Campaign.
- [ ] Legacy backfill creates no transaction, fill, or fabricated execution time.
- [ ] Legacy opening premium/time values remain unchanged.
- [ ] Clean database migration succeeds.
- [ ] Upgrade from the Phase 6 schema succeeds.
- [ ] EF model snapshot matches the migration.

### Testing and Architecture

- [ ] Domain/Strategy remains independent of EF Core, SQLite, ASP.NET, Tradier, and Excel.
- [ ] Lifecycle orchestration is in Application, persistence in Infrastructure, and transport in API.
- [ ] No live provider or brokerage access is required by tests.
- [ ] Atomic rollback is tested for every command.
- [ ] Concurrent idempotency is tested.
- [ ] Full solution builds with zero compiler errors.
- [ ] Full automated test suite passes.
- [ ] `git diff --check` passes.

## Required Golden Lifecycle Scenarios

- [ ] InitialStoWithOneFill
- [ ] InitialStoWithMultipleFills
- [ ] OffSystemStoWithoutAnalyticalLinks
- [ ] ImportedPositionWithUnknownOpeningEconomics
- [ ] PartialBtc
- [ ] FullBtcClosesCampaign
- [ ] PartialRollCreatesTwoCurrentPositions
- [ ] RollRejectsUnequalLegQuantities
- [ ] PartialRollPreservesUnrolledOldContracts
- [ ] NonUtcLifecycleTimestampRejected
- [ ] RollWouldExceedCoveredShares
- [ ] ExpirationWithNullFillPrice
- [ ] PartialAssignmentReducesHoldingShares
- [ ] AssignmentRequiresTaxLotReconciliation
- [ ] DuplicateIdenticalCommandReplay
- [ ] DuplicateOperationIdConflict
- [ ] CorrectLatestOperation
- [ ] CorrectionInvalidatesDownstreamHistory
- [ ] AnalyticalExecutionDivergence
- [ ] ImportedCampaignEconomicsRemainIncomplete

## Definition of Done

- [ ] Packets 7A–7G are complete.
- [ ] Every key invariant in the design has deterministic tests.
- [ ] Actual fills are authoritative throughout persistence and projections.
- [ ] Roll economics cannot be double-counted.
- [ ] Current positions consumed by Phases 5/6 reflect effective lifecycle history.
- [ ] Historical analytical artifacts and superseded operational history remain auditable.
- [ ] No automatic trading, Phase 8+, or unrelated functionality is included.

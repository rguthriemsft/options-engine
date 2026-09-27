# Post-Phase-6 Roadmap

## Authority and Baseline

This document is the authoritative phase sequence after the merged Phase 6 baseline until a later approved design supersedes it. `SPECIFICATION.md` remains authoritative for product and strategy behavior. Historical Phase 3–6 design and acceptance documents remain authoritative records of those phases and are not retroactively reinterpreted by this roadmap.

Phases 1–6 are complete and merged into `main`. The system currently provides provider-independent market data, indicators, immutable entry and position-sizing evaluations, and immutable defense and roll evaluations.

The governing objective is:

1. Avoid assignment and preserve the underlying shares.
2. Subject to that assignment protection, maximize cumulative net covered-call profit.

Assignment protection wins any conflict. This is a priority ordering, not a promise that assignment can never occur. Net campaign profit is the sum of STO premium credits minus the sum of BTC costs, fees, and other explicitly approved strategy costs. It is not gross premium or an after-tax-dollar objective.

A separate measurement objective is to determine whether the covered-call strategy adds economic value relative to simply holding the underlying shares. That measurement belongs to Phase 11 and must use authoritative actual lifecycle data.

Phase 6 roll economics intentionally remain local to one candidate: replacement Bid minus existing-call Ask. Phase 6 has no cumulative campaign economics. A roll remains linked BTC and replacement STO transaction legs; its net credit/debit is derived from those legs and is never counted again. No campaign-relative roll rule is approved here.

## Current Operational Gap

The database contains a narrow mutable `OpenShortCallPosition` state used by Phase 5 and Phase 6, but there is no public application workflow to:

- record STO or BTC executions;
- record a roll as linked BTC and STO legs;
- record expiration or assignment;
- create, update, or close the current open short-call position;
- maintain a campaign from actual fills, fees, and timestamps.

Without that lifecycle, the existing strategy engines cannot form a complete daily operating loop. Closing this gap is therefore the highest-priority next phase.

## Phase 7 — Operational Trade and Position Lifecycle

Phase 7 shall design and implement:

- an immutable transaction ledger;
- manual recording of `STO`, `BTC`, `ROLL`, `EXPIRE`, and `ASSIGN` outcomes, with a roll represented by linked BTC and STO transaction legs;
- creation, update, and closure of current open short-call positions;
- campaign identity, core state, and lifecycle transitions;
- actual fill price, fees, and execution time;
- links from executions to the analytical artifacts that informed them;
- validation, idempotency, audit behavior, APIs, migrations, and tests needed for a reliable manual workflow.

Phase 7 does not add brokerage synchronization or automatic execution. Detailed lifecycle invariants, command contracts, and accounting treatment require a dedicated Phase 7 design and acceptance packet before implementation.

## Phase 8 — Assignment Protection Hardening

Phase 8 shall add:

- an authoritative, provider-independent dividend and ex-dividend data source;
- reproducible persistence of the event data consumed;
- a specifically approved early-assignment-risk calculation and trigger or rule;
- integration of that risk into current-position defense analysis and explanations;
- explicit missing, stale, unavailable, and not-applicable behavior.

No early-assignment formula, threshold, or override is approved by this roadmap. Technical-breakout and expected-move defense rules do not become automatic Phase 8 scope.

## Phase 9 — Daily Decision API

Phase 9 shall provide a spreadsheet-friendly application use case that composes:

- portfolio refresh status;
- holdings and current open calls;
- entry, position-sizing, defense, and roll artifacts;
- warnings and explicit missing-data states;
- an action summary.

The composed read model is named `CoveredCallDecision` for planning purposes. It references canonical `EntryStrategyEvaluation`, `PositionSizingEvaluation`, current `OpenShortCallPosition`, `DefenseEvaluation`, and `RollEvaluation` artifacts. It shall not duplicate their quantitative formulas or persistence payloads.

Planned action vocabulary:

```text
NO_TRADE
SELL
NO_ACTION
MONITOR
PROFIT_CLOSE
DEFENSE_REVIEW
ROLL
CLOSE_WAIT
```

Exact route paths and DTO shapes are provisional until the Phase 9 design is approved.

## Phase 10 — Excel Decision Dashboard MVP

Phase 10 shall deliver an Excel presentation and manual-input client with these MVP areas:

- Dashboard;
- Holdings;
- Opportunities;
- Current Positions;
- Roll Analyzer;
- Trade Entry.

C# remains authoritative for strategy calculations. Excel may provide presentation, filtering, approved configuration input, manual transaction entry, and audit navigation, but it shall not reimplement quantitative engines in workbook formulas.

The operational spreadsheet/API MVP is complete after Phase 10 when the user can:

1. Open the workbook.
2. Refresh current portfolio and market decision data.
3. See every enabled holding.
4. For uncovered shares, see whether a call should be sold and the selected contract, strike, expiration, Delta, premium/reference economics, Contract Score, recommended contracts, and explanation.
5. For open calls, see opening premium, current BTC reference, captured premium percentage, DRS, DRS classification, hard triggers, and disposition.
6. When rolling, see the preferred replacement, strike, expiration, Delta, net debit/credit, projected DRS, and RQS.
7. See missing-data and warning states.
8. Execute manually at the broker.
9. Record the actual fill.
10. Have the system update campaign and current-position state.
11. Have the next refresh use the new state.

Performance reporting is not required for this MVP. Strike laddering is not an MVP requirement. “Alerts” are visible attention states on refresh, not scheduled or pushed notifications.

## Phase 11 — Profit and Performance Measurement

Phase 11 shall use the Phase 7 ledger and campaign lifecycle to calculate and report:

- campaign P&L;
- fees and approved strategy costs;
- net option profit and covered-call income;
- cumulative roll economics;
- assignment, expiration, and close rates;
- portfolio and campaign performance;
- buy-and-hold comparisons.

Performance calculations must use authoritative actual transactions and explicitly defined valuation inputs. They must distinguish gross premium from net profit and must not claim after-tax returns without an approved tax-dollar model.

## Phase 12 — Research and Optimization

Phase 12 may add research datasets, counterfactual analysis, factor and bucket studies, backtesting, walk-forward analysis, and parameter optimization. Research must preserve point-in-time inputs and version identities and must not silently alter production strategy rules.

## Dependencies

```text
Phases 1–6 analytical baseline
            |
            v
Phase 7 operational lifecycle
            |
            +-----------> Phase 8 assignment protection
                              |
                              v
                    Phase 9 daily decision API
                              |
                              v
                    Phase 10 Excel MVP
                              |
                              v
                    Phase 11 performance
                              |
                              v
                    Phase 12 research
```

Phase 11 fundamentally depends on the Phase 7 ledger. Phase 9 must compose the Phase 7 current state and Phase 8 assignment-protection result. Phase 10 consumes Phase 9 rather than bypassing it.

## Persistence and Model Decisions

- Existing immutable entry, sizing, defense, and roll evaluations remain canonical analytical artifacts.
- `CoveredCallDecision` is a composed read model, not a replacement quantitative engine.
- `DefenseEvaluation` history is the primary analytical monitoring history for the MVP.
- A separate `DailyPositionSnapshot` table is not required unless a future design identifies a unique non-decision time-series need that cannot be met by market observations, position state, and defense history.
- Historical recommendation terminology may remain in older phase documents, but new work should use explicit artifact names and the composed decision model.

## Explicitly Deferred

- automatic trade execution, order management, or brokerage synchronization;
- scheduled monitoring and push/email notifications;
- tax-dollar or after-tax optimization;
- strike laddering and multiple-candidate initial allocation;
- technical-breakout and expected-move defense rules;
- final Phase 9 endpoint paths and DTO shapes;
- performance views in the Phase 10 spreadsheet MVP;
- a redundant general-purpose Recommendation persistence model;
- a redundant MVP `DailyPositionSnapshot` store;
- Phase 12 research features before authoritative operational and performance data exist.

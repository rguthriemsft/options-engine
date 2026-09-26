# Covered-Call Decision Workflow

## Purpose and Safety Boundary

Options Engine is a decision-support system for manually managed covered calls. It does not place, modify, or cancel brokerage orders and does not guarantee that assignment will never occur.

Its governing objective is lexicographic:

1. Avoid assignment and preserve the underlying shares.
2. Subject to that assignment protection, maximize cumulative net covered-call profit.

When those goals conflict, assignment protection wins. `AssignmentSensitivity` and `TaxSensitivity` describe holding context; they are not an after-tax optimization model or a calculation of tax dollars.

Profit means net campaign economics: STO premium credits, BTC costs, roll credits or debits, fees, and any other strategy cost explicitly approved in the specification. A large gross premium is not, by itself, success.

## What Works Today

The merged Phase 1–6 system can:

- retrieve and persist normalized Tradier market data;
- calculate and persist technical, volatility, and regime indicators;
- calculate immutable entry evaluations with CCOS, gates, contract scores, and ranking;
- calculate immutable position-sizing evaluations;
- evaluate a persisted current short call for profit taking, DRS, hard-defense triggers, and defensive roll candidates;
- persist immutable defense and roll evaluation history.

The application cannot yet use its public API to record an STO, BTC, roll, expiration, or assignment, or to create, update, and close the current `OpenShortCallPosition` that defense analysis consumes. That is the first operational gap addressed by Phase 7.

## Intended Daily Workflow

1. Start the local API and, after Phase 10, open the workbook; confirm database health.
2. Review configured accounts, holdings, shares, and holding sensitivity settings.
3. Refresh market data and inspect explicit stale or missing-data states.
4. Review technical, volatility, market-regime, and sector-regime context.
5. For uncovered capacity, review the immutable entry evaluation: CCOS, hard gates, contract candidates, scores, ranking, and explanations.
6. Review the immutable position-sizing evaluation and its capacity, concentration, exposure, and DER limits.
7. Review the composed daily decision. It references the canonical entry and sizing artifacts rather than recalculating their formulas.
8. For every open short call, review its latest defense evaluation, profit-taking state, DRS, hard triggers, and warnings.
9. When roll analysis is required, review the immutable roll evaluation and candidate economics. Phase 6 economics are local to the proposed roll and use existing-call Ask and replacement-call Bid; they do not yet represent cumulative campaign profit.
10. Verify the live brokerage quote and place any chosen order manually at Fidelity or Schwab.
11. Record the actual fill, fees, and execution time so the transaction ledger, open position, and campaign remain authoritative.

Steps 7, 10, and 11 describe the intended post-Phase-6 workflow. Phase 7 provides transaction and position lifecycle recording; Phase 9 provides the composed daily decision API; Phase 10 provides the spreadsheet workflow.

## Decision and Attention States

The future composed `CoveredCallDecision` may summarize these actions:

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

It is a read model that references canonical immutable evaluations and current position state. It is not another scoring engine and does not duplicate their persistence payloads.

For the spreadsheet MVP, an alert means a visible attention state produced during a manual or daily refresh. Scheduled jobs, push notifications, email, and brokerage automation are not required.

## Known Safety Gaps

Phase 6 intentionally omitted an authoritative dividend/ex-dividend source and a specific early-assignment-risk rule. Phase 8 must add both and integrate the resulting risk state into defense analysis. Until then, Phase 6 DRS and hard triggers are not a complete early-assignment model.

Technical-breakout and expected-move defense rules remain deferred until separately specified. They must not be inferred from the requirement to harden early-assignment protection.

## User Responsibility

Always verify current prices, contract details, corporate events, and brokerage order terms before acting. The system provides analytical support, not investment, brokerage, legal, or tax advice. The user remains responsible for every trade and its consequences.

# Options Engine — Agent Instructions

## 1. Purpose

This repository implements a quantitative covered-call decision-support system.

The system is designed to:

1. Identify favorable covered-call opportunities.
2. Rank option contracts.
3. Determine appropriate position sizes.
4. Monitor open covered-call positions.
5. Detect increasing assignment risk.
6. Recommend defensive actions and rolls.
7. Track complete covered-call campaigns.
8. Measure strategy performance against buy-and-hold.

The system provides decision support only.

Automatic trade execution is explicitly outside the V1 scope.

---

# 2. Authoritative Specification

`SPECIFICATION.md` is the authoritative product and strategy specification.

Before implementing any feature that affects:

- domain behavior;
- strategy calculations;
- scoring;
- market data;
- persistence;
- recommendations;
- position management;
- roll logic;
- performance measurement;

read the relevant sections of `SPECIFICATION.md`.

If this file conflicts with `SPECIFICATION.md` regarding product behavior or strategy rules:

> `SPECIFICATION.md` wins.

Do not silently invent missing strategy requirements.

If implementation requires an assumption not specified in `SPECIFICATION.md`:

1. Prefer the simplest implementation that preserves future flexibility.
2. Document the assumption.
3. Do not introduce new trading rules, thresholds, weights, or financial behavior without explicit specification.

---

# 3. Architecture

The intended architecture is:

```text
External Market Data Provider
          |
          v
Provider Adapter
          |
          v
Normalized Domain Models
          |
          +------------------+
          |                  |
          v                  v
     Persistence         Indicators
          |                  |
          +--------+---------+
                   |
                   v
             Strategy Engines
                   |
                   v
             Application Layer
                   |
                   v
                API
                   |
                   v
           Excel / Future UI
```

Tradier is the initial market-data provider.

Tradier is an implementation detail.

The core application must remain provider-independent.

---

# 4. Solution Structure

Use `OptionsEngine` as the root namespace and project prefix.

Expected structure:

```text
/src
    OptionsEngine.Domain
    OptionsEngine.Strategy
    OptionsEngine.MarketData
    OptionsEngine.Infrastructure
    OptionsEngine.Application
    OptionsEngine.Api

/tests
    OptionsEngine.Domain.Tests
    OptionsEngine.Strategy.Tests
    OptionsEngine.MarketData.Tests
    OptionsEngine.Infrastructure.Tests
    OptionsEngine.Api.Tests

/docs
```

Additional projects require a clear architectural reason.

Do not create projects merely to organize a small number of classes.

---

# 5. Dependency Rules

Dependencies must flow toward the domain and strategy core.

## OptionsEngine.Domain

Contains:

- core entities;
- value objects;
- enums;
- domain concepts;
- provider-independent domain interfaces where appropriate.

Must not reference:

- EF Core;
- SQLite;
- ASP.NET Core;
- Tradier;
- HTTP clients;
- Excel;
- provider DTOs.

Domain must remain infrastructure-independent.

## OptionsEngine.Strategy

Contains pure strategy and quantitative calculations, including eventually:

- CCOS;
- Contract Score;
- Position Sizing;
- DRS;
- RQS;
- Roll Engine;
- strategy hard gates.

Strategy may depend on Domain.

Strategy must not depend on:

- EF Core;
- SQLite;
- ASP.NET Core;
- Tradier;
- HTTP;
- Excel.

Strategy calculations should be deterministic for a fixed input and configuration.

## OptionsEngine.MarketData

Contains:

- market-data abstractions;
- provider contracts;
- normalized market-data interfaces;
- Tradier integration when implemented;
- provider DTO mapping.

Provider-specific DTOs must not escape into Domain or Strategy.

## OptionsEngine.Infrastructure

Contains infrastructure concerns such as:

- EF Core;
- SQLite;
- repositories;
- persistence implementations;
- external storage;
- caching implementations.

Infrastructure may implement interfaces defined in inner layers.

## OptionsEngine.Application

Coordinates use cases.

Examples:

- retrieve market data;
- persist snapshots;
- calculate indicators;
- run strategy engines;
- generate recommendations;
- monitor positions;
- analyze rolls.

Application should orchestrate domain behavior rather than contain quantitative formulas.

## OptionsEngine.Api

Contains:

- ASP.NET Core startup;
- dependency registration;
- endpoints;
- request/response DTOs;
- serialization;
- HTTP-specific behavior.

API endpoints should be thin.

Business and strategy logic does not belong in endpoint handlers.

---

# 6. Provider Independence

Never expose Tradier-specific DTOs outside the market-data/provider boundary.

Bad:

```text
TradierOption -> CCOS calculator
```

Good:

```text
TradierOption
      |
      v
OptionContractSnapshot
      |
      v
Strategy Engine
```

Strategy code must not know which provider supplied the data.

Future providers should be addable without changing scoring engines.

---

# 7. Numeric Conventions

Use:

```text
decimal
```

for:

- money;
- prices;
- premiums;
- strikes;
- fees;
- cost basis;
- realized/unrealized monetary P&L.

Use:

```text
double
```

for statistical and quantitative calculations where floating-point mathematics is appropriate, including:

- RSI;
- volatility;
- standard deviation;
- Bollinger calculations;
- Greeks;
- correlations;
- technical indicators.

Percentages and ratios should use the type appropriate to their calculation domain and remain consistent within that domain.

Do not compare floating-point values using exact equality where tolerance-based comparison is appropriate.

---

# 8. Time Conventions

Persist timestamps in UTC unless there is a documented reason not to.

Prefer:

```text
DateTimeOffset
```

for timestamps.

Market-calendar concepts such as:

- expiration dates;
- earnings dates;
- ex-dividend dates;

should be represented as date-only values when time-of-day is irrelevant.

Do not assume the local machine timezone represents the market timezone.

---

# 9. Identifiers

Persistent entities should use stable identifiers.

Prefer strongly meaningful identifier properties such as:

```text
HoldingId
CampaignId
RecommendationId
TransactionId
```

Do not use ticker symbol as the primary identity of a holding.

The same security may exist:

- in multiple accounts;
- with different tax sensitivity;
- with different assignment sensitivity;
- with different strategy configuration.

---

# 10. Persistence Rules

SQLite is the V1 persistence technology.

EF Core is the V1 ORM.

Persistence models must preserve sufficient information to reproduce historical recommendations.

Never overwrite historical observations merely because newer market data exists.

Market observations should generally be timestamped.

Transactions are immutable ledger records.

A roll consists of:

```text
BTC existing option
+
STO replacement option
```

linked to the same campaign.

Do not mutate the original transaction to represent a roll.

---

# 11. Strategy Reproducibility

Every recommendation must eventually be reproducible from:

- stored market inputs;
- stored calculated indicators;
- strategy version;
- configuration version;
- timestamp.

Never design persistence that stores only the final score.

For example, storing:

```text
CCOS = 84
```

without its inputs and component scores is insufficient.

Historical recommendations must not silently change when current configuration changes.

---

# 12. Strategy Versioning

Strategy behavior is versioned independently from configuration.

Examples:

```text
StrategyVersion = 1.0.0
ConfigurationVersion = 1
```

Changing a threshold or weight through configuration does not necessarily require rewriting historical records.

Changing algorithmic behavior requires consideration of a strategy-version change.

Never silently reinterpret historical recommendations using newer strategy logic.

---

# 13. Explainability

Scores must not be opaque.

Strategy result objects should expose component-level information.

Prefer designs similar to:

```csharp
public sealed record ScoreComponent(
    string Name,
    double RawValue,
    double Score,
    double MaximumScore,
    string Explanation);
```

A strategy result should eventually communicate:

```text
overall score
classification
eligibility
hard-gate failures
component scores
explanation
```

Avoid APIs that return only:

```csharp
int CalculateScore(...);
```

---

# 14. Hard Gates

Hard strategy constraints are different from weighted scores.

If `SPECIFICATION.md` defines something as a hard gate, implement it as an explicit gate.

Do not convert a hard gate into a score penalty.

Result models should make gate failures observable.

Prefer machine-readable codes such as:

```text
EARNINGS_BEFORE_EXPIRATION
DELTA_EXCEEDS_MAXIMUM
DTE_OUTSIDE_RANGE
STRIKE_NOT_OTM
INSUFFICIENT_LIQUIDITY
BREAKOUT_VETO
MAXIMUM_COVERAGE_EXCEEDED
INSUFFICIENT_DATA
```

Human-readable explanations may accompany these codes.

---

# 15. Missing Data

Missing financial data is not zero.

Never silently convert:

```text
missing delta -> 0
missing IV -> 0
missing volume -> 0
missing earnings date -> no earnings
```

When required data is unavailable, represent the absence explicitly.

If the missing value prevents a safe strategy decision, the resulting recommendation should become:

```text
INSUFFICIENT_DATA
```

or an equivalent explicit state.

---

# 16. Configuration

Important strategy constants must not be buried inside formulas.

Configuration shall eventually include:

- CCOS weights;
- CCOS thresholds;
- Contract Score thresholds;
- maximum delta;
- preferred delta;
- DTE ranges;
- DRS thresholds;
- profit targets;
- premium-stop multiples;
- sizing curves;
- ASL multipliers;
- concentration modifiers;
- DER limits;
- roll constraints.

Defaults must correspond to `SPECIFICATION.md`.

Configuration should be strongly typed and validated at startup or load time.

Invalid configuration should fail clearly.

---

# 17. Error Handling

Distinguish expected operational failures.

Examples:

```text
ProviderUnavailable
AuthenticationFailure
RateLimited
InvalidSymbol
MissingMarketData
StaleMarketData
PersistenceFailure
ConfigurationFailure
StrategyCalculationFailure
```

Do not swallow exceptions.

Do not expose secrets in exception messages or logs.

External-provider errors should be translated into application-level errors at the provider boundary.

---

# 18. Logging

Use structured logging.

Include useful dimensions such as:

```text
Symbol
Provider
Operation
Duration
Result
CorrelationId
```

Never log:

- API tokens;
- passwords;
- complete authorization headers;
- private credentials.

Strategy decisions should eventually produce auditable decision logs.

---

# 19. Security

Never commit credentials.

Tradier credentials must be loaded from secure configuration.

For local development, .NET User Secrets are acceptable.

The Excel workbook must never contain provider credentials.

The local API should bind to localhost by default.

Do not add automatic brokerage execution without an explicit specification change.

---

# 20. Testing Requirements

Tests are part of the implementation, not optional cleanup.

New functionality must include appropriate tests.

Use xUnit.

Tests should be:

- deterministic;
- isolated;
- readable;
- fast where possible.

Strategy tests must not require:

- internet connectivity;
- Tradier;
- a live brokerage account;
- the current date;
- the current market price.

Inject or explicitly supply time where current time affects behavior.

---

# 21. Strategy Boundary Tests

Every threshold must eventually have tests immediately below, at, and immediately above the boundary.

Example:

```text
Maximum Delta = .25

.2499 -> eligible
.2500 -> eligible
.2501 -> rejected
```

Follow the exact inclusive/exclusive semantics specified by the strategy.

Hard gates require dedicated tests.

---

# 22. Golden Scenarios

Maintain canonical strategy scenarios as regression tests.

At minimum, the eventual suite should contain:

```text
StrongEntry
BreakoutVeto
InsufficientData
HighAssignmentRisk
ProfitClose
DefensiveRoll
TaxSensitivePosition
IlliquidContract
EarningsBeforeExpiration
```

Golden tests should verify both numerical results and important classifications/gate outcomes.

---

# 23. External API Testing

Do not require live Tradier access during normal unit tests or CI.

Provider integration should use:

- mocked HTTP handlers;
- captured sanitized JSON fixtures;
- deterministic provider responses.

A small number of explicitly identified integration tests may use a live sandbox/developer environment when appropriate.

Live tests must not run automatically in normal CI.

---

# 24. Test Fixtures

Provider JSON fixtures belong in test resources.

Never include:

- access tokens;
- account numbers;
- personal financial information;
- authorization headers.

Captured market-data fixtures should identify:

- provider;
- endpoint type;
- approximate capture date where useful.

---

# 25. Code Quality

Prefer:

- small focused classes;
- immutable records for calculation inputs/results where practical;
- dependency injection;
- async I/O;
- cancellation tokens for external operations;
- explicit domain terminology;
- nullable reference types;
- compiler warnings treated seriously.

Avoid:

- giant service classes;
- static global state;
- hidden mutable state;
- service locator patterns;
- strategy calculations inside controllers/endpoints;
- strategy calculations inside EF entities;
- unnecessary reflection;
- premature abstraction.

---

# 26. Async Rules

External I/O should be asynchronous.

Methods performing I/O should normally accept:

```csharp
CancellationToken cancellationToken
```

Do not use `.Result`, `.Wait()`, or other sync-over-async patterns in application code.

Pure strategy calculations should remain synchronous unless there is a concrete reason otherwise.

---

# 27. API Design

API endpoints should expose application use cases rather than database tables.

Prefer:

```text
GET /api/opportunities/MSFT
```

over generic persistence-oriented APIs.

API DTOs are contracts and should not expose EF entities directly.

Use appropriate HTTP status codes.

Validation failures should return structured errors.

---

# 28. Database Migrations

All schema changes must use EF Core migrations.

Do not manually edit production database schemas.

A PR containing persistence changes should include the corresponding migration.

Migrations should be reviewable and narrowly scoped.

---

# 29. Documentation

Update documentation when architecture or developer workflow changes.

At minimum:

`README.md` should explain:

- project purpose;
- prerequisites;
- build;
- test;
- run;
- database setup;
- migrations;
- secret configuration.

`SPECIFICATION.md` explains what the product should do.

`AGENTS.md` explains how agents should work in the repository.

Avoid duplicating the entire specification across multiple documents.

---

# 30. Scope Discipline

Implement the requested milestone only.

Do not opportunistically implement later phases.

Examples:

If implementing repository foundation:

Do not also implement CCOS.

If implementing Tradier quote retrieval:

Do not also build the Roll Engine.

If implementing RSI:

Do not introduce machine-learning infrastructure.

Keep PRs small enough to review confidently.

---

# 31. No Automatic Trading

V1 is decision support.

Do not:

- submit orders;
- cancel orders;
- modify brokerage positions;
- automatically roll contracts;
- connect a recommendation directly to brokerage execution.

Any future trade-execution capability requires an explicit architectural and product decision.

---

# 32. Financial Calculation Discipline

Do not invent formulas.

When implementing a formula:

1. Locate it in `SPECIFICATION.md`.
2. Encode it explicitly.
3. Add tests around its boundaries.
4. Preserve raw inputs.
5. Expose component calculations where appropriate.

If a formula is underspecified, do not guess silently.

Document the ambiguity and request clarification or isolate the assumption so it can easily be changed.

---

# 33. Before Starting a Task

Before modifying code:

1. Read this file.
2. Read the relevant portions of `SPECIFICATION.md`.
3. Inspect existing implementation and tests.
4. Identify the intended architectural layer.
5. Identify existing conventions.
6. Define the smallest coherent change.

Do not assume the repository still matches an earlier prompt.

The repository contents are authoritative for current implementation state.

---

# 34. Before Completing a Task

Before declaring work complete:

1. Build the entire solution.
2. Run the entire automated test suite.
3. Review compiler warnings.
4. Verify no credentials were introduced.
5. Verify architectural dependency rules.
6. Verify relevant acceptance criteria.
7. Update documentation where necessary.
8. Summarize assumptions and remaining work.

Required commands should normally include:

```bash
dotnet restore
dotnet build --no-restore
dotnet test --no-build
```

If formatting tooling is configured, run it.

Do not claim tests passed unless they were actually executed successfully.

---

# 35. Pull Request Expectations

Each implementation task should be suitable for a focused pull request.

PR descriptions should contain:

```text
## Summary

## Specification Sections

## Implementation

## Tests

## Database Changes

## Assumptions

## Out of Scope
```

Reference relevant `SPECIFICATION.md` sections.

A reviewer should be able to determine why each significant change exists.

---

# 36. Definition of Done

A feature is complete when:

- behavior matches `SPECIFICATION.md`;
- architecture rules are respected;
- appropriate automated tests exist;
- solution builds;
- tests pass;
- persistence changes include migrations;
- errors are handled explicitly;
- missing data is handled safely;
- documentation is updated where required;
- no secrets are committed;
- no unrelated features are included.

Passing compilation alone is not Definition of Done.
# Phase 1 — Repository/Foundation Acceptance Checklist

## Objective

Establish a clean, tested .NET foundation for the Options Engine without implementing market-data integration or covered-call strategy logic.

Phase 1 corresponds to the Repository/Foundation milestone in `SPECIFICATION.md`.

---

# 1. Repository Structure

- [ ] `SPECIFICATION.md` exists at repository root.
- [ ] `AGENTS.md` exists at repository root.
- [ ] `README.md` exists at repository root.
- [ ] Solution uses `OptionsEngine` as the project/namespace prefix.
- [ ] Source projects are located under `/src`.
- [ ] Test projects are located under `/tests`.
- [ ] Documentation is located under `/docs`.
- [ ] Repository contains an appropriate .NET `.gitignore`.
- [ ] No generated build output is committed.
- [ ] No SQLite runtime database is committed.
- [ ] No secrets are committed.

Expected source projects:

- [ ] `OptionsEngine.Domain`
- [ ] `OptionsEngine.Strategy`
- [ ] `OptionsEngine.MarketData`
- [ ] `OptionsEngine.Infrastructure`
- [ ] `OptionsEngine.Application`
- [ ] `OptionsEngine.Api`

Expected test projects:

- [ ] `OptionsEngine.Domain.Tests`
- [ ] `OptionsEngine.Strategy.Tests`
- [ ] `OptionsEngine.MarketData.Tests`
- [ ] `OptionsEngine.Infrastructure.Tests`
- [ ] `OptionsEngine.Api.Tests`

---

# 2. Build Configuration

- [ ] Target framework is .NET 10.
- [ ] Nullable reference types are enabled.
- [ ] Implicit usings policy is defined consistently.
- [ ] Common build configuration is centralized where appropriate.
- [ ] `Directory.Build.props` exists if common project settings are required.
- [ ] Debug build succeeds.
- [ ] Release build succeeds.
- [ ] Build produces no unexpected warnings.

Verification:

```bash
dotnet restore
dotnet build --configuration Debug
dotnet build --configuration Release
```

---

# 3. Dependency Architecture

Verify project references follow intended dependency direction.

- [ ] `OptionsEngine.Domain` has no project dependencies.
- [ ] Domain does not reference EF Core.
- [ ] Domain does not reference ASP.NET Core.
- [ ] Domain does not reference SQLite.
- [ ] Domain does not reference Tradier-specific packages/types.
- [ ] `OptionsEngine.Strategy` may reference Domain.
- [ ] Strategy does not reference Infrastructure.
- [ ] Strategy does not reference Api.
- [ ] Strategy does not reference Tradier-specific code.
- [ ] Infrastructure may reference Domain/Application contracts as required.
- [ ] Api acts as the composition root.
- [ ] No circular project references exist.

---

# 4. Initial Domain Model

Implement the initial entities required by Phase 1.

## Account

- [ ] Stable `AccountId`.
- [ ] Name.
- [ ] Broker.
- [ ] Account type.
- [ ] Tax-deferred indicator where appropriate.
- [ ] Enabled state.

Broker representation supports at minimum:

- [ ] Fidelity.
- [ ] Schwab.
- [ ] Other.

Account type supports at minimum:

- [ ] Taxable.
- [ ] Traditional IRA.
- [ ] Roth IRA.
- [ ] Other.

## Holding

- [ ] Stable `HoldingId`.
- [ ] `AccountId`.
- [ ] Symbol.
- [ ] Asset type.
- [ ] Shares.
- [ ] Assignment sensitivity.
- [ ] Tax sensitivity.
- [ ] Maximum coverage percentage.
- [ ] Maximum initial delta.
- [ ] Preferred delta minimum.
- [ ] Preferred delta maximum.
- [ ] Minimum CCOS.
- [ ] Minimum Contract Score.
- [ ] Minimum premium.
- [ ] Minimum annualized yield.
- [ ] Maximum Delta Exposure Ratio.
- [ ] Enabled state.

## Tax Lot

- [ ] Stable `TaxLotId`.
- [ ] `HoldingId`.
- [ ] Acquisition date.
- [ ] Shares.
- [ ] Cost basis per share.
- [ ] Total cost basis.
- [ ] Holding-period classification.

---

# 5. Domain Modeling Quality

- [ ] Ticker symbol is not used as the primary identity of a holding.
- [ ] The same symbol can exist in multiple accounts.
- [ ] Account/Holding/TaxLot relationships are explicit.
- [ ] Monetary fields use `decimal`.
- [ ] Date-only concepts use an appropriate date representation.
- [ ] Domain entities contain no EF-specific persistence behavior unless explicitly justified.
- [ ] Domain contains no Tradier-specific types.
- [ ] Domain contains no HTTP-specific types.

---

# 6. Persistence

- [ ] EF Core is configured.
- [ ] SQLite provider is configured.
- [ ] `OptionsEngineDbContext` exists.
- [ ] Account is mapped.
- [ ] Holding is mapped.
- [ ] TaxLot is mapped.
- [ ] Relationships and foreign keys are configured.
- [ ] Appropriate required fields are enforced.
- [ ] Initial EF Core migration exists.
- [ ] Database can be created from migrations.
- [ ] Database can be recreated from scratch.
- [ ] Runtime database location is configurable.
- [ ] Runtime SQLite files are ignored by Git.

Verification should include creating a new database from zero.

---

# 7. Persistence Integrity

Automated tests verify at minimum:

- [ ] Account can be persisted and retrieved.
- [ ] Holding can be persisted and retrieved.
- [ ] Tax lot can be persisted and retrieved.
- [ ] Holding correctly references Account.
- [ ] TaxLot correctly references Holding.
- [ ] Multiple holdings of the same symbol can exist in different accounts.
- [ ] Multiple tax lots can exist for one holding.
- [ ] Required relationships are enforced.
- [ ] Deleting or modifying related records follows an explicitly chosen policy rather than accidental EF defaults.

---

# 8. Application Startup

- [ ] ASP.NET Core application starts successfully.
- [ ] Dependency injection is configured.
- [ ] `OptionsEngineDbContext` resolves through DI.
- [ ] Configuration is loaded through standard .NET configuration.
- [ ] Environment-specific configuration is supported.
- [ ] Startup fails clearly for invalid critical configuration.

---

# 9. Health Endpoint

Implement:

```text
GET /health
```

Acceptance criteria:

- [ ] Endpoint exists.
- [ ] Returns HTTP 200 when application and database are healthy.
- [ ] Database connectivity is checked.
- [ ] Response is machine-readable.
- [ ] Health endpoint does not expose secrets.
- [ ] API integration test covers successful health response.

Example conceptual response:

```json
{
  "status": "Healthy"
}
```

Exact response format may follow standard ASP.NET Core health-check conventions.

---

# 10. Configuration and Secrets

- [ ] No Tradier credentials are required for Phase 1.
- [ ] No placeholder real credentials are committed.
- [ ] `appsettings.json` contains only safe configuration.
- [ ] Development secrets can use .NET User Secrets.
- [ ] README explains how future secrets should be configured.
- [ ] SQLite connection configuration does not require source changes.

---

# 11. Logging

- [ ] ASP.NET Core logging is configured.
- [ ] Startup/shutdown is observable.
- [ ] Health/database failures are logged appropriately.
- [ ] Structured logging conventions are established.
- [ ] No sensitive values are logged.

A third-party logging framework is not required unless justified.

---

# 12. Testing Infrastructure

- [ ] xUnit is configured.
- [ ] All expected test projects compile.
- [ ] Tests can run from repository root.
- [ ] Tests do not require internet access.
- [ ] Tests do not require Tradier credentials.
- [ ] Tests do not require Fidelity credentials.
- [ ] Tests do not require Schwab credentials.
- [ ] Tests do not depend on an existing developer database.
- [ ] Infrastructure tests use isolated databases.
- [ ] API tests start the application in a test environment.

Verification:

```bash
dotnet test
```

must succeed.

---

# 13. Code Quality

- [ ] Nullable warnings have been addressed.
- [ ] No sync-over-async patterns are introduced.
- [ ] No unnecessary global/static mutable state.
- [ ] API code contains no strategy calculations.
- [ ] Domain code contains no persistence implementation.
- [ ] Naming uses financial/domain terminology from `SPECIFICATION.md`.
- [ ] Public APIs are reasonably documented where intent is not obvious.
- [ ] No premature generic repository/framework abstraction is introduced without a concrete need.

---

# 14. README

`README.md` includes:

- [ ] Project purpose.
- [ ] V1 scope summary.
- [ ] Link/reference to `SPECIFICATION.md`.
- [ ] Architecture overview.
- [ ] Required .NET SDK.
- [ ] Clone/setup instructions.
- [ ] Restore instructions.
- [ ] Build instructions.
- [ ] Test instructions.
- [ ] Run instructions.
- [ ] SQLite database location/configuration.
- [ ] EF migration commands.
- [ ] Secret-management guidance.
- [ ] Explicit statement that V1 does not automatically execute trades.

---

# 15. Documentation

- [ ] `AGENTS.md` reflects repository architecture.
- [ ] `SPECIFICATION.md` remains the product/strategy source of truth.
- [ ] No strategy thresholds have been duplicated unnecessarily into implementation documentation.
- [ ] Any Phase 1 architectural assumptions are documented.
- [ ] Any deviation from the specification is explicitly identified.

---

# 16. Scope Validation

Phase 1 must NOT implement:

- [ ] Tradier API integration.
- [ ] Live market quotes.
- [ ] Historical market-data retrieval.
- [ ] Option-chain retrieval.
- [ ] Technical indicators.
- [ ] RSI.
- [ ] Bollinger Bands.
- [ ] MACD.
- [ ] ATR.
- [ ] CCOS.
- [ ] Contract Score.
- [ ] Position Sizing Engine.
- [ ] DRS.
- [ ] Roll Engine.
- [ ] RQS.
- [ ] Excel integration.
- [ ] Brokerage synchronization.
- [ ] Automatic trade execution.

Scaffolding interfaces/projects for future phases is acceptable.

Implementing future behavior is not.

---

# 17. Clean Repository Verification

From a fresh clone, a developer must be able to:

```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project src/OptionsEngine.Api
```

and successfully call:

```text
GET /health
```

without modifying source code.

- [ ] Verified from a clean working tree or equivalent clean environment.
- [ ] No undocumented local files are required.
- [ ] No developer-specific absolute paths exist.
- [ ] No private credentials are required.

---

# 18. Phase 1 Exit Criteria

Phase 1 is complete only when all of the following are true:

- [ ] Entire solution builds.
- [ ] Entire test suite passes.
- [ ] API starts.
- [ ] Health endpoint succeeds.
- [ ] SQLite database can be created from migrations.
- [ ] Account/Holding/TaxLot persistence works.
- [ ] Architecture dependency rules are respected.
- [ ] README allows another developer to run the project.
- [ ] No credentials are committed.
- [ ] No Phase 2+ functionality has leaked into the implementation.
- [ ] Pull request has been reviewed against this checklist.

---

# 19. Required PR Evidence

The Phase 1 PR description should include:

## Build

```text
dotnet build
Result: PASS
```

## Tests

```text
dotnet test
Result: PASS
Tests: <actual count>
Failures: 0
```

## Database

```text
Initial migration:
<actual migration name>

Clean database creation:
PASS
```

## API

```text
GET /health
HTTP 200
```

## Scope

```text
Tradier integration: Not implemented
Strategy engines: Not implemented
Excel integration: Not implemented
Automatic trading: Not implemented
```

Do not populate these fields with expected results. Record the actual results produced by the implementation.
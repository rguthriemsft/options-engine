# Options Engine

Options Engine is a .NET 10 covered-call decision-support system for analyzing holdings, selecting and sizing covered calls, and monitoring open calls for defensive action. The product and strategy source of truth is [SPECIFICATION.md](SPECIFICATION.md).

Its governing objective is to avoid assignment and preserve the underlying shares and, subject to that assignment protection, maximize cumulative net covered-call profit. Assignment protection wins a conflict; the system cannot guarantee assignment will never occur. Profit means campaign-wide STO premium credits, BTC costs, roll credits or debits, fees, and other approved strategy costs—not gross premium or an estimated after-tax result.

A separate measurement objective is to determine whether the strategy adds economic value relative to simply holding the shares. The current Phase 1–6 implementation does not yet calculate complete campaign profit or performance.

V1 uses SQLite, EF Core, an ASP.NET Core Minimal API, and a future Excel presentation layer. It does **not** automatically submit, modify, cancel, or roll trades.

Phases 1–6 are complete and merged into `main`. The implementation includes normalized Tradier market data, versioned indicators, immutable entry evaluations, immutable position-sizing evaluations, and immutable defense/roll evaluations. The next priority is the Phase 7 manual transaction, current-position, and campaign lifecycle.

## Architecture

The solution keeps the domain and strategy core independent from infrastructure:

```text
OptionsEngine.Api (composition root)
├── OptionsEngine.Infrastructure ──> OptionsEngine.Application
└── OptionsEngine.Application
    ├── OptionsEngine.Strategy ──> OptionsEngine.Domain
    └── OptionsEngine.MarketData
```

`OptionsEngine.Domain` contains provider- and persistence-independent models. `OptionsEngine.Strategy` depends only on Domain and owns deterministic indicator, entry, sizing, defense, and roll calculations. `OptionsEngine.MarketData` owns normalized market-data records and `IMarketDataProvider`; its Tradier adapter maps production HTTP payloads at the boundary. `OptionsEngine.Application` orchestrates use cases. `OptionsEngine.Infrastructure` owns EF Core/SQLite persistence. The API composes these layers.

Canonical Phase 3 indicator snapshots are keyed by symbol, as-of date, calculation version, and configuration version. Phase 4–6 analytical evaluations are append-only and retain their consumed inputs, resolved configuration, version identities, scores, gates, explanations, and missing-data states.

There is not yet a public API to record STO/BTC/roll/expiration/assignment events or to create, update, and close `OpenShortCallPosition` state. Until Phase 7 is implemented, the daily operational loop must not be described as complete.

## Prerequisites and setup

Install the .NET 10 SDK, clone the repository, then run:

```bash
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
```

Run the API with:

```bash
dotnet run --project src/OptionsEngine.Api
```

It applies EF Core migrations at startup. `GET /health` returns a JSON health result after checking SQLite connectivity.

## SQLite and migrations

The default local database is `options-engine.db` in the API working directory. Override it without source changes using the standard configuration key `ConnectionStrings__OptionsEngine`, for example:

```bash
ConnectionStrings__OptionsEngine='Data Source=/absolute/path/options-engine.db' dotnet run --project src/OptionsEngine.Api
```

To create a migration after installing the local `dotnet-ef` tool:

```bash
dotnet ef migrations add MigrationName --project src/OptionsEngine.Infrastructure --startup-project src/OptionsEngine.Api
dotnet ef database update --project src/OptionsEngine.Infrastructure --startup-project src/OptionsEngine.Api
```

SQLite runtime files are ignored by Git.

## Tradier market-data token

Phase 2 uses only the Tradier **production** market-data API. Configure a personal production access token locally; it is never stored in appsettings or source control:

```bash
dotnet user-secrets set "Tradier:AccessToken" "YOUR_PRODUCTION_TOKEN" --project src/OptionsEngine.Api
```

`Tradier__AccessToken` is also supported for environment-based configuration. See [docs/TRADIER.md](docs/TRADIER.md) for the endpoint, cache, persistence, and security details. Sandbox, account access, and trading endpoints are not implemented.

## Market-data endpoints

- `GET /api/market/{symbol}/quote`
- `GET /api/market/{symbol}/history?start=YYYY-MM-DD&end=YYYY-MM-DD`
- `GET /api/market/{symbol}/options/expirations`
- `GET /api/market/{symbol}/options?expiration=YYYY-MM-DD`

## Indicator endpoint

`GET /api/indicators/{symbol}` calculates and persists the current canonical indicator snapshot from stored observations, then returns its facts and availability statuses. Add `?asOf=YYYY-MM-DD` to calculate for an exact historical or non-trading date. Without `asOf`, the API uses the latest stored trading observation for the configured provider at or before the request's UTC date; it returns 404 if none exists. Future observations do not affect historical calculations. The GET does not fetch fresh provider data or alter source observations.

The server owns `Indicators:CalculationVersion` and `Indicators:ConfigurationVersion` in configuration; clients cannot select versions through the endpoint. Indicator parameters and `Indicators:SectorBenchmarks` can be configured in the same section. Missing or invalid server configuration fails startup. Unavailable values are returned as `null` with a status, never as zero; IV30 also includes its unavailable reason. Exact-identity historical snapshot retrieval remains internal to Application/repository code.

## Strategy evaluation endpoints

- `POST /api/holdings/{holdingId}/entry-evaluations`
- `GET /api/entry-evaluations/{entryStrategyEvaluationId}`
- `GET /api/holdings/{holdingId}/entry-evaluations`
- `POST /api/entry-evaluations/{entryStrategyEvaluationId}/position-sizing-evaluations`
- `GET /api/position-sizing-evaluations/{positionSizingEvaluationId}`
- `POST /api/holdings/{holdingId}/positions/{positionId}/defense-evaluations`
- `GET /api/defense-evaluations/{defenseEvaluationId}`
- `GET /api/holdings/{holdingId}/positions/{positionId}/defense-evaluations`
- `GET /api/roll-evaluations/{rollEvaluationId}`

The POST routes calculate and persist new immutable analytical evaluations. Retrieval routes do not execute trades. Defense evaluation currently requires an already-persisted open short-call position.

## Roadmap and user workflow

- [Post-Phase-6 Roadmap](docs/design/POST-PHASE-6-ROADMAP.md) defines Phases 7–12 and the spreadsheet MVP boundary.
- [Covered-Call Decision Workflow](docs/product/COVERED-CALL-DECISION-WORKFLOW.md) describes the intended daily user workflow and current limitations.
- [Design Decisions](docs/design/OPTIONS-ENGINE-DESIGN-DECISIONS.md) records approved architectural and strategy decisions.
- [Current Handoff](docs/design/OPTIONS-ENGINE-HANDOFF.md) summarizes the merged baseline and next design task.

The planned sequence is operational lifecycle, assignment-protection hardening, composed daily decision API, Excel decision dashboard MVP, profit/performance measurement, then research and optimization. Strike laddering, scheduled/push alerts, brokerage automation, and tax-dollar optimization are not spreadsheet MVP requirements.

## Foundation assumptions

The acceptance criteria do not define validation rules for holding configuration values, so Phase 1 persists them unchanged. Relationship deletion is deliberately restricted: an account with holdings or a holding with tax lots must not be deleted until its dependents are addressed explicitly.

# Options Engine

Options Engine is a .NET 10 covered-call decision-support system. It will help analyze holdings, covered-call opportunities, and risk, while preserving the user’s control of all brokerage activity. The product and strategy source of truth is [SPECIFICATION.md](SPECIFICATION.md).

V1 uses SQLite, EF Core, an ASP.NET Core Minimal API, and a future Excel presentation layer. It does **not** automatically submit, modify, cancel, or roll trades.

## Architecture

The solution keeps the domain and strategy core independent from infrastructure:

```text
OptionsEngine.Api (composition root)
├── OptionsEngine.Infrastructure ──> OptionsEngine.Application
└── OptionsEngine.Application
    ├── OptionsEngine.Strategy ──> OptionsEngine.Domain
    └── OptionsEngine.MarketData
```

`OptionsEngine.Domain` contains provider- and persistence-independent account, holding, and tax-lot models. `OptionsEngine.Strategy` depends only on Domain and remains infrastructure-independent; its indicator boundary accepts provider-independent daily observations and produces versioned, as-of indicator snapshots. `OptionsEngine.MarketData` owns normalized market-data records and `IMarketDataProvider`; its Tradier adapter maps production HTTP payloads at the boundary. `OptionsEngine.Application` orchestrates the provider abstraction and SQLite cache. `OptionsEngine.Infrastructure` owns EF Core/SQLite snapshot persistence. The API composes these layers.

Phase 3E adds application orchestration over persisted historical prices and normalized option-chain observations, then stores a canonical indicator snapshot keyed by symbol, as-of date, calculation version, and configuration version. Recalculation updates that same canonical row; version-matched IV30 history supports IV Rank and IV Percentile. Empty observed option chains remain explicit. No Phase 3 indicator HTTP endpoint exists yet.

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

## Phase 1 assumptions

The acceptance criteria do not define validation rules for holding configuration values, so Phase 1 persists them unchanged. Relationship deletion is deliberately restricted: an account with holdings or a holding with tax lots must not be deleted until its dependents are addressed explicitly.

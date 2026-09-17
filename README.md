# Options Engine

Options Engine is a .NET 10 covered-call decision-support system. It will help analyze holdings, covered-call opportunities, and risk, while preserving the user’s control of all brokerage activity. The product and strategy source of truth is [SPECIFICATION.md](SPECIFICATION.md).

V1 uses SQLite, EF Core, an ASP.NET Core Minimal API, and a future Excel presentation layer. It does **not** automatically submit, modify, cancel, or roll trades.

## Architecture

The Phase 1 solution uses a dependency flow toward the core:

```text
API (composition root) -> Application -> Domain / Strategy / MarketData
                         -> Infrastructure -> Domain
```

`OptionsEngine.Domain` contains provider- and persistence-independent account, holding, and tax-lot models. `OptionsEngine.Infrastructure` owns EF Core and SQLite. The other layers are intentionally empty foundations until their specified phases.

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

## Secrets

Phase 1 requires no provider credentials. Future local development secrets should use .NET User Secrets or environment variables; do not add credentials to `appsettings.json`, the workbook, or source control.

## Phase 1 assumptions

The acceptance criteria do not define validation rules for holding configuration values, so Phase 1 persists them unchanged. Relationship deletion is deliberately restricted: an account with holdings or a holding with tax lots must not be deleted until its dependents are addressed explicitly.

# Tradier production market data

Phase 2 uses only Tradier's production market-data API at `https://api.tradier.com/v1/`. It requests quotes (`/markets/quotes`), daily history (`/markets/history`), option expirations (`/markets/options/expirations`), and option chains (`/markets/options/chains?greeks=true`). No sandbox, account, position, order, or trading endpoint is implemented.

Set a production personal access token locally using .NET User Secrets:

```bash
dotnet user-secrets set "Tradier:AccessToken" "YOUR_PRODUCTION_TOKEN" --project src/OptionsEngine.Api
```

Alternatively set `Tradier__AccessToken` in the process environment. Never put a token in `appsettings.json`, fixtures, or source control. The service fails clearly when a market-data request is made without one.

All external payloads are normalized before they reach Application. SQLite keeps timestamped quote and append-only option snapshots; daily bars are idempotently upserted by symbol, date, and provider. Cache lifetimes are configurable through the `MarketDataCache` section.

Implementation details were verified against Tradier’s official documentation for [quotes](https://docs.tradier.com/reference/brokerage-api-markets-get-quotes), [history](https://docs.tradier.com/reference/brokerage-api-markets-get-history), [expirations](https://docs.tradier.com/reference/brokerage-api-markets-get-options-expirations), and [chains](https://docs.tradier.com/reference/brokerage-api-markets-get-options-chains).

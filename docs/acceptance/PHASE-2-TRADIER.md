# Phase 2 — Tradier Market Data Integration Acceptance Checklist

## Objective

Implement the first external market-data integration for Options Engine using the Tradier production market-data API.

Phase 2 establishes the provider-independent market-data boundary that all later indicator and strategy engines will consume.

The implementation must retrieve, normalize, persist, cache, and expose:

1. Underlying quotes.
2. Historical daily price data.
3. Option expiration dates.
4. Option chains.
5. Provider-supplied option Greeks and implied volatility when available.

`SPECIFICATION.md` remains the authoritative product specification.

`AGENTS.md` remains the authoritative repository engineering guidance.

This document defines the Phase 2 implementation and acceptance criteria.

---

# 1. Governing Design Principle

Tradier is a market-data provider.

Tradier is not part of the Options Engine domain model.

The architecture must preserve this boundary:

```text
Tradier Production API
        |
        v
Tradier HTTP Client
        |
        v
Tradier DTOs
        |
        v
Tradier Mapping
        |
        v
Normalized Market Data Models
        |
        +-------------------+
        |                   |
        v                   v
   Persistence         Application
                           |
                           v
                          API
```

Future indicator and strategy code must consume normalized market-data models rather than Tradier DTOs.

The implementation must permit a future provider to implement the same provider abstraction without modifying strategy engines.

---

# 2. Phase 2 Scope

Phase 2 MUST implement:

- Tradier production API configuration.
- Secure bearer-token authentication.
- Provider-independent market-data abstraction.
- Quote retrieval.
- Historical daily price retrieval.
- Option expiration retrieval.
- Option-chain retrieval.
- Provider-supplied Greeks and implied-volatility preservation.
- Normalized market-data models.
- Market-data persistence.
- Historical option snapshots.
- Basic SQLite-backed caching.
- Rate-limit awareness.
- Conservative HTTP resilience.
- Provider error translation.
- Market-data API endpoints.
- Deterministic automated tests.
- Sanitized production-shaped Tradier fixtures.
- Documentation.

Phase 2 MUST NOT implement:

- Tradier sandbox support.
- Brokerage account access.
- Fidelity integration.
- Schwab integration.
- Order placement.
- Order modification.
- Order cancellation.
- Automatic trading.
- RSI.
- Bollinger Bands.
- MACD.
- ATR.
- Realized volatility.
- Resistance detection.
- Market regime.
- Sector regime.
- CCOS.
- Contract Score.
- Position Sizing.
- DRS.
- Roll Engine.
- RQS.
- Excel integration.

---

# 3. Provider Abstraction

Define or complete a provider-independent market-data abstraction.

Conceptually:

```csharp
public interface IMarketDataProvider
{
    Task<MarketQuote> GetQuoteAsync(
        string symbol,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HistoricalBar>> GetHistoricalPricesAsync(
        string symbol,
        DateOnly start,
        DateOnly end,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DateOnly>> GetOptionExpirationsAsync(
        string symbol,
        CancellationToken cancellationToken = default);

    Task<OptionChain> GetOptionChainAsync(
        string symbol,
        DateOnly expiration,
        CancellationToken cancellationToken = default);
}
```

The exact signatures may differ if the existing architecture suggests a better design.

Acceptance criteria:

- [ ] Provider-independent interface exists.
- [ ] Interface contains no Tradier-specific types.
- [ ] Interface contains no Tradier-specific terminology.
- [ ] I/O methods are asynchronous.
- [ ] I/O methods accept `CancellationToken`.
- [ ] Application code depends on the abstraction rather than `TradierMarketDataProvider`.
- [ ] Strategy does not reference the Tradier implementation.
- [ ] A future provider could implement the abstraction without modifying strategy engines.

---

# 4. Tradier Provider

Implement a Tradier provider responsible for:

- constructing Tradier requests;
- authenticating requests;
- deserializing Tradier responses;
- interpreting provider errors;
- capturing rate-limit metadata;
- mapping provider DTOs to normalized market-data models.

The provider must not:

- calculate technical indicators;
- calculate CCOS;
- calculate Contract Score;
- perform position sizing;
- perform defense analysis;
- perform roll analysis;
- expose Tradier DTOs to Application or Strategy.

Acceptance criteria:

- [ ] Tradier-specific code remains inside the MarketData/provider boundary.
- [ ] Provider DTOs are separate from normalized models.
- [ ] Tradier response-shape changes are isolated from strategy code.
- [ ] No strategy logic exists in the provider.

---

# 5. Tradier Environment

V1 shall integrate exclusively with the Tradier production market-data API.

Tradier sandbox support is explicitly out of scope.

The production base URL shall be represented through strongly typed provider configuration rather than scattered throughout application code.

Conceptually:

```json
{
  "Tradier": {
    "BaseUrl": "https://api.tradier.com/v1/"
  }
}
```

The API token must not be stored in `appsettings.json`.

Acceptance criteria:

- [ ] V1 uses only the Tradier production API.
- [ ] No sandbox environment selector exists.
- [ ] No sandbox-specific implementation exists.
- [ ] No sandbox-specific application behavior exists.
- [ ] Base URL is represented through strongly typed provider configuration.
- [ ] Provider configuration is validated.
- [ ] Tradier-specific configuration remains outside Domain and Strategy.

Automated testing shall use mocked HTTP responses and sanitized production-shaped fixtures rather than a Tradier sandbox.

---

# 6. Authentication and Secrets

Use a Tradier personal API access token supplied through secure configuration.

Requests shall authenticate using a bearer token.

The token shall be loaded through the standard .NET configuration system.

For local development, .NET User Secrets shall be supported.

Acceptance criteria:

- [ ] Tradier token is not committed.
- [ ] Token is not present in `appsettings.json`.
- [ ] Token is not present in test fixtures.
- [ ] Token is not logged.
- [ ] Authorization headers are not logged.
- [ ] Missing token produces a clear configuration/authentication failure when Tradier access is attempted.
- [ ] README documents local token configuration.
- [ ] Automated tests do not require a real token.

OAuth authorization-code support is out of scope for V1.

---

# 7. HTTP Client

Use `HttpClientFactory`.

Acceptance criteria:

- [ ] `HttpClientFactory` is used.
- [ ] Client configuration is strongly defined.
- [ ] Base address comes from configuration.
- [ ] Requests use JSON responses.
- [ ] Bearer authentication is supplied correctly.
- [ ] Cancellation is supported.
- [ ] A new `HttpClient` is not created for every request.
- [ ] Secrets are never logged.
- [ ] HTTP implementation details do not leak into Domain or Strategy.

---

# 8. Normalized Market Quote

Define a provider-independent quote model.

Preserve at minimum:

```text
Symbol
Timestamp
Last
Bid
Ask
Open
High
Low
PreviousClose
Volume
Provider
```

When provider metadata permits it, preserve useful information about observation freshness or delayed status.

Acceptance criteria:

- [ ] Monetary values use `decimal`.
- [ ] Timestamp uses `DateTimeOffset`.
- [ ] Missing fields remain explicitly unavailable.
- [ ] Missing fields are never silently converted to zero.
- [ ] Provider identity is retained for auditability.
- [ ] No Tradier DTO appears in the normalized model.

---

# 9. Quote Retrieval

Implement quote retrieval using the appropriate Tradier production market-data endpoint.

Acceptance criteria:

- [ ] Valid equity symbol maps successfully.
- [ ] Valid ETF symbol maps successfully.
- [ ] Symbol input is normalized consistently.
- [ ] Invalid symbol produces an explicit error.
- [ ] Missing quote data does not become a zero-price quote.
- [ ] Authentication failures are translated.
- [ ] Rate-limit failures are translated.
- [ ] Provider failures are distinguishable from malformed responses.
- [ ] Cancellation is honored.

---

# 10. Historical Price Model

Define a normalized historical daily bar.

At minimum:

```text
Symbol
Date
Open
High
Low
Close
Volume
Provider
```

Acceptance criteria:

- [ ] Prices use `decimal`.
- [ ] Volume uses an appropriate integral type.
- [ ] Trading date uses `DateOnly`.
- [ ] Missing required OHLC data is handled explicitly.
- [ ] Model contains no Tradier-specific response objects.

---

# 11. Historical Price Retrieval

Retrieve daily historical prices from Tradier.

Phase 2 requires daily bars only.

Acceptance criteria:

- [ ] Caller specifies symbol.
- [ ] Caller specifies start date.
- [ ] Caller specifies end date.
- [ ] Invalid date ranges are rejected.
- [ ] Daily OHLCV data is normalized.
- [ ] Empty responses are handled explicitly.
- [ ] Results are returned chronologically.
- [ ] At least two years of daily history can be requested for an ordinary equity when available from the provider.
- [ ] Malformed or incomplete records are tested.
- [ ] No assumption is made that historical prices are dividend-adjusted unless explicitly confirmed by provider data.

---

# 12. Option Expirations

Implement option-expiration retrieval.

Normalized output shall be provider-independent.

Acceptance criteria:

- [ ] Expirations can be retrieved for a valid optionable symbol.
- [ ] Dates use `DateOnly`.
- [ ] Dates are sorted ascending.
- [ ] Duplicate dates are removed if encountered.
- [ ] Empty expiration lists are represented explicitly.
- [ ] Invalid/non-optionable symbols do not produce manufactured expiration dates.

---

# 13. Normalized Option Contract Snapshot

Define a provider-independent option contract snapshot.

Preserve at minimum:

```text
OptionSymbol
UnderlyingSymbol
Timestamp

Expiration
Strike
OptionType

Bid
Ask
Last

Volume
OpenInterest

ImpliedVolatility

Delta
Gamma
Theta
Vega

UnderlyingPrice
Provider
```

Provider-supplied additional fields useful for later research may be preserved where justified.

Acceptance criteria:

- [ ] Strike uses `decimal`.
- [ ] Bid/ask/last use `decimal`.
- [ ] Timestamp uses `DateTimeOffset`.
- [ ] Expiration uses `DateOnly`.
- [ ] Option type is strongly represented.
- [ ] Greeks use an appropriate quantitative numeric type.
- [ ] Missing Greeks remain explicitly unavailable.
- [ ] Missing IV remains explicitly unavailable.
- [ ] Missing volume/open interest remain explicitly unavailable where appropriate.
- [ ] No derived strategy scores are calculated.

---

# 14. Option Type

Represent option type explicitly.

At minimum:

```text
Call
Put
```

Acceptance criteria:

- [ ] Tradier strings are translated at the provider boundary.
- [ ] Arbitrary provider option-type strings do not propagate throughout the application.
- [ ] Unknown option types are handled explicitly rather than guessed.

---

# 15. OCC Option Symbol

Preserve the complete OCC option symbol supplied by Tradier.

Do not reconstruct it when the provider supplies it.

Acceptance criteria:

- [ ] OCC symbol survives normalization.
- [ ] OCC symbol survives persistence.
- [ ] OCC symbol survives API serialization.
- [ ] Calls and puts remain distinguishable.
- [ ] OCC symbol identifies the contract but not an individual market observation.

---

# 16. Option Chain Retrieval

Retrieve an option chain using:

```text
Underlying Symbol
Expiration Date
```

Request and preserve provider-supplied Greeks when supported.

Acceptance criteria:

- [ ] Calls are returned.
- [ ] Puts are returned.
- [ ] Contracts map to normalized models.
- [ ] OCC option symbol is preserved.
- [ ] Strike is preserved.
- [ ] Bid/ask/last are preserved.
- [ ] Volume/open interest are preserved.
- [ ] Greeks are preserved when supplied.
- [ ] IV is preserved when supplied.
- [ ] Missing Greeks remain missing.
- [ ] Missing IV remains missing.
- [ ] Underlying association is preserved.
- [ ] Provider DTOs do not escape the provider implementation.

---

# 17. Greeks and Implied Volatility

Preserve provider-supplied:

```text
Delta
Gamma
Theta
Vega
ImpliedVolatility
```

Phase 2 shall not independently calculate Greeks or IV.

The application must distinguish:

```text
Delta = 0
```

from:

```text
Delta unavailable
```

The same principle applies to all Greeks and IV.

Acceptance criteria:

- [ ] Delta maps correctly when present.
- [ ] Gamma maps correctly when present.
- [ ] Theta maps correctly when present.
- [ ] Vega maps correctly when present.
- [ ] IV maps correctly when present.
- [ ] Missing values remain nullable or otherwise explicitly unavailable.
- [ ] Tests cover responses with Greeks.
- [ ] Tests cover responses without Greeks.

---

# 18. Market Data Persistence

Persist normalized market observations.

At minimum Phase 2 shall persist:

```text
MarketQuoteSnapshot
HistoricalPriceBar
OptionContractSnapshot
```

Expiration dates may be persisted if useful for caching.

Acceptance criteria:

- [ ] Normalized observations are persisted.
- [ ] Persistence does not depend on raw Tradier DTOs.
- [ ] Provider identity is retained.
- [ ] Observation timestamps are retained.
- [ ] Historical observations are not silently overwritten by newer observations.

---

# 19. Quote Snapshot Persistence

Quote snapshots shall preserve enough information to identify:

```text
Symbol
Provider
Observation Timestamp
Quote Values
```

Acceptance criteria:

- [ ] Quote snapshots persist successfully.
- [ ] New observations can coexist with older observations.
- [ ] Latest observation can be efficiently queried.
- [ ] Missing optional quote fields survive persistence correctly.

---

# 20. Historical Bar Persistence

Historical daily bars shall be idempotent.

Repeated retrieval of the same:

```text
Symbol
Trading Date
Provider
```

must not create uncontrolled duplicates.

Acceptance criteria:

- [ ] Appropriate unique constraint/index exists.
- [ ] Duplicate retrieval is handled deterministically.
- [ ] Existing bars may be updated deterministically when appropriate.
- [ ] Bars can be queried by symbol/date range.
- [ ] Date-range query has an appropriate index.

---

# 21. Option Snapshot Persistence

Option-chain retrieval shall create timestamped historical observations.

Each persisted observation must preserve enough information to identify:

```text
Underlying
OCC Option Symbol
Observation Timestamp
Provider
Expiration
Strike
Option Type
```

Acceptance criteria:

- [ ] Multiple observations of the same option contract can coexist.
- [ ] Retrieving a new chain does not overwrite previous observations.
- [ ] OCC symbol survives persistence.
- [ ] Greeks survive persistence.
- [ ] Nullable Greeks remain nullable.
- [ ] IV survives persistence.
- [ ] Historical snapshots can be queried by underlying and observation time.

---

# 22. Snapshot Identity

The OCC symbol identifies an option contract.

It does not uniquely identify an observation.

Persistence must distinguish observations conceptually by:

```text
Option Contract
Provider
Observation Timestamp
```

Acceptance criteria:

- [ ] OCC symbol alone is not the primary identity of an option snapshot.
- [ ] Multiple observations of one OCC contract are retained.
- [ ] Snapshot identity supports future research and backtesting.

---

# 23. Raw Provider Payloads

Raw Tradier payload retention is optional in Phase 2.

If implemented:

- [ ] Raw payload storage is separated from normalized models.
- [ ] Authorization information is never stored.
- [ ] Storage can be disabled.
- [ ] Raw payloads are not required for normal application operation.

Do not delay Phase 2 to build raw-payload archival.

Normalized historical snapshots are mandatory.

---

# 24. Caching

Implement a simple SQLite-backed caching policy to avoid unnecessary Tradier requests.

Define configurable freshness policies for:

```text
Quote
HistoricalBars
OptionExpirations
OptionChain
```

Acceptance criteria:

- [ ] Freshness settings are configuration-driven.
- [ ] Cache hit avoids unnecessary provider request.
- [ ] Stale cache triggers refresh.
- [ ] Observation timestamp remains the actual observation timestamp.
- [ ] Cached data is never represented as newly observed data.
- [ ] Cache-hit behavior is tested.
- [ ] Cache-miss behavior is tested.
- [ ] Stale-cache behavior is tested.

Do not introduce distributed caching in V1.

---

# 25. Rate-Limit Awareness

Capture Tradier rate-limit metadata when supplied.

Expected provider headers include:

```text
X-Ratelimit-Allowed
X-Ratelimit-Used
X-Ratelimit-Available
X-Ratelimit-Expiry
```

Acceptance criteria:

- [ ] Headers are parsed defensively.
- [ ] Missing headers do not cause failure.
- [ ] Available rate-limit information can be included in structured diagnostics.
- [ ] HTTP 429 becomes an explicit rate-limit error.
- [ ] Provider is not aggressively retried after rate limiting.
- [ ] Rate-limit handling is tested.

Do not build a complex distributed rate-limiter in Phase 2.

---

# 26. HTTP Resilience

Retries must be conservative and bounded.

Transient conditions may include:

```text
temporary network failures
selected 5xx responses
```

Do not blindly retry:

```text
400
401
403
404
```

Rate limiting must respect provider feedback.

Acceptance criteria:

- [ ] Retry count is bounded.
- [ ] Cancellation interrupts retries.
- [ ] Authentication failures are not repeatedly retried.
- [ ] Invalid requests are not repeatedly retried.
- [ ] At least one transient retry scenario is tested.
- [ ] At least one non-retryable scenario is tested.
- [ ] Standard .NET resilience facilities are preferred over a custom retry framework.

---

# 27. Provider Error Translation

Expose provider-independent failure semantics for at least:

```text
AuthenticationFailure
RateLimited
InvalidSymbol
InvalidRequest
ProviderUnavailable
MalformedProviderResponse
MissingMarketData
```

Acceptance criteria:

- [ ] Raw Tradier exceptions are not the primary Application contract.
- [ ] Useful diagnostic context is preserved internally.
- [ ] Secrets are excluded from diagnostics.
- [ ] API can map important failure categories to appropriate HTTP responses.

---

# 28. External Data Validation

Tradier responses are external inputs.

Validate important invariants during normalization.

Examples:

```text
Symbol exists
Option symbol exists
Strike is valid
Expiration exists
Option type is recognized
Observation timestamp is valid
```

Acceptance criteria:

- [ ] Required identity data is validated.
- [ ] Optional missing data is distinguished from malformed required data.
- [ ] Invalid required values are not replaced with plausible defaults.
- [ ] Malformed records are rejected or explicitly handled.
- [ ] Validation failures are observable through logs/errors.

---

# 29. Logging

Provider operations should produce structured diagnostics where appropriate.

Useful dimensions include:

```text
Provider
Operation
Symbol
Expiration
HTTP Status
Duration
CacheHit
RateLimitAvailable
CorrelationId
```

Acceptance criteria:

- [ ] Structured logging is used.
- [ ] Bearer token is never logged.
- [ ] Authorization header is never logged.
- [ ] User Secrets are never logged.
- [ ] Full provider payloads are not logged by default.
- [ ] Provider failures contain enough non-sensitive context for diagnosis.

---

# 30. Database Migration

Phase 2 persistence changes shall use EF Core migrations.

Migration shall create structures required for:

```text
Market quote snapshots
Historical price bars
Option contract snapshots
```

and appropriate supporting indexes.

Acceptance criteria:

- [ ] Migration exists.
- [ ] Clean database can be created from migrations.
- [ ] Existing Phase 1 database migrates successfully.
- [ ] Symbol/date queries have appropriate indexes.
- [ ] Option snapshot queries have appropriate indexes.
- [ ] Unique historical-bar constraint exists.
- [ ] Migration verification is documented in the PR.

---

# 31. Application Service

Introduce only the orchestration required for Phase 2.

Conceptually:

```text
MarketDataService
       |
       v
Check Cache
       |
       +-- Fresh --> Return Existing Observation
       |
       +-- Missing/Stale
                 |
                 v
              Provider
                 |
                 v
             Normalize
                 |
                 v
              Persist
                 |
                 v
               Return
```

Acceptance criteria:

- [ ] Application orchestrates market-data retrieval.
- [ ] Application depends on provider abstraction.
- [ ] HTTP logic does not live in Application.
- [ ] Tradier DTO mapping does not live in Application.
- [ ] API endpoints do not implement caching logic.
- [ ] API endpoints do not call Tradier directly.

---

# 32. Quote API

Expose:

```text
GET /api/market/{symbol}/quote
```

Conceptual response:

```json
{
  "symbol": "MSFT",
  "timestamp": "2026-09-17T18:00:00Z",
  "last": 500.25,
  "bid": 500.20,
  "ask": 500.30,
  "volume": 12345678,
  "provider": "Tradier"
}
```

Acceptance criteria:

- [ ] Endpoint uses Application service.
- [ ] Response is provider-independent.
- [ ] Tradier DTO is not exposed.
- [ ] Invalid symbol maps appropriately.
- [ ] Provider outage maps appropriately.
- [ ] Missing price remains distinguishable from zero.

---

# 33. Historical Price API

Expose:

```text
GET /api/market/{symbol}/history
    ?start=YYYY-MM-DD
    &end=YYYY-MM-DD
```

Acceptance criteria:

- [ ] Valid date range returns normalized daily bars.
- [ ] Invalid range returns validation error.
- [ ] Results are chronological.
- [ ] Persistence entities are not exposed directly.
- [ ] Provider DTOs are not exposed.

---

# 34. Expiration API

Expose:

```text
GET /api/market/{symbol}/options/expirations
```

Acceptance criteria:

- [ ] Returns normalized expiration dates.
- [ ] Dates are ascending.
- [ ] Invalid/non-optionable symbol is handled explicitly.
- [ ] Response contains no Tradier DTOs.

---

# 35. Option Chain API

Expose:

```text
GET /api/market/{symbol}/options
    ?expiration=YYYY-MM-DD
```

Acceptance criteria:

- [ ] Expiration is required.
- [ ] Calls are returned.
- [ ] Puts are returned.
- [ ] Contracts are normalized.
- [ ] Greeks/IV are included when available.
- [ ] Missing Greeks remain unavailable/null.
- [ ] No Contract Score is calculated.
- [ ] No strategy filtering is performed.
- [ ] No covered-call recommendation is generated.

This endpoint exposes market data, not strategy output.

---

# 36. Tradier Test Fixtures

Create sanitized production-shaped Tradier JSON fixtures covering at minimum:

```text
valid quote
invalid or empty quote
historical daily bars
expiration dates
option chain with Greeks
option chain without Greeks
authentication error
rate-limit response
provider/server error
```

Acceptance criteria:

- [ ] Fixtures contain no credentials.
- [ ] Fixtures contain no personal account information.
- [ ] Fixtures closely represent Tradier production response shapes.
- [ ] Fixture purpose is identifiable.
- [ ] Automated tests require no network connectivity.

---

# 37. HTTP Tests

Tradier integration tests shall use mocked HTTP behavior.

Tests should verify:

- [ ] Correct endpoint path.
- [ ] Correct query parameters.
- [ ] JSON response requested.
- [ ] Authorization header is present.
- [ ] Token value is not exposed in test output.
- [ ] Cancellation propagates.
- [ ] Deserialization works.
- [ ] Mapping works.
- [ ] Error translation works.
- [ ] Rate-limit metadata handling works.

Automated tests MUST NOT call either the Tradier production API or Tradier sandbox API.

---

# 38. Mapping Tests

Mapping tests shall verify at minimum:

- [ ] Equity quote.
- [ ] Missing quote fields.
- [ ] Historical OHLCV.
- [ ] Expiration dates.
- [ ] Call contract.
- [ ] Put contract.
- [ ] Strike.
- [ ] OCC symbol.
- [ ] Delta.
- [ ] Gamma.
- [ ] Theta.
- [ ] Vega.
- [ ] Implied volatility.
- [ ] Missing Greeks.
- [ ] Missing IV.
- [ ] Missing volume/open interest.

---

# 39. Persistence Tests

Automated tests shall verify:

- [ ] Quote snapshot persists.
- [ ] Multiple quote observations can coexist.
- [ ] Historical bars persist.
- [ ] Historical duplicate handling is deterministic.
- [ ] Option snapshots persist.
- [ ] Multiple observations of one option contract can coexist.
- [ ] OCC symbol survives round-trip persistence.
- [ ] Nullable Greeks survive round-trip persistence.
- [ ] IV survives round-trip persistence.
- [ ] Symbol/date queries return expected records.

---

# 40. Application Tests

Test the complete orchestration path with mocked provider behavior.

At minimum:

- [ ] Cache miss calls provider.
- [ ] Cache miss persists normalized result.
- [ ] Fresh cache hit avoids provider call.
- [ ] Stale cache calls provider.
- [ ] Stale cache preserves correct observation timestamp.
- [ ] Provider failure does not create fabricated market data.
- [ ] Cancellation propagates.
- [ ] Missing optional Greeks do not invalidate an otherwise usable option chain.

---

# 41. API Integration Tests

At minimum test:

```text
GET /health

GET /api/market/MSFT/quote

GET /api/market/MSFT/history

GET /api/market/MSFT/options/expirations

GET /api/market/MSFT/options?expiration=<fixture-date>
```

Acceptance criteria:

- [ ] Tests use deterministic mocked provider data.
- [ ] Tests require no internet connectivity.
- [ ] Tests require no Tradier token.
- [ ] Tests do not depend on current market conditions.
- [ ] Responses contain normalized API DTOs.

---

# 42. Optional Live Production Smoke Tests

A small manually invoked live-production verification mechanism may be implemented.

If implemented:

- [ ] It is disabled by default.
- [ ] It requires explicit developer configuration.
- [ ] It never runs during normal `dotnet test`.
- [ ] It uses secure token configuration.
- [ ] It performs read-only market-data operations.
- [ ] It does not access brokerage accounts.
- [ ] It does not submit orders.
- [ ] It does not modify orders.
- [ ] It does not cancel orders.

Live smoke-test support is optional and shall not block Phase 2 completion.

---

# 43. README Updates

README shall document:

- [ ] Tradier is the V1 market-data provider.
- [ ] V1 uses the Tradier production API only.
- [ ] Tradier sandbox is not supported.
- [ ] How to configure a personal API token.
- [ ] How to use .NET User Secrets.
- [ ] How to configure the production base URL.
- [ ] How to run the application.
- [ ] Example market-data endpoints.
- [ ] Automated tests use mocked provider responses.
- [ ] No brokerage trading endpoints are used.
- [ ] Automatic trading does not exist.

Never include a real API token.

---

# 44. Tradier Documentation

Create or update:

```text
docs/TRADIER.md
```

Document:

- provider architecture;
- production API configuration;
- authentication;
- endpoints consumed;
- normalized model mapping;
- caching;
- historical snapshot behavior;
- rate-limit handling;
- retry behavior;
- error translation;
- testing strategy;
- optional live verification.

Include links to official Tradier documentation where useful.

Do not duplicate the complete Tradier API documentation.

---

# 45. Clean Repository Verification

From a clean repository:

```bash
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
```

must succeed without:

- a Tradier token;
- internet connectivity;
- a pre-existing SQLite database.

Acceptance criteria:

- [ ] Restore succeeds.
- [ ] Release build succeeds.
- [ ] Automated tests succeed.
- [ ] No live provider access occurs.
- [ ] No developer-specific absolute paths are required.
- [ ] No secrets are required.

---

# 46. Manual Production Verification

With a valid Tradier production API token configured securely, manually verify:

```text
GET /health

GET /api/market/MSFT/quote

GET /api/market/MSFT/history
    ?start=<approximately two years ago>
    &end=<current date>

GET /api/market/MSFT/options/expirations

GET /api/market/MSFT/options
    ?expiration=<valid expiration>
```

Verify:

- [ ] Quote is returned.
- [ ] Historical records are returned.
- [ ] Expiration dates are returned.
- [ ] Calls are returned.
- [ ] Puts are returned.
- [ ] OCC symbols are populated.
- [ ] Bid/ask data is populated when supplied by Tradier.
- [ ] Greeks are populated when supplied by Tradier.
- [ ] IV is populated when supplied by Tradier.
- [ ] Normalized records persist in SQLite.
- [ ] A subsequent fresh-cache request avoids an unnecessary provider call.

Manual verification is recommended but shall not be fabricated if it cannot be performed.

---

# 47. Phase 2 Exit Criteria

Phase 2 is complete only when:

- [ ] Provider abstraction exists.
- [ ] Tradier production provider implements it.
- [ ] Tradier DTOs remain isolated.
- [ ] Secure bearer authentication is implemented.
- [ ] No sandbox implementation exists.
- [ ] Quote retrieval works.
- [ ] Historical daily retrieval works.
- [ ] Expiration retrieval works.
- [ ] Option-chain retrieval works.
- [ ] Greeks/IV are preserved when available.
- [ ] Missing Greeks/IV are represented safely.
- [ ] Normalized market data persists.
- [ ] Option snapshots accumulate historically.
- [ ] Historical bars are idempotent.
- [ ] Cache behavior works.
- [ ] Rate-limit responses are handled.
- [ ] Retry behavior is bounded.
- [ ] Provider errors are translated.
- [ ] API endpoints work.
- [ ] Automated tests pass without internet access.
- [ ] Automated tests pass without Tradier credentials.
- [ ] Clean database migration succeeds.
- [ ] Phase 1 database migrates forward.
- [ ] Documentation is updated.
- [ ] No Phase 3+ functionality has been introduced.
- [ ] No brokerage access has been introduced.
- [ ] No trading capability has been introduced.

---

# 48. Required PR Evidence

The Phase 2 PR description shall contain:

## Build

```text
dotnet build --configuration Release

Result:
<actual result>
```

## Tests

```text
dotnet test --configuration Release

Result:
<actual result>

Tests:
<actual count>

Failures:
<actual count>
```

## Database

```text
Migration:
<actual migration name>

Clean migration:
PASS / FAIL

Phase 1 -> Phase 2 migration:
PASS / FAIL
```

## Provider Tests

```text
Quote fixture:
PASS / FAIL

Historical fixture:
PASS / FAIL

Expiration fixture:
PASS / FAIL

Option chain with Greeks:
PASS / FAIL

Option chain without Greeks:
PASS / FAIL

Authentication failure:
PASS / FAIL

Provider error translation:
PASS / FAIL

Rate-limit handling:
PASS / FAIL

Transient retry:
PASS / FAIL
```

## Manual Tradier Verification

```text
Environment:
Production / Not Performed

Quote:
PASS / FAIL / NOT PERFORMED

History:
PASS / FAIL / NOT PERFORMED

Expirations:
PASS / FAIL / NOT PERFORMED

Option Chain:
PASS / FAIL / NOT PERFORMED

Greeks:
PASS / FAIL / UNAVAILABLE / NOT PERFORMED

Persistence:
PASS / FAIL / NOT PERFORMED

Cache:
PASS / FAIL / NOT PERFORMED
```

Never fabricate manual verification results.

## Scope

```text
Tradier sandbox:
Not implemented

Indicators:
Not implemented

Strategy engines:
Not implemented

Excel:
Not implemented

Brokerage account access:
Not implemented

Trade execution:
Not implemented
```

## Assumptions

List any implementation assumptions made because the specification was ambiguous.

---

# 49. Definition of Done

Phase 2 is done when Options Engine can reliably execute:

```text
Tradier Production API
        |
        v
HTTP / Authentication
        |
        v
Tradier DTOs
        |
        v
Normalization
        |
        v
Persistence / Cache
        |
        v
Application
        |
        v
REST API
```

for:

```text
Quote
Historical Daily Prices
Option Expirations
Option Chains
Greeks / Implied Volatility
```

while satisfying all of the following architectural constraints:

1. Tradier-specific models do not leak into Domain or Strategy.
2. Missing financial data is never silently converted to zero.
3. Historical option observations are retained rather than overwritten.
4. Automated tests require neither network access nor Tradier credentials.
5. Tradier sandbox support is not implemented.
6. No indicator or strategy logic is introduced.
7. No brokerage account or trading capability is introduced.
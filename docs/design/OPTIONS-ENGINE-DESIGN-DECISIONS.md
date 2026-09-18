# Options Engine --- Durable Design Decision Register

This file records decisions that emerged during requirements/design
discussions and should not be rediscovered or silently changed by an
implementation agent.

## Product / Risk

-   Primary objective: sustainable covered-call income with very low
    assignment probability.
-   Preserving highly appreciated shares takes priority over maximizing
    option premium.
-   Assignment risk is treated as a hard constraint.
-   Initial covered-call sale horizon: 14--45 DTE.
-   Rolling is supported.
-   Every sell recommendation should eventually include a defense plan.
-   Automatic execution is out of V1.

## Data / Provider

-   Tradier is the V1 market-data provider.
-   V1 uses Tradier PRODUCTION API only.
-   Tradier sandbox is explicitly out of scope.
-   Automated tests use mocked HTTP and sanitized production-shaped
    fixtures.
-   Core/domain/strategy code remains provider-independent.
-   Provider-specific DTOs remain at the provider boundary.
-   Missing provider values remain unavailable/null; never substitute
    zero.
-   Historical market observations should be retained to support
    reproducibility and research.

## Architecture

-   C# is authoritative for calculations and strategy logic.
-   SQLite is V1 persistence.
-   Excel is presentation/configuration/audit, not the strategy engine.
-   Domain must not depend on EF Core, SQLite, ASP.NET, Tradier, HTTP,
    or Excel.
-   MarketData owns provider abstractions and mapping.
-   Infrastructure owns EF Core/SQLite/repositories/cache.
-   Application orchestrates.
-   API remains thin.
-   Statistical calculations may use double; monetary values use
    decimal.

## Reproducibility

-   Explicit AsOfDate is mandatory for historical calculations.
-   No observation after AsOfDate may affect the result.
-   Future data appended after a historical AsOfDate must not alter that
    historical calculation.
-   Missing data is not zero.
-   Missing regime is not neutral.
-   Calculation, configuration, and strategy versions are separate:
    -   IndicatorCalculationVersion
    -   ConfigurationVersion
    -   StrategyVersion
-   Historical results must not silently change under newer
    algorithms/configuration.

## Phase 3 Indicator Math

-   SMA: arithmetic mean; no partial windows.
-   RSI14: Wilder formulation; initial arithmetic average; Wilder
    smoothing thereafter; requires 15 closes.
-   Bollinger: 20 periods, 2 sigma; population standard deviation; %B
    unclamped.
-   MACD: 12/26/9; EMA seeded by first complete-period SMA.
-   ATR14: standard True Range; Wilder smoothing.
-   RV20/RV30: close-to-close log returns; sample standard deviation;
    annualized by sqrt(252); decimal-ratio representation.
-   Market regime: SPY default; SMA50/SMA200/close plus 20-trading-day
    SMA50 direction.
-   Sector regime: same regime algorithm using configured sector
    benchmark.

## Resistance

-   Phase 3 resistance is an objective market-structure observation, not
    a strategy score.
-   Deterministic confirmed swing highs.
-   Default swing window = 3.
-   Historical swing high must have its complete future-side
    confirmation window on/before AsOfDate.
-   Default lookback = 120 trading observations.
-   V1 uses one-dimensional complete-linkage clustering after sorting
    confirmed swing highs by price ascending, then TradingDate ascending.
-   One fixed threshold for the requested AsOfDate is
    `ClusterDistanceAtrMultiplier × ATR14(AsOfDate)`; the default
    multiplier is `0.75`.
-   A candidate joins the current cluster when its price minus the
    cluster minimum is less than or equal to that threshold. The
    boundary is inclusive, and every cluster's maximum-minus-minimum
    range is at most the threshold. Transitive chaining is not allowed.
-   Cluster representative = arithmetic mean of member swing-high
    prices; it does not control cluster membership.
-   Configurable `MinimumResistanceTouches` defaults to `2` and must
    be at least `1`. Only clusters meeting this minimum qualify.
-   Primary resistance = nearest qualified cluster representative
    strictly above current close; equality is not overhead resistance.
-   No qualified level =\> unavailable.
-   Do not fabricate resistance.
-   `ResistanceStrength` was intentionally removed because its formula
    was not defined and it mixed structural evidence with later strategy
    interpretation.
-   Phase 3 instead exposes:
    -   ResistancePrice
    -   DistanceToResistance
    -   DistanceToResistancePercent
    -   ResistanceTouchCount
    -   ResistanceLastTouchDate
    -   ResistanceAgeTradingDays
-   ResistanceAgeTradingDays counts trading observations, not calendar
    days.
-   Phase 4 later determines how resistance evidence contributes to the
    15-point Resistance/Structure CCOS component.

## IV Context

-   Phase 2 owns normalized contract-level option observations.
-   Phase 3 owns provider-independent underlying-level IV context.
-   Phase 3 outputs include IV30, IVRank, and IVPercentile.
-   Phase 4 consumes those values for CCOS.
-   Phase 4 must not independently reconstruct them from provider data.
-   V1 consumes normalized provider-supplied contract IV; it does not
    invert Black-Scholes or another option-pricing model.
-   IV30 targets 30 calendar DTE. For each eligible expiration, use
    the nearest ATM strike (lower strike wins an equal-distance tie),
    within configurable `MaxAtmStrikeDistanceRatio = 0.05` by default.
-   Both call and put IV are required at that strike; expiration IV is
    their arithmetic mean. V1 applies no liquidity filter.
-   An exact 30-DTE expiration supplies IV30 directly. Otherwise,
    linearly interpolate between qualifying expirations bracketing 30
    DTE; never extrapolate.
-   IV Rank and IV Percentile use the most recent 252 valid IV30
    observations through AsOfDate, with at least 126 required. Missing
    observations are ignored, never treated as zero.
-   Historical calculations use only data available through AsOfDate;
    future observations cannot alter past results.

## CCOS

Weights: - Implied volatility 25 - RSI 15 - Bollinger 15 -
Trend/Momentum 15 - Resistance/Structure 15 - Market/Sector Regime 15

Default open threshold: CCOS \>= 70.

Existing IV component requires IV Percentile and IV30/RV30.

## Contract Score

Weights: - Delta 25 - Strike safety 20 - Premium efficiency 20 - DTE
10 - IV edge 10 - Liquidity 10 - Theta 5

Default entry: CCOS \>= 70 AND Contract Score \>= 80.

## Phase Boundaries

Phase 3 calculates facts/classifications.

Phase 4 interprets them for covered-call entry.

Phase 3 must not calculate: - CCOS - Contract Score - trade
eligibility - recommended contract - recommended contract count - DRS -
roll recommendation

## Explicitly Deferred

-   Trend/Momentum CCOS scoring
-   Bollinger bandwidth CCOS scoring
-   Resistance/Structure CCOS scoring

These require explicit design before implementation.

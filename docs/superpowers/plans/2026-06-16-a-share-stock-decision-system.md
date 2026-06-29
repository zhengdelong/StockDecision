# A股选股与交易决策系统 DDD + MySQL Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a local A-share stock decision support system for a 20000 RMB account, focused on profitable daily decision assistance, explainable indicators, backtesting, and learning review.

**Architecture:** Use a pragmatic DDD backend. `Domain` owns pure business concepts and policies, `Application` owns use cases and repository ports, `Infrastructure` owns EF Core + MySQL and external adapters, `Api` exposes HTTP controllers, and `Worker` runs scheduled application use cases. Python collects free data into MySQL raw tables only; C# imports raw data into domain tables.

**Tech Stack:** .NET 8, ASP.NET Core Web API controllers, EF Core, Pomelo MySQL provider, MySQL 8.4, Python, AKShare, SQLAlchemy, PyMySQL, Vue 3, TypeScript, Element Plus, ECharts, Docker Compose, xUnit, pytest, Vitest.

---

## Source Documents

Implementation must follow:

- `docs/stock-decision-system/00-overview.md`
- `docs/stock-decision-system/01-indicator-glossary.md`
- `docs/stock-decision-system/02-trading-strategy-20k.md`
- `docs/stock-decision-system/03-data-source-and-fields.md`
- `docs/stock-decision-system/04-backtest-rules.md`
- `docs/stock-decision-system/05-learning-and-review.md`
- `docs/stock-decision-system/06-scoring-and-execution-v2.md`

If code behavior conflicts with strategy documents, the documents win until a new strategy version is explicitly created.

For all work related to scoring, fund-flow ingestion, 龙虎榜 ingestion, trade execution plans, position management advice, and v2 backtest behavior, `06-scoring-and-execution-v2.md` is the primary source of truth.

## DDD Dependency Rules

Required project graph:

```text
StockDecision.Api -> StockDecision.Application -> StockDecision.Domain
StockDecision.Worker -> StockDecision.Application -> StockDecision.Domain
StockDecision.Infrastructure -> StockDecision.Application + StockDecision.Domain
```

Rules:

- `Domain` must not reference EF Core, ASP.NET Core, MySQL, Python, or Infrastructure.
- `Application` may reference `Domain`, but not `Infrastructure`.
- `Infrastructure` implements repository and raw-data ports defined by `Application`.
- `Api` controllers stay thin and call Application use cases.
- `Worker` runs scheduled Application use cases; it must not embed strategy rules.

## Task 1: Runtime Skeleton

**Files:**

- Create: `src/StockDecision.Domain/`
- Create: `src/StockDecision.Application/`
- Create: `src/StockDecision.Infrastructure/`
- Create: `src/StockDecision.Api/`
- Create: `src/StockDecision.Worker/`
- Create: `collector/`
- Create: `web/`
- Create: `docker-compose.yml`
- Create: `.env.example`

Steps:

- [ ] Initialize `StockDecision.sln`.
- [ ] Add Domain, Application, Infrastructure, Api, Worker, and test projects.
- [ ] Enforce DDD dependency direction with architecture tests.
- [ ] Add MySQL 8.4 to Docker Compose with `utf8mb4`.
- [ ] Add Vue 3 TypeScript frontend under `web/`.
- [ ] Add Python collector under `collector/`.
- [ ] Verify `dotnet test StockDecision.sln` passes.
- [ ] Verify `npm run build` passes.
- [ ] Verify collector health command runs.

## Task 2: MySQL Persistence Boundary

**Files:**

- Create: `src/StockDecision.Infrastructure/Persistence/StockDecisionDbContext.cs`
- Create: `src/StockDecision.Infrastructure/Persistence/StockDecisionDatabaseOptions.cs`
- Create: `src/StockDecision.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`
- Create: `src/StockDecision.Infrastructure/Migrations/`

Steps:

- [ ] Use `Pomelo.EntityFrameworkCore.MySql`.
- [ ] Read connection string named `StockDecision`.
- [ ] Register EF Core DbContext through `AddInfrastructure`.
- [ ] Use MySQL `utf8mb4` charset.
- [ ] Add infrastructure tests for service registration and connection string validation.

## Task 3: Raw Data Import Tables

Python writes only raw tables:

- `raw_stocks`
- `raw_daily_bars`
- `raw_financial_snapshots`
- `raw_market_index_bars`
- `raw_industry_daily_stats`
- `data_ingestion_logs`

Task 3 must encode these operating rules, not just table creation:

- Full bootstrap scope:
  - stock master snapshot: current full market
  - daily bars: target universe last 8 years
  - market indices: HS300 / CSI500 / ChiNext last 10 years
  - financial snapshots: last 12 report periods
  - industry daily stats: last 3 years
- Incremental cadence:
  - 15:30-16:30: stocks, daily bars, indices, industries
  - 18:00-21:00: financial snapshots and failed-job retries
  - 21:00-22:00: raw-import -> indicators -> scores -> signals
- Backtest readiness:
  - minimum 5 full years daily bars
  - recommended 6-8 years
  - first 120 trading days are warm-up only
- AKShare constraint assumption:
  - no published universal request quota
  - collector must use conservative throttling, retry, and circuit-break rules
  - incomplete-day data blocks buy-signal generation

Steps:

- [ ] Implement collector MySQL writer using SQLAlchemy + PyMySQL.
- [ ] Store AKShare payloads after field normalization, but before applying trading rules.
- [ ] Record source, interface, symbol, trade date or report date, batch id, fetched time, success count, failure count, missing-field count, retry count, payload hash, and exception message.
- [ ] Add throttling policy by interface type: full-market snapshots low-frequency, per-symbol daily bars serial or small-batch, financial interfaces serial with slower delay.
- [ ] Add exponential backoff and per-interface circuit-break rules for timeout spikes and upstream throttling.
- [ ] If key daily data is incomplete, mark the ingestion log as incomplete and block buy-plan generation for that date.
- [ ] Add a completeness policy: if daily bar coverage is below threshold or market indices are missing, that trade date is ineligible for signals and backtests.

## Task 4: Domain Model

Bounded contexts:

- MarketData: stocks, daily bars, indices, industry stats, financial snapshots.
- Strategy: indicators, market regime, filters, scores, trade signals.
- Portfolio: simulated positions, stop loss, stop profit, position sizing.
- Backtesting: backtest runs, trades, equity curve, drawdown.
- Learning: indicator explanations, trade review notes.

Core value objects:

- `StockCode`
- `TradeDate`
- `Money`
- `Price`
- `Percentage`
- `RiskRewardRatio`
- `ScoreBreakdown`

Steps:

- [ ] Implement value objects with validation.
- [ ] Implement domain policies for indicators, market regime, scoring, signals, position sizing, and exits.
- [ ] Keep all domain policies deterministic and database-free.
- [ ] Add unit tests for each policy.

## Task 5: Application Use Cases

Application owns orchestration:

- Import raw market data into domain tables.
- Calculate indicators for a trade date.
- Generate candidate stocks.
- Generate today trade plans.
- Maintain simulated positions.
- Run backtests.
- Produce learning explanations.

Steps:

- [ ] Define repository ports in Application.
- [ ] Define request/response DTOs for use cases.
- [ ] Implement use cases without EF Core references.
- [ ] Add tests with fake repositories.

## Task 6: Infrastructure Repositories

Infrastructure owns:

- EF Core mappings.
- MySQL migrations.
- Repository implementations.
- Raw table access.
- Unit of work / transaction boundary.

Steps:

- [ ] Map decimal fields with explicit precision.
- [ ] Index stock code, trade date, strategy version, and signal date.
- [ ] Store Chinese explanation text as `utf8mb4`.
- [ ] Add MySQL integration tests for migrations, indexes, and Chinese text.

## Task 7: API

Controllers:

- `DashboardController`
- `CandidatesController`
- `StocksController`
- `SignalsController`
- `PositionsController`
- `BacktestsController`
- `LearningController`

Endpoints:

- `GET /api/dashboard`
- `GET /api/candidates?date=yyyy-MM-dd`
- `GET /api/stocks/{code}`
- `GET /api/signals/today`
- `POST /api/positions/simulate-buy`
- `POST /api/positions/{id}/sell`
- `POST /api/backtests/run`
- `GET /api/backtests/{id}`
- `GET /api/learning/{signalId}`

Steps:

- [ ] Keep controllers thin.
- [ ] Return DTOs, never EF entities.
- [ ] Return ProblemDetails-compatible errors.
- [ ] Add API integration tests.

## Task 8: Frontend

Pages:

- Dashboard.
- Candidates.
- Stock detail.
- Today plan.
- Simulated positions.
- Backtests.
- Learning.

Steps:

- [ ] Show market state and whether trading is allowed.
- [ ] Show candidates with score breakdown, strategy type, stop price, target price, and risk-reward ratio.
- [ ] Explain MA20, MA60, ATR14, relative strength, PE, PB, and ROE in beginner language.
- [ ] Hide “可买” until the current strategy version has acceptable backtest status.

## Task 9: Scoring And Execution v2

This task upgrades the system from a score-only signal engine to a score + execution-plan decision system.

Required specification:

- `docs/stock-decision-system/06-scoring-and-execution-v2.md`

Steps:

- [ ] Add raw fund-flow and 龙虎榜 tables to the collector layer, with normalized fields and audit metadata.
- [ ] Add market/domain snapshots for fund-flow and 龙虎榜 summaries.
- [ ] Replace the v1 score logic with the v2 score dimensions, weights, and de-duplicated rules.
- [ ] Keep risk-reward as an execution gate instead of a major scoring source.
- [ ] Add trade execution plans for candidates, signals, and stock detail responses.
- [ ] Add position-management advice for held positions.
- [ ] Update backtests so entry, invalidation, holding-day limits, and exits follow the same v2 execution rules.
- [ ] Update frontend drawers and position pages to show trade plan, fund-flow, 龙虎榜, and holding advice.
- [ ] Preserve strategy version separation between v1 and v2 results.

## Acceptance Criteria

- Architecture tests prove DDD dependency direction.
- Docker Compose uses MySQL 8.4 with `utf8mb4`.
- `.env.example` uses MySQL connection variables.
- Collector uses SQLAlchemy + PyMySQL and writes raw tables only.
- Collector plan explicitly defines bootstrap range, incremental cadence, AKShare throttling assumptions, retry rules, and incomplete-day blocking.
- Infrastructure uses Pomelo EF Core MySQL provider.
- Strategy behavior remains traceable to `docs/stock-decision-system/`.
- `dotnet test StockDecision.sln` passes.
- `npm run build` passes.
- Collector health command runs.

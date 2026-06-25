# StockDecision

Local A-share stock decision system built around a Docker Compose development
environment.

## Development Runtime

The primary local runtime is Docker Compose. The default long-running services are:

- `mysql`
- `api`
- `web`

Task-oriented services are available through profiles and `docker compose run`:

- `collector` for one-off market data jobs
- `collector-scheduler` for automatic incremental collection
- `worker` for automatic raw-to-domain synchronization

## Prerequisites

- Docker Desktop for Windows
- Linux containers enabled

Create a local `.env` from `.env.example` if you need to override defaults.

If your Windows host uses a local HTTP proxy for outbound internet access, set
`HTTP_PROXY` and `HTTPS_PROXY` in `.env` before running market-data jobs. For a
proxy that listens on the Windows host itself, prefer
`http://host.docker.internal:<port>` so Linux containers can reach it reliably.
If your proxy client exposes a SOCKS port instead of an HTTP port, use
`socks5h://host.docker.internal:<port>`.

## First Startup

Start the core development stack from the repository root:

```powershell
docker compose up -d mysql api web
```

Check service state:

```powershell
docker compose ps
```

Open the main entry points:

- API: `http://localhost:5080`
- Web: `http://localhost:5173`
- MySQL: `localhost:3306`

## Collector Jobs

Run one-off collector commands with Compose:

```powershell
docker compose --profile jobs run --rm collector health
docker compose --profile jobs run --rm collector bootstrap-stocks
docker compose --profile jobs run --rm collector sync-daily --symbols 600000
```

Start the automatic incremental scheduler when you want the collector to keep
running and pick up daily jobs by time:

```powershell
docker compose --profile scheduler up -d collector-scheduler
```

The collector container talks to MySQL through the internal hostname `mysql`.
From the Windows host, use `localhost:3306` to inspect the same database with a
GUI client or CLI tool.

## Optional Worker

The worker service is not part of the default startup path. Start it when you want
the system to automatically import raw collector tables into domain snapshot tables
and refresh indicators, candidates, and signals in the background:

```powershell
docker compose --profile worker up -d worker
```

The worker now performs:

- one immediate sync check on startup
- one polling pass every `DOMAIN_SYNC_WORKER_POLL_SECONDS` seconds
- actual sync only when raw trade-date data or raw financial report data is newer than the imported domain snapshot

## Collector Scheduler

The automatic collector scheduler is also optional. It runs inside the collector
image and triggers incremental jobs based on `.env` settings:

- `COLLECTOR_INTRADAY_SYNC_TIME`
- `COLLECTOR_INTRADAY_SYNC_DAYS`
- `COLLECTOR_NIGHT_SYNC_TIME`
- `COLLECTOR_NIGHT_SYNC_DAYS`
- `COLLECTOR_RETRY_SYNC_TIME`
- `COLLECTOR_RETRY_SYNC_DAYS`
- `COLLECTOR_FINANCIAL_SYNC_TIME`
- `COLLECTOR_FINANCIAL_SYNC_DAYS`

Default behavior:

- intraday market sync at `11:30` on ISO weekdays `1,2,3,4,5`
- first end-of-day market sync at `16:30` on ISO weekdays `1,2,3,4,5`
- retry market sync at `18:30` on ISO weekdays `1,2,3,4,5`
- financial sync at `21:00` on ISO weekday `7` (Sunday)

Current timetable in `Asia/Shanghai`:

| Task | Purpose | Schedule |
| --- | --- | --- |
| `sync-daily-intraday` | stock snapshot, daily bars, index bars, industry stats | Monday-Friday `11:30` |
| `sync-daily-night` | stock snapshot, daily bars, index bars, industry stats | Monday-Friday `16:30` |
| `sync-daily-retry` | stock snapshot, daily bars, index bars, industry stats | Monday-Friday `18:30` |
| `sync-financials` | financial snapshot sync | Sunday `21:00` |

Scheduler behavior:

- If the container starts after the scheduled time and that job has not run yet for the same day, it will catch up immediately.
- Scheduler logs now include `scheduled_at`, `started_at`, `finished_at`, and `duration_seconds`.
- `data_ingestion_logs.created_at` is stored in the database time basis, so compare it with container local time carefully when checking exact trigger time.

## Task Center And Sync Chain

The runtime chain is now split clearly:

- `collector-scheduler` writes raw tables and collector audit rows
- `worker` watches raw trade-date and financial freshness, then imports into domain tables
- `api` and `web` read the domain snapshot tables for dashboard, candidates, signals, industries, financials, strategy explanation, backtest, and task center

The task center shows:

- latest collector runs from `data_ingestion_logs`
- latest domain sync runs from `domain_sync_runs`
- raw-vs-imported freshness for trade date and financial report date

Useful commands:

```powershell
docker compose --profile scheduler up -d collector-scheduler
docker compose logs -f collector-scheduler
docker compose --profile jobs run --rm collector schedule-sync --once
```

## Logs And Inspection

Tail logs:

```powershell
docker compose logs -f api
docker compose logs -f web
docker compose logs -f worker
```

Inspect recent collector audit rows:

```powershell
docker compose exec mysql mysql -ustock_decision -pstock_decision_dev stock_decision -e "SELECT interface_name, target_scope, success_count, failure_count, is_complete, is_signal_eligible FROM data_ingestion_logs ORDER BY id DESC LIMIT 10;"
```

Inspect collector checkpoints:

```powershell
docker compose exec mysql mysql -ustock_decision -pstock_decision_dev stock_decision -e "SELECT job_type, stock_code, status, retry_count, updated_at FROM collector_checkpoints ORDER BY id DESC LIMIT 10;"
```

## Local Tests

Containerized runtime is the primary path, but code-level tests remain local:

```powershell
dotnet test StockDecision.sln
cd collector
..\.venv\Scripts\python.exe -m pytest tests -q
```

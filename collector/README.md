# Stock Collector

Python data collection package for free A-share data sources.

v1 priority:

1. AKShare daily bars.
2. AKShare market index bars.
3. AKShare valuation and financial snapshots.
4. Ingestion logs and missing-field reports.

## Runtime Path

The primary runtime path is Docker Compose from the repository root. The `collector`
container is a single-run job runner, not a long-lived service, and is intended to
run against the shared `mysql` container in the Compose network.

1. Start MySQL first:

```powershell
docker compose up -d mysql
```

2. Run a health check:

```powershell
docker compose --profile jobs run --rm collector health
```

3. Run a bootstrap example:

```powershell
docker compose --profile jobs run --rm collector bootstrap-stocks
```

4. Run an incremental example:

```powershell
docker compose --profile jobs run --rm collector sync-daily --symbols 600000
```

5. Run one scheduler evaluation pass:

```powershell
docker compose --profile jobs run --rm collector schedule-sync --once
```

The image default command is `health`, so `docker compose --profile jobs run --rm collector`
is a safe readiness check and does not trigger market data collection by itself.

For day-to-day local development, the recommended long-running stack is:

```powershell
docker compose up -d mysql api web
```

`collector` remains a job runner on top of that stack and should still be invoked
through `docker compose --profile jobs run --rm collector ...`.

For automatic incremental collection, start the dedicated scheduler service:

```powershell
docker compose --profile scheduler up -d collector-scheduler
```

## Environment

The collector reads these variables from Compose and `.env`:

- `MYSQL_HOST`
- `MYSQL_PORT`
- `MYSQL_DATABASE`
- `MYSQL_USER`
- `MYSQL_PASSWORD`
- `COLLECTOR_TIMEZONE`
- `COLLECTOR_SCHEDULER_POLL_SECONDS`
- `COLLECTOR_NIGHT_SYNC_TIME`
- `COLLECTOR_NIGHT_SYNC_DAYS`
- `COLLECTOR_RETRY_SYNC_TIME`
- `COLLECTOR_RETRY_SYNC_DAYS`
- `COLLECTOR_FINANCIAL_SYNC_TIME`
- `COLLECTOR_FINANCIAL_SYNC_DAYS`

Inside Docker, `MYSQL_HOST` must stay as `mysql`. From the Windows host, use
`localhost:3306` to inspect the same database with a SQL client.

Use `.env.example` as the baseline when creating a local `.env`. Compose defaults
are already aligned with the checked-in sample values.

If outbound market-data requests need a Windows-host proxy, set `HTTP_PROXY` and
`HTTPS_PROXY` in `.env`. For a proxy bound on the host machine, use
`http://host.docker.internal:<port>` instead of `127.0.0.1` so the Linux
container can resolve the host endpoint consistently. If your proxy tool only
exposes a SOCKS listener, use `socks5h://host.docker.internal:<port>`.

## What Gets Written

Collector jobs write raw and audit records into MySQL tables:

- `raw_stocks`
- `raw_daily_bars`
- `raw_financial_snapshots`
- `raw_market_index_bars`
- `raw_industry_daily_stats`
- `data_ingestion_logs`
- `collector_checkpoints`

`data_ingestion_logs` records completeness and signal eligibility. `collector_checkpoints`
tracks per-symbol retry and resume status for long-running jobs.

## Scheduler Defaults

The `schedule-sync` job is a long-lived polling loop that checks whether the
current local time is past a configured schedule and whether that target scope
has already logged a run for the current day.

Default schedules:

- `sync-daily` first end-of-day run at `16:30` on ISO weekdays `1,2,3,4,5`
- `sync-daily` retry run at `18:30` on ISO weekdays `1,2,3,4,5`
- `sync-financials` at `21:00` on ISO weekday `7`

Current timetable in `Asia/Shanghai`:

| Job | Data scope | Schedule |
| --- | --- | --- |
| `sync-daily-night` | `raw_stocks`, `raw_daily_bars`, `raw_market_index_bars`, `raw_industry_daily_stats` | Monday-Friday `16:30` |
| `sync-daily-retry` | `raw_stocks`, `raw_daily_bars`, `raw_market_index_bars`, `raw_industry_daily_stats` | Monday-Friday `18:30` |
| `sync-financials` | `raw_financial_snapshots` | Sunday `21:00` |

Scheduler behavior notes:

- Missing a schedule window does not drop the job. If the container comes back later the same day, it will execute a catch-up run immediately.
- Scheduler summary logs include `scheduled_at`, `started_at`, `finished_at`, and `duration_seconds`.

## Smoke Check

A minimal end-to-end smoke path is:

```powershell
docker compose up -d mysql api web
docker compose --profile jobs run --rm collector health
docker compose --profile jobs run --rm collector bootstrap-stocks
docker compose --profile jobs run --rm collector sync-daily --symbols 600000
```

To inspect the latest logs from MySQL:

```powershell
docker compose exec mysql mysql -ustock_decision -pstock_decision_dev stock_decision -e "SELECT interface_name, target_scope, success_count, failure_count, is_complete, is_signal_eligible FROM data_ingestion_logs ORDER BY id DESC LIMIT 10;"
```

To inspect recent checkpoint status:

```powershell
docker compose exec mysql mysql -ustock_decision -pstock_decision_dev stock_decision -e "SELECT job_type, stock_code, status, retry_count, updated_at FROM collector_checkpoints ORDER BY id DESC LIMIT 10;"
```

## Troubleshooting

- `At least one stock code is required for this job.`
  Pass `--symbols` for `bootstrap-daily-bars`, `bootstrap-financials`, `retry-failed`,
  `sync-daily`, and `sync-financials`.
- MySQL connection errors
  Confirm `docker compose up -d mysql api web` completed and the `mysql` service is
  healthy before running collector jobs.
- Empty payloads or partial status
  Check `data_ingestion_logs.error_message`, `failure_count`, and `is_complete`. Some
  incremental runs can finish with `is_signal_eligible = false` when completeness
  thresholds are not met.
- AKShare dependency or remote data issues
  Run `docker compose --profile jobs run --rm collector health` first, then inspect
  the latest ingestion logs to see whether the failure is local startup or upstream
  data access.

## Local Dev

For development and pytest only, you can still run the package locally in a virtual
environment:

```powershell
cd collector
..\.venv\Scripts\python.exe -m pytest tests -q
```

from __future__ import annotations

import argparse
import json
import os
import traceback
from dataclasses import dataclass
from datetime import UTC
from datetime import date
from datetime import datetime
from datetime import time
from time import sleep
from typing import Any
from zoneinfo import ZoneInfo

from stock_collector.akshare_client import AkshareClient
from stock_collector.mysql_writer import MySqlSettings
from stock_collector.mysql_writer import RawDataWriter
from stock_collector.mysql_writer import create_in_memory_engine
from stock_collector.mysql_writer import create_mysql_engine
from stock_collector.normalizers import build_ingestion_log
from stock_collector.orchestrator import CollectorOrchestrator

SYMBOL_REQUIRED_JOBS = {
    "bootstrap-daily-bars",
    "bootstrap-financials",
    "retry-failed",
    "sync-daily",
    "sync-financials",
}

RETRY_COMPLETENESS_SCOPES = [
    "sync-daily-stocks",
    "sync-daily-indices",
    "sync-daily-industries",
    "sync-daily-bars",
]


@dataclass(frozen=True)
class ScheduledCollectorJob:
    name: str
    action: str
    target_scope: str
    run_time: time
    run_iso_weekdays: set[int]
    skip_when_trade_date_current: bool = False


@dataclass(frozen=True)
class SchedulerSettings:
    timezone: ZoneInfo
    poll_interval_seconds: int
    jobs: tuple[ScheduledCollectorJob, ...]


def main() -> None:
    parser = argparse.ArgumentParser(description="Stock data collector jobs")
    parser.add_argument(
        "job",
        choices=[
            "health",
            "bootstrap-indices",
            "bootstrap-stocks",
            "bootstrap-daily-bars",
            "bootstrap-financials",
            "bootstrap-industries",
            "retry-failed",
            "sync-daily",
            "sync-daily-missing",
            "sync-financials",
            "schedule-sync",
        ],
        help="Job name to run",
    )
    parser.add_argument("--symbols", nargs="*", default=[], help="Target stock codes")
    parser.add_argument("--limit-symbols", type=int, default=None, help="Limit symbols for financial sync")
    parser.add_argument("--trade-date", default=None, help="Target trade date in YYYY-MM-DD format")
    parser.add_argument(
        "--once",
        action="store_true",
        help="Run one scheduler evaluation pass and exit",
    )
    parser.add_argument(
        "--use-in-memory-db",
        action="store_true",
        help="Use an in-memory SQLite database instead of MySQL",
    )
    args = parser.parse_args()

    if args.job in SYMBOL_REQUIRED_JOBS and not args.symbols:
        raise SystemExit("At least one stock code is required for this job.")

    orchestrator = build_orchestrator(use_in_memory_db=args.use_in_memory_db)

    if args.job == "health":
        print(json.dumps(orchestrator_health(orchestrator), ensure_ascii=False))
        return

    if args.job == "bootstrap-indices":
        print_result(orchestrator.bootstrap_indices())
        return

    if args.job == "bootstrap-stocks":
        print_result(orchestrator.bootstrap_stocks())
        return

    if args.job == "bootstrap-industries":
        print_result(orchestrator.bootstrap_industries())
        return

    if args.job == "bootstrap-daily-bars":
        print_result(orchestrator.bootstrap_daily_bars(args.symbols))
        return

    if args.job == "bootstrap-financials":
        print_result(orchestrator.bootstrap_financials(args.symbols, limit_symbols=args.limit_symbols))
        return

    if args.job == "retry-failed":
        print_result(orchestrator.retry_failed(args.symbols))
        return

    if args.job == "sync-daily":
        selected_trade_date = _parse_trade_date_arg(args.trade_date)
        result = orchestrator.sync_daily_market(args.symbols, trade_date=selected_trade_date)
        print(json.dumps([item.__dict__ for item in result], ensure_ascii=False, default=str))
        return

    if args.job == "sync-daily-missing":
        selected_trade_date = _parse_trade_date_arg(args.trade_date) or datetime.now(UTC).date()
        missing_codes = orchestrator._writer.list_missing_daily_bar_stock_codes(trade_date=selected_trade_date)  # noqa: SLF001
        result = orchestrator.sync_daily_market(missing_codes, trade_date=selected_trade_date)
        print(json.dumps([item.__dict__ for item in result], ensure_ascii=False, default=str))
        return

    if args.job == "sync-financials":
        print_result(orchestrator.sync_financials(args.symbols, limit_symbols=args.limit_symbols))
        return

    if args.job == "schedule-sync":
        run_schedule_loop(orchestrator, once=args.once)


def print_result(result: Any) -> None:
    print(json.dumps(result.__dict__, ensure_ascii=False, default=str))


def build_orchestrator(*, use_in_memory_db: bool = False) -> CollectorOrchestrator:
    client = AkshareClient()
    writer = RawDataWriter(
        create_in_memory_engine() if use_in_memory_db else create_mysql_engine(load_mysql_settings())
    )
    writer.create_tables()
    return CollectorOrchestrator(client, writer)


def orchestrator_health(orchestrator: CollectorOrchestrator) -> dict[str, Any]:
    client_health = orchestrator._client.health()  # noqa: SLF001
    return {
        "source": client_health["source"],
        "status": client_health["status"],
        "time": datetime.now(UTC).isoformat(),
        "jobs": [
            "bootstrap-indices",
            "bootstrap-stocks",
            "bootstrap-daily-bars",
            "bootstrap-financials",
            "bootstrap-industries",
            "retry-failed",
            "sync-daily",
            "sync-daily-missing",
            "sync-financials",
            "schedule-sync",
        ],
    }


def run_schedule_loop(orchestrator: CollectorOrchestrator, *, once: bool = False) -> None:
    settings = load_scheduler_settings()
    while True:
        now = datetime.now(settings.timezone)
        try:
            outcomes = run_schedule_pass(orchestrator, settings=settings, now=now)
        except Exception as exc:  # noqa: BLE001
            outcomes = [
                {
                    "job": "schedule-sync",
                    "status": "failed",
                    "reason": "schedule_pass_exception",
                    "error": str(exc),
                    "traceback": traceback.format_exc(),
                    "evaluated_at": now,
                }
            ]
        if outcomes:
            print(json.dumps(outcomes, ensure_ascii=False, default=str))
        if once:
            return
        sleep(settings.poll_interval_seconds)


def run_schedule_pass(
    orchestrator: CollectorOrchestrator,
    *,
    settings: SchedulerSettings,
    now: datetime,
) -> list[dict[str, Any]]:
    writer = orchestrator._writer  # noqa: SLF001
    stock_codes: list[str] | None = None
    outcomes: list[dict[str, Any]] = []

    for job in settings.jobs:
        if now.isoweekday() not in job.run_iso_weekdays or now.time() < job.run_time:
            continue
        scheduled_at = now.replace(
            hour=job.run_time.hour,
            minute=job.run_time.minute,
            second=0,
            microsecond=0,
        )
        if writer.has_log_since(job.target_scope, created_at_or_after=scheduled_at):
            continue
        latest_trade_date = writer.get_latest_trade_date() if job.skip_when_trade_date_current else None
        if (
            latest_trade_date is not None
            and latest_trade_date >= scheduled_at.date()
            and writer.is_trade_date_fully_collected(scheduled_at.date(), scopes=RETRY_COMPLETENESS_SCOPES)
        ):
            continue

        lock_name = _build_schedule_lock_name(job=job, scheduled_at=scheduled_at)
        if not writer.acquire_advisory_lock(lock_name, timeout_seconds=0):
            outcomes.append(
                {
                    "job": job.name,
                    "status": "skipped",
                    "reason": "job_locked",
                    "scheduled_at": scheduled_at,
                    "started_at": now,
                    "finished_at": now,
                    "duration_seconds": 0.0,
                    "evaluated_at": now,
                }
            )
            continue

        try:
            if stock_codes is None:
                stock_codes = writer.list_stock_codes()
                if not stock_codes:
                    orchestrator.bootstrap_stocks()
                    stock_codes = writer.list_stock_codes()
                if not stock_codes:
                    outcomes.append(
                        {
                            "job": job.name,
                            "status": "skipped",
                            "reason": "no_stock_codes_available",
                            "scheduled_at": scheduled_at,
                            "started_at": now,
                            "finished_at": now,
                            "duration_seconds": 0.0,
                            "evaluated_at": now,
                        }
                    )
                    continue

            started_at = datetime.now(settings.timezone)
            if job.action == "sync-daily":
                result = orchestrator.sync_daily_market(stock_codes)
                finished_at = datetime.now(settings.timezone)
                total_rows_written = sum(item.rows_written for item in result)
                outcomes.append(
                    {
                        "job": job.name,
                        "status": "executed",
                        "datasets": [item.dataset for item in result],
                        "rows_written": total_rows_written,
                        "scheduled_at": scheduled_at,
                        "started_at": started_at,
                        "finished_at": finished_at,
                        "duration_seconds": round((finished_at - started_at).total_seconds(), 3),
                        "evaluated_at": now,
                    }
                )
                _append_schedule_marker(
                    writer,
                    job=job,
                    scheduled_at=scheduled_at,
                    started_at=started_at,
                    finished_at=finished_at,
                    success_count=total_rows_written,
                    failure_count=0,
                    is_complete=all(item.is_complete for item in result),
                    is_signal_eligible=all(item.is_signal_eligible for item in result),
                    error_message=None,
                )
                continue

            result = orchestrator.sync_financials(stock_codes)
            finished_at = datetime.now(settings.timezone)
            outcomes.append(
                {
                    "job": job.name,
                    "status": "executed",
                    "dataset": result.dataset,
                    "rows_written": result.rows_written,
                    "scheduled_at": scheduled_at,
                    "started_at": started_at,
                    "finished_at": finished_at,
                    "duration_seconds": round((finished_at - started_at).total_seconds(), 3),
                    "evaluated_at": now,
                }
            )
            _append_schedule_marker(
                writer,
                job=job,
                scheduled_at=scheduled_at,
                started_at=started_at,
                finished_at=finished_at,
                success_count=result.rows_written,
                failure_count=0,
                is_complete=result.is_complete,
                is_signal_eligible=result.is_signal_eligible,
                error_message=None,
            )
        except Exception as exc:  # noqa: BLE001
            failed_at = datetime.now(settings.timezone)
            outcomes.append(
                {
                    "job": job.name,
                    "status": "failed",
                    "reason": "job_execution_exception",
                    "error": str(exc),
                    "traceback": traceback.format_exc(),
                    "scheduled_at": scheduled_at,
                    "started_at": started_at if "started_at" in locals() else now,
                    "finished_at": failed_at,
                    "duration_seconds": round((failed_at - (started_at if "started_at" in locals() else now)).total_seconds(), 3),
                    "evaluated_at": now,
                }
            )
            _append_schedule_marker(
                writer,
                job=job,
                scheduled_at=scheduled_at,
                started_at=started_at if "started_at" in locals() else now,
                finished_at=failed_at,
                success_count=0,
                failure_count=1,
                is_complete=False,
                is_signal_eligible=False,
                error_message=str(exc),
            )
        finally:
            writer.release_advisory_lock(lock_name)

    return outcomes


def load_scheduler_settings() -> SchedulerSettings:
    timezone = ZoneInfo(_read_env("COLLECTOR_TIMEZONE", default="Asia/Shanghai"))
    return SchedulerSettings(
        timezone=timezone,
        poll_interval_seconds=int(_read_env("COLLECTOR_SCHEDULER_POLL_SECONDS", default="60")),
        jobs=(
            ScheduledCollectorJob(
                name="sync-end-of-day-final",
                action="sync-daily",
                target_scope="sync-end-of-day-final-bars",
                run_time=_parse_time(_read_env("COLLECTOR_NIGHT_SYNC_TIME", default="15:20")),
                run_iso_weekdays=_parse_iso_weekdays(
                    _read_env("COLLECTOR_NIGHT_SYNC_DAYS", default="1,2,3,4,5")
                ),
            ),
            ScheduledCollectorJob(
                name="sync-end-of-day-retry-1600",
                action="sync-daily",
                target_scope="sync-end-of-day-retry-1600",
                run_time=_parse_time(_read_env("COLLECTOR_RETRY_SYNC_TIME_1", default="16:00")),
                run_iso_weekdays=_parse_iso_weekdays(
                    _read_env("COLLECTOR_RETRY_SYNC_DAYS", default="1,2,3,4,5")
                ),
                skip_when_trade_date_current=True,
            ),
            ScheduledCollectorJob(
                name="sync-end-of-day-retry-1630",
                action="sync-daily",
                target_scope="sync-end-of-day-retry-1630",
                run_time=_parse_time(_read_env("COLLECTOR_RETRY_SYNC_TIME_2", default="16:30")),
                run_iso_weekdays=_parse_iso_weekdays(
                    _read_env("COLLECTOR_RETRY_SYNC_DAYS", default="1,2,3,4,5")
                ),
                skip_when_trade_date_current=True,
            ),
            ScheduledCollectorJob(
                name="sync-end-of-day-retry-1700",
                action="sync-daily",
                target_scope="sync-end-of-day-retry-1700",
                run_time=_parse_time(_read_env("COLLECTOR_RETRY_SYNC_TIME_3", default="17:00")),
                run_iso_weekdays=_parse_iso_weekdays(
                    _read_env("COLLECTOR_RETRY_SYNC_DAYS", default="1,2,3,4,5")
                ),
                skip_when_trade_date_current=True,
            ),
            ScheduledCollectorJob(
                name="sync-end-of-day-retry-1800",
                action="sync-daily",
                target_scope="sync-end-of-day-retry-1800",
                run_time=_parse_time(_read_env("COLLECTOR_RETRY_SYNC_TIME_4", default="18:00")),
                run_iso_weekdays=_parse_iso_weekdays(
                    _read_env("COLLECTOR_RETRY_SYNC_DAYS", default="1,2,3,4,5")
                ),
                skip_when_trade_date_current=True,
            ),
            ScheduledCollectorJob(
                name="sync-financials",
                action="sync-financials",
                target_scope="sync-financials",
                run_time=_parse_time(_read_env("COLLECTOR_FINANCIAL_SYNC_TIME", default="21:00")),
                run_iso_weekdays=_parse_iso_weekdays(
                    _read_env("COLLECTOR_FINANCIAL_SYNC_DAYS", default="7")
                ),
            ),
        ),
    )


def _parse_time(raw_value: str) -> time:
    hour_text, minute_text = raw_value.split(":", maxsplit=1)
    return time(hour=int(hour_text), minute=int(minute_text))


def _parse_iso_weekdays(raw_value: str) -> set[int]:
    days = {int(item.strip()) for item in raw_value.split(",") if item.strip()}
    if not days or any(day < 1 or day > 7 for day in days):
        raise RuntimeError("Schedule weekdays must be comma-separated ISO weekday numbers between 1 and 7.")
    return days


def _build_schedule_lock_name(*, job: ScheduledCollectorJob, scheduled_at: datetime) -> str:
    return f"collector:{job.name}:{scheduled_at.date().isoformat()}:{job.run_time.strftime('%H%M')}"


def _append_schedule_marker(
    writer: RawDataWriter,
    *,
    job: ScheduledCollectorJob,
    scheduled_at: datetime,
    started_at: datetime,
    finished_at: datetime,
    success_count: int,
    failure_count: int,
    is_complete: bool,
    is_signal_eligible: bool,
    error_message: str | None,
) -> None:
    writer.append_log(
        build_ingestion_log(
            batch_id=f"schedule:{job.name}:{scheduled_at.isoformat()}",
            source_name="scheduler",
            interface_name=job.action,
            target_scope=job.target_scope,
            is_incremental=True,
            started_at=started_at,
            finished_at=finished_at,
            success_count=success_count,
            failure_count=failure_count,
            missing_field_count=0,
            consecutive_failure_count=0,
            window_failure_rate=1.0 if failure_count > 0 else 0.0,
            is_complete=is_complete,
            is_signal_eligible=is_signal_eligible,
            circuit_breaker_opened=False,
            trade_date=scheduled_at.date(),
            error_message=_truncate_log_error_message(error_message),
        )
    )


def _truncate_log_error_message(error_message: str | None) -> str | None:
    # 调度失败时异常文本可能很长，这里与 data_ingestion_logs.error_message 字段长度对齐，避免二次写库失败。
    if error_message is None:
        return None
    return error_message[:1024]


def load_mysql_settings() -> MySqlSettings:
    return MySqlSettings(
        host=_read_env("MYSQL_HOST", default="127.0.0.1"),
        port=int(_read_env("MYSQL_PORT", default="3306")),
        database=_read_env("MYSQL_DATABASE", default="stockdecision"),
        user=_read_env("MYSQL_USER", default="stockdecision"),
        password=_read_env("MYSQL_PASSWORD", default="stockdecision"),
    )


def _read_env(name: str, *, default: str | None = None) -> str:
    value = os.getenv(name, default)
    if value is None or not value.strip():
        raise RuntimeError(f"Environment variable {name} is required.")
    return value


def _parse_trade_date_arg(raw_value: str | None) -> date | None:
    if raw_value is None or not raw_value.strip():
        return None
    return date.fromisoformat(raw_value.strip())


if __name__ == "__main__":
    main()

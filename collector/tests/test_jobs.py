from __future__ import annotations

import json
from datetime import UTC
from datetime import date
from datetime import datetime
from datetime import time
from zoneinfo import ZoneInfo

import pytest

from stock_collector.jobs import main
from stock_collector.jobs import run_schedule_pass
from stock_collector.jobs import ScheduledCollectorJob
from stock_collector.jobs import SchedulerSettings
from stock_collector.jobs import _build_schedule_batch_id
from stock_collector.jobs import _truncate_log_error_message
from stock_collector.orchestrator import CollectionRunResult


class FakeWriter:
    def __init__(self) -> None:
        self.stock_codes = ["600000", "000001"]
        self.missing_stock_codes = ["000001"]
        self.logged_scopes: dict[str, list[datetime]] = {}
        self.locked_names: set[str] = set()
        self.acquired_names: list[str] = []
        self.released_names: list[str] = []
        self.trade_date_fully_collected = False

    def list_stock_codes(self) -> list[str]:
        return list(self.stock_codes)

    def list_missing_daily_bar_stock_codes(self, *, trade_date: date, latest_batch_only: bool = True, limit: int | None = None) -> list[str]:
        _ = trade_date
        _ = latest_batch_only
        _ = limit
        return list(self.missing_stock_codes)

    def has_log_since(self, target_scope: str, *, created_at_or_after: datetime) -> bool:
        return any(item >= created_at_or_after for item in self.logged_scopes.get(target_scope, []))

    def append_log(self, row: dict[str, object]) -> None:
        target_scope = str(row["target_scope"])
        created_at = row["created_at"] if "created_at" in row else row["finished_at"]
        assert isinstance(created_at, datetime)
        self.logged_scopes.setdefault(target_scope, []).append(created_at)

    def acquire_advisory_lock(self, lock_name: str, *, timeout_seconds: int = 0) -> bool:
        _ = timeout_seconds
        if lock_name in self.locked_names:
            return False
        self.acquired_names.append(lock_name)
        return True

    def release_advisory_lock(self, lock_name: str) -> None:
        self.released_names.append(lock_name)

    def get_latest_trade_date(self) -> date | None:
        return None

    def is_trade_date_fully_collected(self, trade_date: date, *, scopes: list[str]) -> bool:
        _ = trade_date
        _ = scopes
        return self.trade_date_fully_collected


class FakeOrchestrator:
    def __init__(self) -> None:
        self.called_with: dict[str, object] = {}
        self._client = type("FakeClient", (), {"health": lambda self: {"source": "akshare", "status": "ready"}})()
        self._writer = FakeWriter()

    def bootstrap_indices(self) -> CollectionRunResult:
        self.called_with["job"] = "bootstrap-indices"
        return _result("raw_market_index_bars")

    def bootstrap_stocks(self) -> CollectionRunResult:
        self.called_with["job"] = "bootstrap-stocks"
        return _result("raw_stocks")

    def bootstrap_industries(self) -> CollectionRunResult:
        self.called_with["job"] = "bootstrap-industries"
        return _result("raw_industry_daily_stats")

    def bootstrap_daily_bars(self, symbols: list[str]) -> CollectionRunResult:
        self.called_with["job"] = "bootstrap-daily-bars"
        self.called_with["symbols"] = symbols
        return _result("raw_daily_bars")

    def bootstrap_financials(self, symbols: list[str], *, limit_symbols: int | None = None) -> CollectionRunResult:
        self.called_with["job"] = "bootstrap-financials"
        self.called_with["symbols"] = symbols
        self.called_with["limit_symbols"] = limit_symbols
        return _result("raw_financial_snapshots")

    def retry_failed(self, symbols: list[str]) -> CollectionRunResult:
        self.called_with["job"] = "retry-failed"
        self.called_with["symbols"] = symbols
        return _result("raw_daily_bars")

    def sync_daily_market(self, symbols: list[str], *, trade_date: date | None = None) -> list[CollectionRunResult]:
        self.called_with["job"] = "sync-daily"
        self.called_with["symbols"] = symbols
        self.called_with["trade_date"] = trade_date
        return [_result("raw_stocks"), _result("raw_daily_bars")]

    def sync_financials(self, symbols: list[str], *, limit_symbols: int | None = None) -> CollectionRunResult:
        self.called_with["job"] = "sync-financials"
        self.called_with["symbols"] = symbols
        self.called_with["limit_symbols"] = limit_symbols
        return _result("raw_financial_snapshots")


class FailingDailyOrchestrator(FakeOrchestrator):
    def sync_daily_market(self, symbols: list[str], *, trade_date: date | None = None) -> list[CollectionRunResult]:
        self.called_with["job"] = "sync-daily"
        self.called_with["symbols"] = symbols
        self.called_with["trade_date"] = trade_date
        raise RuntimeError("upstream html payload")


def _result(dataset: str) -> CollectionRunResult:
    return CollectionRunResult(
        dataset=dataset,
        rows_written=1,
        log_status="success",
        is_complete=True,
        is_signal_eligible=True,
        completeness_reasons=[],
    )


def test_health_job_prints_json(monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]) -> None:
    orchestrator = FakeOrchestrator()
    monkeypatch.setattr("stock_collector.jobs.build_orchestrator", lambda **_: orchestrator)
    monkeypatch.setattr("sys.argv", ["jobs.py", "health"])

    main()

    payload = json.loads(capsys.readouterr().out)
    assert payload["source"] == "akshare"
    assert payload["status"] == "ready"
    assert "bootstrap-daily-bars" in payload["jobs"]
    datetime.fromisoformat(payload["time"]).astimezone(UTC)


def test_sync_daily_job_prints_result_list(monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]) -> None:
    orchestrator = FakeOrchestrator()
    monkeypatch.setattr("stock_collector.jobs.build_orchestrator", lambda **_: orchestrator)
    monkeypatch.setattr("sys.argv", ["jobs.py", "sync-daily", "--symbols", "600000"])

    main()

    payload = json.loads(capsys.readouterr().out)
    assert [item["dataset"] for item in payload] == ["raw_stocks", "raw_daily_bars"]
    assert orchestrator.called_with["symbols"] == ["600000"]


def test_sync_daily_missing_job_uses_missing_codes(monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]) -> None:
    orchestrator = FakeOrchestrator()
    monkeypatch.setattr("stock_collector.jobs.build_orchestrator", lambda **_: orchestrator)
    monkeypatch.setattr("sys.argv", ["jobs.py", "sync-daily-missing", "--trade-date", "2026-06-23"])

    main()

    payload = json.loads(capsys.readouterr().out)
    assert [item["dataset"] for item in payload] == ["raw_stocks", "raw_daily_bars"]
    assert orchestrator.called_with["symbols"] == ["000001"]


def test_symbol_jobs_fail_fast_before_building_orchestrator(monkeypatch: pytest.MonkeyPatch) -> None:
    build_calls = 0

    def fail_if_called(**_: object) -> FakeOrchestrator:
        nonlocal build_calls
        build_calls += 1
        return FakeOrchestrator()

    monkeypatch.setattr("stock_collector.jobs.build_orchestrator", fail_if_called)
    monkeypatch.setattr("sys.argv", ["jobs.py", "bootstrap-daily-bars"])

    with pytest.raises(SystemExit, match="At least one stock code is required"):
        main()

    assert build_calls == 0


def test_sync_financials_passes_limit_symbols(monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]) -> None:
    orchestrator = FakeOrchestrator()
    monkeypatch.setattr("stock_collector.jobs.build_orchestrator", lambda **_: orchestrator)
    monkeypatch.setattr(
        "sys.argv",
        ["jobs.py", "sync-financials", "--symbols", "600000", "--limit-symbols", "1"],
    )

    main()

    payload = json.loads(capsys.readouterr().out)
    assert payload["dataset"] == "raw_financial_snapshots"
    assert orchestrator.called_with["limit_symbols"] == 1


def test_schedule_pass_runs_due_jobs_once_per_scope() -> None:
    orchestrator = FakeOrchestrator()
    settings = SchedulerSettings(
        timezone=ZoneInfo("Asia/Shanghai"),
        poll_interval_seconds=60,
        jobs=(
            ScheduledCollectorJob(
                name="sync-end-of-day-final",
                action="sync-daily",
                target_scope="sync-end-of-day-final-bars",
                run_time=time(15, 20),
                run_iso_weekdays={1},
            ),
            ScheduledCollectorJob(
                name="sync-end-of-day-retry-1600",
                action="sync-daily",
                target_scope="sync-end-of-day-retry-1600",
                run_time=time(16, 0),
                run_iso_weekdays={1},
                skip_when_trade_date_current=True,
            ),
            ScheduledCollectorJob(
                name="sync-end-of-day-retry-1800",
                action="sync-daily",
                target_scope="sync-end-of-day-retry-1800",
                run_time=time(18, 0),
                run_iso_weekdays={1},
                skip_when_trade_date_current=True,
            ),
            ScheduledCollectorJob(
                name="sync-financials",
                action="sync-financials",
                target_scope="sync-financials",
                run_time=time(21, 0),
                run_iso_weekdays={1},
            ),
        ),
    )

    now = datetime(2026, 6, 22, 21, 30, tzinfo=ZoneInfo("Asia/Shanghai"))
    outcomes = run_schedule_pass(orchestrator, settings=settings, now=now)

    assert [item["job"] for item in outcomes] == ["sync-end-of-day-final", "sync-end-of-day-retry-1600", "sync-end-of-day-retry-1800", "sync-financials"]
    assert orchestrator.called_with["job"] == "sync-financials"
    assert orchestrator.called_with["symbols"] == ["600000", "000001"]


def test_schedule_pass_skips_scope_that_already_logged_since_window_start() -> None:
    orchestrator = FakeOrchestrator()
    orchestrator._writer.logged_scopes = {
        "sync-end-of-day-final-bars": [datetime(2026, 6, 22, 15, 21, tzinfo=ZoneInfo("Asia/Shanghai"))],
    }
    settings = SchedulerSettings(
        timezone=ZoneInfo("Asia/Shanghai"),
        poll_interval_seconds=60,
        jobs=(
            ScheduledCollectorJob(
                name="sync-end-of-day-final",
                action="sync-daily",
                target_scope="sync-end-of-day-final-bars",
                run_time=time(15, 20),
                run_iso_weekdays={1},
            ),
            ScheduledCollectorJob(
                name="sync-end-of-day-retry-1600",
                action="sync-daily",
                target_scope="sync-end-of-day-retry-1600",
                run_time=time(16, 0),
                run_iso_weekdays={1},
                skip_when_trade_date_current=True,
            ),
        ),
    )

    now = datetime(2026, 6, 22, 19, 0, tzinfo=ZoneInfo("Asia/Shanghai"))
    outcomes = run_schedule_pass(orchestrator, settings=settings, now=now)

    assert [item["job"] for item in outcomes] == ["sync-end-of-day-retry-1600"]
    assert orchestrator.called_with["job"] == "sync-daily"


def test_schedule_pass_skips_job_when_advisory_lock_is_held() -> None:
    orchestrator = FakeOrchestrator()
    lock_name = "collector:sync-end-of-day-final:2026-06-22:1520"
    orchestrator._writer.locked_names.add(lock_name)
    settings = SchedulerSettings(
        timezone=ZoneInfo("Asia/Shanghai"),
        poll_interval_seconds=60,
        jobs=(
            ScheduledCollectorJob(
                name="sync-end-of-day-final",
                action="sync-daily",
                target_scope="sync-end-of-day-final-bars",
                run_time=time(15, 20),
                run_iso_weekdays={1},
            ),
        ),
    )

    now = datetime(2026, 6, 22, 19, 0, tzinfo=ZoneInfo("Asia/Shanghai"))
    outcomes = run_schedule_pass(orchestrator, settings=settings, now=now)

    assert outcomes == [
        {
            "job": "sync-end-of-day-final",
            "status": "skipped",
            "reason": "job_locked",
            "scheduled_at": datetime(2026, 6, 22, 15, 20, tzinfo=ZoneInfo("Asia/Shanghai")),
            "started_at": now,
            "finished_at": now,
            "duration_seconds": 0.0,
            "evaluated_at": now,
        }
    ]
    assert "job" not in orchestrator.called_with


def test_schedule_pass_records_failed_job_and_continues() -> None:
    orchestrator = FailingDailyOrchestrator()
    settings = SchedulerSettings(
        timezone=ZoneInfo("Asia/Shanghai"),
        poll_interval_seconds=60,
        jobs=(
            ScheduledCollectorJob(
                name="sync-end-of-day-final",
                action="sync-daily",
                target_scope="sync-end-of-day-final-bars",
                run_time=time(15, 20),
                run_iso_weekdays={1},
            ),
            ScheduledCollectorJob(
                name="sync-financials",
                action="sync-financials",
                target_scope="sync-financials",
                run_time=time(15, 20),
                run_iso_weekdays={1},
            ),
        ),
    )

    now = datetime(2026, 6, 22, 16, 0, tzinfo=ZoneInfo("Asia/Shanghai"))
    outcomes = run_schedule_pass(orchestrator, settings=settings, now=now)

    assert outcomes[0]["job"] == "sync-end-of-day-final"
    assert outcomes[0]["status"] == "failed"
    assert outcomes[0]["reason"] == "job_execution_exception"
    assert "upstream html payload" in outcomes[0]["error"]
    assert outcomes[1]["job"] == "sync-financials"
    assert outcomes[1]["status"] == "executed"
    assert orchestrator._writer.released_names == [
        "collector:sync-end-of-day-final:2026-06-22:1520",
        "collector:sync-financials:2026-06-22:1520",
    ]


def test_schedule_pass_marks_scope_as_logged_after_execution() -> None:
    orchestrator = FakeOrchestrator()
    settings = SchedulerSettings(
        timezone=ZoneInfo("Asia/Shanghai"),
        poll_interval_seconds=60,
        jobs=(
            ScheduledCollectorJob(
                name="sync-end-of-day-final",
                action="sync-daily",
                target_scope="sync-end-of-day-final-bars",
                run_time=time(15, 20),
                run_iso_weekdays={1},
            ),
        ),
    )

    now = datetime(2026, 6, 22, 16, 0, tzinfo=ZoneInfo("Asia/Shanghai"))
    first_outcomes = run_schedule_pass(orchestrator, settings=settings, now=now)
    second_outcomes = run_schedule_pass(orchestrator, settings=settings, now=now)

    assert len(first_outcomes) == 1
    assert second_outcomes == []


def test_schedule_pass_uses_utc_threshold_when_checking_schedule_marker() -> None:
    orchestrator = FakeOrchestrator()
    orchestrator._writer.logged_scopes = {
        "sync-end-of-day-final-bars": [datetime(2026, 6, 25, 10, 35, tzinfo=UTC)],
    }
    settings = SchedulerSettings(
        timezone=ZoneInfo("Asia/Shanghai"),
        poll_interval_seconds=60,
        jobs=(
            ScheduledCollectorJob(
                name="sync-end-of-day-final",
                action="sync-daily",
                target_scope="sync-end-of-day-final-bars",
                run_time=time(16, 30),
                run_iso_weekdays={4},
            ),
        ),
    )

    now = datetime(2026, 6, 25, 18, 0, tzinfo=ZoneInfo("Asia/Shanghai"))
    outcomes = run_schedule_pass(orchestrator, settings=settings, now=now)

    assert outcomes == []


def test_build_schedule_batch_id_keeps_value_within_column_limit() -> None:
    scheduled_at = datetime(2026, 6, 26, 18, 35, tzinfo=ZoneInfo("Asia/Shanghai"))
    job = ScheduledCollectorJob(
        name="backfill-candidate-industries-retry",
        action="backfill-candidate-industries",
        target_scope="backfill-candidate-industries-retry",
        run_time=time(18, 35),
        run_iso_weekdays={1, 2, 3, 4, 5},
    )

    batch_id = _build_schedule_batch_id(job=job, scheduled_at=scheduled_at)

    assert len(batch_id) <= 64
    assert batch_id.startswith("schedule:")
    assert "20260626T183500+0800" in batch_id


def test_schedule_pass_skips_retry_job_when_trade_date_is_current() -> None:
    orchestrator = FakeOrchestrator()

    def get_latest_trade_date() -> date | None:
        return date(2026, 6, 22)

    orchestrator._writer.get_latest_trade_date = get_latest_trade_date  # type: ignore[method-assign]
    orchestrator._writer.trade_date_fully_collected = True
    settings = SchedulerSettings(
        timezone=ZoneInfo("Asia/Shanghai"),
        poll_interval_seconds=60,
        jobs=(
            ScheduledCollectorJob(
                name="sync-end-of-day-retry-1600",
                action="sync-daily",
                target_scope="sync-end-of-day-retry-1600",
                run_time=time(16, 0),
                run_iso_weekdays={1},
                skip_when_trade_date_current=True,
            ),
        ),
    )

    now = datetime(2026, 6, 22, 16, 10, tzinfo=ZoneInfo("Asia/Shanghai"))
    outcomes = run_schedule_pass(orchestrator, settings=settings, now=now)

    assert outcomes == []
    assert "job" not in orchestrator.called_with


def test_schedule_pass_keeps_retry_job_when_trade_date_current_but_not_fully_collected() -> None:
    orchestrator = FakeOrchestrator()

    def get_latest_trade_date() -> date | None:
        return date(2026, 6, 22)

    orchestrator._writer.get_latest_trade_date = get_latest_trade_date  # type: ignore[method-assign]
    orchestrator._writer.trade_date_fully_collected = False
    settings = SchedulerSettings(
        timezone=ZoneInfo("Asia/Shanghai"),
        poll_interval_seconds=60,
        jobs=(
            ScheduledCollectorJob(
                name="sync-end-of-day-retry-1600",
                action="sync-daily",
                target_scope="sync-end-of-day-retry-1600",
                run_time=time(16, 0),
                run_iso_weekdays={1},
                skip_when_trade_date_current=True,
            ),
        ),
    )

    now = datetime(2026, 6, 22, 16, 10, tzinfo=ZoneInfo("Asia/Shanghai"))
    outcomes = run_schedule_pass(orchestrator, settings=settings, now=now)

    assert [item["job"] for item in outcomes] == ["sync-end-of-day-retry-1600"]
    assert orchestrator.called_with["job"] == "sync-daily"


def test_truncate_log_error_message_limits_length() -> None:
    text = "x" * 1500

    truncated = _truncate_log_error_message(text)

    assert truncated is not None
    assert len(truncated) == 1024

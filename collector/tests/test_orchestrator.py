from datetime import UTC, date, datetime

from sqlalchemy import select

from stock_collector.akshare_client import AkshareClient
from stock_collector.mysql_writer import RawDataWriter
from stock_collector.mysql_writer import create_in_memory_engine
from stock_collector.mysql_writer import data_ingestion_logs_table
from stock_collector.mysql_writer import raw_daily_bars_table
from stock_collector.mysql_writer import raw_financial_snapshots_table
from stock_collector.mysql_writer import raw_industry_daily_stats_table
from stock_collector.mysql_writer import raw_market_index_bars_table
from stock_collector.mysql_writer import raw_stocks_table
from stock_collector.orchestrator import CollectionWindow
from stock_collector.orchestrator import CollectorOrchestrator


class FakeProvider:
    def __init__(self) -> None:
        self.daily_bar_attempts: dict[str, int] = {}

    def stock_zh_a_spot(self):
        return [
            {"代码": "600000", "名称": "浦发银行", "所处行业": "银行", "市盈率TTM": "6.82", "市净率": "0.58"},
            {"代码": "000001", "名称": "平安银行", "所处行业": "银行"},
        ]

    def stock_zh_a_daily(self, symbol, start_date, end_date, adjust):
        self.daily_bar_attempts[symbol] = self.daily_bar_attempts.get(symbol, 0) + 1
        if symbol == "sz000001":
            return []
        if symbol == "sz000002" and self.daily_bar_attempts[symbol] < 2:
            raise RuntimeError("timeout")
        if symbol == "sz000003":
            raise RuntimeError("forbidden anti bot")
        return [
            {
                "date": "2026-06-16",
                "open": 10.0,
                "high": 10.5,
                "low": 9.8,
                "close": 10.2,
                "volume": 1000,
                "amount": 5000,
                "turnover": 1.1,
            }
        ]

    def stock_zh_index_daily(self, symbol):
        if symbol == "sz399006":
            return []
        return [{"日期": "2026-06-16", "开盘": "100", "最高": "101", "最低": "99", "收盘": "100.5"}]

    def stock_financial_abstract_ths(self, symbol, indicator):
        assert indicator == "按报告期"
        if symbol == "000001":
            return []
        return [{"报告期": "2025-12-31", "净利润同比增长率": "12.5%", "营业总收入同比增长率": "8.1%", "净资产收益率-摊薄": "9.2%"}]

    def stock_board_industry_name_ths(self):
        return [{"name": "半导体", "code": "881121"}]

    def stock_board_industry_index_ths(self, symbol, start_date, end_date):
        assert symbol == "半导体"
        return [{"日期": "2026-06-16", "收盘价": 100}, {"日期": "2026-06-17", "收盘价": 110}]


def build_orchestrator():
    writer = RawDataWriter(create_in_memory_engine())
    writer.create_tables()
    sleeps: list[float] = []
    fake_now = datetime(2026, 6, 17, tzinfo=UTC)
    orchestrator = CollectorOrchestrator(
        AkshareClient(provider=FakeProvider()),
        writer,
        sleep_fn=lambda seconds: sleeps.append(seconds),
        now_fn=lambda: fake_now,
    )
    return orchestrator, writer, sleeps


def test_collect_stock_snapshot_writes_raw_rows() -> None:
    orchestrator, writer, _ = build_orchestrator()

    result = orchestrator.collect_stock_snapshot(
        trade_date=date(2026, 6, 16),
        is_incremental=True,
        target_scope="sync-daily-stocks",
    )

    assert result.rows_written == 2
    with writer.engine.connect() as connection:
        rows = connection.execute(select(raw_stocks_table.c.stock_code, raw_stocks_table.c.pe, raw_stocks_table.c.pb)).all()
    assert len(rows) == 2
    assert rows[0][1] is not None
    assert rows[0][2] is not None


def test_bootstrap_daily_bars_uses_checkpoints_and_marks_partial_when_coverage_low() -> None:
    orchestrator, writer, sleeps = build_orchestrator()

    result = orchestrator.bootstrap_daily_bars(["600000", "000001"])

    assert result.is_signal_eligible is False
    assert "daily_bar_coverage" in result.completeness_reasons
    assert 0.6 in sleeps

    checkpoints = writer.load_checkpoints("bootstrap-daily-bars")
    assert checkpoints["600000"]["status"] == "success"
    assert checkpoints["000001"]["status"] == "skipped"

    with writer.engine.connect() as connection:
        rows = connection.execute(select(raw_daily_bars_table.c.stock_code)).all()
    assert len(rows) == 1


def test_retry_failed_retries_only_failed_symbols() -> None:
    orchestrator, writer, sleeps = build_orchestrator()

    writer.upsert_checkpoint(
        {
            "job_type": "bootstrap-daily-bars",
            "batch_id": "old-batch",
            "stock_code": "000002",
            "status": "failed",
            "last_success_date": None,
            "retry_count": 3,
            "error_message": "timeout",
            "updated_at": datetime(2026, 6, 17, tzinfo=UTC),
            "created_at": datetime(2026, 6, 17, tzinfo=UTC),
        }
    )

    result = orchestrator.retry_failed(["000002"])

    assert result.rows_written == 1
    assert 5 in sleeps
    checkpoints = writer.load_checkpoints("bootstrap-daily-bars")
    assert checkpoints["000002"]["status"] == "success"


def test_bootstrap_daily_bars_treats_existing_success_checkpoints_as_covered() -> None:
    orchestrator, writer, _ = build_orchestrator()

    writer.upsert_checkpoint(
        {
            "job_type": "bootstrap-daily-bars",
            "batch_id": "old-batch",
            "stock_code": "600000",
            "status": "success",
            "last_success_date": date(2026, 6, 16),
            "retry_count": 0,
            "error_message": None,
            "updated_at": datetime(2026, 6, 17, tzinfo=UTC),
            "created_at": datetime(2026, 6, 17, tzinfo=UTC),
        }
    )

    result = orchestrator.bootstrap_daily_bars(["600000"])

    assert result.rows_written == 0
    assert result.log_status == "success"
    assert result.is_complete is True
    assert result.is_signal_eligible is True
    assert result.completeness_reasons == []


def test_incremental_daily_bars_does_not_reuse_previous_trade_date_success_checkpoint() -> None:
    class CurrentTradeDateProvider(FakeProvider):
        def stock_zh_a_hist(self, symbol, period, start_date, end_date, adjust):
            return [
                {
                    "date": "2026-06-17",
                    "open": 10.0,
                    "high": 10.5,
                    "low": 9.8,
                    "close": 10.2,
                    "volume": 1000,
                    "amount": 5000,
                    "turnover": 1.1,
                }
            ]

    writer = RawDataWriter(create_in_memory_engine())
    writer.create_tables()
    orchestrator = CollectorOrchestrator(
        AkshareClient(provider=CurrentTradeDateProvider()),
        writer,
        sleep_fn=lambda seconds: None,
        now_fn=lambda: datetime(2026, 6, 17, tzinfo=UTC),
    )

    writer.upsert_checkpoint(
        {
            "job_type": "sync-daily-bars",
            "batch_id": "old-batch",
            "stock_code": "600000",
            "status": "success",
            "last_success_date": date(2026, 6, 16),
            "retry_count": 0,
            "error_message": None,
            "updated_at": datetime(2026, 6, 17, tzinfo=UTC),
            "created_at": datetime(2026, 6, 17, tzinfo=UTC),
        }
    )

    result = orchestrator._collect_daily_bars(  # noqa: SLF001
        ["600000"],
        window=CollectionWindow(start_date=date(2026, 6, 17), end_date=date(2026, 6, 17)),
        adjust_type="qfq",
        mode="incremental",
        target_scope="sync-daily-bars",
        stock_snapshot_refreshed=True,
        missing_market_indices=[],
        industry_missing_rate=0.0,
    )

    assert result.rows_written == 1


def test_bootstrap_daily_bars_treats_existing_skipped_checkpoints_as_terminal() -> None:
    orchestrator, writer, _ = build_orchestrator()

    writer.upsert_checkpoint(
        {
            "job_type": "bootstrap-daily-bars",
            "batch_id": "old-batch",
            "stock_code": "000001",
            "status": "skipped",
            "last_success_date": None,
            "retry_count": 0,
            "error_message": "empty_payload",
            "updated_at": datetime(2026, 6, 17, tzinfo=UTC),
            "created_at": datetime(2026, 6, 17, tzinfo=UTC),
        }
    )

    result = orchestrator.bootstrap_daily_bars(["000001"])

    assert result.rows_written == 0
    assert result.log_status == "success"
    assert result.is_complete is True
    assert result.is_signal_eligible is True
    assert result.completeness_reasons == []


def test_bootstrap_daily_bars_does_not_mark_success_checkpoint_when_row_write_fails() -> None:
    orchestrator, writer, _ = build_orchestrator()
    original_upsert_rows = writer.upsert_rows

    def failing_upsert_rows(table_name, rows):  # type: ignore[no-untyped-def]
        if table_name == "raw_daily_bars":
            raise RuntimeError("db write failed")
        return original_upsert_rows(table_name, rows)

    writer.upsert_rows = failing_upsert_rows  # type: ignore[method-assign]

    try:
        orchestrator.bootstrap_daily_bars(["600000"])
    except RuntimeError as exc:
        assert str(exc) == "db write failed"
    else:
        raise AssertionError("expected bootstrap_daily_bars to fail")

    checkpoints = writer.load_checkpoints("bootstrap-daily-bars")
    assert "600000" not in checkpoints


def test_incremental_daily_bars_flushes_each_batch_to_database() -> None:
    orchestrator, writer, _ = build_orchestrator()
    original_upsert_rows = writer.upsert_rows
    batch_row_counts: list[int] = []

    def tracking_upsert_rows(table_name, rows):  # type: ignore[no-untyped-def]
        if table_name == "raw_daily_bars":
            batch_row_counts.append(len(rows))
        return original_upsert_rows(table_name, rows)

    writer.upsert_rows = tracking_upsert_rows  # type: ignore[method-assign]
    stock_codes = [str(600100 + index) for index in range(101)]

    result = orchestrator._collect_daily_bars(  # noqa: SLF001
        stock_codes,
        window=CollectionWindow(start_date=date(2026, 6, 16), end_date=date(2026, 6, 16)),
        adjust_type="qfq",
        mode="incremental",
        target_scope="sync-daily-bars",
        stock_snapshot_refreshed=True,
        missing_market_indices=[],
        industry_missing_rate=0.0,
    )

    assert result.rows_written == 101
    assert batch_row_counts == [1] * 101
    with writer.engine.connect() as connection:
        rows = connection.execute(select(raw_daily_bars_table.c.stock_code)).all()
    assert len(rows) == 101


def test_sync_daily_market_marks_missing_indices_in_completeness() -> None:
    orchestrator, _, _ = build_orchestrator()

    results = orchestrator.sync_daily_market(["600000"], trade_date=date(2026, 6, 16))
    daily_result = next(item for item in results if item.dataset == "raw_daily_bars")

    assert daily_result.is_signal_eligible is False
    assert "market_indices" in daily_result.completeness_reasons


def test_circuit_breaker_opens_after_many_failed_symbols() -> None:
    orchestrator, writer, _ = build_orchestrator()

    result = orchestrator.bootstrap_daily_bars(["000003"] * 20)

    assert result.circuit_breaker_opened is True
    with writer.engine.connect() as connection:
        log = connection.execute(select(data_ingestion_logs_table.c.circuit_breaker_opened)).scalar_one()
    assert log is True


def test_collect_index_and_industry_rows_are_persisted() -> None:
    orchestrator, writer, _ = build_orchestrator()

    orchestrator.collect_index_bars(
        window=type("Window", (), {"start_date": date(2026, 6, 16), "end_date": date(2026, 6, 16)})(),
        is_incremental=True,
        target_scope="sync-daily-indices",
    )
    orchestrator.collect_industry_stats(
        trade_date=date(2026, 6, 17),
        is_incremental=True,
        target_scope="sync-daily-industries",
    )

    with writer.engine.connect() as connection:
        index_rows = connection.execute(select(raw_market_index_bars_table.c.index_code)).all()
        industry_rows = connection.execute(select(raw_industry_daily_stats_table.c.industry_code)).all()
    assert len(index_rows) == 2
    assert len(industry_rows) == 1


def test_sync_financials_writes_last_12_periods_subset() -> None:
    orchestrator, writer, sleeps = build_orchestrator()

    result = orchestrator.sync_financials(["600000"], limit_symbols=1)

    assert result.rows_written == 1
    assert any(seconds >= 0.8 for seconds in sleeps)
    with writer.engine.connect() as connection:
        rows = connection.execute(select(raw_financial_snapshots_table.c.stock_code)).all()
    assert rows[0][0] == "600000"


def test_sync_financials_marks_empty_payload_as_partial() -> None:
    class EmptyFinancialProvider(FakeProvider):
        def stock_financial_abstract_ths(self, symbol, indicator):
            return []

        def stock_financial_report_sina(self, stock, symbol):
            return []

    writer = RawDataWriter(create_in_memory_engine())
    writer.create_tables()
    orchestrator = CollectorOrchestrator(
        AkshareClient(provider=EmptyFinancialProvider()),
        writer,
        sleep_fn=lambda seconds: None,
        now_fn=lambda: datetime(2026, 6, 17, tzinfo=UTC),
    )

    result = orchestrator.sync_financials(["600000"], limit_symbols=1)

    assert result.rows_written == 0
    assert result.log_status == "partial"
    assert result.is_complete is False
    assert result.is_signal_eligible is False
    checkpoints = writer.load_checkpoints("sync-financials")
    assert checkpoints["600000"]["status"] == "skipped"


class IncrementalTradeDateMismatchProvider:
    def stock_zh_a_spot(self):
        return [{"code": "600000", "name": "示例"}]

    def stock_zh_index_daily(self, symbol):
        return [{"date": "2026-06-16", "close": "100"}]

    def stock_board_industry_name_ths(self):
        return [{"name": "半导体", "code": "881121"}]

    def stock_board_industry_index_ths(self, symbol, start_date, end_date):
        return [{"date": "2026-06-16", "close": 100}, {"date": "2026-06-17", "close": 110}]

    def stock_zh_a_hist(self, symbol, period, start_date, end_date, adjust):
        return [{"date": "2026-06-16", "close": "10.2"}]


def test_sync_daily_market_filters_previous_trade_date_rows() -> None:
    writer = RawDataWriter(create_in_memory_engine())
    writer.create_tables()
    orchestrator = CollectorOrchestrator(
        AkshareClient(provider=IncrementalTradeDateMismatchProvider()),
        writer,
        sleep_fn=lambda seconds: None,
        now_fn=lambda: datetime(2026, 6, 17, tzinfo=UTC),
    )

    results = orchestrator.sync_daily_market(["600000"], trade_date=date(2026, 6, 17))

    daily_result = next(item for item in results if item.dataset == "raw_daily_bars")
    industry_result = next(item for item in results if item.dataset == "raw_industry_daily_stats")
    index_result = next(item for item in results if item.dataset == "raw_market_index_bars")

    assert daily_result.rows_written == 0
    assert industry_result.rows_written == 1
    assert index_result.rows_written == 0

    checkpoints = writer.load_checkpoints("sync-daily-bars")
    assert checkpoints["600000"]["status"] == "skipped"
    assert checkpoints["600000"]["error_message"] == "empty_payload"

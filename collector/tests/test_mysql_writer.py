from datetime import UTC, date, datetime
from decimal import Decimal
import math

from sqlalchemy import inspect
from sqlalchemy import select

from stock_collector.mysql_writer import RawDataWriter
from stock_collector.mysql_writer import collector_checkpoints_table
from stock_collector.mysql_writer import create_in_memory_engine
from stock_collector.mysql_writer import data_ingestion_logs_table
from stock_collector.mysql_writer import latest_raw_stocks_table
from stock_collector.mysql_writer import raw_daily_bars_table
from stock_collector.mysql_writer import raw_stocks_table


def test_raw_data_writer_creates_tables_with_new_columns() -> None:
    writer = RawDataWriter(create_in_memory_engine())
    writer.create_tables()

    inspector = inspect(writer.engine)
    columns = {column["name"] for column in inspector.get_columns("raw_daily_bars")}
    assert "raw_payload" in columns
    assert "created_at" in columns

    stock_columns = {column["name"] for column in inspector.get_columns("raw_stocks")}
    assert "pe" in stock_columns
    assert "pb" in stock_columns
    assert "turnover_rate" in stock_columns

    log_columns = {column["name"] for column in inspector.get_columns("data_ingestion_logs")}
    assert "target_scope" in log_columns
    assert "is_signal_eligible" in log_columns
    assert "circuit_breaker_opened" in log_columns


def test_raw_data_writer_inserts_rows_without_explicit_id() -> None:
    writer = RawDataWriter(create_in_memory_engine())
    writer.create_tables()

    count = writer.upsert_rows(
        "raw_daily_bars",
        [
            {
                "stock_code": "600000",
                "trade_date": date(2026, 6, 16),
                "open": Decimal("10"),
                "high": Decimal("11"),
                "low": Decimal("9"),
                "close": Decimal("10.5"),
                "volume": 1000,
                "amount": Decimal("5000"),
                "amplitude": None,
                "pct_change": Decimal("1.2"),
                "turnover_rate": None,
                "adjust_type": "qfq",
                "source_name": "akshare",
                "interface_name": "stock_zh_a_daily",
                "fetched_at": datetime(2026, 6, 17, tzinfo=UTC),
                "batch_id": "batch1",
                "is_incremental": True,
                "payload_hash": "hash1",
                "retry_count": 0,
                "missing_field_count": 0,
                "ingestion_status": "success",
                "error_message": None,
                "raw_payload": {"code": "600000"},
                "created_at": datetime(2026, 6, 17, tzinfo=UTC),
            }
        ],
    )

    assert count == 1

    with writer.engine.connect() as connection:
        row = connection.execute(select(raw_daily_bars_table.c.stock_code)).scalar_one()
    assert row == "600000"


def test_raw_data_writer_serializes_json_payload_dates() -> None:
    writer = RawDataWriter(create_in_memory_engine())
    writer.create_tables()

    writer.upsert_rows(
        "raw_daily_bars",
        [
            {
                "stock_code": "600001",
                "trade_date": date(2026, 6, 16),
                "open": Decimal("10"),
                "high": Decimal("11"),
                "low": Decimal("9"),
                "close": Decimal("10.5"),
                "volume": 1000,
                "amount": Decimal("5000"),
                "amplitude": None,
                "pct_change": Decimal("1.2"),
                "turnover_rate": None,
                "adjust_type": "qfq",
                "source_name": "akshare",
                "interface_name": "stock_zh_a_daily",
                "fetched_at": datetime(2026, 6, 17, tzinfo=UTC),
                "batch_id": "batch-json",
                "is_incremental": True,
                "payload_hash": "hash-json",
                "retry_count": 0,
                "missing_field_count": 0,
                "ingestion_status": "success",
                "error_message": None,
                "raw_payload": {"date": date(2026, 6, 16), "nested": {"value": Decimal("1.23")}},
                "created_at": datetime(2026, 6, 17, tzinfo=UTC),
            }
        ],
    )

    with writer.engine.connect() as connection:
        row = connection.execute(select(raw_daily_bars_table.c.raw_payload)).scalar_one()
    assert row["date"] == "2026-06-16"
    assert row["nested"]["value"] == "1.23"


def test_raw_data_writer_serializes_nan_in_json_payload() -> None:
    writer = RawDataWriter(create_in_memory_engine())
    writer.create_tables()

    writer.upsert_rows(
        "raw_daily_bars",
        [
            {
                "stock_code": "600002",
                "trade_date": date(2026, 6, 16),
                "open": Decimal("10"),
                "high": Decimal("11"),
                "low": Decimal("9"),
                "close": Decimal("10.5"),
                "volume": 1000,
                "amount": Decimal("5000"),
                "amplitude": None,
                "pct_change": Decimal("1.2"),
                "turnover_rate": None,
                "adjust_type": "qfq",
                "source_name": "akshare",
                "interface_name": "stock_zh_a_daily",
                "fetched_at": datetime(2026, 6, 17, tzinfo=UTC),
                "batch_id": "batch-nan",
                "is_incremental": True,
                "payload_hash": "hash-nan",
                "retry_count": 0,
                "missing_field_count": 0,
                "ingestion_status": "success",
                "error_message": None,
                "raw_payload": {"value": math.nan},
                "created_at": datetime(2026, 6, 17, tzinfo=UTC),
            }
        ],
    )

    with writer.engine.connect() as connection:
        row = connection.execute(select(raw_daily_bars_table.c.raw_payload)).scalar_one()
    assert row["value"] is None


def test_raw_data_writer_appends_ingestion_log() -> None:
    writer = RawDataWriter(create_in_memory_engine())
    writer.create_tables()

    writer.append_log(
        {
            "batch_id": "batch1",
            "source_name": "akshare",
            "interface_name": "stock_zh_a_daily",
            "target_scope": "bootstrap-daily-bars",
            "trade_date": date(2026, 6, 17),
            "report_date": None,
            "started_at": datetime(2026, 6, 17, tzinfo=UTC),
            "finished_at": datetime(2026, 6, 17, 1, tzinfo=UTC),
            "success_count": 1,
            "failure_count": 0,
            "missing_field_count": 0,
            "consecutive_failure_count": 0,
            "window_failure_rate": 0.0,
            "is_incremental": True,
            "is_complete": True,
            "is_signal_eligible": True,
            "circuit_breaker_opened": False,
            "error_message": None,
            "created_at": datetime(2026, 6, 17, tzinfo=UTC),
        }
    )

    with writer.engine.connect() as connection:
        row = connection.execute(select(data_ingestion_logs_table.c.target_scope)).scalar_one()
    assert row == "bootstrap-daily-bars"


def test_raw_data_writer_upserts_checkpoints() -> None:
    writer = RawDataWriter(create_in_memory_engine())
    writer.create_tables()

    writer.upsert_checkpoint(
        {
            "job_type": "bootstrap-daily-bars",
            "batch_id": "batch1",
            "stock_code": "600000",
            "status": "failed",
            "last_success_date": None,
            "retry_count": 2,
            "error_message": "timeout",
            "updated_at": datetime(2026, 6, 17, tzinfo=UTC),
            "created_at": datetime(2026, 6, 17, tzinfo=UTC),
        }
    )
    writer.upsert_checkpoint(
        {
            "job_type": "bootstrap-daily-bars",
            "batch_id": "batch2",
            "stock_code": "600000",
            "status": "success",
            "last_success_date": date(2026, 6, 16),
            "retry_count": 0,
            "error_message": None,
            "updated_at": datetime(2026, 6, 18, tzinfo=UTC),
            "created_at": datetime(2026, 6, 18, tzinfo=UTC),
        }
    )

    checkpoints = writer.load_checkpoints("bootstrap-daily-bars")
    assert checkpoints["600000"]["status"] == "success"

    with writer.engine.connect() as connection:
        count = connection.execute(select(collector_checkpoints_table.c.stock_code)).all()
    assert len(count) == 1


def test_raw_data_writer_lists_numeric_active_stock_codes_only() -> None:
    writer = RawDataWriter(create_in_memory_engine())
    writer.create_tables()

    writer.upsert_rows(
        "raw_stocks",
        [
            {
                "stock_code": "600000",
                "stock_name": "A",
                "market": "SH",
                "industry_name": None,
                "list_date": None,
                "is_st": False,
                "is_delisting_risk": False,
                "is_active": True,
                "source_name": "akshare",
                "interface_name": "stock_zh_a_spot",
                "fetched_at": datetime(2026, 6, 17, tzinfo=UTC),
                "batch_id": "batch1",
                "payload_hash": "hash1",
                "raw_payload": None,
                "created_at": datetime(2026, 6, 17, tzinfo=UTC),
            },
            {
                "stock_code": "bj920992",
                "stock_name": "B",
                "market": "BJ",
                "industry_name": None,
                "list_date": None,
                "is_st": False,
                "is_delisting_risk": False,
                "is_active": True,
                "source_name": "akshare",
                "interface_name": "stock_zh_a_spot",
                "fetched_at": datetime(2026, 6, 17, tzinfo=UTC),
                "batch_id": "batch1",
                "payload_hash": "hash2",
                "raw_payload": None,
                "created_at": datetime(2026, 6, 17, tzinfo=UTC),
            },
            {
                "stock_code": "000001",
                "stock_name": "C",
                "market": "SZ",
                "industry_name": None,
                "list_date": None,
                "is_st": False,
                "is_delisting_risk": False,
                "is_active": False,
                "source_name": "akshare",
                "interface_name": "stock_zh_a_spot",
                "fetched_at": datetime(2026, 6, 17, tzinfo=UTC),
                "batch_id": "batch1",
                "payload_hash": "hash3",
                "raw_payload": None,
                "created_at": datetime(2026, 6, 17, tzinfo=UTC),
            },
        ],
    )

    assert writer.list_stock_codes() == ["600000"]
    assert writer.list_stock_codes(active_only=False) == ["000001", "600000"]


def test_raw_data_writer_lists_missing_daily_bar_stock_codes_for_trade_date() -> None:
    writer = RawDataWriter(create_in_memory_engine())
    writer.create_tables()

    fetched_at = datetime(2026, 6, 23, tzinfo=UTC)
    writer.upsert_rows(
        "raw_stocks",
        [
            {
                "stock_code": "000001",
                "stock_name": "A",
                "market": "SZ",
                "industry_name": None,
                "list_date": None,
                "is_st": False,
                "is_delisting_risk": False,
                "is_active": True,
                "source_name": "akshare",
                "interface_name": "stock_zh_a_spot",
                "fetched_at": fetched_at,
                "batch_id": "batch1",
                "payload_hash": "hash1",
                "raw_payload": None,
                "created_at": fetched_at,
            },
            {
                "stock_code": "000002",
                "stock_name": "B",
                "market": "SZ",
                "industry_name": None,
                "list_date": None,
                "is_st": False,
                "is_delisting_risk": False,
                "is_active": True,
                "source_name": "akshare",
                "interface_name": "stock_zh_a_spot",
                "fetched_at": fetched_at,
                "batch_id": "batch1",
                "payload_hash": "hash2",
                "raw_payload": None,
                "created_at": fetched_at,
            },
        ],
    )
    writer.upsert_rows(
        "raw_daily_bars",
        [
            {
                "stock_code": "000001",
                "trade_date": date(2026, 6, 23),
                "open": Decimal("10"),
                "high": Decimal("11"),
                "low": Decimal("9"),
                "close": Decimal("10.5"),
                "volume": 1000,
                "amount": Decimal("5000"),
                "amplitude": None,
                "pct_change": Decimal("1.2"),
                "turnover_rate": None,
                "adjust_type": "qfq",
                "source_name": "akshare",
                "interface_name": "stock_zh_a_hist",
                "fetched_at": fetched_at,
                "batch_id": "batch-bars",
                "is_incremental": True,
                "payload_hash": "hash-bars",
                "retry_count": 0,
                "missing_field_count": 0,
                "ingestion_status": "success",
                "error_message": None,
                "raw_payload": {"code": "000001"},
                "created_at": fetched_at,
            }
        ],
    )

    assert writer.list_missing_daily_bar_stock_codes(trade_date=date(2026, 6, 23)) == ["000002"]


def test_raw_data_writer_detects_logs_since_timestamp() -> None:
    writer = RawDataWriter(create_in_memory_engine())
    writer.create_tables()

    writer.append_log(
        {
            "batch_id": "batch2",
            "source_name": "akshare",
            "interface_name": "stock_zh_a_daily",
            "target_scope": "sync-daily-bars",
            "trade_date": date(2026, 6, 17),
            "report_date": None,
            "started_at": datetime(2026, 6, 17, 18, tzinfo=UTC),
            "finished_at": datetime(2026, 6, 17, 19, tzinfo=UTC),
            "success_count": 1,
            "failure_count": 0,
            "missing_field_count": 0,
            "consecutive_failure_count": 0,
            "window_failure_rate": 0.0,
            "is_incremental": True,
            "is_complete": True,
            "is_signal_eligible": True,
            "circuit_breaker_opened": False,
            "error_message": None,
            "created_at": datetime(2026, 6, 17, 19, tzinfo=UTC),
        }
    )

    assert writer.has_log_since("sync-daily-bars", created_at_or_after=datetime(2026, 6, 17, 0, tzinfo=UTC)) is True
    assert writer.has_log_since("sync-daily-bars", created_at_or_after=datetime(2026, 6, 18, 0, tzinfo=UTC)) is False


def test_update_stock_metadata_updates_latest_raw_stocks_too() -> None:
    writer = RawDataWriter(create_in_memory_engine())
    writer.create_tables()

    fetched_at = datetime(2026, 6, 25, 15, 30, tzinfo=UTC)
    writer.upsert_rows(
        "raw_stocks",
        [
            {
                "stock_code": "600000",
                "stock_name": "浦发银行",
                "market": "SH",
                "industry_name": None,
                "list_date": None,
                "is_st": False,
                "is_delisting_risk": False,
                "is_active": True,
                "source_name": "akshare",
                "interface_name": "stock_zh_a_spot",
                "fetched_at": fetched_at,
                "batch_id": "batch1",
                "payload_hash": "hash1",
                "raw_payload": None,
                "created_at": fetched_at,
            }
        ],
    )

    updated = writer.update_stock_metadata(
        "batch1",
        [
            {
                "stock_code": "600000",
                "stock_name": "浦发银行",
                "industry_name": "银行",
                "list_date": date(1999, 11, 10),
            }
        ],
    )

    assert updated == 1

    with writer.engine.connect() as connection:
        latest_row = connection.execute(
            select(
                latest_raw_stocks_table.c.stock_code,
                latest_raw_stocks_table.c.industry_name,
                latest_raw_stocks_table.c.list_date,
            ).where(latest_raw_stocks_table.c.stock_code == "600000")
        ).mappings().one()

    assert latest_row["industry_name"] == "银行"
    assert latest_row["list_date"] == date(1999, 11, 10)


def test_raw_data_writer_deduplicates_raw_stocks_within_same_batch() -> None:
    writer = RawDataWriter(create_in_memory_engine())
    writer.create_tables()

    fetched_at = datetime(2026, 6, 25, 15, 30, tzinfo=UTC)
    written = writer.upsert_rows(
        "raw_stocks",
        [
            {
                "stock_code": "002317",
                "stock_name": "众生药业",
                "market": "SZ",
                "industry_name": "C 制造业",
                "list_date": None,
                "is_st": False,
                "is_delisting_risk": False,
                "is_active": True,
                "source_name": "akshare",
                "interface_name": "stock_zh_a_spot",
                "fetched_at": fetched_at,
                "batch_id": "batch1",
                "payload_hash": "hash1",
                "raw_payload": {"industry_name": "C 制造业"},
                "created_at": fetched_at,
            },
            {
                "stock_code": "002317",
                "stock_name": "众生药业",
                "market": "SZ",
                "industry_name": "化学制品",
                "list_date": None,
                "is_st": False,
                "is_delisting_risk": False,
                "is_active": True,
                "source_name": "akshare",
                "interface_name": "stock_zh_a_spot",
                "fetched_at": fetched_at,
                "batch_id": "batch1",
                "payload_hash": "hash2",
                "raw_payload": {"industry_name": "化学制品"},
                "created_at": fetched_at,
            },
        ],
    )

    assert written == 1

    with writer.engine.connect() as connection:
        raw_rows = connection.execute(
            select(
                raw_stocks_table.c.stock_code,
                raw_stocks_table.c.industry_name,
                raw_stocks_table.c.payload_hash,
            ).where(raw_stocks_table.c.stock_code == "002317")
        ).mappings().all()
        latest_row = connection.execute(
            select(
                latest_raw_stocks_table.c.stock_code,
                latest_raw_stocks_table.c.industry_name,
                latest_raw_stocks_table.c.batch_id,
            ).where(latest_raw_stocks_table.c.stock_code == "002317")
        ).mappings().one()

    assert len(raw_rows) == 1
    assert raw_rows[0]["industry_name"] == "化学制品"
    assert raw_rows[0]["payload_hash"] == "hash2"
    assert latest_row["industry_name"] == "化学制品"
    assert latest_row["batch_id"] == "batch1"


def test_raw_stock_upsert_preserves_existing_latest_industry_when_new_value_is_missing() -> None:
    writer = RawDataWriter(create_in_memory_engine())
    writer.create_tables()

    fetched_at = datetime(2026, 6, 25, 15, 30, tzinfo=UTC)
    writer.upsert_rows(
        "raw_stocks",
        [
            {
                "stock_code": "002317",
                "stock_name": "众生药业",
                "market": "SZ",
                "industry_name": "化学制品",
                "list_date": None,
                "is_st": False,
                "is_delisting_risk": False,
                "is_active": True,
                "source_name": "akshare",
                "interface_name": "stock_zh_a_spot",
                "fetched_at": fetched_at,
                "batch_id": "batch1",
                "payload_hash": "hash1",
                "raw_payload": {"industry_name": "化学制品"},
                "created_at": fetched_at,
            }
        ],
    )

    writer.upsert_rows(
        "raw_stocks",
        [
            {
                "stock_code": "002317",
                "stock_name": "众生药业",
                "market": "SZ",
                "industry_name": None,
                "list_date": None,
                "is_st": False,
                "is_delisting_risk": False,
                "is_active": True,
                "source_name": "akshare",
                "interface_name": "stock_zh_a_spot",
                "fetched_at": fetched_at,
                "batch_id": "batch2",
                "payload_hash": "hash2",
                "raw_payload": {"industry_name": "C 制造业"},
                "created_at": fetched_at,
            }
        ],
    )

    with writer.engine.connect() as connection:
        latest_row = connection.execute(
            select(latest_raw_stocks_table.c.industry_name)
            .where(latest_raw_stocks_table.c.stock_code == "002317")
        ).mappings().one()

    assert latest_row["industry_name"] == "化学制品"


def test_update_stock_metadata_preserves_existing_latest_industry_when_new_value_is_generic() -> None:
    writer = RawDataWriter(create_in_memory_engine())
    writer.create_tables()

    fetched_at = datetime(2026, 6, 25, 15, 30, tzinfo=UTC)
    writer.upsert_rows(
        "raw_stocks",
        [
            {
                "stock_code": "002317",
                "stock_name": "众生药业",
                "market": "SZ",
                "industry_name": "化学制品",
                "list_date": None,
                "is_st": False,
                "is_delisting_risk": False,
                "is_active": True,
                "source_name": "akshare",
                "interface_name": "stock_zh_a_spot",
                "fetched_at": fetched_at,
                "batch_id": "batch1",
                "payload_hash": "hash1",
                "raw_payload": {"industry_name": "化学制品"},
                "created_at": fetched_at,
            }
        ],
    )

    updated = writer.update_stock_metadata(
        "batch1",
        [
            {
                "stock_code": "002317",
                "stock_name": "众生药业",
                "industry_name": "C 制造业",
                "list_date": None,
            }
        ],
    )

    assert updated == 1
    with writer.engine.connect() as connection:
        latest_row = connection.execute(
            select(latest_raw_stocks_table.c.industry_name)
            .where(latest_raw_stocks_table.c.stock_code == "002317")
        ).mappings().one()

    assert latest_row["industry_name"] == "化学制品"


def test_raw_data_writer_reports_trade_date_not_fully_collected_when_one_scope_missing() -> None:
    writer = RawDataWriter(create_in_memory_engine())
    writer.create_tables()

    completed_scopes = [
        "sync-daily-stocks",
        "sync-daily-indices",
        "sync-daily-bars",
    ]
    for index, scope in enumerate(completed_scopes, start=1):
        writer.append_log(
            {
                "batch_id": f"batch-{index}",
                "source_name": "akshare",
                "interface_name": scope,
                "target_scope": scope,
                "trade_date": date(2026, 6, 23),
                "report_date": None,
                "started_at": datetime(2026, 6, 23, index, tzinfo=UTC),
                "finished_at": datetime(2026, 6, 23, index, 1, tzinfo=UTC),
                "success_count": 1,
                "failure_count": 0,
                "missing_field_count": 0,
                "consecutive_failure_count": 0,
                "window_failure_rate": 0.0,
                "is_incremental": True,
                "is_complete": True,
                "is_signal_eligible": True,
                "circuit_breaker_opened": False,
                "error_message": None,
                "created_at": datetime(2026, 6, 23, index, 1, tzinfo=UTC),
            }
        )

    assert writer.is_trade_date_fully_collected(
        date(2026, 6, 23),
        scopes=[
            "sync-daily-stocks",
            "sync-daily-indices",
            "sync-daily-industries",
            "sync-daily-bars",
        ],
    ) is False


def test_raw_data_writer_reports_trade_date_fully_collected_when_all_scopes_complete() -> None:
    writer = RawDataWriter(create_in_memory_engine())
    writer.create_tables()

    completed_scopes = [
        "sync-daily-stocks",
        "sync-daily-indices",
        "sync-daily-industries",
        "sync-daily-bars",
    ]
    for index, scope in enumerate(completed_scopes, start=1):
        writer.append_log(
            {
                "batch_id": f"batch-{index}",
                "source_name": "akshare",
                "interface_name": scope,
                "target_scope": scope,
                "trade_date": date(2026, 6, 23),
                "report_date": None,
                "started_at": datetime(2026, 6, 23, index, tzinfo=UTC),
                "finished_at": datetime(2026, 6, 23, index, 1, tzinfo=UTC),
                "success_count": 1,
                "failure_count": 0,
                "missing_field_count": 0,
                "consecutive_failure_count": 0,
                "window_failure_rate": 0.0,
                "is_incremental": True,
                "is_complete": True,
                "is_signal_eligible": True,
                "circuit_breaker_opened": False,
                "error_message": None,
                "created_at": datetime(2026, 6, 23, index, 1, tzinfo=UTC),
            }
        )

    assert writer.is_trade_date_fully_collected(
        date(2026, 6, 23),
        scopes=completed_scopes,
    ) is True

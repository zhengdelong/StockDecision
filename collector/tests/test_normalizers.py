from datetime import UTC, date, datetime
from decimal import Decimal

from stock_collector.normalizers import AuditContext
from stock_collector.normalizers import build_checkpoint
from stock_collector.normalizers import build_ingestion_log
from stock_collector.normalizers import normalize_daily_bar
from stock_collector.normalizers import normalize_financial_snapshot
from stock_collector.normalizers import normalize_index_bar
from stock_collector.normalizers import normalize_industry_stat
from stock_collector.normalizers import normalize_stock_code
from stock_collector.normalizers import normalize_stock_snapshot


def test_normalize_stock_code_zero_pads_numeric_code() -> None:
    assert normalize_stock_code("1234") == "001234"


def test_normalize_stock_code_strips_exchange_prefix() -> None:
    assert normalize_stock_code("sh600000") == "600000"
    assert normalize_stock_code("sz000001") == "000001"
    assert normalize_stock_code("bj920000") == "920000"


def test_parse_decimal_treats_bool_as_missing() -> None:
    from stock_collector.normalizers import parse_decimal

    assert parse_decimal(False) is None


def test_parse_decimal_treats_non_finite_values_as_missing() -> None:
    from stock_collector.normalizers import parse_decimal

    assert parse_decimal(float("nan")) is None
    assert parse_decimal(float("inf")) is None
    assert parse_decimal(Decimal("NaN")) is None
    assert parse_decimal("Infinity") is None


def test_normalize_stock_snapshot_maps_core_fields() -> None:
    audit = AuditContext.create(
        source_name="akshare",
        interface_name="stock_zh_a_spot",
        symbol="all",
        batch_id="b1",
        is_incremental=True,
    )

    row = normalize_stock_snapshot(
        {
            "代码": "600000",
            "名称": "浦发银行",
            "所处行业": "银行",
        },
        audit,
    )

    assert row["stock_code"] == "600000"
    assert row["market"] == "SH"
    assert row["industry_name"] == "银行"
    assert row["raw_payload"]["代码"] == "600000"


def test_normalize_stock_snapshot_treats_generic_industry_as_missing() -> None:
    audit = AuditContext.create(
        source_name="akshare",
        interface_name="stock_zh_a_spot",
        symbol="all",
        batch_id="b1",
        is_incremental=True,
    )

    row = normalize_stock_snapshot(
        {
            "代码": "002317",
            "名称": "众生药业",
            "所处行业": "C 制造业",
        },
        audit,
    )

    assert row["industry_name"] is None


def test_normalize_stock_snapshot_maps_pe_pb() -> None:
    audit = AuditContext.create(
        source_name="akshare",
        interface_name="stock_zh_a_spot",
        symbol="all",
        batch_id="b1-valuations",
        is_incremental=True,
    )

    row = normalize_stock_snapshot(
        {
            "代码": "600000",
            "名称": "浦发银行",
            "市盈率TTM": "6.82",
            "市净率": "0.58",
        },
        audit,
    )

    assert row["pe"] == Decimal("6.82")
    assert row["pb"] == Decimal("0.58")


def test_normalize_stock_snapshot_maps_dynamic_pe_alias() -> None:
    audit = AuditContext.create(
        source_name="akshare",
        interface_name="stock_zh_a_spot_em",
        symbol="all",
        batch_id="b1-dynamic-pe",
        is_incremental=True,
    )

    row = normalize_stock_snapshot(
        {
            "代码": "600000",
            "名称": "浦发银行",
            "市盈率-动态": "6.82",
            "市净率": "0.58",
        },
        audit,
    )

    assert row["pe"] == Decimal("6.82")
    assert row["pb"] == Decimal("0.58")


def test_normalize_daily_bar_counts_missing_required_fields() -> None:
    audit = AuditContext.create(
        source_name="akshare",
        interface_name="stock_zh_a_daily",
        symbol="600000",
        batch_id="b2",
        is_incremental=False,
    )

    row = normalize_daily_bar(
        {
            "日期": "2026-06-16",
            "开盘": "10.0",
            "最高": "10.1",
            "最低": "9.8",
            "收盘": "10.0",
            "成交量": "1000",
            "涨跌幅": "0.1",
        },
        audit,
        stock_code="600000",
        adjust_type="qfq",
    )

    assert row["trade_date"] == date(2026, 6, 16)
    assert row["missing_field_count"] == 1


def test_normalize_stock_snapshot_maps_prefixed_codes_to_market() -> None:
    audit = AuditContext.create(
        source_name="akshare",
        interface_name="stock_zh_a_spot",
        symbol="all",
        batch_id="b1-prefixed",
        is_incremental=True,
    )

    row = normalize_stock_snapshot(
        {
            "code": "bj920000",
            "name": "sample",
        },
        audit,
    )

    assert row["stock_code"] == "920000"
    assert row["market"] == "BJ"


def test_normalize_index_bar_maps_trade_date() -> None:
    audit = AuditContext.create(
        source_name="akshare",
        interface_name="stock_zh_index_daily",
        symbol="000300",
        batch_id="b3",
        is_incremental=True,
    )

    row = normalize_index_bar(
        {"日期": "20260616", "开盘": "100", "最高": "110", "最低": "90", "收盘": "105"},
        audit,
        index_code="000300",
        index_name="沪深300",
    )

    assert row["trade_date"] == date(2026, 6, 16)
    assert row["index_name"] == "沪深300"


def test_normalize_financial_snapshot_maps_report_date() -> None:
    audit = AuditContext.create(
        source_name="akshare",
        interface_name="stock_financial_abstract_ths_fallback_sina",
        symbol="600000",
        batch_id="b4",
        is_incremental=True,
    )

    row = normalize_financial_snapshot(
        {"报告期": "2025-12-31", "净资产收益率": "12.5"},
        audit,
        stock_code="600000",
    )

    assert row["report_date"] == date(2025, 12, 31)
    assert row["roe"] == Decimal("12.5")


def test_normalize_financial_snapshot_maps_pe_pb_aliases() -> None:
    audit = AuditContext.create(
        source_name="akshare",
        interface_name="stock_financial_abstract_ths_fallback_sina",
        symbol="600000",
        batch_id="b4-pepb",
        is_incremental=True,
    )

    row = normalize_financial_snapshot(
        {"report_date": "2025-12-31", "市盈率TTM": "18.6", "市净率": "2.08"},
        audit,
        stock_code="600000",
    )

    assert row["report_date"] == date(2025, 12, 31)
    assert row["pe"] == Decimal("18.6")
    assert row["pb"] == Decimal("2.08")


def test_normalize_industry_stat_maps_rank() -> None:
    audit = AuditContext.create(
        source_name="akshare",
        interface_name="stock_board_industry_index_ths",
        symbol="all",
        batch_id="b5",
        is_incremental=True,
    )

    row = normalize_industry_stat(
        {"板块代码": "BK0917", "板块名称": "证券", "日期": "2026-06-16", "20日排名": "2"},
        audit,
    )

    assert row["industry_code"] == "BK0917"
    assert row["rank_20d"] == 2


def test_build_ingestion_log_keeps_new_audit_fields() -> None:
    started_at = datetime(2026, 6, 16, tzinfo=UTC)
    finished_at = datetime(2026, 6, 16, 1, tzinfo=UTC)
    row = build_ingestion_log(
        batch_id="b6",
        source_name="akshare",
        interface_name="stock_zh_a_daily",
        target_scope="bootstrap-daily-bars",
        is_incremental=False,
        started_at=started_at,
        finished_at=finished_at,
        success_count=10,
        failure_count=1,
        missing_field_count=2,
        consecutive_failure_count=1,
        window_failure_rate=0.1,
        is_complete=False,
        is_signal_eligible=False,
        circuit_breaker_opened=False,
        trade_date=date(2026, 6, 16),
    )

    assert row["target_scope"] == "bootstrap-daily-bars"
    assert row["started_at"] == started_at
    assert row["is_complete"] is False


def test_build_checkpoint_includes_status_and_dates() -> None:
    row = build_checkpoint(
        job_type="bootstrap-daily-bars",
        batch_id="b7",
        stock_code="600000",
        status="failed",
        retry_count=3,
        error_message="timeout",
    )

    assert row["job_type"] == "bootstrap-daily-bars"
    assert row["status"] == "failed"
    assert row["retry_count"] == 3

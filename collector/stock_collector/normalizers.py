from __future__ import annotations

import hashlib
import json
from dataclasses import dataclass
from datetime import UTC, date, datetime
from decimal import Decimal
from typing import Any


def normalize_stock_code(raw_code: Any) -> str:
    code = str(raw_code).strip().lower()
    if not code:
        raise ValueError("stock code is required")
    if code.startswith(("sh", "sz", "bj")):
        code = code[2:]
    return code.zfill(6) if code.isdigit() and len(code) < 6 else code


def normalize_symbol(raw_symbol: Any) -> str:
    symbol = str(raw_symbol).strip()
    if not symbol:
        raise ValueError("symbol is required")
    return symbol


def parse_trade_date(raw_value: Any) -> date | None:
    if raw_value in (None, ""):
        return None
    if isinstance(raw_value, date) and not isinstance(raw_value, datetime):
        return raw_value
    if isinstance(raw_value, datetime):
        return raw_value.date()

    text = str(raw_value).strip().replace("/", "-").replace(".", "-")
    if len(text) == 8 and text.isdigit():
        text = f"{text[0:4]}-{text[4:6]}-{text[6:8]}"
    return date.fromisoformat(text)


def parse_decimal(raw_value: Any) -> Decimal | None:
    if raw_value in (None, "", "-", "--"):
        return None
    if isinstance(raw_value, bool):
        return None
    if isinstance(raw_value, Decimal):
        return raw_value
    if isinstance(raw_value, (int, float)):
        return Decimal(str(raw_value))

    text = str(raw_value).strip().replace(",", "").replace("%", "")
    if not text:
        return None
    return Decimal(text)


def parse_integer(raw_value: Any) -> int | None:
    decimal_value = parse_decimal(raw_value)
    if decimal_value is None:
        return None
    return int(decimal_value)


def parse_bool(raw_value: Any) -> bool:
    if isinstance(raw_value, bool):
        return raw_value
    text = str(raw_value).strip().lower()
    return text in {"1", "true", "y", "yes", "st"}


def make_payload_hash(payload: Any) -> str:
    serialized = json.dumps(payload, ensure_ascii=False, sort_keys=True, default=str)
    return hashlib.sha256(serialized.encode("utf-8")).hexdigest()


def count_missing_fields(record: dict[str, Any], required_fields: set[str]) -> int:
    return sum(1 for field in required_fields if record.get(field) in (None, "", "-", "--"))


@dataclass(frozen=True)
class AuditContext:
    source_name: str
    interface_name: str
    symbol: str
    batch_id: str
    fetched_at: datetime
    is_incremental: bool
    retry_count: int = 0
    ingestion_status: str = "success"
    error_message: str | None = None
    trade_date: date | None = None
    report_date: date | None = None

    @classmethod
    def create(
        cls,
        *,
        source_name: str,
        interface_name: str,
        symbol: str,
        batch_id: str,
        is_incremental: bool,
        retry_count: int = 0,
        ingestion_status: str = "success",
        error_message: str | None = None,
        trade_date: date | None = None,
        report_date: date | None = None,
    ) -> "AuditContext":
        return cls(
            source_name=source_name,
            interface_name=interface_name,
            symbol=normalize_symbol(symbol),
            batch_id=batch_id,
            fetched_at=datetime.now(UTC),
            is_incremental=is_incremental,
            retry_count=retry_count,
            ingestion_status=ingestion_status,
            error_message=error_message,
            trade_date=trade_date,
            report_date=report_date,
        )


def normalize_stock_snapshot(raw: dict[str, Any], audit: AuditContext) -> dict[str, Any]:
    stock_code = normalize_stock_code(
        _first_present(
            raw,
            "\u4ee3\u7801",
            "浠ｇ爜",
            "code",
            "symbol",
        )
    )
    stock_name = str(_first_present(raw, "\u540d\u79f0", "鍚嶇О", "name", default="")).strip()
    normalized = {
        "stock_code": stock_code,
        "stock_name": stock_name,
        "market": _infer_market(stock_code),
        "industry_name": _optional_text(
            _first_present(
                raw,
                "\u6240\u5904\u884c\u4e1a",
                "鎵€澶勮涓?",
                "industry",
            )
        ),
        "list_date": parse_trade_date(
            _first_present(
                raw,
                "\u4e0a\u5e02\u65f6\u95f4",
                "涓婂競鏃堕棿",
                "list_date",
            )
        ),
        "is_st": _detect_st(stock_name, raw),
        "is_delisting_risk": _detect_delisting_risk(stock_name, raw),
        "is_active": True,
        **build_audit_columns(raw, audit),
    }
    return normalized


def normalize_daily_bar(
    raw: dict[str, Any],
    audit: AuditContext,
    *,
    stock_code: str,
    adjust_type: str,
) -> dict[str, Any]:
    trade_date = parse_trade_date(_first_present(raw, "\u65e5\u671f", "鏃ユ湡", "trade_date", "date"))
    normalized = {
        "stock_code": stock_code,
        "trade_date": trade_date,
        "open": parse_decimal(_first_present(raw, "\u5f00\u76d8", "寮€鐩?", "open")),
        "high": parse_decimal(_first_present(raw, "\u6700\u9ad8", "鏈€楂?", "high")),
        "low": parse_decimal(_first_present(raw, "\u6700\u4f4e", "鏈€浣?", "low")),
        "close": parse_decimal(_first_present(raw, "\u6536\u76d8", "鏀剁洏", "close")),
        "volume": parse_integer(_first_present(raw, "\u6210\u4ea4\u91cf", "鎴愪氦閲?", "volume")),
        "amount": parse_decimal(_first_present(raw, "\u6210\u4ea4\u989d", "鎴愪氦棰?", "amount")),
        "amplitude": parse_decimal(_first_present(raw, "\u632f\u5e45", "鎸箙", "amplitude")),
        "pct_change": parse_decimal(_first_present(raw, "\u6da8\u8dcc\u5e45", "娑ㄨ穼骞?", "pct_change")),
        "turnover_rate": parse_decimal(_first_present(raw, "\u6362\u624b\u7387", "鎹㈡墜鐜?", "turnover_rate")),
        "adjust_type": adjust_type,
        **build_audit_columns(raw, audit, trade_date=trade_date),
    }
    normalized["missing_field_count"] = count_missing_fields(
        normalized,
        {"stock_code", "trade_date", "open", "high", "low", "close", "volume", "amount", "pct_change"},
    )
    return normalized


def normalize_index_bar(
    raw: dict[str, Any],
    audit: AuditContext,
    *,
    index_code: str,
    index_name: str,
) -> dict[str, Any]:
    trade_date = parse_trade_date(_first_present(raw, "\u65e5\u671f", "鏃ユ湡", "trade_date", "date"))
    return {
        "index_code": index_code,
        "index_name": index_name,
        "trade_date": trade_date,
        "open": parse_decimal(_first_present(raw, "\u5f00\u76d8", "寮€鐩?", "open")),
        "high": parse_decimal(_first_present(raw, "\u6700\u9ad8", "鏈€楂?", "high")),
        "low": parse_decimal(_first_present(raw, "\u6700\u4f4e", "鏈€浣?", "low")),
        "close": parse_decimal(_first_present(raw, "\u6536\u76d8", "鏀剁洏", "close")),
        "amount": parse_decimal(_first_present(raw, "\u6210\u4ea4\u989d", "鎴愪氦棰?", "amount")),
        **build_audit_columns(raw, audit, trade_date=trade_date),
    }


def normalize_financial_snapshot(
    raw: dict[str, Any],
    audit: AuditContext,
    *,
    stock_code: str,
) -> dict[str, Any]:
    report_date = parse_trade_date(_first_present(raw, "\u62a5\u544a\u671f", "鎶ュ憡鏈?", "report_date", "\u62a5\u544a\u65e5", "鎶ュ憡鏃?"))
    normalized = {
        "stock_code": stock_code,
        "report_date": report_date,
        "pe": parse_decimal(_first_present(raw, "\u5e02\u76c8\u7387", "甯傜泩鐜?", "pe")),
        "pb": parse_decimal(_first_present(raw, "\u5e02\u51c0\u7387", "甯傚噣鐜?", "pb")),
        "roe": parse_decimal(_first_present(raw, "\u51c0\u8d44\u4ea7\u6536\u76ca\u7387", "鍑€璧勪骇鏀剁泭鐜?", "roe")),
        "revenue_yoy": parse_decimal(_first_present(raw, "\u8425\u4e1a\u6536\u5165\u540c\u6bd4\u589e\u957f", "钀ヤ笟鏀跺叆鍚屾瘮澧為暱", "revenue_yoy")),
        "net_profit_yoy": parse_decimal(_first_present(raw, "\u51c0\u5229\u6da6\u540c\u6bd4\u589e\u957f", "鍑€鍒╂鼎鍚屾瘮澧為暱", "net_profit_yoy")),
        "free_float_market_cap": parse_decimal(_first_present(raw, "\u6d41\u901a\u5e02\u503c", "娴侀€氬競鍊?", "free_float_market_cap")),
        "operating_cash_flow": parse_decimal(_first_present(raw, "\u6bcf\u80a1\u7ecf\u8425\u6027\u73b0\u91d1\u6d41", "姣忚偂缁忚惀鎬х幇閲戞祦", "operating_cash_flow")),
        **build_audit_columns(raw, audit, report_date=report_date),
    }
    normalized["missing_field_count"] = count_missing_fields(normalized, {"stock_code", "report_date"})
    return normalized


def normalize_industry_stat(raw: dict[str, Any], audit: AuditContext) -> dict[str, Any]:
    trade_date = parse_trade_date(_first_present(raw, "\u65e5\u671f", "鏃ユ湡", "trade_date"))
    return {
        "industry_code": normalize_symbol(_first_present(raw, "\u677f\u5757\u4ee3\u7801", "鏉垮潡浠ｇ爜", "industry_code", "symbol")),
        "industry_name": str(_first_present(raw, "\u677f\u5757\u540d\u79f0", "鏉垮潡鍚嶇О", "industry_name", default="")).strip(),
        "trade_date": trade_date,
        "pct_change_20d": parse_decimal(_first_present(raw, "20\u65e5\u6da8\u8dcc\u5e45", "20鏃ユ定璺屽箙", "pct_change_20d")),
        "rank_20d": parse_integer(_first_present(raw, "20\u65e5\u6392\u540d", "20鏃ユ帓鍚?", "rank_20d")),
        "member_count": parse_integer(_first_present(raw, "\u6210\u5206\u80a1\u6570\u91cf", "鎴愬垎鑲℃暟閲?", "member_count")),
        **build_audit_columns(raw, audit, trade_date=trade_date),
    }


def build_ingestion_log(
    *,
    batch_id: str,
    source_name: str,
    interface_name: str,
    target_scope: str,
    is_incremental: bool,
    started_at: datetime,
    finished_at: datetime,
    success_count: int,
    failure_count: int,
    missing_field_count: int,
    consecutive_failure_count: int,
    window_failure_rate: float | None,
    is_complete: bool,
    is_signal_eligible: bool,
    circuit_breaker_opened: bool,
    trade_date: date | None = None,
    report_date: date | None = None,
    error_message: str | None = None,
) -> dict[str, Any]:
    return {
        "batch_id": batch_id,
        "source_name": source_name,
        "interface_name": interface_name,
        "target_scope": target_scope,
        "trade_date": trade_date,
        "report_date": report_date,
        "started_at": started_at,
        "finished_at": finished_at,
        "success_count": success_count,
        "failure_count": failure_count,
        "missing_field_count": missing_field_count,
        "consecutive_failure_count": consecutive_failure_count,
        "window_failure_rate": window_failure_rate,
        "is_incremental": is_incremental,
        "is_complete": is_complete,
        "is_signal_eligible": is_signal_eligible,
        "circuit_breaker_opened": circuit_breaker_opened,
        "error_message": error_message,
    }


def build_checkpoint(
    *,
    job_type: str,
    batch_id: str,
    stock_code: str,
    status: str,
    retry_count: int,
    error_message: str | None = None,
    last_success_date: date | None = None,
) -> dict[str, Any]:
    now = datetime.now(UTC)
    return {
        "job_type": job_type,
        "batch_id": batch_id,
        "stock_code": stock_code,
        "status": status,
        "last_success_date": last_success_date,
        "retry_count": retry_count,
        "error_message": error_message,
        "updated_at": now,
        "created_at": now,
    }


def build_audit_columns(
    raw_payload: Any,
    audit: AuditContext,
    *,
    trade_date: date | None = None,
    report_date: date | None = None,
) -> dict[str, Any]:
    return {
        "source_name": audit.source_name,
        "interface_name": audit.interface_name,
        "fetched_at": audit.fetched_at,
        "batch_id": audit.batch_id,
        "is_incremental": audit.is_incremental,
        "payload_hash": make_payload_hash(raw_payload),
        "retry_count": audit.retry_count,
        "ingestion_status": audit.ingestion_status,
        "error_message": audit.error_message,
        "raw_payload": raw_payload,
        "created_at": audit.fetched_at,
        "trade_date": trade_date if trade_date is not None else audit.trade_date,
        "report_date": report_date if report_date is not None else audit.report_date,
    }


def _first_present(raw: dict[str, Any], *keys: str, default: Any = None) -> Any:
    for key in keys:
        if key in raw and raw[key] not in (None, ""):
            return raw[key]
    return default


def _optional_text(raw_value: Any) -> str | None:
    if raw_value is None:
        return None
    text = str(raw_value).strip()
    return text or None


def _detect_st(stock_name: str, raw: dict[str, Any]) -> bool:
    if parse_bool(raw.get("is_st")):
        return True
    return "ST" in stock_name.upper()


def _detect_delisting_risk(stock_name: str, raw: dict[str, Any]) -> bool:
    if parse_bool(raw.get("is_delisting_risk")):
        return True
    name = stock_name.upper()
    return "\u9000" in stock_name or "*ST" in name


def _infer_market(stock_code: str) -> str:
    if stock_code.startswith(("600", "601", "603", "605", "688")):
        return "SH"
    if stock_code.startswith(("000", "001", "002", "003", "300", "301", "302")):
        return "SZ"
    if stock_code.startswith(("430", "830", "831", "832", "833", "835", "836", "837", "838", "920")):
        return "BJ"
    return "UNKNOWN"


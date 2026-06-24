from __future__ import annotations

from collections import deque
from dataclasses import dataclass
from datetime import UTC, date, datetime, timedelta
from time import sleep as default_sleep
from typing import Any
from uuid import uuid4

from stock_collector.akshare_client import DEFAULT_INDEX_DEFINITIONS
from stock_collector.akshare_client import AkshareClient
from stock_collector.akshare_client import IndexDefinition
from stock_collector.mysql_writer import RawDataWriter
from stock_collector.normalizers import AuditContext
from stock_collector.normalizers import build_checkpoint
from stock_collector.normalizers import build_ingestion_log
from stock_collector.normalizers import normalize_daily_bar
from stock_collector.normalizers import normalize_financial_snapshot
from stock_collector.normalizers import normalize_index_bar
from stock_collector.normalizers import normalize_industry_stat
from stock_collector.normalizers import normalize_stock_snapshot
from stock_collector.policies import CircuitBreakerDecision
from stock_collector.policies import DataCompletenessInput
from stock_collector.policies import DataCompletenessPolicy
from stock_collector.policies import InterfaceThrottlePolicy
from stock_collector.policies import RetryPolicy

DAILY_BAR_INTERFACE = "stock_zh_a_hist"
INDEX_BAR_INTERFACE = "stock_zh_index_daily"
INDUSTRY_INTERFACE = "stock_board_industry_index_ths"
FINANCIAL_INTERFACE = "stock_financial_abstract_ths_fallback_sina"


@dataclass(frozen=True)
class CollectionWindow:
    start_date: date
    end_date: date


@dataclass(frozen=True)
class CollectionRunResult:
    dataset: str
    rows_written: int
    log_status: str
    is_complete: bool
    is_signal_eligible: bool
    completeness_reasons: list[str]
    circuit_breaker_opened: bool = False


class CollectorOrchestrator:
    def __init__(
        self,
        client: AkshareClient,
        writer: RawDataWriter,
        *,
        sleep_fn: Any = default_sleep,
        now_fn: Any | None = None,
    ) -> None:
        self._client = client
        self._writer = writer
        self._sleep = sleep_fn
        self._now = now_fn or (lambda: datetime.now(UTC))

    def bootstrap_indices(self, *, years: int = 10) -> CollectionRunResult:
        end_date = self._now().date()
        start_date = end_date - timedelta(days=years * 365)
        return self.collect_index_bars(
            window=CollectionWindow(start_date=start_date, end_date=end_date),
            is_incremental=False,
            target_scope="bootstrap-indices",
        )

    def bootstrap_stocks(self) -> CollectionRunResult:
        return self.collect_stock_snapshot(is_incremental=False, target_scope="bootstrap-stocks")

    def bootstrap_daily_bars(
        self,
        stock_codes: list[str],
        *,
        years: int = 8,
        adjust_type: str = "qfq",
    ) -> CollectionRunResult:
        end_date = self._now().date()
        start_date = end_date - timedelta(days=years * 365)
        return self._collect_daily_bars(
            stock_codes,
            window=CollectionWindow(start_date=start_date, end_date=end_date),
            adjust_type=adjust_type,
            mode="bootstrap",
            target_scope="bootstrap-daily-bars",
            stock_snapshot_refreshed=True,
            missing_market_indices=[],
            industry_missing_rate=0.0,
        )

    def bootstrap_financials(self, stock_codes: list[str], *, limit_symbols: int | None = None) -> CollectionRunResult:
        return self._collect_financials(
            stock_codes,
            mode="bootstrap",
            target_scope="bootstrap-financials",
            limit_symbols=limit_symbols,
        )

    def bootstrap_industries(self) -> CollectionRunResult:
        return self.collect_industry_stats(is_incremental=False, target_scope="bootstrap-industries")

    def retry_failed(self, stock_codes: list[str], *, years: int = 8) -> CollectionRunResult:
        checkpoints = self._writer.load_checkpoints("bootstrap-daily-bars")
        retry_codes = [code for code in stock_codes if checkpoints.get(code, {}).get("status") == "failed"]
        return self.bootstrap_daily_bars(retry_codes, years=years) if retry_codes else CollectionRunResult(
            dataset="raw_daily_bars",
            rows_written=0,
            log_status="success",
            is_complete=True,
            is_signal_eligible=True,
            completeness_reasons=[],
        )

    def sync_daily_market(self, stock_codes: list[str], *, trade_date: date | None = None) -> list[CollectionRunResult]:
        selected_date = trade_date or self._now().date()
        window = CollectionWindow(start_date=selected_date, end_date=selected_date)

        stock_result = self.collect_stock_snapshot(
            trade_date=selected_date,
            is_incremental=True,
            target_scope="sync-daily-stocks",
        )
        index_result = self.collect_index_bars(
            window=window,
            is_incremental=True,
            target_scope="sync-daily-indices",
        )
        industry_result = self.collect_industry_stats(
            trade_date=selected_date,
            is_incremental=True,
            target_scope="sync-daily-industries",
        )
        missing_market_indices = [] if index_result.rows_written >= len(DEFAULT_INDEX_DEFINITIONS) else [
            definition.code for definition in DEFAULT_INDEX_DEFINITIONS
        ]
        industry_missing_rate = 0.0 if industry_result.rows_written > 0 else 1.0
        daily_result = self._collect_daily_bars(
            stock_codes,
            window=window,
            adjust_type="qfq",
            mode="incremental",
            target_scope="sync-daily-bars",
            stock_snapshot_refreshed=stock_result.rows_written > 0,
            missing_market_indices=missing_market_indices,
            industry_missing_rate=industry_missing_rate,
        )
        return [stock_result, daily_result, index_result, industry_result]

    def sync_financials(self, stock_codes: list[str], *, limit_symbols: int | None = None) -> CollectionRunResult:
        return self._collect_financials(
            stock_codes,
            mode="incremental",
            target_scope="sync-financials",
            limit_symbols=limit_symbols,
        )

    def collect_stock_snapshot(
        self,
        *,
        trade_date: date | None = None,
        is_incremental: bool,
        target_scope: str,
    ) -> CollectionRunResult:
        started_at = self._now()
        batch_id = self._new_batch_id("raw_stocks")
        payload = self._client.fetch_stock_spot_snapshot()
        audit = AuditContext.create(
            source_name="akshare",
            interface_name="stock_zh_a_spot",
            symbol="all",
            batch_id=batch_id,
            is_incremental=is_incremental,
            trade_date=trade_date or self._now().date(),
        )
        rows = [normalize_stock_snapshot(item, audit) for item in payload]
        written = self._writer.upsert_rows("raw_stocks", rows)
        self._writer.append_log(
            build_ingestion_log(
                batch_id=batch_id,
                source_name="akshare",
                interface_name="stock_zh_a_spot",
                target_scope=target_scope,
                is_incremental=is_incremental,
                started_at=started_at,
                finished_at=self._now(),
                success_count=written,
                failure_count=0,
                missing_field_count=0,
                consecutive_failure_count=0,
                window_failure_rate=0.0,
                is_complete=written > 0,
                is_signal_eligible=written > 0,
                circuit_breaker_opened=False,
                trade_date=audit.trade_date,
            )
        )
        return CollectionRunResult(
            dataset="raw_stocks",
            rows_written=written,
            log_status="success",
            is_complete=written > 0,
            is_signal_eligible=written > 0,
            completeness_reasons=[] if written > 0 else ["stock_snapshot_not_refreshed"],
        )

    def collect_index_bars(
        self,
        *,
        window: CollectionWindow,
        is_incremental: bool,
        target_scope: str,
        index_definitions: tuple[IndexDefinition, ...] = DEFAULT_INDEX_DEFINITIONS,
    ) -> CollectionRunResult:
        started_at = self._now()
        batch_id = self._new_batch_id("raw_market_index_bars")
        rows: list[dict[str, Any]] = []
        failures = 0
        for definition in index_definitions:
            payload, retry_count, _ = self._fetch_with_retry(
                lambda symbol=definition.symbol: self._client.fetch_index_bars(
                    symbol,
                    start_date=window.start_date.strftime("%Y%m%d"),
                    end_date=window.end_date.strftime("%Y%m%d"),
                ),
                interface_name=INDEX_BAR_INTERFACE,
                mode="incremental" if is_incremental else "bootstrap",
            )
            if payload is None:
                failures += 1
                continue
            audit = AuditContext.create(
                source_name="akshare",
                interface_name=INDEX_BAR_INTERFACE,
                symbol=definition.code,
                batch_id=batch_id,
                is_incremental=is_incremental,
                retry_count=retry_count,
            )
            normalized_rows = self._filter_rows_for_window(
                [
                    normalize_index_bar(item, audit, index_code=definition.code, index_name=definition.name)
                    for item in payload
                ],
                window=window,
            )
            rows.extend(
                normalized_rows
            )

        written = self._writer.upsert_rows("raw_market_index_bars", rows)
        self._writer.append_log(
            build_ingestion_log(
                batch_id=batch_id,
                source_name="akshare",
                interface_name=INDEX_BAR_INTERFACE,
                target_scope=target_scope,
                is_incremental=is_incremental,
                started_at=started_at,
                finished_at=self._now(),
                success_count=written,
                failure_count=failures,
                missing_field_count=0,
                consecutive_failure_count=failures,
                window_failure_rate=failures / max(len(index_definitions), 1),
                is_complete=failures == 0 and written > 0,
                is_signal_eligible=failures == 0 and written >= len(index_definitions),
                circuit_breaker_opened=False,
                trade_date=window.end_date,
            )
        )
        return CollectionRunResult(
            dataset="raw_market_index_bars",
            rows_written=written,
            log_status="success" if failures == 0 else "partial",
            is_complete=failures == 0 and written > 0,
            is_signal_eligible=failures == 0 and written >= len(index_definitions),
            completeness_reasons=[] if failures == 0 else ["market_indices"],
        )

    def collect_industry_stats(
        self,
        *,
        trade_date: date | None = None,
        is_incremental: bool,
        target_scope: str,
    ) -> CollectionRunResult:
        started_at = self._now()
        batch_id = self._new_batch_id("raw_industry_daily_stats")
        payload, retry_count, _ = self._fetch_with_retry(
            self._client.fetch_industry_daily_stats,
            interface_name=INDUSTRY_INTERFACE,
            mode="incremental" if is_incremental else "bootstrap",
        )
        payload = payload or []
        audit = AuditContext.create(
            source_name="akshare",
            interface_name=INDUSTRY_INTERFACE,
            symbol="all",
            batch_id=batch_id,
            is_incremental=is_incremental,
            retry_count=retry_count,
            trade_date=trade_date or self._now().date(),
        )
        rows = [normalize_industry_stat(item, audit) for item in payload]
        if trade_date is not None:
            rows = self._filter_rows_for_trade_date(rows, trade_date=trade_date)
        written = self._writer.upsert_rows("raw_industry_daily_stats", rows)
        self._writer.append_log(
            build_ingestion_log(
                batch_id=batch_id,
                source_name="akshare",
                interface_name=INDUSTRY_INTERFACE,
                target_scope=target_scope,
                is_incremental=is_incremental,
                started_at=started_at,
                finished_at=self._now(),
                success_count=written,
                failure_count=0 if rows else 1,
                missing_field_count=0,
                consecutive_failure_count=0 if rows else 1,
                window_failure_rate=0.0 if rows else 1.0,
                is_complete=written > 0,
                is_signal_eligible=written > 0,
                circuit_breaker_opened=False,
                trade_date=audit.trade_date,
            )
        )
        return CollectionRunResult(
            dataset="raw_industry_daily_stats",
            rows_written=written,
            log_status="success" if written > 0 else "partial",
            is_complete=written > 0,
            is_signal_eligible=written > 0,
            completeness_reasons=[] if written > 0 else ["industry_missing_rate"],
        )

    def _collect_financials(
        self,
        stock_codes: list[str],
        *,
        mode: str,
        target_scope: str,
        limit_symbols: int | None = None,
    ) -> CollectionRunResult:
        started_at = self._now()
        batch_id = self._new_batch_id("raw_financial_snapshots")
        policy = InterfaceThrottlePolicy.for_interface(FINANCIAL_INTERFACE, mode=mode)
        retry_policy = RetryPolicy.default()
        codes = stock_codes[:limit_symbols] if limit_symbols else stock_codes
        rows: list[dict[str, Any]] = []
        success_checkpoints: list[dict[str, Any]] = []
        failures = 0
        consecutive_failures = 0
        empty_payload_symbols = 0
        for stock_code in codes:
            payload, retry_count, error_message = self._fetch_with_retry(
                lambda code=stock_code: self._client.fetch_financial_snapshots(code),
                interface_name=FINANCIAL_INTERFACE,
                mode=mode,
            )
            if payload is None:
                failures += 1
                consecutive_failures += 1
                self._writer.upsert_checkpoint(
                    build_checkpoint(
                        job_type=target_scope,
                        batch_id=batch_id,
                        stock_code=stock_code,
                        status="failed",
                        retry_count=retry_policy.max_attempts - 1,
                        error_message=error_message,
                    )
                )
                if policy.pause_after_consecutive_failures and consecutive_failures >= policy.pause_after_consecutive_failures:
                    self._sleep(policy.pause_seconds or 0)
                continue

            consecutive_failures = 0
            if not payload:
                empty_payload_symbols += 1
                self._writer.upsert_checkpoint(
                    build_checkpoint(
                        job_type=target_scope,
                        batch_id=batch_id,
                        stock_code=stock_code,
                        status="skipped",
                        retry_count=retry_count,
                        error_message="empty_payload",
                    )
                )
                self._sleep(policy.min_delay_seconds)
                continue
            audit = AuditContext.create(
                source_name="akshare",
                interface_name=FINANCIAL_INTERFACE,
                symbol=stock_code,
                batch_id=batch_id,
                is_incremental=mode == "incremental",
                retry_count=retry_count,
            )
            rows.extend(normalize_financial_snapshot(item, audit, stock_code=stock_code) for item in payload[:12])
            success_checkpoints.append(
                build_checkpoint(
                    job_type=target_scope,
                    batch_id=batch_id,
                    stock_code=stock_code,
                    status="success",
                    retry_count=retry_count,
                    last_success_date=self._now().date(),
                )
            )
            self._sleep(policy.min_delay_seconds)

        written = self._writer.upsert_rows("raw_financial_snapshots", rows)
        for checkpoint in success_checkpoints:
            self._writer.upsert_checkpoint(checkpoint)
        self._writer.append_log(
            build_ingestion_log(
                batch_id=batch_id,
                source_name="akshare",
                interface_name=FINANCIAL_INTERFACE,
                target_scope=target_scope,
                is_incremental=mode == "incremental",
                started_at=started_at,
                finished_at=self._now(),
                success_count=written,
                failure_count=failures,
                missing_field_count=sum(row.get("missing_field_count", 0) for row in rows),
                consecutive_failure_count=consecutive_failures,
                window_failure_rate=failures / max(len(codes), 1),
                is_complete=written > 0 and failures == 0 and empty_payload_symbols == 0,
                is_signal_eligible=written > 0 and failures == 0 and empty_payload_symbols == 0,
                circuit_breaker_opened=False,
                error_message="empty_payload" if written == 0 and empty_payload_symbols > 0 and failures == 0 else None,
            )
        )
        return CollectionRunResult(
            dataset="raw_financial_snapshots",
            rows_written=written,
            log_status="success" if written > 0 and failures == 0 and empty_payload_symbols == 0 else "partial",
            is_complete=written > 0 and failures == 0 and empty_payload_symbols == 0,
            is_signal_eligible=written > 0 and failures == 0 and empty_payload_symbols == 0,
            completeness_reasons=[] if written > 0 and failures == 0 and empty_payload_symbols == 0 else ["financial_partial"],
        )

    def _collect_daily_bars(
        self,
        stock_codes: list[str],
        *,
        window: CollectionWindow,
        adjust_type: str,
        mode: str,
        target_scope: str,
        stock_snapshot_refreshed: bool,
        missing_market_indices: list[str],
        industry_missing_rate: float,
    ) -> CollectionRunResult:
        started_at = self._now()
        batch_id = self._new_batch_id("raw_daily_bars")
        policy = InterfaceThrottlePolicy.for_interface(DAILY_BAR_INTERFACE, mode=mode)
        retry_policy = RetryPolicy.default()
        checkpoints = self._writer.load_checkpoints(target_scope)
        total_rows_written = 0
        total_missing_field_count = 0
        successful_symbols = sum(
            1 for code in stock_codes
            if self._is_terminal_checkpoint(
                checkpoints.get(code),
                mode=mode,
                target_date=window.end_date,
            )
        )
        failures = 0
        consecutive_failures = 0
        breaker_recent = deque(maxlen=20)
        circuit_breaker_opened = False

        pending_codes = [
            code for code in stock_codes
            if not self._is_terminal_checkpoint(
                checkpoints.get(code),
                mode=mode,
                target_date=window.end_date,
            )
        ]

        for batch_start in range(0, len(pending_codes), policy.batch_size):
            batch_codes = pending_codes[batch_start: batch_start + policy.batch_size]
            for stock_code in batch_codes:
                payload, retry_count, error_message = self._fetch_with_retry(
                    lambda code=stock_code: self._client.fetch_daily_bars(
                        code,
                        start_date=window.start_date.strftime("%Y%m%d"),
                        end_date=window.end_date.strftime("%Y%m%d"),
                        adjust=adjust_type,
                    ),
                    interface_name=DAILY_BAR_INTERFACE,
                    mode=mode,
                )
                if payload is None:
                    failures += 1
                    consecutive_failures += 1
                    breaker_recent.append(False)
                    self._writer.upsert_checkpoint(
                        build_checkpoint(
                            job_type=target_scope,
                            batch_id=batch_id,
                            stock_code=stock_code,
                            status="failed",
                            retry_count=retry_policy.max_attempts - 1,
                            error_message=error_message,
                        )
                    )
                    decision = CircuitBreakerDecision.evaluate(
                        consecutive_failures=consecutive_failures,
                        window_failure_rate=self._failure_rate(breaker_recent),
                        anti_bot_detected=self._is_anti_bot_error(error_message),
                    )
                    if decision.should_open:
                        circuit_breaker_opened = True
                        break
                    continue

                consecutive_failures = 0
                breaker_recent.append(True)
                audit = AuditContext.create(
                    source_name="akshare",
                    interface_name=DAILY_BAR_INTERFACE,
                    symbol=stock_code,
                    batch_id=batch_id,
                    is_incremental=mode == "incremental",
                    retry_count=retry_count,
                )
                normalized_rows = self._filter_rows_for_window(
                    [
                    normalize_daily_bar(item, audit, stock_code=stock_code, adjust_type=adjust_type)
                    for item in payload
                    ],
                    window=window,
                )
                if normalized_rows:
                    successful_symbols += 1
                    written = self._writer.upsert_rows("raw_daily_bars", normalized_rows)
                    total_rows_written += written
                    total_missing_field_count += sum(row.get("missing_field_count", 0) for row in normalized_rows)
                    self._writer.upsert_checkpoint(
                        build_checkpoint(
                            job_type=target_scope,
                            batch_id=batch_id,
                            stock_code=stock_code,
                            status="success",
                            retry_count=retry_count,
                            last_success_date=window.end_date,
                        )
                    )
                else:
                    self._writer.upsert_checkpoint(
                        build_checkpoint(
                            job_type=target_scope,
                            batch_id=batch_id,
                            stock_code=stock_code,
                            status="skipped",
                            retry_count=retry_count,
                            error_message="target_trade_date_mismatch" if payload else "empty_payload",
                        )
                    )
                self._sleep(policy.min_delay_seconds)

            if circuit_breaker_opened:
                break

            if batch_start + policy.batch_size < len(pending_codes):
                self._sleep(policy.cooldown_after_batch_seconds)

        coverage = 1.0 if not stock_codes else successful_symbols / len(stock_codes)
        completeness = DataCompletenessPolicy.evaluate(
            DataCompletenessInput(
                daily_bar_coverage=coverage,
                missing_market_indices=missing_market_indices,
                industry_missing_rate=industry_missing_rate,
                stock_snapshot_refreshed=stock_snapshot_refreshed,
            )
        )
        log_status = "success" if completeness.is_signal_eligible else "partial"
        self._writer.append_log(
            build_ingestion_log(
                batch_id=batch_id,
                source_name="akshare",
                interface_name=DAILY_BAR_INTERFACE,
                target_scope=target_scope,
                is_incremental=mode == "incremental",
                started_at=started_at,
                finished_at=self._now(),
                success_count=total_rows_written,
                failure_count=failures,
                missing_field_count=total_missing_field_count,
                consecutive_failure_count=consecutive_failures,
                window_failure_rate=self._failure_rate(breaker_recent),
                is_complete=not circuit_breaker_opened and coverage >= 0.95,
                is_signal_eligible=completeness.is_signal_eligible and not circuit_breaker_opened,
                circuit_breaker_opened=circuit_breaker_opened,
                trade_date=window.end_date,
                error_message=";".join(completeness.reasons) if completeness.reasons else None,
            )
        )
        return CollectionRunResult(
            dataset="raw_daily_bars",
            rows_written=total_rows_written,
            log_status=log_status,
            is_complete=not circuit_breaker_opened and coverage >= 0.95,
            is_signal_eligible=completeness.is_signal_eligible and not circuit_breaker_opened,
            completeness_reasons=completeness.reasons,
            circuit_breaker_opened=circuit_breaker_opened,
        )

    def _fetch_with_retry(
        self,
        operation: Any,
        *,
        interface_name: str,
        mode: str,
    ) -> tuple[Any | None, int, str | None]:
        retry_policy = RetryPolicy.default()
        _ = InterfaceThrottlePolicy.for_interface(interface_name, mode=mode)
        last_error: str | None = None

        for attempt in range(1, retry_policy.max_attempts + 1):
            try:
                return operation(), attempt - 1, None
            except Exception as exc:  # noqa: BLE001
                last_error = str(exc)
                delay = retry_policy.delay_for_attempt(attempt)
                if delay is None:
                    break
                self._sleep(delay)

        return None, retry_policy.max_attempts - 1, last_error

    @staticmethod
    def _failure_rate(results: deque[bool]) -> float:
        if not results:
            return 0.0
        failures = sum(1 for item in results if item is False)
        return failures / len(results)

    @staticmethod
    def _is_anti_bot_error(error_message: str | None) -> bool:
        if not error_message:
            return False
        text = error_message.lower()
        return "anti" in text or "captcha" in text or "forbidden" in text or "blocked" in text

    @staticmethod
    def _is_terminal_checkpoint(
        checkpoint: dict[str, Any] | None,
        *,
        mode: str,
        target_date: date,
    ) -> bool:
        if checkpoint is None:
            return False

        status = checkpoint.get("status")
        if status == "success":
            if mode == "bootstrap":
                return True
            last_success_date = checkpoint.get("last_success_date")
            return isinstance(last_success_date, date) and last_success_date >= target_date
        return mode == "bootstrap" and status == "skipped"

    def _new_batch_id(self, dataset: str) -> str:
        return f"{dataset}-{self._now().strftime('%Y%m%d%H%M%S')}-{uuid4().hex[:8]}"

    @staticmethod
    def _filter_rows_for_window(rows: list[dict[str, Any]], *, window: CollectionWindow) -> list[dict[str, Any]]:
        # 增量采集只接受目标窗口内的数据，避免把上一交易日误记为当天成功。
        return [
            row for row in rows
            if isinstance(row.get("trade_date"), date) and window.start_date <= row["trade_date"] <= window.end_date
        ]

    @staticmethod
    def _filter_rows_for_trade_date(rows: list[dict[str, Any]], *, trade_date: date) -> list[dict[str, Any]]:
        # 行业数据接口可能返回最近可用交易日，这里只保留目标交易日。
        return [row for row in rows if row.get("trade_date") == trade_date]

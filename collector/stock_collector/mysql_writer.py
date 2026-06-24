from __future__ import annotations

from dataclasses import dataclass
from datetime import UTC, date, datetime
from decimal import Decimal
import math
from typing import Any

from sqlalchemy import Boolean
from sqlalchemy import BigInteger
from sqlalchemy import Column
from sqlalchemy import Date
from sqlalchemy import DateTime
from sqlalchemy import Float
from sqlalchemy import Index
from sqlalchemy import Integer
from sqlalchemy import JSON
from sqlalchemy import MetaData
from sqlalchemy import Numeric
from sqlalchemy import func
from sqlalchemy import not_
from sqlalchemy import or_
from sqlalchemy import String
from sqlalchemy import Table
from sqlalchemy import Text
from sqlalchemy import UniqueConstraint
from sqlalchemy import update
from sqlalchemy import create_engine
from sqlalchemy import text
from sqlalchemy.engine import Engine
from sqlalchemy.engine import URL
from sqlalchemy import select
from sqlalchemy.sql import Select


@dataclass(frozen=True)
class MySqlSettings:
    host: str
    port: int
    database: str
    user: str
    password: str
    charset: str = "utf8mb4"

    @property
    def url(self) -> str:
        return (
            f"mysql+pymysql://{self.user}:{self.password}@"
            f"{self.host}:{self.port}/{self.database}?charset={self.charset}"
        )

    def to_sqlalchemy_url(self) -> URL:
        return URL.create(
            drivername="mysql+pymysql",
            username=self.user,
            password=self.password,
            host=self.host,
            port=self.port,
            database=self.database,
            query={"charset": self.charset},
        )


RAW_TABLE_METADATA = MetaData()

raw_stocks_table = Table(
    "raw_stocks",
    RAW_TABLE_METADATA,
    Column("id", Integer, primary_key=True, autoincrement=True),
    Column("stock_code", String(16), nullable=False),
    Column("stock_name", String(64), nullable=False),
    Column("market", String(16), nullable=True),
    Column("industry_name", String(64), nullable=True),
    Column("list_date", Date, nullable=True),
    Column("is_st", Boolean, nullable=False, default=False),
    Column("is_delisting_risk", Boolean, nullable=False, default=False),
    Column("is_active", Boolean, nullable=False, default=True),
    Column("source_name", String(32), nullable=False),
    Column("interface_name", String(64), nullable=False),
    Column("fetched_at", DateTime(timezone=True), nullable=False),
    Column("batch_id", String(64), nullable=False),
    Column("payload_hash", String(64), nullable=True),
    Column("raw_payload", JSON, nullable=True),
    Column("created_at", DateTime(timezone=True), nullable=False),
    UniqueConstraint("batch_id", "stock_code", name="uk_raw_stocks_batch_code"),
    Index("idx_raw_stocks_code", "stock_code"),
    Index("idx_raw_stocks_fetched_at", "fetched_at"),
)

raw_daily_bars_table = Table(
    "raw_daily_bars",
    RAW_TABLE_METADATA,
    Column("id", Integer, primary_key=True, autoincrement=True),
    Column("stock_code", String(16), nullable=False),
    Column("trade_date", Date, nullable=False),
    Column("open", Numeric(18, 4), nullable=True),
    Column("high", Numeric(18, 4), nullable=True),
    Column("low", Numeric(18, 4), nullable=True),
    Column("close", Numeric(18, 4), nullable=True),
    Column("volume", BigInteger, nullable=True),
    Column("amount", Numeric(20, 2), nullable=True),
    Column("amplitude", Numeric(10, 4), nullable=True),
    Column("pct_change", Numeric(10, 4), nullable=True),
    Column("turnover_rate", Numeric(10, 4), nullable=True),
    Column("adjust_type", String(16), nullable=False),
    Column("source_name", String(32), nullable=False),
    Column("interface_name", String(64), nullable=False),
    Column("fetched_at", DateTime(timezone=True), nullable=False),
    Column("batch_id", String(64), nullable=False),
    Column("is_incremental", Boolean, nullable=False, default=False),
    Column("payload_hash", String(64), nullable=True),
    Column("retry_count", Integer, nullable=False, default=0),
    Column("missing_field_count", Integer, nullable=False, default=0),
    Column("ingestion_status", String(16), nullable=False),
    Column("error_message", String(512), nullable=True),
    Column("raw_payload", JSON, nullable=True),
    Column("created_at", DateTime(timezone=True), nullable=False),
    UniqueConstraint("stock_code", "trade_date", "adjust_type", name="uk_raw_daily_bars_code_date_adjust"),
    Index("idx_raw_daily_bars_trade_date", "trade_date"),
    Index("idx_raw_daily_bars_batch_id", "batch_id"),
)

raw_financial_snapshots_table = Table(
    "raw_financial_snapshots",
    RAW_TABLE_METADATA,
    Column("id", Integer, primary_key=True, autoincrement=True),
    Column("stock_code", String(16), nullable=False),
    Column("report_date", Date, nullable=False),
    Column("pe", Numeric(18, 4), nullable=True),
    Column("pb", Numeric(18, 4), nullable=True),
    Column("roe", Numeric(18, 4), nullable=True),
    Column("revenue_yoy", Numeric(18, 4), nullable=True),
    Column("net_profit_yoy", Numeric(18, 4), nullable=True),
    Column("free_float_market_cap", Numeric(20, 2), nullable=True),
    Column("operating_cash_flow", Numeric(20, 2), nullable=True),
    Column("source_name", String(32), nullable=False),
    Column("interface_name", String(64), nullable=False),
    Column("fetched_at", DateTime(timezone=True), nullable=False),
    Column("batch_id", String(64), nullable=False),
    Column("payload_hash", String(64), nullable=True),
    Column("retry_count", Integer, nullable=False, default=0),
    Column("missing_field_count", Integer, nullable=False, default=0),
    Column("ingestion_status", String(16), nullable=False),
    Column("error_message", String(512), nullable=True),
    Column("raw_payload", JSON, nullable=True),
    Column("created_at", DateTime(timezone=True), nullable=False),
    UniqueConstraint("stock_code", "report_date", name="uk_raw_financial_code_report"),
    Index("idx_raw_financial_batch_id", "batch_id"),
    Index("idx_raw_financial_report_date", "report_date"),
)

raw_market_index_bars_table = Table(
    "raw_market_index_bars",
    RAW_TABLE_METADATA,
    Column("id", Integer, primary_key=True, autoincrement=True),
    Column("index_code", String(16), nullable=False),
    Column("index_name", String(64), nullable=False),
    Column("trade_date", Date, nullable=False),
    Column("open", Numeric(18, 4), nullable=True),
    Column("high", Numeric(18, 4), nullable=True),
    Column("low", Numeric(18, 4), nullable=True),
    Column("close", Numeric(18, 4), nullable=True),
    Column("amount", Numeric(20, 2), nullable=True),
    Column("source_name", String(32), nullable=False),
    Column("interface_name", String(64), nullable=False),
    Column("fetched_at", DateTime(timezone=True), nullable=False),
    Column("batch_id", String(64), nullable=False),
    Column("payload_hash", String(64), nullable=True),
    Column("retry_count", Integer, nullable=False, default=0),
    Column("ingestion_status", String(16), nullable=False),
    Column("error_message", String(512), nullable=True),
    Column("raw_payload", JSON, nullable=True),
    Column("created_at", DateTime(timezone=True), nullable=False),
    UniqueConstraint("index_code", "trade_date", name="uk_raw_index_code_date"),
    Index("idx_raw_index_trade_date", "trade_date"),
)

raw_industry_daily_stats_table = Table(
    "raw_industry_daily_stats",
    RAW_TABLE_METADATA,
    Column("id", Integer, primary_key=True, autoincrement=True),
    Column("industry_code", String(32), nullable=False),
    Column("industry_name", String(64), nullable=False),
    Column("trade_date", Date, nullable=False),
    Column("pct_change_20d", Numeric(10, 4), nullable=True),
    Column("rank_20d", Integer, nullable=True),
    Column("member_count", Integer, nullable=True),
    Column("source_name", String(32), nullable=False),
    Column("interface_name", String(64), nullable=False),
    Column("fetched_at", DateTime(timezone=True), nullable=False),
    Column("batch_id", String(64), nullable=False),
    Column("payload_hash", String(64), nullable=True),
    Column("retry_count", Integer, nullable=False, default=0),
    Column("ingestion_status", String(16), nullable=False),
    Column("error_message", String(512), nullable=True),
    Column("raw_payload", JSON, nullable=True),
    Column("created_at", DateTime(timezone=True), nullable=False),
    UniqueConstraint("industry_code", "trade_date", name="uk_raw_industry_code_date"),
    Index("idx_raw_industry_trade_date", "trade_date"),
)

data_ingestion_logs_table = Table(
    "data_ingestion_logs",
    RAW_TABLE_METADATA,
    Column("id", Integer, primary_key=True, autoincrement=True),
    Column("batch_id", String(64), nullable=False),
    Column("source_name", String(32), nullable=False),
    Column("interface_name", String(64), nullable=False),
    Column("target_scope", String(64), nullable=False),
    Column("trade_date", Date, nullable=True),
    Column("report_date", Date, nullable=True),
    Column("started_at", DateTime(timezone=True), nullable=False),
    Column("finished_at", DateTime(timezone=True), nullable=True),
    Column("success_count", Integer, nullable=False, default=0),
    Column("failure_count", Integer, nullable=False, default=0),
    Column("missing_field_count", Integer, nullable=False, default=0),
    Column("consecutive_failure_count", Integer, nullable=False, default=0),
    Column("window_failure_rate", Float, nullable=True),
    Column("is_incremental", Boolean, nullable=False, default=False),
    Column("is_complete", Boolean, nullable=False, default=False),
    Column("is_signal_eligible", Boolean, nullable=False, default=False),
    Column("circuit_breaker_opened", Boolean, nullable=False, default=False),
    Column("error_message", String(1024), nullable=True),
    Column("created_at", DateTime(timezone=True), nullable=False),
    Index("idx_ingestion_logs_batch_id", "batch_id"),
    Index("idx_ingestion_logs_trade_date", "trade_date"),
    Index("idx_ingestion_logs_interface_name", "interface_name"),
)

collector_checkpoints_table = Table(
    "collector_checkpoints",
    RAW_TABLE_METADATA,
    Column("id", Integer, primary_key=True, autoincrement=True),
    Column("job_type", String(64), nullable=False),
    Column("batch_id", String(64), nullable=False),
    Column("stock_code", String(16), nullable=False),
    Column("status", String(16), nullable=False),
    Column("last_success_date", Date, nullable=True),
    Column("retry_count", Integer, nullable=False, default=0),
    Column("error_message", String(512), nullable=True),
    Column("updated_at", DateTime(timezone=True), nullable=False),
    Column("created_at", DateTime(timezone=True), nullable=False),
    UniqueConstraint("job_type", "stock_code", name="uk_collector_checkpoints_job_stock"),
    Index("idx_collector_checkpoints_batch_id", "batch_id"),
    Index("idx_collector_checkpoints_status", "status"),
)


RAW_TABLES: dict[str, Table] = {
    "raw_stocks": raw_stocks_table,
    "raw_daily_bars": raw_daily_bars_table,
    "raw_financial_snapshots": raw_financial_snapshots_table,
    "raw_market_index_bars": raw_market_index_bars_table,
    "raw_industry_daily_stats": raw_industry_daily_stats_table,
    "data_ingestion_logs": data_ingestion_logs_table,
    "collector_checkpoints": collector_checkpoints_table,
}


def create_mysql_engine(settings: MySqlSettings, *, echo: bool = False) -> Engine:
    return create_engine(settings.to_sqlalchemy_url(), echo=echo, future=True)


def create_in_memory_engine() -> Engine:
    return create_engine("sqlite+pysqlite:///:memory:", future=True)


class RawDataWriter:
    def __init__(self, engine: Engine) -> None:
        self._engine = engine

    @property
    def engine(self) -> Engine:
        return self._engine

    def create_tables(self) -> None:
        RAW_TABLE_METADATA.create_all(self._engine)

    def upsert_rows(self, table_name: str, rows: list[dict[str, Any]]) -> int:
        if not rows:
            return 0

        table = RAW_TABLES[table_name]
        normalized_rows = [self._normalize_row(table_name, row) for row in rows]
        with self._engine.begin() as connection:
            for row in normalized_rows:
                delete_filters = self._build_delete_filters(table_name, table, row)
                if delete_filters:
                    connection.execute(table.delete().where(*delete_filters))
            connection.execute(table.insert(), normalized_rows)
        return len(normalized_rows)

    def append_log(self, row: dict[str, Any]) -> None:
        normalized = self._normalize_row("data_ingestion_logs", row)
        with self._engine.begin() as connection:
            connection.execute(data_ingestion_logs_table.insert(), [normalized])

    def upsert_checkpoint(self, row: dict[str, Any]) -> None:
        normalized = self._normalize_row("collector_checkpoints", row)
        table = collector_checkpoints_table
        with self._engine.begin() as connection:
            connection.execute(
                table.delete().where(
                    table.c.job_type == normalized["job_type"],
                    table.c.stock_code == normalized["stock_code"],
                )
            )
            connection.execute(table.insert(), [normalized])

    def load_checkpoints(self, job_type: str) -> dict[str, dict[str, Any]]:
        table = collector_checkpoints_table
        statement: Select[Any] = table.select().where(table.c.job_type == job_type)
        with self._engine.connect() as connection:
            rows = connection.execute(statement).mappings().all()
        return {str(row["stock_code"]): dict(row) for row in rows}

    def truncate_checkpoints(self, job_type: str) -> None:
        with self._engine.begin() as connection:
            connection.execute(
                collector_checkpoints_table.delete().where(collector_checkpoints_table.c.job_type == job_type)
            )

    def list_stock_codes(self, *, active_only: bool = True) -> list[str]:
        statement = select(raw_stocks_table.c.stock_code).distinct().order_by(raw_stocks_table.c.stock_code)
        if active_only:
            statement = statement.where(raw_stocks_table.c.is_active.is_(True))
        with self._engine.connect() as connection:
            rows = connection.execute(statement).scalars().all()
        return [
            code for code in (str(item).strip() for item in rows)
            if len(code) == 6 and code.isdigit()
        ]

    def list_missing_daily_bar_stock_codes(
        self,
        *,
        trade_date: date,
        latest_batch_only: bool = True,
        limit: int | None = None,
    ) -> list[str]:
        stocks = raw_stocks_table
        daily_bars = raw_daily_bars_table
        statement = (
            select(stocks.c.stock_code)
            .select_from(
                stocks.outerjoin(
                    daily_bars,
                    (daily_bars.c.stock_code == stocks.c.stock_code) & (daily_bars.c.trade_date == trade_date),
                )
            )
            .where(stocks.c.is_active.is_(True))
            .where(func.length(stocks.c.stock_code) == 6)
            .where(daily_bars.c.stock_code.is_(None))
            .order_by(stocks.c.stock_code)
        )
        if latest_batch_only:
            latest_fetched_at = select(stocks.c.fetched_at).order_by(stocks.c.fetched_at.desc()).limit(1)
            statement = statement.where(stocks.c.fetched_at == latest_fetched_at.scalar_subquery())
        if limit is not None:
            statement = statement.limit(limit)
        with self._engine.connect() as connection:
            rows = connection.execute(statement).scalars().all()
        return [str(item).strip() for item in rows if item]

    def has_log_since(self, target_scope: str, *, created_at_or_after: datetime) -> bool:
        statement = (
            select(data_ingestion_logs_table.c.id)
            .where(
                data_ingestion_logs_table.c.target_scope == target_scope,
                data_ingestion_logs_table.c.created_at >= created_at_or_after,
            )
            .limit(1)
        )
        with self._engine.connect() as connection:
            row = connection.execute(statement).first()
        return row is not None

    def get_latest_trade_date(self) -> date | None:
        statement = select(func.max(raw_daily_bars_table.c.trade_date))
        with self._engine.connect() as connection:
            value = connection.execute(statement).scalar_one_or_none()
        return value if isinstance(value, date) else None

    def is_trade_date_fully_collected(self, trade_date: date, *, scopes: list[str]) -> bool:
        if not scopes:
            return False

        table = data_ingestion_logs_table
        with self._engine.connect() as connection:
            for scope in scopes:
                statement = (
                    select(table.c.id)
                    .where(
                        table.c.target_scope == scope,
                        table.c.trade_date == trade_date,
                        table.c.is_complete.is_(True),
                    )
                    .order_by(table.c.created_at.desc())
                    .limit(1)
                )
                row = connection.execute(statement).first()
                if row is None:
                    return False

        return True

    def list_stock_codes_missing_industry(self, *, latest_batch_only: bool = True, limit: int | None = None) -> list[str]:
        statement = (
            select(raw_stocks_table.c.stock_code)
            .where(func.length(raw_stocks_table.c.stock_code) == 6)
            .where(
                not_(
                    or_(
                        raw_stocks_table.c.stock_code.like("sh%"),
                        raw_stocks_table.c.stock_code.like("sz%"),
                        raw_stocks_table.c.stock_code.like("bj%"),
                    )
                )
            )
            .where((raw_stocks_table.c.industry_name.is_(None)) | (raw_stocks_table.c.industry_name == ""))
            .order_by(raw_stocks_table.c.stock_code)
        )
        if latest_batch_only:
            latest_fetched_at = select(raw_stocks_table.c.fetched_at).order_by(raw_stocks_table.c.fetched_at.desc()).limit(1)
            statement = statement.where(raw_stocks_table.c.fetched_at == latest_fetched_at.scalar_subquery())
        if limit is not None:
            statement = statement.limit(limit)
        with self._engine.connect() as connection:
            rows = connection.execute(statement).scalars().all()
        return [str(item).strip() for item in rows if item]

    def update_stock_metadata(self, batch_id: str, rows: list[dict[str, Any]]) -> int:
        if not rows:
            return 0

        updated = 0
        with self._engine.begin() as connection:
            for row in rows:
                result = connection.execute(
                    update(raw_stocks_table)
                    .where(raw_stocks_table.c.batch_id == batch_id)
                    .where(raw_stocks_table.c.stock_code == row["stock_code"])
                    .values(
                        stock_name=row.get("stock_name"),
                        industry_name=row.get("industry_name"),
                        list_date=row.get("list_date"),
                    )
                )
                updated += int(result.rowcount or 0)
        return updated

    def acquire_advisory_lock(self, lock_name: str, *, timeout_seconds: int = 0) -> bool:
        if self._engine.dialect.name != "mysql":
            return True
        with self._engine.connect() as connection:
            result = connection.execute(
                text("SELECT GET_LOCK(:lock_name, :timeout_seconds)"),
                {"lock_name": lock_name, "timeout_seconds": timeout_seconds},
            ).scalar_one()
        return bool(result)

    def release_advisory_lock(self, lock_name: str) -> None:
        if self._engine.dialect.name != "mysql":
            return
        with self._engine.connect() as connection:
            connection.execute(
                text("SELECT RELEASE_LOCK(:lock_name)"),
                {"lock_name": lock_name},
            )

    @staticmethod
    def _build_delete_filters(table_name: str, table: Table, row: dict[str, Any]) -> list[Any]:
        if table_name == "raw_stocks":
            return [table.c.batch_id == row["batch_id"], table.c.stock_code == row["stock_code"]]
        if table_name == "raw_daily_bars":
            return [
                table.c.stock_code == row["stock_code"],
                table.c.trade_date == row["trade_date"],
                table.c.adjust_type == row["adjust_type"],
            ]
        if table_name == "raw_financial_snapshots":
            return [table.c.stock_code == row["stock_code"], table.c.report_date == row["report_date"]]
        if table_name == "raw_market_index_bars":
            return [table.c.index_code == row["index_code"], table.c.trade_date == row["trade_date"]]
        if table_name == "raw_industry_daily_stats":
            return [table.c.industry_code == row["industry_code"], table.c.trade_date == row["trade_date"]]
        return []

    @staticmethod
    def _normalize_row(table_name: str, row: dict[str, Any]) -> dict[str, Any]:
        normalized = dict(row)

        for key in ("trade_date", "report_date", "last_success_date"):
            if key in normalized and isinstance(normalized[key], datetime):
                normalized[key] = normalized[key].date()

        now = datetime.now(UTC)
        if "created_at" in RAW_TABLES[table_name].c and normalized.get("created_at") is None:
            normalized["created_at"] = now
        if table_name == "collector_checkpoints" and normalized.get("updated_at") is None:
            normalized["updated_at"] = now
        if table_name == "data_ingestion_logs" and normalized.get("started_at") is None:
            normalized["started_at"] = now

        for key, value in list(normalized.items()):
            if isinstance(value, float):
                normalized[key] = Decimal(str(value)) if key not in {"window_failure_rate"} else value
            elif key == "raw_payload":
                normalized[key] = RawDataWriter._to_json_compatible(value)

        return normalized

    @staticmethod
    def _to_json_compatible(value: Any) -> Any:
        if isinstance(value, float) and (math.isnan(value) or math.isinf(value)):
            return None
        if isinstance(value, datetime):
            return value.isoformat()
        if isinstance(value, date):
            return value.isoformat()
        if isinstance(value, Decimal):
            return str(value)
        if isinstance(value, dict):
            return {str(key): RawDataWriter._to_json_compatible(item) for key, item in value.items()}
        if isinstance(value, list):
            return [RawDataWriter._to_json_compatible(item) for item in value]
        if isinstance(value, tuple):
            return [RawDataWriter._to_json_compatible(item) for item in value]
        return value

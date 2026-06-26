from __future__ import annotations

from collections.abc import Iterable
from dataclasses import dataclass
from datetime import date
from typing import Any

import akshare as ak
from sqlalchemy import text

from stock_collector.mysql_writer import RawDataWriter


GENERIC_INDUSTRIES = {
    "制造业",
    "金融业",
    "房地产业",
    "建筑业",
    "农林牧渔业",
    "采矿业",
}


@dataclass(frozen=True)
class StockToRepair:
    stock_code: str
    stock_name: str
    current_industry_name: str


@dataclass(frozen=True)
class CandidateIndustryBackfillResult:
    ready: bool
    repaired_count: int
    scanned_count: int
    updates: tuple[str, ...]


def is_generic_industry_name(industry_name: str | None) -> bool:
    if not industry_name:
        return False

    value = industry_name.strip()
    return (len(value) >= 3 and value[1] == " " and value[0].isalpha()) or value in GENERIC_INDUSTRIES


def iter_rank_candidates(row: dict[str, Any]) -> Iterable[str]:
    for key in ("行业中类", "行业大类", "行业次类", "行业门类"):
        value = str(row.get(key) or "").strip()
        if value:
            yield value


def resolve_industry_name(stock_code: str, ranked_industry_names: set[str]) -> str | None:
    changes = ak.stock_industry_change_cninfo(
        symbol=stock_code,
        start_date="20000101",
        end_date="20300101",
    )
    if changes.empty:
        return None

    ordered = changes.sort_values(by="变更日期", ascending=False)
    for row in ordered.to_dict(orient="records"):
        for candidate in iter_rank_candidates(row):
            if candidate in ranked_industry_names:
                return candidate
    return None


def backfill_candidate_industries(
    writer: RawDataWriter,
    *,
    trade_date: date,
    snapshot_version: str = "end_of_day_final",
) -> CandidateIndustryBackfillResult:
    engine = writer.engine
    with engine.begin() as connection:
        candidate_rows = connection.execute(
            text(
                """
                select distinct t.stock_code, p.stock_name, p.industry_name
                from (
                    select stock_code
                    from strategy_candidates
                    where trade_date = :trade_date and snapshot_version = :snapshot_version
                    union
                    select stock_code
                    from strategy_trade_signals
                    where trade_date = :trade_date and snapshot_version = :snapshot_version
                ) t
                join market_stock_profiles p
                  on p.stock_code = t.stock_code
                 and p.trade_date = :trade_date
                """
            ),
            {"trade_date": trade_date, "snapshot_version": snapshot_version},
        ).mappings().all()

        if not candidate_rows:
            return CandidateIndustryBackfillResult(
                ready=False,
                repaired_count=0,
                scanned_count=0,
                updates=(),
            )

        repair_targets = [
            StockToRepair(
                stock_code=str(row["stock_code"]),
                stock_name=str(row["stock_name"]),
                current_industry_name=str(row["industry_name"] or ""),
            )
            for row in candidate_rows
            if is_generic_industry_name(str(row["industry_name"] or ""))
        ]

        ranked_industry_names = {
            str(row["industry_name"]).strip()
            for row in connection.execute(
                text(
                    """
                    select distinct industry_name
                    from market_industry_daily_stats
                    where trade_date = :trade_date
                    """
                ),
                {"trade_date": trade_date},
            ).mappings().all()
            if str(row["industry_name"] or "").strip()
        }

        updates: list[tuple[str, StockToRepair]] = []
        for target in repair_targets:
            resolved = resolve_industry_name(target.stock_code, ranked_industry_names)
            if not resolved or resolved == target.current_industry_name:
                continue
            updates.append((resolved, target))

        for resolved_industry_name, target in updates:
            params = {
                "industry_name": resolved_industry_name,
                "stock_code": target.stock_code,
                "trade_date": trade_date,
                "snapshot_version": snapshot_version,
            }
            connection.execute(
                text(
                    """
                    update market_stock_profiles
                    set industry_name = :industry_name
                    where stock_code = :stock_code and trade_date = :trade_date
                    """
                ),
                params,
            )
            connection.execute(
                text(
                    """
                    update strategy_candidates
                    set industry_name = :industry_name
                    where stock_code = :stock_code and trade_date = :trade_date and snapshot_version = :snapshot_version
                    """
                ),
                params,
            )
            connection.execute(
                text(
                    """
                    update strategy_trade_signals
                    set industry_name = :industry_name
                    where stock_code = :stock_code and trade_date = :trade_date and snapshot_version = :snapshot_version
                    """
                ),
                params,
            )

    return CandidateIndustryBackfillResult(
        ready=True,
        repaired_count=len(updates),
        scanned_count=len(repair_targets),
        updates=tuple(
            f"{target.stock_code} {target.stock_name}: {target.current_industry_name} -> {resolved_industry_name}"
            for resolved_industry_name, target in updates
        ),
    )

from __future__ import annotations

import argparse
from concurrent.futures import ThreadPoolExecutor
from concurrent.futures import as_completed
from collections.abc import Iterable
from dataclasses import dataclass
from datetime import date
from pathlib import Path
from typing import Any

import akshare as ak
import pandas as pd
import py_mini_racer
import requests
from akshare.stock.stock_industry_cninfo import _get_file_content_ths
from sqlalchemy import text

from stock_collector.akshare_client import AkshareClient
from stock_collector.mysql_writer import MySqlSettings
from stock_collector.mysql_writer import RawDataWriter
from stock_collector.mysql_writer import create_mysql_engine


GENERIC_INDUSTRIES = {
    "制造业",
    "金融业",
    "房地产业",
    "建筑业",
    "农林牧渔业",
    "采矿业",
}

INDUSTRY_ALIASES = {
    "IT与互联网服务": "软件开发",
    "办公服务与用品": "其他社会服务",
    "包装食品与肉类": "食品加工制造",
    "百货": "零售",
    "百货商店": "零售",
    "半导体产品": "半导体",
    "播控设备": "通信设备",
    "餐馆": "旅游及酒店",
    "餐饮": "旅游及酒店",
    "餐饮服务": "旅游及酒店",
    "电脑与电子产品零售": "零售",
    "电影与娱乐": "文化传媒",
    "动物保健": "农化制品",
    "动物保健Ⅱ": "农化制品",
    "动物保健Ⅲ": "农化制品",
    "动力煤": "煤炭开采加工",
    "多业态零售": "零售",
    "多元化零售": "零售",
    "纺织服装": "纺织制造",
    "纺织和服装": "纺织制造",
    "纺织品": "纺织制造",
    "纺织业": "纺织制造",
    "港口": "港口航运",
    "港口服务": "港口航运",
    "港口业": "港口航运",
    "高速公路": "公路铁路运输",
    "各种商业与专业服务": "其他社会服务",
    "公交": "公路铁路运输",
    "公路运输": "公路铁路运输",
    "公路运输业": "公路铁路运输",
    "公路与铁路": "公路铁路运输",
    "供热及其他": "电力",
    "供热或其他公用事业": "电力",
    "国防装备": "军工装备",
    "航空公司": "机场航运",
    "航空货运": "物流",
    "航空货运与物流": "物流",
    "航空机场": "机场航运",
    "航空运输": "机场航运",
    "航空运输业": "机场航运",
    "航天航空": "军工装备",
    "航运": "港口航运",
    "航运港口": "港口航运",
    "化学药": "化学制药",
    "化学原料药": "化学制药",
    "互联网软件与服务": "软件开发",
    "互联网信息服务": "软件开发",
    "机械设备": "通用设备",
    "机场": "机场航运",
    "机场服务": "机场航运",
    "计算机网络开发、维护与咨询": "软件开发",
    "检测服务": "其他社会服务",
    "焦煤": "煤炭开采加工",
    "焦炭": "煤炭开采加工",
    "焦炭加工": "煤炭开采加工",
    "酒店": "旅游及酒店",
    "酒店、餐馆与休闲": "旅游及酒店",
    "酒店餐饮": "旅游及酒店",
    "酒店餐饮与休闲": "旅游及酒店",
    "包装食品与肉类": "食品加工制造",
    "粮食及饲料加工业": "农产品加工",
    "粮食种植": "种植业与林业",
    "炼化及贸易": "石油加工贸易",
    "林业": "种植业与林业",
    "林业Ⅱ": "种植业与林业",
    "林业Ⅲ": "种植业与林业",
    "旅游": "旅游及酒店",
    "旅游及景区": "旅游及酒店",
    "旅游业": "旅游及酒店",
    "旅游综合": "旅游及酒店",
    "旅行社": "旅游及酒店",
    "煤炭": "煤炭开采加工",
    "煤炭开采": "煤炭开采加工",
    "煤炭开采和洗选业": "煤炭开采加工",
    "能源服务": "油气开采及服务",
    "能源设备与服务": "油气开采及服务",
    "其他IT与互联网服务": "软件开发",
    "其他家用轻工": "家居用品",
    "其他零售": "零售",
    "其他零售业": "零售",
    "其他农产品": "农产品加工",
    "其他轻工制造": "家居用品",
    "其他商业服务": "其他社会服务",
    "其他商业服务与用品": "其他社会服务",
    "其他水上运输业": "港口航运",
    "其他文教用品制造业": "家居用品",
    "其他休闲服务": "其他社会服务",
    "其他专业服务": "其他社会服务",
    "其他专业、科研服务业": "其他社会服务",
    "其他专营零售": "零售",
    "汽车服务": "汽车服务及其他",
    "汽车综合服务": "汽车服务及其他",
    "汽车零配件": "汽车零部件",
    "汽车零配件与设备": "汽车零部件",
    "汽车与汽车零配件": "汽车零部件",
    "汽油贸易": "石油加工贸易",
    "轻工制造": "家居用品",
    "人工景点": "旅游及酒店",
    "日用百货零售业": "零售",
    "国防装备": "军工装备",
    "航天航空": "军工装备",
    "软件开发及服务": "软件开发",
    "商业服务与用品": "其他社会服务",
    "商业物业经营": "零售",
    "商业用品与服务": "其他社会服务",
    "商务服务业": "其他社会服务",
    "商贸零售": "零售",
    "社会服务": "其他社会服务",
    "社会服务业": "其他社会服务",
    "石油和天然气开采服务业": "油气开采及服务",
    "石油天然气": "石油加工贸易",
    "石油天然气的炼制与销售": "石油加工贸易",
    "石油天然气的勘探与生产": "油气开采及服务",
    "石油贸易": "石油加工贸易",
    "石油与天然气": "石油加工贸易",
    "石油与天然气的炼制和营销": "石油加工贸易",
    "食品": "食品加工制造",
    "食品分销商": "零售",
    "食品加工业": "食品加工制造",
    "食品、饮料": "食品加工制造",
    "食品、饮料与烟草": "食品加工制造",
    "食品与日用品零售": "零售",
    "食品与主要用品零售": "零售",
    "食品制造业": "食品加工制造",
    "食品综合": "食品加工制造",
    "水产饲料": "农产品加工",
    "水产养殖": "养殖业",
    "水上运输": "港口航运",
    "水上运输业": "港口航运",
    "塑料": "塑料制品",
    "铁路公路": "公路铁路运输",
    "铁路运输": "公路铁路运输",
    "铁路运输业": "公路铁路运输",
    "通信服务业": "通信服务",
    "网络服务": "软件开发",
    "文化艺术业": "文化传媒",
    "文教、工美、体育和娱乐用品制造业": "家居用品",
    "文教体育用品制造业": "家居用品",
    "文娱用品": "家居用品",
    "休闲服务": "旅游及酒店",
    "休闲设施": "旅游及酒店",
    "畜牧产品": "农产品加工",
    "畜禽饲料": "农产品加工",
    "养殖": "养殖业",
    "药品": "化学制药",
    "药品及生物科技": "化学制药",
    "一般零售": "零售",
    "医疗保健业": "化学制药",
    "医药": "化学制药",
    "医药、生物制品": "化学制药",
    "医药生物": "化学制药",
    "医药卫生": "化学制药",
    "医药制造业": "化学制药",
    "仪器仪表": "自动化设备",
    "仪器仪表制造业": "自动化设备",
    "饮料": "饮料制造",
    "饮料乳品": "饮料制造",
    "油品石化贸易": "石油加工贸易",
    "油气流通及其他": "石油加工贸易",
    "油气钻采服务": "油气开采及服务",
    "娱乐用品": "家居用品",
    "原料药": "化学制药",
    "造纸、印刷": "家居用品",
    "造纸印刷": "家居用品",
    "专业服务": "其他社会服务",
    "专业技术服务业": "其他社会服务",
    "专业连锁": "零售",
    "专业连锁Ⅱ": "零售",
    "专业连锁Ⅲ": "零售",
    "专业零售": "零售",
    "专业市场": "零售",
    "专营零售": "零售",
    "种植业": "种植业与林业",
    "种子": "种植业与林业",
    "种子生产": "种植业与林业",
    "珠宝首饰": "家居用品",
    "自然景点": "旅游及酒店",
    "自然景区": "旅游及酒店",
    "摩托车": "汽车整车",
    "摩托车及其他": "汽车整车",
    "其他储能设备": "其他电源设备",
    "肉制品": "食品加工制造",
}

INDUSTRY_LEVEL_SUFFIXES = ("III", "II", "I", "Ⅲ", "Ⅱ", "Ⅰ")
CNINFO_STOCK_INDUSTRY_CHANGE_URL = "https://webapi.cninfo.com.cn/api/stock/p_stock2110"


@dataclass(frozen=True)
class BackfillPlan:
    scanned_stock_count: int
    matched_stock_count: int
    failed_stock_count: int
    generic_latest_stock_count: int
    generic_profile_row_count: int
    generic_candidate_row_count: int
    generic_signal_row_count: int
    sample_updates: tuple[str, ...]
    sample_failures: tuple[str, ...]


def is_generic_industry_name(industry_name: str | None) -> bool:
    if not industry_name:
        return False

    value = industry_name.strip()
    return (len(value) >= 3 and value[1] == " " and value[0].isalpha()) or value in GENERIC_INDUSTRIES


def iter_board_member_industries() -> Iterable[tuple[str, str]]:
    client = AkshareClient(provider=ak)
    boards = client._to_records(ak.stock_board_industry_name_em())  # noqa: SLF001
    for board in boards:
        board_name = str(board.get("板块名称") or board.get("name") or "").strip()
        board_code = str(board.get("板块代码") or board.get("code") or "").strip()
        if not board_name:
            continue

        symbol = board_code or board_name
        try:
            members = client._to_records(ak.stock_board_industry_cons_em(symbol=symbol))  # noqa: SLF001
        except Exception:  # noqa: BLE001
            continue

        for member in members:
            stock_code = client._extract_stock_code(member)  # noqa: SLF001
            if stock_code:
                yield stock_code, board_name


def build_stock_industry_map() -> dict[str, str]:
    mapping: dict[str, str] = {}
    for stock_code, industry_name in iter_board_member_industries():
        mapping.setdefault(stock_code, industry_name)
    return mapping


def iter_cninfo_industry_candidates(row: dict[str, Any]) -> Iterable[str]:
    for key in ("行业中类", "行业大类", "行业次类", "行业门类"):
        value = str(row.get(key) or "").strip()
        if value and value.lower() != "nan":
            yield value


def resolve_ranked_industry_name(candidate: str, ranked_industry_names: set[str]) -> str | None:
    if candidate in ranked_industry_names:
        return candidate

    alias = INDUSTRY_ALIASES.get(candidate)
    if alias in ranked_industry_names:
        return alias

    for suffix in INDUSTRY_LEVEL_SUFFIXES:
        if candidate.endswith(suffix):
            normalized = candidate[: -len(suffix)]
            if normalized in ranked_industry_names:
                return normalized
            alias = INDUSTRY_ALIASES.get(normalized)
            if alias in ranked_industry_names:
                return alias

    return None


def build_cninfo_headers() -> dict[str, str]:
    js_code = py_mini_racer.MiniRacer()
    js_code.eval(_get_file_content_ths("cninfo.js"))
    accept_enckey = js_code.call("getResCode1")
    return {
        "Accept": "*/*",
        "Accept-Encoding": "gzip, deflate",
        "Accept-Language": "zh-CN,zh;q=0.9,en;q=0.8",
        "Cache-Control": "no-cache",
        "Content-Length": "0",
        "Host": "webapi.cninfo.com.cn",
        "Accept-Enckey": accept_enckey,
        "Origin": "https://webapi.cninfo.com.cn",
        "Pragma": "no-cache",
        "Proxy-Connection": "keep-alive",
        "Referer": "https://webapi.cninfo.com.cn/",
        "User-Agent": (
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
            "(KHTML, like Gecko) Chrome/93.0.4577.63 Safari/537.36"
        ),
        "X-Requested-With": "XMLHttpRequest",
    }


def fetch_cninfo_changes_direct(stock_code: str, headers: dict[str, str], *, timeout: float) -> pd.DataFrame:
    params = {
        "scode": stock_code,
        "sdate": "2000-01-01",
        "edate": "2030-01-01",
    }
    response = requests.post(CNINFO_STOCK_INDUSTRY_CHANGE_URL, params=params, headers=headers, timeout=timeout)
    response.raise_for_status()
    records = response.json().get("records") or []
    data_frame = pd.DataFrame(records)
    if data_frame.empty:
        return data_frame

    columns_map = {
        "ORGNAME": "机构名称",
        "SECCODE": "证券代码",
        "SECNAME": "新证券简称",
        "VARYDATE": "变更日期",
        "F001V": "分类标准编码",
        "F002V": "分类标准",
        "F003V": "行业编码",
        "F004V": "行业门类",
        "F005V": "行业次类",
        "F006V": "行业大类",
        "F007V": "行业中类",
    }
    data_frame.rename(columns=columns_map, inplace=True)
    data_frame["变更日期"] = pd.to_datetime(data_frame["变更日期"], errors="coerce").dt.date
    return data_frame


def resolve_from_cninfo(
    stock_code: str,
    ranked_industry_names: set[str],
    *,
    headers: dict[str, str] | None = None,
    timeout: float = 10,
) -> str | None:
    try:
        if headers is None:
            changes = ak.stock_industry_change_cninfo(
                symbol=stock_code,
                start_date="20000101",
                end_date="20300101",
            )
        else:
            changes = fetch_cninfo_changes_direct(stock_code, headers, timeout=timeout)
    except Exception:  # noqa: BLE001
        return None
    if changes.empty:
        return None

    ordered = changes.sort_values(by="变更日期", ascending=False)
    for row in ordered.to_dict(orient="records"):
        for candidate in iter_cninfo_industry_candidates(row):
            resolved = resolve_ranked_industry_name(candidate, ranked_industry_names)
            if resolved:
                return resolved
    return None


def fetch_generic_stock_codes(writer: RawDataWriter, *, source: str = "all") -> set[str]:
    if source == "domain":
        query = text(
            """
            select stock_code, industry_name from strategy_candidates
            union
            select stock_code, industry_name from strategy_trade_signals
            """
        )
    else:
        query = text(
            """
            select stock_code, industry_name from latest_raw_stocks
            union
            select stock_code, industry_name from market_stock_profiles
            union
            select stock_code, industry_name from strategy_candidates
            union
            select stock_code, industry_name from strategy_trade_signals
            """
        )
    with writer.engine.connect() as connection:
        rows = connection.execute(query).mappings().all()

    return {
        str(row["stock_code"])
        for row in rows
        if is_generic_industry_name(str(row["industry_name"] or ""))
    }


def fetch_ranked_industry_names(writer: RawDataWriter) -> set[str]:
    query = text(
        """
        select distinct industry_name
        from market_industry_daily_stats
        where trade_date = (select max(trade_date) from market_industry_daily_stats)
        """
    )
    with writer.engine.connect() as connection:
        rows = connection.execute(query).mappings().all()

    return {str(row["industry_name"]).strip() for row in rows if str(row["industry_name"] or "").strip()}


def count_generic_rows(writer: RawDataWriter, table_name: str) -> int:
    query = text(f"select stock_code, industry_name from {table_name}")  # noqa: S608
    with writer.engine.connect() as connection:
        rows = connection.execute(query).mappings().all()
    return sum(1 for row in rows if is_generic_industry_name(str(row["industry_name"] or "")))


def apply_updates(
    writer: RawDataWriter,
    mapping: dict[str, str],
    *,
    dry_run: bool,
    scanned_stock_count: int = 0,
    failed_stock_count: int = 0,
    failed_stock_codes: tuple[str, ...] = (),
) -> BackfillPlan:
    generic_stock_codes = fetch_generic_stock_codes(writer)
    matched = {stock_code: mapping[stock_code] for stock_code in generic_stock_codes if stock_code in mapping}
    sample_updates = tuple(f"{code} -> {industry}" for code, industry in sorted(matched.items())[:20])

    if not dry_run and matched:
        update_statements = (
            "update latest_raw_stocks set industry_name = :industry_name where stock_code = :stock_code",
            "update market_stock_profiles set industry_name = :industry_name where stock_code = :stock_code",
            "update strategy_candidates set industry_name = :industry_name where stock_code = :stock_code",
            "update strategy_trade_signals set industry_name = :industry_name where stock_code = :stock_code",
        )
        with writer.engine.begin() as connection:
            for stock_code, industry_name in matched.items():
                params: dict[str, Any] = {"stock_code": stock_code, "industry_name": industry_name}
                for statement in update_statements:
                    connection.execute(text(statement), params)

    return BackfillPlan(
        scanned_stock_count=scanned_stock_count,
        matched_stock_count=len(matched),
        failed_stock_count=failed_stock_count,
        generic_latest_stock_count=count_generic_rows(writer, "latest_raw_stocks"),
        generic_profile_row_count=count_generic_rows(writer, "market_stock_profiles"),
        generic_candidate_row_count=count_generic_rows(writer, "strategy_candidates"),
        generic_signal_row_count=count_generic_rows(writer, "strategy_trade_signals"),
        sample_updates=sample_updates,
        sample_failures=failed_stock_codes[:20],
    )


def build_cninfo_stock_industry_map(
    writer: RawDataWriter,
    *,
    limit: int | None = None,
    offset: int = 0,
    skip_stock_codes: set[str] | None = None,
    stock_source: str = "all",
    workers: int = 1,
    request_timeout: float = 10,
) -> dict[str, str]:
    skip_stock_codes = skip_stock_codes or set()
    generic_stock_codes = sorted(fetch_generic_stock_codes(writer, source=stock_source))
    if skip_stock_codes:
        generic_stock_codes = [stock_code for stock_code in generic_stock_codes if stock_code not in skip_stock_codes]
    if offset > 0:
        generic_stock_codes = generic_stock_codes[offset:]
    if limit is not None:
        generic_stock_codes = generic_stock_codes[:limit]

    ranked_industry_names = fetch_ranked_industry_names(writer)
    mapping: dict[str, str] = {}
    failed_stock_codes: list[str] = []
    if workers <= 1:
        for stock_code in generic_stock_codes:
            resolved = resolve_from_cninfo(stock_code, ranked_industry_names, timeout=request_timeout)
            if resolved:
                mapping[stock_code] = resolved
            else:
                failed_stock_codes.append(stock_code)
    else:
        headers = build_cninfo_headers()
        with ThreadPoolExecutor(max_workers=workers) as executor:
            futures = {
                executor.submit(
                    resolve_from_cninfo,
                    stock_code,
                    ranked_industry_names,
                    headers=headers,
                    timeout=request_timeout,
                ): stock_code
                for stock_code in generic_stock_codes
            }
            for future in as_completed(futures):
                stock_code = futures[future]
                try:
                    resolved = future.result()
                except Exception:  # noqa: BLE001
                    resolved = None
                if resolved:
                    mapping[stock_code] = resolved
                else:
                    failed_stock_codes.append(stock_code)
    build_cninfo_stock_industry_map.scanned_count = len(generic_stock_codes)  # type: ignore[attr-defined]
    build_cninfo_stock_industry_map.failed_codes = tuple(failed_stock_codes)  # type: ignore[attr-defined]
    return mapping


def read_stock_code_file(path: Path | None) -> set[str]:
    if path is None or not path.exists():
        return set()

    return {
        line.strip()
        for line in path.read_text(encoding="utf-8").splitlines()
        if line.strip() and not line.lstrip().startswith("#")
    }


def append_stock_code_file(path: Path | None, stock_codes: Iterable[str]) -> None:
    if path is None:
        return

    existing = read_stock_code_file(path)
    new_stock_codes = [stock_code for stock_code in sorted(set(stock_codes)) if stock_code not in existing]
    if not new_stock_codes:
        return

    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8", newline="\n") as file:
        for stock_code in new_stock_codes:
            file.write(f"{stock_code}\n")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Backfill generic stock industries from Eastmoney board members.")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=3306)
    parser.add_argument("--user", default="stock_decision")
    parser.add_argument("--password", default="stock_decision_dev")
    parser.add_argument("--database", default="stock_decision")
    parser.add_argument("--source", choices=("board-members", "cninfo"), default="board-members")
    parser.add_argument("--stock-source", choices=("all", "domain"), default="all")
    parser.add_argument("--limit", type=int, default=None)
    parser.add_argument("--offset", type=int, default=0)
    parser.add_argument("--workers", type=int, default=1)
    parser.add_argument("--request-timeout", type=float, default=10)
    parser.add_argument("--unresolved-file", type=Path, default=None)
    parser.add_argument("--apply", action="store_true", help="Apply updates. Omit for dry-run.")
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    settings = MySqlSettings(
        host=args.host,
        port=args.port,
        database=args.database,
        user=args.user,
        password=args.password,
    )
    writer = RawDataWriter(create_mysql_engine(settings))
    if args.source == "cninfo":
        skip_stock_codes = read_stock_code_file(args.unresolved_file)
        mapping = build_cninfo_stock_industry_map(
            writer,
            limit=args.limit,
            offset=args.offset,
            skip_stock_codes=skip_stock_codes,
            stock_source=args.stock_source,
            workers=max(args.workers, 1),
            request_timeout=max(args.request_timeout, 1),
        )
        scanned_count = getattr(build_cninfo_stock_industry_map, "scanned_count", 0)
        failed_codes = getattr(build_cninfo_stock_industry_map, "failed_codes", ())
    else:
        mapping = build_stock_industry_map()
        scanned_count = 0
        failed_codes = ()
    plan = apply_updates(
        writer,
        mapping,
        dry_run=not args.apply,
        scanned_stock_count=scanned_count,
        failed_stock_count=len(failed_codes),
        failed_stock_codes=failed_codes,
    )
    if args.apply:
        append_stock_code_file(args.unresolved_file, failed_codes)

    mode = "APPLY" if args.apply else "DRY-RUN"
    print(f"{mode}: scanned {plan.scanned_stock_count} generic stocks from {args.source}.")
    print(f"{mode}: matched {plan.matched_stock_count} generic stocks from {args.source}.")
    print(f"failed/unresolved stock count: {plan.failed_stock_count}")
    if plan.sample_failures:
        print("failed/unresolved sample: " + ", ".join(plan.sample_failures))
    print(f"generic latest_raw_stocks rows: {plan.generic_latest_stock_count}")
    print(f"generic market_stock_profiles rows: {plan.generic_profile_row_count}")
    print(f"generic strategy_candidates rows: {plan.generic_candidate_row_count}")
    print(f"generic strategy_trade_signals rows: {plan.generic_signal_row_count}")
    for item in plan.sample_updates:
        print(item)


if __name__ == "__main__":
    main()

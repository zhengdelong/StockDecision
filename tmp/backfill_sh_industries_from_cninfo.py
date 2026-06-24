from __future__ import annotations

from concurrent.futures import ThreadPoolExecutor
from concurrent.futures import as_completed
import os
from pathlib import Path
from typing import Any

import pymysql
import py_mini_racer
import requests
from akshare.datasets import get_ths_js

ROOT = Path(__file__).resolve().parent
ENV_PATH = ROOT.parent / ".env"
INPUT_PATH = ROOT / "missing_sh_codes.txt"
OUTPUT_SQL_PATH = ROOT / "backfill_sh_industries.sql"
OUTPUT_FAIL_PATH = ROOT / "backfill_sh_industries_failed.txt"
MAX_WORKERS = 8
CNINFO_URL = "https://webapi.cninfo.com.cn/api/sysapi/p_sysapi1133"
CNINFO_HEADERS: dict[str, str] | None = None


def main() -> None:
    global CNINFO_HEADERS  # noqa: PLW0603
    CNINFO_HEADERS = build_cninfo_headers()

    codes = [line.strip() for line in INPUT_PATH.read_text(encoding="utf-8").splitlines() if line.strip()]
    results: list[tuple[str, str | None, str | None]] = []
    failed_codes: list[str] = []

    # 巨潮接口逐只查询，使用小并发提高吞吐，同时避免把上游直接打挂。
    with ThreadPoolExecutor(max_workers=MAX_WORKERS) as executor:
        future_map = {executor.submit(fetch_stock_metadata, code): code for code in codes}
        for future in as_completed(future_map):
            code = future_map[future]
            try:
                industry_name, list_date = future.result()
            except Exception:  # noqa: BLE001
                failed_codes.append(code)
                continue

            if not industry_name:
                failed_codes.append(code)
                continue

            results.append((code, industry_name, list_date))

    write_sql(results)
    apply_updates(results)
    OUTPUT_FAIL_PATH.write_text("\n".join(failed_codes), encoding="utf-8")
    print(
        {
            "total_codes": len(codes),
            "success_count": len(results),
            "failed_count": len(failed_codes),
            "output_sql": str(OUTPUT_SQL_PATH),
            "failed_codes_file": str(OUTPUT_FAIL_PATH),
        }
    )


def fetch_stock_metadata(code: str) -> tuple[str | None, str | None]:
    assert CNINFO_HEADERS is not None
    response = requests.post(
        CNINFO_URL,
        params={"scode": code},
        headers=CNINFO_HEADERS,
        timeout=20,
    )
    response.raise_for_status()
    payload = response.json()
    records = payload.get("records") or []
    if not records:
        return None, None

    row: dict[str, Any] = records[0]
    industry_name = normalize_text(row.get("F032V"))
    list_date = normalize_text(row.get("F006D"))
    return industry_name, list_date


def write_sql(results: list[tuple[str, str | None, str | None]]) -> None:
    lines = ["START TRANSACTION;"]
    for code, industry_name, list_date in sorted(results):
        safe_industry = escape_sql_text(industry_name or "")
        list_date_sql = f"'{list_date}'" if list_date else "list_date"
        lines.append(
            "UPDATE raw_stocks "
            f"SET industry_name='{safe_industry}', list_date=COALESCE(list_date, {list_date_sql}) "
            f"WHERE stock_code='{code}' "
            "AND fetched_at=(SELECT latest_fetched_at FROM (SELECT MAX(fetched_at) AS latest_fetched_at FROM raw_stocks) AS latest_batch) "
            "AND (industry_name IS NULL OR industry_name='');"
        )
    lines.append("COMMIT;")
    OUTPUT_SQL_PATH.write_text("\n".join(lines) + "\n", encoding="utf-8")


def normalize_text(value: Any) -> str | None:
    if value is None:
        return None
    text = str(value).strip()
    return text or None


def escape_sql_text(value: str) -> str:
    return value.replace("\\", "\\\\").replace("'", "''")


def build_cninfo_headers() -> dict[str, str]:
    js_code = py_mini_racer.MiniRacer()
    js_content = Path(get_ths_js("cninfo.js")).read_text(encoding="utf-8")
    js_code.eval(js_content)
    mcode = js_code.call("getResCode1")
    return {
        "Accept": "*/*",
        "Accept-Encoding": "gzip, deflate",
        "Accept-Language": "zh-CN,zh;q=0.9,en;q=0.8",
        "Cache-Control": "no-cache",
        "Content-Length": "0",
        "Host": "webapi.cninfo.com.cn",
        "Accept-Enckey": mcode,
        "Origin": "https://webapi.cninfo.com.cn",
        "Pragma": "no-cache",
        "Proxy-Connection": "keep-alive",
        "Referer": "https://webapi.cninfo.com.cn/",
        "X-Requested-With": "XMLHttpRequest",
    }


def apply_updates(results: list[tuple[str, str | None, str | None]]) -> None:
    if not results:
        return

    env = load_env()
    host = env.get("MYSQL_HOST", "127.0.0.1")
    if host == "mysql":
        host = "127.0.0.1"

    connection = pymysql.connect(
        host=host,
        port=int(env.get("MYSQL_PORT", "3306")),
        user=env.get("MYSQL_USER", "stock_decision"),
        password=env.get("MYSQL_PASSWORD", "stock_decision_dev"),
        database=env.get("MYSQL_DATABASE", "stock_decision"),
        charset="utf8mb4",
        autocommit=False,
    )
    try:
        with connection.cursor() as cursor:
            cursor.executemany(
                """
                UPDATE raw_stocks
                SET industry_name=%s, list_date=COALESCE(list_date, %s)
                WHERE stock_code=%s
                  AND fetched_at=(
                      SELECT latest_fetched_at
                      FROM (SELECT MAX(fetched_at) AS latest_fetched_at FROM raw_stocks) AS latest_batch
                  )
                  AND (industry_name IS NULL OR industry_name='')
                """,
                [(industry_name, list_date, code) for code, industry_name, list_date in results],
            )
        connection.commit()
    finally:
        connection.close()


def load_env() -> dict[str, str]:
    values: dict[str, str] = {}
    for line in ENV_PATH.read_text(encoding="utf-8").splitlines():
        text = line.strip()
        if not text or text.startswith("#") or "=" not in text:
            continue
        key, value = text.split("=", maxsplit=1)
        values[key.strip()] = value.strip()
    values.update({key: value for key, value in os.environ.items() if key in values or key.startswith("MYSQL_")})
    return values


if __name__ == "__main__":
    main()

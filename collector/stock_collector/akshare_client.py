from __future__ import annotations

from dataclasses import dataclass
from datetime import date
from datetime import timedelta
from typing import Any

class AkshareClientError(RuntimeError):
    pass


@dataclass(frozen=True)
class IndexDefinition:
    code: str
    name: str
    symbol: str


DEFAULT_INDEX_DEFINITIONS = (
    IndexDefinition(code="000300", name="沪深300", symbol="sh000300"),
    IndexDefinition(code="000905", name="中证500", symbol="sh000905"),
    IndexDefinition(code="399006", name="创业板指", symbol="sz399006"),
)


@dataclass
class AkshareClient:
    """Thin boundary around AKShare so the rest of the collector stays testable."""

    provider: Any | None = None

    def health(self) -> dict[str, str]:
        try:
            self._get_provider()
        except AkshareClientError:
            return {"source": "akshare", "status": "not_configured"}
        return {"source": "akshare", "status": "ready"}

    def fetch_stock_spot_snapshot(self) -> list[dict[str, Any]]:
        provider = self._get_provider()
        rows = self._to_records(provider.stock_zh_a_spot())
        industry_map = self._build_stock_metadata_map(provider)
        if not industry_map:
            return rows

        enriched_rows: list[dict[str, Any]] = []
        for row in rows:
            stock_code = self._extract_stock_code(row)
            metadata = industry_map.get(stock_code)
            if metadata is None:
                enriched_rows.append(row)
                continue

            enriched = dict(row)
            if not enriched.get("所处行业") and not enriched.get("industry"):
                enriched["所处行业"] = metadata.get("industry_name")
            if not enriched.get("上市时间") and not enriched.get("list_date"):
                enriched["上市时间"] = metadata.get("list_date")
            enriched_rows.append(enriched)
        return enriched_rows

    def fetch_stock_metadata_from_company_profile(self, stock_code: str) -> dict[str, Any] | None:
        provider = self._get_provider()
        if not hasattr(provider, "stock_profile_cninfo"):
            return None

        try:
            rows = self._to_records(provider.stock_profile_cninfo(symbol=stock_code))
        except Exception:  # noqa: BLE001
            return None

        if not rows:
            return None

        row = rows[0]
        industry_name = (
            row.get("所属行业")
            or row.get("所处行业")
            or row.get("industry_name")
            or row.get("industry")
        )
        list_date = row.get("上市日期") or row.get("上市时间") or row.get("list_date")
        stock_name = row.get("A股简称") or row.get("证券简称") or row.get("名称") or row.get("name")

        normalized_industry = str(industry_name).strip() if industry_name not in (None, "") else None
        normalized_list_date = list_date if list_date not in (None, "") else None
        normalized_stock_name = str(stock_name).strip() if stock_name not in (None, "") else None
        if normalized_industry is None and normalized_list_date is None and normalized_stock_name is None:
            return None

        return {
            "stock_code": stock_code,
            "stock_name": normalized_stock_name,
            "industry_name": normalized_industry,
            "list_date": normalized_list_date,
        }

    def fetch_stock_metadata_batch(self, stock_codes: list[str], *, limit: int | None = None) -> dict[str, dict[str, Any]]:
        metadata: dict[str, dict[str, Any]] = {}
        if limit is not None:
            stock_codes = stock_codes[:limit]

        for stock_code in stock_codes:
            item = self.fetch_stock_metadata_from_company_profile(stock_code)
            if item is None:
                continue
            metadata[stock_code] = item
        return metadata

    def fetch_daily_bars(self, stock_code: str, start_date: str, end_date: str, *, adjust: str = "qfq") -> list[dict[str, Any]]:
        provider = self._get_provider()
        if hasattr(provider, "stock_zh_a_hist"):
            try:
                frame = provider.stock_zh_a_hist(
                    symbol=stock_code,
                    period="daily",
                    start_date=start_date,
                    end_date=end_date,
                    adjust=adjust,
                )
                rows = self._to_records(frame)
                filtered_rows = self._filter_rows_by_date(rows, start_date=start_date, end_date=end_date)
                return [self._map_daily_bar_row(row) for row in filtered_rows]
            except Exception:  # noqa: BLE001
                # 历史日线主接口失败时再退回旧接口，避免在高频补拉场景里无限重试多套源。
                pass

        try:
            frame = provider.stock_zh_a_daily(
                symbol=self._to_market_symbol(stock_code),
                start_date=start_date,
                end_date=end_date,
                adjust=adjust,
            )
        except KeyError as exc:
            # Some AKShare symbols intermittently return malformed payloads that
            # surface as a missing "date" column inside the provider.
            if str(exc).strip("'\"") == "date":
                return []
            raise
        rows = self._to_records(frame)
        filtered_rows = self._filter_rows_by_date(rows, start_date=start_date, end_date=end_date)
        return [self._map_daily_bar_row(row) for row in filtered_rows]

    def fetch_index_bars(self, index_symbol: str, start_date: str, end_date: str) -> list[dict[str, Any]]:
        provider = self._get_provider()

        if hasattr(provider, "stock_zh_index_daily"):
            frame = provider.stock_zh_index_daily(symbol=index_symbol)
            rows = self._to_records(frame)
            filtered_rows = self._filter_rows_by_date(rows, start_date=start_date, end_date=end_date)
            return [self._map_index_daily_row(row) for row in filtered_rows]
        if hasattr(provider, "index_zh_a_hist"):
            frame = provider.index_zh_a_hist(
                symbol=index_symbol,
                period="daily",
                start_date=start_date,
                end_date=end_date,
            )
            return self._to_records(frame)
        raise AkshareClientError("No supported AKShare index interface is available.")

    def fetch_financial_snapshots(self, stock_code: str) -> list[dict[str, Any]]:
        provider = self._get_provider()
        try:
            frame = provider.stock_financial_abstract_ths(symbol=stock_code, indicator="按报告期")
            rows = self._to_records(frame)
            if rows:
                return [self._map_financial_snapshot_row(row) for row in rows]
        except Exception:  # noqa: BLE001
            pass

        frame = provider.stock_financial_report_sina(
            stock=self._to_market_symbol(stock_code),
            symbol="资产负债表",
        )
        rows = self._to_records(frame)
        return [self._map_financial_report_row(row) for row in rows]

    def fetch_industry_daily_stats(self) -> list[dict[str, Any]]:
        provider = self._get_provider()
        boards = self._to_records(provider.stock_board_industry_name_ths())
        if not boards:
            raise AkshareClientError("THS industry board list returned no rows.")

        end_date = date.today()
        start_date = end_date - timedelta(days=60)
        stats: list[dict[str, Any]] = []
        for board in boards:
            board_name = str(board.get("name") or "").strip()
            board_code = str(board.get("code") or "").strip()
            if not board_name or not board_code:
                continue
            history_rows = self._to_records(
                provider.stock_board_industry_index_ths(
                    symbol=board_name,
                    start_date=start_date.strftime("%Y%m%d"),
                    end_date=end_date.strftime("%Y%m%d"),
                )
            )
            stat = self._build_industry_stat_row(board_code=board_code, board_name=board_name, history_rows=history_rows)
            if stat is not None:
                stats.append(stat)

        ranked = sorted(
            stats,
            key=lambda item: item.get("pct_change_20d") if item.get("pct_change_20d") is not None else float("-inf"),
            reverse=True,
        )
        for index, item in enumerate(ranked, start=1):
            item["rank_20d"] = index
        return ranked

    def _get_provider(self) -> Any:
        if self.provider is not None:
            return self.provider

        try:
            import akshare as ak  # type: ignore
        except ImportError as exc:
            raise AkshareClientError("AKShare is not installed.") from exc

        return ak

    @staticmethod
    def _to_records(payload: Any) -> list[dict[str, Any]]:
        if payload is None:
            return []
        if isinstance(payload, list):
            return payload
        if hasattr(payload, "to_dict"):
            return payload.to_dict(orient="records")
        if isinstance(payload, tuple):
            return [dict(item) for item in payload]
        raise AkshareClientError(f"Unsupported payload type: {type(payload)!r}")

    @staticmethod
    def _filter_rows_by_date(rows: list[dict[str, Any]], *, start_date: str, end_date: str) -> list[dict[str, Any]]:
        start = date.fromisoformat(f"{start_date[0:4]}-{start_date[4:6]}-{start_date[6:8]}")
        end = date.fromisoformat(f"{end_date[0:4]}-{end_date[4:6]}-{end_date[6:8]}")
        filtered: list[dict[str, Any]] = []
        for row in rows:
            raw_trade_date = row.get("date") or row.get("日期") or row.get("trade_date")
            if raw_trade_date is None:
                continue
            trade_date = raw_trade_date if isinstance(raw_trade_date, date) else date.fromisoformat(str(raw_trade_date)[0:10])
            if start <= trade_date <= end:
                filtered.append(row)
        return filtered

    @staticmethod
    def _map_index_daily_row(row: dict[str, Any]) -> dict[str, Any]:
        if "date" not in row and "open" not in row and "close" not in row:
            return row
        return {
            "trade_date": row.get("trade_date") or row.get("date") or row.get("日期"),
            "open": row.get("open") or row.get("开盘"),
            "high": row.get("high") or row.get("最高"),
            "low": row.get("low") or row.get("最低"),
            "close": row.get("close") or row.get("收盘"),
            "amount": row.get("amount") or row.get("成交额"),
            **row,
        }

    @staticmethod
    def _map_daily_bar_row(row: dict[str, Any]) -> dict[str, Any]:
        return {
            "trade_date": row.get("trade_date") or row.get("date") or row.get("日期"),
            "open": row.get("open") or row.get("开盘"),
            "high": row.get("high") or row.get("最高"),
            "low": row.get("low") or row.get("最低"),
            "close": row.get("close") or row.get("收盘"),
            "volume": row.get("volume") or row.get("成交量"),
            "amount": row.get("amount") or row.get("成交额"),
            "turnover_rate": row.get("turnover_rate") or row.get("turnover") or row.get("换手率"),
            **row,
        }

    @staticmethod
    def _map_financial_snapshot_row(row: dict[str, Any]) -> dict[str, Any]:
        return {
            "report_date": row.get("report_date") or row.get("报告期"),
            "roe": row.get("roe") or row.get("净资产收益率-摊薄") or row.get("净资产收益率"),
            "revenue_yoy": row.get("revenue_yoy") or row.get("营业总收入同比增长率"),
            "net_profit_yoy": row.get("net_profit_yoy") or row.get("净利润同比增长率"),
            "operating_cash_flow": row.get("operating_cash_flow") or row.get("每股经营现金流"),
            "pe": row.get("pe"),
            "pb": row.get("pb"),
            "free_float_market_cap": row.get("free_float_market_cap"),
            **row,
        }

    @staticmethod
    def _map_financial_report_row(row: dict[str, Any]) -> dict[str, Any]:
        return {
            "report_date": row.get("report_date") or row.get("报告日"),
            "operating_cash_flow": row.get("operating_cash_flow") or row.get("每股经营现金流"),
            **row,
        }

    @staticmethod
    def _build_industry_stat_row(
        *,
        board_code: str,
        board_name: str,
        history_rows: list[dict[str, Any]],
    ) -> dict[str, Any] | None:
        if len(history_rows) < 2:
            return None

        mapped_rows = []
        for row in history_rows:
            trade_date = row.get("trade_date") or row.get("date") or row.get("日期")
            close = row.get("close") or row.get("收盘价") or row.get("收盘")
            if trade_date is None or close in (None, "", "-", "--"):
                continue
            mapped_rows.append({"trade_date": trade_date, "close": close})

        if len(mapped_rows) < 2:
            return None

        latest = mapped_rows[-1]
        base_index = -21 if len(mapped_rows) >= 21 else 0
        base = mapped_rows[base_index]
        latest_close = float(latest["close"])
        base_close = float(base["close"])
        if base_close == 0:
            return None

        pct_change_20d = ((latest_close - base_close) / base_close) * 100
        return {
            "industry_code": board_code,
            "industry_name": board_name,
            "trade_date": latest["trade_date"],
            "pct_change_20d": pct_change_20d,
            "member_count": None,
        }

    def _build_stock_metadata_map(self, provider: Any) -> dict[str, dict[str, Any]]:
        """
        用交易所股票名录补齐实时快照里经常缺失的行业和上市日期。
        """
        metadata: dict[str, dict[str, Any]] = {}
        datasets: list[list[dict[str, Any]]] = []

        try:
            datasets.append(self._to_records(provider.stock_info_sz_name_code(symbol="A股列表")))
        except Exception:  # noqa: BLE001
            pass

        for symbol in ("主板A股", "科创板"):
            try:
                datasets.append(self._to_records(provider.stock_info_sh_name_code(symbol=symbol)))
            except Exception:  # noqa: BLE001
                pass

        try:
            datasets.append(self._to_records(provider.stock_info_bj_name_code()))
        except Exception:  # noqa: BLE001
            pass

        for rows in datasets:
            for row in rows:
                stock_code = self._extract_stock_code(row)
                if not stock_code:
                    continue

                industry_name = (
                    row.get("所属行业")
                    or row.get("所处行业")
                    or row.get("industry_name")
                    or row.get("industry")
                )
                list_date = row.get("上市日期") or row.get("A股上市日期") or row.get("list_date")

                metadata[stock_code] = {
                    "industry_name": str(industry_name).strip() if industry_name not in (None, "") else None,
                    "list_date": list_date,
                }

        self._merge_industry_board_members(provider, metadata)
        return metadata

    def _merge_industry_board_members(self, provider: Any, metadata: dict[str, dict[str, Any]]) -> None:
        """
        用东财行业板块成分股补齐仍为空的行业字段，优先解决沪市股票行业缺失问题。
        """
        try:
            boards = self._to_records(provider.stock_board_industry_name_em())
        except Exception:  # noqa: BLE001
            return

        for board in boards:
            board_name = str(board.get("板块名称") or board.get("name") or "").strip()
            board_code = str(board.get("板块代码") or board.get("code") or "").strip()
            if not board_name:
                continue

            symbol = board_code or board_name
            try:
                members = self._to_records(provider.stock_board_industry_cons_em(symbol=symbol))
            except Exception:  # noqa: BLE001
                continue

            for member in members:
                stock_code = self._extract_stock_code(member)
                if not stock_code:
                    continue

                current = metadata.get(stock_code)
                if current is None:
                    metadata[stock_code] = {"industry_name": board_name, "list_date": None}
                    continue

                if not current.get("industry_name"):
                    current["industry_name"] = board_name

    @staticmethod
    def _extract_stock_code(row: dict[str, Any]) -> str:
        raw_code = row.get("代码") or row.get("A股代码") or row.get("证券代码") or row.get("stock_code") or row.get("symbol")
        if raw_code in (None, ""):
            return ""

        text = str(raw_code).strip().lower()
        if text.startswith(("sh", "sz", "bj")):
            text = text[2:]
        return text.zfill(6) if text.isdigit() and len(text) < 6 else text

    @staticmethod
    def _to_market_symbol(stock_code: str) -> str:
        if stock_code.startswith(("600", "601", "603", "605", "688")):
            return f"sh{stock_code}"
        if stock_code.startswith(("000", "001", "002", "003", "300", "301")):
            return f"sz{stock_code}"
        if stock_code.lower().startswith("bj"):
            return stock_code.lower()
        if stock_code.startswith(("430", "830", "831", "832", "833", "835", "836", "837", "838", "920")):
            return f"bj{stock_code}"
        return stock_code

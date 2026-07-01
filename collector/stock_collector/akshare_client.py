from __future__ import annotations

from dataclasses import dataclass
from datetime import date
from datetime import timedelta
from typing import Any
import time

import requests

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
        rows = None
        if self.provider is None:
            try:
                rows = self._fetch_tencent_stock_spot_snapshot()
            except Exception:  # noqa: BLE001
                try:
                    rows = self._fetch_eastmoney_stock_spot_snapshot()
                except Exception:  # noqa: BLE001
                    rows = None

        if rows is None and hasattr(provider, "stock_zh_a_spot_em"):
            try:
                rows = self._to_records(provider.stock_zh_a_spot_em())
            except Exception:  # noqa: BLE001
                rows = self._to_records(provider.stock_zh_a_spot())
        elif rows is None:
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
            current_industry_name = str(enriched.get("所处行业") or enriched.get("industry") or "").strip()
            if (not current_industry_name) or self._is_generic_industry_name(current_industry_name):
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
                if filtered_rows:
                    return [self._map_daily_bar_row(row) for row in filtered_rows]
                if rows:
                    return []
            except Exception:  # noqa: BLE001
                # 历史日线主接口失败时再退回旧接口，避免在高频补拉场景里无限重试多套源。
                pass

        if hasattr(provider, "stock_zh_a_hist_tx"):
            try:
                frame = provider.stock_zh_a_hist_tx(
                    symbol=self._to_market_symbol(stock_code),
                    start_date=start_date,
                    end_date=end_date,
                    adjust=adjust,
                )
                rows = self._to_records(frame)
                filtered_rows = self._filter_rows_by_date(rows, start_date=start_date, end_date=end_date)
                if filtered_rows:
                    return [self._map_tencent_daily_bar_row(row) for row in filtered_rows]
            except Exception:  # noqa: BLE001
                pass

        if not hasattr(provider, "stock_zh_a_daily"):
            return []

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

    def fetch_financial_report_snapshots(self, report_date: str) -> list[dict[str, Any]]:
        provider = self._get_provider()
        if not hasattr(provider, "stock_yjbb_em"):
            return []

        base_rows = self._to_records(provider.stock_yjbb_em(date=report_date))
        merged: dict[str, dict[str, Any]] = {}
        for row in base_rows:
            stock_code = self._extract_stock_code(row)
            if not stock_code:
                continue
            mapped = self._map_eastmoney_financial_report_row(row, report_date)
            mapped["stock_code"] = stock_code
            merged[stock_code] = mapped

        supplemental_interfaces = (
            ("stock_lrb_em", self._map_eastmoney_profit_report_row),
            ("stock_zcfz_em", self._map_eastmoney_balance_report_row),
            ("stock_xjll_em", self._map_eastmoney_cash_flow_report_row),
        )
        for interface_name, mapper in supplemental_interfaces:
            if not hasattr(provider, interface_name):
                continue
            try:
                supplemental_rows = self._to_records(getattr(provider, interface_name)(date=report_date))
            except Exception:  # noqa: BLE001
                continue
            for row in supplemental_rows:
                stock_code = self._extract_stock_code(row)
                if not stock_code:
                    continue
                target = merged.setdefault(
                    stock_code,
                    {
                        "stock_code": stock_code,
                        "report_date": report_date,
                        "data_source_priority": "eastmoney_report",
                    },
                )
                target.update({key: value for key, value in mapper(row, report_date).items() if value not in (None, "")})
                target["stock_code"] = stock_code

        return list(merged.values())

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

    def fetch_stock_fund_flow(self, stock_code: str, market: str | None = None) -> list[dict[str, Any]]:
        provider = self._get_provider()
        if not hasattr(provider, "stock_individual_fund_flow"):
            raise AkshareClientError("AKShare stock_individual_fund_flow is not available.")

        resolved_market = market or self._infer_fund_flow_market(stock_code)
        rows = self._to_records(provider.stock_individual_fund_flow(stock=stock_code, market=resolved_market))
        return rows

    def fetch_stock_fund_flow_rank(self, *, indicator: str = "今日") -> list[dict[str, Any]]:
        provider = self._get_provider()
        if hasattr(provider, "stock_individual_fund_flow_rank"):
            return self._to_records(provider.stock_individual_fund_flow_rank(indicator=indicator))
        if indicator == "今日" and hasattr(provider, "stock_main_fund_flow"):
            return self._to_records(provider.stock_main_fund_flow(symbol="全部股票"))

        raise AkshareClientError("No supported AKShare stock fund flow rank interface is available.")

    def fetch_stock_fund_flow_rank_resilient(self, *, indicator: str = "今日") -> list[dict[str, Any]]:
        provider = self._get_provider()

        try:
            return self.fetch_stock_fund_flow_rank(indicator=indicator)
        except Exception:  # noqa: BLE001
            pass

        if hasattr(provider, "stock_fund_flow_individual"):
            symbol_map = {
                "今日": "即时",
                "3日": "3日排行",
                "5日": "5日排行",
                "10日": "10日排行",
            }
            mapped_symbol = symbol_map.get(indicator)
            if mapped_symbol is not None:
                return self._to_records(provider.stock_fund_flow_individual(symbol=mapped_symbol))

        raise AkshareClientError("No supported resilient stock fund flow rank interface is available.")

    def fetch_industry_fund_flow(
        self,
        *,
        indicator: str = "\u4eca\u65e5",
        sector_type: str = "\u884c\u4e1a\u8d44\u91d1\u6d41",
    ) -> list[dict[str, Any]]:
        provider = self._get_provider()
        if hasattr(provider, "stock_sector_fund_flow_rank"):
            try:
                return self._to_records(provider.stock_sector_fund_flow_rank(indicator=indicator, sector_type=sector_type))
            except requests.RequestException:
                pass
            except ConnectionError:
                pass

        if hasattr(provider, "stock_fund_flow_industry"):
            return self._to_records(provider.stock_fund_flow_industry(symbol="即时"))

        raise AkshareClientError("No supported AKShare industry fund flow interface is available.")

    def fetch_lhb_stock_summary(self, trade_date: str) -> list[dict[str, Any]]:
        provider = self._get_provider()
        if hasattr(provider, "stock_lhb_detail_em"):
            return self._to_records(provider.stock_lhb_detail_em(start_date=trade_date, end_date=trade_date))
        if hasattr(provider, "stock_lhb_detail_daily_sina"):
            return self._to_records(provider.stock_lhb_detail_daily_sina(date=trade_date))

        raise AkshareClientError("No supported AKShare daily LHB summary interface is available.")

    def fetch_lhb_institution_summary(self, trade_date: str) -> list[dict[str, Any]]:
        provider = self._get_provider()
        if hasattr(provider, "stock_lhb_jgmmtj_em"):
            return self._to_records(provider.stock_lhb_jgmmtj_em(start_date=trade_date, end_date=trade_date))

        return []

    def _fetch_tencent_stock_spot_snapshot(self) -> list[dict[str, Any]]:
        url = "https://proxy.finance.qq.com/cgi/cgi-bin/rank/hs/getBoardRankList"
        rows: list[dict[str, Any]] = []
        offset = 0
        page_size = 200
        while True:
            payload = self._request_json_with_retry(
                url,
                {
                    "_appver": "11.17.0",
                    "board_code": "aStock",
                    "sort_type": "price",
                    "direct": "down",
                    "offset": str(offset),
                    "count": str(page_size),
                },
            )
            data = payload.get("data")
            if not isinstance(data, dict):
                raise AkshareClientError("Tencent spot snapshot missing data object.")
            rank_list = data.get("rank_list")
            if not isinstance(rank_list, list):
                raise AkshareClientError("Tencent spot snapshot missing rank list.")
            if not rank_list:
                break
            rows.extend(self._map_tencent_stock_spot_row(dict(item)) for item in rank_list)
            if len(rank_list) < page_size:
                break
            offset += page_size
        return rows

    def _fetch_eastmoney_stock_spot_snapshot(self) -> list[dict[str, Any]]:
        url = "https://82.push2.eastmoney.com/api/qt/clist/get"
        base_params = {
            "pn": "1",
            "pz": "100",
            "po": "1",
            "np": "1",
            "ut": "bd1d9ddb04089700cf9c27f6f7426281",
            "fltt": "2",
            "invt": "2",
            "fid": "f12",
            "fs": "m:0 t:6,m:0 t:80,m:1 t:2,m:1 t:23,m:0 t:81 s:2048",
            "fields": "f1,f2,f3,f4,f5,f6,f7,f8,f9,f10,f12,f13,f14,f15,f16,f17,f18,"
            "f20,f21,f23,f24,f25,f22,f11,f62,f128,f136,f115,f152",
        }
        first_page = self._request_json_with_retry(url, base_params)
        diff = self._extract_eastmoney_diff(first_page)
        total = int(first_page["data"].get("total") or len(diff))
        page_size = max(len(diff), 1)
        total_pages = (total + page_size - 1) // page_size
        rows = [self._map_eastmoney_stock_spot_row(row) for row in diff]
        for page in range(2, total_pages + 1):
            params = dict(base_params)
            params["pn"] = str(page)
            page_payload = self._request_json_with_retry(url, params)
            page_rows = self._extract_eastmoney_diff(page_payload)
            rows.extend(self._map_eastmoney_stock_spot_row(row) for row in page_rows)
        return rows

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
    def _request_json_with_retry(url: str, params: dict[str, str], *, timeout: int = 15, max_retries: int = 3) -> dict[str, Any]:
        last_exception: Exception | None = None
        for attempt in range(max_retries):
            try:
                response = requests.get(url, params=params, timeout=timeout)
                response.raise_for_status()
                payload = response.json()
                if not isinstance(payload, dict):
                    raise AkshareClientError("EastMoney spot snapshot returned non-dict payload.")
                return payload
            except (requests.RequestException, ValueError, AkshareClientError) as exc:
                last_exception = exc
                if attempt < max_retries - 1:
                    time.sleep(1 + attempt)
        raise AkshareClientError("EastMoney spot snapshot request failed.") from last_exception

    @staticmethod
    def _extract_eastmoney_diff(payload: dict[str, Any]) -> list[dict[str, Any]]:
        data = payload.get("data")
        if not isinstance(data, dict):
            raise AkshareClientError("EastMoney spot snapshot missing data object.")
        diff = data.get("diff")
        if diff in (None, []):
            return []
        if not isinstance(diff, list):
            raise AkshareClientError("EastMoney spot snapshot returned non-list diff payload.")
        return [dict(row) for row in diff]

    @staticmethod
    def _map_eastmoney_stock_spot_row(row: dict[str, Any]) -> dict[str, Any]:
        return {
            "代码": row.get("f12"),
            "名称": row.get("f14"),
            "最新价": row.get("f2"),
            "涨跌幅": row.get("f3"),
            "涨跌额": row.get("f4"),
            "成交量": row.get("f5"),
            "成交额": row.get("f6"),
            "振幅": row.get("f7"),
            "换手率": row.get("f8"),
            "市盈率-动态": row.get("f9"),
            "量比": row.get("f10"),
            "最高": row.get("f15"),
            "最低": row.get("f16"),
            "今开": row.get("f17"),
            "昨收": row.get("f18"),
            "总市值": row.get("f20"),
            "流通市值": row.get("f21"),
            "涨速": row.get("f22"),
            "市净率": row.get("f23"),
            "60日涨跌幅": row.get("f24"),
            "年初至今涨跌幅": row.get("f25"),
            **row,
        }

    @staticmethod
    def _map_tencent_stock_spot_row(row: dict[str, Any]) -> dict[str, Any]:
        return {
            "代码": row.get("code"),
            "名称": row.get("name"),
            "最新价": row.get("zxj"),
            "涨跌幅": row.get("zdf"),
            "涨跌额": row.get("zd"),
            "成交量": row.get("turnover"),
            "成交额": row.get("volume"),
            "振幅": row.get("zf"),
            "换手率": row.get("hsl"),
            "市盈率TTM": row.get("pe_ttm"),
            "市净率": row.get("pn"),
            "量比": row.get("lb"),
            "总市值": row.get("zsz"),
            "流通市值": row.get("ltsz"),
            "涨速": row.get("speed"),
            "60日涨跌幅": row.get("zdf_d60"),
            "年初至今涨跌幅": row.get("zdf_y"),
            **row,
        }

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
    def _map_tencent_daily_bar_row(row: dict[str, Any]) -> dict[str, Any]:
        return {
            **row,
            "trade_date": row.get("trade_date") or row.get("date"),
            "open": row.get("open"),
            "high": row.get("high"),
            "low": row.get("low"),
            "close": row.get("close"),
            "volume": row.get("volume") or row.get("amount"),
            "amount": row.get("turnover_amount"),
            "turnover_rate": row.get("turnover_rate") or row.get("turnover"),
        }

    @staticmethod
    def _map_financial_snapshot_row(row: dict[str, Any]) -> dict[str, Any]:
        return {
            "report_date": row.get("report_date") or row.get("报告期"),
            "roe": row.get("roe") or row.get("净资产收益率-摊薄") or row.get("净资产收益率"),
            "revenue_yoy": row.get("revenue_yoy") or row.get("营业总收入同比增长率"),
            "net_profit_yoy": row.get("net_profit_yoy") or row.get("净利润同比增长率"),
            "operating_cash_flow": row.get("operating_cash_flow") or row.get("每股经营现金流"),
            "pe": row.get("pe") or row.get("市盈率") or row.get("市盈率TTM") or row.get("市盈率(TTM)"),
            "pb": row.get("pb") or row.get("市净率"),
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
    def _map_eastmoney_financial_report_row(row: dict[str, Any], report_date: str) -> dict[str, Any]:
        return {
            "stock_code": row.get("stock_code") or row.get("股票代码") or row.get("代码"),
            "report_date": report_date,
            "roe": row.get("roe") or row.get("净资产收益率"),
            "revenue_yoy": row.get("revenue_yoy") or row.get("营业总收入-同比增长"),
            "net_profit_yoy": row.get("net_profit_yoy") or row.get("净利润-同比增长"),
            "operating_cash_flow": row.get("operating_cash_flow") or row.get("每股经营现金流量"),
            "gross_margin": row.get("gross_margin") or row.get("销售毛利率"),
            "announcement_date": row.get("announcement_date") or row.get("最新公告日期"),
            "data_source_priority": "eastmoney_report",
            **row,
        }

    @staticmethod
    def _map_eastmoney_profit_report_row(row: dict[str, Any], report_date: str) -> dict[str, Any]:
        return {
            "stock_code": row.get("stock_code") or row.get("股票代码") or row.get("代码"),
            "report_date": report_date,
            "revenue_yoy": row.get("revenue_yoy") or row.get("营业总收入同比"),
            "net_profit_yoy": row.get("net_profit_yoy") or row.get("净利润同比"),
            "announcement_date": row.get("announcement_date") or row.get("公告日期"),
        }

    @staticmethod
    def _map_eastmoney_balance_report_row(row: dict[str, Any], report_date: str) -> dict[str, Any]:
        return {
            "stock_code": row.get("stock_code") or row.get("股票代码") or row.get("代码"),
            "report_date": report_date,
            "debt_to_asset_ratio": row.get("debt_to_asset_ratio") or row.get("资产负债率"),
            "announcement_date": row.get("announcement_date") or row.get("公告日期"),
        }

    @staticmethod
    def _map_eastmoney_cash_flow_report_row(row: dict[str, Any], report_date: str) -> dict[str, Any]:
        return {
            "stock_code": row.get("stock_code") or row.get("股票代码") or row.get("代码"),
            "report_date": report_date,
            "operating_cash_flow_net": row.get("operating_cash_flow_net") or row.get("经营性现金流-现金流量净额"),
            "announcement_date": row.get("announcement_date") or row.get("公告日期"),
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

                current_industry_name = str(current.get("industry_name") or "").strip()
                if (not current_industry_name) or self._is_generic_industry_name(current_industry_name):
                    current["industry_name"] = board_name

    @staticmethod
    def _is_generic_industry_name(industry_name: str) -> bool:
        generic_prefixes = tuple(f"{prefix} " for prefix in "ABCDEFGHIJKLMNOPQRS")
        if industry_name.startswith(generic_prefixes):
            return True

        return industry_name in {
            "制造业",
            "金融业",
            "房地产业",
            "建筑业",
            "农林牧渔业",
            "采矿业",
        }

    @staticmethod
    def _extract_stock_code(row: dict[str, Any]) -> str:
        raw_code = (
            row.get("代码")
            or row.get("股票代码")
            or row.get("A股代码")
            or row.get("证券代码")
            or row.get("stock_code")
            or row.get("symbol")
        )
        if raw_code in (None, ""):
            return ""

        text = str(raw_code).strip().lower()
        if text.startswith(("sh", "sz", "bj")):
            text = text[2:]
        return text.zfill(6) if text.isdigit() and len(text) < 6 else text

    @staticmethod
    def _to_market_symbol(stock_code: str) -> str:
        if stock_code.startswith(("600", "601", "603", "605", "688", "689")):
            return f"sh{stock_code}"
        if stock_code.startswith(("000", "001", "002", "003", "300", "301", "302")):
            return f"sz{stock_code}"
        if stock_code.lower().startswith("bj"):
            return stock_code.lower()
        if stock_code.startswith(("430", "830", "831", "832", "833", "835", "836", "837", "838", "920")):
            return f"bj{stock_code}"
        return stock_code

    @staticmethod
    def _infer_fund_flow_market(stock_code: str) -> str:
        if stock_code.startswith(("600", "601", "603", "605", "688")):
            return "sh"
        if stock_code.startswith(("000", "001", "002", "003", "300", "301")):
            return "sz"
        if stock_code.startswith(("430", "830", "831", "832", "833", "835", "836", "837", "838", "920")):
            return "bj"
        return "sh"

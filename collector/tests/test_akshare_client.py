from __future__ import annotations

import builtins
from unittest.mock import Mock

import pytest

import stock_collector.akshare_client as akshare_client_module
from stock_collector.akshare_client import AkshareClient
from stock_collector.akshare_client import AkshareClientError


class DailyMalformedProvider:
    def stock_zh_a_daily(self, symbol: str, start_date: str, end_date: str, adjust: str):
        raise KeyError("date")


class HistPreferredProvider:
    def __init__(self) -> None:
        self.called: list[str] = []

    def stock_zh_a_hist(self, symbol: str, period: str, start_date: str, end_date: str, adjust: str):
        self.called.append("hist")
        assert symbol == "600000"
        assert period == "daily"
        return [{"日期": "2026-06-23", "开盘": "10", "最高": "10.5", "最低": "9.8", "收盘": "10.2"}]

    def stock_zh_a_daily(self, symbol: str, start_date: str, end_date: str, adjust: str):
        self.called.append("daily")
        return [{"日期": "2026-06-22"}]


class HistFallbackProvider:
    def __init__(self) -> None:
        self.called: list[str] = []

    def stock_zh_a_hist(self, symbol: str, period: str, start_date: str, end_date: str, adjust: str):
        self.called.append("hist")
        raise RuntimeError("hist unavailable")

    def stock_zh_a_daily(self, symbol: str, start_date: str, end_date: str, adjust: str):
        self.called.append("daily")
        return [{"日期": "2026-06-22", "收盘": "10.2"}]


class DataFrameLikePayload:
    def to_dict(self, *, orient: str) -> list[dict[str, str]]:
        assert orient == "records"
        return [{"symbol": "600000"}]


class SpotProvider:
    def stock_zh_a_spot(self):
        return [{"代码": "600000"}]


class SpotEmFallbackProvider:
    def stock_zh_a_spot_em(self):
        raise RuntimeError("em unavailable")

    def stock_zh_a_spot(self):
        return [{"代码": "600000"}]


class SpotProviderWithMetadata:
    def stock_zh_a_spot_em(self):
        return [{"代码": "600000", "名称": "浦发银行"}]

    def stock_info_sz_name_code(self, symbol: str):
        assert symbol == "A股列表"
        return []

    def stock_info_sh_name_code(self, symbol: str):
        if symbol == "主板A股":
            return [{"证券代码": "600000", "所属行业": "银行", "上市日期": "1999-11-10"}]
        return []

    def stock_info_bj_name_code(self):
        return []


class SpotProviderWithIndustryBoardMembers:
    def stock_zh_a_spot_em(self):
        return [{"代码": "605020", "名称": "永和股份"}]

    def stock_info_sz_name_code(self, symbol: str):
        return []

    def stock_info_sh_name_code(self, symbol: str):
        return []

    def stock_info_bj_name_code(self):
        return []

    def stock_board_industry_name_em(self):
        return [{"板块名称": "化学制品", "板块代码": "BK1234"}]

    def stock_board_industry_cons_em(self, symbol: str):
        assert symbol == "BK1234"
        return [{"代码": "605020", "名称": "永和股份"}]


class SpotProviderWithGenericIndustryAndBoardMembers:
    def stock_zh_a_spot_em(self):
        return [{"代码": "002317", "名称": "众生药业"}]

    def stock_info_sz_name_code(self, symbol: str):
        return [{"A股代码": "002317", "A股简称": "众生药业", "所属行业": "C 制造业"}]

    def stock_info_sh_name_code(self, symbol: str):
        return []

    def stock_info_bj_name_code(self):
        return []

    def stock_board_industry_name_em(self):
        return [{"板块名称": "化学制品", "板块代码": "BK1234"}]

    def stock_board_industry_cons_em(self, symbol: str):
        assert symbol == "BK1234"
        return [{"代码": "002317", "名称": "众生药业"}]


class SpotProviderWithSpotGenericIndustryAndBoardMembers:
    def stock_zh_a_spot_em(self):
        return [{"代码": "sz002317", "名称": "众生药业", "所处行业": "C 制造业", "上市时间": "2009-12-11"}]

    def stock_info_sz_name_code(self, symbol: str):
        return [{"A股代码": "002317", "A股简称": "众生药业", "所属行业": "C 制造业"}]

    def stock_info_sh_name_code(self, symbol: str):
        return []

    def stock_info_bj_name_code(self):
        return []

    def stock_board_industry_name_em(self):
        return [{"板块名称": "化学制品", "板块代码": "BK1234"}]

    def stock_board_industry_cons_em(self, symbol: str):
        assert symbol == "BK1234"
        return [{"代码": "002317", "名称": "众生药业"}]


class IndexFallbackProvider:
    def stock_zh_index_daily(self, symbol: str):
        assert symbol == "sh000300"
        return [{"日期": "2026-06-16", "收盘": "100"}]


class IndustryFallbackProvider:
    def stock_board_industry_name_ths(self):
        return [{"name": "半导体", "code": "881121"}]

    def stock_board_industry_index_ths(self, symbol: str, start_date: str, end_date: str):
        assert symbol == "半导体"
        return [{"日期": "2026-06-16", "收盘价": 100}, {"日期": "2026-06-17", "收盘价": 110}]


class FinancialFallbackProvider:
    def stock_financial_abstract_ths(self, symbol: str, indicator: str):
        raise AttributeError("ths unavailable")

    def stock_financial_report_sina(self, stock: str, symbol: str):
        assert stock == "bj920000"
        assert symbol == "资产负债表"
        return [{"报告日": "20260331"}]


class FinancialThsProvider:
    def stock_financial_abstract_ths(self, symbol: str, indicator: str):
        assert symbol == "600000"
        assert indicator == "按报告期"
        return [{
            "报告期": "2025-12-31",
            "市盈率TTM": "15.2",
            "市净率": "1.43",
            "净资产收益率": "12.5",
        }]


def test_health_reports_ready_when_provider_is_injected() -> None:
    client = AkshareClient(provider=object())

    result = client.health()

    assert result == {"source": "akshare", "status": "ready"}


def test_health_reports_not_configured_when_akshare_import_fails(monkeypatch: pytest.MonkeyPatch) -> None:
    original_import = builtins.__import__

    def fake_import(name, globals=None, locals=None, fromlist=(), level=0):  # type: ignore[no-untyped-def]
        if name == "akshare":
            raise ImportError("akshare missing")
        return original_import(name, globals, locals, fromlist, level)

    monkeypatch.setattr(builtins, "__import__", fake_import)

    result = AkshareClient().health()

    assert result == {"source": "akshare", "status": "not_configured"}


def test_fetch_index_bars_uses_fallback_provider() -> None:
    client = AkshareClient(provider=IndexFallbackProvider())

    rows = client.fetch_index_bars("sh000300", "20260616", "20260616")

    assert rows == [{"日期": "2026-06-16", "收盘": "100"}]


def test_fetch_industry_daily_stats_uses_ths_provider() -> None:
    client = AkshareClient(provider=IndustryFallbackProvider())

    rows = client.fetch_industry_daily_stats()

    assert rows[0]["industry_code"] == "881121"
    assert rows[0]["industry_name"] == "半导体"
    assert rows[0]["rank_20d"] == 1


def test_to_records_supports_dataframe_like_payload() -> None:
    rows = AkshareClient._to_records(DataFrameLikePayload())  # noqa: SLF001

    assert rows == [{"symbol": "600000"}]


def test_fetch_stock_spot_snapshot_falls_back_to_non_em_interface() -> None:
    client = AkshareClient(provider=SpotProvider())

    rows = client.fetch_stock_spot_snapshot()

    assert rows == [{"代码": "600000"}]


def test_fetch_stock_spot_snapshot_falls_back_when_em_request_fails() -> None:
    client = AkshareClient(provider=SpotEmFallbackProvider())

    rows = client.fetch_stock_spot_snapshot()

    assert rows == [{"代码": "600000"}]


def test_fetch_stock_spot_snapshot_prefers_direct_eastmoney_pages(monkeypatch: pytest.MonkeyPatch) -> None:
    responses = [
        {
            "data": {
                "total": 2,
                "diff": [{"f12": "600000", "f14": "浦发银行", "f9": 6.82, "f23": 0.58}],
            }
        },
        {
            "data": {
                "total": 2,
                "diff": [{"f12": "000001", "f14": "平安银行", "f9": 5.11, "f23": 0.61}],
            }
        },
    ]

    def fake_get(url: str, params: dict[str, str], timeout: int):  # type: ignore[no-untyped-def]
        payload = responses.pop(0)
        response = Mock()
        response.raise_for_status.return_value = None
        response.json.return_value = payload
        return response

    monkeypatch.setattr(akshare_client_module.requests, "get", fake_get)
    client = AkshareClient(provider=None)

    rows = client._fetch_eastmoney_stock_spot_snapshot()  # noqa: SLF001

    assert rows[0]["代码"] == "600000"
    assert rows[0]["市盈率-动态"] == 6.82
    assert rows[1]["代码"] == "000001"
    assert rows[1]["市净率"] == 0.61


def test_fetch_stock_spot_snapshot_prefers_direct_tencent_pages(monkeypatch: pytest.MonkeyPatch) -> None:
    responses = [
        {
            "data": {
                "rank_list": [
                    {"code": "sh600000", "name": "娴﹀彂閾惰", "pe_ttm": "5.78", "pn": "0.39", "hsl": "0.10", "zxj": "8.73"},
                ]
            }
        },
        {"data": {"rank_list": []}},
    ]

    def fake_get(url: str, params: dict[str, str], timeout: int):  # type: ignore[no-untyped-def]
        payload = responses.pop(0)
        response = Mock()
        response.raise_for_status.return_value = None
        response.json.return_value = payload
        return response

    monkeypatch.setattr(akshare_client_module.requests, "get", fake_get)
    client = AkshareClient(provider=None)

    rows = client._fetch_tencent_stock_spot_snapshot()  # noqa: SLF001

    assert rows[0]["code"] == "sh600000"
    assert rows[0]["pe_ttm"] == "5.78"
    assert rows[0]["pn"] == "0.39"
    assert rows[0]["hsl"] == "0.10"


def test_fetch_stock_spot_snapshot_enriches_industry_from_exchange_lists() -> None:
    client = AkshareClient(provider=SpotProviderWithMetadata())

    rows = client.fetch_stock_spot_snapshot()

    assert rows == [{"代码": "600000", "名称": "浦发银行", "所处行业": "银行", "上市时间": "1999-11-10"}]


def test_fetch_stock_spot_snapshot_enriches_industry_from_board_members() -> None:
    client = AkshareClient(provider=SpotProviderWithIndustryBoardMembers())

    rows = client.fetch_stock_spot_snapshot()

    assert rows == [{"代码": "605020", "名称": "永和股份", "所处行业": "化学制品", "上市时间": None}]


def test_fetch_stock_spot_snapshot_board_members_override_generic_industry() -> None:
    client = AkshareClient(provider=SpotProviderWithGenericIndustryAndBoardMembers())

    rows = client.fetch_stock_spot_snapshot()

    assert rows == [{"代码": "002317", "名称": "众生药业", "所处行业": "化学制品", "上市时间": None}]


def test_fetch_stock_spot_snapshot_overrides_generic_spot_industry() -> None:
    client = AkshareClient(provider=SpotProviderWithSpotGenericIndustryAndBoardMembers())

    rows = client.fetch_stock_spot_snapshot()

    assert rows == [{"代码": "sz002317", "名称": "众生药业", "所处行业": "化学制品", "上市时间": "2009-12-11"}]


def test_fetch_daily_bars_treats_missing_date_keyerror_as_empty_payload() -> None:
    client = AkshareClient(provider=DailyMalformedProvider())

    rows = client.fetch_daily_bars("302132", "20200101", "20260621")

    assert rows == []


def test_fetch_daily_bars_prefers_hist_interface() -> None:
    provider = HistPreferredProvider()
    client = AkshareClient(provider=provider)

    rows = client.fetch_daily_bars("600000", "20260623", "20260623")

    assert rows[0]["trade_date"] == "2026-06-23"
    assert provider.called == ["hist"]


def test_fetch_daily_bars_falls_back_to_daily_when_hist_fails() -> None:
    provider = HistFallbackProvider()
    client = AkshareClient(provider=provider)

    rows = client.fetch_daily_bars("600000", "20260622", "20260622")

    assert rows[0]["trade_date"] == "2026-06-22"
    assert provider.called == ["hist", "daily"]


def test_fetch_financial_snapshots_falls_back_to_sina() -> None:
    client = AkshareClient(provider=FinancialFallbackProvider())

    rows = client.fetch_financial_snapshots("920000")

    assert rows == [{"report_date": "20260331", "报告日": "20260331", "operating_cash_flow": None}]


def test_fetch_financial_snapshots_maps_pe_pb_from_ths_payload() -> None:
    client = AkshareClient(provider=FinancialThsProvider())

    rows = client.fetch_financial_snapshots("600000")

    assert rows == [{
        "report_date": "2025-12-31",
        "roe": "12.5",
        "revenue_yoy": None,
        "net_profit_yoy": None,
        "operating_cash_flow": None,
        "pe": "15.2",
        "pb": "1.43",
        "free_float_market_cap": None,
        "报告期": "2025-12-31",
        "市盈率TTM": "15.2",
        "市净率": "1.43",
        "净资产收益率": "12.5",
    }]


def test_to_records_rejects_unsupported_payload() -> None:
    with pytest.raises(AkshareClientError):
        AkshareClient._to_records("unsupported")  # noqa: SLF001


class HistMismatchedDateProvider:
    def stock_zh_a_hist(self, symbol: str, period: str, start_date: str, end_date: str, adjust: str):
        return [{"date": "2026-06-20", "close": "10.2"}]


def test_fetch_daily_bars_filters_out_rows_outside_requested_window() -> None:
    client = AkshareClient(provider=HistMismatchedDateProvider())

    rows = client.fetch_daily_bars("600000", "20260623", "20260623")

    assert rows == []

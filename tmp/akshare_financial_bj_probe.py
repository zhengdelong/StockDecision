import json
import traceback

import akshare as ak


SYMBOLS = ["bj920000", "bj920001"]
CALLS = [
    ("stock_financial_abstract_ths", lambda symbol: ak.stock_financial_abstract_ths(symbol=symbol, indicator="按报告期")),
    ("stock_financial_abstract_new_ths", lambda symbol: ak.stock_financial_abstract_new_ths(symbol=symbol, indicator="按报告期")),
    ("stock_financial_report_sina", lambda symbol: ak.stock_financial_report_sina(stock=symbol, symbol="资产负债表")),
]


def main() -> None:
    for stock_code in SYMBOLS:
        for name, fn in CALLS:
            print(f"=== {stock_code} {name} ===")
            try:
                payload = fn(stock_code)
                rows = payload.head(3).to_dict(orient="records") if hasattr(payload, "head") else payload[:3]
                print(json.dumps({"ok": True, "rows": rows}, ensure_ascii=False, default=str))
            except Exception as exc:  # noqa: BLE001
                print(json.dumps({"ok": False, "error_type": type(exc).__name__, "error": str(exc)}, ensure_ascii=False))
                traceback.print_exc()


if __name__ == "__main__":
    main()

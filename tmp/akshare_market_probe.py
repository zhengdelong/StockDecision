import json
import traceback

import akshare as ak


FUNCTIONS = [
    "index_zh_a_hist",
    "stock_zh_index_daily",
    "stock_board_industry_name_em",
    "stock_board_industry_hist_em",
]


def try_call(name: str):
    func = getattr(ak, name)
    if name == "index_zh_a_hist":
        return func(symbol="sh000300", period="daily", start_date="20250101", end_date="20250131")
    if name == "stock_zh_index_daily":
        return func(symbol="sh000300")
    if name == "stock_board_industry_name_em":
        return func()
    if name == "stock_board_industry_hist_em":
        return func(symbol="小金属", start_date="20250101", end_date="20250131", period="日k", adjust="")
    raise ValueError(name)


def main() -> None:
    for name in FUNCTIONS:
        print(f"=== {name} ===")
        try:
            payload = try_call(name)
            if hasattr(payload, "head"):
                rows = payload.head(3).to_dict(orient="records")
            else:
                rows = payload[:3]
            print(json.dumps({"ok": True, "rows": rows}, ensure_ascii=False, default=str))
        except Exception as exc:  # noqa: BLE001
            print(json.dumps({"ok": False, "error_type": type(exc).__name__, "error": str(exc)}, ensure_ascii=False))
            traceback.print_exc()


if __name__ == "__main__":
    main()

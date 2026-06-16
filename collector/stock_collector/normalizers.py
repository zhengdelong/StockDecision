from typing import Any


def normalize_stock_code(raw_code: Any) -> str:
    code = str(raw_code).strip()
    if not code:
        raise ValueError("stock code is required")
    return code

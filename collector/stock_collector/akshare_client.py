from dataclasses import dataclass
from typing import Any


@dataclass(frozen=True)
class AkshareClient:
    """Thin boundary around AKShare so the rest of the collector stays testable."""

    def health(self) -> dict[str, str]:
        return {"source": "akshare", "status": "not_configured"}

    def fetch_daily_bars(self, stock_code: str, start_date: str, end_date: str) -> list[dict[str, Any]]:
        raise NotImplementedError("Daily bar collection will be implemented in Task 3.")

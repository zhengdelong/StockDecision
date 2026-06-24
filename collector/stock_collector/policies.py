from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class InterfaceThrottlePolicy:
    interface_name: str
    max_parallelism: int
    min_delay_seconds: float
    max_delay_seconds: float
    batch_size: int
    cooldown_after_batch_seconds: int
    pause_after_consecutive_failures: int | None = None
    pause_seconds: int | None = None

    @classmethod
    def for_interface(
        cls,
        interface_name: str,
        *,
        mode: str = "incremental",
    ) -> "InterfaceThrottlePolicy":
        if interface_name == "stock_zh_a_spot":
            return cls(
                interface_name=interface_name,
                max_parallelism=1,
                min_delay_seconds=3.0,
                max_delay_seconds=3.0,
                batch_size=1,
                cooldown_after_batch_seconds=0,
            )

        if interface_name in {"stock_zh_a_daily", "stock_zh_a_hist"}:
            if mode == "bootstrap":
                return cls(
                    interface_name=interface_name,
                    max_parallelism=1,
                    min_delay_seconds=0.6,
                    max_delay_seconds=0.8,
                    batch_size=80,
                    cooldown_after_batch_seconds=40,
                )

            return cls(
                interface_name=interface_name,
                max_parallelism=1,
                min_delay_seconds=0.35,
                max_delay_seconds=0.7,
                batch_size=150,
                cooldown_after_batch_seconds=20,
            )

        if interface_name == "stock_financial_abstract_ths_fallback_sina":
            if mode == "bootstrap":
                return cls(
                    interface_name=interface_name,
                    max_parallelism=1,
                    min_delay_seconds=1.5,
                    max_delay_seconds=1.5,
                    batch_size=1,
                    cooldown_after_batch_seconds=0,
                    pause_after_consecutive_failures=5,
                    pause_seconds=600,
                )

            return cls(
                interface_name=interface_name,
                max_parallelism=1,
                min_delay_seconds=0.8,
                max_delay_seconds=1.5,
                batch_size=1,
                cooldown_after_batch_seconds=0,
                pause_after_consecutive_failures=5,
                pause_seconds=600,
            )

        if interface_name == "stock_board_industry_index_ths" and mode == "bootstrap":
            return cls(
                interface_name=interface_name,
                max_parallelism=1,
                min_delay_seconds=1.0,
                max_delay_seconds=1.0,
                batch_size=1,
                cooldown_after_batch_seconds=0,
            )

        return cls(
            interface_name=interface_name,
            max_parallelism=1,
            min_delay_seconds=1.0,
            max_delay_seconds=1.0,
            batch_size=1,
            cooldown_after_batch_seconds=0,
        )


@dataclass(frozen=True)
class RetryPolicy:
    delays_in_seconds: tuple[int, ...]

    @classmethod
    def default(cls) -> "RetryPolicy":
        return cls(delays_in_seconds=(5, 15, 60))

    def delay_for_attempt(self, attempt: int) -> int | None:
        if attempt <= 0 or attempt > len(self.delays_in_seconds):
            return None
        return self.delays_in_seconds[attempt - 1]

    @property
    def max_attempts(self) -> int:
        return len(self.delays_in_seconds) + 1


@dataclass(frozen=True)
class CircuitBreakerDecision:
    should_open: bool
    reasons: list[str]

    @classmethod
    def evaluate(
        cls,
        *,
        consecutive_failures: int,
        window_failure_rate: float,
        anti_bot_detected: bool,
    ) -> "CircuitBreakerDecision":
        reasons: list[str] = []

        if consecutive_failures >= 20:
            reasons.append("consecutive_failures")
        if window_failure_rate > 0.3:
            reasons.append("window_failure_rate")
        if anti_bot_detected:
            reasons.append("anti_bot_detected")

        return cls(should_open=bool(reasons), reasons=reasons)


@dataclass(frozen=True)
class DataCompletenessInput:
    daily_bar_coverage: float
    missing_market_indices: list[str]
    industry_missing_rate: float
    stock_snapshot_refreshed: bool


@dataclass(frozen=True)
class DataCompletenessResult:
    is_signal_eligible: bool
    is_backtest_eligible: bool
    reasons: list[str]


class DataCompletenessPolicy:
    @staticmethod
    def evaluate(data: DataCompletenessInput) -> DataCompletenessResult:
        reasons: list[str] = []

        if data.daily_bar_coverage < 0.95:
            reasons.append("daily_bar_coverage")
        if data.missing_market_indices:
            reasons.append("market_indices")
        if not data.stock_snapshot_refreshed:
            reasons.append("stock_snapshot_not_refreshed")
        if data.industry_missing_rate > 0.10:
            reasons.append("industry_missing_rate")

        is_eligible = not reasons
        return DataCompletenessResult(
            is_signal_eligible=is_eligible,
            is_backtest_eligible=is_eligible,
            reasons=reasons,
        )

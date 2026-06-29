from stock_collector.policies import (
    CircuitBreakerDecision,
    DataCompletenessInput,
    DataCompletenessPolicy,
    InterfaceThrottlePolicy,
    RetryPolicy,
)


def test_market_snapshot_policy_is_low_frequency() -> None:
    policy = InterfaceThrottlePolicy.for_interface("stock_zh_a_spot")

    assert policy.max_parallelism == 1
    assert policy.min_delay_seconds == 3.0
    assert policy.batch_size == 1


def test_bootstrap_daily_bar_policy_is_most_conservative() -> None:
    policy = InterfaceThrottlePolicy.for_interface("stock_zh_a_hist", mode="bootstrap")

    assert policy.max_parallelism == 1
    assert policy.min_delay_seconds == 0.6
    assert policy.max_delay_seconds == 0.8
    assert policy.cooldown_after_batch_seconds == 40
    assert policy.batch_size == 80


def test_incremental_daily_bar_policy_is_small_batch() -> None:
    policy = InterfaceThrottlePolicy.for_interface("stock_zh_a_hist", mode="incremental")

    assert policy.max_parallelism == 1
    assert policy.min_delay_seconds == 0.35
    assert policy.max_delay_seconds == 0.7
    assert policy.cooldown_after_batch_seconds == 20
    assert policy.batch_size == 150


def test_legacy_daily_bar_fallback_uses_same_policy_as_hist() -> None:
    hist_policy = InterfaceThrottlePolicy.for_interface("stock_zh_a_hist", mode="incremental")
    fallback_policy = InterfaceThrottlePolicy.for_interface("stock_zh_a_daily", mode="incremental")

    assert fallback_policy.max_parallelism == hist_policy.max_parallelism
    assert fallback_policy.min_delay_seconds == hist_policy.min_delay_seconds
    assert fallback_policy.max_delay_seconds == hist_policy.max_delay_seconds
    assert fallback_policy.batch_size == hist_policy.batch_size
    assert fallback_policy.cooldown_after_batch_seconds == hist_policy.cooldown_after_batch_seconds


def test_bootstrap_financial_policy_is_serial_and_slow() -> None:
    policy = InterfaceThrottlePolicy.for_interface("stock_financial_abstract_ths_fallback_sina", mode="bootstrap")

    assert policy.max_parallelism == 1
    assert policy.min_delay_seconds == 1.5
    assert policy.pause_after_consecutive_failures == 5
    assert policy.pause_seconds == 600


def test_stock_fund_flow_rank_policy_is_serial_and_conservative() -> None:
    policy = InterfaceThrottlePolicy.for_interface("stock_individual_fund_flow", mode="incremental")

    assert policy.max_parallelism == 1
    assert policy.min_delay_seconds == 2.0
    assert policy.max_delay_seconds == 2.0


def test_retry_policy_uses_expected_backoff_schedule() -> None:
    policy = RetryPolicy.default()

    assert policy.delay_for_attempt(1) == 5
    assert policy.delay_for_attempt(2) == 15
    assert policy.delay_for_attempt(3) == 60
    assert policy.max_attempts == 4


def test_retry_policy_rejects_attempts_outside_range() -> None:
    policy = RetryPolicy.default()

    assert policy.delay_for_attempt(0) is None
    assert policy.delay_for_attempt(4) is None


def test_circuit_breaker_opens_on_consecutive_failures() -> None:
    decision = CircuitBreakerDecision.evaluate(
        consecutive_failures=20,
        window_failure_rate=0.1,
        anti_bot_detected=False,
    )

    assert decision.should_open is True
    assert "consecutive_failures" in decision.reasons


def test_circuit_breaker_opens_on_failure_rate() -> None:
    decision = CircuitBreakerDecision.evaluate(
        consecutive_failures=3,
        window_failure_rate=0.31,
        anti_bot_detected=False,
    )

    assert decision.should_open is True
    assert "window_failure_rate" in decision.reasons


def test_circuit_breaker_opens_on_antibot_signal() -> None:
    decision = CircuitBreakerDecision.evaluate(
        consecutive_failures=1,
        window_failure_rate=0.05,
        anti_bot_detected=True,
    )

    assert decision.should_open is True
    assert "anti_bot_detected" in decision.reasons


def test_data_completeness_blocks_signal_generation_when_core_inputs_missing() -> None:
    result = DataCompletenessPolicy.evaluate(
        DataCompletenessInput(
            daily_bar_coverage=0.94,
            missing_market_indices=["399006"],
            industry_missing_rate=0.12,
            stock_snapshot_refreshed=False,
        )
    )

    assert result.is_signal_eligible is False
    assert result.is_backtest_eligible is False
    assert "daily_bar_coverage" in result.reasons
    assert "market_indices" in result.reasons
    assert "industry_missing_rate" in result.reasons
    assert "stock_snapshot_not_refreshed" in result.reasons


def test_data_completeness_allows_signal_generation_when_thresholds_pass() -> None:
    result = DataCompletenessPolicy.evaluate(
        DataCompletenessInput(
            daily_bar_coverage=0.97,
            missing_market_indices=[],
            industry_missing_rate=0.08,
            stock_snapshot_refreshed=True,
        )
    )

    assert result.is_signal_eligible is True
    assert result.is_backtest_eligible is True
    assert result.reasons == []

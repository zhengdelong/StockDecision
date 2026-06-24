namespace StockDecision.Infrastructure.Persistence;

/// <summary>
/// 模拟持仓实体。
/// </summary>
public sealed class SimulatedPositionEntity
{
    /// <summary>主键。</summary>
    public int Id { get; set; }

    /// <summary>股票代码。</summary>
    public string StockCode { get; set; } = string.Empty;

    /// <summary>股票名称。</summary>
    public string StockName { get; set; } = string.Empty;

    /// <summary>行业名称。</summary>
    public string? IndustryName { get; set; }

    /// <summary>策略类型。</summary>
    public string StrategyType { get; set; } = string.Empty;

    /// <summary>快照版本。</summary>
    public string SnapshotVersion { get; set; } = string.Empty;

    /// <summary>开仓交易日。</summary>
    public DateOnly TradeDate { get; set; }

    /// <summary>开仓价格。</summary>
    public decimal EntryPrice { get; set; }

    /// <summary>建议止损价。</summary>
    public decimal StopLossPrice { get; set; }

    /// <summary>建议止盈价。</summary>
    public decimal TargetPrice { get; set; }

    /// <summary>持仓股数。</summary>
    public int Quantity { get; set; }

    /// <summary>投入资金。</summary>
    public decimal InvestedCapital { get; set; }

    /// <summary>状态。</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>建仓时间。</summary>
    public DateTime OpenedAtUtc { get; set; }

    /// <summary>平仓时间。</summary>
    public DateTime? ClosedAtUtc { get; set; }

    /// <summary>平仓交易日。</summary>
    public DateOnly? ClosedTradeDate { get; set; }

    /// <summary>平仓价格。</summary>
    public decimal? ExitPrice { get; set; }

    /// <summary>已实现盈亏金额。</summary>
    public decimal? RealizedProfitAmount { get; set; }

    /// <summary>已实现盈亏比例。</summary>
    public decimal? RealizedProfitPct { get; set; }

    /// <summary>备注。</summary>
    public string? Notes { get; set; }
}

/// <summary>
/// 模拟交易流水实体。
/// </summary>
public sealed class SimulatedTradeHistoryEntity
{
    /// <summary>主键。</summary>
    public int Id { get; set; }

    /// <summary>对应持仓主键。</summary>
    public int PositionId { get; set; }

    /// <summary>动作类型。</summary>
    public string ActionType { get; set; } = string.Empty;

    /// <summary>股票代码。</summary>
    public string StockCode { get; set; } = string.Empty;

    /// <summary>股票名称。</summary>
    public string StockName { get; set; } = string.Empty;

    /// <summary>交易日。</summary>
    public DateOnly TradeDate { get; set; }

    /// <summary>成交价格。</summary>
    public decimal Price { get; set; }

    /// <summary>成交股数。</summary>
    public int Quantity { get; set; }

    /// <summary>成交金额。</summary>
    public decimal Amount { get; set; }

    /// <summary>摘要说明。</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>创建时间。</summary>
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// 回测运行结果实体。
/// </summary>
public sealed class BacktestRunEntity
{
    /// <summary>主键。</summary>
    public int Id { get; set; }

    /// <summary>策略版本。</summary>
    public string StrategyVersion { get; set; } = string.Empty;

    /// <summary>快照版本。</summary>
    public string SnapshotVersion { get; set; } = string.Empty;

    /// <summary>开始日期。</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>结束日期。</summary>
    public DateOnly EndDate { get; set; }

    /// <summary>样本交易数。</summary>
    public int SampleTradeCount { get; set; }

    /// <summary>胜率。</summary>
    public decimal WinRatePct { get; set; }

    /// <summary>平均收益率。</summary>
    public decimal AverageReturnPct { get; set; }

    /// <summary>平均最大盈利。</summary>
    public decimal AverageMaxGainPct { get; set; }

    /// <summary>平均最大回撤。</summary>
    public decimal AverageMaxDrawdownPct { get; set; }

    /// <summary>盈亏比。</summary>
    public decimal ProfitLossRatio { get; set; }

    /// <summary>最大回撤。</summary>
    public decimal MaxDrawdownPct { get; set; }

    /// <summary>总收益率。</summary>
    public decimal TotalReturnPct { get; set; }

    /// <summary>平均持有天数。</summary>
    public decimal AverageHoldingDays { get; set; }

    /// <summary>创建时间。</summary>
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// 回测交易明细实体。
/// </summary>
public sealed class BacktestTradeResultEntity
{
    /// <summary>主键。</summary>
    public int Id { get; set; }

    /// <summary>回测运行主键。</summary>
    public int BacktestRunId { get; set; }

    /// <summary>交易日。</summary>
    public DateOnly TradeDate { get; set; }

    /// <summary>股票代码。</summary>
    public string StockCode { get; set; } = string.Empty;

    /// <summary>股票名称。</summary>
    public string StockName { get; set; } = string.Empty;

    /// <summary>策略类型。</summary>
    public string StrategyType { get; set; } = string.Empty;

    /// <summary>入场价。</summary>
    public decimal EntryPrice { get; set; }

    /// <summary>出场价。</summary>
    public decimal ExitPrice { get; set; }

    /// <summary>收益率。</summary>
    public decimal ReturnPct { get; set; }

    /// <summary>最大盈利。</summary>
    public decimal MaxGainPct { get; set; }

    /// <summary>最大回撤。</summary>
    public decimal MaxDrawdownPct { get; set; }

    /// <summary>是否命中止盈。</summary>
    public bool HitTarget { get; set; }

    /// <summary>是否命中止损。</summary>
    public bool HitStopLoss { get; set; }
}

/// <summary>
/// 回测净值曲线点实体。
/// </summary>
public sealed class BacktestEquityPointEntity
{
    /// <summary>主键。</summary>
    public int Id { get; set; }

    /// <summary>回测运行主键。</summary>
    public int BacktestRunId { get; set; }

    /// <summary>交易日。</summary>
    public DateOnly TradeDate { get; set; }

    /// <summary>净值。</summary>
    public decimal Equity { get; set; }

    /// <summary>累计收益率。</summary>
    public decimal ReturnPct { get; set; }
}

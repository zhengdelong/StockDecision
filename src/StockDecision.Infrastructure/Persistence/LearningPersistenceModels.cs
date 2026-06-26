namespace StockDecision.Infrastructure.Persistence;

/// <summary>
/// 学习复盘记录实体。
/// </summary>
public sealed class LearningReviewEntity
{
    /// <summary>主键。</summary>
    public int Id { get; set; }

    /// <summary>关联的模拟持仓主键。</summary>
    public int? PositionId { get; set; }

    /// <summary>股票代码。</summary>
    public string StockCode { get; set; } = string.Empty;

    /// <summary>股票名称。</summary>
    public string StockName { get; set; } = string.Empty;

    /// <summary>交易日。</summary>
    public DateOnly TradeDate { get; set; }

    /// <summary>快照版本。</summary>
    public string SnapshotVersion { get; set; } = string.Empty;

    /// <summary>买入原因。</summary>
    public string BuyReason { get; set; } = string.Empty;

    /// <summary>市场环境。</summary>
    public string MarketContext { get; set; } = string.Empty;

    /// <summary>纪律执行情况。</summary>
    public string ExecutionDiscipline { get; set; } = string.Empty;

    /// <summary>结果总结。</summary>
    public string ResultSummary { get; set; } = string.Empty;

    /// <summary>改进计划。</summary>
    public string ImprovementPlan { get; set; } = string.Empty;
    /// <summary>错误标签，使用 | 分隔。</summary>
    public string ErrorTags { get; set; } = string.Empty;
    /// <summary>是否策略内交易。</summary>
    public bool IsStrategyAligned { get; set; }
    /// <summary>是否执行止损纪律。</summary>
    public bool FollowedStopLoss { get; set; }
    /// <summary>是否执行止盈纪律。</summary>
    public bool FollowedTakeProfit { get; set; }
    /// <summary>是否中途改计划。</summary>
    public bool ModifiedPlanDuringTrade { get; set; }
    /// <summary>是否遵守高开放弃规则。</summary>
    public bool FollowedGapRule { get; set; }

    /// <summary>创建时间。</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>更新时间。</summary>
    public DateTime UpdatedAtUtc { get; set; }
}

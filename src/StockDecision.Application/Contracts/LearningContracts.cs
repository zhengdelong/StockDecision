namespace StockDecision.Application.Contracts;

/// <summary>
/// 单条学习复盘记录。
/// </summary>
public sealed record LearningReviewItemResponse(
    int Id,
    int? PositionId,
    string StockCode,
    string StockName,
    DateOnly TradeDate,
    string SnapshotVersion,
    string BuyReason,
    string MarketContext,
    string ExecutionDiscipline,
    string ResultSummary,
    string ImprovementPlan,
    IReadOnlyList<string> ErrorTags,
    bool IsStrategyAligned,
    bool FollowedStopLoss,
    bool FollowedTakeProfit,
    bool ModifiedPlanDuringTrade,
    bool FollowedGapRule,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

/// <summary>
/// 学习进度摘要。
/// </summary>
public sealed record LearningProgressSummaryResponse(
    int ReviewCount,
    int StrategyAlignedTradeCount,
    int OffStrategyTradeCount,
    int ConsecutiveStopLossFollowCount,
    int ConsecutiveGapRuleFollowCount);

/// <summary>
/// 复盘错误标签统计。
/// </summary>
public sealed record LearningErrorTagStatResponse(
    string Tag,
    int Count);

/// <summary>
/// 学习复盘页面所需的概览数据。
/// </summary>
public sealed record LearningReviewOverviewResponse(
    string? StockCode,
    string? StockName,
    DateOnly? TradeDate,
    string SnapshotVersion,
    IReadOnlyList<string> ReviewPrompts,
    LearningProgressSummaryResponse ProgressSummary,
    IReadOnlyList<LearningErrorTagStatResponse> ErrorTagStats,
    IReadOnlyList<LearningReviewItemResponse> Reviews);

/// <summary>
/// 新增或更新复盘记录的请求。
/// </summary>
public sealed record SaveLearningReviewRequest
{
    /// <summary>已有记录主键，为空时表示新增。</summary>
    public int? Id { get; init; }

    /// <summary>关联的模拟持仓主键，可为空。</summary>
    public int? PositionId { get; init; }

    /// <summary>股票代码。</summary>
    public string StockCode { get; init; } = string.Empty;

    /// <summary>股票名称。</summary>
    public string StockName { get; init; } = string.Empty;

    /// <summary>本次复盘对应的交易日。</summary>
    public DateOnly TradeDate { get; init; }

    /// <summary>快照版本，默认按正式版解析。</summary>
    public string? SnapshotVersion { get; init; }

    /// <summary>买入原因。</summary>
    public string BuyReason { get; init; } = string.Empty;

    /// <summary>买入时的市场环境。</summary>
    public string MarketContext { get; init; } = string.Empty;

    /// <summary>纪律执行情况。</summary>
    public string ExecutionDiscipline { get; init; } = string.Empty;

    /// <summary>最终结果总结。</summary>
    public string ResultSummary { get; init; } = string.Empty;

    /// <summary>下次准备怎么改进。</summary>
    public string ImprovementPlan { get; init; } = string.Empty;

    /// <summary>错误标签。</summary>
    public IReadOnlyList<string>? ErrorTags { get; init; }

    /// <summary>是否属于策略内交易。</summary>
    public bool IsStrategyAligned { get; init; } = true;

    /// <summary>是否执行了止损纪律。</summary>
    public bool FollowedStopLoss { get; init; }

    /// <summary>是否执行了止盈纪律。</summary>
    public bool FollowedTakeProfit { get; init; }

    /// <summary>是否中途改计划。</summary>
    public bool ModifiedPlanDuringTrade { get; init; }

    /// <summary>是否遵守高开放弃规则。</summary>
    public bool FollowedGapRule { get; init; }
}

/// <summary>
/// 学习复盘仓储接口。
/// </summary>
public interface ILearningReviewRepository
{
    /// <summary>读取全部复盘记录。</summary>
    Task<IReadOnlyList<LearningReviewItemResponse>> GetReviewsAsync(CancellationToken cancellationToken);

    /// <summary>按股票代码读取复盘记录。</summary>
    Task<IReadOnlyList<LearningReviewItemResponse>> GetReviewsByStockAsync(string stockCode, CancellationToken cancellationToken);

    /// <summary>保存一条复盘记录。</summary>
    Task<LearningReviewItemResponse> SaveReviewAsync(LearningReviewDraft draft, CancellationToken cancellationToken);
}

/// <summary>
/// 保存复盘时在应用层内部流转的草稿对象。
/// </summary>
public sealed record LearningReviewDraft(
    int? Id,
    int? PositionId,
    string StockCode,
    string StockName,
    DateOnly TradeDate,
    string SnapshotVersion,
    string BuyReason,
    string MarketContext,
    string ExecutionDiscipline,
    string ResultSummary,
    string ImprovementPlan,
    IReadOnlyList<string> ErrorTags,
    bool IsStrategyAligned,
    bool FollowedStopLoss,
    bool FollowedTakeProfit,
    bool ModifiedPlanDuringTrade,
    bool FollowedGapRule,
    DateTime TimestampUtc);

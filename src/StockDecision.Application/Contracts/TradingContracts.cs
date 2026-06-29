using StockDecision.Domain.Strategy;

namespace StockDecision.Application.Contracts;

/// <summary>
/// 模拟持仓查询结果。
/// </summary>
public sealed record SimulatedPositionItemResponse(
    int Id,
    string StockCode,
    string StockName,
    string? IndustryName,
    string StrategyType,
    string SnapshotVersion,
    DateOnly TradeDate,
    decimal EntryPrice,
    decimal StopLossPrice,
    decimal TargetPrice,
    int Quantity,
    decimal InvestedCapital,
    decimal? LatestPrice,
    DateOnly? LatestTradeDate,
    decimal FloatingProfitAmount,
    decimal FloatingProfitPct,
    int HeldDays,
    string Status,
    string AdviceStatus,
    string AdviceTitle,
    string AdviceText,
    IReadOnlyList<string> AdviceTags,
    TradeExecutionPlanResponse? ExecutionPlan,
    DateTime OpenedAtUtc,
    DateTime? ClosedAtUtc,
    decimal? ExitPrice,
    decimal? RealizedProfitAmount,
    decimal? RealizedProfitPct,
    string? Notes);

/// <summary>
/// 模拟交易历史记录。
/// </summary>
public sealed record SimulatedTradeHistoryItemResponse(
    int Id,
    int PositionId,
    string ActionType,
    string StockCode,
    string StockName,
    DateOnly TradeDate,
    decimal Price,
    int Quantity,
    decimal Amount,
    string Summary,
    DateTime CreatedAtUtc);

/// <summary>
/// 发起模拟买入请求。
/// </summary>
public sealed record SimulateBuyRequest
{
    /// <summary>股票代码。</summary>
    public string StockCode { get; init; } = string.Empty;

    /// <summary>信号交易日，空值时默认使用最新交易日。</summary>
    public DateOnly? TradeDate { get; init; }

    /// <summary>快照版本，空值时默认使用正式版。</summary>
    public string? SnapshotVersion { get; init; }

    /// <summary>买入价格，空值时使用信号触发价。</summary>
    public decimal? EntryPrice { get; init; }

    /// <summary>买入股数，空值时使用系统预估股数。</summary>
    public int? Quantity { get; init; }

    /// <summary>备注。</summary>
    public string? Notes { get; init; }
}

/// <summary>
/// 发起模拟卖出请求。
/// </summary>
public sealed record SimulateSellRequest
{
    /// <summary>卖出价格，空值时使用最新价。</summary>
    public decimal? ExitPrice { get; init; }

    /// <summary>卖出交易日，空值时默认使用最新交易日。</summary>
    public DateOnly? TradeDate { get; init; }

    /// <summary>备注。</summary>
    public string? Notes { get; init; }
}

/// <summary>
/// 回测执行请求。
/// </summary>
public sealed record RunBacktestRequest
{
    /// <summary>开始日期。</summary>
    public DateOnly StartDate { get; init; }

    /// <summary>结束日期。</summary>
    public DateOnly EndDate { get; init; }

    /// <summary>快照版本，空值时默认使用正式版。</summary>
    public string? SnapshotVersion { get; init; }

    /// <summary>每个交易日最多纳入多少只股票。</summary>
    public int MaxSignalsPerDay { get; init; } = 5;

    /// <summary>持有天数上限。</summary>
    public int MaxHoldingDays { get; init; } = 5;
}

/// <summary>
/// 回测列表项。
/// </summary>
public sealed record BacktestRunListItemResponse(
    int Id,
    string StrategyVersion,
    string SnapshotVersion,
    DateOnly StartDate,
    DateOnly EndDate,
    int SampleTradeCount,
    decimal WinRatePct,
    decimal AverageReturnPct,
    decimal ProfitLossRatio,
    decimal MaxDrawdownPct,
    decimal TotalReturnPct,
    decimal BenchmarkReturnPct,
    decimal DataCoveragePct,
    int SkippedTradeDays,
    decimal AnnualTradeCount,
    int MaxConsecutiveLosses,
    bool IsApproved,
    DateTime CreatedAtUtc);

/// <summary>
/// 回测净值曲线点。
/// </summary>
public sealed record BacktestEquityPointResponse(
    DateOnly TradeDate,
    decimal Equity,
    decimal ReturnPct);

/// <summary>
/// 单次回测结果详情。
/// </summary>
public sealed record BacktestRunDetailResponse(
    int Id,
    string StrategyVersion,
    string SnapshotVersion,
    DateOnly StartDate,
    DateOnly EndDate,
    int SampleTradeCount,
    decimal WinRatePct,
    decimal AverageReturnPct,
    decimal AverageMaxGainPct,
    decimal AverageMaxDrawdownPct,
    decimal ProfitLossRatio,
    decimal MaxDrawdownPct,
    decimal TotalReturnPct,
    decimal BenchmarkReturnPct,
    decimal DataCoveragePct,
    int SkippedTradeDays,
    decimal AnnualTradeCount,
    int MaxConsecutiveLosses,
    bool IsApproved,
    IReadOnlyList<string> FailureReasons,
    decimal AverageHoldingDays,
    DateTime CreatedAtUtc,
    IReadOnlyList<BacktestEquityPointResponse> EquityCurve,
    IReadOnlyList<BacktestTradeItemResponse> Trades);

/// <summary>
/// 模拟持仓仓储接口。
/// </summary>
public interface ISimulatedTradingRepository
{
    /// <summary>新增模拟持仓。</summary>
    Task<int> AddPositionAsync(SimulatedPositionDraft draft, CancellationToken cancellationToken);

    /// <summary>读取全部持仓。</summary>
    Task<IReadOnlyList<SimulatedPositionState>> GetPositionsAsync(bool includeClosed, CancellationToken cancellationToken);

    /// <summary>按主键读取单个持仓。</summary>
    Task<SimulatedPositionState?> GetPositionAsync(int id, CancellationToken cancellationToken);

    /// <summary>关闭持仓。</summary>
    Task<SimulatedPositionState?> ClosePositionAsync(int id, DateOnly tradeDate, decimal exitPrice, string? notes, CancellationToken cancellationToken);

    /// <summary>新增交易流水。</summary>
    Task AddTradeHistoryAsync(SimulatedTradeHistoryDraft draft, CancellationToken cancellationToken);

    /// <summary>读取历史交易流水。</summary>
    Task<IReadOnlyList<SimulatedTradeHistoryItemResponse>> GetHistoryAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 回测结果仓储接口。
/// </summary>
public interface IBacktestRunRepository
{
    /// <summary>保存回测结果。</summary>
    Task<int> AddRunAsync(BacktestRunDraft draft, CancellationToken cancellationToken);

    /// <summary>读取回测列表。</summary>
    Task<IReadOnlyList<BacktestRunListItemResponse>> GetRunsAsync(CancellationToken cancellationToken);

    /// <summary>读取单次回测详情。</summary>
    Task<BacktestRunDetailResponse?> GetRunAsync(int id, CancellationToken cancellationToken);

    /// <summary>读取最近一次回测结果。</summary>
    Task<BacktestRunDetailResponse?> GetLatestRunAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 新建模拟持仓时的内部草稿。
/// </summary>
public sealed record SimulatedPositionDraft(
    string StockCode,
    string StockName,
    string? IndustryName,
    string StrategyType,
    string SnapshotVersion,
    DateOnly TradeDate,
    decimal EntryPrice,
    decimal StopLossPrice,
    decimal TargetPrice,
    int Quantity,
    decimal InvestedCapital,
    string Status,
    DateTime OpenedAtUtc,
    string? Notes);

/// <summary>
/// 持仓状态内部模型。
/// </summary>
public sealed record SimulatedPositionState(
    int Id,
    string StockCode,
    string StockName,
    string? IndustryName,
    string StrategyType,
    string SnapshotVersion,
    DateOnly TradeDate,
    decimal EntryPrice,
    decimal StopLossPrice,
    decimal TargetPrice,
    int Quantity,
    decimal InvestedCapital,
    string Status,
    DateTime OpenedAtUtc,
    DateTime? ClosedAtUtc,
    DateOnly? ClosedTradeDate,
    decimal? ExitPrice,
    decimal? RealizedProfitAmount,
    decimal? RealizedProfitPct,
    string? Notes);

/// <summary>
/// 新增交易流水时的内部草稿。
/// </summary>
public sealed record SimulatedTradeHistoryDraft(
    int PositionId,
    string ActionType,
    string StockCode,
    string StockName,
    DateOnly TradeDate,
    decimal Price,
    int Quantity,
    decimal Amount,
    string Summary,
    DateTime CreatedAtUtc);

/// <summary>
/// 回测结果保存草稿。
/// </summary>
public sealed record BacktestRunDraft(
    string StrategyVersion,
    string SnapshotVersion,
    DateOnly StartDate,
    DateOnly EndDate,
    int SampleTradeCount,
    decimal WinRatePct,
    decimal AverageReturnPct,
    decimal AverageMaxGainPct,
    decimal AverageMaxDrawdownPct,
    decimal ProfitLossRatio,
    decimal MaxDrawdownPct,
    decimal TotalReturnPct,
    decimal BenchmarkReturnPct,
    decimal DataCoveragePct,
    int SkippedTradeDays,
    decimal AnnualTradeCount,
    int MaxConsecutiveLosses,
    bool IsApproved,
    string FailureReasons,
    decimal AverageHoldingDays,
    DateTime CreatedAtUtc,
    IReadOnlyList<BacktestEquityPointResponse> EquityCurve,
    IReadOnlyList<BacktestTradeItemResponse> Trades);

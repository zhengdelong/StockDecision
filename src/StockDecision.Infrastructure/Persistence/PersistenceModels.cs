namespace StockDecision.Infrastructure.Persistence;

/// <summary>
/// 原始股票基础信息行。
/// </summary>
public sealed class RawStockRow
{
    /// <summary>主键。</summary>
    public int Id { get; set; }
    /// <summary>股票代码。</summary>
    public string StockCode { get; set; } = string.Empty;
    /// <summary>股票名称。</summary>
    public string StockName { get; set; } = string.Empty;
    /// <summary>行业名称。</summary>
    public string? IndustryName { get; set; }
    /// <summary>是否有效。</summary>
    public bool IsActive { get; set; }
    /// <summary>是否 ST。</summary>
    public bool IsSt { get; set; }
    /// <summary>是否存在退市风险。</summary>
    public bool IsDelistingRisk { get; set; }
    /// <summary>上市日期。</summary>
    public DateOnly? ListDate { get; set; }
    /// <summary>市盈率。</summary>
    public decimal? Pe { get; set; }
    /// <summary>市净率。</summary>
    public decimal? Pb { get; set; }
    /// <summary>换手率。</summary>
    public decimal? TurnoverRate { get; set; }
    /// <summary>采集时间。</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 领域同步运行日志行。
/// </summary>
public sealed class DomainSyncRunRow
{
    /// <summary>主键。</summary>
    public int Id { get; set; }
    /// <summary>任务名。</summary>
    public string JobName { get; set; } = string.Empty;
    /// <summary>触发来源。</summary>
    public string TriggerKind { get; set; } = string.Empty;
    /// <summary>本次同步写入的快照版本。</summary>
    public string SnapshotVersion { get; set; } = string.Empty;
    /// <summary>运行状态。</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>本次是否实际更新了领域数据。</summary>
    public bool DataUpdated { get; set; }
    /// <summary>本次运行后市场是否允许信号。</summary>
    public bool IsSignalEligible { get; set; }
    /// <summary>本次同步对应的交易日。</summary>
    public DateOnly? EffectiveTradeDate { get; set; }
    /// <summary>本次同步对应的财报日期。</summary>
    public DateOnly? FinancialReportDate { get; set; }
    /// <summary>开始时间。</summary>
    public DateTime StartedAt { get; set; }
    /// <summary>结束时间。</summary>
    public DateTime? FinishedAt { get; set; }
    /// <summary>摘要说明。</summary>
    public string Summary { get; set; } = string.Empty;
    /// <summary>创建时间。</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 原始股票日线行。
/// </summary>
public sealed class RawDailyBarRow
{
    /// <summary>主键。</summary>
    public int Id { get; set; }
    /// <summary>股票代码。</summary>
    public string StockCode { get; set; } = string.Empty;
    /// <summary>交易日。</summary>
    public DateOnly TradeDate { get; set; }
    /// <summary>开盘价。</summary>
    public decimal? Open { get; set; }
    /// <summary>最高价。</summary>
    public decimal? High { get; set; }
    /// <summary>最低价。</summary>
    public decimal? Low { get; set; }
    /// <summary>收盘价。</summary>
    public decimal? Close { get; set; }
    /// <summary>成交量。</summary>
    public long? Volume { get; set; }
    /// <summary>成交额。</summary>
    public decimal? Amount { get; set; }
    /// <summary>涨跌幅。</summary>
    public decimal? PctChange { get; set; }
    /// <summary>换手率。</summary>
    public decimal? TurnoverRate { get; set; }
}

/// <summary>
/// 原始指数日线行。
/// </summary>
public sealed class RawMarketIndexBarRow
{
    /// <summary>主键。</summary>
    public int Id { get; set; }
    /// <summary>指数代码。</summary>
    public string IndexCode { get; set; } = string.Empty;
    /// <summary>指数名称。</summary>
    public string IndexName { get; set; } = string.Empty;
    /// <summary>交易日。</summary>
    public DateOnly TradeDate { get; set; }
    /// <summary>收盘点位。</summary>
    public decimal? Close { get; set; }
}

/// <summary>
/// 原始行业日统计行。
/// </summary>
public sealed class RawIndustryDailyStatRow
{
    /// <summary>主键。</summary>
    public int Id { get; set; }
    /// <summary>行业代码。</summary>
    public string IndustryCode { get; set; } = string.Empty;
    /// <summary>行业名称。</summary>
    public string IndustryName { get; set; } = string.Empty;
    /// <summary>交易日。</summary>
    public DateOnly TradeDate { get; set; }
    /// <summary>近 20 日涨跌幅。</summary>
    public decimal? PctChange20d { get; set; }
    /// <summary>近 20 日排名。</summary>
    public int? Rank20d { get; set; }
}

/// <summary>
/// 原始财务快照行。
/// </summary>
public sealed class RawFinancialSnapshotRow
{
    /// <summary>主键。</summary>
    public int Id { get; set; }
    /// <summary>股票代码。</summary>
    public string StockCode { get; set; } = string.Empty;
    /// <summary>财报日期。</summary>
    public DateOnly ReportDate { get; set; }
    /// <summary>市盈率。</summary>
    public decimal? Pe { get; set; }
    /// <summary>市净率。</summary>
    public decimal? Pb { get; set; }
    /// <summary>净资产收益率。</summary>
    public decimal? Roe { get; set; }
    /// <summary>营收同比。</summary>
    public decimal? RevenueYoy { get; set; }
    /// <summary>净利润同比。</summary>
    public decimal? NetProfitYoy { get; set; }
    /// <summary>流通市值。</summary>
    public decimal? FreeFloatMarketCap { get; set; }
}

/// <summary>
/// 采集任务日志行。
/// </summary>
public sealed class DataIngestionLogRow
{
    /// <summary>主键。</summary>
    public int Id { get; set; }
    /// <summary>采集范围。</summary>
    public string TargetScope { get; set; } = string.Empty;
    /// <summary>是否完整。</summary>
    public bool IsComplete { get; set; }
    /// <summary>是否达到可交易市场状态。</summary>
    public bool IsSignalEligible { get; set; }
    /// <summary>写入时间。</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 领域层股票画像持久化实体。
/// </summary>
public sealed class MarketStockProfileEntity
{
    /// <summary>股票代码。</summary>
    public string StockCode { get; set; } = string.Empty;
    /// <summary>交易日。</summary>
    public DateOnly TradeDate { get; set; }
    /// <summary>股票名称。</summary>
    public string StockName { get; set; } = string.Empty;
    /// <summary>行业名称。</summary>
    public string? IndustryName { get; set; }
    /// <summary>是否有效。</summary>
    public bool IsActive { get; set; }
    /// <summary>是否 ST。</summary>
    public bool IsSt { get; set; }
    /// <summary>是否存在退市风险。</summary>
    public bool IsDelistingRisk { get; set; }
    /// <summary>上市日期。</summary>
    public DateOnly? ListDate { get; set; }
    /// <summary>最新价格。</summary>
    public decimal? LatestPrice { get; set; }
    /// <summary>市盈率。</summary>
    public decimal? Pe { get; set; }
    /// <summary>市净率。</summary>
    public decimal? Pb { get; set; }
    /// <summary>流通市值。</summary>
    public decimal? FreeFloatMarketCap { get; set; }
    /// <summary>换手率。</summary>
    public decimal? TurnoverRate { get; set; }
    /// <summary>近 20 日平均成交额。</summary>
    public decimal? AverageAmount20d { get; set; }
}

/// <summary>
/// 领域层股票日线实体。
/// </summary>
public sealed class MarketDailyBarEntity
{
    /// <summary>股票代码。</summary>
    public string StockCode { get; set; } = string.Empty;
    /// <summary>交易日。</summary>
    public DateOnly TradeDate { get; set; }
    /// <summary>开盘价。</summary>
    public decimal Open { get; set; }
    /// <summary>最高价。</summary>
    public decimal High { get; set; }
    /// <summary>最低价。</summary>
    public decimal Low { get; set; }
    /// <summary>收盘价。</summary>
    public decimal Close { get; set; }
    /// <summary>成交量。</summary>
    public long Volume { get; set; }
    /// <summary>成交额。</summary>
    public decimal Amount { get; set; }
    /// <summary>涨跌幅。</summary>
    public decimal? PctChange { get; set; }
    /// <summary>换手率。</summary>
    public decimal? TurnoverRate { get; set; }
}

/// <summary>
/// 领域层指数日线实体。
/// </summary>
public sealed class MarketIndexBarEntity
{
    /// <summary>指数代码。</summary>
    public string IndexCode { get; set; } = string.Empty;
    /// <summary>交易日。</summary>
    public DateOnly TradeDate { get; set; }
    /// <summary>指数名称。</summary>
    public string IndexName { get; set; } = string.Empty;
    /// <summary>收盘点位。</summary>
    public decimal Close { get; set; }
}

/// <summary>
/// 领域层行业统计实体。
/// </summary>
public sealed class MarketIndustryDailyStatEntity
{
    /// <summary>行业代码。</summary>
    public string IndustryCode { get; set; } = string.Empty;
    /// <summary>交易日。</summary>
    public DateOnly TradeDate { get; set; }
    /// <summary>行业名称。</summary>
    public string IndustryName { get; set; } = string.Empty;
    /// <summary>近 20 日涨跌幅。</summary>
    public decimal? PctChange20d { get; set; }
    /// <summary>近 20 日排名。</summary>
    public int? Rank20d { get; set; }
}

/// <summary>
/// 领域层财务快照实体。
/// </summary>
public sealed class MarketFinancialSnapshotEntity
{
    /// <summary>股票代码。</summary>
    public string StockCode { get; set; } = string.Empty;
    /// <summary>财报日期。</summary>
    public DateOnly ReportDate { get; set; }
    /// <summary>市盈率。</summary>
    public decimal? Pe { get; set; }
    /// <summary>市净率。</summary>
    public decimal? Pb { get; set; }
    /// <summary>净资产收益率。</summary>
    public decimal? Roe { get; set; }
    /// <summary>营收同比。</summary>
    public decimal? RevenueYoy { get; set; }
    /// <summary>净利润同比。</summary>
    public decimal? NetProfitYoy { get; set; }
    /// <summary>流通市值。</summary>
    public decimal? FreeFloatMarketCap { get; set; }
}

/// <summary>
/// 指标快照实体。
/// </summary>
public sealed class StrategyIndicatorSnapshotEntity
{
    /// <summary>股票代码。</summary>
    public string StockCode { get; set; } = string.Empty;
    /// <summary>交易日。</summary>
    public DateOnly TradeDate { get; set; }
    /// <summary>快照版本。</summary>
    public string SnapshotVersion { get; set; } = string.Empty;
    /// <summary>收盘价。</summary>
    public decimal Close { get; set; }
    /// <summary>20 日均线。</summary>
    public decimal Ma20 { get; set; }
    /// <summary>60 日均线。</summary>
    public decimal Ma60 { get; set; }
    /// <summary>120 日均线。</summary>
    public decimal Ma120 { get; set; }
    /// <summary>14 日 ATR。</summary>
    public decimal Atr14 { get; set; }
    /// <summary>20 日收益率。</summary>
    public decimal Return20d { get; set; }
    /// <summary>60 日收益率。</summary>
    public decimal Return60d { get; set; }
    /// <summary>相对强弱得分。</summary>
    public decimal RelativeStrengthScore { get; set; }
    /// <summary>是否突破 20 日高点。</summary>
    public bool Is20DayBreakout { get; set; }
    /// <summary>20 日均线是否上行。</summary>
    public bool IsMa20Upward { get; set; }
    /// <summary>是否多头排列。</summary>
    public bool IsBullishStacked { get; set; }
    /// <summary>距离 20 日均线的偏离百分比。</summary>
    public decimal DistanceToMa20Pct { get; set; }
    /// <summary>换手率。</summary>
    public decimal? TurnoverRate { get; set; }
}

/// <summary>
/// 市场环境实体。
/// </summary>
public sealed class StrategyMarketRegimeEntity
{
    /// <summary>交易日。</summary>
    public DateOnly TradeDate { get; set; }
    /// <summary>快照版本。</summary>
    public string SnapshotVersion { get; set; } = string.Empty;
    /// <summary>市场环境枚举名称。</summary>
    public string Regime { get; set; } = string.Empty;
    /// <summary>确认指数数量。</summary>
    public int ConfirmedIndexCount { get; set; }
    /// <summary>是否允许执行信号。</summary>
    public bool IsSignalEligible { get; set; }
    /// <summary>环境说明。</summary>
    public string Summary { get; set; } = string.Empty;
}

/// <summary>
/// 候选股实体。
/// </summary>
public sealed class StrategyCandidateEntity
{
    /// <summary>交易日。</summary>
    public DateOnly TradeDate { get; set; }
    /// <summary>快照版本。</summary>
    public string SnapshotVersion { get; set; } = string.Empty;
    /// <summary>股票代码。</summary>
    public string StockCode { get; set; } = string.Empty;
    /// <summary>股票名称。</summary>
    public string StockName { get; set; } = string.Empty;
    /// <summary>行业名称。</summary>
    public string? IndustryName { get; set; }
    /// <summary>评级。</summary>
    public string Grade { get; set; } = string.Empty;
    /// <summary>策略类型。</summary>
    public string StrategyType { get; set; } = string.Empty;
    /// <summary>是否可执行。</summary>
    public bool IsTradable { get; set; }
    /// <summary>总分。</summary>
    public decimal TotalScore { get; set; }
    /// <summary>相对强弱分项。</summary>
    public decimal RelativeStrengthScorePart { get; set; }
    /// <summary>趋势分项。</summary>
    public decimal TrendScorePart { get; set; }
    /// <summary>量价分项。</summary>
    public decimal VolumePriceScorePart { get; set; }
    /// <summary>基本面分项。</summary>
    public decimal FundamentalScorePart { get; set; }
    /// <summary>收盘价。</summary>
    public decimal Close { get; set; }
    /// <summary>20 日均线。</summary>
    public decimal Ma20 { get; set; }
    /// <summary>60 日均线。</summary>
    public decimal Ma60 { get; set; }
    /// <summary>120 日均线。</summary>
    public decimal Ma120 { get; set; }
    /// <summary>14 日 ATR。</summary>
    public decimal Atr14 { get; set; }
    /// <summary>相对强弱总分。</summary>
    public decimal RelativeStrengthScore { get; set; }
    /// <summary>市盈率。</summary>
    public decimal? Pe { get; set; }
    /// <summary>市净率。</summary>
    public decimal? Pb { get; set; }
    /// <summary>净资产收益率。</summary>
    public decimal? Roe { get; set; }
    /// <summary>止损价。</summary>
    public decimal StopLossPrice { get; set; }
    /// <summary>目标价。</summary>
    public decimal TargetPrice { get; set; }
    /// <summary>风险收益比。</summary>
    public decimal RiskRewardRatio { get; set; }
    /// <summary>解释文本。</summary>
    public string Explanation { get; set; } = string.Empty;
}

/// <summary>
/// 交易信号实体。
/// </summary>
public sealed class StrategyTradeSignalEntity
{
    /// <summary>交易日。</summary>
    public DateOnly TradeDate { get; set; }
    /// <summary>快照版本。</summary>
    public string SnapshotVersion { get; set; } = string.Empty;
    /// <summary>股票代码。</summary>
    public string StockCode { get; set; } = string.Empty;
    /// <summary>股票名称。</summary>
    public string StockName { get; set; } = string.Empty;
    /// <summary>行业名称。</summary>
    public string? IndustryName { get; set; }
    /// <summary>策略类型。</summary>
    public string StrategyType { get; set; } = string.Empty;
    /// <summary>总分。</summary>
    public decimal TotalScore { get; set; }
    /// <summary>相对强弱分项。</summary>
    public decimal RelativeStrengthScorePart { get; set; }
    /// <summary>趋势分项。</summary>
    public decimal TrendScorePart { get; set; }
    /// <summary>量价分项。</summary>
    public decimal VolumePriceScorePart { get; set; }
    /// <summary>基本面分项。</summary>
    public decimal FundamentalScorePart { get; set; }
    /// <summary>触发价。</summary>
    public decimal TriggerPrice { get; set; }
    /// <summary>止损价。</summary>
    public decimal StopLossPrice { get; set; }
    /// <summary>目标价。</summary>
    public decimal TargetPrice { get; set; }
    /// <summary>风险收益比。</summary>
    public decimal RiskRewardRatio { get; set; }
    /// <summary>建议资金。</summary>
    public decimal SuggestedCapital { get; set; }
    /// <summary>预估股数。</summary>
    public int EstimatedShares { get; set; }
    /// <summary>解释文本。</summary>
    public string Explanation { get; set; } = string.Empty;
    /// <summary>UTC 生成时间。</summary>
    public DateTime GeneratedAtUtc { get; set; }
}

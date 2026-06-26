using StockDecision.Domain.Market;
using StockDecision.Domain.Strategy;

namespace StockDecision.Application.Contracts;

/// <summary>
/// 首页仪表盘响应。
/// </summary>
public sealed record DashboardResponse
{
    /// <summary>
    /// 初始化仪表盘响应。
    /// </summary>
    public DashboardResponse(
        DateOnly? tradeDate,
        string snapshotVersion,
        string snapshotVersionName,
        bool isDataComplete,
        bool isSignalEligible,
        bool isBacktestApproved,
        string backtestStatusNote,
        string marketRegime,
        int candidateCount,
        int signalCount,
        DateTime? latestIngestionAtUtc,
        IReadOnlyList<DashboardMetricResponse> metrics)
    {
        TradeDate = tradeDate;
        SnapshotVersion = snapshotVersion;
        SnapshotVersionName = snapshotVersionName;
        IsDataComplete = isDataComplete;
        IsSignalEligible = isSignalEligible;
        IsBacktestApproved = isBacktestApproved;
        BacktestStatusNote = backtestStatusNote;
        MarketRegime = marketRegime;
        CandidateCount = candidateCount;
        SignalCount = signalCount;
        LatestIngestionAtUtc = latestIngestionAtUtc;
        Metrics = metrics;
    }

    /// <summary>
    /// 当前展示的交易日。
    /// </summary>
    public DateOnly? TradeDate { get; init; }
    /// <summary>
    /// 当前返回数据对应的快照版本值。
    /// </summary>
    public string SnapshotVersion { get; init; }
    /// <summary>
    /// 当前返回数据对应的快照版本中文名。
    /// </summary>
    public string SnapshotVersionName { get; init; }

    /// <summary>
    /// 数据是否完整可用。
    /// </summary>
    public bool IsDataComplete { get; init; }

    /// <summary>
    /// 当前市场是否允许执行信号。
    /// </summary>
    public bool IsSignalEligible { get; init; }
    /// <summary>
    /// 最近一次回测是否已通过准入标准。
    /// </summary>
    public bool IsBacktestApproved { get; init; }

    /// <summary>
    /// 回测准入状态说明。
    /// </summary>
    public string BacktestStatusNote { get; init; }

    /// <summary>
    /// 市场环境文本。
    /// </summary>
    public string MarketRegime { get; init; }

    /// <summary>
    /// 候选股数量。
    /// </summary>
    public int CandidateCount { get; init; }

    /// <summary>
    /// 交易信号数量。
    /// </summary>
    public int SignalCount { get; init; }

    /// <summary>
    /// 最近一次成功采集时间。
    /// </summary>
    public DateTime? LatestIngestionAtUtc { get; init; }

    /// <summary>
    /// 仪表盘指标列表。
    /// </summary>
    public IReadOnlyList<DashboardMetricResponse> Metrics { get; init; }
}

public sealed record ScoreRuleDetailResponse(
    string Key,
    string Dimension,
    string Label,
    decimal Score,
    decimal MaxScore,
    bool Hit,
    string Evidence);

/// <summary>
/// 首页单个指标卡片。
/// </summary>
public sealed record DashboardMetricResponse
{
    /// <summary>
    /// 初始化指标卡片。
    /// </summary>
    public DashboardMetricResponse(string key, string label, string value, string tone)
    {
        Key = key;
        Label = label;
        Value = value;
        Tone = tone;
    }

    /// <summary>指标键。</summary>
    public string Key { get; init; }
    /// <summary>展示标签。</summary>
    public string Label { get; init; }
    /// <summary>展示值。</summary>
    public string Value { get; init; }
    /// <summary>语义色调。</summary>
    public string Tone { get; init; }
}

/// <summary>
/// 通用分页响应。
/// </summary>
public sealed record PagedResponse<TItem>
{
    /// <summary>
    /// 初始化分页响应。
    /// </summary>
    public PagedResponse(IReadOnlyList<TItem> items, int page, int pageSize, int totalCount)
    {
        Items = items;
        Page = page;
        PageSize = pageSize;
        TotalCount = totalCount;
    }

    /// <summary>当前页数据。</summary>
    public IReadOnlyList<TItem> Items { get; init; }
    /// <summary>当前页码，从 1 开始。</summary>
    public int Page { get; init; }
    /// <summary>每页条数。</summary>
    public int PageSize { get; init; }
    /// <summary>总条数。</summary>
    public int TotalCount { get; init; }
}

/// <summary>
/// 候选股列表查询条件。
/// </summary>
public sealed record CandidateListQuery
{
    /// <summary>交易日，为空时取最新。</summary>
    public DateOnly? Date { get; init; }
    /// <summary>快照版本，为空时默认正式版。</summary>
    public string? SnapshotVersion { get; init; }
    /// <summary>搜索关键字，匹配代码、名称和行业。</summary>
    public string? Search { get; init; }
    /// <summary>最低分数过滤。</summary>
    public decimal? MinScore { get; init; }
    /// <summary>是否只返回可执行候选股。</summary>
    public bool? OnlyTradable { get; init; }
    /// <summary>排序字段，支持 score、rr、close。</summary>
    public string? SortBy { get; init; }
    /// <summary>页码，从 1 开始。</summary>
    public int Page { get; init; } = 1;
    /// <summary>每页条数。</summary>
    public int PageSize { get; init; } = 10;
}

public sealed record IndustryListQuery
{
    public DateOnly? Date { get; init; }
    public string? SnapshotVersion { get; init; }
    public string? Search { get; init; }
    public string? SortBy { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 10;
}

/// <summary>
/// 财务列表查询条件。
/// </summary>
public sealed record FinancialListQuery
{
    /// <summary>搜索关键字，匹配代码、名称和行业。</summary>
    public string? Search { get; init; }
    /// <summary>排序字段，支持 roe、revenue、profit、marketCap。</summary>
    public string? SortBy { get; init; }
    /// <summary>最低 ROE 过滤。</summary>
    public decimal? MinRoe { get; init; }
    /// <summary>是否只保留营收和净利同比都为正的标的。</summary>
    public bool? PositiveGrowthOnly { get; init; }
    /// <summary>页码，从 1 开始。</summary>
    public int Page { get; init; } = 1;
    /// <summary>每页条数。</summary>
    public int PageSize { get; init; } = 10;
}

/// <summary>
/// 信号列表查询条件。
/// </summary>
public sealed record SignalListQuery
{
    /// <summary>交易日，为空时取最新。</summary>
    public DateOnly? Date { get; init; }
    /// <summary>快照版本，为空时默认正式版。</summary>
    public string? SnapshotVersion { get; init; }
    /// <summary>搜索关键字，匹配代码、名称和行业。</summary>
    public string? Search { get; init; }
    /// <summary>排序字段，支持 score、rr、capital。</summary>
    public string? SortBy { get; init; }
    /// <summary>页码，从 1 开始。</summary>
    public int Page { get; init; } = 1;
    /// <summary>每页条数。</summary>
    public int PageSize { get; init; } = 10;
}

/// <summary>
/// 候选股列表项响应。
/// </summary>
public sealed record CandidateListItemResponse
{
    /// <summary>
    /// 初始化候选股列表项响应。
    /// </summary>
    public CandidateListItemResponse(
        string stockCode,
        string stockName,
        string? industryName,
        string grade,
        string strategyType,
        bool isTradable,
        string eligibilityStatus,
        IReadOnlyList<string> eligibilityReasons,
        decimal totalScore,
        CandidateScoreBreakdown scoreBreakdown,
        decimal close,
        decimal ma20,
        decimal ma60,
        decimal atr14,
        decimal relativeStrengthScore,
        decimal stopLossPrice,
        decimal targetPrice,
        decimal riskRewardRatio,
        string explanation,
        IReadOnlyList<ScoreRuleDetailResponse>? scoreDetails = null)
    {
        StockCode = stockCode;
        StockName = stockName;
        IndustryName = industryName;
        Grade = grade;
        StrategyType = strategyType;
        IsTradable = isTradable;
        EligibilityStatus = eligibilityStatus;
        EligibilityReasons = eligibilityReasons;
        TotalScore = totalScore;
        ScoreBreakdown = scoreBreakdown;
        Close = close;
        Ma20 = ma20;
        Ma60 = ma60;
        Atr14 = atr14;
        RelativeStrengthScore = relativeStrengthScore;
        StopLossPrice = stopLossPrice;
        TargetPrice = targetPrice;
        RiskRewardRatio = riskRewardRatio;
        Explanation = explanation;
        ScoreDetails = scoreDetails ?? [];
    }

    /// <summary>股票代码。</summary>
    public string StockCode { get; init; }
    /// <summary>股票名称。</summary>
    public string StockName { get; init; }
    /// <summary>行业名称。</summary>
    public string? IndustryName { get; init; }
    /// <summary>评级。</summary>
    public string Grade { get; init; }
    /// <summary>策略类型。</summary>
    public string StrategyType { get; init; }
    /// <summary>是否可执行。</summary>
    public bool IsTradable { get; init; }
    /// <summary>准入状态。</summary>
    public string EligibilityStatus { get; init; }
    /// <summary>准入原因。</summary>
    public IReadOnlyList<string> EligibilityReasons { get; init; }
    /// <summary>综合得分。</summary>
    public decimal TotalScore { get; init; }
    /// <summary>评分拆解。</summary>
    public CandidateScoreBreakdown ScoreBreakdown { get; init; }
    /// <summary>收盘价。</summary>
    public decimal Close { get; init; }
    /// <summary>20 日均线。</summary>
    public decimal Ma20 { get; init; }
    /// <summary>60 日均线。</summary>
    public decimal Ma60 { get; init; }
    /// <summary>14 日 ATR。</summary>
    public decimal Atr14 { get; init; }
    /// <summary>相对强弱得分。</summary>
    public decimal RelativeStrengthScore { get; init; }
    /// <summary>止损价。</summary>
    public decimal StopLossPrice { get; init; }
    /// <summary>目标价。</summary>
    public decimal TargetPrice { get; init; }
    /// <summary>风险收益比。</summary>
    public decimal RiskRewardRatio { get; init; }
    /// <summary>解释文本。</summary>
    public string Explanation { get; init; }
    public IReadOnlyList<ScoreRuleDetailResponse> ScoreDetails { get; init; }
}

/// <summary>
/// 交易信号列表项响应。
/// </summary>
public sealed record SignalListItemResponse
{
    /// <summary>
    /// 初始化交易信号列表项响应。
    /// </summary>
    public SignalListItemResponse(
        string stockCode,
        string stockName,
        string? industryName,
        string strategyType,
        string eligibilityStatus,
        IReadOnlyList<string> eligibilityReasons,
        decimal totalScore,
        CandidateScoreBreakdown scoreBreakdown,
        decimal triggerPrice,
        decimal stopLossPrice,
        decimal targetPrice,
        decimal riskRewardRatio,
        decimal suggestedCapital,
        int estimatedShares,
        string explanation,
        DateTime generatedAtUtc)
    {
        StockCode = stockCode;
        StockName = stockName;
        IndustryName = industryName;
        StrategyType = strategyType;
        EligibilityStatus = eligibilityStatus;
        EligibilityReasons = eligibilityReasons;
        TotalScore = totalScore;
        ScoreBreakdown = scoreBreakdown;
        TriggerPrice = triggerPrice;
        StopLossPrice = stopLossPrice;
        TargetPrice = targetPrice;
        RiskRewardRatio = riskRewardRatio;
        SuggestedCapital = suggestedCapital;
        EstimatedShares = estimatedShares;
        Explanation = explanation;
        GeneratedAtUtc = generatedAtUtc;
    }

    /// <summary>股票代码。</summary>
    public string StockCode { get; init; }
    /// <summary>股票名称。</summary>
    public string StockName { get; init; }
    /// <summary>行业名称。</summary>
    public string? IndustryName { get; init; }
    /// <summary>策略类型。</summary>
    public string StrategyType { get; init; }
    /// <summary>准入状态。</summary>
    public string EligibilityStatus { get; init; }
    /// <summary>准入原因。</summary>
    public IReadOnlyList<string> EligibilityReasons { get; init; }
    /// <summary>综合得分。</summary>
    public decimal TotalScore { get; init; }
    /// <summary>评分拆解。</summary>
    public CandidateScoreBreakdown ScoreBreakdown { get; init; }
    /// <summary>触发价。</summary>
    public decimal TriggerPrice { get; init; }
    /// <summary>止损价。</summary>
    public decimal StopLossPrice { get; init; }
    /// <summary>目标价。</summary>
    public decimal TargetPrice { get; init; }
    /// <summary>风险收益比。</summary>
    public decimal RiskRewardRatio { get; init; }
    /// <summary>建议投入资金。</summary>
    public decimal SuggestedCapital { get; init; }
    /// <summary>预估股数。</summary>
    public int EstimatedShares { get; init; }
    /// <summary>解释文本。</summary>
    public string Explanation { get; init; }
    /// <summary>UTC 生成时间。</summary>
    public DateTime GeneratedAtUtc { get; init; }
}

/// <summary>
/// 个股详情响应。
/// </summary>
public sealed record IndustryListItemResponse(
    string IndustryCode,
    string IndustryName,
    DateOnly TradeDate,
    decimal PctChange20d,
    int Rank20d,
    int CandidateCount,
    int SignalCount,
    decimal? TopCandidateScore,
    decimal? TopSignalScore);

/// <summary>
/// 财务列表项响应。
/// </summary>
public sealed record FinancialListItemResponse(
    string StockCode,
    string StockName,
    string? IndustryName,
    DateOnly ReportDate,
    decimal? Pe,
    decimal? Pb,
    decimal? Roe,
    decimal? RevenueYoy,
    decimal? NetProfitYoy,
    decimal? FreeFloatMarketCap);

public sealed record StockDetailResponse
{
    /// <summary>
    /// 初始化个股详情响应。
    /// </summary>
    public StockDetailResponse(
        string stockCode,
        string stockName,
        string? industryName,
        DateOnly tradeDate,
        string snapshotVersion,
        string snapshotVersionName,
        PriceSeriesResponse latestBar,
        FinancialSummaryResponse? financial,
        IndicatorSummaryResponse? indicator,
        CandidateListItemResponse? candidate,
        SignalListItemResponse? signal,
        IReadOnlyList<PriceSeriesResponse> recentBars)
    {
        StockCode = stockCode;
        StockName = stockName;
        IndustryName = industryName;
        TradeDate = tradeDate;
        SnapshotVersion = snapshotVersion;
        SnapshotVersionName = snapshotVersionName;
        LatestBar = latestBar;
        Financial = financial;
        Indicator = indicator;
        Candidate = candidate;
        Signal = signal;
        RecentBars = recentBars;
    }

    /// <summary>股票代码。</summary>
    public string StockCode { get; init; }
    /// <summary>股票名称。</summary>
    public string StockName { get; init; }
    /// <summary>行业名称。</summary>
    public string? IndustryName { get; init; }
    /// <summary>交易日。</summary>
    public DateOnly TradeDate { get; init; }
    /// <summary>当前详情使用的快照版本值。</summary>
    public string SnapshotVersion { get; init; }
    /// <summary>当前详情使用的快照版本中文名。</summary>
    public string SnapshotVersionName { get; init; }
    /// <summary>最新 K 线摘要。</summary>
    public PriceSeriesResponse LatestBar { get; init; }
    /// <summary>财务摘要。</summary>
    public FinancialSummaryResponse? Financial { get; init; }
    /// <summary>指标摘要。</summary>
    public IndicatorSummaryResponse? Indicator { get; init; }
    /// <summary>候选股结果。</summary>
    public CandidateListItemResponse? Candidate { get; init; }
    /// <summary>交易信号结果。</summary>
    public SignalListItemResponse? Signal { get; init; }
    /// <summary>近期价格序列。</summary>
    public IReadOnlyList<PriceSeriesResponse> RecentBars { get; init; }
}

/// <summary>
/// K 线序列项响应。
/// </summary>
public sealed record PriceSeriesResponse
{
    /// <summary>
    /// 初始化价格序列项。
    /// </summary>
    public PriceSeriesResponse(
        DateOnly tradeDate,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        long volume,
        decimal amount,
        decimal? ma20,
        decimal? ma60,
        decimal? ma120)
    {
        TradeDate = tradeDate;
        Open = open;
        High = high;
        Low = low;
        Close = close;
        Volume = volume;
        Amount = amount;
        Ma20 = ma20;
        Ma60 = ma60;
        Ma120 = ma120;
    }

    /// <summary>交易日。</summary>
    public DateOnly TradeDate { get; init; }
    /// <summary>开盘价。</summary>
    public decimal Open { get; init; }
    /// <summary>最高价。</summary>
    public decimal High { get; init; }
    /// <summary>最低价。</summary>
    public decimal Low { get; init; }
    /// <summary>收盘价。</summary>
    public decimal Close { get; init; }
    /// <summary>成交量。</summary>
    public long Volume { get; init; }
    /// <summary>成交额。</summary>
    public decimal Amount { get; init; }
    /// <summary>20 日均线。</summary>
    public decimal? Ma20 { get; init; }
    /// <summary>60 日均线。</summary>
    public decimal? Ma60 { get; init; }
    /// <summary>120 日均线。</summary>
    public decimal? Ma120 { get; init; }
}

/// <summary>
/// 财务摘要响应。
/// </summary>
public sealed record FinancialSummaryResponse
{
    /// <summary>
    /// 初始化财务摘要。
    /// </summary>
    public FinancialSummaryResponse(
        DateOnly reportDate,
        decimal? pe,
        decimal? pb,
        decimal? roe,
        decimal? revenueYoy,
        decimal? netProfitYoy)
    {
        ReportDate = reportDate;
        Pe = pe;
        Pb = pb;
        Roe = roe;
        RevenueYoy = revenueYoy;
        NetProfitYoy = netProfitYoy;
    }

    /// <summary>财报日期。</summary>
    public DateOnly ReportDate { get; init; }
    /// <summary>市盈率。</summary>
    public decimal? Pe { get; init; }
    /// <summary>市净率。</summary>
    public decimal? Pb { get; init; }
    /// <summary>净资产收益率。</summary>
    public decimal? Roe { get; init; }
    /// <summary>营收同比。</summary>
    public decimal? RevenueYoy { get; init; }
    /// <summary>净利润同比。</summary>
    public decimal? NetProfitYoy { get; init; }
}

/// <summary>
/// 技术指标摘要响应。
/// </summary>
public sealed record IndicatorSummaryResponse
{
    /// <summary>
    /// 初始化指标摘要。
    /// </summary>
    public IndicatorSummaryResponse(
        decimal close,
        decimal ma20,
        decimal ma60,
        decimal ma120,
        decimal atr14,
        decimal return20d,
        decimal return60d,
        decimal relativeStrengthScore,
        bool is20DayBreakout,
        bool isMa20Upward,
        bool isBullishStacked,
        decimal distanceToMa20Pct)
    {
        Close = close;
        Ma20 = ma20;
        Ma60 = ma60;
        Ma120 = ma120;
        Atr14 = atr14;
        Return20d = return20d;
        Return60d = return60d;
        RelativeStrengthScore = relativeStrengthScore;
        Is20DayBreakout = is20DayBreakout;
        IsMa20Upward = isMa20Upward;
        IsBullishStacked = isBullishStacked;
        DistanceToMa20Pct = distanceToMa20Pct;
    }

    /// <summary>收盘价。</summary>
    public decimal Close { get; init; }
    /// <summary>20 日均线。</summary>
    public decimal Ma20 { get; init; }
    /// <summary>60 日均线。</summary>
    public decimal Ma60 { get; init; }
    /// <summary>120 日均线。</summary>
    public decimal Ma120 { get; init; }
    /// <summary>14 日 ATR。</summary>
    public decimal Atr14 { get; init; }
    /// <summary>20 日收益率。</summary>
    public decimal Return20d { get; init; }
    /// <summary>60 日收益率。</summary>
    public decimal Return60d { get; init; }
    /// <summary>相对强弱得分。</summary>
    public decimal RelativeStrengthScore { get; init; }
    /// <summary>是否突破 20 日收盘高点。</summary>
    public bool Is20DayBreakout { get; init; }
    /// <summary>20 日均线是否上行。</summary>
    public bool IsMa20Upward { get; init; }
    /// <summary>是否多头排列。</summary>
    public bool IsBullishStacked { get; init; }
    /// <summary>相对 20 日均线偏离百分比。</summary>
    public decimal DistanceToMa20Pct { get; init; }
}

/// <summary>
/// 单次导入快照结果。
/// </summary>
public sealed record ImportSnapshotResult
{
    /// <summary>
    /// 初始化导入结果。
    /// </summary>
    public ImportSnapshotResult(
        DateOnly tradeDate,
        int stockProfiles,
        int dailyBars,
        int indexBars,
        int industryStats,
        int financialSnapshots)
    {
        TradeDate = tradeDate;
        StockProfiles = stockProfiles;
        DailyBars = dailyBars;
        IndexBars = indexBars;
        IndustryStats = industryStats;
        FinancialSnapshots = financialSnapshots;
    }

    /// <summary>交易日。</summary>
    public DateOnly TradeDate { get; init; }
    /// <summary>股票画像数量。</summary>
    public int StockProfiles { get; init; }
    /// <summary>日线数量。</summary>
    public int DailyBars { get; init; }
    /// <summary>指数数量。</summary>
    public int IndexBars { get; init; }
    /// <summary>行业统计数量。</summary>
    public int IndustryStats { get; init; }
    /// <summary>财务快照数量。</summary>
    public int FinancialSnapshots { get; init; }
}

/// <summary>
/// 领域快照同步前的状态摘要。
/// </summary>
public sealed record MarketSnapshotSyncState(
    DateOnly? EffectiveTradeDate,
    DateOnly? LatestRawTradeDate,
    DateOnly? LatestImportedTradeDate,
    DateOnly? LatestRawFinancialReportDate,
    DateOnly? LatestImportedFinancialReportDate,
    bool RequiresSync,
    bool HasTradeDateGap,
    bool HasFinancialGap);

/// <summary>
/// 原始市场数据读取仓储。
/// </summary>
public interface IRawMarketDataRepository
{
    /// <summary>
    /// 读取原始日线中的最新交易日。
    /// </summary>
    Task<DateOnly?> GetLatestTradeDateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 读取指定交易日可用的股票画像。
    /// </summary>
    Task<IReadOnlyList<StockProfile>> GetLatestStockProfilesAsync(DateOnly tradeDate, CancellationToken cancellationToken);

    /// <summary>
    /// 读取指定交易日的股票日线。
    /// </summary>
    Task<IReadOnlyList<DailyBar>> GetDailyBarsByTradeDateAsync(DateOnly tradeDate, CancellationToken cancellationToken);

    /// <summary>
    /// 读取指定交易日的指数日线。
    /// </summary>
    Task<IReadOnlyList<MarketIndexBar>> GetIndexBarsByTradeDateAsync(DateOnly tradeDate, CancellationToken cancellationToken);

    /// <summary>
    /// 读取指定交易日的行业统计。
    /// </summary>
    Task<IReadOnlyList<IndustryDailyStat>> GetIndustryStatsByTradeDateAsync(DateOnly tradeDate, CancellationToken cancellationToken);

    /// <summary>
    /// 读取每只股票最近一期财务快照。
    /// </summary>
    Task<IReadOnlyList<FinancialSnapshot>> GetLatestFinancialSnapshotsAsync(CancellationToken cancellationToken);
    Task<DateOnly?> GetLatestFinancialReportDateAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 市场快照与策略快照仓储。
/// </summary>
public interface IMarketDataRepository
{
    /// <summary>
    /// 读取已导入市场快照的最新交易日。
    /// </summary>
    Task<DateOnly?> GetLatestImportedTradeDateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 用指定交易日的全量数据替换领域层市场快照。
    /// </summary>
    Task ReplaceMarketSnapshotAsync(
        DateOnly tradeDate,
        IReadOnlyList<StockProfile> stocks,
        IReadOnlyList<DailyBar> dailyBars,
        IReadOnlyList<MarketIndexBar> indexBars,
        IReadOnlyList<IndustryDailyStat> industries,
        IReadOnlyList<FinancialSnapshot> financials,
        CancellationToken cancellationToken);

    /// <summary>读取活跃股票代码列表。</summary>
    Task<IReadOnlyList<string>> GetActiveStockCodesAsync(CancellationToken cancellationToken);
    /// <summary>读取单只股票历史日线。</summary>
    Task<IReadOnlyList<DailyBar>> GetDailyBarHistoryAsync(string stockCode, DateOnly tradeDate, int maxRows, CancellationToken cancellationToken);
    /// <summary>读取单只股票指定交易日后的前瞻日线。</summary>
    Task<IReadOnlyList<DailyBar>> GetForwardDailyBarsAsync(string stockCode, DateOnly tradeDate, int maxRows, CancellationToken cancellationToken);
    /// <summary>读取指数历史日线。</summary>
    Task<IReadOnlyList<MarketIndexBar>> GetIndexBarHistoryAsync(DateOnly tradeDate, int maxRows, CancellationToken cancellationToken);
    /// <summary>读取最近若干个交易日。</summary>
    Task<IReadOnlyList<DateOnly>> GetRecentTradeDatesAsync(int maxRows, CancellationToken cancellationToken);
    /// <summary>按代码批量读取股票画像。</summary>
    Task<IReadOnlyDictionary<string, StockProfile>> GetStockProfilesByCodesAsync(IEnumerable<string> stockCodes, CancellationToken cancellationToken);
    /// <summary>按代码批量读取最新财务快照。</summary>
    Task<IReadOnlyDictionary<string, FinancialSnapshot>> GetLatestFinancialsByCodesAsync(IEnumerable<string> stockCodes, CancellationToken cancellationToken);
    /// <summary>读取全部最新财务快照。</summary>
    Task<IReadOnlyList<FinancialSnapshot>> GetLatestFinancialSnapshotsAsync(CancellationToken cancellationToken);
    Task<DateOnly?> GetLatestImportedFinancialReportDateAsync(CancellationToken cancellationToken);
    /// <summary>读取指定交易日的行业快照列表。</summary>
    Task<IReadOnlyList<IndustryDailyStat>> GetIndustryStatsAsync(DateOnly tradeDate, CancellationToken cancellationToken);
    /// <summary>按行业名批量读取行业统计。</summary>
    Task<IReadOnlyDictionary<string, IndustryDailyStat>> GetIndustryStatsByNamesAsync(DateOnly tradeDate, IEnumerable<string?> industryNames, CancellationToken cancellationToken);
    /// <summary>写入指标快照。</summary>
    Task UpsertIndicatorSnapshotsAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, IReadOnlyList<IndicatorSnapshot> indicators, CancellationToken cancellationToken);
    /// <summary>写入市场环境快照。</summary>
    Task UpsertMarketRegimeAsync(StrategySnapshotVersion snapshotVersion, MarketRegimeSnapshot regime, CancellationToken cancellationToken);
    /// <summary>写入候选股结果。</summary>
    Task UpsertCandidatesAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, IReadOnlyList<CandidateStock> candidates, CancellationToken cancellationToken);
    /// <summary>写入交易信号结果。</summary>
    Task UpsertSignalsAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, IReadOnlyList<TradeSignal> signals, CancellationToken cancellationToken);
    /// <summary>读取指定交易日指标快照。</summary>
    Task<IReadOnlyList<IndicatorSnapshot>> GetIndicatorSnapshotsAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken);
    /// <summary>读取指定交易日市场环境。</summary>
    Task<MarketRegimeSnapshot?> GetMarketRegimeAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken);
    /// <summary>读取指定交易日候选股。</summary>
    Task<IReadOnlyList<CandidateStock>> GetCandidatesAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken);
    /// <summary>读取指定交易日交易信号。</summary>
    Task<IReadOnlyList<TradeSignal>> GetSignalsAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken);
    /// <summary>读取单只股票画像。</summary>
    Task<StockProfile?> GetStockProfileAsync(string stockCode, CancellationToken cancellationToken);
    /// <summary>读取单只股票最近财务快照。</summary>
    Task<FinancialSnapshot?> GetLatestFinancialAsync(string stockCode, CancellationToken cancellationToken);
    /// <summary>读取单只股票指标快照。</summary>
    Task<IndicatorSnapshot?> GetIndicatorSnapshotAsync(string stockCode, DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken);
    /// <summary>读取单只股票候选结果。</summary>
    Task<CandidateStock?> GetCandidateAsync(string stockCode, DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken);
    /// <summary>读取单只股票信号结果。</summary>
    Task<TradeSignal?> GetSignalAsync(string stockCode, DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken);
    /// <summary>读取导入层近期日线。</summary>
    Task<IReadOnlyList<DailyBar>> GetRecentImportedDailyBarsAsync(string stockCode, DateOnly tradeDate, int maxRows, CancellationToken cancellationToken);
}

/// <summary>
/// 数据采集日志仓储。
/// </summary>
public interface IIngestionLogRepository
{
    /// <summary>
    /// 读取最近一次成功采集时间。
    /// </summary>
    Task<DateTime?> GetLatestSuccessfulIngestionAtUtcAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 读取最近的采集日志记录。
    /// </summary>
    Task<IReadOnlyList<IngestionLogEntry>> GetRecentRunsAsync(int take, CancellationToken cancellationToken);
}

/// <summary>
/// 领域同步运行日志仓储。
/// </summary>
public interface IDomainSyncRunRepository
{
    Task AddRunAsync(DomainSyncRunEntry entry, CancellationToken cancellationToken);
    Task<IReadOnlyList<DomainSyncRunEntry>> GetRecentRunsAsync(int take, CancellationToken cancellationToken);
    Task<DateTime?> GetLatestSuccessfulFinishedAtUtcAsync(CancellationToken cancellationToken);
    Task<DateTime?> GetLatestSuccessfulFinishedAtUtcAsync(StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken);
}

/// <summary>
/// 单条采集日志记录。
/// </summary>
public sealed record IngestionLogEntry(
    string TargetScope,
    bool IsComplete,
    bool IsSignalEligible,
    DateTime CreatedAtUtc);

/// <summary>
/// 单次领域同步运行记录。
/// </summary>
public sealed record DomainSyncRunEntry(
    string JobName,
    string TriggerKind,
    string SnapshotVersion,
    string Status,
    bool DataUpdated,
    bool IsSignalEligible,
    DateOnly? EffectiveTradeDate,
    DateOnly? FinancialReportDate,
    DateTime StartedAtUtc,
    DateTime? FinishedAtUtc,
    string Summary);

/// <summary>
/// 任务中心单条运行记录响应。
/// </summary>
public sealed record TaskRunItemResponse(
    string TargetScope,
    bool IsComplete,
    bool IsSignalEligible,
    DateTime CreatedAtUtc);

/// <summary>
/// 任务中心中的领域同步运行项。
/// </summary>
public sealed record DomainSyncRunItemResponse(
    string JobName,
    string TriggerKind,
    string SnapshotVersion,
    string Status,
    bool DataUpdated,
    bool IsSignalEligible,
    DateOnly? EffectiveTradeDate,
    DateOnly? FinancialReportDate,
    DateTime StartedAtUtc,
    DateTime? FinishedAtUtc,
    string Summary);

/// <summary>
/// 任务中心中的固定调度项。
/// </summary>
public sealed record TaskScheduleItemResponse(
    string Name,
    string Schedule,
    string Description);

/// <summary>
/// 任务中心中的领域同步状态摘要。
/// </summary>
public sealed record DomainSyncStatusResponse(
    DateOnly? LatestRawTradeDate,
    DateOnly? LatestImportedTradeDate,
    DateOnly? LatestRawFinancialReportDate,
    DateOnly? LatestImportedFinancialReportDate,
    DateTime? LatestSuccessfulDomainSyncAtUtc,
    DateTime? LatestSuccessfulEndOfDayFinalAtUtc,
    bool RequiresSync,
    bool HasTradeDateGap,
    bool HasFinancialGap);

/// <summary>
/// 任务中心概览响应。
/// </summary>
public sealed record TaskCenterOverviewResponse(
    DateOnly? TradeDate,
    string SnapshotVersion,
    string SnapshotVersionName,
    DateTime? LatestSuccessfulIngestionAtUtc,
    DomainSyncStatusResponse DomainSyncStatus,
    int CandidateCount,
    int SignalCount,
    string MarketRegime,
    bool IsSignalEligible,
    IReadOnlyList<TaskScheduleItemResponse> Schedules,
    IReadOnlyList<string> StatusMessages,
    IReadOnlyList<TaskRunItemResponse> CollectorRuns,
    IReadOnlyList<DomainSyncRunItemResponse> DomainSyncRuns);

/// <summary>
/// 策略解释页响应。
/// </summary>
public sealed record StrategyExplanationResponse(
    string StrategyVersion,
    IReadOnlyList<StrategyRuleSectionResponse> Sections,
    IReadOnlyList<StrategyScoreDimensionResponse> ScoreDimensions,
    IReadOnlyList<StrategyExecutionRuleResponse> ExecutionRules);

/// <summary>
/// 策略解释页章节。
/// </summary>
public sealed record StrategyRuleSectionResponse(
    string Title,
    IReadOnlyList<string> Items);

/// <summary>
/// 评分维度说明。
/// </summary>
public sealed record StrategyScoreDimensionResponse(
    string Name,
    decimal MaxScore,
    IReadOnlyList<string> Rules);

/// <summary>
/// 执行规则说明。
/// </summary>
public sealed record StrategyExecutionRuleResponse(
    string Name,
    string Description);

/// <summary>
/// 回测概览响应。
/// </summary>
public sealed record BacktestOverviewResponse(
    string StrategyVersion,
    int SampleTradeCount,
    decimal WinRatePct,
    decimal AverageReturnPct,
    decimal AverageMaxGainPct,
    decimal AverageMaxDrawdownPct,
    decimal BenchmarkReturnPct,
    decimal DataCoveragePct,
    int SkippedTradeDays,
    decimal AnnualTradeCount,
    int MaxConsecutiveLosses,
    bool IsApproved,
    IReadOnlyList<string> FailureReasons,
    IReadOnlyList<BacktestTradeItemResponse> Trades);

/// <summary>
/// 单笔回测交易结果。
/// </summary>
public sealed record BacktestTradeItemResponse(
    DateOnly TradeDate,
    string StockCode,
    string StockName,
    string StrategyType,
    decimal EntryPrice,
    decimal ExitPrice,
    decimal ReturnPct,
    decimal MaxGainPct,
    decimal MaxDrawdownPct,
    bool HitTarget,
    bool HitStopLoss,
    int Quantity,
    decimal InvestedCapital,
    decimal ProfitAmount,
    int MaxHoldingDays);

namespace StockDecision.Domain.Strategy;

/// <summary>
/// 表示市场环境对信号执行的许可程度。
/// </summary>
public enum MarketSignalEligibility
{
    /// <summary>
    /// 不允许执行交易。
    /// </summary>
    NoTrade = 0,

    /// <summary>
    /// 仅允许弱机会观察。
    /// </summary>
    WeakOpportunity = 1,

    /// <summary>
    /// 允许常规交易。
    /// </summary>
    Tradable = 2,

    /// <summary>
    /// 市场环境强，可提高执行力度。
    /// </summary>
    Strong = 3
}

/// <summary>
/// 表示候选股综合评级。
/// </summary>
public enum CandidateGrade
{
    /// <summary>
    /// 最低评级。
    /// </summary>
    D = 0,

    /// <summary>
    /// 基础观察评级。
    /// </summary>
    C = 1,

    /// <summary>
    /// 较优评级。
    /// </summary>
    B = 2,

    /// <summary>
    /// 最高评级。
    /// </summary>
    A = 3
}

/// <summary>
/// 表示候选股对应的策略类型。
/// </summary>
public enum StrategyType
{
    /// <summary>
    /// 仅观察突破。
    /// </summary>
    WatchBreakout = 0,

    /// <summary>
    /// 仅观察回踩。
    /// </summary>
    WatchPullback = 1,

    /// <summary>
    /// 突破型可执行策略。
    /// </summary>
    Breakout = 2,

    /// <summary>
    /// 回踩 20 日线可执行策略。
    /// </summary>
    PullbackToMa20 = 3
}

/// <summary>
/// 表示单只股票在某个交易日的技术指标快照。
/// </summary>
public sealed record IndicatorSnapshot
{
    /// <summary>
    /// 初始化指标快照。
    /// </summary>
    public IndicatorSnapshot(
        string stockCode,
        DateOnly tradeDate,
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
        decimal distanceToMa20Pct,
        decimal? turnoverRate)
    {
        StockCode = stockCode;
        TradeDate = tradeDate;
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
        TurnoverRate = turnoverRate;
    }

    /// <summary>
    /// 股票代码。
    /// </summary>
    public string StockCode { get; init; }

    /// <summary>
    /// 交易日。
    /// </summary>
    public DateOnly TradeDate { get; init; }

    /// <summary>
    /// 收盘价。
    /// </summary>
    public decimal Close { get; init; }

    /// <summary>
    /// 20 日均线。
    /// </summary>
    public decimal Ma20 { get; init; }

    /// <summary>
    /// 60 日均线。
    /// </summary>
    public decimal Ma60 { get; init; }

    /// <summary>
    /// 120 日均线。
    /// </summary>
    public decimal Ma120 { get; init; }

    /// <summary>
    /// 14 日 ATR 波动率。
    /// </summary>
    public decimal Atr14 { get; init; }

    /// <summary>
    /// 近 20 日收益率。
    /// </summary>
    public decimal Return20d { get; init; }

    /// <summary>
    /// 近 60 日收益率。
    /// </summary>
    public decimal Return60d { get; init; }

    /// <summary>
    /// 相对强弱得分。
    /// </summary>
    public decimal RelativeStrengthScore { get; init; }

    /// <summary>
    /// 是否创出 20 日收盘突破。
    /// </summary>
    public bool Is20DayBreakout { get; init; }

    /// <summary>
    /// 20 日均线是否继续上行。
    /// </summary>
    public bool IsMa20Upward { get; init; }

    /// <summary>
    /// 是否满足多头均线排列。
    /// </summary>
    public bool IsBullishStacked { get; init; }

    /// <summary>
    /// 收盘价相对 20 日均线的偏离百分比。
    /// </summary>
    public decimal DistanceToMa20Pct { get; init; }

    /// <summary>
    /// 当日换手率。
    /// </summary>
    public decimal? TurnoverRate { get; init; }
}

/// <summary>
/// 表示某个交易日的市场环境判断结果。
/// </summary>
public sealed record MarketRegimeSnapshot
{
    /// <summary>
    /// 初始化市场环境快照。
    /// </summary>
    public MarketRegimeSnapshot(
        DateOnly tradeDate,
        MarketSignalEligibility regime,
        int confirmedIndexCount,
        bool isSignalEligible,
        string summary)
    {
        TradeDate = tradeDate;
        Regime = regime;
        ConfirmedIndexCount = confirmedIndexCount;
        IsSignalEligible = isSignalEligible;
        Summary = summary;
    }

    /// <summary>
    /// 交易日。
    /// </summary>
    public DateOnly TradeDate { get; init; }

    /// <summary>
    /// 市场环境等级。
    /// </summary>
    public MarketSignalEligibility Regime { get; init; }

    /// <summary>
    /// 被确认的指数数量。
    /// </summary>
    public int ConfirmedIndexCount { get; init; }

    /// <summary>
    /// 是否允许生成可执行信号。
    /// </summary>
    public bool IsSignalEligible { get; init; }

    /// <summary>
    /// 文本摘要。
    /// </summary>
    public string Summary { get; init; }
}

/// <summary>
/// 表示单条评分规则的命中结果。
/// </summary>
public sealed record ScoreRuleDetail(
    string Key,
    string Dimension,
    string Label,
    decimal Score,
    decimal MaxScore,
    bool Hit,
    string Evidence);

/// <summary>
/// 表示候选股评分的分项拆解。
/// </summary>
public sealed record CandidateScoreBreakdown
{
    /// <summary>
    /// 初始化评分拆解。
    /// </summary>
    public CandidateScoreBreakdown(
        decimal relativeStrengthScore,
        decimal trendScore,
        decimal volumePriceScore,
        decimal fundamentalScore,
        IReadOnlyList<ScoreRuleDetail>? details = null)
    {
        RelativeStrengthScore = relativeStrengthScore;
        TrendScore = trendScore;
        VolumePriceScore = volumePriceScore;
        FundamentalScore = fundamentalScore;
        Details = details ?? [];
    }

    /// <summary>
    /// 相对强弱维度得分。
    /// </summary>
    public decimal RelativeStrengthScore { get; init; }

    /// <summary>
    /// 趋势维度得分。
    /// </summary>
    public decimal TrendScore { get; init; }

    /// <summary>
    /// 量价维度得分。
    /// </summary>
    public decimal VolumePriceScore { get; init; }

    /// <summary>
    /// 基本面维度得分。
    /// </summary>
    public decimal FundamentalScore { get; init; }

    /// <summary>
    /// 逐条评分明细。
    /// </summary>
    public IReadOnlyList<ScoreRuleDetail> Details { get; init; }

    /// <summary>
    /// 综合总分。
    /// </summary>
    public decimal TotalScore => RelativeStrengthScore + TrendScore + VolumePriceScore + FundamentalScore;
}

/// <summary>
/// 表示可供观察或执行的候选股票。
/// </summary>
public sealed record CandidateStock
{
    /// <summary>
    /// 初始化候选股。
    /// </summary>
    public CandidateStock(
        DateOnly tradeDate,
        string stockCode,
        string stockName,
        string? industryName,
        CandidateGrade grade,
        StrategyType strategyType,
        bool isTradable,
        string eligibilityStatus,
        string eligibilityReason,
        decimal totalScore,
        CandidateScoreBreakdown scoreBreakdown,
        decimal close,
        decimal ma20,
        decimal ma60,
        decimal ma120,
        decimal atr14,
        decimal relativeStrengthScore,
        decimal? pe,
        decimal? pb,
        decimal? roe,
        decimal stopLossPrice,
        decimal targetPrice,
        decimal riskRewardRatio,
        string explanation)
    {
        TradeDate = tradeDate;
        StockCode = stockCode;
        StockName = stockName;
        IndustryName = industryName;
        Grade = grade;
        StrategyType = strategyType;
        IsTradable = isTradable;
        EligibilityStatus = eligibilityStatus;
        EligibilityReason = eligibilityReason;
        TotalScore = totalScore;
        ScoreBreakdown = scoreBreakdown;
        Close = close;
        Ma20 = ma20;
        Ma60 = ma60;
        Ma120 = ma120;
        Atr14 = atr14;
        RelativeStrengthScore = relativeStrengthScore;
        Pe = pe;
        Pb = pb;
        Roe = roe;
        StopLossPrice = stopLossPrice;
        TargetPrice = targetPrice;
        RiskRewardRatio = riskRewardRatio;
        Explanation = explanation;
    }

    /// <summary>
    /// 交易日。
    /// </summary>
    public DateOnly TradeDate { get; init; }

    /// <summary>
    /// 股票代码。
    /// </summary>
    public string StockCode { get; init; }

    /// <summary>
    /// 股票名称。
    /// </summary>
    public string StockName { get; init; }

    /// <summary>
    /// 行业名称。
    /// </summary>
    public string? IndustryName { get; init; }

    /// <summary>
    /// 候选评级。
    /// </summary>
    public CandidateGrade Grade { get; init; }

    /// <summary>
    /// 对应策略类型。
    /// </summary>
    public StrategyType StrategyType { get; init; }

    /// <summary>
    /// 是否达到可执行标准。
    /// </summary>
    public bool IsTradable { get; init; }

    /// <summary>
    /// 准入状态。
    /// </summary>
    public string EligibilityStatus { get; init; }

    /// <summary>
    /// 当前准入状态说明。
    /// </summary>
    public string EligibilityReason { get; init; }

    /// <summary>
    /// 综合得分。
    /// </summary>
    public decimal TotalScore { get; init; }

    /// <summary>
    /// 分项评分拆解。
    /// </summary>
    public CandidateScoreBreakdown ScoreBreakdown { get; init; }

    /// <summary>
    /// 当前收盘价。
    /// </summary>
    public decimal Close { get; init; }

    /// <summary>
    /// 20 日均线。
    /// </summary>
    public decimal Ma20 { get; init; }

    /// <summary>
    /// 60 日均线。
    /// </summary>
    public decimal Ma60 { get; init; }

    /// <summary>
    /// 120 日均线。
    /// </summary>
    public decimal Ma120 { get; init; }

    /// <summary>
    /// 14 日 ATR。
    /// </summary>
    public decimal Atr14 { get; init; }

    /// <summary>
    /// 相对强弱得分。
    /// </summary>
    public decimal RelativeStrengthScore { get; init; }

    /// <summary>
    /// 市盈率。
    /// </summary>
    public decimal? Pe { get; init; }

    /// <summary>
    /// 市净率。
    /// </summary>
    public decimal? Pb { get; init; }

    /// <summary>
    /// 净资产收益率。
    /// </summary>
    public decimal? Roe { get; init; }

    /// <summary>
    /// 止损价。
    /// </summary>
    public decimal StopLossPrice { get; init; }

    /// <summary>
    /// 目标价。
    /// </summary>
    public decimal TargetPrice { get; init; }

    /// <summary>
    /// 风险收益比。
    /// </summary>
    public decimal RiskRewardRatio { get; init; }

    /// <summary>
    /// 解释文本。
    /// </summary>
    public string Explanation { get; init; }
}

/// <summary>
/// 表示最终可执行的交易信号。
/// </summary>
public sealed record TradeSignal
{
    /// <summary>
    /// 初始化交易信号。
    /// </summary>
    public TradeSignal(
        DateOnly tradeDate,
        string stockCode,
        string stockName,
        string? industryName,
        StrategyType strategyType,
        string eligibilityStatus,
        string eligibilityReason,
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
        TradeDate = tradeDate;
        StockCode = stockCode;
        StockName = stockName;
        IndustryName = industryName;
        StrategyType = strategyType;
        EligibilityStatus = eligibilityStatus;
        EligibilityReason = eligibilityReason;
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

    /// <summary>
    /// 交易日。
    /// </summary>
    public DateOnly TradeDate { get; init; }

    /// <summary>
    /// 股票代码。
    /// </summary>
    public string StockCode { get; init; }

    /// <summary>
    /// 股票名称。
    /// </summary>
    public string StockName { get; init; }

    /// <summary>
    /// 行业名称。
    /// </summary>
    public string? IndustryName { get; init; }

    /// <summary>
    /// 信号所属策略类型。
    /// </summary>
    public StrategyType StrategyType { get; init; }

    /// <summary>
    /// 准入状态。
    /// </summary>
    public string EligibilityStatus { get; init; }

    /// <summary>
    /// 当前准入状态说明。
    /// </summary>
    public string EligibilityReason { get; init; }

    /// <summary>
    /// 综合得分。
    /// </summary>
    public decimal TotalScore { get; init; }

    /// <summary>
    /// 分项评分拆解。
    /// </summary>
    public CandidateScoreBreakdown ScoreBreakdown { get; init; }

    /// <summary>
    /// 触发价。
    /// </summary>
    public decimal TriggerPrice { get; init; }

    /// <summary>
    /// 止损价。
    /// </summary>
    public decimal StopLossPrice { get; init; }

    /// <summary>
    /// 目标价。
    /// </summary>
    public decimal TargetPrice { get; init; }

    /// <summary>
    /// 风险收益比。
    /// </summary>
    public decimal RiskRewardRatio { get; init; }

    /// <summary>
    /// 建议投入资金。
    /// </summary>
    public decimal SuggestedCapital { get; init; }

    /// <summary>
    /// 估算可买入股数。
    /// </summary>
    public int EstimatedShares { get; init; }

    /// <summary>
    /// 解释文本。
    /// </summary>
    public string Explanation { get; init; }

    /// <summary>
    /// UTC 生成时间。
    /// </summary>
    public DateTime GeneratedAtUtc { get; init; }
}

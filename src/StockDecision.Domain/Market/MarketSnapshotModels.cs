namespace StockDecision.Domain.Market;

/// <summary>
/// 表示某个交易日可供策略使用的股票静态画像。
/// </summary>
public sealed record StockProfile
{
    /// <summary>
    /// 初始化股票画像。
    /// </summary>
    public StockProfile(
        string stockCode,
        string stockName,
        string? industryName,
        bool isActive,
        bool isSt,
        bool isDelistingRisk,
        DateOnly? listDate,
        decimal? latestPrice,
        decimal? pe,
        decimal? pb,
        decimal? freeFloatMarketCap,
        decimal? turnoverRate,
        decimal? averageAmount20d,
        DateOnly snapshotDate,
        string? scoringIndustryName = null)
    {
        StockCode = stockCode;
        StockName = stockName;
        IndustryName = industryName;
        IsActive = isActive;
        IsSt = isSt;
        IsDelistingRisk = isDelistingRisk;
        ListDate = listDate;
        LatestPrice = latestPrice;
        Pe = pe;
        Pb = pb;
        FreeFloatMarketCap = freeFloatMarketCap;
        TurnoverRate = turnoverRate;
        AverageAmount20d = averageAmount20d;
        SnapshotDate = snapshotDate;
        ScoringIndustryName = string.IsNullOrWhiteSpace(scoringIndustryName) ? null : scoringIndustryName.Trim();
    }

    /// <summary>
    /// 股票代码。
    /// </summary>
    public string StockCode { get; init; }

    /// <summary>
    /// 股票名称。
    /// </summary>
    public string StockName { get; init; }

    /// <summary>
    /// 所属行业名称。
    /// </summary>
    public string? IndustryName { get; init; }

    /// <summary>
    /// 用于匹配行业强度和行业资金流的评分行业名称。
    /// </summary>
    public string? ScoringIndustryName { get; init; }

    /// <summary>
    /// 优先使用评分行业；未能映射时回退到展示行业。
    /// </summary>
    public string? EffectiveScoringIndustryName => string.IsNullOrWhiteSpace(ScoringIndustryName) ? IndustryName : ScoringIndustryName;

    /// <summary>
    /// 是否处于可交易状态。
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// 是否为 ST 股票。
    /// </summary>
    public bool IsSt { get; init; }

    /// <summary>
    /// 是否存在退市风险。
    /// </summary>
    public bool IsDelistingRisk { get; init; }

    /// <summary>
    /// 上市日期。
    /// </summary>
    public DateOnly? ListDate { get; init; }

    /// <summary>
    /// 快照交易日对应的最新价格。
    /// </summary>
    public decimal? LatestPrice { get; init; }

    /// <summary>
    /// 市盈率。
    /// </summary>
    public decimal? Pe { get; init; }

    /// <summary>
    /// 市净率。
    /// </summary>
    public decimal? Pb { get; init; }

    /// <summary>
    /// 流通市值。
    /// </summary>
    public decimal? FreeFloatMarketCap { get; init; }

    /// <summary>
    /// 换手率。
    /// </summary>
    public decimal? TurnoverRate { get; init; }

    /// <summary>
    /// 近 20 日平均成交额。
    /// </summary>
    public decimal? AverageAmount20d { get; init; }

    /// <summary>
    /// 画像快照对应的交易日。
    /// </summary>
    public DateOnly SnapshotDate { get; init; }
}

/// <summary>
/// 表示股票单日 K 线数据。
/// </summary>
public sealed record DailyBar
{
    /// <summary>
    /// 初始化单日 K 线。
    /// </summary>
    public DailyBar(
        string stockCode,
        DateOnly tradeDate,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        long volume,
        decimal amount,
        decimal? pctChange,
        decimal? turnoverRate)
    {
        StockCode = stockCode;
        TradeDate = tradeDate;
        Open = open;
        High = high;
        Low = low;
        Close = close;
        Volume = volume;
        Amount = amount;
        PctChange = pctChange;
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
    /// 开盘价。
    /// </summary>
    public decimal Open { get; init; }

    /// <summary>
    /// 最高价。
    /// </summary>
    public decimal High { get; init; }

    /// <summary>
    /// 最低价。
    /// </summary>
    public decimal Low { get; init; }

    /// <summary>
    /// 收盘价。
    /// </summary>
    public decimal Close { get; init; }

    /// <summary>
    /// 成交量。
    /// </summary>
    public long Volume { get; init; }

    /// <summary>
    /// 成交额。
    /// </summary>
    public decimal Amount { get; init; }

    /// <summary>
    /// 涨跌幅百分比。
    /// </summary>
    public decimal? PctChange { get; init; }

    /// <summary>
    /// 换手率。
    /// </summary>
    public decimal? TurnoverRate { get; init; }
}

/// <summary>
/// 表示列表打分所需的轻量历史量价指标。
/// </summary>
public sealed record StockScoringHistoryMetrics(
    string StockCode,
    decimal Return10d,
    decimal AmountRatio1d,
    decimal Ma60Previous);

/// <summary>
/// 表示生成策略指标所需的单股历史聚合结果。
/// </summary>
public sealed record IndicatorCalculationMetrics(
    string StockCode,
    decimal Close,
    decimal Ma20,
    decimal Ma60,
    decimal Ma120,
    decimal Atr14,
    decimal Return20d,
    decimal Return60d,
    decimal Return10d,
    decimal AmountRatio1d,
    decimal PreviousMa20,
    decimal Ma60Previous,
    decimal BreakoutClose,
    decimal? TurnoverRate);

/// <summary>
/// 表示市场指数的单日收盘快照。
/// </summary>
public sealed record MarketIndexBar
{
    /// <summary>
    /// 初始化指数日线。
    /// </summary>
    public MarketIndexBar(string indexCode, string indexName, DateOnly tradeDate, decimal close)
    {
        IndexCode = indexCode;
        IndexName = indexName;
        TradeDate = tradeDate;
        Close = close;
    }

    /// <summary>
    /// 指数代码。
    /// </summary>
    public string IndexCode { get; init; }

    /// <summary>
    /// 指数名称。
    /// </summary>
    public string IndexName { get; init; }

    /// <summary>
    /// 交易日。
    /// </summary>
    public DateOnly TradeDate { get; init; }

    /// <summary>
    /// 收盘点位。
    /// </summary>
    public decimal Close { get; init; }
}

/// <summary>
/// 表示行业层面的日度强弱统计。
/// </summary>
public sealed record IndustryDailyStat
{
    /// <summary>
    /// 初始化行业统计。
    /// </summary>
    public IndustryDailyStat(
        string industryCode,
        string industryName,
        DateOnly tradeDate,
        decimal? pctChange20d,
        int? rank20d)
    {
        IndustryCode = industryCode;
        IndustryName = industryName;
        TradeDate = tradeDate;
        PctChange20d = pctChange20d;
        Rank20d = rank20d;
    }

    /// <summary>
    /// 行业代码。
    /// </summary>
    public string IndustryCode { get; init; }

    /// <summary>
    /// 行业名称。
    /// </summary>
    public string IndustryName { get; init; }

    /// <summary>
    /// 交易日。
    /// </summary>
    public DateOnly TradeDate { get; init; }

    /// <summary>
    /// 近 20 日涨跌幅。
    /// </summary>
    public decimal? PctChange20d { get; init; }

    /// <summary>
    /// 近 20 日行业强弱排名，数值越小越强。
    /// </summary>
    public int? Rank20d { get; init; }
}

/// <summary>
/// 表示股票最近一期财务快照。
/// </summary>
public sealed record FinancialSnapshot
{
    /// <summary>
    /// 初始化财务快照。
    /// </summary>
    public FinancialSnapshot(
        string stockCode,
        DateOnly reportDate,
        decimal? pe,
        decimal? pb,
        decimal? roe,
        decimal? revenueYoy,
        decimal? netProfitYoy,
        decimal? freeFloatMarketCap,
        decimal? operatingCashFlow = null,
        decimal? grossMargin = null,
        decimal? debtToAssetRatio = null,
        decimal? operatingCashFlowNet = null,
        DateOnly? announcementDate = null,
        string? dataSourcePriority = null)
    {
        StockCode = stockCode;
        ReportDate = reportDate;
        Pe = pe;
        Pb = pb;
        Roe = roe;
        RevenueYoy = revenueYoy;
        NetProfitYoy = netProfitYoy;
        FreeFloatMarketCap = freeFloatMarketCap;
        OperatingCashFlow = operatingCashFlow;
        GrossMargin = grossMargin;
        DebtToAssetRatio = debtToAssetRatio;
        OperatingCashFlowNet = operatingCashFlowNet;
        AnnouncementDate = announcementDate;
        DataSourcePriority = dataSourcePriority;
    }

    /// <summary>
    /// 股票代码。
    /// </summary>
    public string StockCode { get; init; }

    /// <summary>
    /// 财报截止日期。
    /// </summary>
    public DateOnly ReportDate { get; init; }

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
    /// 营收同比增速。
    /// </summary>
    public decimal? RevenueYoy { get; init; }

    /// <summary>
    /// 净利润同比增速。
    /// </summary>
    public decimal? NetProfitYoy { get; init; }

    /// <summary>
    /// 流通市值。
    /// </summary>
    public decimal? FreeFloatMarketCap { get; init; }

    public decimal? OperatingCashFlow { get; init; }

    public decimal? GrossMargin { get; init; }

    public decimal? DebtToAssetRatio { get; init; }

    public decimal? OperatingCashFlowNet { get; init; }

    public DateOnly? AnnouncementDate { get; init; }

    public string? DataSourcePriority { get; init; }
}

/// <summary>
/// 表示个股资金流快照。
/// </summary>
public sealed record StockFundFlowSnapshot(
    string StockCode,
    DateOnly TradeDate,
    decimal? MainNetAmount,
    decimal? MainNetPct,
    decimal? SuperLargeNetAmount,
    decimal? SuperLargeNetPct,
    decimal? LargeNetAmount,
    decimal? LargeNetPct,
    decimal? MediumNetAmount,
    decimal? MediumNetPct,
    decimal? SmallNetAmount,
    decimal? SmallNetPct,
    decimal? RankPercentile5d);

/// <summary>
/// 表示行业资金流快照。
/// </summary>
public sealed record IndustryFundFlowSnapshot(
    string IndustryName,
    DateOnly TradeDate,
    decimal? MainNetAmount,
    decimal? MainNetPct,
    int? Rank,
    decimal? RankPercentile);

/// <summary>
/// 表示龙虎榜汇总快照。
/// </summary>
public sealed record LhbSnapshot(
    string StockCode,
    DateOnly TradeDate,
    string? Reason,
    decimal? BuyTop5Amount,
    decimal? SellTop5Amount,
    decimal? NetAmount,
    decimal? InstitutionBuyAmount,
    decimal? InstitutionSellAmount,
    decimal? InstitutionNetAmount,
    int? InstitutionBuyCount,
    bool IsInstitutionNetBuy,
    bool IsOnLhbToday,
    int Recent20dLhbCount,
    int? DaysSinceLastLhb,
    string? RiskFlags);

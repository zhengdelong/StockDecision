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
        DateOnly snapshotDate)
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
        decimal? freeFloatMarketCap)
    {
        StockCode = stockCode;
        ReportDate = reportDate;
        Pe = pe;
        Pb = pb;
        Roe = roe;
        RevenueYoy = revenueYoy;
        NetProfitYoy = netProfitYoy;
        FreeFloatMarketCap = freeFloatMarketCap;
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
}

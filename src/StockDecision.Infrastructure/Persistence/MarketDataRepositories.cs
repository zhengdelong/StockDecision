using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using System.Data;
using System.Text.Json;
using StockDecision.Application.Contracts;
using StockDecision.Domain.Market;
using StockDecision.Domain.Strategy;

namespace StockDecision.Infrastructure.Persistence;

/// <summary>
/// 基于 EF Core 的原始市场数据仓储实现。
/// </summary>
public sealed class EfRawMarketDataRepository(StockDecisionDbContext dbContext) : IRawMarketDataRepository
{
    private const int MinimumFinancialReportCoverage = 1000;

    /// <summary>
    /// 读取原始日线中的最新交易日。
    /// </summary>
    public Task<DateOnly?> GetLatestTradeDateAsync(CancellationToken cancellationToken)
    {
        return dbContext.RawDailyBars.MaxAsync(static item => (DateOnly?)item.TradeDate, cancellationToken);
    }

    /// <summary>
    /// 读取指定交易日的股票画像，并补充最近财务和流动性信息。
    /// </summary>
    public async Task<IReadOnlyList<StockProfile>> GetLatestStockProfilesAsync(DateOnly tradeDate, CancellationToken cancellationToken)
    {
        var latestStocksFastPath = await dbContext.LatestRawStocks
            .AsNoTracking()
            .Where(static item => !item.StockCode.StartsWith("sh") && !item.StockCode.StartsWith("sz") && !item.StockCode.StartsWith("bj"))
            .Select(static item => new
            {
                item.StockCode,
                item.StockName,
                item.IndustryName,
                item.IsActive,
                item.IsSt,
                item.IsDelistingRisk,
                item.ListDate
            })
            .ToListAsync(cancellationToken);

        if (latestStocksFastPath.Count > 0)
        {
            var latestStockCodesFastPath = latestStocksFastPath.Select(static item => item.StockCode).ToList();
            var latestValuationFastPath = await dbContext.RawStocks
                .AsNoTracking()
                .Where(item => latestStockCodesFastPath.Contains(item.StockCode))
                .GroupBy(static item => item.StockCode)
                .Select(static group => group
                    .OrderByDescending(item => item.CreatedAt)
                    .Select(item => new
                    {
                        item.StockCode,
                        item.Pe,
                        item.Pb
                    })
                    .First())
                .ToDictionaryAsync(static item => item.StockCode, cancellationToken);

            var stocksNeedingMetadataFallback = latestStocksFastPath
                .Where(static item => IsMissingOrGenericIndustryName(item.IndustryName) || item.ListDate is null)
                .Select(static item => item.StockCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var latestIndustryFallbackFastPath = stocksNeedingMetadataFallback.Count == 0
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : await dbContext.RawStocks
                    .AsNoTracking()
                    .Where(item => stocksNeedingMetadataFallback.Contains(item.StockCode))
                    .Where(static item => item.IndustryName != null && item.IndustryName != string.Empty)
                    .GroupBy(static item => item.StockCode)
                    .Select(static group => group
                        .OrderByDescending(item => item.CreatedAt)
                        .Select(item => new
                        {
                            item.StockCode,
                            item.IndustryName
                        })
                        .First())
                    .ToDictionaryAsync(static item => item.StockCode, static item => item.IndustryName!, cancellationToken);

            var latestListDateFallbackFastPath = stocksNeedingMetadataFallback.Count == 0
                ? new Dictionary<string, DateOnly?>(StringComparer.OrdinalIgnoreCase)
                : await dbContext.RawStocks
                    .AsNoTracking()
                    .Where(item => stocksNeedingMetadataFallback.Contains(item.StockCode))
                    .Where(static item => item.ListDate != null)
                    .GroupBy(static item => item.StockCode)
                    .Select(static group => group
                        .OrderByDescending(item => item.CreatedAt)
                        .Select(item => new
                        {
                            item.StockCode,
                            item.ListDate
                        })
                        .First())
                    .ToDictionaryAsync(static item => item.StockCode, static item => item.ListDate, cancellationToken);

            var recentAmountTradeDatesFastPath = await dbContext.RawDailyBars
                .AsNoTracking()
                .Where(item => item.TradeDate <= tradeDate)
                .Select(static item => item.TradeDate)
                .Distinct()
                .OrderByDescending(static item => item)
                .Take(20)
                .ToListAsync(cancellationToken);

            var amountHistoryFastPath = await dbContext.RawDailyBars
                .AsNoTracking()
                .Where(item => recentAmountTradeDatesFastPath.Contains(item.TradeDate))
                .GroupBy(static item => item.StockCode)
                .Select(group => new
                {
                    StockCode = group.Key,
                    AverageAmount20d = group.Average(item => item.Amount ?? 0m)
                })
                .ToDictionaryAsync(static item => item.StockCode, static item => (decimal?)item.AverageAmount20d, cancellationToken);

            var latestDailyFastPath = await dbContext.RawDailyBars
                .AsNoTracking()
                .Where(item => item.TradeDate == tradeDate)
                .ToDictionaryAsync(static item => item.StockCode, cancellationToken);

            var validFinancialReportDatesFastPath = await GetValidRawFinancialReportDatesAsync(cancellationToken);
            var latestFinancialFastPath = await dbContext.RawFinancialSnapshots
                .AsNoTracking()
                .Where(item => validFinancialReportDatesFastPath.Contains(item.ReportDate))
                .GroupBy(static item => item.StockCode)
                .Select(static group => group.OrderByDescending(item => item.ReportDate).First())
                .ToDictionaryAsync(static item => item.StockCode, cancellationToken);

            return latestStocksFastPath.Select(item =>
            {
                latestDailyFastPath.TryGetValue(item.StockCode, out var latestBar);
                latestFinancialFastPath.TryGetValue(item.StockCode, out var financial);
                latestValuationFastPath.TryGetValue(item.StockCode, out var valuation);
                amountHistoryFastPath.TryGetValue(item.StockCode, out var averageAmount20d);
                latestIndustryFallbackFastPath.TryGetValue(item.StockCode, out var fallbackIndustryName);
                latestListDateFallbackFastPath.TryGetValue(item.StockCode, out var fallbackListDate);

                var resolvedIndustryName = IsMissingOrGenericIndustryName(item.IndustryName)
                    ? fallbackIndustryName
                    : item.IndustryName;
                var resolvedListDate = item.ListDate ?? fallbackListDate;

                return new StockProfile(
                    item.StockCode,
                    item.StockName,
                    resolvedIndustryName,
                    item.IsActive,
                    item.IsSt,
                    item.IsDelistingRisk,
                    resolvedListDate,
                    latestBar?.Close,
                    valuation?.Pe ?? financial?.Pe,
                    valuation?.Pb ?? financial?.Pb,
                    financial?.FreeFloatMarketCap,
                    latestBar?.TurnoverRate,
                    averageAmount20d,
                    tradeDate);
            }).ToList();
        }

        var latestStocks = await dbContext.LatestRawStocks
            .AsNoTracking()
            .Where(static item => !item.StockCode.StartsWith("sh") && !item.StockCode.StartsWith("sz") && !item.StockCode.StartsWith("bj"))
            .Select(static item => new
            {
                item.StockCode,
                item.StockName,
                item.IndustryName,
                item.IsActive,
                item.IsSt,
                item.IsDelistingRisk,
                item.ListDate
            })
            .ToListAsync(cancellationToken);

        var latestStockCodes = latestStocks.Select(static item => item.StockCode).ToList();
        var latestValuationByStock = await dbContext.RawStocks
            .AsNoTracking()
            .Where(item => latestStockCodes.Contains(item.StockCode))
            .GroupBy(static item => item.StockCode)
            .Select(static group => group
                .OrderByDescending(item => item.CreatedAt)
                .Select(item => new
                {
                    item.StockCode,
                    item.Pe,
                    item.Pb
                })
                .First())
            .ToDictionaryAsync(static item => item.StockCode, cancellationToken);

        // 行业和上市日期属于低频元数据，若最新快照缺失，则沿用历史上最近一次非空值。
        var latestIndustryByStock = await dbContext.RawStocks
            .Where(static item => !item.StockCode.StartsWith("sh") && !item.StockCode.StartsWith("sz") && !item.StockCode.StartsWith("bj"))
            .Where(static item => item.IndustryName != null && item.IndustryName != string.Empty)
            .GroupBy(static item => item.StockCode)
            .Select(static group => group
                .OrderByDescending(item => item.CreatedAt)
                .Select(item => new
                {
                    item.StockCode,
                    item.IndustryName
                })
                .First())
            .ToDictionaryAsync(static item => item.StockCode, static item => item.IndustryName!, cancellationToken);

        var latestListDateByStock = await dbContext.RawStocks
            .Where(static item => !item.StockCode.StartsWith("sh") && !item.StockCode.StartsWith("sz") && !item.StockCode.StartsWith("bj"))
            .Where(static item => item.ListDate != null)
            .GroupBy(static item => item.StockCode)
            .Select(static group => group
                .OrderByDescending(item => item.CreatedAt)
                .Select(item => new
                {
                    item.StockCode,
                    item.ListDate
                })
                .First())
            .ToDictionaryAsync(static item => item.StockCode, static item => item.ListDate, cancellationToken);

        var recentAmountTradeDates = await dbContext.RawDailyBars
            .AsNoTracking()
            .Where(item => item.TradeDate <= tradeDate)
            .Select(static item => item.TradeDate)
            .Distinct()
            .OrderByDescending(static item => item)
            .Take(20)
            .ToListAsync(cancellationToken);

        var amountHistory = await dbContext.RawDailyBars
            .AsNoTracking()
            .Where(item => recentAmountTradeDates.Contains(item.TradeDate))
            .GroupBy(static item => item.StockCode)
            .Select(group => new
            {
                StockCode = group.Key,
                AverageAmount20d = group.Average(item => item.Amount ?? 0m)
            })
            .ToDictionaryAsync(static item => item.StockCode, static item => (decimal?)item.AverageAmount20d, cancellationToken);

        var latestDaily = await dbContext.RawDailyBars
            .Where(item => item.TradeDate == tradeDate)
            .ToDictionaryAsync(static item => item.StockCode, cancellationToken);

        var validFinancialReportDates = await GetValidRawFinancialReportDatesAsync(cancellationToken);
        var latestFinancial = await dbContext.RawFinancialSnapshots
            .Where(item => validFinancialReportDates.Contains(item.ReportDate))
            .GroupBy(static item => item.StockCode)
            .Select(static group => group.OrderByDescending(item => item.ReportDate).First())
            .ToDictionaryAsync(static item => item.StockCode, cancellationToken);

        return latestStocks.Select(item =>
        {
            latestDaily.TryGetValue(item.StockCode, out var latestBar);
            latestFinancial.TryGetValue(item.StockCode, out var financial);
            latestValuationByStock.TryGetValue(item.StockCode, out var valuation);
            amountHistory.TryGetValue(item.StockCode, out var averageAmount20d);
            latestIndustryByStock.TryGetValue(item.StockCode, out var fallbackIndustryName);
            latestListDateByStock.TryGetValue(item.StockCode, out var fallbackListDate);

            var resolvedIndustryName = IsMissingOrGenericIndustryName(item.IndustryName)
                ? fallbackIndustryName
                : item.IndustryName;
            var resolvedListDate = item.ListDate ?? fallbackListDate;

            return new StockProfile(
                item.StockCode,
                item.StockName,
                resolvedIndustryName,
                item.IsActive,
                item.IsSt,
                item.IsDelistingRisk,
                resolvedListDate,
                latestBar?.Close,
                valuation?.Pe ?? financial?.Pe,
                valuation?.Pb ?? financial?.Pb,
                financial?.FreeFloatMarketCap,
                latestBar?.TurnoverRate,
                averageAmount20d,
                tradeDate);
        }).ToList();
    }

    private static bool IsMissingOrGenericIndustryName(string? industryName)
    {
        if (string.IsNullOrWhiteSpace(industryName))
        {
            return true;
        }

        var value = industryName.Trim();
        for (var prefix = 'A'; prefix <= 'S'; prefix++)
        {
            if (value.StartsWith($"{prefix} ", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return value is "制造业" or "金融业" or "房地产业" or "建筑业" or "农林牧渔业" or "采矿业";
    }

    /// <summary>
    /// 读取指定交易日的股票日线。
    /// </summary>
    public async Task<IReadOnlyList<DailyBar>> GetDailyBarsByTradeDateAsync(DateOnly tradeDate, CancellationToken cancellationToken)
    {
        return await dbContext.RawDailyBars
            .Where(item => item.TradeDate == tradeDate)
            .Select(static item => new DailyBar(
                item.StockCode,
                item.TradeDate,
                item.Open ?? 0m,
                item.High ?? 0m,
                item.Low ?? 0m,
                item.Close ?? 0m,
                item.Volume ?? 0L,
                item.Amount ?? 0m,
                item.PctChange,
                item.TurnoverRate))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 读取指定交易日的指数日线。
    /// </summary>
    public async Task<IReadOnlyList<MarketIndexBar>> GetIndexBarsByTradeDateAsync(DateOnly tradeDate, CancellationToken cancellationToken)
    {
        return await dbContext.RawMarketIndexBars
            .Where(item => item.TradeDate == tradeDate)
            .Select(static item => new MarketIndexBar(item.IndexCode, item.IndexName, item.TradeDate, item.Close ?? 0m))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 读取指定交易日的行业统计。
    /// </summary>
    public async Task<IReadOnlyList<IndustryDailyStat>> GetIndustryStatsByTradeDateAsync(DateOnly tradeDate, CancellationToken cancellationToken)
    {
        return await dbContext.RawIndustryDailyStats
            .Where(item => item.TradeDate == tradeDate)
            .Select(static item => new IndustryDailyStat(item.IndustryCode, item.IndustryName, item.TradeDate, item.PctChange20d, item.Rank20d))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 读取每只股票最新一期财务快照。
    /// </summary>
    public async Task<IReadOnlyList<FinancialSnapshot>> GetLatestFinancialSnapshotsAsync(CancellationToken cancellationToken)
    {
        var validReportDates = await GetValidRawFinancialReportDatesAsync(cancellationToken);
        if (validReportDates.Count == 0)
        {
            return [];
        }

        var rows = await dbContext.RawFinancialSnapshots
            .AsNoTracking()
            .Where(item => validReportDates.Contains(item.ReportDate))
            .Select(static item => new FinancialSnapshot(
                item.StockCode,
                item.ReportDate,
                item.Pe,
                item.Pb,
                item.Roe,
                item.RevenueYoy,
                item.NetProfitYoy,
                item.FreeFloatMarketCap,
                item.OperatingCashFlow,
                item.GrossMargin,
                item.DebtToAssetRatio,
                item.OperatingCashFlowNet,
                item.AnnouncementDate,
                item.DataSourcePriority))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(static item => item.StockCode)
            .Select(static group => group.OrderByDescending(item => item.ReportDate).First())
            .ToList();
    }

    private async Task<IReadOnlyList<DateOnly>> GetValidRawFinancialReportDatesAsync(CancellationToken cancellationToken)
    {
        return await dbContext.RawFinancialSnapshots
            .AsNoTracking()
            .Where(static item => item.Roe != null || item.RevenueYoy != null || item.NetProfitYoy != null)
            .GroupBy(static item => item.ReportDate)
            .Where(static group => group.Count() >= MinimumFinancialReportCoverage)
            .OrderByDescending(static group => group.Key)
            .Take(2)
            .Select(static group => group.Key)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StockFundFlowSnapshot>> GetStockFundFlowsByTradeDateAsync(DateOnly tradeDate, CancellationToken cancellationToken)
    {
        return await dbContext.RawStockFundFlows
            .Where(item => item.TradeDate == tradeDate)
            .Select(static item => new StockFundFlowSnapshot(
                item.StockCode,
                item.TradeDate,
                item.MainNetAmount,
                item.MainNetPct,
                item.SuperLargeNetAmount,
                item.SuperLargeNetPct,
                item.LargeNetAmount,
                item.LargeNetPct,
                item.MediumNetAmount,
                item.MediumNetPct,
                item.SmallNetAmount,
                item.SmallNetPct,
                item.RankPercentile5d))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<IndustryFundFlowSnapshot>> GetIndustryFundFlowsByTradeDateAsync(DateOnly tradeDate, CancellationToken cancellationToken)
    {
        var rows = await dbContext.RawIndustryFundFlows
            .Where(item => item.TradeDate == tradeDate)
            .ToListAsync(cancellationToken);
        var rankedCount = rows.Count(static item => item.Rank is not null);

        return rows.Select(item => new IndustryFundFlowSnapshot(
                item.IndustryName,
                item.TradeDate,
                item.MainNetAmount,
                item.MainNetPct,
                item.Rank,
                CalculateIndustryFundFlowRankPercentile(item.Rank, rankedCount)))
            .ToList();
    }

    private static decimal? CalculateIndustryFundFlowRankPercentile(int? rank, int rankedCount)
    {
        if (rank is null || rankedCount <= 0)
        {
            return null;
        }

        if (rankedCount == 1)
        {
            return 100m;
        }

        var percentile = (rankedCount - rank.Value) * 100m / (rankedCount - 1);
        return Math.Round(Math.Clamp(percentile, 0m, 100m), 4, MidpointRounding.AwayFromZero);
    }

    public async Task<IReadOnlyList<LhbSnapshot>> GetLhbSnapshotsByTradeDateAsync(DateOnly tradeDate, CancellationToken cancellationToken)
    {
        var rows = await dbContext.RawLhbStockSummaries
            .Where(item => item.TradeDate <= tradeDate)
            .OrderByDescending(item => item.TradeDate)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(static item => item.StockCode)
            .Select(group =>
            {
                var ordered = group.OrderByDescending(item => item.TradeDate).ToList();
                var latest = ordered[0];
                var recent20dCount = ordered.Count(item => item.TradeDate >= tradeDate.AddDays(-30));
                var previous = ordered.FirstOrDefault(item => item.TradeDate < tradeDate);
                var daysSinceLast = latest.TradeDate == tradeDate
                    ? (previous is null ? (int?)null : tradeDate.DayNumber - previous.TradeDate.DayNumber)
                    : tradeDate.DayNumber - latest.TradeDate.DayNumber;

                return new LhbSnapshot(
                    latest.StockCode,
                    latest.TradeDate,
                    latest.Reason,
                    latest.BuyTop5Amount,
                    latest.SellTop5Amount,
                    latest.NetAmount,
                    latest.InstitutionBuyAmount,
                    latest.InstitutionSellAmount,
                    latest.InstitutionNetAmount,
                    latest.InstitutionBuyCount,
                    latest.IsInstitutionNetBuy,
                    latest.TradeDate == tradeDate,
                    recent20dCount,
                    daysSinceLast,
                    BuildLhbRiskFlags(latest, recent20dCount));
            })
            .ToList();
    }

    private static string? BuildLhbRiskFlags(RawLhbStockSummaryRow latest, int recent20dCount)
    {
        var flags = new List<string>();
        if (recent20dCount >= 3)
        {
            flags.Add("频繁上榜");
        }

        if (latest.InstitutionNetAmount < 0m)
        {
            flags.Add("机构净卖出");
        }

        if (latest.NetAmount < 0m)
        {
            flags.Add("龙虎榜净卖出");
        }

        if (!string.IsNullOrWhiteSpace(latest.Reason) &&
            (latest.Reason.Contains("异常波动", StringComparison.Ordinal) ||
             latest.Reason.Contains("连续涨停", StringComparison.Ordinal) ||
             latest.Reason.Contains("严重异动", StringComparison.Ordinal)))
        {
            flags.Add("短炒异动");
        }

        return flags.Count == 0 ? null : string.Join(",", flags);
    }

    /// <summary>
    /// 读取原始财务快照中的最新财报日期。
    /// </summary>
    public Task<DateOnly?> GetLatestFinancialReportDateAsync(CancellationToken cancellationToken)
    {
        return dbContext.RawFinancialSnapshots.MaxAsync(static item => (DateOnly?)item.ReportDate, cancellationToken);
    }
}

/// <summary>
/// 基于 EF Core 的领域层市场快照仓储实现。
/// </summary>
public sealed class EfMarketDataRepository(StockDecisionDbContext dbContext) : IMarketDataRepository
{
    private const int MinimumFinancialReportCoverage = 1000;
    private static readonly IReadOnlyDictionary<string, string> ScoringIndustryAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["IT服务Ⅲ"] = "IT服务",
        ["证券Ⅲ"] = "证券",
        ["中药Ⅲ"] = "中药",
        ["电子化学品Ⅲ"] = "电子化学品",
        ["轨交设备Ⅲ"] = "轨交设备",
        ["白酒Ⅲ"] = "白酒",
        ["贸易Ⅲ"] = "贸易",
        ["环保设备Ⅲ"] = "环保设备",
        ["城商行Ⅲ"] = "银行",
        ["专用设备制造业"] = "专用设备",
        ["通用设备制造业"] = "通用设备",
        ["其他专用设备"] = "专用设备",
        ["其他通用设备"] = "通用设备",
        ["垂直应用软件"] = "软件开发",
        ["半导体材料"] = "半导体",
        ["半导体设备"] = "半导体",
        ["其他汽车零部件"] = "汽车零部件",
        ["车身附件及饰件"] = "汽车零部件",
        ["动力煤"] = "煤炭开采加工",
        ["普钢"] = "钢铁",
        ["港口"] = "港口航运",
        ["航运港口"] = "港口航运",
        ["铁路公路"] = "公路铁路运输",
        ["房地产开发"] = "房地产",
        ["产业地产"] = "房地产",
        ["多业态零售"] = "零售",
        ["食品加工"] = "食品加工制造",
        ["机器人"] = "自动化设备",
        ["工控设备"] = "自动化设备",
        ["农药"] = "农化制品",
        ["其他金属新材料"] = "金属新材料",
        ["非运动服装"] = "服装家纺",
        ["成品家居"] = "家居用品",
        ["定制家居"] = "家居用品",
        ["体外诊断"] = "医疗器械",
        ["通信终端及配件"] = "通信设备",
        ["配电设备"] = "电网设备",
        ["医药流通"] = "医药商业",
        ["轮胎轮毂"] = "橡胶制品",
        ["其他化学制品"] = "化学制品",
        ["水务及水治理"] = "环境治理",
        ["传媒"] = "文化传媒",
        ["医疗研发外包"] = "医疗服务",
        ["工程机械整机"] = "工程机械",
        ["出版"] = "文化传媒",
        ["调味发酵品Ⅲ"] = "食品加工制造",
        ["面板"] = "光学光电子",
        ["其他专业工程"] = "建筑装饰",
        ["专业工程"] = "建筑装饰",
        ["煤炭"] = "煤炭开采加工",
        ["大宗用纸"] = "造纸",
        ["饮料乳品"] = "饮料制造",
        ["集成电路封测"] = "半导体",
        ["其他酒类"] = "饮料制造",
        ["风力发电"] = "电力",
        ["航空机场"] = "机场航运",
        ["自然景区"] = "旅游及酒店",
        ["人工景区"] = "旅游及酒店",
        ["航天装备Ⅲ"] = "军工装备",
        ["软饮料"] = "饮料制造",
        ["被动元件"] = "元件",
        ["焦炭Ⅲ"] = "煤炭开采加工",
        ["农商行Ⅲ"] = "银行",
        ["国有大型银行Ⅲ"] = "银行",
        ["纸包装"] = "包装印刷",
        ["炼油化工"] = "石油加工贸易",
        ["炼化及贸易"] = "石油加工贸易",
        ["光伏发电"] = "电力",
        ["食品制造业"] = "食品加工制造",
        ["品牌化妆品"] = "美容护理",
        ["其他小金属"] = "小金属",
        ["通信工程及服务"] = "通信服务",
        ["玻璃玻纤"] = "非金属材料",
        ["玻纤制造"] = "非金属材料",
        ["其他通信设备"] = "通信设备",
        ["涤纶"] = "化学纤维",
        ["其他化学原料"] = "化学原料",
        ["公交"] = "公路铁路运输",
        ["LED"] = "光学光电子",
        ["机场"] = "机场航运",
        ["商用车"] = "汽车整车",
        ["商用载客车"] = "汽车整车",
        ["商业地产"] = "房地产",
        ["聚氨酯"] = "化学制品",
        ["照明设备Ⅲ"] = "光学光电子",
        ["汽车服务"] = "汽车服务及其他",
        ["培训教育"] = "教育",
        ["光伏加工设备"] = "光伏设备",
        ["仓储物流"] = "物流",
        ["油田服务"] = "油气开采及服务",
        ["有机硅"] = "化学制品",
        ["电信运营商"] = "通信服务",
        ["非白酒"] = "饮料制造",
        ["个护用品"] = "美容护理",
        ["果蔬加工"] = "农产品加工",
        ["酒店"] = "旅游及酒店",
        ["种子"] = "种植业与林业",
        ["家具制造业"] = "家居用品",
        ["生猪养殖"] = "养殖业",
        ["风电整机"] = "风电设备",
        ["其他黑色家电"] = "黑色家电",
        ["冶钢辅料"] = "钢铁",
        ["钢铁管材"] = "钢铁",
        ["橡胶"] = "橡胶制品",
        ["熟食"] = "食品加工制造",
        ["生活用纸"] = "造纸",
        ["卫浴电器"] = "厨卫电器",
        ["涂料"] = "化学制品"
    };

    /// <summary>
    /// 读取已导入快照的最新交易日。
    /// </summary>
    public Task<DateOnly?> GetLatestImportedTradeDateAsync(CancellationToken cancellationToken)
    {
        return dbContext.MarketDailyBars.MaxAsync(static item => (DateOnly?)item.TradeDate, cancellationToken);
    }

    private async Task<IReadOnlyDictionary<string, string>> GetAvailableScoringIndustryNamesAsync(DateOnly tradeDate, CancellationToken cancellationToken)
    {
        var statNames = await dbContext.MarketIndustryDailyStats
            .AsNoTracking()
            .Where(item => item.TradeDate == tradeDate)
            .Select(static item => item.IndustryName)
            .ToListAsync(cancellationToken);
        var fundFlowNames = await dbContext.MarketIndustryFundFlows
            .AsNoTracking()
            .Where(item => item.TradeDate == tradeDate)
            .Select(static item => item.IndustryName)
            .ToListAsync(cancellationToken);

        return statNames.Concat(fundFlowNames)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static item => item, static item => item, StringComparer.OrdinalIgnoreCase);
    }

    private static string? ResolveScoringIndustryName(string? industryName, IReadOnlyDictionary<string, string> availableIndustryNames)
    {
        if (string.IsNullOrWhiteSpace(industryName) || availableIndustryNames.Count == 0)
        {
            return null;
        }

        var normalized = industryName.Trim();
        if (availableIndustryNames.TryGetValue(normalized, out var exact))
        {
            return exact;
        }

        var strippedRomanSuffix = normalized.TrimEnd('Ⅰ', 'Ⅱ', 'Ⅲ', 'Ⅳ', 'Ⅴ', 'Ⅵ', 'Ⅶ', 'Ⅷ', 'Ⅸ', 'Ⅹ').Trim();
        if (strippedRomanSuffix.Length != normalized.Length
            && availableIndustryNames.TryGetValue(strippedRomanSuffix, out var romanSuffixMatch))
        {
            return romanSuffixMatch;
        }

        if (normalized.EndsWith("制造业", StringComparison.Ordinal))
        {
            var strippedManufacturing = normalized[..^"制造业".Length].Trim();
            if (availableIndustryNames.TryGetValue(strippedManufacturing, out var manufacturingMatch))
            {
                return manufacturingMatch;
            }
        }

        if (ScoringIndustryAliases.TryGetValue(normalized, out var alias)
            && availableIndustryNames.TryGetValue(alias, out var aliasMatch))
        {
            return aliasMatch;
        }

        return null;
    }

    /// <summary>
    /// 用同一交易日的全量数据替换领域快照。
    /// </summary>
#pragma warning disable EF1002 // tradeDateLiteral is formatted from DateOnly, not user input.
    public async Task ReplaceMarketSnapshotAsync(
        DateOnly tradeDate,
        IReadOnlyList<StockProfile> stocks,
        IReadOnlyList<DailyBar> dailyBars,
        IReadOnlyList<MarketIndexBar> indexBars,
        IReadOnlyList<IndustryDailyStat> industries,
        IReadOnlyList<FinancialSnapshot> financials,
        IReadOnlyList<StockFundFlowSnapshot> stockFundFlows,
        IReadOnlyList<IndustryFundFlowSnapshot> industryFundFlows,
        IReadOnlyList<LhbSnapshot> lhbSnapshots,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var tradeDateLiteral = tradeDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var deletedProfiles = await dbContext.Database.ExecuteSqlRawAsync($"DELETE FROM market_stock_profiles WHERE trade_date = '{tradeDateLiteral}'", [], cancellationToken);
        var deletedDailyBars = await dbContext.Database.ExecuteSqlRawAsync($"DELETE FROM market_daily_bars WHERE trade_date = '{tradeDateLiteral}'", [], cancellationToken);
        var deletedIndexBars = await dbContext.Database.ExecuteSqlRawAsync($"DELETE FROM market_index_bars WHERE trade_date = '{tradeDateLiteral}'", [], cancellationToken);
        var deletedIndustries = await dbContext.Database.ExecuteSqlRawAsync($"DELETE FROM market_industry_daily_stats WHERE trade_date = '{tradeDateLiteral}'", [], cancellationToken);
        var deletedStockFundFlows = await dbContext.Database.ExecuteSqlRawAsync($"DELETE FROM market_stock_fund_flows WHERE trade_date = '{tradeDateLiteral}'", [], cancellationToken);
        var deletedIndustryFundFlows = await dbContext.Database.ExecuteSqlRawAsync($"DELETE FROM market_industry_fund_flows WHERE trade_date = '{tradeDateLiteral}'", [], cancellationToken);
        var deletedLhb = await dbContext.Database.ExecuteSqlRawAsync($"DELETE FROM market_lhb_snapshots WHERE trade_date = '{tradeDateLiteral}'", [], cancellationToken);
        _ = (deletedProfiles, deletedDailyBars, deletedIndexBars, deletedIndustries, deletedStockFundFlows, deletedIndustryFundFlows, deletedLhb);
        var insertedDailyBars = await dbContext.Database.ExecuteSqlRawAsync($"""
            INSERT INTO market_daily_bars (
                stock_code,
                trade_date,
                open,
                high,
                low,
                close,
                volume,
                amount,
                pct_change,
                turnover_rate)
            SELECT
                stock_code,
                trade_date,
                COALESCE(open, 0),
                COALESCE(high, 0),
                COALESCE(low, 0),
                COALESCE(close, 0),
                COALESCE(volume, 0),
                COALESCE(amount, 0),
                pct_change,
                turnover_rate
            FROM (
                SELECT
                    r.*,
                    ROW_NUMBER() OVER (
                        PARTITION BY r.stock_code, r.trade_date
                        ORDER BY CASE r.adjust_type WHEN 'qfq' THEN 0 WHEN '' THEN 1 ELSE 2 END
                    ) AS rn
                FROM raw_daily_bars AS r
                WHERE r.trade_date = '{tradeDateLiteral}'
            ) AS deduped
            WHERE rn = 1
            """, [], cancellationToken);
        _ = insertedDailyBars;
        var insertedIndexBars = await dbContext.Database.ExecuteSqlRawAsync($"""
            INSERT INTO market_index_bars (
                index_code,
                trade_date,
                index_name,
                close)
            SELECT
                index_code,
                trade_date,
                index_name,
                COALESCE(close, 0)
            FROM (
                SELECT
                    r.*,
                    ROW_NUMBER() OVER (
                        PARTITION BY r.index_code, r.trade_date
                        ORDER BY r.fetched_at DESC, r.id DESC
                    ) AS rn
                FROM raw_market_index_bars AS r
                WHERE r.trade_date = '{tradeDateLiteral}'
            ) AS deduped
            WHERE rn = 1
            ON DUPLICATE KEY UPDATE
                index_name = VALUES(index_name),
                close = VALUES(close)
            """, [], cancellationToken);
        _ = insertedIndexBars;
        var insertedIndustries = await dbContext.Database.ExecuteSqlRawAsync($"""
            INSERT INTO market_industry_daily_stats (
                industry_code,
                trade_date,
                industry_name,
                pct_change_20d,
                rank_20d)
            SELECT
                industry_code,
                trade_date,
                industry_name,
                pct_change_20d,
                rank_20d
            FROM (
                SELECT
                    r.*,
                    ROW_NUMBER() OVER (
                        PARTITION BY r.industry_code, r.trade_date
                        ORDER BY r.id DESC
                    ) AS rn
                FROM raw_industry_daily_stats AS r
                WHERE r.trade_date = '{tradeDateLiteral}'
            ) AS deduped
            WHERE rn = 1
            ON DUPLICATE KEY UPDATE
                industry_name = VALUES(industry_name),
                pct_change_20d = VALUES(pct_change_20d),
                rank_20d = VALUES(rank_20d)
            """, [], cancellationToken);
        _ = insertedIndustries;
        var insertedStockFundFlows = await dbContext.Database.ExecuteSqlRawAsync($"""
            INSERT INTO market_stock_fund_flows (
                stock_code,
                trade_date,
                main_net_amount,
                main_net_pct,
                super_large_net_amount,
                super_large_net_pct,
                large_net_amount,
                large_net_pct,
                medium_net_amount,
                medium_net_pct,
                small_net_amount,
                small_net_pct,
                rank_percentile_5d)
            SELECT
                stock_code,
                trade_date,
                main_net_amount,
                main_net_pct,
                super_large_net_amount,
                super_large_net_pct,
                large_net_amount,
                large_net_pct,
                medium_net_amount,
                medium_net_pct,
                small_net_amount,
                small_net_pct,
                rank_percentile_5d
            FROM (
                SELECT
                    r.*,
                    ROW_NUMBER() OVER (
                        PARTITION BY r.stock_code, r.trade_date
                        ORDER BY r.fetched_at DESC, r.id DESC
                    ) AS rn
                FROM raw_stock_fund_flows AS r
                WHERE r.trade_date = '{tradeDateLiteral}'
            ) AS deduped
            WHERE rn = 1
            ON DUPLICATE KEY UPDATE
                main_net_amount = VALUES(main_net_amount),
                main_net_pct = VALUES(main_net_pct),
                super_large_net_amount = VALUES(super_large_net_amount),
                super_large_net_pct = VALUES(super_large_net_pct),
                large_net_amount = VALUES(large_net_amount),
                large_net_pct = VALUES(large_net_pct),
                medium_net_amount = VALUES(medium_net_amount),
                medium_net_pct = VALUES(medium_net_pct),
                small_net_amount = VALUES(small_net_amount),
                small_net_pct = VALUES(small_net_pct),
                rank_percentile_5d = VALUES(rank_percentile_5d)
            """, [], cancellationToken);
        _ = insertedStockFundFlows;
        var insertedIndustryFundFlows = await dbContext.Database.ExecuteSqlRawAsync($"""
            INSERT INTO market_industry_fund_flows (
                industry_name,
                trade_date,
                main_net_amount,
                main_net_pct,
                `rank`,
                rank_percentile)
            SELECT
                industry_name,
                trade_date,
                main_net_amount,
                main_net_pct,
                `rank`,
                CASE
                    WHEN `rank` IS NULL THEN NULL
                    WHEN ranked_count <= 1 THEN 100
                    ELSE ROUND(((ranked_count - `rank`) * 100) / (ranked_count - 1), 4)
                END
            FROM (
                SELECT
                    r.*,
                    COUNT(CASE WHEN r.`rank` IS NOT NULL THEN 1 END) OVER () AS ranked_count,
                    ROW_NUMBER() OVER (
                        PARTITION BY r.industry_name, r.trade_date
                        ORDER BY r.fetched_at DESC, r.id DESC
                    ) AS rn
                FROM raw_industry_fund_flows AS r
                WHERE r.trade_date = '{tradeDateLiteral}'
            ) AS deduped
            WHERE rn = 1
            ON DUPLICATE KEY UPDATE
                main_net_amount = VALUES(main_net_amount),
                main_net_pct = VALUES(main_net_pct),
                `rank` = VALUES(`rank`),
                rank_percentile = VALUES(rank_percentile)
            """, [], cancellationToken);
        _ = insertedIndustryFundFlows;
        var insertedLhb = await dbContext.Database.ExecuteSqlRawAsync($"""
            INSERT INTO market_lhb_snapshots (
                stock_code,
                trade_date,
                reason,
                buy_top5_amount,
                sell_top5_amount,
                net_amount,
                institution_buy_amount,
                institution_sell_amount,
                institution_net_amount,
                institution_buy_count,
                is_institution_net_buy,
                is_on_lhb_today,
                recent_20d_lhb_count,
                days_since_last_lhb,
                risk_flags)
            SELECT
                stock_code,
                trade_date,
                reason,
                buy_top5_amount,
                sell_top5_amount,
                net_amount,
                institution_buy_amount,
                institution_sell_amount,
                institution_net_amount,
                institution_buy_count,
                is_institution_net_buy,
                trade_date = '{tradeDateLiteral}',
                recent_20d_lhb_count,
                CASE
                    WHEN trade_date = '{tradeDateLiteral}' AND previous_trade_date IS NULL THEN NULL
                    WHEN trade_date = '{tradeDateLiteral}' THEN DATEDIFF('{tradeDateLiteral}', previous_trade_date)
                    ELSE DATEDIFF('{tradeDateLiteral}', trade_date)
                END,
                NULLIF(CONCAT_WS(',',
                    CASE WHEN recent_20d_lhb_count >= 3 THEN '频繁上榜' END,
                    CASE WHEN institution_net_amount < 0 THEN '机构净卖出' END,
                    CASE WHEN net_amount < 0 THEN '龙虎榜净卖出' END,
                    CASE WHEN reason REGEXP '异常波动|连续.*涨停|严重异动' THEN '短炒异动' END
                ), '')
            FROM (
                SELECT
                    r.*,
                    ROW_NUMBER() OVER (
                        PARTITION BY r.stock_code
                        ORDER BY r.trade_date DESC, r.fetched_at DESC, r.id DESC
                    ) AS rn,
                    SUM(CASE WHEN r.trade_date >= DATE_SUB('{tradeDateLiteral}', INTERVAL 30 DAY) THEN 1 ELSE 0 END)
                        OVER (PARTITION BY r.stock_code) AS recent_20d_lhb_count,
                    MAX(CASE WHEN r.trade_date < '{tradeDateLiteral}' THEN r.trade_date END)
                        OVER (PARTITION BY r.stock_code) AS previous_trade_date
                FROM raw_lhb_stock_summaries AS r
                WHERE r.trade_date <= '{tradeDateLiteral}'
            ) AS ranked
            WHERE rn = 1
            ON DUPLICATE KEY UPDATE
                reason = VALUES(reason),
                buy_top5_amount = VALUES(buy_top5_amount),
                sell_top5_amount = VALUES(sell_top5_amount),
                net_amount = VALUES(net_amount),
                institution_buy_amount = VALUES(institution_buy_amount),
                institution_sell_amount = VALUES(institution_sell_amount),
                institution_net_amount = VALUES(institution_net_amount),
                institution_buy_count = VALUES(institution_buy_count),
                is_institution_net_buy = VALUES(is_institution_net_buy),
                is_on_lhb_today = VALUES(is_on_lhb_today),
                recent_20d_lhb_count = VALUES(recent_20d_lhb_count),
                days_since_last_lhb = VALUES(days_since_last_lhb),
                risk_flags = VALUES(risk_flags)
            """, [], cancellationToken);
        _ = insertedLhb;
        var upsertedProfiles = await UpsertMarketStockProfilesAsync(tradeDate, stocks, cancellationToken);
        _ = upsertedProfiles;

        // 财务快照按“最近一期全量覆盖”维护，因此这里不按 tradeDate 删除，而是整表替换。
        dbContext.MarketFinancialSnapshots.RemoveRange(dbContext.MarketFinancialSnapshots);
        await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM market_financial_snapshots", [], cancellationToken);

        dbContext.ChangeTracker.Clear();
        dbContext.MarketFinancialSnapshots.AddRange(financials.Select(item => new MarketFinancialSnapshotEntity
        {
            StockCode = item.StockCode,
            ReportDate = item.ReportDate,
            Pe = item.Pe,
            Pb = item.Pb,
            Roe = item.Roe,
            RevenueYoy = item.RevenueYoy,
            NetProfitYoy = item.NetProfitYoy,
            FreeFloatMarketCap = item.FreeFloatMarketCap,
            OperatingCashFlow = item.OperatingCashFlow,
            GrossMargin = item.GrossMargin,
            DebtToAssetRatio = item.DebtToAssetRatio,
            OperatingCashFlowNet = item.OperatingCashFlowNet,
            AnnouncementDate = item.AnnouncementDate,
            DataSourcePriority = item.DataSourcePriority
        }));
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<int> UpsertMarketStockProfilesAsync(DateOnly tradeDate, IReadOnlyList<StockProfile> stocks, CancellationToken cancellationToken)
    {
        var total = 0;
        var tradeDateValue = tradeDate.ToDateTime(TimeOnly.MinValue);
        var availableIndustryNames = await GetAvailableScoringIndustryNamesAsync(tradeDate, cancellationToken);
        foreach (var batch in stocks.Chunk(500))
        {
            var values = new List<string>(batch.Length);
            var parameters = new List<object>(batch.Length * 15);
            for (var index = 0; index < batch.Length; index++)
            {
                var item = batch[index];
                var scoringIndustryName = ResolveScoringIndustryName(item.ScoringIndustryName, availableIndustryNames)
                    ?? ResolveScoringIndustryName(item.IndustryName, availableIndustryNames);
                values.Add($"""
                    (@stockCode{index}, @tradeDate{index}, @stockName{index}, @industryName{index}, @isActive{index},
                     @isSt{index}, @isDelistingRisk{index}, @listDate{index}, @latestPrice{index}, @pe{index},
                     @pb{index}, @freeFloatMarketCap{index}, @turnoverRate{index}, @averageAmount20d{index}, @scoringIndustryName{index})
                    """);
                parameters.Add(new MySqlParameter($"@stockCode{index}", item.StockCode));
                parameters.Add(new MySqlParameter($"@tradeDate{index}", tradeDateValue));
                parameters.Add(new MySqlParameter($"@stockName{index}", item.StockName));
                parameters.Add(new MySqlParameter($"@industryName{index}", (object?)item.IndustryName ?? DBNull.Value));
                parameters.Add(new MySqlParameter($"@scoringIndustryName{index}", (object?)scoringIndustryName ?? DBNull.Value));
                parameters.Add(new MySqlParameter($"@isActive{index}", item.IsActive));
                parameters.Add(new MySqlParameter($"@isSt{index}", item.IsSt));
                parameters.Add(new MySqlParameter($"@isDelistingRisk{index}", item.IsDelistingRisk));
                parameters.Add(new MySqlParameter($"@listDate{index}", item.ListDate?.ToDateTime(TimeOnly.MinValue) ?? (object)DBNull.Value));
                parameters.Add(new MySqlParameter($"@latestPrice{index}", (object?)item.LatestPrice ?? DBNull.Value));
                parameters.Add(new MySqlParameter($"@pe{index}", (object?)item.Pe ?? DBNull.Value));
                parameters.Add(new MySqlParameter($"@pb{index}", (object?)item.Pb ?? DBNull.Value));
                parameters.Add(new MySqlParameter($"@freeFloatMarketCap{index}", (object?)item.FreeFloatMarketCap ?? DBNull.Value));
                parameters.Add(new MySqlParameter($"@turnoverRate{index}", (object?)item.TurnoverRate ?? DBNull.Value));
                parameters.Add(new MySqlParameter($"@averageAmount20d{index}", (object?)item.AverageAmount20d ?? DBNull.Value));
            }

            var sql = $"""
                INSERT INTO market_stock_profiles (
                    stock_code,
                    trade_date,
                    stock_name,
                    industry_name,
                    is_active,
                    is_st,
                    is_delisting_risk,
                    list_date,
                    latest_price,
                    pe,
                    pb,
                    free_float_market_cap,
                    turnover_rate,
                    average_amount20d,
                    scoring_industry_name)
                VALUES {string.Join(",\n", values)}
                ON DUPLICATE KEY UPDATE
                    stock_name = VALUES(stock_name),
                    industry_name = VALUES(industry_name),
                    scoring_industry_name = VALUES(scoring_industry_name),
                    is_active = VALUES(is_active),
                    is_st = VALUES(is_st),
                    is_delisting_risk = VALUES(is_delisting_risk),
                    list_date = VALUES(list_date),
                    latest_price = VALUES(latest_price),
                    pe = VALUES(pe),
                    pb = VALUES(pb),
                    free_float_market_cap = VALUES(free_float_market_cap),
                    turnover_rate = VALUES(turnover_rate),
                    average_amount20d = VALUES(average_amount20d)
                """;
            total += await dbContext.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);
        }

        return total;
    }
#pragma warning restore EF1002

    /// <summary>
    /// 读取活跃股票代码。
    /// </summary>
    public async Task<IReadOnlyList<string>> GetActiveStockCodesAsync(CancellationToken cancellationToken)
    {
        return await dbContext.MarketStockProfiles
            .Where(static item => item.IsActive)
            .Select(static item => item.StockCode)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 读取单只股票历史日线。
    /// </summary>
    public async Task<IReadOnlyList<DailyBar>> GetDailyBarHistoryAsync(string stockCode, DateOnly tradeDate, int maxRows, CancellationToken cancellationToken)
    {
        return await dbContext.RawDailyBars
            .Where(item => item.StockCode == stockCode && item.TradeDate <= tradeDate)
            .OrderByDescending(item => item.TradeDate)
            .Take(maxRows)
            .OrderBy(item => item.TradeDate)
            .Select(static item => new DailyBar(item.StockCode, item.TradeDate, item.Open ?? 0m, item.High ?? 0m, item.Low ?? 0m, item.Close ?? 0m, item.Volume ?? 0L, item.Amount ?? 0m, item.PctChange, item.TurnoverRate))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<DailyBar>>> GetDailyBarHistoriesByCodesAsync(IEnumerable<string> stockCodes, DateOnly tradeDate, int maxRows, CancellationToken cancellationToken)
    {
        var codes = stockCodes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (codes.Count == 0)
        {
            return new Dictionary<string, IReadOnlyList<DailyBar>>(StringComparer.OrdinalIgnoreCase);
        }

        var recentTradeDates = await dbContext.RawDailyBars
            .Where(item => item.TradeDate <= tradeDate)
            .Select(static item => item.TradeDate)
            .Distinct()
            .OrderByDescending(static item => item)
            .Take(maxRows)
            .ToListAsync(cancellationToken);

        if (recentTradeDates.Count == 0)
        {
            return new Dictionary<string, IReadOnlyList<DailyBar>>(StringComparer.OrdinalIgnoreCase);
        }

        var rows = await dbContext.RawDailyBars
            .AsNoTracking()
            .Where(item => codes.Contains(item.StockCode) && recentTradeDates.Contains(item.TradeDate))
            .Select(static item => new DailyBar(item.StockCode, item.TradeDate, item.Open ?? 0m, item.High ?? 0m, item.Low ?? 0m, item.Close ?? 0m, item.Volume ?? 0L, item.Amount ?? 0m, item.PctChange, item.TurnoverRate))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(static item => item.StockCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<DailyBar>)group.OrderBy(static item => item.TradeDate).ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 读取单只股票指定交易日后的前瞻日线。
    /// </summary>
    public async Task<IReadOnlyDictionary<string, IndicatorCalculationMetrics>> GetIndicatorCalculationMetricsByCodesAsync(IEnumerable<string> stockCodes, DateOnly tradeDate, int maxRows, CancellationToken cancellationToken)
    {
        var codeSet = stockCodes
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (codeSet.Count == 0)
        {
            return new Dictionary<string, IndicatorCalculationMetrics>(StringComparer.OrdinalIgnoreCase);
        }

        var normalizedMaxRows = Math.Clamp(maxRows, 120, 260);
        var sql = $"""
            WITH cutoff AS (
                SELECT MIN(trade_date) AS min_trade_date
                FROM (
                    SELECT DISTINCT trade_date
                    FROM raw_daily_bars
                    WHERE trade_date <= @tradeDate
                    ORDER BY trade_date DESC
                    LIMIT {normalizedMaxRows}
                ) AS d
            ),
            bars AS (
                SELECT
                    r.stock_code AS StockCode,
                    r.trade_date AS TradeDate,
                    COALESCE(r.close, 0) AS ClosePrice,
                    COALESCE(r.high, 0) AS HighPrice,
                    COALESCE(r.low, 0) AS LowPrice,
                    COALESCE(r.amount, 0) AS Amount,
                    r.turnover_rate AS TurnoverRate,
                    ROW_NUMBER() OVER (PARTITION BY r.stock_code ORDER BY r.trade_date DESC) AS RowNumber,
                    LEAD(COALESCE(r.close, 0)) OVER (PARTITION BY r.stock_code ORDER BY r.trade_date DESC) AS PreviousClose
                FROM raw_daily_bars AS r
                CROSS JOIN cutoff AS c
                WHERE r.trade_date BETWEEN c.min_trade_date AND @tradeDate
            )
            SELECT
                StockCode,
                MAX(CASE WHEN RowNumber = 1 THEN ClosePrice END) AS Close,
                ROUND(AVG(CASE WHEN RowNumber <= 20 THEN ClosePrice END), 4) AS Ma20,
                ROUND(AVG(CASE WHEN RowNumber <= 60 THEN ClosePrice END), 4) AS Ma60,
                ROUND(AVG(CASE WHEN RowNumber <= 120 THEN ClosePrice END), 4) AS Ma120,
                COALESCE(ROUND(AVG(CASE WHEN RowNumber <= 14 THEN GREATEST(HighPrice - LowPrice, ABS(HighPrice - PreviousClose), ABS(LowPrice - PreviousClose)) END), 4), 0) AS Atr14,
                COALESCE(ROUND(((MAX(CASE WHEN RowNumber = 1 THEN ClosePrice END) - MAX(CASE WHEN RowNumber = 21 THEN ClosePrice END)) / NULLIF(MAX(CASE WHEN RowNumber = 21 THEN ClosePrice END), 0)) * 100, 4), 0) AS Return20d,
                COALESCE(ROUND(((MAX(CASE WHEN RowNumber = 1 THEN ClosePrice END) - MAX(CASE WHEN RowNumber = 61 THEN ClosePrice END)) / NULLIF(MAX(CASE WHEN RowNumber = 61 THEN ClosePrice END), 0)) * 100, 4), 0) AS Return60d,
                COALESCE(ROUND(((MAX(CASE WHEN RowNumber = 1 THEN ClosePrice END) - MAX(CASE WHEN RowNumber = 11 THEN ClosePrice END)) / NULLIF(MAX(CASE WHEN RowNumber = 11 THEN ClosePrice END), 0)) * 100, 4), 0) AS Return10d,
                COALESCE(ROUND(MAX(CASE WHEN RowNumber = 1 THEN Amount END) / NULLIF(AVG(CASE WHEN RowNumber BETWEEN 2 AND 21 THEN Amount END), 0), 4), 0) AS AmountRatio1d,
                COALESCE(ROUND(AVG(CASE WHEN RowNumber BETWEEN 11 AND 30 THEN ClosePrice END), 4), 0) AS PreviousMa20,
                COALESCE(ROUND(AVG(CASE WHEN RowNumber BETWEEN 11 AND 70 THEN ClosePrice END), 4), 0) AS Ma60Previous,
                MAX(CASE WHEN RowNumber <= 20 THEN ClosePrice END) AS BreakoutClose,
                MAX(CASE WHEN RowNumber = 1 THEN TurnoverRate END) AS TurnoverRate
            FROM bars
            GROUP BY StockCode
            HAVING COUNT(*) >= 120
            """;

        var result = new Dictionary<string, IndicatorCalculationMetrics>(StringComparer.OrdinalIgnoreCase);
        var connection = dbContext.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;
        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 120;
            command.Parameters.Add(new MySqlParameter("@tradeDate", tradeDate.ToDateTime(TimeOnly.MinValue)));

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var stockCode = reader.GetString(0);
                if (!codeSet.Contains(stockCode))
                {
                    continue;
                }

                result[stockCode] = new IndicatorCalculationMetrics(
                    stockCode,
                    reader.GetDecimal(1),
                    reader.GetDecimal(2),
                    reader.GetDecimal(3),
                    reader.GetDecimal(4),
                    reader.GetDecimal(5),
                    reader.GetDecimal(6),
                    reader.GetDecimal(7),
                    reader.GetDecimal(8),
                    reader.GetDecimal(9),
                    reader.GetDecimal(10),
                    reader.GetDecimal(11),
                    reader.GetDecimal(12),
                    reader.IsDBNull(13) ? null : reader.GetDecimal(13));
            }
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<string, StockScoringHistoryMetrics>> GetScoringHistoryMetricsByCodesAsync(IEnumerable<string> stockCodes, DateOnly tradeDate, CancellationToken cancellationToken)
    {
        var codes = stockCodes
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (codes.Count == 0)
        {
            return new Dictionary<string, StockScoringHistoryMetrics>(StringComparer.OrdinalIgnoreCase);
        }

        const string sql = """
            WITH code_filter AS (
                SELECT stock_code
                FROM JSON_TABLE(@codesJson, '$[*]' COLUMNS (stock_code VARCHAR(16) PATH '$')) AS codes
            ),
            ranked AS (
                SELECT
                    r.stock_code AS StockCode,
                    COALESCE(r.close, 0) AS ClosePrice,
                    COALESCE(r.amount, 0) AS Amount,
                    ROW_NUMBER() OVER (PARTITION BY r.stock_code ORDER BY r.trade_date DESC) AS RowNumber
                FROM raw_daily_bars AS r
                INNER JOIN code_filter AS c ON c.stock_code = r.stock_code
                WHERE r.trade_date <= @tradeDate
            )
            SELECT
                StockCode,
                CASE
                    WHEN MAX(CASE WHEN RowNumber = 11 THEN ClosePrice END) IS NULL
                        OR MAX(CASE WHEN RowNumber = 11 THEN ClosePrice END) = 0
                    THEN 0
                    ELSE ROUND(
                        ((MAX(CASE WHEN RowNumber = 1 THEN ClosePrice END) - MAX(CASE WHEN RowNumber = 11 THEN ClosePrice END))
                        / MAX(CASE WHEN RowNumber = 11 THEN ClosePrice END)) * 100,
                        4)
                END AS Return10d,
                CASE
                    WHEN COUNT(CASE WHEN RowNumber BETWEEN 2 AND 21 THEN 1 END) < 20
                        OR AVG(CASE WHEN RowNumber BETWEEN 2 AND 21 THEN Amount END) <= 0
                    THEN 0
                    ELSE ROUND(
                        MAX(CASE WHEN RowNumber = 1 THEN Amount END)
                        / AVG(CASE WHEN RowNumber BETWEEN 2 AND 21 THEN Amount END),
                        4)
                END AS AmountRatio1d,
                CASE
                    WHEN COUNT(CASE WHEN RowNumber BETWEEN 11 AND 70 THEN 1 END) < 60
                    THEN 0
                    ELSE ROUND(AVG(CASE WHEN RowNumber BETWEEN 11 AND 70 THEN ClosePrice END), 4)
                END AS Ma60Previous
            FROM ranked
            WHERE RowNumber <= 70
            GROUP BY StockCode
            """;

        var rows = await dbContext.Database
            .SqlQueryRaw<ScoringHistoryMetricsRow>(
                sql,
                new MySqlParameter("@codesJson", JsonSerializer.Serialize(codes)),
                new MySqlParameter("@tradeDate", tradeDate))
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            static item => item.StockCode,
            static item => new StockScoringHistoryMetrics(item.StockCode, item.Return10d, item.AmountRatio1d, item.Ma60Previous),
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<DailyBar>> GetForwardDailyBarsAsync(string stockCode, DateOnly tradeDate, int maxRows, CancellationToken cancellationToken)
    {
        return await dbContext.RawDailyBars
            .Where(item => item.StockCode == stockCode && item.TradeDate > tradeDate)
            .OrderBy(item => item.TradeDate)
            .Take(maxRows)
            .Select(static item => new DailyBar(item.StockCode, item.TradeDate, item.Open ?? 0m, item.High ?? 0m, item.Low ?? 0m, item.Close ?? 0m, item.Volume ?? 0L, item.Amount ?? 0m, item.PctChange, item.TurnoverRate))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 读取指数历史日线。
    /// </summary>
    public async Task<IReadOnlyList<MarketIndexBar>> GetIndexBarHistoryAsync(DateOnly tradeDate, int maxRows, CancellationToken cancellationToken)
    {
        var rows = await dbContext.RawMarketIndexBars
            .Where(item => item.TradeDate <= tradeDate)
            .OrderByDescending(item => item.TradeDate)
            .Take(maxRows * 3)
            .ToListAsync(cancellationToken);

        // 先粗略取近端样本，再按指数分组截断，避免一次把所有指数历史都拉出内存。
        return rows
            .GroupBy(static item => item.IndexCode)
            .SelectMany(group => group.OrderBy(item => item.TradeDate).TakeLast(maxRows))
            .Select(static item => new MarketIndexBar(item.IndexCode, item.IndexName, item.TradeDate, item.Close ?? 0m))
            .ToList();
    }

    /// <summary>
    /// 读取最近若干个交易日。
    /// </summary>
    public async Task<IReadOnlyList<DateOnly>> GetRecentTradeDatesAsync(int maxRows, CancellationToken cancellationToken)
    {
        // 回测会按区间读取较长时间的交易日，不能再被截断成最近 60 天。
        var normalized = Math.Clamp(maxRows, 1, 4000);
        return await dbContext.RawDailyBars
            .Select(item => item.TradeDate)
            .Distinct()
            .OrderByDescending(item => item)
            .Take(normalized)
            .OrderBy(item => item)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 批量读取股票画像。
    /// </summary>
    public async Task<IReadOnlyDictionary<string, StockProfile>> GetStockProfilesByCodesAsync(IEnumerable<string> stockCodes, CancellationToken cancellationToken)
    {
        var set = stockCodes.Distinct().ToList();
        var rows = await dbContext.MarketStockProfiles
            .AsNoTracking()
            .Where(item => set.Contains(item.StockCode))
            .GroupBy(item => item.StockCode)
            .Select(static group => group
                .OrderByDescending(item => item.TradeDate)
                .First())
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            static item => item.StockCode,
            static item => new StockProfile(item.StockCode, item.StockName, item.IndustryName, item.IsActive, item.IsSt, item.IsDelistingRisk, item.ListDate, item.LatestPrice, item.Pe, item.Pb, item.FreeFloatMarketCap, item.TurnoverRate, item.AverageAmount20d, item.TradeDate, item.ScoringIndustryName));
    }

    /// <summary>
    /// 批量读取最新财务快照。
    /// </summary>
    public async Task<IReadOnlyDictionary<string, FinancialSnapshot>> GetLatestFinancialsByCodesAsync(IEnumerable<string> stockCodes, CancellationToken cancellationToken)
    {
        var set = stockCodes.Distinct().ToList();
        var validReportDates = await GetValidMarketFinancialReportDatesAsync(cancellationToken);
        if (set.Count == 0 || validReportDates.Count == 0)
        {
            return new Dictionary<string, FinancialSnapshot>(StringComparer.OrdinalIgnoreCase);
        }

        var rows = await dbContext.MarketFinancialSnapshots
            .AsNoTracking()
            .Where(item => set.Contains(item.StockCode) && validReportDates.Contains(item.ReportDate))
            .ToListAsync(cancellationToken);

        return rows.GroupBy(static item => item.StockCode)
            .Select(static group => group.OrderByDescending(item => item.ReportDate).First())
            .ToDictionary(
                static item => item.StockCode,
                static item => new FinancialSnapshot(item.StockCode, item.ReportDate, item.Pe, item.Pb, item.Roe, item.RevenueYoy, item.NetProfitYoy, item.FreeFloatMarketCap, item.OperatingCashFlow, item.GrossMargin, item.DebtToAssetRatio, item.OperatingCashFlowNet, item.AnnouncementDate, item.DataSourcePriority));
    }

    /// <summary>
    /// 读取全部最新财务快照。
    /// </summary>
    public async Task<IReadOnlyList<FinancialSnapshot>> GetLatestFinancialSnapshotsAsync(CancellationToken cancellationToken)
    {
        var validReportDates = await GetValidMarketFinancialReportDatesAsync(cancellationToken);
        if (validReportDates.Count == 0)
        {
            return [];
        }

        return await dbContext.MarketFinancialSnapshots
            .Where(item => validReportDates.Contains(item.ReportDate))
            .GroupBy(item => item.StockCode)
            .Select(static group => group
                .OrderByDescending(item => item.ReportDate)
                .Select(item => new FinancialSnapshot(item.StockCode, item.ReportDate, item.Pe, item.Pb, item.Roe, item.RevenueYoy, item.NetProfitYoy, item.FreeFloatMarketCap, item.OperatingCashFlow, item.GrossMargin, item.DebtToAssetRatio, item.OperatingCashFlowNet, item.AnnouncementDate, item.DataSourcePriority))
                .First())
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<DateOnly>> GetValidMarketFinancialReportDatesAsync(CancellationToken cancellationToken)
    {
        return await dbContext.MarketFinancialSnapshots
            .AsNoTracking()
            .Where(static item => item.Roe != null || item.RevenueYoy != null || item.NetProfitYoy != null)
            .GroupBy(static item => item.ReportDate)
            .Where(static group => group.Count() >= MinimumFinancialReportCoverage)
            .OrderByDescending(static group => group.Key)
            .Take(2)
            .Select(static group => group.Key)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, StockFundFlowSnapshot>> GetStockFundFlowsByCodesAsync(DateOnly tradeDate, IEnumerable<string> stockCodes, CancellationToken cancellationToken)
    {
        var set = stockCodes.Distinct().ToList();
        var rows = await dbContext.MarketStockFundFlows
            .AsNoTracking()
            .Where(item => item.TradeDate == tradeDate && set.Contains(item.StockCode))
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            static item => item.StockCode,
            static item => new StockFundFlowSnapshot(item.StockCode, item.TradeDate, item.MainNetAmount, item.MainNetPct, item.SuperLargeNetAmount, item.SuperLargeNetPct, item.LargeNetAmount, item.LargeNetPct, item.MediumNetAmount, item.MediumNetPct, item.SmallNetAmount, item.SmallNetPct, item.RankPercentile5d));
    }

    public async Task<IReadOnlyDictionary<string, IndustryFundFlowSnapshot>> GetIndustryFundFlowsByNamesAsync(DateOnly tradeDate, IEnumerable<string?> industryNames, CancellationToken cancellationToken)
    {
        var names = industryNames.Where(static item => !string.IsNullOrWhiteSpace(item)).Select(static item => item!).Distinct().ToList();
        var rows = await dbContext.MarketIndustryFundFlows
            .AsNoTracking()
            .Where(item => item.TradeDate == tradeDate && names.Contains(item.IndustryName))
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            static item => item.IndustryName,
            static item => new IndustryFundFlowSnapshot(item.IndustryName, item.TradeDate, item.MainNetAmount, item.MainNetPct, item.Rank, item.RankPercentile));
    }

    public async Task<IReadOnlyDictionary<string, LhbSnapshot>> GetLhbSnapshotsByCodesAsync(DateOnly tradeDate, IEnumerable<string> stockCodes, CancellationToken cancellationToken)
    {
        var set = stockCodes.Distinct().ToList();
        var rows = await dbContext.MarketLhbSnapshots
            .AsNoTracking()
            .Where(item => item.TradeDate == tradeDate && set.Contains(item.StockCode))
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            static item => item.StockCode,
            static item => new LhbSnapshot(item.StockCode, item.TradeDate, item.Reason, item.BuyTop5Amount, item.SellTop5Amount, item.NetAmount, item.InstitutionBuyAmount, item.InstitutionSellAmount, item.InstitutionNetAmount, item.InstitutionBuyCount, item.IsInstitutionNetBuy, item.IsOnLhbToday, item.Recent20dLhbCount, item.DaysSinceLastLhb, item.RiskFlags));
    }

    /// <summary>
    /// 读取导入层财务快照中的最新财报日期。
    /// </summary>
    public Task<DateOnly?> GetLatestImportedFinancialReportDateAsync(CancellationToken cancellationToken)
    {
        return dbContext.MarketFinancialSnapshots.MaxAsync(static item => (DateOnly?)item.ReportDate, cancellationToken);
    }

    /// <summary>
    /// 按行业名称批量读取行业统计。
    /// </summary>
    public async Task<IReadOnlyDictionary<string, IndustryDailyStat>> GetIndustryStatsByNamesAsync(DateOnly tradeDate, IEnumerable<string?> industryNames, CancellationToken cancellationToken)
    {
        var names = industryNames.Where(static item => !string.IsNullOrWhiteSpace(item)).Select(static item => item!).Distinct().ToList();
        var rows = await dbContext.MarketIndustryDailyStats
            .AsNoTracking()
            .Where(item => item.TradeDate == tradeDate && names.Contains(item.IndustryName))
            .GroupBy(item => item.IndustryName)
            .Select(static group => group
                .OrderBy(item => item.IndustryCode)
                .First())
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            static item => item.IndustryName,
            static item => new IndustryDailyStat(item.IndustryCode, item.IndustryName, item.TradeDate, item.PctChange20d, item.Rank20d));
    }

    /// <summary>
    /// 读取指定交易日的行业快照列表。
    /// </summary>
    public async Task<IReadOnlyList<IndustryDailyStat>> GetIndustryStatsAsync(DateOnly tradeDate, CancellationToken cancellationToken)
    {
        return await dbContext.MarketIndustryDailyStats
            .Where(item => item.TradeDate == tradeDate)
            .OrderBy(item => item.Rank20d ?? int.MaxValue)
            .ThenBy(item => item.IndustryName)
            .Select(static item => new IndustryDailyStat(item.IndustryCode, item.IndustryName, item.TradeDate, item.PctChange20d, item.Rank20d))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 写入技术指标快照。
    /// </summary>
    public async Task UpsertIndicatorSnapshotsAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, IReadOnlyList<IndicatorSnapshot> indicators, CancellationToken cancellationToken)
    {
        var snapshotVersionValue = snapshotVersion.ToValue();
        await dbContext.StrategyIndicatorSnapshots
            .Where(item => item.TradeDate == tradeDate && item.SnapshotVersion == snapshotVersionValue)
            .ExecuteDeleteAsync(cancellationToken);

        var previousAutoDetectChanges = dbContext.ChangeTracker.AutoDetectChangesEnabled;
        try
        {
            dbContext.ChangeTracker.AutoDetectChangesEnabled = false;
        dbContext.StrategyIndicatorSnapshots.AddRange(indicators.Select(item => new StrategyIndicatorSnapshotEntity
        {
            StockCode = item.StockCode,
            TradeDate = item.TradeDate,
            SnapshotVersion = snapshotVersionValue,
            Close = item.Close,
            Ma20 = item.Ma20,
            Ma60 = item.Ma60,
            Ma120 = item.Ma120,
            Atr14 = item.Atr14,
            Return20d = item.Return20d,
            Return60d = item.Return60d,
            RelativeStrengthScore = item.RelativeStrengthScore,
            Is20DayBreakout = item.Is20DayBreakout,
            IsMa20Upward = item.IsMa20Upward,
            IsBullishStacked = item.IsBullishStacked,
            DistanceToMa20Pct = item.DistanceToMa20Pct,
            TurnoverRate = item.TurnoverRate
        }));
        await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            dbContext.ChangeTracker.AutoDetectChangesEnabled = previousAutoDetectChanges;
        }

        dbContext.ChangeTracker.Clear();
    }

    /// <summary>
    /// 写入市场环境快照。
    /// </summary>
    public async Task UpsertMarketRegimeAsync(StrategySnapshotVersion snapshotVersion, MarketRegimeSnapshot regime, CancellationToken cancellationToken)
    {
        var snapshotVersionValue = snapshotVersion.ToValue();
        await dbContext.StrategyMarketRegimes
            .Where(item => item.TradeDate == regime.TradeDate && item.SnapshotVersion == snapshotVersionValue)
            .ExecuteDeleteAsync(cancellationToken);
        dbContext.StrategyMarketRegimes.Add(new StrategyMarketRegimeEntity
        {
            TradeDate = regime.TradeDate,
            SnapshotVersion = snapshotVersionValue,
            Regime = regime.Regime.ToString(),
            ConfirmedIndexCount = regime.ConfirmedIndexCount,
            IsSignalEligible = regime.IsSignalEligible,
            Summary = regime.Summary
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// 写入候选股列表。
    /// </summary>
    public async Task UpsertCandidatesAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, IReadOnlyList<CandidateStock> candidates, CancellationToken cancellationToken)
    {
        var snapshotVersionValue = snapshotVersion.ToValue();
        await dbContext.StrategyCandidates
            .Where(item => item.TradeDate == tradeDate && item.SnapshotVersion == snapshotVersionValue)
            .ExecuteDeleteAsync(cancellationToken);

        var previousAutoDetectChanges = dbContext.ChangeTracker.AutoDetectChangesEnabled;
        try
        {
            dbContext.ChangeTracker.AutoDetectChangesEnabled = false;
        dbContext.StrategyCandidates.AddRange(candidates.Select(item => new StrategyCandidateEntity
        {
            TradeDate = item.TradeDate,
            SnapshotVersion = snapshotVersionValue,
            StockCode = item.StockCode,
            StockName = item.StockName,
            IndustryName = item.IndustryName,
            Grade = item.Grade.ToString(),
            StrategyType = item.StrategyType.ToString(),
            IsTradable = item.IsTradable,
            EligibilityStatus = item.EligibilityStatus,
            EligibilityReason = item.EligibilityReason,
            TotalScore = item.TotalScore,
            RelativeStrengthScorePart = item.ScoreBreakdown.RelativeStrengthScore,
            TrendScorePart = item.ScoreBreakdown.TrendScore,
            VolumePriceScorePart = item.ScoreBreakdown.VolumePriceScore,
            FundamentalScorePart = item.ScoreBreakdown.FundamentalScore,
            RiskDisciplineScorePart = item.ScoreBreakdown.RiskDisciplineScore,
            Close = item.Close,
            Ma20 = item.Ma20,
            Ma60 = item.Ma60,
            Ma120 = item.Ma120,
            Atr14 = item.Atr14,
            RelativeStrengthScore = item.RelativeStrengthScore,
            Pe = item.Pe,
            Pb = item.Pb,
            Roe = item.Roe,
            StopLossPrice = item.StopLossPrice,
            TargetPrice = item.TargetPrice,
            RiskRewardRatio = item.RiskRewardRatio,
            Explanation = item.Explanation
        }));
        await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            dbContext.ChangeTracker.AutoDetectChangesEnabled = previousAutoDetectChanges;
        }

        dbContext.ChangeTracker.Clear();
    }

    /// <summary>
    /// 写入交易信号列表。
    /// </summary>
    public async Task UpsertScoreSnapshotsAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, IReadOnlyList<StrategyScoreSnapshot> scores, CancellationToken cancellationToken)
    {
        var snapshotVersionValue = snapshotVersion.ToValue();
        await dbContext.StrategyScoreSnapshots
            .Where(item => item.TradeDate == tradeDate && item.SnapshotVersion == snapshotVersionValue)
            .ExecuteDeleteAsync(cancellationToken);

        var previousAutoDetectChanges = dbContext.ChangeTracker.AutoDetectChangesEnabled;
        try
        {
            dbContext.ChangeTracker.AutoDetectChangesEnabled = false;
        dbContext.StrategyScoreSnapshots.AddRange(scores.Select(item => new StrategyScoreSnapshotEntity
        {
            TradeDate = item.TradeDate,
            SnapshotVersion = snapshotVersionValue,
            StockCode = item.StockCode,
            StockName = item.StockName,
            IndustryName = item.IndustryName,
            TotalScore = item.TotalScore,
            RelativeStrengthScorePart = item.RelativeStrengthScorePart,
            TrendScorePart = item.TrendScorePart,
            VolumePriceScorePart = item.VolumePriceScorePart,
            FundamentalScorePart = item.FundamentalScorePart,
            RiskDisciplineScorePart = item.RiskDisciplineScorePart,
            Pe = item.Pe,
            Pb = item.Pb,
            Roe = item.Roe
        }));
        await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            dbContext.ChangeTracker.AutoDetectChangesEnabled = previousAutoDetectChanges;
        }

        dbContext.ChangeTracker.Clear();
    }

    public async Task UpsertSignalsAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, IReadOnlyList<TradeSignal> signals, CancellationToken cancellationToken)
    {
        var snapshotVersionValue = snapshotVersion.ToValue();
        await dbContext.StrategyTradeSignals
            .Where(item => item.TradeDate == tradeDate && item.SnapshotVersion == snapshotVersionValue)
            .ExecuteDeleteAsync(cancellationToken);

        var previousAutoDetectChanges = dbContext.ChangeTracker.AutoDetectChangesEnabled;
        try
        {
            dbContext.ChangeTracker.AutoDetectChangesEnabled = false;
        dbContext.StrategyTradeSignals.AddRange(signals.Select(item => new StrategyTradeSignalEntity
        {
            TradeDate = item.TradeDate,
            SnapshotVersion = snapshotVersionValue,
            StockCode = item.StockCode,
            StockName = item.StockName,
            IndustryName = item.IndustryName,
            StrategyType = item.StrategyType.ToString(),
            EligibilityStatus = item.EligibilityStatus,
            EligibilityReason = item.EligibilityReason,
            TotalScore = item.TotalScore,
            RelativeStrengthScorePart = item.ScoreBreakdown.RelativeStrengthScore,
            TrendScorePart = item.ScoreBreakdown.TrendScore,
            VolumePriceScorePart = item.ScoreBreakdown.VolumePriceScore,
            FundamentalScorePart = item.ScoreBreakdown.FundamentalScore,
            RiskDisciplineScorePart = item.ScoreBreakdown.RiskDisciplineScore,
            TriggerPrice = item.TriggerPrice,
            StopLossPrice = item.StopLossPrice,
            TargetPrice = item.TargetPrice,
            RiskRewardRatio = item.RiskRewardRatio,
            SuggestedCapital = item.SuggestedCapital,
            EstimatedShares = item.EstimatedShares,
            Explanation = item.Explanation,
            GeneratedAtUtc = item.GeneratedAtUtc
        }));
        await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            dbContext.ChangeTracker.AutoDetectChangesEnabled = previousAutoDetectChanges;
        }

        dbContext.ChangeTracker.Clear();
    }

    /// <summary>
    /// 读取指定交易日的指标快照列表。
    /// </summary>
    public async Task<IReadOnlyList<IndicatorSnapshot>> GetIndicatorSnapshotsAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken)
    {
        var snapshotVersionValue = snapshotVersion.ToValue();
        return await dbContext.StrategyIndicatorSnapshots
            .Where(item => item.TradeDate == tradeDate && item.SnapshotVersion == snapshotVersionValue)
            .Select(static item => new IndicatorSnapshot(item.StockCode, item.TradeDate, item.Close, item.Ma20, item.Ma60, item.Ma120, item.Atr14, item.Return20d, item.Return60d, item.RelativeStrengthScore, item.Is20DayBreakout, item.IsMa20Upward, item.IsBullishStacked, item.DistanceToMa20Pct, item.TurnoverRate))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 读取指定交易日的市场环境。
    /// </summary>
    public Task<int> CountScoreSnapshotsAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken)
    {
        var snapshotVersionValue = snapshotVersion.ToValue();
        return dbContext.StrategyScoreSnapshots
            .AsNoTracking()
            .CountAsync(item => item.TradeDate == tradeDate && item.SnapshotVersion == snapshotVersionValue, cancellationToken);
    }

    public async Task<PagedResponse<FinancialListItemResponse>> GetFinancialScorePageAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, FinancialListQuery query, CancellationToken cancellationToken)
    {
        var snapshotVersionValue = snapshotVersion.ToValue();
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var where = new List<string>
        {
            "s.trade_date = @tradeDate",
            "s.snapshot_version = @snapshotVersion"
        };
        var search = query.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            where.Add("(s.stock_code LIKE @search OR s.stock_name LIKE @search OR s.industry_name LIKE @search)");
        }

        if (query.MinRoe is not null)
        {
            where.Add("COALESCE(f.roe, s.roe) >= @minRoe");
        }

        if (query.PositiveGrowthOnly == true)
        {
            where.Add("COALESCE(f.revenue_yoy, 0) > 0 AND COALESCE(f.net_profit_yoy, 0) > 0");
        }

        var whereSql = string.Join(" AND ", where);
        const string validFinancialDatesSql = """
            SELECT report_date
            FROM (
                SELECT report_date
                FROM market_financial_snapshots
                WHERE roe IS NOT NULL OR revenue_yoy IS NOT NULL OR net_profit_yoy IS NOT NULL
                GROUP BY report_date
                HAVING COUNT(*) >= 1000
                ORDER BY report_date DESC
                LIMIT 2
            ) AS valid_financial_dates
            """;
        var financialJoinSql = $"""
            LEFT JOIN (
                SELECT mf.*
                FROM market_financial_snapshots AS mf
                INNER JOIN (
                    SELECT stock_code, MAX(report_date) AS report_date
                    FROM market_financial_snapshots
                    WHERE report_date IN ({validFinancialDatesSql})
                    GROUP BY stock_code
                ) AS latest_f
                    ON latest_f.stock_code = mf.stock_code
                   AND latest_f.report_date = mf.report_date
            ) AS f
                ON f.stock_code = s.stock_code
            """;
        var countJoinSql = query.MinRoe is not null || query.PositiveGrowthOnly == true
            ? financialJoinSql
            : string.Empty;
        var orderSql = (query.SortBy ?? "score").ToLowerInvariant() switch
        {
            "revenue" => "f.revenue_yoy DESC",
            "profit" => "f.net_profit_yoy DESC",
            "marketcap" => "f.free_float_market_cap DESC",
            _ => "s.total_score DESC, COALESCE(f.roe, s.roe) DESC"
        };

        var countSql = $"""
            SELECT COUNT(*) AS Value
            FROM strategy_score_snapshots AS s
            {countJoinSql}
            WHERE {whereSql}
            """;
        var totalCount = await dbContext.Database
            .SqlQueryRaw<int>(countSql, BuildFinancialPageParameters(tradeDate, snapshotVersionValue, search, query.MinRoe, null, null))
            .SingleAsync(cancellationToken);

        var dataSql = $"""
            SELECT
                s.stock_code AS StockCode,
                s.stock_name AS StockName,
                s.industry_name AS IndustryName,
                f.report_date AS ReportDate,
                s.total_score AS TotalScore,
                COALESCE(f.pe, s.pe) AS Pe,
                COALESCE(f.pb, s.pb) AS Pb,
                COALESCE(f.roe, s.roe) AS Roe,
                f.revenue_yoy AS RevenueYoy,
                f.net_profit_yoy AS NetProfitYoy,
                f.free_float_market_cap AS FreeFloatMarketCap,
                f.operating_cash_flow AS OperatingCashFlow,
                f.gross_margin AS GrossMargin,
                f.debt_to_asset_ratio AS DebtToAssetRatio,
                f.operating_cash_flow_net AS OperatingCashFlowNet,
                f.announcement_date AS AnnouncementDate,
                f.data_source_priority AS DataSourcePriority
            FROM strategy_score_snapshots AS s
            {financialJoinSql}
            WHERE {whereSql}
            ORDER BY {orderSql}
            LIMIT @take OFFSET @skip
            """;
        var rows = await dbContext.Database
            .SqlQueryRaw<FinancialScorePageRow>(
                dataSql,
                BuildFinancialPageParameters(tradeDate, snapshotVersionValue, search, query.MinRoe, (page - 1) * pageSize, pageSize))
            .ToListAsync(cancellationToken);
        var items = rows
            .Select(item => new FinancialListItemResponse(
                item.StockCode,
                item.StockName,
                item.IndustryName,
                item.ReportDate,
                item.TotalScore,
                item.Pe,
                item.Pb,
                item.Roe,
                item.RevenueYoy,
                item.NetProfitYoy,
                item.FreeFloatMarketCap,
                item.OperatingCashFlow,
                item.GrossMargin,
                item.DebtToAssetRatio,
                item.OperatingCashFlowNet,
                item.AnnouncementDate,
                item.DataSourcePriority))
            .ToList();

        return new PagedResponse<FinancialListItemResponse>(items, page, pageSize, totalCount);
    }

    public async Task<MarketRegimeSnapshot?> GetMarketRegimeAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken)
    {
        var snapshotVersionValue = snapshotVersion.ToValue();
        var row = await dbContext.StrategyMarketRegimes.FirstOrDefaultAsync(item => item.TradeDate == tradeDate && item.SnapshotVersion == snapshotVersionValue, cancellationToken);
        return row is null ? null : new MarketRegimeSnapshot(row.TradeDate, Enum.Parse<MarketSignalEligibility>(row.Regime), row.ConfirmedIndexCount, row.IsSignalEligible, row.Summary);
    }

    /// <summary>
    /// 读取指定交易日候选股。
    /// </summary>
    public async Task<IReadOnlyList<CandidateStock>> GetCandidatesAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken)
    {
        var snapshotVersionValue = snapshotVersion.ToValue();
        return await dbContext.StrategyCandidates
            .Where(item => item.TradeDate == tradeDate && item.SnapshotVersion == snapshotVersionValue)
            .Select(static item => new CandidateStock(
                item.TradeDate,
                item.StockCode,
                item.StockName,
                item.IndustryName,
                Enum.Parse<CandidateGrade>(item.Grade),
                Enum.Parse<StrategyType>(item.StrategyType),
                item.IsTradable,
                item.EligibilityStatus,
                item.EligibilityReason,
                item.TotalScore,
                new CandidateScoreBreakdown(item.RelativeStrengthScorePart, item.TrendScorePart, item.VolumePriceScorePart, item.FundamentalScorePart, item.RiskDisciplineScorePart, null),
                item.Close,
                item.Ma20,
                item.Ma60,
                item.Ma120,
                item.Atr14,
                item.RelativeStrengthScore,
                item.Pe,
                item.Pb,
                item.Roe,
                item.StopLossPrice,
                item.TargetPrice,
                item.RiskRewardRatio,
                item.Explanation))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 读取指定交易日交易信号。
    /// </summary>
    public async Task<IReadOnlyList<TradeSignal>> GetSignalsAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken)
    {
        var snapshotVersionValue = snapshotVersion.ToValue();
        return await dbContext.StrategyTradeSignals
            .Where(item => item.TradeDate == tradeDate && item.SnapshotVersion == snapshotVersionValue)
            .Select(static item => new TradeSignal(
                item.TradeDate,
                item.StockCode,
                item.StockName,
                item.IndustryName,
                Enum.Parse<StrategyType>(item.StrategyType),
                item.EligibilityStatus,
                item.EligibilityReason,
                item.TotalScore,
                new CandidateScoreBreakdown(item.RelativeStrengthScorePart, item.TrendScorePart, item.VolumePriceScorePart, item.FundamentalScorePart, item.RiskDisciplineScorePart, null),
                item.TriggerPrice,
                item.StopLossPrice,
                item.TargetPrice,
                item.RiskRewardRatio,
                item.SuggestedCapital,
                item.EstimatedShares,
                item.Explanation,
                item.GeneratedAtUtc))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 读取单只股票画像。
    /// </summary>
    public async Task<StockProfile?> GetStockProfileAsync(string stockCode, CancellationToken cancellationToken)
    {
        var row = await dbContext.MarketStockProfiles
            .Where(item => item.StockCode == stockCode)
            .OrderByDescending(item => item.TradeDate)
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : new StockProfile(row.StockCode, row.StockName, row.IndustryName, row.IsActive, row.IsSt, row.IsDelistingRisk, row.ListDate, row.LatestPrice, row.Pe, row.Pb, row.FreeFloatMarketCap, row.TurnoverRate, row.AverageAmount20d, row.TradeDate, row.ScoringIndustryName);
    }

    /// <summary>
    /// 读取单只股票最近财务快照。
    /// </summary>
    public async Task<FinancialSnapshot?> GetLatestFinancialAsync(string stockCode, CancellationToken cancellationToken)
    {
        var validReportDates = await GetValidMarketFinancialReportDatesAsync(cancellationToken);
        if (validReportDates.Count == 0)
        {
            return null;
        }

        var row = await dbContext.MarketFinancialSnapshots
            .Where(item => item.StockCode == stockCode && validReportDates.Contains(item.ReportDate))
            .OrderByDescending(item => item.ReportDate)
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : new FinancialSnapshot(row.StockCode, row.ReportDate, row.Pe, row.Pb, row.Roe, row.RevenueYoy, row.NetProfitYoy, row.FreeFloatMarketCap, row.OperatingCashFlow, row.GrossMargin, row.DebtToAssetRatio, row.OperatingCashFlowNet, row.AnnouncementDate, row.DataSourcePriority);
    }

    /// <summary>
    /// 读取单只股票指标快照。
    /// </summary>
    public async Task<IndicatorSnapshot?> GetIndicatorSnapshotAsync(string stockCode, DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken)
    {
        var snapshotVersionValue = snapshotVersion.ToValue();
        var row = await dbContext.StrategyIndicatorSnapshots.FirstOrDefaultAsync(item => item.StockCode == stockCode && item.TradeDate == tradeDate && item.SnapshotVersion == snapshotVersionValue, cancellationToken);
        return row is null ? null : new IndicatorSnapshot(row.StockCode, row.TradeDate, row.Close, row.Ma20, row.Ma60, row.Ma120, row.Atr14, row.Return20d, row.Return60d, row.RelativeStrengthScore, row.Is20DayBreakout, row.IsMa20Upward, row.IsBullishStacked, row.DistanceToMa20Pct, row.TurnoverRate);
    }

    /// <summary>
    /// 读取单只股票候选结果。
    /// </summary>
    public async Task<CandidateStock?> GetCandidateAsync(string stockCode, DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken)
    {
        var snapshotVersionValue = snapshotVersion.ToValue();
        var row = await dbContext.StrategyCandidates.FirstOrDefaultAsync(item => item.StockCode == stockCode && item.TradeDate == tradeDate && item.SnapshotVersion == snapshotVersionValue, cancellationToken);
        return row is null ? null : new CandidateStock(
            row.TradeDate,
            row.StockCode,
            row.StockName,
            row.IndustryName,
            Enum.Parse<CandidateGrade>(row.Grade),
            Enum.Parse<StrategyType>(row.StrategyType),
            row.IsTradable,
            row.EligibilityStatus,
            row.EligibilityReason,
            row.TotalScore,
            new CandidateScoreBreakdown(row.RelativeStrengthScorePart, row.TrendScorePart, row.VolumePriceScorePart, row.FundamentalScorePart, row.RiskDisciplineScorePart, null),
            row.Close,
            row.Ma20,
            row.Ma60,
            row.Ma120,
            row.Atr14,
            row.RelativeStrengthScore,
            row.Pe,
            row.Pb,
            row.Roe,
            row.StopLossPrice,
            row.TargetPrice,
            row.RiskRewardRatio,
            row.Explanation);
    }

    /// <summary>
    /// 读取单只股票交易信号。
    /// </summary>
    public async Task<TradeSignal?> GetSignalAsync(string stockCode, DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken)
    {
        var snapshotVersionValue = snapshotVersion.ToValue();
        var row = await dbContext.StrategyTradeSignals.FirstOrDefaultAsync(item => item.StockCode == stockCode && item.TradeDate == tradeDate && item.SnapshotVersion == snapshotVersionValue, cancellationToken);
        return row is null ? null : new TradeSignal(
            row.TradeDate,
            row.StockCode,
            row.StockName,
            row.IndustryName,
            Enum.Parse<StrategyType>(row.StrategyType),
            row.EligibilityStatus,
            row.EligibilityReason,
            row.TotalScore,
            new CandidateScoreBreakdown(row.RelativeStrengthScorePart, row.TrendScorePart, row.VolumePriceScorePart, row.FundamentalScorePart, row.RiskDisciplineScorePart, null),
            row.TriggerPrice,
            row.StopLossPrice,
            row.TargetPrice,
            row.RiskRewardRatio,
            row.SuggestedCapital,
            row.EstimatedShares,
            row.Explanation,
            row.GeneratedAtUtc);
    }

    /// <summary>
    /// 读取单只股票近期已导入日线。
    /// </summary>
    public async Task<IReadOnlyList<DailyBar>> GetRecentImportedDailyBarsAsync(string stockCode, DateOnly tradeDate, int maxRows, CancellationToken cancellationToken)
    {
        return await dbContext.RawDailyBars
            .Where(item => item.StockCode == stockCode && item.TradeDate <= tradeDate)
            .OrderByDescending(item => item.TradeDate)
            .Take(maxRows)
            .OrderBy(item => item.TradeDate)
            .Select(static item => new DailyBar(item.StockCode, item.TradeDate, item.Open ?? 0m, item.High ?? 0m, item.Low ?? 0m, item.Close ?? 0m, item.Volume ?? 0L, item.Amount ?? 0m, item.PctChange, item.TurnoverRate))
            .ToListAsync(cancellationToken);
    }

    private static object[] BuildFinancialPageParameters(DateOnly tradeDate, string snapshotVersion, string? search, decimal? minRoe, int? skip, int? take)
    {
        var parameters = new List<object>
        {
            new MySqlParameter("@tradeDate", tradeDate),
            new MySqlParameter("@snapshotVersion", snapshotVersion)
        };
        if (!string.IsNullOrWhiteSpace(search))
        {
            parameters.Add(new MySqlParameter("@search", $"%{search}%"));
        }

        if (minRoe is not null)
        {
            parameters.Add(new MySqlParameter("@minRoe", minRoe.Value));
        }

        if (skip is not null)
        {
            parameters.Add(new MySqlParameter("@skip", skip.Value));
        }

        if (take is not null)
        {
            parameters.Add(new MySqlParameter("@take", take.Value));
        }

        return parameters.ToArray();
    }
}

/// <summary>
/// 基于 EF Core 的采集日志仓储实现。
/// </summary>
public sealed class EfIngestionLogRepository(StockDecisionDbContext dbContext) : IIngestionLogRepository
{
    /// <summary>
    /// 读取最近一次成功采集时间。
    /// </summary>
    public Task<DateTime?> GetLatestSuccessfulIngestionAtUtcAsync(CancellationToken cancellationToken)
    {
        return dbContext.DataIngestionLogs
            .Where(static item => item.IsComplete)
            .MaxAsync(static item => (DateTime?)item.CreatedAt, cancellationToken);
    }

    /// <summary>
    /// 读取最近的采集日志记录。
    /// </summary>
    public async Task<IReadOnlyList<IngestionLogEntry>> GetRecentRunsAsync(int take, CancellationToken cancellationToken)
    {
        var normalizedTake = Math.Clamp(take, 1, 50);
        return await dbContext.DataIngestionLogs
            .OrderByDescending(static item => item.CreatedAt)
            .Take(normalizedTake)
            .Select(static item => new IngestionLogEntry(
                item.TargetScope,
                item.IsComplete,
                item.IsSignalEligible,
                item.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}

/// <summary>
/// 基于 EF Core 的领域同步运行日志仓储实现。
/// </summary>
internal sealed class ScoringHistoryMetricsRow
{
    public string StockCode { get; set; } = string.Empty;
    public decimal Return10d { get; set; }
    public decimal AmountRatio1d { get; set; }
    public decimal Ma60Previous { get; set; }
}

internal sealed class IndicatorCalculationMetricsRow
{
    public string StockCode { get; set; } = string.Empty;
    public decimal Close { get; set; }
    public decimal Ma20 { get; set; }
    public decimal Ma60 { get; set; }
    public decimal Ma120 { get; set; }
    public decimal Atr14 { get; set; }
    public decimal Return20d { get; set; }
    public decimal Return60d { get; set; }
    public decimal Return10d { get; set; }
    public decimal AmountRatio1d { get; set; }
    public decimal PreviousMa20 { get; set; }
    public decimal Ma60Previous { get; set; }
    public decimal BreakoutClose { get; set; }
    public decimal? TurnoverRate { get; set; }
}

internal sealed class FinancialScorePageRow
{
    public string StockCode { get; set; } = string.Empty;
    public string StockName { get; set; } = string.Empty;
    public string? IndustryName { get; set; }
    public DateOnly? ReportDate { get; set; }
    public decimal TotalScore { get; set; }
    public decimal? Pe { get; set; }
    public decimal? Pb { get; set; }
    public decimal? Roe { get; set; }
    public decimal? RevenueYoy { get; set; }
    public decimal? NetProfitYoy { get; set; }
    public decimal? FreeFloatMarketCap { get; set; }
    public decimal? OperatingCashFlow { get; set; }
    public decimal? GrossMargin { get; set; }
    public decimal? DebtToAssetRatio { get; set; }
    public decimal? OperatingCashFlowNet { get; set; }
    public DateOnly? AnnouncementDate { get; set; }
    public string? DataSourcePriority { get; set; }
}

public sealed class EfDomainSyncRunRepository(StockDecisionDbContext dbContext) : IDomainSyncRunRepository
{
    /// <summary>
    /// 写入单次领域同步运行记录。
    /// </summary>
    public async Task AddRunAsync(DomainSyncRunEntry entry, CancellationToken cancellationToken)
    {
        dbContext.DomainSyncRuns.Add(new DomainSyncRunRow
        {
            JobName = entry.JobName,
            TriggerKind = entry.TriggerKind,
            SnapshotVersion = entry.SnapshotVersion,
            Status = entry.Status,
            DataUpdated = entry.DataUpdated,
            IsSignalEligible = entry.IsSignalEligible,
            EffectiveTradeDate = entry.EffectiveTradeDate,
            FinancialReportDate = entry.FinancialReportDate,
            StartedAt = entry.StartedAtUtc,
            FinishedAt = entry.FinishedAtUtc,
            Summary = entry.Summary,
            CreatedAt = entry.FinishedAtUtc ?? entry.StartedAtUtc
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// 读取最近的领域同步运行记录。
    /// </summary>
    public async Task<IReadOnlyList<DomainSyncRunEntry>> GetRecentRunsAsync(int take, CancellationToken cancellationToken)
    {
        var normalizedTake = Math.Clamp(take, 1, 50);
        return await dbContext.DomainSyncRuns
            .OrderByDescending(static item => item.CreatedAt)
            .Take(normalizedTake)
            .Select(static item => new DomainSyncRunEntry(
                item.JobName,
                item.TriggerKind,
                item.SnapshotVersion,
                item.Status,
                item.DataUpdated,
                item.IsSignalEligible,
                item.EffectiveTradeDate,
                item.FinancialReportDate,
                item.StartedAt,
                item.FinishedAt,
                item.Summary))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 读取最近一次成功完成的同步时间。
    /// </summary>
    public Task<DateTime?> GetLatestSuccessfulFinishedAtUtcAsync(CancellationToken cancellationToken)
    {
        return dbContext.DomainSyncRuns
            .Where(item => (item.Status == "Succeeded" || item.Status == "成功") && item.FinishedAt != null)
            .MaxAsync(static item => item.FinishedAt, cancellationToken);
    }

    /// <summary>
    /// 读取指定版本最近一次成功完成的同步时间。
    /// </summary>
    public Task<DateTime?> GetLatestSuccessfulFinishedAtUtcAsync(StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken)
    {
        var snapshotVersionValue = snapshotVersion.ToValue();
        return dbContext.DomainSyncRuns
            .Where(item => item.SnapshotVersion == snapshotVersionValue && (item.Status == "Succeeded" || item.Status == "成功") && item.FinishedAt != null)
            .MaxAsync(static item => item.FinishedAt, cancellationToken);
    }
}

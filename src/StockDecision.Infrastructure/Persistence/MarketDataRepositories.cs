using Microsoft.EntityFrameworkCore;
using StockDecision.Application.Contracts;
using StockDecision.Domain.Market;
using StockDecision.Domain.Strategy;

namespace StockDecision.Infrastructure.Persistence;

/// <summary>
/// 基于 EF Core 的原始市场数据仓储实现。
/// </summary>
public sealed class EfRawMarketDataRepository(StockDecisionDbContext dbContext) : IRawMarketDataRepository
{
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
                .Where(static item => string.IsNullOrWhiteSpace(item.IndustryName) || item.ListDate is null)
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

            var amountHistoryFastPath = await dbContext.RawDailyBars
                .AsNoTracking()
                .Where(item => item.TradeDate <= tradeDate)
                .GroupBy(static item => item.StockCode)
                .Select(group => new
                {
                    StockCode = group.Key,
                    AverageAmount20d = group
                        .OrderByDescending(item => item.TradeDate)
                        .Take(20)
                        .Average(item => item.Amount ?? 0m)
                })
                .ToDictionaryAsync(static item => item.StockCode, static item => (decimal?)item.AverageAmount20d, cancellationToken);

            var latestDailyFastPath = await dbContext.RawDailyBars
                .AsNoTracking()
                .Where(item => item.TradeDate == tradeDate)
                .ToDictionaryAsync(static item => item.StockCode, cancellationToken);

            var latestFinancialFastPath = await dbContext.RawFinancialSnapshots
                .AsNoTracking()
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

                var resolvedIndustryName = string.IsNullOrWhiteSpace(item.IndustryName)
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

        var amountHistory = await dbContext.RawDailyBars
            .Where(item => item.TradeDate <= tradeDate)
            .GroupBy(static item => item.StockCode)
            .Select(group => new
            {
                StockCode = group.Key,
                AverageAmount20d = group
                    .OrderByDescending(item => item.TradeDate)
                    .Take(20)
                    .Average(item => item.Amount ?? 0m)
            })
            .ToDictionaryAsync(static item => item.StockCode, static item => (decimal?)item.AverageAmount20d, cancellationToken);

        var latestDaily = await dbContext.RawDailyBars
            .Where(item => item.TradeDate == tradeDate)
            .ToDictionaryAsync(static item => item.StockCode, cancellationToken);

        var latestFinancial = await dbContext.RawFinancialSnapshots
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

            var resolvedIndustryName = string.IsNullOrWhiteSpace(item.IndustryName)
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
        var rows = await dbContext.RawFinancialSnapshots
            .Select(static item => new FinancialSnapshot(
                item.StockCode,
                item.ReportDate,
                item.Pe,
                item.Pb,
                item.Roe,
                item.RevenueYoy,
                item.NetProfitYoy,
                item.FreeFloatMarketCap))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(static item => item.StockCode)
            .Select(static group => group.OrderByDescending(item => item.ReportDate).First())
            .ToList();
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
    /// <summary>
    /// 读取已导入快照的最新交易日。
    /// </summary>
    public Task<DateOnly?> GetLatestImportedTradeDateAsync(CancellationToken cancellationToken)
    {
        return dbContext.MarketDailyBars.MaxAsync(static item => (DateOnly?)item.TradeDate, cancellationToken);
    }

    /// <summary>
    /// 用同一交易日的全量数据替换领域快照。
    /// </summary>
    public async Task ReplaceMarketSnapshotAsync(
        DateOnly tradeDate,
        IReadOnlyList<StockProfile> stocks,
        IReadOnlyList<DailyBar> dailyBars,
        IReadOnlyList<MarketIndexBar> indexBars,
        IReadOnlyList<IndustryDailyStat> industries,
        IReadOnlyList<FinancialSnapshot> financials,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        dbContext.MarketStockProfiles.RemoveRange(dbContext.MarketStockProfiles.Where(item => item.TradeDate == tradeDate));
        dbContext.MarketDailyBars.RemoveRange(dbContext.MarketDailyBars.Where(item => item.TradeDate == tradeDate));
        dbContext.MarketIndexBars.RemoveRange(dbContext.MarketIndexBars.Where(item => item.TradeDate == tradeDate));
        dbContext.MarketIndustryDailyStats.RemoveRange(dbContext.MarketIndustryDailyStats.Where(item => item.TradeDate == tradeDate));

        // 财务快照按“最近一期全量覆盖”维护，因此这里不按 tradeDate 删除，而是整表替换。
        dbContext.MarketFinancialSnapshots.RemoveRange(dbContext.MarketFinancialSnapshots);
        await dbContext.SaveChangesAsync(cancellationToken);

        dbContext.MarketStockProfiles.AddRange(stocks.Select(item => new MarketStockProfileEntity
        {
            StockCode = item.StockCode,
            TradeDate = tradeDate,
            StockName = item.StockName,
            IndustryName = item.IndustryName,
            IsActive = item.IsActive,
            IsSt = item.IsSt,
            IsDelistingRisk = item.IsDelistingRisk,
            ListDate = item.ListDate,
            LatestPrice = item.LatestPrice,
            Pe = item.Pe,
            Pb = item.Pb,
            FreeFloatMarketCap = item.FreeFloatMarketCap,
            TurnoverRate = item.TurnoverRate,
            AverageAmount20d = item.AverageAmount20d
        }));
        dbContext.MarketDailyBars.AddRange(dailyBars.Select(item => new MarketDailyBarEntity
        {
            StockCode = item.StockCode,
            TradeDate = item.TradeDate,
            Open = item.Open,
            High = item.High,
            Low = item.Low,
            Close = item.Close,
            Volume = item.Volume,
            Amount = item.Amount,
            PctChange = item.PctChange,
            TurnoverRate = item.TurnoverRate
        }));
        dbContext.MarketIndexBars.AddRange(indexBars.Select(item => new MarketIndexBarEntity
        {
            IndexCode = item.IndexCode,
            IndexName = item.IndexName,
            TradeDate = item.TradeDate,
            Close = item.Close
        }));
        dbContext.MarketIndustryDailyStats.AddRange(industries.Select(item => new MarketIndustryDailyStatEntity
        {
            IndustryCode = item.IndustryCode,
            IndustryName = item.IndustryName,
            TradeDate = item.TradeDate,
            PctChange20d = item.PctChange20d,
            Rank20d = item.Rank20d
        }));
        dbContext.MarketFinancialSnapshots.AddRange(financials.Select(item => new MarketFinancialSnapshotEntity
        {
            StockCode = item.StockCode,
            ReportDate = item.ReportDate,
            Pe = item.Pe,
            Pb = item.Pb,
            Roe = item.Roe,
            RevenueYoy = item.RevenueYoy,
            NetProfitYoy = item.NetProfitYoy,
            FreeFloatMarketCap = item.FreeFloatMarketCap
        }));

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

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

    /// <summary>
    /// 读取单只股票指定交易日后的前瞻日线。
    /// </summary>
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
            .Where(item => set.Contains(item.StockCode))
            .GroupBy(item => item.StockCode)
            .Select(static group => group
                .OrderByDescending(item => item.TradeDate)
                .First())
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            static item => item.StockCode,
            static item => new StockProfile(item.StockCode, item.StockName, item.IndustryName, item.IsActive, item.IsSt, item.IsDelistingRisk, item.ListDate, item.LatestPrice, item.Pe, item.Pb, item.FreeFloatMarketCap, item.TurnoverRate, item.AverageAmount20d, item.TradeDate));
    }

    /// <summary>
    /// 批量读取最新财务快照。
    /// </summary>
    public async Task<IReadOnlyDictionary<string, FinancialSnapshot>> GetLatestFinancialsByCodesAsync(IEnumerable<string> stockCodes, CancellationToken cancellationToken)
    {
        var set = stockCodes.Distinct().ToList();
        var rows = await dbContext.MarketFinancialSnapshots
            .Where(item => set.Contains(item.StockCode))
            .ToListAsync(cancellationToken);

        return rows.GroupBy(static item => item.StockCode)
            .Select(static group => group.OrderByDescending(item => item.ReportDate).First())
            .ToDictionary(
                static item => item.StockCode,
                static item => new FinancialSnapshot(item.StockCode, item.ReportDate, item.Pe, item.Pb, item.Roe, item.RevenueYoy, item.NetProfitYoy, item.FreeFloatMarketCap));
    }

    /// <summary>
    /// 读取全部最新财务快照。
    /// </summary>
    public async Task<IReadOnlyList<FinancialSnapshot>> GetLatestFinancialSnapshotsAsync(CancellationToken cancellationToken)
    {
        return await dbContext.MarketFinancialSnapshots
            .GroupBy(item => item.StockCode)
            .Select(static group => group
                .OrderByDescending(item => item.ReportDate)
                .Select(item => new FinancialSnapshot(item.StockCode, item.ReportDate, item.Pe, item.Pb, item.Roe, item.RevenueYoy, item.NetProfitYoy, item.FreeFloatMarketCap))
                .First())
            .ToListAsync(cancellationToken);
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
        dbContext.StrategyIndicatorSnapshots.RemoveRange(dbContext.StrategyIndicatorSnapshots.Where(item => item.TradeDate == tradeDate && item.SnapshotVersion == snapshotVersionValue));
        await dbContext.SaveChangesAsync(cancellationToken);
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

    /// <summary>
    /// 写入市场环境快照。
    /// </summary>
    public async Task UpsertMarketRegimeAsync(StrategySnapshotVersion snapshotVersion, MarketRegimeSnapshot regime, CancellationToken cancellationToken)
    {
        var snapshotVersionValue = snapshotVersion.ToValue();
        dbContext.StrategyMarketRegimes.RemoveRange(dbContext.StrategyMarketRegimes.Where(item => item.TradeDate == regime.TradeDate && item.SnapshotVersion == snapshotVersionValue));
        await dbContext.SaveChangesAsync(cancellationToken);
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
        dbContext.StrategyCandidates.RemoveRange(dbContext.StrategyCandidates.Where(item => item.TradeDate == tradeDate && item.SnapshotVersion == snapshotVersionValue));
        await dbContext.SaveChangesAsync(cancellationToken);
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

    /// <summary>
    /// 写入交易信号列表。
    /// </summary>
    public async Task UpsertSignalsAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, IReadOnlyList<TradeSignal> signals, CancellationToken cancellationToken)
    {
        var snapshotVersionValue = snapshotVersion.ToValue();
        dbContext.StrategyTradeSignals.RemoveRange(dbContext.StrategyTradeSignals.Where(item => item.TradeDate == tradeDate && item.SnapshotVersion == snapshotVersionValue));
        await dbContext.SaveChangesAsync(cancellationToken);
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
                new CandidateScoreBreakdown(item.RelativeStrengthScorePart, item.TrendScorePart, item.VolumePriceScorePart, item.FundamentalScorePart, null),
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
                new CandidateScoreBreakdown(item.RelativeStrengthScorePart, item.TrendScorePart, item.VolumePriceScorePart, item.FundamentalScorePart, null),
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
        return row is null ? null : new StockProfile(row.StockCode, row.StockName, row.IndustryName, row.IsActive, row.IsSt, row.IsDelistingRisk, row.ListDate, row.LatestPrice, row.Pe, row.Pb, row.FreeFloatMarketCap, row.TurnoverRate, row.AverageAmount20d, row.TradeDate);
    }

    /// <summary>
    /// 读取单只股票最近财务快照。
    /// </summary>
    public async Task<FinancialSnapshot?> GetLatestFinancialAsync(string stockCode, CancellationToken cancellationToken)
    {
        var row = await dbContext.MarketFinancialSnapshots
            .Where(item => item.StockCode == stockCode)
            .OrderByDescending(item => item.ReportDate)
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : new FinancialSnapshot(row.StockCode, row.ReportDate, row.Pe, row.Pb, row.Roe, row.RevenueYoy, row.NetProfitYoy, row.FreeFloatMarketCap);
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
            new CandidateScoreBreakdown(row.RelativeStrengthScorePart, row.TrendScorePart, row.VolumePriceScorePart, row.FundamentalScorePart, null),
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
            new CandidateScoreBreakdown(row.RelativeStrengthScorePart, row.TrendScorePart, row.VolumePriceScorePart, row.FundamentalScorePart, null),
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

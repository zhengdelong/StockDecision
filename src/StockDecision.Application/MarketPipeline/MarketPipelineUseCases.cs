using StockDecision.Application.Contracts;
using StockDecision.Domain.Market;
using StockDecision.Domain.Strategy;

namespace StockDecision.Application.MarketPipeline;

internal static class StrategySnapshotVersionResolver
{
    /// <summary>
    /// 将查询参数解析为快照版本。
    /// </summary>
    internal static StrategySnapshotVersion Resolve(string? rawValue)
    {
        return StrategySnapshotVersionCodec.ParseOrDefault(rawValue);
    }

    /// <summary>
    /// 根据当前上海时间推断自动同步应写入的版本。
    /// </summary>
    internal static StrategySnapshotVersion ResolveForAutomation(DateTimeOffset now)
    {
        _ = now;
        return StrategySnapshotVersion.EndOfDayFinal;
    }

}

/// <summary>
/// 确保领域层存在最新市场快照，并在必要时补做指标和信号计算。
/// </summary>
public sealed class EnsureLatestMarketSnapshotUseCase(
    IRawMarketDataRepository rawRepository,
    IMarketDataRepository marketRepository,
    IIngestionLogRepository ingestionLogRepository)
{
    public async Task<MarketSnapshotSyncState> InspectAsync(CancellationToken cancellationToken = default)
    {
        var latestRawTradeDate = await rawRepository.GetLatestTradeDateAsync(cancellationToken);
        var latestImportedTradeDate = await marketRepository.GetLatestImportedTradeDateAsync(cancellationToken);
        var latestRawFinancialReportDate = await rawRepository.GetLatestFinancialReportDateAsync(cancellationToken);
        var latestImportedFinancialReportDate = await marketRepository.GetLatestImportedFinancialReportDateAsync(cancellationToken);
        var effectiveTradeDate = latestRawTradeDate ?? latestImportedTradeDate;
        var hasTradeDateGap = latestRawTradeDate is not null && latestImportedTradeDate != latestRawTradeDate.Value;
        var hasFinancialGap = latestRawFinancialReportDate is not null && latestImportedFinancialReportDate != latestRawFinancialReportDate.Value;

        return new MarketSnapshotSyncState(
            effectiveTradeDate,
            latestRawTradeDate,
            latestImportedTradeDate,
            latestRawFinancialReportDate,
            latestImportedFinancialReportDate,
            hasTradeDateGap || hasFinancialGap,
            hasTradeDateGap,
            hasFinancialGap);
    }

    /// <summary>
    /// 将原始层最新交易日同步到领域快照层。
    /// </summary>
    public async Task<DateOnly?> ExecuteAsync(
        StrategySnapshotVersion snapshotVersion = StrategySnapshotVersion.EndOfDayFinal,
        CancellationToken cancellationToken = default)
    {
        var syncState = await InspectAsync(cancellationToken);
        if (syncState.LatestRawTradeDate is null)
        {
            return null;
        }

        if (!syncState.RequiresSync)
        {
            return syncState.EffectiveTradeDate;
        }

        var syncTradeDate = syncState.LatestRawTradeDate.Value;
        var stocks = await rawRepository.GetLatestStockProfilesAsync(syncTradeDate, cancellationToken);
        var dailyBars = await rawRepository.GetDailyBarsByTradeDateAsync(syncTradeDate, cancellationToken);
        var indices = await rawRepository.GetIndexBarsByTradeDateAsync(syncTradeDate, cancellationToken);
        var industries = await rawRepository.GetIndustryStatsByTradeDateAsync(syncTradeDate, cancellationToken);
        var financials = await rawRepository.GetLatestFinancialSnapshotsAsync(cancellationToken);

        await marketRepository.ReplaceMarketSnapshotAsync(
            syncTradeDate,
            stocks,
            dailyBars,
            indices,
            industries,
            financials,
            cancellationToken);

        await CalculateIndicatorsAndSignalsAsync(syncTradeDate, snapshotVersion, cancellationToken);

        // 当前首页仍需要展示最近采集时间，这里保留依赖，后续扩展写入逻辑时直接接入。
        _ = ingestionLogRepository;
        return syncTradeDate;
    }

    /// <summary>
    /// 强制基于最新原始层数据重建一次领域快照。
    /// </summary>
    public async Task<DateOnly?> RebuildLatestAsync(
        StrategySnapshotVersion snapshotVersion = StrategySnapshotVersion.EndOfDayFinal,
        CancellationToken cancellationToken = default)
    {
        var syncState = await InspectAsync(cancellationToken);
        if (syncState.LatestRawTradeDate is null)
        {
            return null;
        }

        var syncTradeDate = syncState.LatestRawTradeDate.Value;
        var stocks = await rawRepository.GetLatestStockProfilesAsync(syncTradeDate, cancellationToken);
        var dailyBars = await rawRepository.GetDailyBarsByTradeDateAsync(syncTradeDate, cancellationToken);
        var indices = await rawRepository.GetIndexBarsByTradeDateAsync(syncTradeDate, cancellationToken);
        var industries = await rawRepository.GetIndustryStatsByTradeDateAsync(syncTradeDate, cancellationToken);
        var financials = await rawRepository.GetLatestFinancialSnapshotsAsync(cancellationToken);

        await marketRepository.ReplaceMarketSnapshotAsync(
            syncTradeDate,
            stocks,
            dailyBars,
            indices,
            industries,
            financials,
            cancellationToken);

        await CalculateIndicatorsAndSignalsAsync(syncTradeDate, snapshotVersion, cancellationToken);

        _ = ingestionLogRepository;
        return syncTradeDate;
    }

    /// <summary>
    /// 计算指标、市场环境、候选股与交易信号。
    /// </summary>
    private async Task CalculateIndicatorsAndSignalsAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken)
    {
        var stockCodes = await marketRepository.GetActiveStockCodesAsync(cancellationToken);
        var indicatorSnapshots = new List<IndicatorSnapshot>(stockCodes.Count);
        foreach (var stockCode in stockCodes)
        {
            var history = await marketRepository.GetDailyBarHistoryAsync(stockCode, tradeDate, 140, cancellationToken);
            if (history.Count < 120)
            {
                continue;
            }

            var ordered = history.OrderBy(static item => item.TradeDate).ToList();
            var latest = ordered[^1];
            var ma20 = IndicatorMath.CalculateSimpleMovingAverage(ordered, 20);
            var ma60 = IndicatorMath.CalculateSimpleMovingAverage(ordered, 60);
            var ma120 = IndicatorMath.CalculateSimpleMovingAverage(ordered, 120);
            var atr14 = IndicatorMath.CalculateAtr14(ordered);
            var return20 = IndicatorMath.CalculateReturn(ordered, 20);
            var return60 = IndicatorMath.CalculateReturn(ordered, 60);
            var distanceToMa20 = ma20 == 0m ? 0m : ((latest.Close - ma20) / ma20) * 100m;

            // 用 10 个交易日前的 MA20 对比当前 MA20，判断均线是否仍在抬升，减少短期噪声。
            var previousMa20 = ordered.Count > 30
                ? IndicatorMath.CalculateSimpleMovingAverage(ordered.Take(ordered.Count - 10).ToList(), 20)
                : 0m;

            // 20 日突破使用近 20 根收盘价高点，而不是盘中最高价，避免长上影造成误判。
            var breakoutClose = ordered.TakeLast(20).Max(static item => item.Close);

            indicatorSnapshots.Add(new IndicatorSnapshot(
                stockCode,
                tradeDate,
                latest.Close,
                ma20,
                ma60,
                ma120,
                atr14,
                return20,
                return60,
                return20,
                latest.Close >= breakoutClose,
                ma20 > previousMa20,
                ma20 > ma60 && ma60 > ma120,
                Math.Round(distanceToMa20, 4, MidpointRounding.AwayFromZero),
                latest.TurnoverRate));
        }

        await marketRepository.UpsertIndicatorSnapshotsAsync(tradeDate, snapshotVersion, indicatorSnapshots, cancellationToken);

        var indexHistory = await marketRepository.GetIndexBarHistoryAsync(tradeDate, 30, cancellationToken);
        var regime = MarketRegimePolicy.Evaluate(
            tradeDate,
            indexHistory
                .GroupBy(static item => item.IndexCode)
                .ToDictionary(
                    static group => group.Key,
                    static group => (IReadOnlyList<MarketIndexBar>)group.OrderBy(item => item.TradeDate).ToList()));
        await marketRepository.UpsertMarketRegimeAsync(snapshotVersion, regime, cancellationToken);

        var profiles = await marketRepository.GetStockProfilesByCodesAsync(indicatorSnapshots.Select(static item => item.StockCode), cancellationToken);
        var financials = await marketRepository.GetLatestFinancialsByCodesAsync(indicatorSnapshots.Select(static item => item.StockCode), cancellationToken);
        var industries = await marketRepository.GetIndustryStatsByNamesAsync(
            tradeDate,
            profiles.Values.Select(static item => item.IndustryName).Distinct().ToList(),
            cancellationToken);

        var candidates = new List<CandidateStock>();
        foreach (var indicator in indicatorSnapshots)
        {
            if (!profiles.TryGetValue(indicator.StockCode, out var profile))
            {
                continue;
            }

            financials.TryGetValue(indicator.StockCode, out var financial);
            industries.TryGetValue(profile.IndustryName ?? string.Empty, out var industry);
            var candidate = CandidatePolicy.Evaluate(tradeDate, profile, indicator, financial, industry, regime);
            if (candidate is not null)
            {
                candidates.Add(candidate);
            }
        }

        await marketRepository.UpsertCandidatesAsync(tradeDate, snapshotVersion, candidates, cancellationToken);

        var signals = candidates
            .Select(candidate => CandidatePolicy.BuildSignal(candidate, regime))
            .Where(static item => item is not null)
            .Cast<TradeSignal>()
            .ToList();
        await marketRepository.UpsertSignalsAsync(tradeDate, snapshotVersion, signals, cancellationToken);
    }
}

/// <summary>
/// 读取首页仪表盘数据。
/// </summary>
public sealed class GetDashboardUseCase(
    EnsureLatestMarketSnapshotUseCase ensureLatestSnapshot,
    IMarketDataRepository marketRepository,
    IIngestionLogRepository ingestionLogRepository)
{
    /// <summary>
    /// 返回首页仪表盘响应。
    /// </summary>
    public async Task<DashboardResponse> ExecuteAsync(
        StrategySnapshotVersion snapshotVersion = StrategySnapshotVersion.EndOfDayFinal,
        CancellationToken cancellationToken = default)
    {
        var tradeDate = await ensureLatestSnapshot.ExecuteAsync(snapshotVersion, cancellationToken);
        if (tradeDate is null)
        {
            return new DashboardResponse(null, snapshotVersion.ToValue(), snapshotVersion.ToDisplayName(), false, false, "暂无数据", 0, 0, null, []);
        }

        var candidates = await marketRepository.GetCandidatesAsync(tradeDate.Value, snapshotVersion, cancellationToken);
        var signals = await marketRepository.GetSignalsAsync(tradeDate.Value, snapshotVersion, cancellationToken);
        var regime = await marketRepository.GetMarketRegimeAsync(tradeDate.Value, snapshotVersion, cancellationToken);
        var latestIngestion = await ingestionLogRepository.GetLatestSuccessfulIngestionAtUtcAsync(cancellationToken);

        return new DashboardResponse(
            tradeDate.Value,
            snapshotVersion.ToValue(),
            snapshotVersion.ToDisplayName(),
            true,
            regime?.IsSignalEligible ?? false,
            MarketResponseMapper.FormatMarketRegime(regime?.Regime),
            candidates.Count,
            signals.Count,
            latestIngestion,
            [
                new DashboardMetricResponse("tradeDate", "交易日", tradeDate.Value.ToString("yyyy-MM-dd"), "neutral"),
                new DashboardMetricResponse("snapshotVersion", "结果版本", snapshotVersion.ToDisplayName(), snapshotVersion == StrategySnapshotVersion.EndOfDayFinal ? "positive" : "warning"),
                new DashboardMetricResponse("regime", "市场环境", MarketResponseMapper.FormatMarketRegime(regime?.Regime), regime?.IsSignalEligible == true ? "positive" : "warning"),
                new DashboardMetricResponse("candidates", "候选池", candidates.Count.ToString(), "neutral"),
                new DashboardMetricResponse("signals", "交易信号", signals.Count.ToString(), signals.Count > 0 ? "positive" : "neutral")
            ]);
    }
}

/// <summary>
/// 读取候选股列表。
/// </summary>
public sealed class GetCandidatesUseCase(
    EnsureLatestMarketSnapshotUseCase ensureLatestSnapshot,
    IMarketDataRepository marketRepository)
{
    /// <summary>
    /// 返回指定交易日或最新交易日的候选股分页列表。
    /// </summary>
    public async Task<PagedResponse<CandidateListItemResponse>> ExecuteAsync(CandidateListQuery query, CancellationToken cancellationToken = default)
    {
        var snapshotVersion = StrategySnapshotVersionResolver.Resolve(query.SnapshotVersion);
        var resolvedDate = query.Date ?? await ensureLatestSnapshot.ExecuteAsync(snapshotVersion, cancellationToken);
        if (resolvedDate is null)
        {
            return new PagedResponse<CandidateListItemResponse>([], 1, MarketListQueryPaging.NormalizePageSize(query.PageSize), 0);
        }

        var candidates = await marketRepository.GetCandidatesAsync(resolvedDate.Value, snapshotVersion, cancellationToken);
        var items = candidates
            .Select(static item => new CandidateListItemResponse(
                item.StockCode,
                item.StockName,
                item.IndustryName,
                MarketResponseMapper.FormatCandidateGrade(item.Grade),
                MarketResponseMapper.FormatStrategyType(item.StrategyType),
                item.IsTradable,
                item.TotalScore,
                item.ScoreBreakdown,
                item.Close,
                item.Ma20,
                item.Ma60,
                item.Atr14,
                item.RelativeStrengthScore,
                item.StopLossPrice,
                item.TargetPrice,
                item.RiskRewardRatio,
                item.Explanation,
                MarketResponseMapper.BuildScoreRuleDetails(item.ScoreBreakdown)))
            .ToList();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var keyword = query.Search.Trim();
            items = items.Where(item =>
                    item.StockCode.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    item.StockName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    (item.IndustryName?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        if (query.MinScore is decimal minScore)
        {
            items = items.Where(item => item.TotalScore >= minScore).ToList();
        }

        if (query.OnlyTradable == true)
        {
            items = items.Where(item => item.IsTradable).ToList();
        }

        items = (query.SortBy ?? "score").ToLowerInvariant() switch
        {
            "rr" => items.OrderByDescending(item => item.RiskRewardRatio).ToList(),
            "close" => items.OrderByDescending(item => item.Close).ToList(),
            _ => items.OrderByDescending(item => item.TotalScore).ToList()
        };

        return MarketListQueryPaging.ToPagedResponse(items, query.Page, query.PageSize);
    }
}

/// <summary>
/// 读取最新交易日的可执行信号。
/// </summary>
public sealed class GetTodaySignalsUseCase(
    EnsureLatestMarketSnapshotUseCase ensureLatestSnapshot,
    IMarketDataRepository marketRepository)
{
    /// <summary>
    /// 返回指定交易日或最新交易日的交易信号分页列表。
    /// </summary>
    public async Task<PagedResponse<SignalListItemResponse>> ExecuteAsync(SignalListQuery query, CancellationToken cancellationToken = default)
    {
        var snapshotVersion = StrategySnapshotVersionResolver.Resolve(query.SnapshotVersion);
        var resolvedDate = query.Date ?? await ensureLatestSnapshot.ExecuteAsync(snapshotVersion, cancellationToken);
        if (resolvedDate is null)
        {
            return new PagedResponse<SignalListItemResponse>([], 1, MarketListQueryPaging.NormalizePageSize(query.PageSize), 0);
        }

        var signals = await marketRepository.GetSignalsAsync(resolvedDate.Value, snapshotVersion, cancellationToken);
        var items = signals
            .Select(static item => new SignalListItemResponse(
                item.StockCode,
                item.StockName,
                item.IndustryName,
                MarketResponseMapper.FormatStrategyType(item.StrategyType),
                item.TotalScore,
                item.ScoreBreakdown,
                item.TriggerPrice,
                item.StopLossPrice,
                item.TargetPrice,
                item.RiskRewardRatio,
                item.SuggestedCapital,
                item.EstimatedShares,
                item.Explanation,
                item.GeneratedAtUtc))
            .ToList();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var keyword = query.Search.Trim();
            items = items.Where(item =>
                    item.StockCode.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    item.StockName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    (item.IndustryName?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        items = (query.SortBy ?? "score").ToLowerInvariant() switch
        {
            "rr" => items.OrderByDescending(item => item.RiskRewardRatio).ToList(),
            "capital" => items.OrderByDescending(item => item.SuggestedCapital).ToList(),
            _ => items.OrderByDescending(item => item.TotalScore).ToList()
        };

        return MarketListQueryPaging.ToPagedResponse(items, query.Page, query.PageSize);
    }
}

/// <summary>
/// 读取行业强度与行业内候选/信号分布。
/// </summary>
public sealed class GetIndustriesUseCase(
    EnsureLatestMarketSnapshotUseCase ensureLatestSnapshot,
    IMarketDataRepository marketRepository)
{
    /// <summary>
    /// 返回指定交易日或最新交易日的行业分页列表。
    /// </summary>
    public async Task<PagedResponse<IndustryListItemResponse>> ExecuteAsync(IndustryListQuery query, CancellationToken cancellationToken = default)
    {
        var snapshotVersion = StrategySnapshotVersionResolver.Resolve(query.SnapshotVersion);
        var resolvedDate = query.Date ?? await ensureLatestSnapshot.ExecuteAsync(snapshotVersion, cancellationToken);
        if (resolvedDate is null)
        {
            return new PagedResponse<IndustryListItemResponse>([], 1, MarketListQueryPaging.NormalizePageSize(query.PageSize), 0);
        }

        var industryStats = await marketRepository.GetIndustryStatsAsync(resolvedDate.Value, cancellationToken);
        var candidates = await marketRepository.GetCandidatesAsync(resolvedDate.Value, snapshotVersion, cancellationToken);
        var signals = await marketRepository.GetSignalsAsync(resolvedDate.Value, snapshotVersion, cancellationToken);

        var candidateGroups = candidates
            .Where(static item => !string.IsNullOrWhiteSpace(item.IndustryName))
            .GroupBy(item => item.IndustryName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => new
                {
                    Count = group.Count(),
                    TopScore = group.Max(item => item.TotalScore)
                },
                StringComparer.OrdinalIgnoreCase);

        var signalGroups = signals
            .Where(static item => !string.IsNullOrWhiteSpace(item.IndustryName))
            .GroupBy(item => item.IndustryName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => new
                {
                    Count = group.Count(),
                    TopScore = group.Max(item => item.TotalScore)
                },
                StringComparer.OrdinalIgnoreCase);

        var industryNames = industryStats.Select(static item => item.IndustryName)
            .Concat(candidateGroups.Keys)
            .Concat(signalGroups.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var industryStatsByName = industryStats.ToDictionary(static item => item.IndustryName, StringComparer.OrdinalIgnoreCase);
        var items = industryNames
            .Select(industryName =>
            {
                industryStatsByName.TryGetValue(industryName, out var industry);
                candidateGroups.TryGetValue(industryName, out var candidateGroup);
                signalGroups.TryGetValue(industryName, out var signalGroup);

                return new IndustryListItemResponse(
                    industry?.IndustryCode ?? string.Empty,
                    industryName,
                    resolvedDate.Value,
                    industry?.PctChange20d ?? 0m,
                    industry?.Rank20d ?? int.MaxValue,
                    candidateGroup?.Count ?? 0,
                    signalGroup?.Count ?? 0,
                    candidateGroup?.TopScore,
                    signalGroup?.TopScore);
            })
            .ToList();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var keyword = query.Search.Trim();
            items = items.Where(item =>
                    item.IndustryCode.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    item.IndustryName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        items = (query.SortBy ?? "strength").ToLowerInvariant() switch
        {
            "rank" => items.OrderBy(item => item.Rank20d).ThenByDescending(item => item.PctChange20d).ToList(),
            "candidates" => items.OrderByDescending(item => item.CandidateCount).ThenByDescending(item => item.TopCandidateScore ?? 0m).ToList(),
            "signals" => items.OrderByDescending(item => item.SignalCount).ThenByDescending(item => item.TopSignalScore ?? 0m).ToList(),
            _ => items.OrderByDescending(item => item.PctChange20d).ThenBy(item => item.Rank20d).ToList()
        };

        return MarketListQueryPaging.ToPagedResponse(items, query.Page, query.PageSize);
    }
}

/// <summary>
/// 读取财务质量列表。
/// </summary>
public sealed class GetFinancialsUseCase(IMarketDataRepository marketRepository)
{
    /// <summary>
    /// 返回最新财务快照分页列表。
    /// </summary>
    public async Task<PagedResponse<FinancialListItemResponse>> ExecuteAsync(FinancialListQuery query, CancellationToken cancellationToken = default)
    {
        var financials = await marketRepository.GetLatestFinancialSnapshotsAsync(cancellationToken);
        var profiles = await marketRepository.GetStockProfilesByCodesAsync(financials.Select(static item => item.StockCode), cancellationToken);

        var items = financials
            .Select(financial =>
            {
                profiles.TryGetValue(financial.StockCode, out var profile);
                return new FinancialListItemResponse(
                    financial.StockCode,
                    profile?.StockName ?? financial.StockCode,
                    profile?.IndustryName,
                    financial.ReportDate,
                    financial.Pe,
                    financial.Pb,
                    financial.Roe,
                    financial.RevenueYoy,
                    financial.NetProfitYoy,
                    financial.FreeFloatMarketCap);
            })
            .ToList();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var keyword = query.Search.Trim();
            items = items.Where(item =>
                    item.StockCode.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    item.StockName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    (item.IndustryName?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        if (query.MinRoe is decimal minRoe)
        {
            items = items.Where(item => (item.Roe ?? decimal.MinValue) >= minRoe).ToList();
        }

        if (query.PositiveGrowthOnly == true)
        {
            items = items.Where(item => (item.RevenueYoy ?? 0m) > 0m && (item.NetProfitYoy ?? 0m) > 0m).ToList();
        }

        items = (query.SortBy ?? "roe").ToLowerInvariant() switch
        {
            "revenue" => items.OrderByDescending(item => item.RevenueYoy ?? decimal.MinValue).ToList(),
            "profit" => items.OrderByDescending(item => item.NetProfitYoy ?? decimal.MinValue).ToList(),
            "marketcap" => items.OrderByDescending(item => item.FreeFloatMarketCap ?? decimal.MinValue).ToList(),
            _ => items.OrderByDescending(item => item.Roe ?? decimal.MinValue).ToList()
        };

        return MarketListQueryPaging.ToPagedResponse(items, query.Page, query.PageSize);
    }
}

/// <summary>
/// 读取策略解释页数据。
/// </summary>
public sealed class GetStrategyExplanationUseCase
{
    /// <summary>
    /// 返回当前策略版本的结构化说明。
    /// </summary>
    public StrategyExplanationResponse Execute()
    {
        return new StrategyExplanationResponse(
            "a-share-20k-v1",
            [
                new StrategyRuleSectionResponse("候选池范围", [
                    "剔除停牌、ST、退市风险以及上市未满 365 天的股票。",
                    "要求股价站上 MA60，ATR 波动率不超过 7%，最新价格位于 5 到 80 元之间。",
                    "要求 20 日平均成交额不低于 2 亿元。"
                ]),
                new StrategyRuleSectionResponse("市场环境", [
                    "使用三大指数判断环境，收盘站上 MA20 且 MA20 上行为有效确认。",
                    "3 个确认为强势，2 个确认可交易，1 个确认弱机会，0 个确认不交易。"
                ]),
                new StrategyRuleSectionResponse("可执行信号门槛", [
                    "候选股总分至少达到 60 分。",
                    "策略类型必须是突破或回踩 MA20。",
                    "市场环境不能是不交易，且风险收益比至少为 2。"
                ])
            ],
            [
                new StrategyScoreDimensionResponse("相对强弱", 30m, [
                    "20日收益率为正，加 10 分。",
                    "60日收益率为正，加 8 分。",
                    "相对强弱分为正，加 7 分。",
                    "距离 MA20 在 10% 以内，加 5 分。"
                ]),
                new StrategyScoreDimensionResponse("趋势", 25m, [
                    "收盘价站上 MA60，加 6 分。",
                    "MA20 > MA60 > MA120 的多头排列，加 8 分。",
                    "收盘价站上 MA20，加 4 分。",
                    "MA20 保持上行，加 4 分。",
                    "距离 MA20 在 10% 以内，加 3 分。"
                ]),
                new StrategyScoreDimensionResponse("量价", 25m, [
                    "出现 20 日收盘突破，加 7 分。",
                    "换手率位于 2% 到 8%，加 4 分。",
                    "20 日平均成交额超过 5 亿元加 4 分，否则加 2 分。",
                    "行业 20 日强度排名前 10，加 5 分。"
                ]),
                new StrategyScoreDimensionResponse("基本面", 20m, [
                    "净利润同比为正，加 5 分。",
                    "营收同比为正，加 4 分。",
                    "ROE 不低于 8，加 5 分。",
                    "PE 位于 0 到 80 之间，加 3 分。",
                    "PB 位于 0 到 8 之间，加 3 分。"
                ])
            ],
            [
                new StrategyExecutionRuleResponse("止损", "回踩策略取 max(MA20 * 0.98, close * 0.96)；突破策略取 max(close * 0.97, close * 0.96)。"),
                new StrategyExecutionRuleResponse("目标价", "回踩策略目标价为 close * 1.10；突破策略目标价为 close * 1.12。"),
                new StrategyExecutionRuleResponse("仓位建议", "强势环境建议投入 10000 元；可交易环境和满足条件的弱机会回踩建议投入 8000 元；股数向下取整到 100 股。"),
                new StrategyExecutionRuleResponse("信号生成", "只有候选股可执行、市场环境允许且风险收益比达标时，才生成交易信号。")
            ]);
    }
}

/// <summary>
/// 读取最近交易日的回测概览。
/// </summary>
public sealed class GetBacktestOverviewUseCase(IMarketDataRepository marketRepository)
{
    /// <summary>
    /// 基于历史信号和后续 5 根 K 线返回回测摘要。
    /// </summary>
    public async Task<BacktestOverviewResponse> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var tradeDates = await marketRepository.GetRecentTradeDatesAsync(20, cancellationToken);
        var trades = new List<BacktestTradeItemResponse>();

        foreach (var tradeDate in tradeDates)
        {
            var signals = await marketRepository.GetSignalsAsync(tradeDate, StrategySnapshotVersion.EndOfDayFinal, cancellationToken);
            foreach (var signal in signals.Take(10))
            {
                var forwardBars = await marketRepository.GetForwardDailyBarsAsync(signal.StockCode, tradeDate, 5, cancellationToken);
                if (forwardBars.Count == 0)
                {
                    continue;
                }

                trades.Add(SimulateTrade(signal, forwardBars));
            }
        }

        if (trades.Count == 0)
        {
            return new BacktestOverviewResponse("a-share-20k-v1", 0, 0m, 0m, 0m, 0m, []);
        }

        return new BacktestOverviewResponse(
            "a-share-20k-v1",
            trades.Count,
            Math.Round(trades.Count(static item => item.ReturnPct > 0m) * 100m / trades.Count, 2, MidpointRounding.AwayFromZero),
            Math.Round(trades.Average(static item => item.ReturnPct), 2, MidpointRounding.AwayFromZero),
            Math.Round(trades.Average(static item => item.MaxGainPct), 2, MidpointRounding.AwayFromZero),
            Math.Round(trades.Average(static item => item.MaxDrawdownPct), 2, MidpointRounding.AwayFromZero),
            trades.OrderByDescending(static item => item.TradeDate).ThenBy(static item => item.StockCode).ToList());
    }

    /// <summary>
    /// 用保守顺序模拟单笔交易，优先判定止损。
    /// </summary>
    private static BacktestTradeItemResponse SimulateTrade(TradeSignal signal, IReadOnlyList<DailyBar> forwardBars)
    {
        var entryPrice = signal.TriggerPrice;
        var exitPrice = forwardBars[^1].Close;
        var hitTarget = false;
        var hitStopLoss = false;
        var maxGainPct = decimal.MinValue;
        var maxDrawdownPct = decimal.MaxValue;

        foreach (var bar in forwardBars)
        {
            var gainPct = entryPrice == 0m ? 0m : (bar.High - entryPrice) / entryPrice * 100m;
            var drawdownPct = entryPrice == 0m ? 0m : (bar.Low - entryPrice) / entryPrice * 100m;
            maxGainPct = Math.Max(maxGainPct, gainPct);
            maxDrawdownPct = Math.Min(maxDrawdownPct, drawdownPct);

            if (bar.Low <= signal.StopLossPrice)
            {
                exitPrice = signal.StopLossPrice;
                hitStopLoss = true;
                break;
            }

            if (bar.High >= signal.TargetPrice)
            {
                exitPrice = signal.TargetPrice;
                hitTarget = true;
                break;
            }

            exitPrice = bar.Close;
        }

        var returnPct = entryPrice == 0m ? 0m : (exitPrice - entryPrice) / entryPrice * 100m;
        return new BacktestTradeItemResponse(
            signal.TradeDate,
            signal.StockCode,
            signal.StockName,
            MarketResponseMapper.FormatStrategyType(signal.StrategyType),
            Math.Round(entryPrice, 4, MidpointRounding.AwayFromZero),
            Math.Round(exitPrice, 4, MidpointRounding.AwayFromZero),
            Math.Round(returnPct, 2, MidpointRounding.AwayFromZero),
            Math.Round(maxGainPct == decimal.MinValue ? 0m : maxGainPct, 2, MidpointRounding.AwayFromZero),
            Math.Round(maxDrawdownPct == decimal.MaxValue ? 0m : maxDrawdownPct, 2, MidpointRounding.AwayFromZero),
            hitTarget,
            hitStopLoss,
            Math.Max(signal.EstimatedShares, 0),
            Math.Round(entryPrice * Math.Max(signal.EstimatedShares, 0), 2, MidpointRounding.AwayFromZero),
            0m,
            forwardBars.Count);
    }
}

/// <summary>
/// 读取任务中心概览与最近执行记录。
/// </summary>
public sealed class RunDomainSyncUseCase(
    EnsureLatestMarketSnapshotUseCase ensureLatestSnapshot,
    IMarketDataRepository marketRepository,
    IDomainSyncRunRepository domainSyncRunRepository)
{
    /// <summary>
    /// 按触发来源执行一次领域同步。若没有增量可同步，则返回空。
    /// </summary>
    public async Task<DomainSyncRunEntry?> ExecuteAsync(
        string triggerKind,
        bool forceRefreshLatest = false,
        StrategySnapshotVersion? snapshotVersionOverride = null,
        CancellationToken cancellationToken = default)
    {
        var syncState = await ensureLatestSnapshot.InspectAsync(cancellationToken);
        if (!syncState.RequiresSync && !forceRefreshLatest)
        {
            return null;
        }

        var snapshotVersion = snapshotVersionOverride ?? triggerKind switch
        {
            "startup" or "manual" => StrategySnapshotVersion.EndOfDayFinal,
            _ => StrategySnapshotVersion.EndOfDayFinal
        };
        var startedAtUtc = DateTime.UtcNow;

        try
        {
            var tradeDate = forceRefreshLatest
                ? await ensureLatestSnapshot.RebuildLatestAsync(snapshotVersion, cancellationToken)
                : await ensureLatestSnapshot.ExecuteAsync(snapshotVersion, cancellationToken);
            var finishedAtUtc = DateTime.UtcNow;
            var regime = tradeDate is null
                ? null
                : await marketRepository.GetMarketRegimeAsync(tradeDate.Value, snapshotVersion, cancellationToken);

            var entry = new DomainSyncRunEntry(
                "领域市场同步",
                triggerKind,
                snapshotVersion.ToValue(),
                "成功",
                true,
                regime?.IsSignalEligible ?? false,
                tradeDate,
                syncState.LatestRawFinancialReportDate,
                startedAtUtc,
                finishedAtUtc,
                BuildSyncSummary(syncState, forceRefreshLatest, triggerKind));

            await domainSyncRunRepository.AddRunAsync(entry, cancellationToken);
            return entry;
        }
        catch (Exception ex)
        {
            var failedEntry = new DomainSyncRunEntry(
                "领域市场同步",
                triggerKind,
                snapshotVersion.ToValue(),
                "失败",
                false,
                false,
                syncState.EffectiveTradeDate,
                syncState.LatestRawFinancialReportDate,
                startedAtUtc,
                DateTime.UtcNow,
                ex.Message);

            await domainSyncRunRepository.AddRunAsync(failedEntry, cancellationToken);
            throw;
        }
    }

    private static string BuildSyncSummary(MarketSnapshotSyncState syncState, bool forceRefreshLatest, string triggerKind)
    {
        if (forceRefreshLatest)
        {
            return triggerKind == "manual"
                ? "手动强制重建最新正式结果"
                : "启动时强制重建最新交易日快照";
        }

        var reasons = new List<string>();
        if (syncState.HasTradeDateGap)
        {
            reasons.Add("交易日存在差异");
        }

        if (syncState.HasFinancialGap)
        {
            reasons.Add("财报期存在差异");
        }

        return reasons.Count == 0 ? "手动同步" : string.Join("，", reasons);
    }
}

public sealed class GetTaskCenterOverviewUseCase(
    EnsureLatestMarketSnapshotUseCase ensureLatestSnapshot,
    IMarketDataRepository marketRepository,
    IIngestionLogRepository ingestionLogRepository,
    IDomainSyncRunRepository domainSyncRunRepository)
{
    /// <summary>
    /// 返回任务中心概览数据。
    /// </summary>
    public async Task<TaskCenterOverviewResponse> ExecuteAsync(
        StrategySnapshotVersion snapshotVersion = StrategySnapshotVersion.EndOfDayFinal,
        CancellationToken cancellationToken = default)
    {
        var tradeDate = await ensureLatestSnapshot.ExecuteAsync(snapshotVersion, cancellationToken);
        var syncState = await ensureLatestSnapshot.InspectAsync(cancellationToken);
        var latestSuccessfulIngestionAtUtc = await ingestionLogRepository.GetLatestSuccessfulIngestionAtUtcAsync(cancellationToken);
        var latestSuccessfulDomainSyncAtUtc = await domainSyncRunRepository.GetLatestSuccessfulFinishedAtUtcAsync(cancellationToken);
        var latestSuccessfulEndOfDayFinalAtUtc = await domainSyncRunRepository.GetLatestSuccessfulFinishedAtUtcAsync(StrategySnapshotVersion.EndOfDayFinal, cancellationToken);
        var recentCollectorRuns = await ingestionLogRepository.GetRecentRunsAsync(10, cancellationToken);
        var recentDomainSyncRuns = await domainSyncRunRepository.GetRecentRunsAsync(10, cancellationToken);
        var syncStatus = new DomainSyncStatusResponse(
            syncState.LatestRawTradeDate,
            syncState.LatestImportedTradeDate,
            syncState.LatestRawFinancialReportDate,
            syncState.LatestImportedFinancialReportDate,
            latestSuccessfulDomainSyncAtUtc,
            latestSuccessfulEndOfDayFinalAtUtc,
            syncState.RequiresSync,
            syncState.HasTradeDateGap,
            syncState.HasFinancialGap);
        var schedules = BuildTaskSchedules();
        var statusMessages = BuildTaskStatusMessages(syncState, recentCollectorRuns);

        if (tradeDate is null)
        {
            return new TaskCenterOverviewResponse(
                null,
                snapshotVersion.ToValue(),
                snapshotVersion.ToDisplayName(),
                latestSuccessfulIngestionAtUtc,
                syncStatus,
                0,
                0,
                "暂无数据",
                false,
                schedules,
                statusMessages,
                recentCollectorRuns.Select(static item => new TaskRunItemResponse(item.TargetScope, item.IsComplete, item.IsSignalEligible, item.CreatedAtUtc)).ToList(),
                recentDomainSyncRuns
                    .Select(static item => new DomainSyncRunItemResponse(
                        item.JobName,
                        MarketResponseMapper.FormatTriggerKind(item.TriggerKind),
                        MarketResponseMapper.FormatSnapshotVersion(item.SnapshotVersion),
                        MarketResponseMapper.FormatRunStatus(item.Status),
                        item.DataUpdated,
                        item.IsSignalEligible,
                        item.EffectiveTradeDate,
                        item.FinancialReportDate,
                        item.StartedAtUtc,
                        item.FinishedAtUtc,
                        item.Summary))
                    .ToList());
        }

        var candidates = await marketRepository.GetCandidatesAsync(tradeDate.Value, snapshotVersion, cancellationToken);
        var signals = await marketRepository.GetSignalsAsync(tradeDate.Value, snapshotVersion, cancellationToken);
        var regime = await marketRepository.GetMarketRegimeAsync(tradeDate.Value, snapshotVersion, cancellationToken);

        return new TaskCenterOverviewResponse(
            tradeDate.Value,
            snapshotVersion.ToValue(),
            snapshotVersion.ToDisplayName(),
            latestSuccessfulIngestionAtUtc,
            syncStatus,
            candidates.Count,
            signals.Count,
            MarketResponseMapper.FormatMarketRegime(regime?.Regime),
            regime?.IsSignalEligible ?? false,
            schedules,
            statusMessages,
            recentCollectorRuns.Select(static item => new TaskRunItemResponse(item.TargetScope, item.IsComplete, item.IsSignalEligible, item.CreatedAtUtc)).ToList(),
            recentDomainSyncRuns
                .Select(static item => new DomainSyncRunItemResponse(
                    item.JobName,
                    MarketResponseMapper.FormatTriggerKind(item.TriggerKind),
                    MarketResponseMapper.FormatSnapshotVersion(item.SnapshotVersion),
                    MarketResponseMapper.FormatRunStatus(item.Status),
                    item.DataUpdated,
                    item.IsSignalEligible,
                    item.EffectiveTradeDate,
                    item.FinancialReportDate,
                    item.StartedAtUtc,
                    item.FinishedAtUtc,
                    item.Summary))
                .ToList());
    }

    private static IReadOnlyList<TaskScheduleItemResponse> BuildTaskSchedules()
    {
        return
        [
            new TaskScheduleItemResponse("收盘正式版采集", "每周一至周五 15:20（Asia/Shanghai）", "收盘后采集股票快照、日线、指数与行业数据，并生成正式结果。"),
            new TaskScheduleItemResponse("收盘补拉任务", "每周一至周五 16:00 / 16:30 / 17:00 / 18:00（Asia/Shanghai）", "若上游仍未发布当日日频数据，则继续补拉，直到原始交易日更新为当天。"),
            new TaskScheduleItemResponse("财务采集", "每周日 21:00（Asia/Shanghai）", "更新财务与估值快照。"),
            new TaskScheduleItemResponse("领域同步任务", "启动时 + 每 60 秒", "检查原始层变更并同步到领域快照、指标、候选池和交易信号。")
        ];
    }

    private static IReadOnlyList<string> BuildTaskStatusMessages(
        MarketSnapshotSyncState syncState,
        IReadOnlyList<IngestionLogEntry> recentCollectorRuns)
    {
        var messages = new List<string>();
        var today = DateOnly.FromDateTime(DateTime.Now);

        if (syncState.LatestRawTradeDate is null)
        {
            messages.Add("原始层暂时还没有可用行情数据。");
            return messages;
        }

        if (syncState.LatestRawTradeDate < today)
        {
            messages.Add("上游仍返回昨日交易日，今日日频数据尚未发布。");
        }

        if (recentCollectorRuns.Any(item => item.TargetScope == "sync-daily-indices" && !item.IsComplete))
        {
            messages.Add("指数数据不完整，正式评分会继续沿用最近完整交易日。");
        }

        if (messages.Count == 0)
        {
            messages.Add("当前正式版数据已同步到最新可用交易日。");
        }

        return messages;
    }
}

/// <summary>
/// 读取单只股票详情快照。
/// </summary>
public sealed class GetStockDetailUseCase(
    EnsureLatestMarketSnapshotUseCase ensureLatestSnapshot,
    IMarketDataRepository marketRepository)
{
    /// <summary>
    /// 返回单只股票在指定交易日的详情。
    /// </summary>
    public async Task<StockDetailResponse?> ExecuteAsync(string stockCode, DateOnly? tradeDate, string? snapshotVersionRaw, CancellationToken cancellationToken = default)
    {
        var snapshotVersion = StrategySnapshotVersionResolver.Resolve(snapshotVersionRaw);
        var resolvedDate = tradeDate ?? await ensureLatestSnapshot.ExecuteAsync(snapshotVersion, cancellationToken);
        if (resolvedDate is null)
        {
            return null;
        }

        var profile = await marketRepository.GetStockProfileAsync(stockCode, cancellationToken);
        if (profile is null)
        {
            return null;
        }

        var latestBarHistory = await marketRepository.GetRecentImportedDailyBarsAsync(stockCode, resolvedDate.Value, 60, cancellationToken);
        var indicator = await marketRepository.GetIndicatorSnapshotAsync(stockCode, resolvedDate.Value, snapshotVersion, cancellationToken);
        var financial = await marketRepository.GetLatestFinancialAsync(stockCode, cancellationToken);
        var candidate = await marketRepository.GetCandidateAsync(stockCode, resolvedDate.Value, snapshotVersion, cancellationToken);
        var signal = await marketRepository.GetSignalAsync(stockCode, resolvedDate.Value, snapshotVersion, cancellationToken);
        if (latestBarHistory.Count == 0)
        {
            return null;
        }

        var latestBar = latestBarHistory.OrderBy(static item => item.TradeDate).Last();
        CandidateListItemResponse? candidateResponse = null;
        if (candidate is not null)
        {
            // 详情页按最新规则重建一次评分拆解，避免数据库中仅有汇总分而缺少逐条明细。
            var industry = await ResolveIndustryAsync(profile.IndustryName, resolvedDate.Value, cancellationToken);
            var scoreBreakdown = indicator is null
                ? candidate.ScoreBreakdown
                : CandidatePolicy.DescribeScoreBreakdown(profile, indicator, financial, industry);

            candidateResponse = new CandidateListItemResponse(
                candidate.StockCode,
                candidate.StockName,
                candidate.IndustryName,
                MarketResponseMapper.FormatCandidateGrade(candidate.Grade),
                MarketResponseMapper.FormatStrategyType(candidate.StrategyType),
                candidate.IsTradable,
                candidate.TotalScore,
                scoreBreakdown,
                candidate.Close,
                candidate.Ma20,
                candidate.Ma60,
                candidate.Atr14,
                candidate.RelativeStrengthScore,
                candidate.StopLossPrice,
                candidate.TargetPrice,
                candidate.RiskRewardRatio,
                candidate.Explanation,
                MarketResponseMapper.BuildScoreRuleDetails(scoreBreakdown));
        }

        return new StockDetailResponse(
            stockCode,
            profile.StockName,
            profile.IndustryName,
            resolvedDate.Value,
            snapshotVersion.ToValue(),
            snapshotVersion.ToDisplayName(),
            new PriceSeriesResponse(latestBar.TradeDate, latestBar.Open, latestBar.High, latestBar.Low, latestBar.Close, latestBar.Volume, latestBar.Amount, indicator?.Ma20, indicator?.Ma60, indicator?.Ma120),
            financial is null ? null : new FinancialSummaryResponse(financial.ReportDate, financial.Pe, financial.Pb, financial.Roe, financial.RevenueYoy, financial.NetProfitYoy),
            indicator is null ? null : new IndicatorSummaryResponse(indicator.Close, indicator.Ma20, indicator.Ma60, indicator.Ma120, indicator.Atr14, indicator.Return20d, indicator.Return60d, indicator.RelativeStrengthScore, indicator.Is20DayBreakout, indicator.IsMa20Upward, indicator.IsBullishStacked, indicator.DistanceToMa20Pct),
            candidateResponse,
            signal is null ? null : new SignalListItemResponse(signal.StockCode, signal.StockName, signal.IndustryName, MarketResponseMapper.FormatStrategyType(signal.StrategyType), signal.TotalScore, signal.ScoreBreakdown, signal.TriggerPrice, signal.StopLossPrice, signal.TargetPrice, signal.RiskRewardRatio, signal.SuggestedCapital, signal.EstimatedShares, signal.Explanation, signal.GeneratedAtUtc),
            latestBarHistory
                .OrderBy(static item => item.TradeDate)
                .Select(item => new PriceSeriesResponse(item.TradeDate, item.Open, item.High, item.Low, item.Close, item.Volume, item.Amount, item.TradeDate == resolvedDate.Value ? indicator?.Ma20 : null, item.TradeDate == resolvedDate.Value ? indicator?.Ma60 : null, item.TradeDate == resolvedDate.Value ? indicator?.Ma120 : null))
                .ToList());
    }

    /// <summary>
    /// 读取个股所属行业在当前交易日的统计快照，供评分解释补足行业维度证据。
    /// </summary>
    private async Task<IndustryDailyStat?> ResolveIndustryAsync(string? industryName, DateOnly tradeDate, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(industryName))
        {
            return null;
        }

        var industries = await marketRepository.GetIndustryStatsByNamesAsync(tradeDate, [industryName], cancellationToken);
        return industries.TryGetValue(industryName, out var industry) ? industry : null;
    }
}

/// <summary>
/// 列表查询辅助逻辑。
/// </summary>
internal static class MarketListQueryPaging
{
    /// <summary>
    /// 将列表转换为分页响应。
    /// </summary>
    internal static PagedResponse<TItem> ToPagedResponse<TItem>(IReadOnlyList<TItem> items, int page, int pageSize)
    {
        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = NormalizePageSize(pageSize);
        var pagedItems = items
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToList();
        return new PagedResponse<TItem>(pagedItems, normalizedPage, normalizedPageSize, items.Count);
    }

    /// <summary>
    /// 约束分页大小，避免接口一次返回过多数据。
    /// </summary>
    internal static int NormalizePageSize(int pageSize)
    {
        return Math.Clamp(pageSize, 1, 100);
    }
}

internal static class MarketResponseMapper
{
    internal static string FormatRunStatus(string status)
    {
        return status switch
        {
            "Succeeded" or "成功" => "成功",
            "Failed" or "失败" => "失败",
            "Running" or "执行中" => "执行中",
            _ => status
        };
    }

    internal static string FormatTriggerKind(string triggerKind)
    {
        return triggerKind switch
        {
            "startup" => "启动时",
            "poll" => "轮询",
            "manual" => "手动",
            _ => triggerKind
        };
    }

    internal static string FormatSnapshotVersion(string snapshotVersion)
    {
        return StrategySnapshotVersionCodec.ParseOrDefault(snapshotVersion).ToDisplayName();
    }

    internal static string FormatMarketRegime(MarketSignalEligibility? regime)
    {
        return regime switch
        {
            MarketSignalEligibility.Strong => "强势",
            MarketSignalEligibility.Tradable => "可交易",
            MarketSignalEligibility.WeakOpportunity => "弱机会",
            MarketSignalEligibility.NoTrade => "不交易",
            _ => "未知"
        };
    }

    internal static string FormatCandidateGrade(CandidateGrade grade)
    {
        return grade switch
        {
            CandidateGrade.A => "A",
            CandidateGrade.B => "B",
            CandidateGrade.C => "C",
            _ => "D"
        };
    }

    internal static string FormatStrategyType(StrategyType strategyType)
    {
        return strategyType switch
        {
            StrategyType.Breakout => "突破",
            StrategyType.PullbackToMa20 => "回踩 MA20",
            StrategyType.WatchBreakout => "观察突破",
            StrategyType.WatchPullback => "观察回踩",
            _ => strategyType.ToString()
        };
    }

    internal static IReadOnlyList<ScoreRuleDetailResponse> BuildScoreRuleDetails(CandidateScoreBreakdown breakdown)
    {
        return breakdown.Details
            .Select(static item => new ScoreRuleDetailResponse(
                item.Key,
                item.Dimension,
                item.Label,
                item.Score,
                item.MaxScore,
                item.Hit,
                item.Evidence))
            .ToList();
    }
}

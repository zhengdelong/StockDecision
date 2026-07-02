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
    IIngestionLogRepository ingestionLogRepository,
    TradingPermissionsOptions tradingPermissions)
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
        return await ExecuteAsync(syncState, snapshotVersion, cancellationToken);
    }

    internal async Task<DateOnly?> ExecuteAsync(
        MarketSnapshotSyncState syncState,
        StrategySnapshotVersion snapshotVersion,
        CancellationToken cancellationToken = default)
    {
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
        var stockFundFlows = await rawRepository.GetStockFundFlowsByTradeDateAsync(syncTradeDate, cancellationToken);
        var industryFundFlows = await rawRepository.GetIndustryFundFlowsByTradeDateAsync(syncTradeDate, cancellationToken);
        var lhbSnapshots = await rawRepository.GetLhbSnapshotsByTradeDateAsync(syncTradeDate, cancellationToken);

        await marketRepository.ReplaceMarketSnapshotAsync(
            syncTradeDate,
            stocks,
            dailyBars,
            indices,
            industries,
            financials,
            stockFundFlows,
            industryFundFlows,
            lhbSnapshots,
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
        return await RebuildLatestAsync(syncState, snapshotVersion, cancellationToken);
    }

    internal async Task<DateOnly?> RebuildLatestAsync(
        MarketSnapshotSyncState syncState,
        StrategySnapshotVersion snapshotVersion,
        CancellationToken cancellationToken = default)
    {
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
        var stockFundFlows = await rawRepository.GetStockFundFlowsByTradeDateAsync(syncTradeDate, cancellationToken);
        var industryFundFlows = await rawRepository.GetIndustryFundFlowsByTradeDateAsync(syncTradeDate, cancellationToken);
        var lhbSnapshots = await rawRepository.GetLhbSnapshotsByTradeDateAsync(syncTradeDate, cancellationToken);

        await marketRepository.ReplaceMarketSnapshotAsync(
            syncTradeDate,
            stocks,
            dailyBars,
            indices,
            industries,
            financials,
            stockFundFlows,
            industryFundFlows,
            lhbSnapshots,
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
        var indicatorMetrics = await marketRepository.GetIndicatorCalculationMetricsByCodesAsync(stockCodes, tradeDate, 140, cancellationToken);
        var indicatorSnapshots = new List<IndicatorSnapshot>(stockCodes.Count);
        var stockHistoryMetrics = new Dictionary<string, StockScoringHistoryMetrics>(StringComparer.OrdinalIgnoreCase);
        foreach (var stockCode in stockCodes)
        {
            if (!indicatorMetrics.TryGetValue(stockCode, out var metrics))
            {
                continue;
            }

            var distanceToMa20 = metrics.Ma20 == 0m ? 0m : ((metrics.Close - metrics.Ma20) / metrics.Ma20) * 100m;

            indicatorSnapshots.Add(new IndicatorSnapshot(
                stockCode,
                tradeDate,
                metrics.Close,
                metrics.Ma20,
                metrics.Ma60,
                metrics.Ma120,
                metrics.Atr14,
                metrics.Return20d,
                metrics.Return60d,
                metrics.Return20d,
                metrics.Close >= metrics.BreakoutClose,
                metrics.Ma20 > metrics.PreviousMa20,
                metrics.Ma20 > metrics.Ma60 && metrics.Ma60 > metrics.Ma120,
                Math.Round(distanceToMa20, 4, MidpointRounding.AwayFromZero),
                metrics.TurnoverRate));
            stockHistoryMetrics[stockCode] = new StockScoringHistoryMetrics(stockCode, metrics.Return10d, metrics.AmountRatio1d, metrics.Ma60Previous);
        }

        indicatorSnapshots = indicatorSnapshots
            .OrderBy(static item => item.Return20d)
            .Select((item, index) =>
            {
                var percentile = indicatorSnapshots.Count <= 1
                    ? 100m
                    : Math.Round(index * 100m / (indicatorSnapshots.Count - 1), 4, MidpointRounding.AwayFromZero);

                return item with { RelativeStrengthScore = percentile };
            })
            .ToList();

        await marketRepository.UpsertIndicatorSnapshotsAsync(tradeDate, snapshotVersion, indicatorSnapshots, cancellationToken);

        var indexHistory = await marketRepository.GetIndexBarHistoryAsync(tradeDate, 30, cancellationToken);
        var orderedIndexBars = indexHistory
            .GroupBy(static item => item.IndexCode)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderBy(item => item.TradeDate).ToList());
        var regime = MarketRegimePolicy.Evaluate(
            tradeDate,
            orderedIndexBars.ToDictionary(
                static pair => pair.Key,
                static pair => (IReadOnlyList<MarketIndexBar>)pair.Value));
        await marketRepository.UpsertMarketRegimeAsync(snapshotVersion, regime, cancellationToken);

        var profiles = await marketRepository.GetStockProfilesByCodesAsync(indicatorSnapshots.Select(static item => item.StockCode), cancellationToken);
        var financials = await marketRepository.GetLatestFinancialsByCodesAsync(indicatorSnapshots.Select(static item => item.StockCode), cancellationToken);
        var stockFundFlows = await marketRepository.GetStockFundFlowsByCodesAsync(tradeDate, indicatorSnapshots.Select(static item => item.StockCode), cancellationToken);
        var industries = await marketRepository.GetIndustryStatsByNamesAsync(
            tradeDate,
            profiles.Values.Select(static item => item.EffectiveScoringIndustryName).Distinct().ToList(),
            cancellationToken);
        var industryFundFlows = await marketRepository.GetIndustryFundFlowsByNamesAsync(
            tradeDate,
            profiles.Values.Select(static item => item.EffectiveScoringIndustryName).Distinct().ToList(),
            cancellationToken);
        var lhbSnapshots = await marketRepository.GetLhbSnapshotsByCodesAsync(tradeDate, indicatorSnapshots.Select(static item => item.StockCode), cancellationToken);

        var benchmarkIndex = orderedIndexBars.TryGetValue("000300", out var csi300)
            ? csi300
            : orderedIndexBars.Values.FirstOrDefault();
        var indexReturn20d = benchmarkIndex is not null ? IndicatorMath.CalculateIndexReturn(benchmarkIndex, 20) : 0m;
        var indexReturn60d = benchmarkIndex is not null ? IndicatorMath.CalculateIndexReturn(benchmarkIndex, 60) : 0m;

        var scoreSnapshots = new List<StrategyScoreSnapshot>();
        var candidates = new List<CandidateStock>();
        foreach (var indicator in indicatorSnapshots)
        {
            if (!profiles.TryGetValue(indicator.StockCode, out var profile))
            {
                continue;
            }

            if (!profile.IsActive || profile.IsSt || profile.IsDelistingRisk)
            {
                continue;
            }

            financials.TryGetValue(indicator.StockCode, out var financial);
            var scoringIndustryName = profile.EffectiveScoringIndustryName ?? string.Empty;
            industries.TryGetValue(scoringIndustryName, out var industry);
            stockFundFlows.TryGetValue(indicator.StockCode, out var stockFundFlow);
            industryFundFlows.TryGetValue(scoringIndustryName, out var industryFundFlow);
            lhbSnapshots.TryGetValue(indicator.StockCode, out var lhbSnapshot);

            stockHistoryMetrics.TryGetValue(indicator.StockCode, out var historyMetrics);
            var return10d = historyMetrics?.Return10d ?? 0m;
            var amountRatio1d = historyMetrics?.AmountRatio1d ?? 0m;
            var ma60Previous = historyMetrics?.Ma60Previous ?? 0m;
            var context = new CandidateScoringContext(
                return10d,
                indexReturn20d,
                indexReturn60d,
                industry?.PctChange20d,
                indicator.Ma60 > ma60Previous && ma60Previous > 0m,
                amountRatio1d,
                stockFundFlow,
                industryFundFlow,
                lhbSnapshot);
            var scoreBreakdown = CandidatePolicy.DescribeScoreBreakdown(profile, indicator, financial, industry, context);
            scoreSnapshots.Add(new StrategyScoreSnapshot(
                tradeDate,
                profile.StockCode,
                profile.StockName,
                profile.IndustryName,
                scoreBreakdown.TotalScore,
                scoreBreakdown.RelativeStrengthScore,
                scoreBreakdown.TrendScore,
                scoreBreakdown.VolumePriceScore,
                scoreBreakdown.FundamentalScore,
                financial?.Pe ?? profile.Pe,
                financial?.Pb ?? profile.Pb,
                financial?.Roe,
                scoreBreakdown.RiskDisciplineScore));
            var candidate = CandidatePolicy.Evaluate(
                tradeDate,
                profile,
                indicator,
                financial,
                industry,
                context,
                regime,
                TradingBoardClassifier.IsInTradablePool(profile.StockCode, tradingPermissions),
                TradingBoardClassifier.IsInWatchPool(profile.StockCode));
            if (candidate is not null)
            {
                candidates.Add(candidate);
            }
        }

        await marketRepository.UpsertScoreSnapshotsAsync(tradeDate, snapshotVersion, scoreSnapshots, cancellationToken);
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
    IIngestionLogRepository ingestionLogRepository,
    IBacktestRunRepository backtestRunRepository)
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
            return new DashboardResponse(null, snapshotVersion.ToValue(), snapshotVersion.ToDisplayName(), false, false, false, "还没有可用回测记录，暂不开放可执行信号。", "暂无数据", 0, 0, null, []);
        }

        var candidates = await marketRepository.GetCandidatesAsync(tradeDate.Value, snapshotVersion, cancellationToken);
        var rawSignals = await marketRepository.GetSignalsAsync(tradeDate.Value, snapshotVersion, cancellationToken);
        var regime = await marketRepository.GetMarketRegimeAsync(tradeDate.Value, snapshotVersion, cancellationToken);
        var latestIngestion = await ingestionLogRepository.GetLatestSuccessfulIngestionAtUtcAsync(cancellationToken);
        var backtestApproval = BacktestApprovalPolicy.Resolve(await backtestRunRepository.GetLatestRunAsync(cancellationToken));
        var isSignalEligible = (regime?.IsSignalEligible ?? false) && backtestApproval.IsApproved;
        var signals = MarketListQueryPaging.ResolveVisibleSignals(rawSignals, candidates, regime);

        return new DashboardResponse(
            tradeDate.Value,
            snapshotVersion.ToValue(),
            snapshotVersion.ToDisplayName(),
            true,
            isSignalEligible,
            backtestApproval.IsApproved,
            backtestApproval.StatusNote,
            MarketResponseMapper.FormatMarketRegime(regime?.Regime),
            candidates.Count,
            signals.Count,
            latestIngestion,
            [
                new DashboardMetricResponse("tradeDate", "交易日", tradeDate.Value.ToString("yyyy-MM-dd"), "neutral"),
                new DashboardMetricResponse("snapshotVersion", "结果版本", snapshotVersion.ToDisplayName(), snapshotVersion == StrategySnapshotVersion.EndOfDayFinal ? "positive" : "warning"),
                new DashboardMetricResponse("regime", "市场环境", MarketResponseMapper.FormatMarketRegime(regime?.Regime), regime?.IsSignalEligible == true ? "positive" : "warning"),
                new DashboardMetricResponse("backtest", "回测准入", backtestApproval.IsApproved ? "已通过" : "未通过", backtestApproval.IsApproved ? "positive" : "warning"),
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
                MarketResponseMapper.FormatEligibilityStatus(item.EligibilityStatus),
                MarketResponseMapper.BuildEligibilityReasons(item.EligibilityReason),
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
                MarketResponseMapper.BuildScoreRuleDetails(item.ScoreBreakdown),
                TradeExecutionPlanFactory.BuildForCandidate(item)))
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
    IMarketDataRepository marketRepository,
    IBacktestRunRepository backtestRunRepository)
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

        _ = BacktestApprovalPolicy.Resolve(await backtestRunRepository.GetLatestRunAsync(cancellationToken));
        var rawSignals = await marketRepository.GetSignalsAsync(resolvedDate.Value, snapshotVersion, cancellationToken);
        var candidates = await marketRepository.GetCandidatesAsync(resolvedDate.Value, snapshotVersion, cancellationToken);
        var regime = await marketRepository.GetMarketRegimeAsync(resolvedDate.Value, snapshotVersion, cancellationToken);
        var signals = MarketListQueryPaging.ResolveVisibleSignals(rawSignals, candidates, regime);
        var items = signals
            .Select(static item => new SignalListItemResponse(
                item.StockCode,
                item.StockName,
                item.IndustryName,
                MarketResponseMapper.FormatStrategyType(item.StrategyType),
                MarketResponseMapper.FormatEligibilityStatus(item.EligibilityStatus),
                MarketResponseMapper.BuildEligibilityReasons(item.EligibilityReason),
                item.TotalScore,
                item.ScoreBreakdown,
                item.TriggerPrice,
                item.StopLossPrice,
                item.TargetPrice,
                item.RiskRewardRatio,
                item.SuggestedCapital,
                item.EstimatedShares,
                item.Explanation,
                item.GeneratedAtUtc,
                TradeExecutionPlanFactory.BuildForSignal(item)))
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

        var items = industryStats
            .Select(industry =>
            {
                candidateGroups.TryGetValue(industry.IndustryName, out var candidateGroup);
                signalGroups.TryGetValue(industry.IndustryName, out var signalGroup);

                return new IndustryListItemResponse(
                    industry.IndustryCode,
                    industry.IndustryName,
                    resolvedDate.Value,
                    industry.PctChange20d ?? 0m,
                    industry.Rank20d ?? int.MaxValue,
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
/// 读取个股资金流列表。
/// </summary>
public sealed class GetStockFundFlowsUseCase(
    EnsureLatestMarketSnapshotUseCase ensureLatestSnapshot,
    IMarketDataRepository marketRepository)
{
    /// <summary>
    /// 返回指定交易日或最新交易日的个股资金流分页列表。
    /// </summary>
    public async Task<PagedResponse<StockFundFlowListItemResponse>> ExecuteAsync(FundFlowListQuery query, CancellationToken cancellationToken = default)
    {
        var snapshotVersion = StrategySnapshotVersionResolver.Resolve(query.SnapshotVersion);
        var resolvedDate = query.Date ?? await ensureLatestSnapshot.ExecuteAsync(snapshotVersion, cancellationToken);
        if (resolvedDate is null)
        {
            return new PagedResponse<StockFundFlowListItemResponse>([], 1, MarketListQueryPaging.NormalizePageSize(query.PageSize), 0);
        }

        return await marketRepository.GetStockFundFlowPageAsync(resolvedDate.Value, snapshotVersion, query, cancellationToken);
    }
}

/// <summary>
/// 读取行业资金流列表。
/// </summary>
public sealed class GetIndustryFundFlowsUseCase(
    EnsureLatestMarketSnapshotUseCase ensureLatestSnapshot,
    IMarketDataRepository marketRepository)
{
    /// <summary>
    /// 返回指定交易日或最新交易日的行业资金流分页列表。
    /// </summary>
    public async Task<PagedResponse<IndustryFundFlowListItemResponse>> ExecuteAsync(FundFlowListQuery query, CancellationToken cancellationToken = default)
    {
        var snapshotVersion = StrategySnapshotVersionResolver.Resolve(query.SnapshotVersion);
        var resolvedDate = query.Date ?? await ensureLatestSnapshot.ExecuteAsync(snapshotVersion, cancellationToken);
        if (resolvedDate is null)
        {
            return new PagedResponse<IndustryFundFlowListItemResponse>([], 1, MarketListQueryPaging.NormalizePageSize(query.PageSize), 0);
        }

        return await marketRepository.GetIndustryFundFlowPageAsync(resolvedDate.Value, snapshotVersion, query, cancellationToken);
    }
}

/// <summary>
/// 读取财务质量列表。
/// </summary>
public sealed class GetFinancialsUseCase(
    EnsureLatestMarketSnapshotUseCase ensureLatestSnapshot,
    IMarketDataRepository marketRepository)
{
    /// <summary>
    /// 返回最新财务快照分页列表。
    /// </summary>
    public async Task<PagedResponse<FinancialListItemResponse>> ExecuteAsync(FinancialListQuery query, CancellationToken cancellationToken = default)
    {
        var snapshotVersion = StrategySnapshotVersionResolver.Resolve(query.SnapshotVersion);
        var resolvedDate = query.Date ?? await ensureLatestSnapshot.ExecuteAsync(snapshotVersion, cancellationToken);
        if (resolvedDate is null)
        {
            return new PagedResponse<FinancialListItemResponse>([], 1, MarketListQueryPaging.NormalizePageSize(query.PageSize), 0);
        }

        if (await marketRepository.CountScoreSnapshotsAsync(resolvedDate.Value, snapshotVersion, cancellationToken) > 0)
        {
            return await marketRepository.GetFinancialScorePageAsync(resolvedDate.Value, snapshotVersion, query, cancellationToken);
        }

        var indicators = await marketRepository.GetIndicatorSnapshotsAsync(resolvedDate.Value, snapshotVersion, cancellationToken);
        var stockCodes = indicators.Select(static item => item.StockCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var profiles = await marketRepository.GetStockProfilesByCodesAsync(stockCodes, cancellationToken);
        var financials = await marketRepository.GetLatestFinancialsByCodesAsync(stockCodes, cancellationToken);
        var candidateScores = (await marketRepository.GetCandidatesAsync(resolvedDate.Value, snapshotVersion, cancellationToken))
            .ToDictionary(static item => item.StockCode, StringComparer.OrdinalIgnoreCase);
        var stockFundFlows = await marketRepository.GetStockFundFlowsByCodesAsync(resolvedDate.Value, stockCodes, cancellationToken);
        var industries = await marketRepository.GetIndustryStatsByNamesAsync(
            resolvedDate.Value,
            profiles.Values.Select(static item => item.EffectiveScoringIndustryName).Distinct().ToList(),
            cancellationToken);
        var industryFundFlows = await marketRepository.GetIndustryFundFlowsByNamesAsync(
            resolvedDate.Value,
            profiles.Values.Select(static item => item.EffectiveScoringIndustryName).Distinct().ToList(),
            cancellationToken);
        var lhbSnapshots = await marketRepository.GetLhbSnapshotsByCodesAsync(resolvedDate.Value, stockCodes, cancellationToken);
        var indexHistory = await marketRepository.GetIndexBarHistoryAsync(resolvedDate.Value, 80, cancellationToken);
        var stockHistoryMetrics = await marketRepository.GetScoringHistoryMetricsByCodesAsync(stockCodes, resolvedDate.Value, cancellationToken);
        var orderedIndexBars = indexHistory
            .GroupBy(static item => item.IndexCode)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderBy(item => item.TradeDate).ToList());
        var benchmarkIndex = orderedIndexBars.TryGetValue("000300", out var csi300)
            ? csi300
            : orderedIndexBars.Values.FirstOrDefault();
        var indexReturn20d = benchmarkIndex is not null ? IndicatorMath.CalculateIndexReturn(benchmarkIndex, 20) : 0m;
        var indexReturn60d = benchmarkIndex is not null ? IndicatorMath.CalculateIndexReturn(benchmarkIndex, 60) : 0m;

        var items = new List<FinancialListItemResponse>();
        foreach (var indicator in indicators)
        {
            if (!profiles.TryGetValue(indicator.StockCode, out var profile))
            {
                continue;
            }

            financials.TryGetValue(indicator.StockCode, out var financial);
            var scoringIndustryName = profile.EffectiveScoringIndustryName ?? string.Empty;
            industries.TryGetValue(scoringIndustryName, out var industry);
            stockFundFlows.TryGetValue(indicator.StockCode, out var stockFundFlow);
            industryFundFlows.TryGetValue(scoringIndustryName, out var industryFundFlow);
            lhbSnapshots.TryGetValue(indicator.StockCode, out var lhbSnapshot);

            stockHistoryMetrics.TryGetValue(indicator.StockCode, out var historyMetrics);
            var return10d = historyMetrics?.Return10d ?? 0m;
            var amountRatio1d = historyMetrics?.AmountRatio1d ?? 0m;
            var ma60Previous = historyMetrics?.Ma60Previous ?? 0m;
            var context = new CandidateScoringContext(
                return10d,
                indexReturn20d,
                indexReturn60d,
                industry?.PctChange20d,
                indicator.Ma60 > ma60Previous && ma60Previous > 0m,
                amountRatio1d,
                stockFundFlow,
                industryFundFlow,
                lhbSnapshot);
            var scoreBreakdown = candidateScores.TryGetValue(indicator.StockCode, out var candidate)
                ? candidate.ScoreBreakdown
                : CandidatePolicy.DescribeScoreBreakdown(profile, indicator, financial, industry, context);
            var totalScore = candidate is not null
                ? candidate.TotalScore
                : scoreBreakdown.TotalScore;

            items.Add(new FinancialListItemResponse(
                indicator.StockCode,
                profile.StockName,
                profile.IndustryName,
                financial?.ReportDate,
                totalScore,
                financial?.Pe ?? profile.Pe,
                financial?.Pb ?? profile.Pb,
                financial?.Roe,
                financial?.RevenueYoy,
                financial?.NetProfitYoy,
                financial?.FreeFloatMarketCap ?? profile.FreeFloatMarketCap,
                financial?.OperatingCashFlow,
                financial?.GrossMargin,
                financial?.DebtToAssetRatio,
                financial?.OperatingCashFlowNet,
                financial?.AnnouncementDate,
                financial?.DataSourcePriority));
        }

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

        items = (query.SortBy ?? "score").ToLowerInvariant() switch
        {
            "score" => items.OrderByDescending(item => item.TotalScore ?? decimal.MinValue).ThenByDescending(item => item.Roe ?? decimal.MinValue).ToList(),
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
            "a-share-20k-v2",
            [
                new StrategyRuleSectionResponse("候选池范围", [
                    "观察池默认覆盖主板和创业板；是否能进入可执行结果，由账户权限开关控制。",
                    "剔除停牌、ST、退市风险以及上市未满 250 个交易日的股票。",
                    "要求股价站上 MA60，ATR 波动率不超过 7%，最新价格位于 5 到 80 元之间。",
                    "要求 20 日平均成交额不低于 2 亿元。"
                ]),
                new StrategyRuleSectionResponse("市场环境", [
                    "使用三大指数判断环境，收盘站上 MA20 且 MA20 上行为有效确认。",
                    "3 个确认为强势，2 个确认可交易，1 个确认弱机会，0 个确认不交易。"
                ]),
                new StrategyRuleSectionResponse("评分分层", [
                    "82 分以下直接淘汰，不进入观察池。",
                    "82 到 87 分只保留普通观察。",
                    "88 到 89 分进入强观察，但不进入可执行结果。",
                    "90 分及以上才有资格进入可执行评估。"
                ]),
                new StrategyRuleSectionResponse("可执行信号门槛", [
                    "策略类型必须是突破或回踩 MA20。",
                    "市场环境不能是不交易；弱机会市场不执行突破策略。",
                    "股票必须在当前账户的可交易池内，否则只保留观察。",
                    "风险收益比至少为 1.8。",
                    "最近一次回测必须通过准入标准，否则系统只展示观察结果。"
                ])
            ],
            [
                new StrategyScoreDimensionResponse("相对强弱", 30m, [
                    "20日收益率跑赢基准指数，加 8 分。",
                    "20日收益率跑赢所属行业，加 7 分。",
                    "60日收益率跑赢基准指数，加 7 分。",
                    "全市场相对强度分位不低于 80，加 8 分。"
                ]),
                new StrategyScoreDimensionResponse("趋势质量", 25m, [
                    "收盘价站上 MA20/MA60/MA120，合计最高 11 分。",
                    "MA20 > MA60 > MA120 的多头排列，加 6 分。",
                    "MA20 和 MA60 保持上行，合计最高 5 分。",
                    "20日收盘突破，加 2 分。",
                    "距离 MA20 不超过 10%，加 1 分。"
                ]),
                new StrategyScoreDimensionResponse("量价确认", 20m, [
                    "近 1 日成交额相对 20 日均量明显放大，加 6 分。",
                    "换手率位于 2% 到 8%，加 4 分。",
                    "20 日平均成交额达到 15 亿以上，加 4 分。",
                    "成交额未明显萎缩，加 3 分。",
                    "量价配合稳定，加 3 分。",
                    "行业、资金流和龙虎榜机构净买入在观察期只作为解释标签，不参与核心加分。"
                ]),
                new StrategyScoreDimensionResponse("基本面质量", 15m, [
                    "ROE 不低于 8，加 4 分。",
                    "营收同比为正，加 3 分。",
                    "净利润同比为正，加 3 分。",
                    "经营现金流为正，加 2 分。",
                    "毛利率和负债率无极端异常，加 1 分。",
                    "估值 PE/PB 无极端异常，加 2 分。"
                ]),
                new StrategyScoreDimensionResponse("风险纪律", 10m, [
                    "ATR/收盘价不超过 5%，加 2 分。",
                    "近 10 日涨幅不超过 20%，加 3 分。",
                    "距离 MA20 不过远，合计最高 3 分。",
                    "换手率不过热，加 2 分。",
                    "极端换手和龙虎榜高热风险会扣分。"
                ])
            ],
            [
                new StrategyExecutionRuleResponse("止损", "回踩策略取 max(MA20 * 0.98, close * 0.96)；突破策略取 max(close * 0.97, close * 0.96)。"),
                new StrategyExecutionRuleResponse("目标价", "回踩策略目标价为 close * 1.10；突破策略目标价为 close * 1.12。"),
                new StrategyExecutionRuleResponse("仓位建议", "强势环境建议投入 10000 元；可交易环境建议投入 8000 元；弱机会环境只允许回踩策略按 8000 元以内执行；股数向下取整到 100 股。"),
                new StrategyExecutionRuleResponse("候选降级", "不在可交易池、市场不允许、评分不足或盈亏比不足时，候选会降级为强观察、学习观察或普通观察。"),
                new StrategyExecutionRuleResponse("信号生成", "只有候选股可执行、市场环境允许、风险收益比达标且最近一次回测通过时，才生成交易信号。")
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
            return new BacktestOverviewResponse("a-share-20k-v2", 0, 0m, 0m, 0m, 0m, 0m, 0m, 0, 0m, 0, false, ["缺少回测记录"], []);
        }

        return new BacktestOverviewResponse(
            "a-share-20k-v2",
            trades.Count,
            Math.Round(trades.Count(static item => item.ReturnPct > 0m) * 100m / trades.Count, 2, MidpointRounding.AwayFromZero),
            Math.Round(trades.Average(static item => item.ReturnPct), 2, MidpointRounding.AwayFromZero),
            Math.Round(trades.Average(static item => item.MaxGainPct), 2, MidpointRounding.AwayFromZero),
            Math.Round(trades.Average(static item => item.MaxDrawdownPct), 2, MidpointRounding.AwayFromZero),
            0m,
            100m,
            0,
            0m,
            0,
            true,
            [],
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
                ? await ensureLatestSnapshot.RebuildLatestAsync(syncState, snapshotVersion, cancellationToken)
                : await ensureLatestSnapshot.ExecuteAsync(syncState, snapshotVersion, cancellationToken);
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
    IDomainSyncRunRepository domainSyncRunRepository,
    IBacktestRunRepository backtestRunRepository)
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
        var backtestApproval = BacktestApprovalPolicy.Resolve(await backtestRunRepository.GetLatestRunAsync(cancellationToken));
        var isSignalEligible = (regime?.IsSignalEligible ?? false) && backtestApproval.IsApproved;

        return new TaskCenterOverviewResponse(
            tradeDate.Value,
            snapshotVersion.ToValue(),
            snapshotVersion.ToDisplayName(),
            latestSuccessfulIngestionAtUtc,
            syncStatus,
            candidates.Count,
            signals.Count,
            MarketResponseMapper.FormatMarketRegime(regime?.Regime),
            isSignalEligible,
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
            new TaskScheduleItemResponse("收盘正式版采集", "每周一至周五 16:30（Asia/Shanghai）", "收盘后采集股票快照、日线、指数与行业数据，并生成正式结果。"),
            new TaskScheduleItemResponse("收盘补拉任务", "每周一至周五 18:30（Asia/Shanghai）", "若上游仍未发布当日日频数据，则继续补拉，直到原始交易日更新为当天。"),
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

        var latestIndustryRun = recentCollectorRuns
            .Where(static item => item.TargetScope == "sync-daily-industries" || item.TargetScope == "bootstrap-industries")
            .OrderByDescending(static item => item.CreatedAtUtc)
            .FirstOrDefault();
        if (latestIndustryRun is not null && !latestIndustryRun.IsComplete)
        {
            messages.Add("行业数据不完整，正式评分会继续沿用最近完整交易日。");
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
        indicator ??= await BuildIndicatorSnapshotFallbackAsync(stockCode, resolvedDate.Value, cancellationToken);
        var financial = await marketRepository.GetLatestFinancialAsync(stockCode, cancellationToken);
        var stockFundFlow = (await marketRepository.GetStockFundFlowsByCodesAsync(resolvedDate.Value, [stockCode], cancellationToken)).GetValueOrDefault(stockCode);
        var candidate = await marketRepository.GetCandidateAsync(stockCode, resolvedDate.Value, snapshotVersion, cancellationToken);
        var signal = await marketRepository.GetSignalAsync(stockCode, resolvedDate.Value, snapshotVersion, cancellationToken);
        if (latestBarHistory.Count == 0)
        {
            return null;
        }

        var latestBar = latestBarHistory.OrderBy(static item => item.TradeDate).Last();
        var scoringIndustryName = profile.EffectiveScoringIndustryName;
        var industry = await ResolveIndustryAsync(scoringIndustryName, resolvedDate.Value, cancellationToken);
        var industryFundFlow = string.IsNullOrWhiteSpace(scoringIndustryName)
            ? null
            : (await marketRepository.GetIndustryFundFlowsByNamesAsync(resolvedDate.Value, [scoringIndustryName], cancellationToken)).GetValueOrDefault(scoringIndustryName);
        var lhb = (await marketRepository.GetLhbSnapshotsByCodesAsync(resolvedDate.Value, [stockCode], cancellationToken)).GetValueOrDefault(stockCode);
        var regime = await marketRepository.GetMarketRegimeAsync(resolvedDate.Value, snapshotVersion, cancellationToken)
            ?? new MarketRegimeSnapshot(resolvedDate.Value, MarketSignalEligibility.NoTrade, 0, false, "暂无市场环境快照");
        var detailHistory = await marketRepository.GetDailyBarHistoryAsync(stockCode, resolvedDate.Value, 80, cancellationToken);
        var orderedDetailHistory = detailHistory.OrderBy(static item => item.TradeDate).ToList();
        var detailReturn10d = IndicatorMath.CalculateReturn(orderedDetailHistory, 10);
        var detailAmountRatio1d = orderedDetailHistory.Count >= 21
            ? IndicatorMath.CalculateAmountRatio(orderedDetailHistory, 20)
            : 0m;
        var detailMa60Previous = orderedDetailHistory.Count >= 70
            ? IndicatorMath.CalculateSimpleMovingAverage(orderedDetailHistory.Take(orderedDetailHistory.Count - 10).ToList(), 60)
            : 0m;
        var detailIndexHistory = await marketRepository.GetIndexBarHistoryAsync(resolvedDate.Value, 80, cancellationToken);
        var orderedDetailIndexBars = detailIndexHistory
            .GroupBy(static item => item.IndexCode)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderBy(item => item.TradeDate).ToList());
        var detailBenchmarkIndex = orderedDetailIndexBars.TryGetValue("000300", out var detailCsi300)
            ? detailCsi300
            : orderedDetailIndexBars.Values.FirstOrDefault();
        var detailIndexReturn20d = detailBenchmarkIndex is not null ? IndicatorMath.CalculateIndexReturn(detailBenchmarkIndex, 20) : 0m;
        var detailIndexReturn60d = detailBenchmarkIndex is not null ? IndicatorMath.CalculateIndexReturn(detailBenchmarkIndex, 60) : 0m;
        var detailContext = new CandidateScoringContext(
            detailReturn10d,
            detailIndexReturn20d,
            detailIndexReturn60d,
            industry?.PctChange20d,
            indicator is not null && detailMa60Previous > 0m && indicator.Ma60 > detailMa60Previous,
            detailAmountRatio1d,
            stockFundFlow,
            industryFundFlow,
            lhb);
        CandidateListItemResponse? candidateResponse = null;
        if (candidate is not null)
        {
            // 详情页按最新规则重建一次评分拆解，避免数据库中仅有汇总分而缺少逐条明细。
            var scoreBreakdown = indicator is null
                ? candidate.ScoreBreakdown
                : CandidatePolicy.DescribeScoreBreakdown(profile, indicator, financial, industry, detailContext);

            candidateResponse = new CandidateListItemResponse(
                candidate.StockCode,
                candidate.StockName,
                candidate.IndustryName,
                MarketResponseMapper.FormatCandidateGrade(candidate.Grade),
                MarketResponseMapper.FormatStrategyType(candidate.StrategyType),
                candidate.IsTradable,
                MarketResponseMapper.FormatEligibilityStatus(candidate.EligibilityStatus),
                MarketResponseMapper.BuildEligibilityReasons(candidate.EligibilityReason),
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
                MarketResponseMapper.BuildScoreRuleDetails(scoreBreakdown),
                TradeExecutionPlanFactory.BuildForCandidate(candidate));
        }
        else if (indicator is not null)
        {
            var preview = CandidatePolicy.DescribeCandidatePreview(
                resolvedDate.Value,
                profile,
                indicator,
                financial,
                industry,
                regime,
                TradingBoardClassifier.IsInTradablePool(profile.StockCode, new TradingPermissionsOptions()),
                detailContext);

            candidateResponse = new CandidateListItemResponse(
                preview.StockCode,
                preview.StockName,
                preview.IndustryName,
                MarketResponseMapper.FormatCandidateGrade(preview.Grade),
                MarketResponseMapper.FormatStrategyType(preview.StrategyType),
                preview.IsTradable,
                MarketResponseMapper.FormatEligibilityStatus(preview.EligibilityStatus),
                MarketResponseMapper.BuildEligibilityReasons(preview.EligibilityReason),
                preview.TotalScore,
                preview.ScoreBreakdown,
                preview.Close,
                preview.Ma20,
                preview.Ma60,
                preview.Atr14,
                preview.RelativeStrengthScore,
                preview.StopLossPrice,
                preview.TargetPrice,
                preview.RiskRewardRatio,
                preview.Explanation,
                MarketResponseMapper.BuildScoreRuleDetails(preview.ScoreBreakdown),
                TradeExecutionPlanFactory.BuildForPreview(preview));
        }

        return new StockDetailResponse(
            stockCode,
            profile.StockName,
            profile.IndustryName,
            resolvedDate.Value,
            snapshotVersion.ToValue(),
            snapshotVersion.ToDisplayName(),
            new PriceSeriesResponse(latestBar.TradeDate, latestBar.Open, latestBar.High, latestBar.Low, latestBar.Close, latestBar.Volume, latestBar.Amount, indicator?.Ma20, indicator?.Ma60, indicator?.Ma120),
            financial is null
                ? null
                : new FinancialSummaryResponse(financial.ReportDate, financial.Pe ?? profile.Pe, financial.Pb ?? profile.Pb, financial.Roe, financial.RevenueYoy, financial.NetProfitYoy, financial.OperatingCashFlow, financial.GrossMargin, financial.DebtToAssetRatio, financial.OperatingCashFlowNet, financial.AnnouncementDate, financial.DataSourcePriority),
            indicator is null ? null : new IndicatorSummaryResponse(indicator.Close, indicator.Ma20, indicator.Ma60, indicator.Ma120, indicator.Atr14, indicator.Return20d, indicator.Return60d, indicator.RelativeStrengthScore, indicator.Is20DayBreakout, indicator.IsMa20Upward, indicator.IsBullishStacked, indicator.DistanceToMa20Pct),
            stockFundFlow is null && industryFundFlow is null
                ? null
                : new FundFlowSummaryResponse(
                    resolvedDate.Value,
                    stockFundFlow?.MainNetAmount,
                    stockFundFlow?.MainNetPct,
                    stockFundFlow?.SuperLargeNetAmount,
                    stockFundFlow?.SuperLargeNetPct,
                    stockFundFlow?.RankPercentile5d,
                    industryFundFlow?.MainNetAmount,
                    industryFundFlow?.MainNetPct,
                    industryFundFlow?.Rank,
                    industryFundFlow?.RankPercentile),
            lhb is null
                ? null
                : new LhbSummaryResponse(
                    lhb.IsOnLhbToday,
                    lhb.TradeDate,
                    lhb.Reason,
                    lhb.NetAmount,
                    lhb.InstitutionNetAmount,
                    lhb.InstitutionBuyCount,
                    lhb.IsInstitutionNetBuy,
                    lhb.Recent20dLhbCount,
                    lhb.DaysSinceLastLhb,
                    lhb.RiskFlags),
            candidateResponse,
            signal is null ? null : new SignalListItemResponse(signal.StockCode, signal.StockName, signal.IndustryName, MarketResponseMapper.FormatStrategyType(signal.StrategyType), MarketResponseMapper.FormatEligibilityStatus(signal.EligibilityStatus), MarketResponseMapper.BuildEligibilityReasons(signal.EligibilityReason), signal.TotalScore, signal.ScoreBreakdown, signal.TriggerPrice, signal.StopLossPrice, signal.TargetPrice, signal.RiskRewardRatio, signal.SuggestedCapital, signal.EstimatedShares, signal.Explanation, signal.GeneratedAtUtc, TradeExecutionPlanFactory.BuildForSignal(signal)),
            latestBarHistory
                .OrderBy(static item => item.TradeDate)
                .Select(item => new PriceSeriesResponse(item.TradeDate, item.Open, item.High, item.Low, item.Close, item.Volume, item.Amount, item.TradeDate == resolvedDate.Value ? indicator?.Ma20 : null, item.TradeDate == resolvedDate.Value ? indicator?.Ma60 : null, item.TradeDate == resolvedDate.Value ? indicator?.Ma120 : null))
                .ToList(),
            profile.ScoringIndustryName);
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

    private async Task<IndicatorSnapshot?> BuildIndicatorSnapshotFallbackAsync(string stockCode, DateOnly tradeDate, CancellationToken cancellationToken)
    {
        var history = await marketRepository.GetDailyBarHistoryAsync(stockCode, tradeDate, 140, cancellationToken);
        var ordered = history
            .Where(item => item.TradeDate <= tradeDate)
            .OrderBy(static item => item.TradeDate)
            .ToList();

        if (ordered.Count < 120)
        {
            return null;
        }

        var latest = ordered[^1];
        var ma20 = IndicatorMath.CalculateSimpleMovingAverage(ordered, 20);
        var ma60 = IndicatorMath.CalculateSimpleMovingAverage(ordered, 60);
        var ma120 = IndicatorMath.CalculateSimpleMovingAverage(ordered, 120);
        var atr14 = IndicatorMath.CalculateAtr14(ordered);
        var return20 = IndicatorMath.CalculateReturn(ordered, 20);
        var return60 = IndicatorMath.CalculateReturn(ordered, 60);
        var distanceToMa20 = ma20 == 0m ? 0m : ((latest.Close - ma20) / ma20) * 100m;
        var previousMa20 = ordered.Count > 30
            ? IndicatorMath.CalculateSimpleMovingAverage(ordered.Take(ordered.Count - 10).ToList(), 20)
            : 0m;
        var breakoutClose = ordered.TakeLast(20).Max(static item => item.Close);

        return new IndicatorSnapshot(
            stockCode,
            tradeDate,
            latest.Close,
            ma20,
            ma60,
            ma120,
            atr14,
            return20,
            return60,
            0m,
            latest.Close >= breakoutClose,
            ma20 > previousMa20,
            ma20 > ma60 && ma60 > ma120,
            Math.Round(distanceToMa20, 4, MidpointRounding.AwayFromZero),
            latest.TurnoverRate);
    }
}

internal static class TradeExecutionPlanFactory
{
    internal static TradeExecutionPlanResponse BuildForCandidate(CandidateStock candidate)
        => Build(
            candidate.StrategyType,
            candidate.EligibilityStatus,
            candidate.Close,
            candidate.Close,
            candidate.StopLossPrice,
            candidate.TargetPrice,
            candidate.RiskRewardRatio,
            null,
            null);

    internal static TradeExecutionPlanResponse BuildForPreview(CandidateListPreview preview)
        => Build(
            preview.StrategyType,
            preview.EligibilityStatus,
            preview.Close,
            preview.Close,
            preview.StopLossPrice,
            preview.TargetPrice,
            preview.RiskRewardRatio,
            null,
            null);

    internal static TradeExecutionPlanResponse BuildForSignal(TradeSignal signal)
        => Build(
            signal.StrategyType,
            signal.EligibilityStatus,
            signal.TriggerPrice,
            signal.TriggerPrice,
            signal.StopLossPrice,
            signal.TargetPrice,
            signal.RiskRewardRatio,
            signal.SuggestedCapital,
            signal.EstimatedShares);

    internal static TradeExecutionPlanResponse BuildForPosition(
        string strategyType,
        decimal entryPrice,
        decimal stopLossPrice,
        decimal targetPrice,
        decimal investedCapital,
        int quantity)
    {
        var normalizedType = strategyType.Contains("回踩", StringComparison.OrdinalIgnoreCase)
            ? StrategyType.PullbackToMa20
            : StrategyType.Breakout;
        var rr = CalculateRiskReward(entryPrice, stopLossPrice, targetPrice);
        return Build(normalizedType, "tradable", entryPrice, entryPrice, stopLossPrice, targetPrice, rr, investedCapital, quantity);
    }

    private static TradeExecutionPlanResponse Build(
        StrategyType strategyType,
        string eligibilityStatus,
        decimal referencePrice,
        decimal triggerPrice,
        decimal stopLossPrice,
        decimal targetPrice,
        decimal riskRewardRatio,
        decimal? suggestedCapital,
        int? estimatedShares)
    {
        var isPullback = strategyType is StrategyType.PullbackToMa20 or StrategyType.WatchPullback;
        var isWatch = strategyType is StrategyType.WatchBreakout or StrategyType.WatchPullback;
        var observationDays = isPullback ? 3 : 2;
        var maxHoldingDays = isPullback ? 10 : 6;
        var maxEntryGapPct = isPullback ? 2m : 3m;
        var planType = MarketResponseMapper.FormatStrategyType(strategyType);
        var status = MarketResponseMapper.FormatEligibilityStatus(eligibilityStatus);
        var summary = isPullback
            ? $"优先等回踩 MA20 一带止跌确认，观察 {observationDays} 个交易日，最长持有 {maxHoldingDays} 天。"
            : $"优先等突破后延续确认，观察 {observationDays} 个交易日，最长持有 {maxHoldingDays} 天。";

        var entryRules = new List<ExecutionPlanRuleResponse>
        {
            new("入场价格", $"{triggerPrice:0.##}", isPullback ? "接近 MA20 后再观察是否止跌转强，不在明显跌破时接飞刀。" : "只在突破价附近参与，避免偏离过大时追高。"),
            new("允许高开幅度", $"{maxEntryGapPct:0.##}%", isPullback ? "高开超过 2% 放弃，避免回踩策略变成追涨。" : "高开超过 3% 放弃，避免突破后溢价过高。"),
            new("观察窗口", $"{observationDays} 天", "信号出来后若迟迟不触发或形态走弱，这笔计划自动作废。"),
        };

        var holdRules = new List<ExecutionPlanRuleResponse>
        {
            new("持仓上限", $"{maxHoldingDays} 天", "到期仍未明显走强则按纪律退出，避免拖成中长期被动持仓。"),
            new("持有原则", isPullback ? "站稳 MA20 再看扩散" : "放量突破后看延续", "有利润时优先看趋势是否延续，不因盘中小波动频繁来回。"),
        };

        var exitRules = new List<ExecutionPlanRuleResponse>
        {
            new("止损价", $"{stopLossPrice:0.##}", "跌破止损价优先退出，不做主观扛单。"),
            new("目标价", $"{targetPrice:0.##}", "触达目标区可分批兑现，剩余仓位再观察是否有更强趋势。"),
            new("风险收益比", $"{riskRewardRatio:0.##}", "低于 2 的计划不值得重仓执行。"),
        };

        var invalidationRules = new List<ExecutionPlanRuleResponse>
        {
            new("形态失效", isPullback ? "跌破 MA20 并无修复" : "突破后回落失守触发区", "说明交易逻辑本身不再成立。"),
            new("高开失效", $">{maxEntryGapPct:0.##}% 高开", "开盘溢价过大时，盈亏比通常会迅速恶化。"),
            new("超时失效", $"{observationDays} 天未触发", "不把旧信号无限延长到后续交易日。"),
        };

        return new TradeExecutionPlanResponse(
            planType,
            status,
            summary,
            Math.Round(referencePrice, 4, MidpointRounding.AwayFromZero),
            Math.Round(triggerPrice, 4, MidpointRounding.AwayFromZero),
            Math.Round(stopLossPrice, 4, MidpointRounding.AwayFromZero),
            Math.Round(targetPrice, 4, MidpointRounding.AwayFromZero),
            Math.Round(riskRewardRatio, 4, MidpointRounding.AwayFromZero),
            suggestedCapital,
            estimatedShares,
            observationDays,
            maxHoldingDays,
            maxEntryGapPct,
            entryRules,
            holdRules,
            exitRules,
            invalidationRules);
    }

    private static decimal CalculateRiskReward(decimal triggerPrice, decimal stopLossPrice, decimal targetPrice)
    {
        var risk = triggerPrice - stopLossPrice;
        if (risk <= 0m)
        {
            return 0m;
        }

        return Math.Round((targetPrice - triggerPrice) / risk, 4, MidpointRounding.AwayFromZero);
    }
}

/// <summary>
/// 列表查询辅助逻辑。
/// </summary>
internal static class MarketListQueryPaging
{
    internal static IReadOnlyList<TradeSignal> ResolveVisibleSignals(
        IReadOnlyList<TradeSignal> rawSignals,
        IReadOnlyList<CandidateStock> candidates,
        MarketRegimeSnapshot? regime)
    {
        if (rawSignals.Count > 0)
        {
            return rawSignals;
        }

        if (regime is null)
        {
            return [];
        }

        return candidates
            .Where(static item => item.IsTradable)
            .Select(candidate => CandidatePolicy.BuildSignal(candidate, regime))
            .Where(static item => item is not null)
            .Cast<TradeSignal>()
            .ToList();
    }

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

    internal static string FormatEligibilityStatus(string status)
    {
        return status switch
        {
            "tradable" => "可执行",
            "strong_watch" => "强观察",
            "study_only" => "学习观察",
            "observe_only" => "观察",
            _ => string.IsNullOrWhiteSpace(status) ? "观察" : status
        };
    }

    internal static IReadOnlyList<string> BuildEligibilityReasons(string? reason)
    {
        return string.IsNullOrWhiteSpace(reason) ? [] : [reason];
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

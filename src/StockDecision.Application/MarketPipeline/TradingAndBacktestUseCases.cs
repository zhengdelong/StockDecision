using StockDecision.Application.Contracts;
using StockDecision.Domain.Market;
using StockDecision.Domain.Strategy;

namespace StockDecision.Application.MarketPipeline;

/// <summary>
/// 读取当前模拟持仓与历史交易记录。
/// </summary>
public sealed class GetSimulatedPositionsUseCase(
    ISimulatedTradingRepository tradingRepository,
    IMarketDataRepository marketRepository)
{
    /// <summary>
    /// 返回当前持仓列表，并补充最新浮盈浮亏。
    /// </summary>
    public async Task<IReadOnlyList<SimulatedPositionItemResponse>> ExecuteAsync(bool includeClosed, CancellationToken cancellationToken = default)
    {
        var positions = await tradingRepository.GetPositionsAsync(includeClosed, cancellationToken);
        var latestTradeDate = await marketRepository.GetLatestImportedTradeDateAsync(cancellationToken);
        var latestPriceMap = await BuildLatestPriceMapAsync(positions, latestTradeDate, cancellationToken);

        return positions
            .Select(position => BuildPositionResponse(position, latestPriceMap.TryGetValue(position.StockCode, out var latest) ? latest : null))
            .OrderByDescending(static item => item.OpenedAtUtc)
            .ToList();
    }

    /// <summary>
    /// 返回历史交易流水。
    /// </summary>
    public Task<IReadOnlyList<SimulatedTradeHistoryItemResponse>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        return tradingRepository.GetHistoryAsync(cancellationToken);
    }

    private async Task<Dictionary<string, (decimal Price, DateOnly TradeDate)>> BuildLatestPriceMapAsync(
        IReadOnlyList<SimulatedPositionState> positions,
        DateOnly? latestTradeDate,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, (decimal Price, DateOnly TradeDate)>(StringComparer.OrdinalIgnoreCase);
        if (latestTradeDate is null)
        {
            return result;
        }

        foreach (var stockCode in positions.Select(static item => item.StockCode).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var bars = await marketRepository.GetRecentImportedDailyBarsAsync(stockCode, latestTradeDate.Value, 1, cancellationToken);
            var latest = bars.LastOrDefault();
            if (latest is not null)
            {
                result[stockCode] = (latest.Close, latest.TradeDate);
            }
        }

        return result;
    }

    private static SimulatedPositionItemResponse BuildPositionResponse(
        SimulatedPositionState position,
        (decimal Price, DateOnly TradeDate)? latestQuote)
    {
        var latestPrice = latestQuote?.Price;
        var latestTradeDate = latestQuote?.TradeDate;
        var floatingProfitAmount = 0m;
        var floatingProfitPct = 0m;
        var comparisonDate = latestTradeDate ?? position.ClosedTradeDate ?? position.TradeDate;
        var heldDays = Math.Max(1, comparisonDate.DayNumber - position.TradeDate.DayNumber + 1);

        if (position.Status == "持有中" && latestPrice is decimal currentPrice)
        {
            floatingProfitAmount = Math.Round((currentPrice - position.EntryPrice) * position.Quantity, 2, MidpointRounding.AwayFromZero);
            floatingProfitPct = position.EntryPrice == 0m
                ? 0m
                : Math.Round((currentPrice - position.EntryPrice) / position.EntryPrice * 100m, 2, MidpointRounding.AwayFromZero);
        }

        var executionPlan = TradeExecutionPlanFactory.BuildForPosition(
            position.StrategyType,
            position.EntryPrice,
            position.StopLossPrice,
            position.TargetPrice,
            position.InvestedCapital,
            position.Quantity);
        var advice = PositionAdviceFactory.Build(
            position.Status,
            heldDays,
            latestPrice ?? position.EntryPrice,
            position.EntryPrice,
            position.StopLossPrice,
            position.TargetPrice,
            floatingProfitPct,
            executionPlan);

        return new SimulatedPositionItemResponse(
            position.Id,
            position.StockCode,
            position.StockName,
            position.IndustryName,
            position.StrategyType,
            position.SnapshotVersion,
            position.TradeDate,
            position.EntryPrice,
            position.StopLossPrice,
            position.TargetPrice,
            position.Quantity,
            position.InvestedCapital,
            latestPrice,
            latestTradeDate,
            floatingProfitAmount,
            floatingProfitPct,
            heldDays,
            position.Status,
            advice.Status,
            advice.Title,
            advice.Text,
            advice.Tags,
            executionPlan,
            position.OpenedAtUtc,
            position.ClosedAtUtc,
            position.ExitPrice,
            position.RealizedProfitAmount,
            position.RealizedProfitPct,
            position.Notes);
    }
}

/// <summary>
/// 创建模拟买入记录。
/// </summary>
public sealed class CreateSimulatedBuyUseCase(
    EnsureLatestMarketSnapshotUseCase ensureLatestSnapshot,
    IMarketDataRepository marketRepository,
    ISimulatedTradingRepository tradingRepository)
{
    /// <summary>
    /// 按交易信号生成一笔模拟买入。
    /// </summary>
    public async Task<SimulatedPositionItemResponse> ExecuteAsync(SimulateBuyRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.StockCode))
        {
            throw new InvalidOperationException("股票代码不能为空。");
        }

        var snapshotVersion = StrategySnapshotVersionCodec.ParseOrDefault(request.SnapshotVersion);
        var tradeDate = request.TradeDate ?? await ensureLatestSnapshot.ExecuteAsync(snapshotVersion, cancellationToken);
        if (tradeDate is null)
        {
            throw new InvalidOperationException("当前没有可用的交易快照，无法创建模拟买入。");
        }

        var signal = await marketRepository.GetSignalAsync(request.StockCode, tradeDate.Value, snapshotVersion, cancellationToken);
        if (signal is null)
        {
            throw new InvalidOperationException("当前股票在该交易日没有可执行信号，不能模拟买入。");
        }

        var quantity = request.Quantity ?? signal.EstimatedShares;
        if (quantity <= 0)
        {
            throw new InvalidOperationException("买入股数必须大于 0。");
        }

        var entryPrice = request.EntryPrice ?? signal.TriggerPrice;
        if (entryPrice <= 0m)
        {
            throw new InvalidOperationException("买入价格必须大于 0。");
        }

        var investedCapital = Math.Round(entryPrice * quantity, 2, MidpointRounding.AwayFromZero);
        if (investedCapital < 3_000m || investedCapital > 12_000m)
        {
            throw new InvalidOperationException("模拟买入金额必须控制在 3000 到 12000 元之间。");
        }

        var positionId = await tradingRepository.AddPositionAsync(
            new SimulatedPositionDraft(
                signal.StockCode,
                signal.StockName,
                signal.IndustryName,
                MarketResponseMapper.FormatStrategyType(signal.StrategyType),
                snapshotVersion.ToValue(),
                tradeDate.Value,
                entryPrice,
                signal.StopLossPrice,
                signal.TargetPrice,
                quantity,
                investedCapital,
                "持有中",
                DateTime.UtcNow,
                request.Notes),
            cancellationToken);

        await tradingRepository.AddTradeHistoryAsync(
            new SimulatedTradeHistoryDraft(
                positionId,
                "买入",
                signal.StockCode,
                signal.StockName,
                tradeDate.Value,
                entryPrice,
                quantity,
                investedCapital,
                $"按 {MarketResponseMapper.FormatStrategyType(signal.StrategyType)} 信号建立模拟持仓。",
                DateTime.UtcNow),
            cancellationToken);

        var position = await tradingRepository.GetPositionAsync(positionId, cancellationToken)
            ?? throw new InvalidOperationException("模拟买入已写入，但读取结果失败。");

        return new SimulatedPositionItemResponse(
            position.Id,
            position.StockCode,
            position.StockName,
            position.IndustryName,
            position.StrategyType,
            position.SnapshotVersion,
            position.TradeDate,
            position.EntryPrice,
            position.StopLossPrice,
            position.TargetPrice,
            position.Quantity,
            position.InvestedCapital,
            position.EntryPrice,
            position.TradeDate,
            0m,
            0m,
            1,
            position.Status,
            "new",
            "新开仓，先按计划执行",
            "买入后先观察是否按预期延续，不要在第一天因为小波动频繁改计划。",
            ["按计划执行", "观察延续"],
            TradeExecutionPlanFactory.BuildForPosition(position.StrategyType, position.EntryPrice, position.StopLossPrice, position.TargetPrice, position.InvestedCapital, position.Quantity),
            position.OpenedAtUtc,
            position.ClosedAtUtc,
            position.ExitPrice,
            position.RealizedProfitAmount,
            position.RealizedProfitPct,
            position.Notes);
    }
}

/// <summary>
/// 执行模拟卖出。
/// </summary>
public sealed class SellSimulatedPositionUseCase(
    ISimulatedTradingRepository tradingRepository,
    IMarketDataRepository marketRepository)
{
    /// <summary>
    /// 关闭指定持仓并写入交易流水。
    /// </summary>
    public async Task<SimulatedPositionItemResponse> ExecuteAsync(int positionId, SimulateSellRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await tradingRepository.GetPositionAsync(positionId, cancellationToken);
        if (existing is null)
        {
            throw new InvalidOperationException("指定持仓不存在。");
        }

        if (existing.Status != "持有中")
        {
            throw new InvalidOperationException("该持仓已经结束，不能重复卖出。");
        }

        var tradeDate = request.TradeDate;
        var exitPrice = request.ExitPrice;
        if (tradeDate is null || exitPrice is null)
        {
            var latestTradeDate = await marketRepository.GetLatestImportedTradeDateAsync(cancellationToken);
            if (latestTradeDate is null)
            {
                throw new InvalidOperationException("当前没有可用行情，无法自动补全卖出价格。");
            }

            var latestBars = await marketRepository.GetRecentImportedDailyBarsAsync(existing.StockCode, latestTradeDate.Value, 1, cancellationToken);
            var latestBar = latestBars.LastOrDefault();
            if (latestBar is null)
            {
                throw new InvalidOperationException("当前股票没有最新日线，无法执行模拟卖出。");
            }

            tradeDate ??= latestBar.TradeDate;
            exitPrice ??= latestBar.Close;
        }

        if (exitPrice <= 0m)
        {
            throw new InvalidOperationException("卖出价格必须大于 0。");
        }

        var closed = await tradingRepository.ClosePositionAsync(positionId, tradeDate.Value, exitPrice.Value, request.Notes, cancellationToken)
            ?? throw new InvalidOperationException("卖出失败，持仓未找到。");

        await tradingRepository.AddTradeHistoryAsync(
            new SimulatedTradeHistoryDraft(
                closed.Id,
                "卖出",
                closed.StockCode,
                closed.StockName,
                tradeDate.Value,
                exitPrice.Value,
                closed.Quantity,
                Math.Round(exitPrice.Value * closed.Quantity, 2, MidpointRounding.AwayFromZero),
                "手动执行模拟卖出。",
                DateTime.UtcNow),
            cancellationToken);

        return new SimulatedPositionItemResponse(
            closed.Id,
            closed.StockCode,
            closed.StockName,
            closed.IndustryName,
            closed.StrategyType,
            closed.SnapshotVersion,
            closed.TradeDate,
            closed.EntryPrice,
            closed.StopLossPrice,
            closed.TargetPrice,
            closed.Quantity,
            closed.InvestedCapital,
            exitPrice.Value,
            tradeDate.Value,
            0m,
            0m,
            Math.Max(1, tradeDate.Value.DayNumber - closed.TradeDate.DayNumber + 1),
            closed.Status,
            "closed",
            "仓位已结束",
            "这笔模拟仓位已经卖出，后续重点复盘是否按原计划执行。",
            ["已平仓", "待复盘"],
            TradeExecutionPlanFactory.BuildForPosition(closed.StrategyType, closed.EntryPrice, closed.StopLossPrice, closed.TargetPrice, closed.InvestedCapital, closed.Quantity),
            closed.OpenedAtUtc,
            closed.ClosedAtUtc,
            closed.ExitPrice,
            closed.RealizedProfitAmount,
            closed.RealizedProfitPct,
            closed.Notes);
    }
}

internal static class PositionAdviceFactory
{
    internal sealed record PositionAdvice(string Status, string Title, string Text, IReadOnlyList<string> Tags);

    internal static PositionAdvice Build(
        string positionStatus,
        int heldDays,
        decimal latestPrice,
        decimal entryPrice,
        decimal stopLossPrice,
        decimal targetPrice,
        decimal floatingProfitPct,
        TradeExecutionPlanResponse executionPlan)
    {
        if (!string.Equals(positionStatus, "持有中", StringComparison.OrdinalIgnoreCase))
        {
            return new PositionAdvice("closed", "仓位已结束", "该仓位已结束，优先复盘是否严格执行了原计划。", ["已结束"]);
        }

        var distanceToStopPct = latestPrice <= 0m ? 999m : (latestPrice - stopLossPrice) / latestPrice * 100m;
        var distanceToTargetPct = latestPrice <= 0m ? 999m : (targetPrice - latestPrice) / latestPrice * 100m;

        if (latestPrice <= stopLossPrice || distanceToStopPct <= 1.5m)
        {
            return new PositionAdvice(
                "risk",
                "接近止损，先执行风控",
                "当前价格已经非常接近止损位，这时优先减小亏损，而不是期待情绪化反弹。",
                ["止损优先", "禁止扛单"]);
        }

        if (latestPrice >= targetPrice || distanceToTargetPct <= 2m)
        {
            return new PositionAdvice(
                "take_profit",
                "接近目标位，准备兑现",
                "价格已接近目标区，优先考虑分批止盈，把纸面利润落袋，而不是临时上调预期。",
                ["分批止盈", "保护利润"]);
        }

        if (heldDays >= executionPlan.MaxHoldingDays)
        {
            return new PositionAdvice(
                "timeout",
                "超过持仓上限，按计划退出",
                $"这笔仓位已持有 {heldDays} 天，超过计划上限 {executionPlan.MaxHoldingDays} 天，趋势若仍未明显扩散，应按纪律退出。",
                ["时间止损", "纪律退出"]);
        }

        if (floatingProfitPct >= 5m)
        {
            return new PositionAdvice(
                "profit",
                "浮盈中，观察趋势延续",
                "仓位已有较明显浮盈，先观察是否继续沿着原计划运行，不要因为短线噪音过早清仓。",
                ["浮盈跟踪", "避免乱动"]);
        }

        return new PositionAdvice(
            "hold",
            "仍在计划内，继续观察",
            "当前位置还在计划容忍区间内，核心是继续跟踪是否站稳关键价位，并严格保留止损纪律。",
            ["计划内", "继续观察"]);
    }
}

/// <summary>
/// 执行并保存回测结果。
/// </summary>
public sealed class RunBacktestUseCase(IMarketDataRepository marketRepository, IBacktestRunRepository backtestRunRepository)
{
    private const string StrategyVersion = "a-share-20k-v2";
    private const decimal InitialAccountCapital = 20_000m;
    private const decimal PreferredPositionCapital = 10_000m;
    private const decimal MinimumPositionCapital = 6_000m;
    private const decimal MaximumLossPerTrade = 400m;
    private const int LotSize = 100;
    private const decimal BuySlippageRate = 0.001m;
    private const decimal SellSlippageRate = 0.001m;
    private const decimal CommissionRate = 0.0003m;
    private const decimal StampDutyRate = 0.0005m;
    private const decimal MinimumCommission = 5m;

    /// <summary>
    /// 基于已生成的交易信号执行一次同步回测。
    /// </summary>
    public async Task<BacktestRunDetailResponse> ExecuteAsync(RunBacktestRequest request, CancellationToken cancellationToken = default)
    {
        if (request.EndDate < request.StartDate)
        {
            throw new InvalidOperationException("回测结束日期不能早于开始日期。");
        }

        var snapshotVersion = StrategySnapshotVersionCodec.ParseOrDefault(request.SnapshotVersion);
        var recentTradeDates = await marketRepository.GetRecentTradeDatesAsync(400, cancellationToken);
        var tradeDates = recentTradeDates
            .Where(item => item >= request.StartDate && item <= request.EndDate)
            .OrderBy(static item => item)
            .ToList();

        if (tradeDates.Count == 0)
        {
            throw new InvalidOperationException("指定区间内没有可用交易日。");
        }

        var trades = new List<BacktestTradeItemResponse>();
        var equityCurve = new List<BacktestEquityPointResponse>();
        var accountCapital = InitialAccountCapital;
        var peakAccountCapital = InitialAccountCapital;
        var maxDrawdownPct = 0m;
        var skippedTradeDays = 0;
        var executedTradeDates = new HashSet<DateOnly>();

        // 回测直接按用户选择的区间扫描已有信号，不再额外跳过前 120 个交易日。
        // 技术指标和候选/信号本身已经在快照阶段算好，这里只负责按区间回放交易结果。
        foreach (var tradeDate in tradeDates)
        {
            var signals = await marketRepository.GetSignalsAsync(tradeDate, snapshotVersion, cancellationToken);
            foreach (var signal in signals.Take(Math.Clamp(request.MaxSignalsPerDay, 1, 20)))
            {
                var forwardBars = await marketRepository.GetForwardDailyBarsAsync(signal.StockCode, tradeDate, Math.Clamp(request.MaxHoldingDays, 1, 20), cancellationToken);
                if (forwardBars.Count == 0)
                {
                    skippedTradeDays++;
                    continue;
                }

                var simulation = SimulateTrade(signal, forwardBars, accountCapital);
                if (simulation is null)
                {
                    skippedTradeDays++;
                    continue;
                }

                trades.Add(simulation.Trade);
                executedTradeDates.Add(tradeDate);
                accountCapital = Math.Round(accountCapital + simulation.ProfitAmount, 2, MidpointRounding.AwayFromZero);
                peakAccountCapital = Math.Max(peakAccountCapital, accountCapital);
                var currentDrawdownPct = peakAccountCapital == 0m ? 0m : (accountCapital - peakAccountCapital) / peakAccountCapital * 100m;
                maxDrawdownPct = Math.Min(maxDrawdownPct, currentDrawdownPct);
                equityCurve.Add(new BacktestEquityPointResponse(
                    simulation.Trade.TradeDate,
                    accountCapital,
                    Math.Round((accountCapital - InitialAccountCapital) / InitialAccountCapital * 100m, 2, MidpointRounding.AwayFromZero)));
            }
        }

        if (trades.Count == 0)
        {
            throw new InvalidOperationException("当前回测区间内没有可回放的交易样本，请扩大日期范围，或等待更多交易日信号与后续日线数据。");
        }

        var averageHoldingDays = trades.Count == 0
            ? 0m
            : Math.Round(trades.Average(static item => (decimal)item.MaxHoldingDays), 2, MidpointRounding.AwayFromZero);
        var benchmarkReturnPct = await CalculateBenchmarkReturnPctAsync(request.StartDate, request.EndDate, cancellationToken);
        var detail = BuildBacktestDetail(
            0,
            request.StartDate,
            request.EndDate,
            snapshotVersion,
            trades,
            equityCurve,
            maxDrawdownPct,
            averageHoldingDays,
            benchmarkReturnPct,
            skippedTradeDays,
            tradeDates.Count,
            DateTime.UtcNow);
        var runId = await backtestRunRepository.AddRunAsync(
            new BacktestRunDraft(
                StrategyVersion,
                snapshotVersion.ToValue(),
                detail.StartDate,
                detail.EndDate,
                detail.SampleTradeCount,
                detail.WinRatePct,
                detail.AverageReturnPct,
                detail.AverageMaxGainPct,
                detail.AverageMaxDrawdownPct,
                detail.ProfitLossRatio,
                detail.MaxDrawdownPct,
                detail.TotalReturnPct,
                detail.BenchmarkReturnPct,
                detail.DataCoveragePct,
                detail.SkippedTradeDays,
                detail.AnnualTradeCount,
                detail.MaxConsecutiveLosses,
                detail.IsApproved,
                string.Join("|", detail.FailureReasons),
                detail.AverageHoldingDays,
                detail.CreatedAtUtc,
                detail.EquityCurve,
                detail.Trades),
            cancellationToken);

        return detail with { Id = runId };
    }

    /// <summary>
    /// 返回最近一次回测概览，用于兼容现有首页卡片。
    /// </summary>
    public async Task<BacktestOverviewResponse> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var latest = await backtestRunRepository.GetLatestRunAsync(cancellationToken);
        if (latest is null)
        {
            return new BacktestOverviewResponse(StrategyVersion, 0, 0m, 0m, 0m, 0m, 0m, 0m, 0, 0m, 0, false, ["缺少回测记录"], []);
        }

        return new BacktestOverviewResponse(
            latest.StrategyVersion,
            latest.SampleTradeCount,
            latest.WinRatePct,
            latest.AverageReturnPct,
            latest.AverageMaxGainPct,
            latest.AverageMaxDrawdownPct,
            latest.BenchmarkReturnPct,
            latest.DataCoveragePct,
            latest.SkippedTradeDays,
            latest.AnnualTradeCount,
            latest.MaxConsecutiveLosses,
            latest.IsApproved,
            latest.FailureReasons,
            latest.Trades);
    }

    private static BacktestRunDetailResponse BuildBacktestDetail(
        int id,
        DateOnly startDate,
        DateOnly endDate,
        StrategySnapshotVersion snapshotVersion,
        IReadOnlyList<BacktestTradeItemResponse> trades,
        IReadOnlyList<BacktestEquityPointResponse> equityCurve,
        decimal maxDrawdownPct,
        decimal averageHoldingDays,
        decimal benchmarkReturnPct,
        int skippedTradeDays,
        int totalTradeDays,
        DateTime createdAtUtc)
    {
        if (trades.Count == 0)
        {
            return new BacktestRunDetailResponse(
                id,
                StrategyVersion,
                snapshotVersion.ToValue(),
                startDate,
                endDate,
                0,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                skippedTradeDays,
                0m,
                0,
                false,
                ["缺少可回放交易样本"],
                averageHoldingDays,
                createdAtUtc,
                [],
                []);
        }

        var wins = trades.Where(static item => item.ReturnPct > 0m).ToList();
        var losses = trades.Where(static item => item.ReturnPct < 0m).ToList();
        var averageWin = wins.Count == 0 ? 0m : wins.Average(static item => item.ReturnPct);
        var averageLoss = losses.Count == 0 ? 0m : Math.Abs(losses.Average(static item => item.ReturnPct));
        var ratio = averageLoss == 0m ? 0m : averageWin / averageLoss;
        var totalReturnPct = Math.Round(equityCurve.LastOrDefault()?.ReturnPct ?? 0m, 2, MidpointRounding.AwayFromZero);
        var annualTradeCount = CalculateAnnualTradeCount(startDate, endDate, trades.Count);
        var dataCoveragePct = totalTradeDays <= 0
            ? 0m
            : Math.Round((totalTradeDays - skippedTradeDays) * 100m / totalTradeDays, 2, MidpointRounding.AwayFromZero);
        var maxConsecutiveLosses = CalculateMaxConsecutiveLosses(trades);
        var failureReasons = BuildFailureReasons(
            totalReturnPct,
            benchmarkReturnPct,
            ratio,
            wins.Count * 100m / trades.Count,
            Math.Abs(maxDrawdownPct),
            annualTradeCount);
        var isApproved = failureReasons.Count == 0;

        return new BacktestRunDetailResponse(
            id,
            StrategyVersion,
            snapshotVersion.ToValue(),
            startDate,
            endDate,
            trades.Count,
            Math.Round(wins.Count * 100m / trades.Count, 2, MidpointRounding.AwayFromZero),
            Math.Round(trades.Average(static item => item.ReturnPct), 2, MidpointRounding.AwayFromZero),
            Math.Round(trades.Average(static item => item.MaxGainPct), 2, MidpointRounding.AwayFromZero),
            Math.Round(trades.Average(static item => item.MaxDrawdownPct), 2, MidpointRounding.AwayFromZero),
            Math.Round(ratio, 2, MidpointRounding.AwayFromZero),
            Math.Round(Math.Abs(maxDrawdownPct), 2, MidpointRounding.AwayFromZero),
            totalReturnPct,
            benchmarkReturnPct,
            dataCoveragePct,
            skippedTradeDays,
            annualTradeCount,
            maxConsecutiveLosses,
            isApproved,
            failureReasons,
            averageHoldingDays,
            createdAtUtc,
            equityCurve,
            trades.OrderByDescending(static item => item.TradeDate).ThenBy(static item => item.StockCode).ToList());
    }

    private async Task<decimal> CalculateBenchmarkReturnPctAsync(DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken)
    {
        var history = await marketRepository.GetIndexBarHistoryAsync(endDate, 2_000, cancellationToken);
        var benchmarkCandidates = history
            .Where(item => item.TradeDate >= startDate && item.TradeDate <= endDate && (item.IndexCode == "000300" || item.IndexCode == "000905"))
            .GroupBy(item => item.IndexCode)
            .Select(group =>
            {
                var ordered = group.OrderBy(item => item.TradeDate).ToList();
                if (ordered.Count < 2 || ordered[0].Close == 0m)
                {
                    return (decimal?)null;
                }

                return Math.Round((ordered[^1].Close - ordered[0].Close) / ordered[0].Close * 100m, 2, MidpointRounding.AwayFromZero);
            })
            .Where(item => item.HasValue)
            .Select(item => item!.Value)
            .ToList();

        return benchmarkCandidates.Count == 0 ? 0m : benchmarkCandidates.Max();
    }

    private static decimal CalculateAnnualTradeCount(DateOnly startDate, DateOnly endDate, int tradeCount)
    {
        var daySpan = Math.Max(1, endDate.DayNumber - startDate.DayNumber + 1);
        var years = daySpan / 365m;
        return Math.Round(tradeCount / years, 2, MidpointRounding.AwayFromZero);
    }

    private static int CalculateMaxConsecutiveLosses(IReadOnlyList<BacktestTradeItemResponse> trades)
    {
        var ordered = trades.OrderBy(item => item.TradeDate).ThenBy(item => item.StockCode).ToList();
        var current = 0;
        var max = 0;
        foreach (var trade in ordered)
        {
            if (trade.ReturnPct < 0m)
            {
                current++;
                max = Math.Max(max, current);
            }
            else
            {
                current = 0;
            }
        }

        return max;
    }

    private static IReadOnlyList<string> BuildFailureReasons(
        decimal totalReturnPct,
        decimal benchmarkReturnPct,
        decimal profitLossRatio,
        decimal winRatePct,
        decimal maxDrawdownPct,
        decimal annualTradeCount)
    {
        var reasons = new List<string>();
        if (totalReturnPct <= 0m)
        {
            reasons.Add("扣成本后总收益未转正");
        }

        if (benchmarkReturnPct > 0m && totalReturnPct <= benchmarkReturnPct)
        {
            reasons.Add("未跑赢可用基准指数");
        }

        if (profitLossRatio < 2m)
        {
            reasons.Add("盈亏比低于 2");
        }

        if (winRatePct < 40m)
        {
            reasons.Add("胜率低于 40%");
        }

        if (maxDrawdownPct > 20m)
        {
            reasons.Add("最大回撤超过 20%");
        }

        if (annualTradeCount < 12m || annualTradeCount > 50m)
        {
            reasons.Add("年化交易次数不在 12 到 50 次之间");
        }

        return reasons;
    }

    /// <summary>
    /// 用保守顺序模拟单笔交易，优先判定止损。
    /// </summary>
    internal static BacktestSimulationResult? SimulateTrade(TradeSignal signal, IReadOnlyList<DailyBar> forwardBars, decimal accountCapital)
    {
        var firstBar = forwardBars[0];
        if (ShouldSkipByGapOpen(signal, firstBar))
        {
            return null;
        }

        var entryPrice = Math.Round(firstBar.Open * (1m + BuySlippageRate), 4, MidpointRounding.AwayFromZero);
        var positionPlan = BuildPositionPlan(signal, entryPrice, accountCapital);
        if (positionPlan is null)
        {
            return null;
        }

        var exitPrice = forwardBars[^1].Close;
        var hitTarget = false;
        var hitStopLoss = false;
        var maxGainPct = decimal.MinValue;
        var maxDrawdownPct = decimal.MaxValue;
        var holdingDays = 0;

        for (var index = 0; index < forwardBars.Count; index++)
        {
            var bar = forwardBars[index];
            var gainPct = entryPrice == 0m ? 0m : (bar.High - entryPrice) / entryPrice * 100m;
            var drawdownPct = entryPrice == 0m ? 0m : (bar.Low - entryPrice) / entryPrice * 100m;
            maxGainPct = Math.Max(maxGainPct, gainPct);
            maxDrawdownPct = Math.Min(maxDrawdownPct, drawdownPct);
            holdingDays = index + 1;

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

        var adjustedExitPrice = Math.Round(exitPrice * (1m - SellSlippageRate), 4, MidpointRounding.AwayFromZero);
        var grossBuyAmount = entryPrice * positionPlan.Quantity;
        var grossSellAmount = adjustedExitPrice * positionPlan.Quantity;
        var buyCommission = CalculateCommission(grossBuyAmount);
        var sellCommission = CalculateCommission(grossSellAmount);
        var stampDuty = grossSellAmount * StampDutyRate;
        var netBuyAmount = grossBuyAmount + buyCommission;
        var netSellAmount = grossSellAmount - sellCommission - stampDuty;
        var profitAmount = netSellAmount - netBuyAmount;
        var returnPct = netBuyAmount == 0m ? 0m : (netSellAmount - netBuyAmount) / netBuyAmount * 100m;

        return new BacktestSimulationResult(
            new BacktestTradeItemResponse(
                signal.TradeDate,
                signal.StockCode,
                signal.StockName,
                MarketResponseMapper.FormatStrategyType(signal.StrategyType),
                entryPrice,
                adjustedExitPrice,
                Math.Round(returnPct, 2, MidpointRounding.AwayFromZero),
                Math.Round(maxGainPct == decimal.MinValue ? 0m : maxGainPct, 2, MidpointRounding.AwayFromZero),
                Math.Round(maxDrawdownPct == decimal.MaxValue ? 0m : maxDrawdownPct, 2, MidpointRounding.AwayFromZero),
                hitTarget,
                hitStopLoss,
                positionPlan.Quantity,
                Math.Round(netBuyAmount, 2, MidpointRounding.AwayFromZero),
                Math.Round(profitAmount, 2, MidpointRounding.AwayFromZero),
                holdingDays),
            Math.Round(profitAmount, 2, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// 按 2 万元账户、100 股整数手和单笔最大亏损规则生成仓位计划。
    /// </summary>
    private static BacktestPositionPlan? BuildPositionPlan(TradeSignal signal, decimal entryPrice, decimal accountCapital)
    {
        if (entryPrice <= 0m || signal.StopLossPrice <= 0m || signal.StopLossPrice >= entryPrice || accountCapital < MinimumPositionCapital)
        {
            return null;
        }

        var suggestedCapital = signal.SuggestedCapital <= 0m ? PreferredPositionCapital : signal.SuggestedCapital;
        var targetCapital = Math.Min(accountCapital, Math.Min(PreferredPositionCapital, Math.Max(suggestedCapital, MinimumPositionCapital)));
        if (targetCapital < MinimumPositionCapital)
        {
            return null;
        }

        var riskPerShare = entryPrice - signal.StopLossPrice;
        if (riskPerShare <= 0m)
        {
            return null;
        }

        var maxSharesByCapital = (int)Math.Floor(targetCapital / entryPrice / LotSize) * LotSize;
        var maxSharesByRisk = (int)Math.Floor(MaximumLossPerTrade / riskPerShare / LotSize) * LotSize;
        var plannedShares = Math.Min(maxSharesByCapital, maxSharesByRisk);
        if (plannedShares < LotSize)
        {
            return null;
        }

        var plannedCapital = Math.Round(entryPrice * plannedShares + CalculateCommission(entryPrice * plannedShares), 2, MidpointRounding.AwayFromZero);
        if (plannedCapital < MinimumPositionCapital || plannedCapital > accountCapital)
        {
            return null;
        }

        return new BacktestPositionPlan(plannedShares, plannedCapital);
    }

    private static decimal CalculateCommission(decimal amount)
    {
        if (amount <= 0m)
        {
            return 0m;
        }

        return Math.Max(amount * CommissionRate, MinimumCommission);
    }

    private static bool ShouldSkipByGapOpen(TradeSignal signal, DailyBar firstBar)
    {
        if (signal.TriggerPrice <= 0m || firstBar.Open <= 0m)
        {
            return true;
        }

        var gapOpenPct = (firstBar.Open - signal.TriggerPrice) / signal.TriggerPrice * 100m;
        var maxGapPct = signal.StrategyType is StrategyType.PullbackToMa20 or StrategyType.WatchPullback ? 2m : 3m;
        return gapOpenPct > maxGapPct;
    }

    internal sealed record BacktestPositionPlan(int Quantity, decimal PlannedCapital);

    internal sealed record BacktestSimulationResult(BacktestTradeItemResponse Trade, decimal ProfitAmount);
}

/// <summary>
/// 读取回测列表。
/// </summary>
public sealed class GetBacktestRunsUseCase(IBacktestRunRepository backtestRunRepository)
{
    /// <summary>
    /// 返回历史回测列表。
    /// </summary>
    public Task<IReadOnlyList<BacktestRunListItemResponse>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        return backtestRunRepository.GetRunsAsync(cancellationToken);
    }
}

/// <summary>
/// 读取单次回测详情。
/// </summary>
public sealed class GetBacktestRunDetailUseCase(IBacktestRunRepository backtestRunRepository)
{
    /// <summary>
    /// 返回单次回测结果详情。
    /// </summary>
    public Task<BacktestRunDetailResponse?> ExecuteAsync(int id, CancellationToken cancellationToken = default)
    {
        return backtestRunRepository.GetRunAsync(id, cancellationToken);
    }
}

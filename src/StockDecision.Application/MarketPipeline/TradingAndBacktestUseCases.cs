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

        if (position.Status == "持有中" && latestPrice is decimal currentPrice)
        {
            floatingProfitAmount = Math.Round((currentPrice - position.EntryPrice) * position.Quantity, 2, MidpointRounding.AwayFromZero);
            floatingProfitPct = position.EntryPrice == 0m
                ? 0m
                : Math.Round((currentPrice - position.EntryPrice) / position.EntryPrice * 100m, 2, MidpointRounding.AwayFromZero);
        }

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
            position.Status,
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
            position.Status,
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
            closed.Status,
            closed.OpenedAtUtc,
            closed.ClosedAtUtc,
            closed.ExitPrice,
            closed.RealizedProfitAmount,
            closed.RealizedProfitPct,
            closed.Notes);
    }
}

/// <summary>
/// 执行并保存回测结果。
/// </summary>
public sealed class RunBacktestUseCase(IMarketDataRepository marketRepository, IBacktestRunRepository backtestRunRepository)
{
    private const string StrategyVersion = "a-share-20k-v1";

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
        var equity = 100m;
        var peakEquity = 100m;
        var maxDrawdownPct = 0m;

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
                    continue;
                }

                var trade = SimulateTrade(signal, forwardBars);
                trades.Add(trade);
                equity *= 1m + trade.ReturnPct / 100m;
                peakEquity = Math.Max(peakEquity, equity);
                var currentDrawdownPct = peakEquity == 0m ? 0m : (equity - peakEquity) / peakEquity * 100m;
                maxDrawdownPct = Math.Min(maxDrawdownPct, currentDrawdownPct);
                equityCurve.Add(new BacktestEquityPointResponse(trade.TradeDate, Math.Round(equity, 4, MidpointRounding.AwayFromZero), Math.Round((equity - 100m) / 100m * 100m, 2, MidpointRounding.AwayFromZero)));
            }
        }

        if (trades.Count == 0)
        {
            throw new InvalidOperationException("当前回测区间内没有可回放的交易样本，请扩大日期范围，或等待更多交易日信号与后续日线数据。");
        }

        var detail = BuildBacktestDetail(0, request.StartDate, request.EndDate, snapshotVersion, trades, equityCurve, maxDrawdownPct, DateTime.UtcNow);
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
            return new BacktestOverviewResponse(StrategyVersion, 0, 0m, 0m, 0m, 0m, []);
        }

        return new BacktestOverviewResponse(
            latest.StrategyVersion,
            latest.SampleTradeCount,
            latest.WinRatePct,
            latest.AverageReturnPct,
            latest.AverageMaxGainPct,
            latest.AverageMaxDrawdownPct,
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
                createdAtUtc,
                [],
                []);
        }

        var wins = trades.Where(static item => item.ReturnPct > 0m).ToList();
        var losses = trades.Where(static item => item.ReturnPct < 0m).ToList();
        var averageWin = wins.Count == 0 ? 0m : wins.Average(static item => item.ReturnPct);
        var averageLoss = losses.Count == 0 ? 0m : Math.Abs(losses.Average(static item => item.ReturnPct));
        var ratio = averageLoss == 0m ? 0m : averageWin / averageLoss;

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
            Math.Round(equityCurve.LastOrDefault()?.ReturnPct ?? 0m, 2, MidpointRounding.AwayFromZero),
            5m,
            createdAtUtc,
            equityCurve,
            trades.OrderByDescending(static item => item.TradeDate).ThenBy(static item => item.StockCode).ToList());
    }

    /// <summary>
    /// 用保守顺序模拟单笔交易，优先判定止损。
    /// </summary>
    internal static BacktestTradeItemResponse SimulateTrade(TradeSignal signal, IReadOnlyList<DailyBar> forwardBars)
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
            hitStopLoss);
    }
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

using Microsoft.EntityFrameworkCore;
using StockDecision.Application.Contracts;

namespace StockDecision.Infrastructure.Persistence;

/// <summary>
/// 基于 EF Core 的模拟交易仓储实现。
/// </summary>
public sealed class EfSimulatedTradingRepository(StockDecisionDbContext dbContext) : ISimulatedTradingRepository
{
    /// <summary>
    /// 新增模拟持仓。
    /// </summary>
    public async Task<int> AddPositionAsync(SimulatedPositionDraft draft, CancellationToken cancellationToken)
    {
        var entity = new SimulatedPositionEntity
        {
            StockCode = draft.StockCode,
            StockName = draft.StockName,
            IndustryName = draft.IndustryName,
            StrategyType = draft.StrategyType,
            SnapshotVersion = draft.SnapshotVersion,
            TradeDate = draft.TradeDate,
            EntryPrice = draft.EntryPrice,
            StopLossPrice = draft.StopLossPrice,
            TargetPrice = draft.TargetPrice,
            Quantity = draft.Quantity,
            InvestedCapital = draft.InvestedCapital,
            Status = draft.Status,
            OpenedAtUtc = draft.OpenedAtUtc,
            Notes = draft.Notes
        };

        dbContext.SimulatedPositions.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    /// <summary>
    /// 读取模拟持仓列表。
    /// </summary>
    public async Task<IReadOnlyList<SimulatedPositionState>> GetPositionsAsync(bool includeClosed, CancellationToken cancellationToken)
    {
        var query = dbContext.SimulatedPositions.AsQueryable();
        if (!includeClosed)
        {
            query = query.Where(static item => item.Status == "持有中");
        }

        return await query
            .OrderByDescending(static item => item.OpenedAtUtc)
            .Select(static item => new SimulatedPositionState(
                item.Id,
                item.StockCode,
                item.StockName,
                item.IndustryName,
                item.StrategyType,
                item.SnapshotVersion,
                item.TradeDate,
                item.EntryPrice,
                item.StopLossPrice,
                item.TargetPrice,
                item.Quantity,
                item.InvestedCapital,
                item.Status,
                item.OpenedAtUtc,
                item.ClosedAtUtc,
                item.ClosedTradeDate,
                item.ExitPrice,
                item.RealizedProfitAmount,
                item.RealizedProfitPct,
                item.Notes))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 读取单个持仓。
    /// </summary>
    public async Task<SimulatedPositionState?> GetPositionAsync(int id, CancellationToken cancellationToken)
    {
        return await dbContext.SimulatedPositions
            .Where(item => item.Id == id)
            .Select(static item => new SimulatedPositionState(
                item.Id,
                item.StockCode,
                item.StockName,
                item.IndustryName,
                item.StrategyType,
                item.SnapshotVersion,
                item.TradeDate,
                item.EntryPrice,
                item.StopLossPrice,
                item.TargetPrice,
                item.Quantity,
                item.InvestedCapital,
                item.Status,
                item.OpenedAtUtc,
                item.ClosedAtUtc,
                item.ClosedTradeDate,
                item.ExitPrice,
                item.RealizedProfitAmount,
                item.RealizedProfitPct,
                item.Notes))
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// 平掉持仓并计算已实现盈亏。
    /// </summary>
    public async Task<SimulatedPositionState?> ClosePositionAsync(int id, DateOnly tradeDate, decimal exitPrice, string? notes, CancellationToken cancellationToken)
    {
        var entity = await dbContext.SimulatedPositions.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        var realizedAmount = Math.Round((exitPrice - entity.EntryPrice) * entity.Quantity, 2, MidpointRounding.AwayFromZero);
        var realizedPct = entity.EntryPrice == 0m
            ? 0m
            : Math.Round((exitPrice - entity.EntryPrice) / entity.EntryPrice * 100m, 2, MidpointRounding.AwayFromZero);

        entity.Status = exitPrice <= entity.StopLossPrice
            ? "已触发止损"
            : exitPrice >= entity.TargetPrice
                ? "已达到止盈目标"
                : "已手动卖出";
        entity.ClosedTradeDate = tradeDate;
        entity.ClosedAtUtc = DateTime.UtcNow;
        entity.ExitPrice = exitPrice;
        entity.RealizedProfitAmount = realizedAmount;
        entity.RealizedProfitPct = realizedPct;
        entity.Notes = notes;

        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetPositionAsync(id, cancellationToken);
    }

    /// <summary>
    /// 新增交易流水。
    /// </summary>
    public async Task AddTradeHistoryAsync(SimulatedTradeHistoryDraft draft, CancellationToken cancellationToken)
    {
        dbContext.SimulatedTradeHistories.Add(new SimulatedTradeHistoryEntity
        {
            PositionId = draft.PositionId,
            ActionType = draft.ActionType,
            StockCode = draft.StockCode,
            StockName = draft.StockName,
            TradeDate = draft.TradeDate,
            Price = draft.Price,
            Quantity = draft.Quantity,
            Amount = draft.Amount,
            Summary = draft.Summary,
            CreatedAtUtc = draft.CreatedAtUtc
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// 读取历史交易流水。
    /// </summary>
    public async Task<IReadOnlyList<SimulatedTradeHistoryItemResponse>> GetHistoryAsync(CancellationToken cancellationToken)
    {
        return await dbContext.SimulatedTradeHistories
            .OrderByDescending(static item => item.CreatedAtUtc)
            .Select(static item => new SimulatedTradeHistoryItemResponse(
                item.Id,
                item.PositionId,
                item.ActionType,
                item.StockCode,
                item.StockName,
                item.TradeDate,
                item.Price,
                item.Quantity,
                item.Amount,
                item.Summary,
                item.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }
}

/// <summary>
/// 基于 EF Core 的回测结果仓储实现。
/// </summary>
public sealed class EfBacktestRunRepository(StockDecisionDbContext dbContext) : IBacktestRunRepository
{
    /// <summary>
    /// 新增回测结果。
    /// </summary>
    public async Task<int> AddRunAsync(BacktestRunDraft draft, CancellationToken cancellationToken)
    {
        var entity = new BacktestRunEntity
        {
            StrategyVersion = draft.StrategyVersion,
            SnapshotVersion = draft.SnapshotVersion,
            StartDate = draft.StartDate,
            EndDate = draft.EndDate,
            SampleTradeCount = draft.SampleTradeCount,
            WinRatePct = draft.WinRatePct,
            AverageReturnPct = draft.AverageReturnPct,
            AverageMaxGainPct = draft.AverageMaxGainPct,
            AverageMaxDrawdownPct = draft.AverageMaxDrawdownPct,
            ProfitLossRatio = draft.ProfitLossRatio,
            MaxDrawdownPct = draft.MaxDrawdownPct,
            TotalReturnPct = draft.TotalReturnPct,
            BenchmarkReturnPct = draft.BenchmarkReturnPct,
            DataCoveragePct = draft.DataCoveragePct,
            SkippedTradeDays = draft.SkippedTradeDays,
            AnnualTradeCount = draft.AnnualTradeCount,
            MaxConsecutiveLosses = draft.MaxConsecutiveLosses,
            IsApproved = draft.IsApproved,
            FailureReasons = draft.FailureReasons,
            AverageHoldingDays = draft.AverageHoldingDays,
            CreatedAtUtc = draft.CreatedAtUtc
        };

        dbContext.BacktestRuns.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        dbContext.BacktestTradeResults.AddRange(draft.Trades.Select(item => new BacktestTradeResultEntity
        {
            BacktestRunId = entity.Id,
            TradeDate = item.TradeDate,
            StockCode = item.StockCode,
            StockName = item.StockName,
            StrategyType = item.StrategyType,
            EntryPrice = item.EntryPrice,
            ExitPrice = item.ExitPrice,
            ReturnPct = item.ReturnPct,
            MaxGainPct = item.MaxGainPct,
            MaxDrawdownPct = item.MaxDrawdownPct,
            HitTarget = item.HitTarget,
            HitStopLoss = item.HitStopLoss
        }));

        dbContext.BacktestEquityPoints.AddRange(draft.EquityCurve.Select(item => new BacktestEquityPointEntity
        {
            BacktestRunId = entity.Id,
            TradeDate = item.TradeDate,
            Equity = item.Equity,
            ReturnPct = item.ReturnPct
        }));

        await dbContext.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    /// <summary>
    /// 读取历史回测列表。
    /// </summary>
    public async Task<IReadOnlyList<BacktestRunListItemResponse>> GetRunsAsync(CancellationToken cancellationToken)
    {
        return await dbContext.BacktestRuns
            .OrderByDescending(static item => item.CreatedAtUtc)
            .Select(static item => new BacktestRunListItemResponse(
                item.Id,
                item.StrategyVersion,
                item.SnapshotVersion,
                item.StartDate,
                item.EndDate,
                item.SampleTradeCount,
                item.WinRatePct,
                item.AverageReturnPct,
                item.ProfitLossRatio,
                item.MaxDrawdownPct,
                item.TotalReturnPct,
                item.BenchmarkReturnPct,
                item.DataCoveragePct,
                item.SkippedTradeDays,
                item.AnnualTradeCount,
                item.MaxConsecutiveLosses,
                item.IsApproved,
                item.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 读取单次回测详情。
    /// </summary>
    public async Task<BacktestRunDetailResponse?> GetRunAsync(int id, CancellationToken cancellationToken)
    {
        var run = await dbContext.BacktestRuns.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (run is null)
        {
            return null;
        }

        var trades = await dbContext.BacktestTradeResults
            .Where(item => item.BacktestRunId == id)
            .OrderByDescending(static item => item.TradeDate)
            .ThenBy(static item => item.StockCode)
            .Select(static item => new BacktestTradeItemResponse(
                item.TradeDate,
                item.StockCode,
                item.StockName,
                item.StrategyType,
                item.EntryPrice,
                item.ExitPrice,
                item.ReturnPct,
                item.MaxGainPct,
                item.MaxDrawdownPct,
                item.HitTarget,
                item.HitStopLoss,
                0,
                0m,
                0m,
                0))
            .ToListAsync(cancellationToken);

        var equityCurve = await dbContext.BacktestEquityPoints
            .Where(item => item.BacktestRunId == id)
            .OrderBy(static item => item.TradeDate)
            .Select(static item => new BacktestEquityPointResponse(item.TradeDate, item.Equity, item.ReturnPct))
            .ToListAsync(cancellationToken);

        return new BacktestRunDetailResponse(
            run.Id,
            run.StrategyVersion,
            run.SnapshotVersion,
            run.StartDate,
            run.EndDate,
            run.SampleTradeCount,
            run.WinRatePct,
            run.AverageReturnPct,
            run.AverageMaxGainPct,
            run.AverageMaxDrawdownPct,
            run.ProfitLossRatio,
            run.MaxDrawdownPct,
            run.TotalReturnPct,
            run.BenchmarkReturnPct,
            run.DataCoveragePct,
            run.SkippedTradeDays,
            run.AnnualTradeCount,
            run.MaxConsecutiveLosses,
            run.IsApproved,
            SplitFailureReasons(run.FailureReasons),
            run.AverageHoldingDays,
            run.CreatedAtUtc,
            equityCurve,
            trades);
    }

    /// <summary>
    /// 读取最近一次回测详情。
    /// </summary>
    public async Task<BacktestRunDetailResponse?> GetLatestRunAsync(CancellationToken cancellationToken)
    {
        var latestId = await dbContext.BacktestRuns
            .OrderByDescending(static item => item.CreatedAtUtc)
            .Select(static item => (int?)item.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return latestId is null ? null : await GetRunAsync(latestId.Value, cancellationToken);
    }

    private static IReadOnlyList<string> SplitFailureReasons(string raw)
    {
        return string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

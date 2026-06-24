using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using StockDecision.Application.Contracts;

namespace StockDecision.Infrastructure.Persistence;

/// <summary>
/// 基于 EF Core 的学习复盘仓储实现。
/// </summary>
public sealed class EfLearningReviewRepository(StockDecisionDbContext dbContext) : ILearningReviewRepository
{
    /// <summary>
    /// 返回全部复盘记录。
    /// </summary>
    public async Task<IReadOnlyList<LearningReviewItemResponse>> GetReviewsAsync(CancellationToken cancellationToken)
    {
        return await dbContext.LearningReviews
            .OrderByDescending(static item => item.UpdatedAtUtc)
            .Select(MapToResponse)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 按股票代码读取复盘记录。
    /// </summary>
    public async Task<IReadOnlyList<LearningReviewItemResponse>> GetReviewsByStockAsync(string stockCode, CancellationToken cancellationToken)
    {
        return await dbContext.LearningReviews
            .Where(item => item.StockCode == stockCode)
            .OrderByDescending(static item => item.UpdatedAtUtc)
            .Select(MapToResponse)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 新增或更新一条复盘记录。
    /// </summary>
    public async Task<LearningReviewItemResponse> SaveReviewAsync(LearningReviewDraft draft, CancellationToken cancellationToken)
    {
        LearningReviewEntity entity;
        if (draft.Id is int existingId)
        {
            entity = await dbContext.LearningReviews.FirstOrDefaultAsync(item => item.Id == existingId, cancellationToken)
                ?? throw new InvalidOperationException("要更新的复盘记录不存在。");
            entity.PositionId = draft.PositionId;
            entity.StockCode = draft.StockCode;
            entity.StockName = draft.StockName;
            entity.TradeDate = draft.TradeDate;
            entity.SnapshotVersion = draft.SnapshotVersion;
            entity.BuyReason = draft.BuyReason;
            entity.MarketContext = draft.MarketContext;
            entity.ExecutionDiscipline = draft.ExecutionDiscipline;
            entity.ResultSummary = draft.ResultSummary;
            entity.ImprovementPlan = draft.ImprovementPlan;
            entity.UpdatedAtUtc = draft.TimestampUtc;
        }
        else
        {
            entity = new LearningReviewEntity
            {
                PositionId = draft.PositionId,
                StockCode = draft.StockCode,
                StockName = draft.StockName,
                TradeDate = draft.TradeDate,
                SnapshotVersion = draft.SnapshotVersion,
                BuyReason = draft.BuyReason,
                MarketContext = draft.MarketContext,
                ExecutionDiscipline = draft.ExecutionDiscipline,
                ResultSummary = draft.ResultSummary,
                ImprovementPlan = draft.ImprovementPlan,
                CreatedAtUtc = draft.TimestampUtc,
                UpdatedAtUtc = draft.TimestampUtc
            };
            dbContext.LearningReviews.Add(entity);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new LearningReviewItemResponse(
            entity.Id,
            entity.PositionId,
            entity.StockCode,
            entity.StockName,
            entity.TradeDate,
            entity.SnapshotVersion,
            entity.BuyReason,
            entity.MarketContext,
            entity.ExecutionDiscipline,
            entity.ResultSummary,
            entity.ImprovementPlan,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);
    }

    private static readonly Expression<Func<LearningReviewEntity, LearningReviewItemResponse>> MapToResponse =
        entity => new LearningReviewItemResponse(
            entity.Id,
            entity.PositionId,
            entity.StockCode,
            entity.StockName,
            entity.TradeDate,
            entity.SnapshotVersion,
            entity.BuyReason,
            entity.MarketContext,
            entity.ExecutionDiscipline,
            entity.ResultSummary,
            entity.ImprovementPlan,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);
}

using StockDecision.Application.Contracts;
using StockDecision.Domain.Strategy;

namespace StockDecision.Application.MarketPipeline;

/// <summary>
/// 读取学习复盘概览。
/// </summary>
public sealed class GetLearningReviewOverviewUseCase(ILearningReviewRepository learningReviewRepository)
{
    /// <summary>
    /// 返回指定股票或全局的复盘提示与历史记录。
    /// </summary>
    public async Task<LearningReviewOverviewResponse> ExecuteAsync(
        string? stockCode,
        string? stockName,
        DateOnly? tradeDate,
        string? snapshotVersion,
        CancellationToken cancellationToken = default)
    {
        var resolvedSnapshotVersion = StrategySnapshotVersionCodec.ParseOrDefault(snapshotVersion).ToValue();
        var reviews = string.IsNullOrWhiteSpace(stockCode)
            ? await learningReviewRepository.GetReviewsAsync(cancellationToken)
            : await learningReviewRepository.GetReviewsByStockAsync(stockCode, cancellationToken);

        return new LearningReviewOverviewResponse(
            stockCode,
            stockName,
            tradeDate,
            resolvedSnapshotVersion,
            BuildReviewPrompts(),
            reviews.OrderByDescending(static item => item.UpdatedAtUtc).ToList());
    }

    /// <summary>
    /// 提供固定的复盘引导问题，帮助用户从环境、纪律和结果三个层面复盘。
    /// </summary>
    private static IReadOnlyList<string> BuildReviewPrompts()
    {
        return
        [
            "这笔交易最初吸引你的原因是什么？是趋势、回踩、突破，还是一时冲动？",
            "买入当天的大盘环境、行业环境和个股位置，是否真的支持你执行这笔交易？",
            "你有没有按照系统给出的止损、止盈和仓位建议执行，还是中途改计划了？",
            "最终结果的好坏，主要来自策略本身，还是来自执行偏差？",
            "如果再来一次，这笔交易你最想改掉的一个动作是什么？"
        ];
    }
}

/// <summary>
/// 保存学习复盘记录。
/// </summary>
public sealed class SaveLearningReviewUseCase(ILearningReviewRepository learningReviewRepository)
{
    /// <summary>
    /// 校验并保存一条学习复盘记录。
    /// </summary>
    public async Task<LearningReviewItemResponse> ExecuteAsync(SaveLearningReviewRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.StockCode))
        {
            throw new InvalidOperationException("复盘记录必须包含股票代码。");
        }

        if (string.IsNullOrWhiteSpace(request.StockName))
        {
            throw new InvalidOperationException("复盘记录必须包含股票名称。");
        }

        if (string.IsNullOrWhiteSpace(request.BuyReason))
        {
            throw new InvalidOperationException("请填写买入原因。");
        }

        if (string.IsNullOrWhiteSpace(request.ResultSummary))
        {
            throw new InvalidOperationException("请填写实际结果。");
        }

        return await learningReviewRepository.SaveReviewAsync(
            new LearningReviewDraft(
                request.Id,
                request.PositionId,
                request.StockCode,
                request.StockName,
                request.TradeDate,
                StrategySnapshotVersionCodec.ParseOrDefault(request.SnapshotVersion).ToValue(),
                request.BuyReason.Trim(),
                request.MarketContext.Trim(),
                request.ExecutionDiscipline.Trim(),
                request.ResultSummary.Trim(),
                request.ImprovementPlan.Trim(),
                DateTime.UtcNow),
            cancellationToken);
    }
}

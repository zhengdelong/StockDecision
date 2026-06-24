using Microsoft.AspNetCore.Mvc;
using StockDecision.Application.Contracts;
using StockDecision.Application.MarketPipeline;

namespace StockDecision.Api.Controllers;

/// <summary>
/// 提供学习复盘相关接口。
/// </summary>
[ApiController]
[Route("api/learning")]
public sealed class LearningController(
    GetLearningReviewOverviewUseCase getLearningReviewOverviewUseCase,
    SaveLearningReviewUseCase saveLearningReviewUseCase) : ControllerBase
{
    /// <summary>
    /// 返回学习复盘概览和历史记录。
    /// </summary>
    [HttpGet("reviews")]
    public async Task<ActionResult<LearningReviewOverviewResponse>> GetReviews(
        [FromQuery] string? stockCode,
        [FromQuery] string? stockName,
        [FromQuery] DateOnly? tradeDate,
        [FromQuery] string? snapshotVersion,
        CancellationToken cancellationToken)
    {
        return Ok(await getLearningReviewOverviewUseCase.ExecuteAsync(stockCode, stockName, tradeDate, snapshotVersion, cancellationToken));
    }

    /// <summary>
    /// 保存一条学习复盘记录。
    /// </summary>
    [HttpPost("reviews")]
    public async Task<ActionResult<LearningReviewItemResponse>> SaveReview([FromBody] SaveLearningReviewRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await saveLearningReviewUseCase.ExecuteAsync(request, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "保存复盘失败", detail: ex.Message);
        }
    }
}

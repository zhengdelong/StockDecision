using Microsoft.AspNetCore.Mvc;
using StockDecision.Application.Contracts;
using StockDecision.Application.MarketPipeline;

namespace StockDecision.Api.Controllers;

/// <summary>
/// 提供回测执行与结果读取接口。
/// </summary>
[ApiController]
[Route("api/backtests")]
public sealed class BacktestsController(
    RunBacktestUseCase runBacktestUseCase,
    GetBacktestRunsUseCase getBacktestRunsUseCase,
    GetBacktestRunDetailUseCase getBacktestRunDetailUseCase) : ControllerBase
{
    /// <summary>
    /// 兼容旧版概览接口，返回最近一次回测摘要。
    /// </summary>
    [HttpGet("overview")]
    public async Task<ActionResult<BacktestOverviewResponse>> GetOverview(CancellationToken cancellationToken)
    {
        return Ok(await runBacktestUseCase.GetOverviewAsync(cancellationToken));
    }

    /// <summary>
    /// 返回历史回测列表。
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BacktestRunListItemResponse>>> GetRuns(CancellationToken cancellationToken)
    {
        return Ok(await getBacktestRunsUseCase.ExecuteAsync(cancellationToken));
    }

    /// <summary>
    /// 返回指定回测详情。
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<BacktestRunDetailResponse>> GetById(int id, CancellationToken cancellationToken)
    {
        var response = await getBacktestRunDetailUseCase.ExecuteAsync(id, cancellationToken);
        return response is null ? NotFound() : Ok(response);
    }

    /// <summary>
    /// 执行一次同步回测。
    /// </summary>
    [HttpPost("run")]
    public async Task<ActionResult<BacktestRunDetailResponse>> Run([FromBody] RunBacktestRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await runBacktestUseCase.ExecuteAsync(request, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "回测执行失败", detail: ex.Message);
        }
    }
}

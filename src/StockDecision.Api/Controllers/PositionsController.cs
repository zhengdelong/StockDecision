using Microsoft.AspNetCore.Mvc;
using StockDecision.Application.Contracts;
using StockDecision.Application.MarketPipeline;

namespace StockDecision.Api.Controllers;

/// <summary>
/// 提供模拟持仓与交易流水接口。
/// </summary>
[ApiController]
[Route("api/positions")]
public sealed class PositionsController(
    GetSimulatedPositionsUseCase getSimulatedPositionsUseCase,
    CreateSimulatedBuyUseCase createSimulatedBuyUseCase,
    SellSimulatedPositionUseCase sellSimulatedPositionUseCase) : ControllerBase
{
    /// <summary>
    /// 返回当前模拟持仓列表。
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SimulatedPositionItemResponse>>> Get(CancellationToken cancellationToken)
    {
        return Ok(await getSimulatedPositionsUseCase.ExecuteAsync(false, cancellationToken));
    }

    /// <summary>
    /// 返回历史持仓列表。
    /// </summary>
    [HttpGet("all")]
    public async Task<ActionResult<IReadOnlyList<SimulatedPositionItemResponse>>> GetAll(CancellationToken cancellationToken)
    {
        return Ok(await getSimulatedPositionsUseCase.ExecuteAsync(true, cancellationToken));
    }

    /// <summary>
    /// 返回交易流水。
    /// </summary>
    [HttpGet("history")]
    public async Task<ActionResult<IReadOnlyList<SimulatedTradeHistoryItemResponse>>> GetHistory(CancellationToken cancellationToken)
    {
        return Ok(await getSimulatedPositionsUseCase.GetHistoryAsync(cancellationToken));
    }

    /// <summary>
    /// 创建模拟买入。
    /// </summary>
    [HttpPost("simulate-buy")]
    public async Task<ActionResult<SimulatedPositionItemResponse>> SimulateBuy([FromBody] SimulateBuyRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await createSimulatedBuyUseCase.ExecuteAsync(request, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "模拟买入失败", detail: ex.Message);
        }
    }

    /// <summary>
    /// 对指定持仓执行模拟卖出。
    /// </summary>
    [HttpPost("{id:int}/sell")]
    public async Task<ActionResult<SimulatedPositionItemResponse>> Sell(int id, [FromBody] SimulateSellRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await sellSimulatedPositionUseCase.ExecuteAsync(id, request, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            var statusCode = ex.Message.Contains("不存在", StringComparison.Ordinal) ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest;
            return Problem(statusCode: statusCode, title: "模拟卖出失败", detail: ex.Message);
        }
    }
}

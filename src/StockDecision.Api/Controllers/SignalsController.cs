using Microsoft.AspNetCore.Mvc;
using StockDecision.Application.Contracts;
using StockDecision.Application.MarketPipeline;

namespace StockDecision.Api.Controllers;

/// <summary>
/// 提供交易信号读取接口。
/// </summary>
[ApiController]
[Route("api/signals")]
public sealed class SignalsController(GetTodaySignalsUseCase getTodaySignalsUseCase) : ControllerBase
{
    /// <summary>
    /// 返回指定交易日或最新交易日的可执行交易信号。
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] SignalListQuery query, CancellationToken cancellationToken)
    {
        var response = await getTodaySignalsUseCase.ExecuteAsync(query, cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// 返回最新交易日的可执行交易信号。
    /// </summary>
    [HttpGet("today")]
    public async Task<IActionResult> GetToday(CancellationToken cancellationToken)
    {
        var response = await getTodaySignalsUseCase.ExecuteAsync(new SignalListQuery(), cancellationToken);
        return Ok(response);
    }
}

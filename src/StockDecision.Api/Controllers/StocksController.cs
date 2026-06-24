using Microsoft.AspNetCore.Mvc;
using StockDecision.Application.MarketPipeline;

namespace StockDecision.Api.Controllers;

/// <summary>
/// 提供单只股票详情接口。
/// </summary>
[ApiController]
[Route("api/stocks")]
public sealed class StocksController(GetStockDetailUseCase getStockDetailUseCase) : ControllerBase
{
    /// <summary>
    /// 返回指定股票在某个交易日的详情快照。
    /// </summary>
    [HttpGet("{code}")]
    public async Task<IActionResult> GetByCode(string code, [FromQuery] DateOnly? date, [FromQuery] string? snapshotVersion, CancellationToken cancellationToken)
    {
        _ = snapshotVersion;
        var response = await getStockDetailUseCase.ExecuteAsync(code, date, snapshotVersion, cancellationToken);
        if (response is null)
        {
            return NotFound();
        }

        return Ok(response);
    }
}

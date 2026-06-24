using Microsoft.AspNetCore.Mvc;
using StockDecision.Application.MarketPipeline;

namespace StockDecision.Api.Controllers;

/// <summary>
/// 提供首页仪表盘所需的市场与系统概览数据。
/// </summary>
[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController(GetDashboardUseCase getDashboardUseCase) : ControllerBase
{
    /// <summary>
    /// 返回最新交易日的仪表盘摘要。
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? snapshotVersion, CancellationToken cancellationToken)
    {
        _ = snapshotVersion;
        var response = await getDashboardUseCase.ExecuteAsync(cancellationToken: cancellationToken);
        return Ok(response);
    }
}

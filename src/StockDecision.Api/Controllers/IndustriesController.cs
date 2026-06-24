using Microsoft.AspNetCore.Mvc;
using StockDecision.Application.Contracts;
using StockDecision.Application.MarketPipeline;

namespace StockDecision.Api.Controllers;

/// <summary>
/// 提供行业列表查询接口。
/// </summary>
[ApiController]
[Route("api/industries")]
public sealed class IndustriesController(GetIndustriesUseCase getIndustriesUseCase) : ControllerBase
{
    /// <summary>
    /// 返回指定条件下的行业分页列表。
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] IndustryListQuery query, CancellationToken cancellationToken)
    {
        var response = await getIndustriesUseCase.ExecuteAsync(query, cancellationToken);
        return Ok(response);
    }
}

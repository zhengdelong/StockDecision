using Microsoft.AspNetCore.Mvc;
using StockDecision.Application.Contracts;
using StockDecision.Application.MarketPipeline;

namespace StockDecision.Api.Controllers;

/// <summary>
/// 提供财务列表查询接口。
/// </summary>
[ApiController]
[Route("api/financials")]
public sealed class FinancialsController(GetFinancialsUseCase getFinancialsUseCase) : ControllerBase
{
    /// <summary>
    /// 返回指定条件下的财务分页列表。
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] FinancialListQuery query, CancellationToken cancellationToken)
    {
        var response = await getFinancialsUseCase.ExecuteAsync(query, cancellationToken);
        return Ok(response);
    }
}

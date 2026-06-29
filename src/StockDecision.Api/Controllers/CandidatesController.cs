using Microsoft.AspNetCore.Mvc;
using StockDecision.Application.Contracts;
using StockDecision.Application.MarketPipeline;

namespace StockDecision.Api.Controllers;

/// <summary>
/// 提供候选股列表查询接口。
/// </summary>
[ApiController]
[Route("api/candidates")]
public sealed class CandidatesController(GetCandidatesUseCase getCandidatesUseCase) : ControllerBase
{
    /// <summary>
    /// 返回指定条件下的候选股分页列表。
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] CandidateListQuery query, CancellationToken cancellationToken)
    {
        var response = await getCandidatesUseCase.ExecuteAsync(query, cancellationToken);
        return Ok(response);
    }
}


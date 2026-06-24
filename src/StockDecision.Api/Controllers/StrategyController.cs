using Microsoft.AspNetCore.Mvc;
using StockDecision.Application.Contracts;
using StockDecision.Application.MarketPipeline;

namespace StockDecision.Api.Controllers;

/// <summary>
/// 提供策略解释接口。
/// </summary>
[ApiController]
[Route("api/strategy")]
public sealed class StrategyController(GetStrategyExplanationUseCase getStrategyExplanationUseCase) : ControllerBase
{
    /// <summary>
    /// 返回当前策略版本的解释说明。
    /// </summary>
    [HttpGet("explanation")]
    public ActionResult<StrategyExplanationResponse> GetExplanation()
    {
        return Ok(getStrategyExplanationUseCase.Execute());
    }
}

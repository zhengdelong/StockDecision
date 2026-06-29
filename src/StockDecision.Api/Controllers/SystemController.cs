using Microsoft.AspNetCore.Mvc;
using StockDecision.Application.SystemSummary;

namespace StockDecision.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class SystemController(GetSystemSummaryUseCase getSystemSummaryUseCase) : ControllerBase
{
    [HttpGet("health")]
    public IActionResult GetHealth()
    {
        return Ok(new
        {
            service = "StockDecision.Api",
            status = "正常",
            strategy = "a-share-20k-v2"
        });
    }

    [HttpGet("about")]
    public ActionResult<SystemSummaryResponse> GetAbout()
    {
        return Ok(getSystemSummaryUseCase.Execute());
    }
}

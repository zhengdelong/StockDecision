using Microsoft.AspNetCore.Mvc;
using StockDecision.Application.SystemSummary;
using StockDecision.Api.Controllers;

namespace StockDecision.Api.Tests.Controllers;

public class SystemControllerTests
{
    [Fact]
    public void GetAbout_Should_Return_System_Summary()
    {
        var controller = new SystemController(new GetSystemSummaryUseCase());

        var result = controller.GetAbout();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var summary = Assert.IsType<SystemSummaryResponse>(ok.Value);
        Assert.Equal("A-share Stock Decision System", summary.Name);
    }
}

using StockDecision.Application.SystemSummary;

namespace StockDecision.Domain.Tests.Application;

public class GetSystemSummaryUseCaseTests
{
    [Fact]
    public void Execute_Should_Return_Default_System_Summary()
    {
        var useCase = new GetSystemSummaryUseCase();

        var summary = useCase.Execute();

        Assert.Equal("A股选股与交易决策系统", summary.Name);
        Assert.Equal(20000m, summary.Capital);
        Assert.Equal("双版本选股决策支持", summary.Mode);
        Assert.Equal("a-share-20k-v1", summary.StrategyVersion);
        Assert.Equal(3, summary.Documents.Count);
        Assert.Contains("docs/stock-decision-system/00-overview.md", summary.Documents);
    }
}

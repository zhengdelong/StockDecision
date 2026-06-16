namespace StockDecision.Application.SystemSummary;

public sealed class GetSystemSummaryUseCase
{
    public SystemSummaryResponse Execute()
    {
        return new SystemSummaryResponse(
            Name: "A-share Stock Decision System",
            Capital: 20000m,
            Mode: "End-of-day decision support",
            StrategyVersion: "a-share-20k-v1",
            Documents:
            [
                "docs/stock-decision-system/00-overview.md",
                "docs/stock-decision-system/01-indicator-glossary.md",
                "docs/stock-decision-system/02-trading-strategy-20k.md"
            ]);
    }
}

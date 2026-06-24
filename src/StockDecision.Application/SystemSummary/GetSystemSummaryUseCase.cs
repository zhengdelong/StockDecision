namespace StockDecision.Application.SystemSummary;

public sealed class GetSystemSummaryUseCase
{
    private readonly SystemSummaryOptions _options;

    public GetSystemSummaryUseCase(SystemSummaryOptions? options = null)
    {
        _options = options ?? new SystemSummaryOptions();
    }

    public SystemSummaryResponse Execute()
    {
        return new SystemSummaryResponse(
            Name: _options.Name,
            Capital: _options.Capital,
            Mode: _options.Mode,
            StrategyVersion: _options.StrategyVersion,
            Documents:
            [
                "docs/stock-decision-system/00-overview.md",
                "docs/stock-decision-system/01-indicator-glossary.md",
                "docs/stock-decision-system/02-trading-strategy-20k.md"
            ]);
    }
}

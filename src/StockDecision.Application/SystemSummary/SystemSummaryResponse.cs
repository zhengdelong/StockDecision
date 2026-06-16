namespace StockDecision.Application.SystemSummary;

public sealed record SystemSummaryResponse(
    string Name,
    decimal Capital,
    string Mode,
    string StrategyVersion,
    IReadOnlyList<string> Documents);

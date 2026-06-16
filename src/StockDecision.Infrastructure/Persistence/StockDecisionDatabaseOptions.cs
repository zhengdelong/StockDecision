namespace StockDecision.Infrastructure.Persistence;

public sealed class StockDecisionDatabaseOptions
{
    public const string ConnectionStringName = "StockDecision";

    public required string ConnectionString { get; init; }
}

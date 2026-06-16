namespace StockDecision.Domain.ValueObjects;

public sealed record RiskRewardRatio
{
    private RiskRewardRatio(decimal value)
    {
        Value = value;
    }

    public decimal Value { get; }

    public static RiskRewardRatio Create(decimal expectedProfit, decimal maximumLoss)
    {
        if (maximumLoss <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumLoss), "Maximum loss must be greater than zero.");
        }

        return new RiskRewardRatio(expectedProfit / maximumLoss);
    }

    public bool IsQualified(decimal minimumRatio) => Value >= minimumRatio;
}

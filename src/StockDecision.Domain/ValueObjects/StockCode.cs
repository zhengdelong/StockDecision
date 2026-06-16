using System.Text.RegularExpressions;

namespace StockDecision.Domain.ValueObjects;

public sealed record StockCode
{
    private static readonly Regex StockCodePattern = new("^[0-9]{6}$", RegexOptions.Compiled);

    private StockCode(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static StockCode Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Stock code is required.", nameof(value));
        }

        var normalized = value.Trim();
        if (!StockCodePattern.IsMatch(normalized))
        {
            throw new ArgumentException("Stock code must be a six-digit number.", nameof(value));
        }

        return new StockCode(normalized);
    }

    public override string ToString() => Value;
}

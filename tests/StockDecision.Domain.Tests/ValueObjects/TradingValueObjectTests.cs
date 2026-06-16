using StockDecision.Domain.ValueObjects;

namespace StockDecision.Domain.Tests.ValueObjects;

public class TradingValueObjectTests
{
    [Fact]
    public void StockCode_Create_Should_Accept_Six_Digit_Code()
    {
        var stockCode = StockCode.Create("600000");

        Assert.Equal("600000", stockCode.Value);
    }

    [Fact]
    public void StockCode_Create_Should_Reject_Invalid_Code()
    {
        Assert.Throws<ArgumentException>(() => StockCode.Create("ABC"));
    }

    [Fact]
    public void RiskRewardRatio_Create_Should_Calculate_Ratio()
    {
        var ratio = RiskRewardRatio.Create(expectedProfit: 800m, maximumLoss: 400m);

        Assert.Equal(2m, ratio.Value);
    }

    [Fact]
    public void RiskRewardRatio_Create_Should_Reject_Zero_Or_Negative_Loss()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RiskRewardRatio.Create(expectedProfit: 800m, maximumLoss: 0m));
    }
}

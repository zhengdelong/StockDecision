namespace StockDecision.Application.SystemSummary;

/// <summary>
/// 系统摘要页的可配置项。
/// </summary>
public sealed class SystemSummaryOptions
{
    /// <summary>系统名称。</summary>
    public string Name { get; set; } = "A股选股与交易决策系统";

    /// <summary>初始资金。</summary>
    public decimal Capital { get; set; } = 20000m;

    /// <summary>系统模式描述。</summary>
    public string Mode { get; set; } = "双版本选股决策支持";

    /// <summary>策略版本号。</summary>
    public string StrategyVersion { get; set; } = "a-share-20k-v1";
}

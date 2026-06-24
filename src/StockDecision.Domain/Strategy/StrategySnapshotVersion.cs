namespace StockDecision.Domain.Strategy;

/// <summary>
/// 表示策略结果快照的版本。
/// </summary>
public enum StrategySnapshotVersion
{
    /// <summary>
    /// 历史保留值，当前应用层不再主动使用。
    /// </summary>
    IntradayPreview = 0,

    /// <summary>
    /// 收盘正式版，用于正式评分、信号与回测。
    /// </summary>
    EndOfDayFinal = 1
}

/// <summary>
/// 提供策略快照版本与外部字符串之间的转换。
/// </summary>
public static class StrategySnapshotVersionCodec
{
    /// <summary>
    /// 正式版查询参数值。
    /// </summary>
    public const string EndOfDayFinalValue = "end_of_day_final";

    /// <summary>
    /// 观察版查询参数值。
    /// </summary>
    public const string IntradayPreviewValue = "intraday_preview";

    /// <summary>
    /// 将版本枚举转换为稳定的接口字符串。
    /// </summary>
    public static string ToValue(this StrategySnapshotVersion version)
    {
        return version switch
        {
            StrategySnapshotVersion.IntradayPreview => IntradayPreviewValue,
            _ => EndOfDayFinalValue
        };
    }

    /// <summary>
    /// 将查询参数解析为版本枚举。
    /// 当前系统已收敛为仅正式版，外部即使传入观察版也统一按正式版处理。
    /// </summary>
    public static StrategySnapshotVersion ParseOrDefault(string? rawValue)
    {
        _ = rawValue;
        return StrategySnapshotVersion.EndOfDayFinal;
    }

    /// <summary>
    /// 将版本枚举格式化为中文标签。
    /// 当前接口对外统一展示正式版，避免把历史观察版数据误解为仍在运行的新链路。
    /// </summary>
    public static string ToDisplayName(this StrategySnapshotVersion version)
    {
        _ = version;
        return "正式版";
    }
}

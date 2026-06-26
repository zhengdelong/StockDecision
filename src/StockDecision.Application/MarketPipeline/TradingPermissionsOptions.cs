namespace StockDecision.Application.MarketPipeline;

/// <summary>
/// 账户可交易市场权限配置。
/// </summary>
public sealed class TradingPermissionsOptions
{
    /// <summary>
    /// 是否允许主板股票进入可执行结果。
    /// </summary>
    public bool EnableMainBoard { get; init; } = true;

    /// <summary>
    /// 是否允许创业板股票进入可执行结果。
    /// </summary>
    public bool EnableChiNext { get; init; } = true;
}

internal enum TradingBoard
{
    Unknown = 0,
    MainBoard = 1,
    ChiNext = 2
}

internal static class TradingBoardClassifier
{
    internal static TradingBoard Classify(string stockCode)
    {
        if (string.IsNullOrWhiteSpace(stockCode))
        {
            return TradingBoard.Unknown;
        }

        return stockCode.Trim() switch
        {
            var code when code.StartsWith("300", StringComparison.Ordinal) || code.StartsWith("301", StringComparison.Ordinal) => TradingBoard.ChiNext,
            var code when code.StartsWith("600", StringComparison.Ordinal) ||
                          code.StartsWith("601", StringComparison.Ordinal) ||
                          code.StartsWith("603", StringComparison.Ordinal) ||
                          code.StartsWith("605", StringComparison.Ordinal) ||
                          code.StartsWith("000", StringComparison.Ordinal) ||
                          code.StartsWith("001", StringComparison.Ordinal) ||
                          code.StartsWith("002", StringComparison.Ordinal) ||
                          code.StartsWith("003", StringComparison.Ordinal) => TradingBoard.MainBoard,
            _ => TradingBoard.Unknown
        };
    }

    internal static bool IsInWatchPool(string stockCode)
    {
        var board = Classify(stockCode);
        return board is TradingBoard.MainBoard or TradingBoard.ChiNext;
    }

    internal static bool IsInTradablePool(string stockCode, TradingPermissionsOptions options)
    {
        return Classify(stockCode) switch
        {
            TradingBoard.MainBoard => options.EnableMainBoard,
            TradingBoard.ChiNext => options.EnableChiNext,
            _ => false
        };
    }
}

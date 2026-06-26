using StockDecision.Application.Contracts;

namespace StockDecision.Application.MarketPipeline;

internal sealed record BacktestApprovalSnapshot(
    bool IsApproved,
    string StatusNote,
    IReadOnlyList<string> FailureReasons);

internal static class BacktestApprovalPolicy
{
    internal static BacktestApprovalSnapshot Resolve(BacktestRunDetailResponse? run)
    {
        if (run is null)
        {
            return new BacktestApprovalSnapshot(false, "还没有可用回测记录，暂不开放可执行信号。", ["缺少回测记录"]);
        }

        if (run.IsApproved)
        {
            return new BacktestApprovalSnapshot(true, "最近一次回测已通过准入标准。", []);
        }

        var reasons = run.FailureReasons.Count == 0 ? ["最近一次回测未通过准入标准"] : run.FailureReasons;
        return new BacktestApprovalSnapshot(false, $"最近一次回测未通过：{string.Join("；", reasons)}", reasons);
    }
}

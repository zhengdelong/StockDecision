namespace StockDecision.Worker;

/// <summary>
/// 领域同步后台任务配置。
/// </summary>
public sealed class DomainSyncWorkerOptions
{
    /// <summary>
    /// 轮询原始层是否有增量的时间间隔，单位秒。
    /// </summary>
    public int PollSeconds { get; set; } = 60;
}

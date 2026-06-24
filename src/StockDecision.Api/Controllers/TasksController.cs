using Microsoft.AspNetCore.Mvc;
using StockDecision.Application.Contracts;
using StockDecision.Application.MarketPipeline;

namespace StockDecision.Api.Controllers;

/// <summary>
/// 提供任务中心概览接口。
/// </summary>
[ApiController]
[Route("api/tasks")]
public sealed class TasksController(GetTaskCenterOverviewUseCase getTaskCenterOverviewUseCase) : ControllerBase
{
    /// <summary>
    /// 返回任务中心概览与最近执行记录。
    /// </summary>
    [HttpGet("overview")]
    public async Task<ActionResult<TaskCenterOverviewResponse>> GetOverview([FromQuery] string? snapshotVersion, CancellationToken cancellationToken)
    {
        _ = snapshotVersion;
        return Ok(await getTaskCenterOverviewUseCase.ExecuteAsync(cancellationToken: cancellationToken));
    }

    /// <summary>
    /// 手动触发正式版领域同步，用于立即生成收盘结果。
    /// </summary>
    [HttpPost("domain-sync")]
    public async Task<ActionResult<DomainSyncRunItemResponse>> TriggerDomainSync(
        [FromServices] RunDomainSyncUseCase runDomainSyncUseCase,
        [FromQuery] string? snapshotVersion,
        CancellationToken cancellationToken)
    {
        _ = snapshotVersion;
        var result = await runDomainSyncUseCase.ExecuteAsync(
            triggerKind: "manual",
            forceRefreshLatest: true,
            cancellationToken: cancellationToken);

        if (result is null)
        {
            return Conflict("当前没有可同步的数据。");
        }

        return Ok(new DomainSyncRunItemResponse(
            result.JobName,
            FormatTriggerKind(result.TriggerKind),
            "正式版",
            FormatRunStatus(result.Status),
            result.DataUpdated,
            result.IsSignalEligible,
            result.EffectiveTradeDate,
            result.FinancialReportDate,
            result.StartedAtUtc,
            result.FinishedAtUtc,
            result.Summary));
    }

    private static string FormatRunStatus(string status)
    {
        return status switch
        {
            "Succeeded" or "成功" => "成功",
            "Failed" or "失败" => "失败",
            "Running" or "执行中" => "执行中",
            _ => status
        };
    }

    private static string FormatTriggerKind(string triggerKind)
    {
        return triggerKind switch
        {
            "startup" => "启动时",
            "poll" => "轮询",
            "manual" => "手动",
            _ => triggerKind
        };
    }
}

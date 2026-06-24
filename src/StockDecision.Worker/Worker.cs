using Microsoft.Extensions.Options;
using StockDecision.Application.MarketPipeline;

namespace StockDecision.Worker;

/// <summary>
/// 定期检查原始层增量，并在发现新数据时同步到领域层。
/// </summary>
public sealed class Worker(
    IServiceScopeFactory scopeFactory,
    IOptions<DomainSyncWorkerOptions> options,
    ILogger<Worker> logger) : BackgroundService
{
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(Math.Max(15, options.Value.PollSeconds));

    /// <summary>
    /// 启动后先立即检查一次，随后按固定间隔轮询。
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Domain sync worker started with poll interval {PollSeconds}s.", _pollInterval.TotalSeconds);

        await ExecuteOnePassAsync("startup", true, stoppingToken);

        using var timer = new PeriodicTimer(_pollInterval);
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ExecuteOnePassAsync("poll", false, stoppingToken);
        }
    }

    /// <summary>
    /// 执行一次同步检查，只有在原始层有新内容时才会真正落库。
    /// </summary>
    private async Task ExecuteOnePassAsync(string triggerKind, bool forceRefreshLatest, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var useCase = scope.ServiceProvider.GetRequiredService<RunDomainSyncUseCase>();
            var result = await useCase.ExecuteAsync(
                triggerKind: triggerKind,
                forceRefreshLatest: forceRefreshLatest,
                cancellationToken: cancellationToken);

            if (result is null)
            {
                logger.LogDebug("Domain sync skipped because no new raw data was detected.");
                return;
            }

            logger.LogInformation(
                "Domain sync finished. Trigger={TriggerKind}, Status={Status}, TradeDate={TradeDate}, FinancialReportDate={FinancialReportDate}, Summary={Summary}",
                result.TriggerKind,
                result.Status,
                result.EffectiveTradeDate,
                result.FinancialReportDate,
                result.Summary);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Domain sync worker is stopping.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Domain sync worker pass failed.");
        }
    }
}

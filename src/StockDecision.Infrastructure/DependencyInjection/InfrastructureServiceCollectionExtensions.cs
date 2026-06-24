using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StockDecision.Application.Contracts;
using StockDecision.Application.MarketPipeline;
using StockDecision.Infrastructure.Persistence;

namespace StockDecision.Infrastructure;

/// <summary>
/// 基础设施层依赖注册入口。
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// 注册 EF Core、仓储实现和应用层用例依赖。
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(StockDecisionDatabaseOptions.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'StockDecision' is required.");
        }

        var databaseOptions = new StockDecisionDatabaseOptions
        {
            ConnectionString = connectionString
        };

        services.AddSingleton(databaseOptions);
        services.AddDbContext<StockDecisionDbContext>(options =>
            options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));
        services.AddScoped<IRawMarketDataRepository, EfRawMarketDataRepository>();
        services.AddScoped<IMarketDataRepository, EfMarketDataRepository>();
        services.AddScoped<IIngestionLogRepository, EfIngestionLogRepository>();
        services.AddScoped<IDomainSyncRunRepository, EfDomainSyncRunRepository>();
        services.AddScoped<ISimulatedTradingRepository, EfSimulatedTradingRepository>();
        services.AddScoped<IBacktestRunRepository, EfBacktestRunRepository>();
        services.AddScoped<ILearningReviewRepository, EfLearningReviewRepository>();
        services.AddScoped<EnsureLatestMarketSnapshotUseCase>();
        services.AddScoped<RunDomainSyncUseCase>();
        services.AddScoped<GetDashboardUseCase>();
        services.AddScoped<GetCandidatesUseCase>();
        services.AddScoped<GetTodaySignalsUseCase>();
        services.AddScoped<GetIndustriesUseCase>();
        services.AddScoped<GetFinancialsUseCase>();
        services.AddScoped<GetStrategyExplanationUseCase>();
        services.AddScoped<GetBacktestOverviewUseCase>();
        services.AddScoped<GetSimulatedPositionsUseCase>();
        services.AddScoped<CreateSimulatedBuyUseCase>();
        services.AddScoped<SellSimulatedPositionUseCase>();
        services.AddScoped<RunBacktestUseCase>();
        services.AddScoped<GetBacktestRunsUseCase>();
        services.AddScoped<GetBacktestRunDetailUseCase>();
        services.AddScoped<GetLearningReviewOverviewUseCase>();
        services.AddScoped<SaveLearningReviewUseCase>();
        services.AddScoped<GetTaskCenterOverviewUseCase>();
        services.AddScoped<GetStockDetailUseCase>();

        return services;
    }
}

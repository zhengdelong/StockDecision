using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StockDecision.Infrastructure.Persistence;

namespace StockDecision.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
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

        return services;
    }
}

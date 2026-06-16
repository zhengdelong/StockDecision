using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StockDecision.Infrastructure;
using StockDecision.Infrastructure.Persistence;

namespace StockDecision.Api.Tests.Infrastructure;

public class InfrastructureRegistrationTests
{
    [Fact]
    public void AddInfrastructure_Should_Register_MySql_Database_Options()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:StockDecision"] = "Server=mysql;Port=3306;Database=stock_decision;User=stock_decision;Password=stock_decision_dev;CharSet=utf8mb4;"
            })
            .Build();

        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<StockDecisionDatabaseOptions>();

        Assert.Contains("Server=mysql", options.ConnectionString, StringComparison.Ordinal);
        Assert.Contains("CharSet=utf8mb4", options.ConnectionString, StringComparison.Ordinal);
    }
}

using StockDecision.Worker;
using StockDecision.Infrastructure;
using StockDecision.Infrastructure.Persistence;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.Configure<DomainSyncWorkerOptions>(builder.Configuration.GetSection("DomainSyncWorker"));
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
await using (var scope = host.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<StockDecisionDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
    await StockDecisionSchemaInitializer.EnsureDomainTablesAsync(dbContext);
}

host.Run();

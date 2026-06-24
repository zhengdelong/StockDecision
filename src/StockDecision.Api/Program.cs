using StockDecision.Application.SystemSummary;
using StockDecision.Infrastructure;
using StockDecision.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
const string CorsPolicyName = "Frontend";

var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
var allowedOrigins = configuredOrigins
    .Concat(
    [
        "http://localhost:5173",
        "http://127.0.0.1:5173",
        "http://localhost:4173",
        "http://127.0.0.1:4173",
        "http://localhost:3000",
        "http://127.0.0.1:3000"
    ])
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton(new GetSystemSummaryUseCase(
    builder.Configuration.GetSection("SystemSummary").Get<SystemSummaryOptions>() ?? new SystemSummaryOptions()));

var app = builder.Build();

app.UseCors(CorsPolicyName);
app.UseHttpsRedirection();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<StockDecisionDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
    await StockDecisionSchemaInitializer.EnsureDomainTablesAsync(dbContext);
}

app.MapControllers();

app.Run();

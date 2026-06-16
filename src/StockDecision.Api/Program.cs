using StockDecision.Application.SystemSummary;
using StockDecision.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<GetSystemSummaryUseCase>();

var app = builder.Build();

app.UseHttpsRedirection();

app.MapControllers();

app.Run();

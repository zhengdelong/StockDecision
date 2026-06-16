using StockDecision.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseHttpsRedirection();

app.MapGet("/api/health", () => Results.Ok(new
{
    service = "StockDecision.Api",
    status = "ok",
    strategy = "a-share-20k-v1"
}));

app.MapGet("/api/about", () => Results.Ok(new
{
    name = "A股选股与交易决策系统",
    capital = 20000,
    mode = "收盘后日线决策辅助",
    documents = new[]
    {
        "docs/stock-decision-system/00-overview.md",
        "docs/stock-decision-system/01-indicator-glossary.md",
        "docs/stock-decision-system/02-trading-strategy-20k.md"
    }
}));

app.Run();

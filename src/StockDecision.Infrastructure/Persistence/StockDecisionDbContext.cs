using Microsoft.EntityFrameworkCore;

namespace StockDecision.Infrastructure.Persistence;

public sealed class StockDecisionDbContext(DbContextOptions<StockDecisionDbContext> options) : DbContext(options)
{
}

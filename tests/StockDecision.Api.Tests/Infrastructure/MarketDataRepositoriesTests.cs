using Microsoft.EntityFrameworkCore;
using StockDecision.Infrastructure.Persistence;

namespace StockDecision.Api.Tests.Infrastructure;

public sealed class MarketDataRepositoriesTests
{
    [Fact]
    public async Task GetStockProfilesByCodesAsync_ReturnsLatestTradeDatePerStock()
    {
        var options = new DbContextOptionsBuilder<StockDecisionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new StockDecisionDbContext(options);
        dbContext.MarketStockProfiles.AddRange(
            new MarketStockProfileEntity
            {
                StockCode = "000001",
                TradeDate = new DateOnly(2026, 6, 22),
                StockName = "平安银行旧",
                IndustryName = "银行",
                IsActive = true
            },
            new MarketStockProfileEntity
            {
                StockCode = "000001",
                TradeDate = new DateOnly(2026, 6, 23),
                StockName = "平安银行新",
                IndustryName = "银行",
                IsActive = true
            });
        await dbContext.SaveChangesAsync();

        var repository = new EfMarketDataRepository(dbContext);

        var result = await repository.GetStockProfilesByCodesAsync(["000001"], CancellationToken.None);

        var profile = Assert.Single(result);
        Assert.Equal("000001", profile.Key);
        Assert.Equal(new DateOnly(2026, 6, 23), profile.Value.SnapshotDate);
        Assert.Equal("平安银行新", profile.Value.StockName);
    }

    [Fact]
    public async Task GetIndustryStatsByNamesAsync_DeduplicatesDuplicateIndustryRows()
    {
        var options = new DbContextOptionsBuilder<StockDecisionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new StockDecisionDbContext(options);
        dbContext.MarketIndustryDailyStats.AddRange(
            new MarketIndustryDailyStatEntity
            {
                IndustryCode = "BK001",
                IndustryName = "银行",
                TradeDate = new DateOnly(2026, 6, 23),
                PctChange20d = 1.23m,
                Rank20d = 3
            },
            new MarketIndustryDailyStatEntity
            {
                IndustryCode = "BK002",
                IndustryName = "银行",
                TradeDate = new DateOnly(2026, 6, 23),
                PctChange20d = 1.25m,
                Rank20d = 2
            });
        await dbContext.SaveChangesAsync();

        var repository = new EfMarketDataRepository(dbContext);

        var result = await repository.GetIndustryStatsByNamesAsync(new DateOnly(2026, 6, 23), ["银行"], CancellationToken.None);

        var industry = Assert.Single(result);
        Assert.Equal("银行", industry.Key);
        Assert.NotNull(industry.Value);
    }

    [Fact]
    public async Task GetIndustryStatsAsync_DeduplicatesDuplicateIndustryRows()
    {
        var options = new DbContextOptionsBuilder<StockDecisionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new StockDecisionDbContext(options);
        dbContext.MarketIndustryDailyStats.AddRange(
            new MarketIndustryDailyStatEntity
            {
                IndustryCode = "881124",
                IndustryName = "半导体",
                TradeDate = new DateOnly(2026, 7, 1),
                PctChange20d = 5.1m,
                Rank20d = 8
            },
            new MarketIndustryDailyStatEntity
            {
                IndustryCode = "BK1037",
                IndustryName = "半导体",
                TradeDate = new DateOnly(2026, 7, 1),
                PctChange20d = 6.2m,
                Rank20d = 3
            });
        await dbContext.SaveChangesAsync();

        var repository = new EfMarketDataRepository(dbContext);

        var result = await repository.GetIndustryStatsAsync(new DateOnly(2026, 7, 1), CancellationToken.None);

        var industry = Assert.Single(result);
        Assert.Equal("半导体", industry.IndustryName);
        Assert.Equal("BK1037", industry.IndustryCode);
    }
}

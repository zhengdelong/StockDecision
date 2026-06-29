using StockDecision.Application.Contracts;
using StockDecision.Application.MarketPipeline;
using StockDecision.Domain.Market;
using StockDecision.Domain.Strategy;

namespace StockDecision.Api.Tests.UseCases;

/// <summary>
/// 覆盖领域同步相关用例的关键行为，防止交易日与财报日期判断回退。
/// </summary>
public class DomainSyncUseCasesTests
{
    /// <summary>
    /// 当交易日未变化但最新财报日期发生变化时，仍然必须触发领域同步。
    /// </summary>
    [Fact]
    public async Task EnsureLatestMarketSnapshotUseCase_Should_Sync_When_Financial_Report_Is_Newer()
    {
        var rawRepository = new FakeRawMarketDataRepository(
            latestTradeDate: new DateOnly(2026, 6, 23),
            latestFinancialReportDate: new DateOnly(2026, 3, 31));
        var marketRepository = new FakeMarketDataRepository(
            importedTradeDate: new DateOnly(2026, 6, 23),
            importedFinancialReportDate: new DateOnly(2025, 12, 31));
        var useCase = new EnsureLatestMarketSnapshotUseCase(rawRepository, marketRepository, new StubIngestionLogRepository(), new TradingPermissionsOptions());

        var tradeDate = await useCase.ExecuteAsync(cancellationToken: CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 6, 23), tradeDate);
        Assert.Equal(1, marketRepository.ReplaceSnapshotCallCount);
        Assert.Equal(new DateOnly(2026, 6, 23), marketRepository.LastReplacedTradeDate);
    }

    /// <summary>
    /// 当原始层没有任何新增内容时，不应重复写入领域同步日志。
    /// </summary>
    [Fact]
    public async Task RunDomainSyncUseCase_Should_Return_Null_When_No_Sync_Is_Required()
    {
        var rawRepository = new FakeRawMarketDataRepository(
            latestTradeDate: new DateOnly(2026, 6, 23),
            latestFinancialReportDate: new DateOnly(2026, 3, 31));
        var marketRepository = new FakeMarketDataRepository(
            importedTradeDate: new DateOnly(2026, 6, 23),
            importedFinancialReportDate: new DateOnly(2026, 3, 31));
        var runRepository = new FakeDomainSyncRunRepository();
        var ensureUseCase = new EnsureLatestMarketSnapshotUseCase(rawRepository, marketRepository, new StubIngestionLogRepository(), new TradingPermissionsOptions());
        var runUseCase = new RunDomainSyncUseCase(ensureUseCase, marketRepository, runRepository);

        var result = await runUseCase.ExecuteAsync("poll", cancellationToken: CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(runRepository.Runs);
        Assert.Equal(0, marketRepository.ReplaceSnapshotCallCount);
    }

    [Fact]
    public async Task RunDomainSyncUseCase_Should_Inspect_State_Only_Once_When_Sync_Is_Required()
    {
        var rawRepository = new FakeRawMarketDataRepository(
            latestTradeDate: new DateOnly(2026, 6, 23),
            latestFinancialReportDate: new DateOnly(2026, 3, 31));
        var marketRepository = new FakeMarketDataRepository(
            importedTradeDate: new DateOnly(2026, 6, 20),
            importedFinancialReportDate: new DateOnly(2025, 12, 31));
        var runRepository = new FakeDomainSyncRunRepository();
        var ensureUseCase = new EnsureLatestMarketSnapshotUseCase(rawRepository, marketRepository, new StubIngestionLogRepository(), new TradingPermissionsOptions());
        var runUseCase = new RunDomainSyncUseCase(ensureUseCase, marketRepository, runRepository);

        var result = await runUseCase.ExecuteAsync("poll", cancellationToken: CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, rawRepository.GetLatestTradeDateCallCount);
        Assert.Equal(1, rawRepository.GetLatestFinancialReportDateCallCount);
        Assert.Equal(1, marketRepository.GetLatestImportedTradeDateCallCount);
        Assert.Equal(1, marketRepository.GetLatestImportedFinancialReportDateCallCount);
    }

    /// <summary>
    /// 原始层仓储测试替身。
    /// </summary>
    private sealed class FakeRawMarketDataRepository(DateOnly latestTradeDate, DateOnly latestFinancialReportDate) : IRawMarketDataRepository
    {
        public int GetLatestTradeDateCallCount { get; private set; }

        public int GetLatestFinancialReportDateCallCount { get; private set; }

        public Task<DateOnly?> GetLatestTradeDateAsync(CancellationToken cancellationToken)
        {
            GetLatestTradeDateCallCount++;
            return Task.FromResult<DateOnly?>(latestTradeDate);
        }

        public Task<IReadOnlyList<StockProfile>> GetLatestStockProfilesAsync(DateOnly tradeDate, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<StockProfile>>(
            [
                new StockProfile("600001", "Alpha Tech", "Software", true, false, false, new DateOnly(2020, 1, 1), 25.36m, 28m, 3.2m, 12_000_000_000m, 4.2m, 650_000_000m, tradeDate)
            ]);

        public Task<IReadOnlyList<DailyBar>> GetDailyBarsByTradeDateAsync(DateOnly tradeDate, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DailyBar>>(
            [
                new DailyBar("600001", tradeDate, 25.00m, 25.80m, 24.90m, 25.36m, 1_580_000L, 352_000_000m, 1.04m, 4.2m)
            ]);

        public Task<IReadOnlyList<MarketIndexBar>> GetIndexBarsByTradeDateAsync(DateOnly tradeDate, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<MarketIndexBar>>(
            [
                new MarketIndexBar("000001", "上证指数", tradeDate, 3100m)
            ]);

        public Task<IReadOnlyList<IndustryDailyStat>> GetIndustryStatsByTradeDateAsync(DateOnly tradeDate, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<IndustryDailyStat>>(
            [
                new IndustryDailyStat("SW001", "Software", tradeDate, 12.6m, 3)
            ]);

        public Task<IReadOnlyList<FinancialSnapshot>> GetLatestFinancialSnapshotsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<FinancialSnapshot>>(
            [
                new FinancialSnapshot("600001", latestFinancialReportDate, 28m, 3.2m, 12.4m, 18.5m, 21.8m, 12_000_000_000m)
            ]);

        public Task<IReadOnlyList<StockFundFlowSnapshot>> GetStockFundFlowsByTradeDateAsync(DateOnly tradeDate, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<StockFundFlowSnapshot>>([]);

        public Task<IReadOnlyList<IndustryFundFlowSnapshot>> GetIndustryFundFlowsByTradeDateAsync(DateOnly tradeDate, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<IndustryFundFlowSnapshot>>([]);

        public Task<IReadOnlyList<LhbSnapshot>> GetLhbSnapshotsByTradeDateAsync(DateOnly tradeDate, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<LhbSnapshot>>([]);

        public Task<DateOnly?> GetLatestFinancialReportDateAsync(CancellationToken cancellationToken)
        {
            GetLatestFinancialReportDateCallCount++;
            return Task.FromResult<DateOnly?>(latestFinancialReportDate);
        }
    }

    /// <summary>
    /// 领域层市场数据仓储测试替身，仅实现同步链路依赖的方法。
    /// </summary>
    private sealed class FakeMarketDataRepository(DateOnly importedTradeDate, DateOnly importedFinancialReportDate) : IMarketDataRepository
    {
        public int ReplaceSnapshotCallCount { get; private set; }

        public DateOnly? LastReplacedTradeDate { get; private set; }

        public int GetLatestImportedTradeDateCallCount { get; private set; }

        public int GetLatestImportedFinancialReportDateCallCount { get; private set; }

        public Task<DateOnly?> GetLatestImportedTradeDateAsync(CancellationToken cancellationToken)
        {
            GetLatestImportedTradeDateCallCount++;
            return Task.FromResult<DateOnly?>(importedTradeDate);
        }

        public Task ReplaceMarketSnapshotAsync(
            DateOnly tradeDate,
            IReadOnlyList<StockProfile> stocks,
            IReadOnlyList<DailyBar> dailyBars,
            IReadOnlyList<MarketIndexBar> indexBars,
            IReadOnlyList<IndustryDailyStat> industries,
            IReadOnlyList<FinancialSnapshot> financials,
            IReadOnlyList<StockFundFlowSnapshot> stockFundFlows,
            IReadOnlyList<IndustryFundFlowSnapshot> industryFundFlows,
            IReadOnlyList<LhbSnapshot> lhbSnapshots,
            CancellationToken cancellationToken)
        {
            ReplaceSnapshotCallCount++;
            LastReplacedTradeDate = tradeDate;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> GetActiveStockCodesAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<DailyBar>> GetDailyBarHistoryAsync(string stockCode, DateOnly tradeDate, int maxRows, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DailyBar>>([]);

        public Task<IReadOnlyDictionary<string, IReadOnlyList<DailyBar>>> GetDailyBarHistoriesByCodesAsync(IEnumerable<string> stockCodes, DateOnly tradeDate, int maxRows, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<DailyBar>>>(new Dictionary<string, IReadOnlyList<DailyBar>>());

        public Task<IReadOnlyDictionary<string, StockScoringHistoryMetrics>> GetScoringHistoryMetricsByCodesAsync(IEnumerable<string> stockCodes, DateOnly tradeDate, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, StockScoringHistoryMetrics>>(new Dictionary<string, StockScoringHistoryMetrics>());

        public Task<IReadOnlyDictionary<string, IndicatorCalculationMetrics>> GetIndicatorCalculationMetricsByCodesAsync(IEnumerable<string> stockCodes, DateOnly tradeDate, int maxRows, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, IndicatorCalculationMetrics>>(new Dictionary<string, IndicatorCalculationMetrics>());

        public Task<IReadOnlyList<DailyBar>> GetForwardDailyBarsAsync(string stockCode, DateOnly tradeDate, int maxRows, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DailyBar>>([]);

        public Task<IReadOnlyList<MarketIndexBar>> GetIndexBarHistoryAsync(DateOnly tradeDate, int maxRows, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<MarketIndexBar>>([]);

        public Task<IReadOnlyList<DateOnly>> GetRecentTradeDatesAsync(int maxRows, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DateOnly>>([importedTradeDate]);

        public Task<IReadOnlyDictionary<string, StockProfile>> GetStockProfilesByCodesAsync(IEnumerable<string> stockCodes, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, StockProfile>>(new Dictionary<string, StockProfile>());

        public Task<IReadOnlyDictionary<string, FinancialSnapshot>> GetLatestFinancialsByCodesAsync(IEnumerable<string> stockCodes, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, FinancialSnapshot>>(new Dictionary<string, FinancialSnapshot>());

        public Task<IReadOnlyList<FinancialSnapshot>> GetLatestFinancialSnapshotsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<FinancialSnapshot>>([]);

        public Task<IReadOnlyDictionary<string, StockFundFlowSnapshot>> GetStockFundFlowsByCodesAsync(DateOnly tradeDate, IEnumerable<string> stockCodes, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, StockFundFlowSnapshot>>(new Dictionary<string, StockFundFlowSnapshot>());

        public Task<IReadOnlyDictionary<string, IndustryFundFlowSnapshot>> GetIndustryFundFlowsByNamesAsync(DateOnly tradeDate, IEnumerable<string?> industryNames, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, IndustryFundFlowSnapshot>>(new Dictionary<string, IndustryFundFlowSnapshot>());

        public Task<IReadOnlyDictionary<string, LhbSnapshot>> GetLhbSnapshotsByCodesAsync(DateOnly tradeDate, IEnumerable<string> stockCodes, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, LhbSnapshot>>(new Dictionary<string, LhbSnapshot>());

        public Task<DateOnly?> GetLatestImportedFinancialReportDateAsync(CancellationToken cancellationToken)
        {
            GetLatestImportedFinancialReportDateCallCount++;
            return Task.FromResult<DateOnly?>(importedFinancialReportDate);
        }

        public Task<IReadOnlyList<IndustryDailyStat>> GetIndustryStatsAsync(DateOnly tradeDate, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<IndustryDailyStat>>([]);

        public Task<IReadOnlyDictionary<string, IndustryDailyStat>> GetIndustryStatsByNamesAsync(DateOnly tradeDate, IEnumerable<string?> industryNames, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, IndustryDailyStat>>(new Dictionary<string, IndustryDailyStat>());

        public Task UpsertIndicatorSnapshotsAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, IReadOnlyList<IndicatorSnapshot> indicators, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task UpsertMarketRegimeAsync(StrategySnapshotVersion snapshotVersion, MarketRegimeSnapshot regime, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task UpsertCandidatesAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, IReadOnlyList<CandidateStock> candidates, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task UpsertScoreSnapshotsAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, IReadOnlyList<StrategyScoreSnapshot> scores, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task UpsertSignalsAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, IReadOnlyList<TradeSignal> signals, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<int> CountScoreSnapshotsAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<PagedResponse<FinancialListItemResponse>> GetFinancialScorePageAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, FinancialListQuery query, CancellationToken cancellationToken)
            => Task.FromResult(new PagedResponse<FinancialListItemResponse>([], 1, query.PageSize, 0));

        public Task<IReadOnlyList<IndicatorSnapshot>> GetIndicatorSnapshotsAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<IndicatorSnapshot>>([]);

        public Task<MarketRegimeSnapshot?> GetMarketRegimeAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken)
            => Task.FromResult<MarketRegimeSnapshot?>(null);

        public Task<IReadOnlyList<CandidateStock>> GetCandidatesAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<CandidateStock>>([]);

        public Task<IReadOnlyList<TradeSignal>> GetSignalsAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TradeSignal>>([]);

        public Task<StockProfile?> GetStockProfileAsync(string stockCode, CancellationToken cancellationToken)
            => Task.FromResult<StockProfile?>(null);

        public Task<FinancialSnapshot?> GetLatestFinancialAsync(string stockCode, CancellationToken cancellationToken)
            => Task.FromResult<FinancialSnapshot?>(null);

        public Task<IndicatorSnapshot?> GetIndicatorSnapshotAsync(string stockCode, DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken)
            => Task.FromResult<IndicatorSnapshot?>(null);

        public Task<CandidateStock?> GetCandidateAsync(string stockCode, DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken)
            => Task.FromResult<CandidateStock?>(null);

        public Task<TradeSignal?> GetSignalAsync(string stockCode, DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken)
            => Task.FromResult<TradeSignal?>(null);

        public Task<IReadOnlyList<DailyBar>> GetRecentImportedDailyBarsAsync(string stockCode, DateOnly tradeDate, int maxRows, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DailyBar>>([]);
    }

    /// <summary>
    /// 领域同步日志仓储测试替身。
    /// </summary>
    private sealed class FakeDomainSyncRunRepository : IDomainSyncRunRepository
    {
        public List<DomainSyncRunEntry> Runs { get; } = [];

        public Task AddRunAsync(DomainSyncRunEntry entry, CancellationToken cancellationToken)
        {
            Runs.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DomainSyncRunEntry>> GetRecentRunsAsync(int take, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DomainSyncRunEntry>>(Runs);

        public Task<DateTime?> GetLatestSuccessfulFinishedAtUtcAsync(CancellationToken cancellationToken)
            => Task.FromResult<DateTime?>(Runs.LastOrDefault(static item => item.Status == "Succeeded")?.FinishedAtUtc);

        public Task<DateTime?> GetLatestSuccessfulFinishedAtUtcAsync(StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken)
            => Task.FromResult<DateTime?>(Runs.LastOrDefault(item => item.Status == "Succeeded" && item.SnapshotVersion == snapshotVersion.ToValue())?.FinishedAtUtc);
    }

    /// <summary>
    /// 采集日志仓储测试替身。
    /// </summary>
    private sealed class StubIngestionLogRepository : IIngestionLogRepository
    {
        public Task<DateTime?> GetLatestSuccessfulIngestionAtUtcAsync(CancellationToken cancellationToken)
            => Task.FromResult<DateTime?>(null);

        public Task<IReadOnlyList<IngestionLogEntry>> GetRecentRunsAsync(int take, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<IngestionLogEntry>>([]);
    }
}

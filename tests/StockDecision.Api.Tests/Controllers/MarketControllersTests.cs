using Microsoft.AspNetCore.Mvc;
using StockDecision.Api.Controllers;
using StockDecision.Application.Contracts;
using StockDecision.Application.MarketPipeline;
using StockDecision.Domain.Market;
using StockDecision.Domain.Strategy;

namespace StockDecision.Api.Tests.Controllers;

/// <summary>
/// 覆盖市场相关控制器的基础行为测试。
/// </summary>
public class MarketControllersTests
{
    private static readonly DateOnly TradeDate = new(2026, 6, 19);

    /// <summary>
    /// 验证仪表盘接口能够返回最新快照摘要。
    /// </summary>
    [Fact]
    public async Task DashboardController_Should_Return_Dashboard_Summary()
    {
        var fixture = CreateFixture();
        var controller = new DashboardController(new GetDashboardUseCase(fixture.EnsureLatestSnapshot, fixture.MarketRepository, fixture.IngestionLogRepository, fixture.BacktestRunRepository));

        var result = await controller.Get(null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<DashboardResponse>(ok.Value);
        Assert.Equal(TradeDate, response.TradeDate);
        Assert.Equal(1, response.CandidateCount);
        Assert.Equal(1, response.SignalCount);
        Assert.True(response.IsSignalEligible);
    }

    [Fact]
    public async Task TasksController_Should_Mark_Signal_Ineligible_When_Backtest_Not_Approved()
    {
        var fixture = CreateFixture(backtestApproved: false);
        var controller = new TasksController(new GetTaskCenterOverviewUseCase(fixture.EnsureLatestSnapshot, fixture.MarketRepository, fixture.IngestionLogRepository, fixture.DomainSyncRunRepository, fixture.BacktestRunRepository));

        var result = await controller.GetOverview(null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<TaskCenterOverviewResponse>(ok.Value);
        Assert.False(response.IsSignalEligible);
    }

    /// <summary>
    /// 验证候选股接口会返回指定交易日的候选列表。
    /// </summary>
    [Fact]
    public async Task CandidatesController_Should_Return_Candidates_For_Date()
    {
        var fixture = CreateFixture();
        var controller = new CandidatesController(new GetCandidatesUseCase(fixture.EnsureLatestSnapshot, fixture.MarketRepository));

        var result = await controller.Get(new CandidateListQuery { Date = TradeDate }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<PagedResponse<CandidateListItemResponse>>(ok.Value);
        var candidate = Assert.Single(response.Items);
        Assert.Equal("600001", candidate.StockCode);
        Assert.Equal("Alpha Tech", candidate.StockName);
        Assert.True(candidate.IsTradable);
        Assert.Equal("可执行", candidate.EligibilityStatus);
    }

    /// <summary>
    /// 验证信号接口会返回最新交易日的交易信号。
    /// </summary>
    [Fact]
    public async Task SignalsController_Should_Return_Today_Signals()
    {
        var fixture = CreateFixture();
        var controller = new SignalsController(new GetTodaySignalsUseCase(fixture.EnsureLatestSnapshot, fixture.MarketRepository, fixture.BacktestRunRepository));

        var result = await controller.GetToday(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<PagedResponse<SignalListItemResponse>>(ok.Value);
        var signal = Assert.Single(response.Items);
        Assert.Equal("600001", signal.StockCode);
        Assert.Equal(8000m, signal.SuggestedCapital);
    }

    /// <summary>
    /// 验证信号接口支持按交易日查询。
    /// </summary>
    [Fact]
    public async Task SignalsController_Should_Return_Signals_For_Date()
    {
        var fixture = CreateFixture();
        var controller = new SignalsController(new GetTodaySignalsUseCase(fixture.EnsureLatestSnapshot, fixture.MarketRepository, fixture.BacktestRunRepository));

        var result = await controller.Get(new SignalListQuery { Date = TradeDate }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<PagedResponse<SignalListItemResponse>>(ok.Value);
        var signal = Assert.Single(response.Items);
        Assert.Equal("600001", signal.StockCode);
        Assert.Equal(TradeDate.ToString("yyyy-MM-dd"), signal.GeneratedAtUtc.Date.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public async Task DashboardController_Should_Keep_Signal_Count_When_Backtest_Not_Approved()
    {
        var fixture = CreateFixture(backtestApproved: false);
        var controller = new DashboardController(new GetDashboardUseCase(fixture.EnsureLatestSnapshot, fixture.MarketRepository, fixture.IngestionLogRepository, fixture.BacktestRunRepository));

        var result = await controller.Get(null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<DashboardResponse>(ok.Value);
        Assert.Equal(1, response.SignalCount);
        Assert.False(response.IsSignalEligible);
        Assert.False(response.IsBacktestApproved);
    }

    [Fact]
    public async Task SignalsController_Should_Return_Signals_When_Backtest_Not_Approved()
    {
        var fixture = CreateFixture(backtestApproved: false);
        var controller = new SignalsController(new GetTodaySignalsUseCase(fixture.EnsureLatestSnapshot, fixture.MarketRepository, fixture.BacktestRunRepository));

        var result = await controller.GetToday(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<PagedResponse<SignalListItemResponse>>(ok.Value);
        var signal = Assert.Single(response.Items);
        Assert.Equal("600001", signal.StockCode);
    }

    /// <summary>
    /// 验证行业接口会返回行业强度和行业内候选/信号统计。
    /// </summary>
    [Fact]
    public async Task IndustriesController_Should_Return_Industry_Summary_For_Date()
    {
        var fixture = CreateFixture();
        var controller = new IndustriesController(new GetIndustriesUseCase(fixture.EnsureLatestSnapshot, fixture.MarketRepository));

        var result = await controller.Get(new IndustryListQuery { Date = TradeDate }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<PagedResponse<IndustryListItemResponse>>(ok.Value);
        var industry = Assert.Single(response.Items);
        Assert.Equal("SW001", industry.IndustryCode);
        Assert.Equal("Software", industry.IndustryName);
        Assert.Equal(1, industry.CandidateCount);
        Assert.Equal(1, industry.SignalCount);
    }

    [Fact]
    public async Task IndustriesController_Should_Not_Return_Candidate_Only_Generic_Industries()
    {
        var fixture = CreateFixture(includeGenericIndustryCandidate: true);
        var controller = new IndustriesController(new GetIndustriesUseCase(fixture.EnsureLatestSnapshot, fixture.MarketRepository));

        var result = await controller.Get(new IndustryListQuery { Date = TradeDate }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<PagedResponse<IndustryListItemResponse>>(ok.Value);
        var industry = Assert.Single(response.Items);
        Assert.Equal("Software", industry.IndustryName);
        Assert.DoesNotContain(response.Items, item => item.IndustryName == "C 制造业");
    }

    /// <summary>
    /// 验证财务接口会返回最新财务快照列表。
    /// </summary>
    [Fact]
    public async Task FinancialsController_Should_Return_Financial_Summary_List()
    {
        var fixture = CreateFixture();
        var controller = new FinancialsController(new GetFinancialsUseCase(fixture.EnsureLatestSnapshot, fixture.MarketRepository));

        var result = await controller.Get(new FinancialListQuery(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<PagedResponse<FinancialListItemResponse>>(ok.Value);
        var financial = Assert.Single(response.Items);
        Assert.Equal("600001", financial.StockCode);
        Assert.Equal("Alpha Tech", financial.StockName);
        Assert.Equal(91m, financial.TotalScore);
        Assert.Equal(12.4m, financial.Roe);
    }

    /// <summary>
    /// 验证策略解释接口会返回结构化规则说明。
    /// </summary>
    [Fact]
    public void StrategyController_Should_Return_Explanation()
    {
        var controller = new StrategyController(new GetStrategyExplanationUseCase());

        var result = controller.GetExplanation();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<StrategyExplanationResponse>(ok.Value);
        Assert.Equal("a-share-20k-v2", response.StrategyVersion);
        Assert.NotEmpty(response.Sections);
        Assert.NotEmpty(response.ScoreDimensions);
    }

    /// <summary>
    /// 验证回测接口会返回最近样本交易的回测结果。
    /// </summary>
    [Fact]
    public async Task BacktestsController_Should_Return_Backtest_Overview()
    {
        var fixture = CreateFixture();
        var controller = new BacktestsController(
            new RunBacktestUseCase(fixture.MarketRepository, fixture.BacktestRunRepository),
            new GetBacktestRunsUseCase(fixture.BacktestRunRepository),
            new GetBacktestRunDetailUseCase(fixture.BacktestRunRepository));

        var result = await controller.GetOverview(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<BacktestOverviewResponse>(ok.Value);
        Assert.Equal("a-share-20k-v1", response.StrategyVersion);
        Assert.NotEmpty(response.Trades);
        Assert.True(response.SampleTradeCount >= 1);
    }

    [Fact]
    public async Task PositionsController_Should_Create_And_Close_Position()
    {
        var fixture = CreateFixture();
        var controller = new PositionsController(
            new GetSimulatedPositionsUseCase(fixture.TradingRepository, fixture.MarketRepository),
            new CreateSimulatedBuyUseCase(fixture.EnsureLatestSnapshot, fixture.MarketRepository, fixture.TradingRepository),
            new SellSimulatedPositionUseCase(fixture.TradingRepository, fixture.MarketRepository));

        var buyResult = await controller.SimulateBuy(new SimulateBuyRequest { StockCode = "600001", TradeDate = TradeDate }, CancellationToken.None);
        var buyOk = Assert.IsType<OkObjectResult>(buyResult.Result);
        var position = Assert.IsType<SimulatedPositionItemResponse>(buyOk.Value);
        Assert.Equal("持有中", position.Status);

        var sellResult = await controller.Sell(position.Id, new SimulateSellRequest { ExitPrice = 28.40m, TradeDate = TradeDate.AddDays(2) }, CancellationToken.None);
        var sellOk = Assert.IsType<OkObjectResult>(sellResult.Result);
        var closed = Assert.IsType<SimulatedPositionItemResponse>(sellOk.Value);
        Assert.NotEqual("持有中", closed.Status);
        Assert.NotNull(closed.RealizedProfitAmount);
    }

    [Fact]
    public async Task BacktestsController_Should_Run_And_List_Backtests()
    {
        var fixture = CreateFixture();
        var controller = new BacktestsController(
            new RunBacktestUseCase(fixture.MarketRepository, fixture.BacktestRunRepository),
            new GetBacktestRunsUseCase(fixture.BacktestRunRepository),
            new GetBacktestRunDetailUseCase(fixture.BacktestRunRepository));

        var runResult = await controller.Run(
            new RunBacktestRequest
            {
                StartDate = TradeDate.AddDays(-1),
                EndDate = TradeDate,
                MaxSignalsPerDay = 3,
                MaxHoldingDays = 5
            },
            CancellationToken.None);

        Assert.IsNotType<NotFoundResult>(runResult.Result);
        if (runResult.Result is OkObjectResult runOk)
        {
            var detail = Assert.IsType<BacktestRunDetailResponse>(runOk.Value);
            Assert.True(detail.Id > 0);
        }
        else
        {
            Assert.IsType<ObjectResult>(runResult.Result);
        }

        var listResult = await controller.GetRuns(CancellationToken.None);
        var listOk = Assert.IsType<OkObjectResult>(listResult.Result);
        var runs = Assert.IsAssignableFrom<IReadOnlyList<BacktestRunListItemResponse>>(listOk.Value);
        Assert.NotEmpty(runs);
    }

    /// <summary>
    /// 验证学习复盘接口能够保存记录，并按股票读取历史复盘。
    /// </summary>
    [Fact]
    public async Task LearningController_Should_Save_And_Return_Reviews()
    {
        var fixture = CreateFixture();
        var controller = new LearningController(
            new GetLearningReviewOverviewUseCase(fixture.LearningReviewRepository),
            new SaveLearningReviewUseCase(fixture.LearningReviewRepository));

        var saveResult = await controller.SaveReview(
            new SaveLearningReviewRequest
            {
                StockCode = "600001",
                StockName = "Alpha Tech",
                TradeDate = TradeDate,
                SnapshotVersion = StrategySnapshotVersion.EndOfDayFinal.ToValue(),
                BuyReason = "突破后回踩企稳，量价结构仍然健康。",
                MarketContext = "大盘和行业同步转强，允许执行趋势策略。",
                ExecutionDiscipline = "按系统仓位和止损规则执行。",
                ResultSummary = "两天后达到止盈目标，整体符合预期。",
                ImprovementPlan = "下次可以把买点再靠近回踩确认位。",
                ErrorTags = ["过早止盈"],
                IsStrategyAligned = true,
                FollowedStopLoss = true,
                FollowedTakeProfit = false,
                ModifiedPlanDuringTrade = false,
                FollowedGapRule = true
            },
            CancellationToken.None);

        var saveOk = Assert.IsType<OkObjectResult>(saveResult.Result);
        var savedReview = Assert.IsType<LearningReviewItemResponse>(saveOk.Value);
        Assert.Equal("600001", savedReview.StockCode);
        Assert.Equal("Alpha Tech", savedReview.StockName);

        var listResult = await controller.GetReviews("600001", "Alpha Tech", TradeDate, StrategySnapshotVersion.EndOfDayFinal.ToValue(), CancellationToken.None);

        var listOk = Assert.IsType<OkObjectResult>(listResult.Result);
        var overview = Assert.IsType<LearningReviewOverviewResponse>(listOk.Value);
        Assert.Equal("600001", overview.StockCode);
        Assert.NotEmpty(overview.ReviewPrompts);
        Assert.Equal(1, overview.ProgressSummary.ReviewCount);
        var review = Assert.Single(overview.Reviews);
        Assert.Equal(savedReview.Id, review.Id);
        Assert.Equal("两天后达到止盈目标，整体符合预期。", review.ResultSummary);
    }

    /// <summary>
    /// 验证任务中心接口会返回最近执行记录和当前快照摘要。
    /// </summary>
    [Fact]
    public async Task TasksController_Should_Return_Task_Overview()
    {
        var fixture = CreateFixture();
        var controller = new TasksController(new GetTaskCenterOverviewUseCase(fixture.EnsureLatestSnapshot, fixture.MarketRepository, fixture.IngestionLogRepository, fixture.DomainSyncRunRepository, fixture.BacktestRunRepository));

        var result = await controller.GetOverview(null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<TaskCenterOverviewResponse>(ok.Value);
        Assert.Equal(TradeDate, response.TradeDate);
        Assert.Equal(1, response.CandidateCount);
        Assert.Equal(1, response.SignalCount);
        Assert.True(response.IsSignalEligible);
        Assert.NotEmpty(response.CollectorRuns);
        Assert.NotEmpty(response.DomainSyncRuns);
        Assert.Equal("daily-snapshot", response.CollectorRuns[0].TargetScope);
        Assert.Equal("domain-market-sync", response.DomainSyncRuns[0].JobName);
    }

    /// <summary>
    /// 验证个股详情接口在命中和未命中时都返回正确结果。
    /// </summary>
    [Fact]
    public async Task StocksController_Should_Return_Ok_And_NotFound()
    {
        var fixture = CreateFixture();
        var controller = new StocksController(new GetStockDetailUseCase(fixture.EnsureLatestSnapshot, fixture.MarketRepository));

        var found = await controller.GetByCode("600001", TradeDate, null, CancellationToken.None);
        var missing = await controller.GetByCode("999999", TradeDate, null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(found);
        var response = Assert.IsType<StockDetailResponse>(ok.Value);
        Assert.Equal("Alpha Tech", response.StockName);
        Assert.Equal(1_950_000L, response.LatestBar.Volume);
        Assert.Equal(420_000_000m, response.LatestBar.Amount);
        Assert.NotNull(response.FundFlow);
        Assert.Equal(125_000_000m, response.FundFlow!.MainNetAmount);
        Assert.Equal(5, response.FundFlow.IndustryRank);
        Assert.NotNull(response.Lhb);
        Assert.True(response.Lhb!.IsOnLhbToday);
        Assert.Equal(3, response.Lhb.Recent20dLhbCount);
        Assert.IsType<NotFoundResult>(missing);
    }

    [Fact]
    public async Task StocksController_Should_Rebuild_Score_Details_When_Indicator_Snapshot_Is_Missing()
    {
        var fixture = CreateFixture(hasIndicatorSnapshot: false, useExtendedHistory: true);
        var controller = new StocksController(new GetStockDetailUseCase(fixture.EnsureLatestSnapshot, fixture.MarketRepository));

        var found = await controller.GetByCode("600001", TradeDate, null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(found);
        var response = Assert.IsType<StockDetailResponse>(ok.Value);
        Assert.NotNull(response.Candidate);
        var candidate = response.Candidate!;
        var industryRankRule = Assert.Single(candidate.ScoreDetails, item => item.Key == "industryTop10");
        Assert.Equal("量价确认", industryRankRule.Dimension);
        Assert.True(industryRankRule.Hit);
    }

    [Fact]
    public async Task StocksController_Should_Use_Scoring_Industry_For_Industry_Rules()
    {
        var fixture = CreateFixture(
            hasIndicatorSnapshot: false,
            useExtendedHistory: true,
            profileIndustryName: "Vertical Software",
            scoringIndustryName: "Software");
        var controller = new StocksController(new GetStockDetailUseCase(fixture.EnsureLatestSnapshot, fixture.MarketRepository));

        var found = await controller.GetByCode("600001", TradeDate, null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(found);
        var response = Assert.IsType<StockDetailResponse>(ok.Value);
        Assert.Equal("Vertical Software", response.IndustryName);
        Assert.Equal("Software", response.ScoringIndustryName);
        Assert.NotNull(response.Candidate);
        var industryRankRule = Assert.Single(response.Candidate!.ScoreDetails, item => item.Key == "industryTop10");
        Assert.True(industryRankRule.Hit);
        Assert.Equal("当前排名 3", industryRankRule.Evidence);
        Assert.Equal(5, response.FundFlow?.IndustryRank);
    }

    /// <summary>
    /// 构造一套最小但完整的测试夹具。
    /// </summary>
    private static TestFixture CreateFixture(
        bool hasIndicatorSnapshot = true,
        bool useExtendedHistory = false,
        bool includeGenericIndustryCandidate = false,
        bool backtestApproved = true,
        string profileIndustryName = "Software",
        string? scoringIndustryName = null)
    {
        var profile = new StockProfile(
            "600001",
            "Alpha Tech",
            profileIndustryName,
            true,
            false,
            false,
            new DateOnly(2020, 1, 1),
            25.36m,
            28m,
            3.2m,
            12_000_000_000m,
            4.2m,
            650_000_000m,
            TradeDate,
            scoringIndustryName);

        List<DailyBar> bars;
        if (useExtendedHistory)
        {
            bars = Enumerable.Range(0, 140)
                .Select(offset =>
                {
                    var tradeDate = TradeDate.AddDays(offset - 139);
                    var close = 18m + offset * 0.06m;
                    var open = close - 0.12m;
                    var high = close + 0.25m;
                    var low = close - 0.22m;
                    var volume = 1_000_000L + offset * 4_000L;
                    var amount = Math.Round(close * volume, 2, MidpointRounding.AwayFromZero);
                    var pctChange = offset == 0 ? 0m : 0.4m;
                    var turnoverRate = 3.2m + (offset % 5) * 0.2m;
                    return new DailyBar("600001", tradeDate, open, high, low, close, volume, amount, pctChange, turnoverRate);
                })
                .ToList();

            bars[^3] = new DailyBar("600001", TradeDate.AddDays(-2), 24.50m, 25.10m, 24.20m, 24.85m, 1_200_000L, 310_000_000m, 1.2m, 3.6m);
            bars[^2] = new DailyBar("600001", TradeDate.AddDays(-1), 24.90m, 25.40m, 24.70m, 25.10m, 1_350_000L, 328_000_000m, 1.0m, 3.9m);
            bars[^1] = new DailyBar("600001", TradeDate, 25.00m, 25.80m, 24.90m, 25.36m, 1_580_000L, 352_000_000m, 1.04m, 4.2m);
        }
        else
        {
            bars =
            [
                new("600001", TradeDate.AddDays(-2), 24.50m, 25.10m, 24.20m, 24.85m, 1_200_000L, 310_000_000m, 1.2m, 3.6m),
                new("600001", TradeDate.AddDays(-1), 24.90m, 25.40m, 24.70m, 25.10m, 1_350_000L, 328_000_000m, 1.0m, 3.9m),
                new("600001", TradeDate, 25.00m, 25.80m, 24.90m, 25.36m, 1_580_000L, 352_000_000m, 1.04m, 4.2m),
                new("600001", TradeDate.AddDays(1), 25.40m, 26.20m, 25.10m, 25.90m, 1_610_000L, 366_000_000m, 2.1m, 4.4m),
                new("600001", TradeDate.AddDays(2), 25.95m, 28.60m, 25.70m, 28.10m, 1_950_000L, 420_000_000m, 8.5m, 4.8m)
            ];
        }

        var financial = new FinancialSnapshot("600001", new DateOnly(2026, 3, 31), 28m, 3.2m, 12.4m, 18.5m, 21.8m, 12_000_000_000m);
        var industry = new IndustryDailyStat("SW001", "Software", TradeDate, 12.6m, 3);
        var indicator = new IndicatorSnapshot("600001", TradeDate, 25.36m, 24.30m, 22.80m, 20.10m, 0.82m, 9.8m, 22.4m, 9.8m, true, true, true, 4.36m, 4.2m);
        var stockFundFlow = new StockFundFlowSnapshot("600001", TradeDate, 125_000_000m, 8.6m, 82_000_000m, 5.2m, 24_000_000m, 1.8m, -18_000_000m, -1.4m, -89_000_000m, -6.1m, 92m);
        var industryFundFlow = new IndustryFundFlowSnapshot("Software", TradeDate, 1_280_000_000m, 4.8m, 5, 96m);
        var lhb = new LhbSnapshot("600001", TradeDate, "日涨幅偏离值达到7%", 168_000_000m, 100_000_000m, 68_000_000m, 52_000_000m, 17_000_000m, 35_000_000m, 2, true, true, 3, 0, "high-volatility");
        var regime = new MarketRegimeSnapshot(TradeDate, MarketSignalEligibility.Tradable, 2, true, "Confirmed indices: 2/3");
        var scoreBreakdown = new CandidateScoreBreakdown(26m, 21m, 24m, 17m);
        var candidate = new CandidateStock(
            TradeDate,
            "600001",
            "Alpha Tech",
            profileIndustryName,
            CandidateGrade.A,
            StrategyType.Breakout,
            true,
            "tradable",
            "满足当前执行条件。",
            91m,
            scoreBreakdown,
            25.36m,
            24.30m,
            22.80m,
            20.10m,
            0.82m,
            9.8m,
            28m,
            3.2m,
            12.4m,
            24.34m,
            28.40m,
            2.2m,
            "Alpha Tech remains in a strong trend with supportive regime.");
        var signal = new TradeSignal(
            TradeDate,
            "600001",
            "Alpha Tech",
            profileIndustryName,
            StrategyType.Breakout,
            "tradable",
            "满足当前执行条件。",
            91m,
            scoreBreakdown,
            25.36m,
            24.34m,
            28.40m,
            2.2m,
            8000m,
            300,
            "Alpha Tech remains in a strong trend with supportive regime.",
            new DateTime(2026, 6, 19, 9, 35, 0, DateTimeKind.Utc));

        var rawRepository = new FakeRawMarketDataRepository(TradeDate);
        var marketRepository = new FakeMarketDataRepository(profile, bars, financial, industry, hasIndicatorSnapshot ? indicator : null, stockFundFlow, industryFundFlow, lhb, regime, candidate, signal, TradeDate, includeGenericIndustryCandidate);
        var ingestionLogRepository = new FakeIngestionLogRepository(new DateTime(2026, 6, 19, 8, 0, 0, DateTimeKind.Utc));
        var domainSyncRunRepository = new FakeDomainSyncRunRepository(new DateTime(2026, 6, 19, 8, 5, 0, DateTimeKind.Utc), TradeDate, financial.ReportDate);

        return new TestFixture(
            marketRepository,
            ingestionLogRepository,
            domainSyncRunRepository,
            new EnsureLatestMarketSnapshotUseCase(rawRepository, marketRepository, ingestionLogRepository, new TradingPermissionsOptions()),
            new InMemorySimulatedTradingRepository(),
            new InMemoryBacktestRunRepository(backtestApproved),
            new InMemoryLearningReviewRepository());
    }

    /// <summary>
    /// 封装测试对象集合。
    /// </summary>
    private sealed record TestFixture(
        FakeMarketDataRepository MarketRepository,
        FakeIngestionLogRepository IngestionLogRepository,
        FakeDomainSyncRunRepository DomainSyncRunRepository,
        EnsureLatestMarketSnapshotUseCase EnsureLatestSnapshot,
        InMemorySimulatedTradingRepository TradingRepository,
        InMemoryBacktestRunRepository BacktestRunRepository,
        InMemoryLearningReviewRepository LearningReviewRepository);

    /// <summary>
    /// 原始仓储测试替身，仅提供最新交易日。
    /// </summary>
    private sealed class FakeRawMarketDataRepository(DateOnly latestTradeDate) : IRawMarketDataRepository
    {
        public Task<DateOnly?> GetLatestTradeDateAsync(CancellationToken cancellationToken) => Task.FromResult<DateOnly?>(latestTradeDate);
        public Task<IReadOnlyList<StockProfile>> GetLatestStockProfilesAsync(DateOnly tradeDate, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<StockProfile>>([]);
        public Task<IReadOnlyList<DailyBar>> GetDailyBarsByTradeDateAsync(DateOnly tradeDate, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DailyBar>>([]);
        public Task<IReadOnlyList<MarketIndexBar>> GetIndexBarsByTradeDateAsync(DateOnly tradeDate, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MarketIndexBar>>([]);
        public Task<IReadOnlyList<IndustryDailyStat>> GetIndustryStatsByTradeDateAsync(DateOnly tradeDate, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<IndustryDailyStat>>([]);
        public Task<IReadOnlyList<FinancialSnapshot>> GetLatestFinancialSnapshotsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FinancialSnapshot>>([]);
        public Task<IReadOnlyList<StockFundFlowSnapshot>> GetStockFundFlowsByTradeDateAsync(DateOnly tradeDate, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<StockFundFlowSnapshot>>([]);
        public Task<IReadOnlyList<IndustryFundFlowSnapshot>> GetIndustryFundFlowsByTradeDateAsync(DateOnly tradeDate, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<IndustryFundFlowSnapshot>>([]);
        public Task<IReadOnlyList<LhbSnapshot>> GetLhbSnapshotsByTradeDateAsync(DateOnly tradeDate, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<LhbSnapshot>>([]);
        public Task<DateOnly?> GetLatestFinancialReportDateAsync(CancellationToken cancellationToken) => Task.FromResult<DateOnly?>(new DateOnly(2026, 3, 31));
    }

    /// <summary>
    /// 市场仓储测试替身，返回固定夹具数据。
    /// </summary>
    private sealed class FakeMarketDataRepository(
        StockProfile profile,
        IReadOnlyList<DailyBar> bars,
        FinancialSnapshot financial,
        IndustryDailyStat industry,
        IndicatorSnapshot? indicator,
        StockFundFlowSnapshot stockFundFlow,
        IndustryFundFlowSnapshot industryFundFlow,
        LhbSnapshot lhb,
        MarketRegimeSnapshot regime,
        CandidateStock candidate,
        TradeSignal signal,
        DateOnly importedTradeDate,
        bool includeGenericIndustryCandidate = false) : IMarketDataRepository
    {
        public Task<DateOnly?> GetLatestImportedTradeDateAsync(CancellationToken cancellationToken) => Task.FromResult<DateOnly?>(importedTradeDate);
        public Task ReplaceMarketSnapshotAsync(DateOnly tradeDate, IReadOnlyList<StockProfile> stocks, IReadOnlyList<DailyBar> dailyBars, IReadOnlyList<MarketIndexBar> indexBars, IReadOnlyList<IndustryDailyStat> industries, IReadOnlyList<FinancialSnapshot> financials, IReadOnlyList<StockFundFlowSnapshot> stockFundFlows, IReadOnlyList<IndustryFundFlowSnapshot> industryFundFlows, IReadOnlyList<LhbSnapshot> lhbSnapshots, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> GetActiveStockCodesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([profile.StockCode]);
        public Task<IReadOnlyList<DailyBar>> GetDailyBarHistoryAsync(string stockCode, DateOnly tradeDate, int maxRows, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DailyBar>>(
                stockCode == profile.StockCode
                    ? bars.Where(item => item.TradeDate <= tradeDate).TakeLast(maxRows).ToList()
                    : []);
        public Task<IReadOnlyDictionary<string, IReadOnlyList<DailyBar>>> GetDailyBarHistoriesByCodesAsync(IEnumerable<string> stockCodes, DateOnly tradeDate, int maxRows, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<DailyBar>>>(
                stockCodes.Contains(profile.StockCode, StringComparer.OrdinalIgnoreCase)
                    ? new Dictionary<string, IReadOnlyList<DailyBar>>(StringComparer.OrdinalIgnoreCase)
                    {
                        [profile.StockCode] = bars.Where(item => item.TradeDate <= tradeDate).TakeLast(maxRows).ToList()
                    }
                    : new Dictionary<string, IReadOnlyList<DailyBar>>(StringComparer.OrdinalIgnoreCase));
        public Task<IReadOnlyDictionary<string, StockScoringHistoryMetrics>> GetScoringHistoryMetricsByCodesAsync(IEnumerable<string> stockCodes, DateOnly tradeDate, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, StockScoringHistoryMetrics>>(
                stockCodes.Contains(profile.StockCode, StringComparer.OrdinalIgnoreCase)
                    ? new Dictionary<string, StockScoringHistoryMetrics>(StringComparer.OrdinalIgnoreCase)
                    {
                        [profile.StockCode] = new StockScoringHistoryMetrics(profile.StockCode, 0m, 0m, 0m)
                    }
                    : new Dictionary<string, StockScoringHistoryMetrics>(StringComparer.OrdinalIgnoreCase));
        public Task<IReadOnlyDictionary<string, IndicatorCalculationMetrics>> GetIndicatorCalculationMetricsByCodesAsync(IEnumerable<string> stockCodes, DateOnly tradeDate, int maxRows, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, IndicatorCalculationMetrics>>(
                stockCodes.Contains(profile.StockCode, StringComparer.OrdinalIgnoreCase)
                    ? new Dictionary<string, IndicatorCalculationMetrics>(StringComparer.OrdinalIgnoreCase)
                    {
                        [profile.StockCode] = new IndicatorCalculationMetrics(profile.StockCode, indicator?.Close ?? 0m, indicator?.Ma20 ?? 0m, indicator?.Ma60 ?? 0m, indicator?.Ma120 ?? 0m, indicator?.Atr14 ?? 0m, indicator?.Return20d ?? 0m, indicator?.Return60d ?? 0m, 0m, 0m, indicator?.Ma20 ?? 0m, indicator?.Ma60 ?? 0m, indicator?.Close ?? 0m, indicator?.TurnoverRate)
                    }
                    : new Dictionary<string, IndicatorCalculationMetrics>(StringComparer.OrdinalIgnoreCase));
        public Task<IReadOnlyList<DailyBar>> GetForwardDailyBarsAsync(string stockCode, DateOnly tradeDate, int maxRows, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DailyBar>>(stockCode == profile.StockCode ? bars.Where(item => item.TradeDate > tradeDate).Take(maxRows).ToList() : []);
        public Task<IReadOnlyList<MarketIndexBar>> GetIndexBarHistoryAsync(DateOnly tradeDate, int maxRows, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MarketIndexBar>>([]);
        public Task<IReadOnlyList<DateOnly>> GetRecentTradeDatesAsync(int maxRows, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DateOnly>>([importedTradeDate]);
        public Task<IReadOnlyDictionary<string, StockProfile>> GetStockProfilesByCodesAsync(IEnumerable<string> stockCodes, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyDictionary<string, StockProfile>>(new Dictionary<string, StockProfile> { [profile.StockCode] = profile });
        public Task<IReadOnlyDictionary<string, FinancialSnapshot>> GetLatestFinancialsByCodesAsync(IEnumerable<string> stockCodes, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyDictionary<string, FinancialSnapshot>>(new Dictionary<string, FinancialSnapshot> { [profile.StockCode] = financial });
        public Task<IReadOnlyList<FinancialSnapshot>> GetLatestFinancialSnapshotsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FinancialSnapshot>>([financial]);
        public Task<IReadOnlyDictionary<string, StockFundFlowSnapshot>> GetStockFundFlowsByCodesAsync(DateOnly tradeDate, IEnumerable<string> stockCodes, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, StockFundFlowSnapshot>>(
                tradeDate == importedTradeDate && stockCodes.Contains(profile.StockCode, StringComparer.OrdinalIgnoreCase)
                    ? new Dictionary<string, StockFundFlowSnapshot> { [profile.StockCode] = stockFundFlow }
                    : new Dictionary<string, StockFundFlowSnapshot>());
        public Task<IReadOnlyDictionary<string, IndustryFundFlowSnapshot>> GetIndustryFundFlowsByNamesAsync(DateOnly tradeDate, IEnumerable<string?> industryNames, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, IndustryFundFlowSnapshot>>(
                tradeDate == importedTradeDate && industryNames.Any(name => string.Equals(name, industry.IndustryName, StringComparison.OrdinalIgnoreCase))
                    ? new Dictionary<string, IndustryFundFlowSnapshot> { [industry.IndustryName] = industryFundFlow }
                    : new Dictionary<string, IndustryFundFlowSnapshot>());
        public Task<IReadOnlyDictionary<string, LhbSnapshot>> GetLhbSnapshotsByCodesAsync(DateOnly tradeDate, IEnumerable<string> stockCodes, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, LhbSnapshot>>(
                tradeDate == importedTradeDate && stockCodes.Contains(profile.StockCode, StringComparer.OrdinalIgnoreCase)
                    ? new Dictionary<string, LhbSnapshot> { [profile.StockCode] = lhb }
                    : new Dictionary<string, LhbSnapshot>());
        public Task<DateOnly?> GetLatestImportedFinancialReportDateAsync(CancellationToken cancellationToken) => Task.FromResult<DateOnly?>(financial.ReportDate);
        public Task<IReadOnlyList<IndustryDailyStat>> GetIndustryStatsAsync(DateOnly tradeDate, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<IndustryDailyStat>>(tradeDate == importedTradeDate ? [industry] : []);
        public Task<IReadOnlyDictionary<string, IndustryDailyStat>> GetIndustryStatsByNamesAsync(DateOnly tradeDate, IEnumerable<string?> industryNames, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, IndustryDailyStat>>(
                tradeDate == importedTradeDate && industryNames.Any(name => string.Equals(name, industry.IndustryName, StringComparison.OrdinalIgnoreCase))
                    ? new Dictionary<string, IndustryDailyStat> { [industry.IndustryName] = industry }
                    : new Dictionary<string, IndustryDailyStat>());
        public Task UpsertIndicatorSnapshotsAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, IReadOnlyList<IndicatorSnapshot> indicators, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpsertMarketRegimeAsync(StrategySnapshotVersion snapshotVersion, MarketRegimeSnapshot regimeSnapshot, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpsertCandidatesAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, IReadOnlyList<CandidateStock> candidates, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpsertScoreSnapshotsAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, IReadOnlyList<StrategyScoreSnapshot> scores, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpsertSignalsAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, IReadOnlyList<TradeSignal> signals, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<int> CountScoreSnapshotsAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<PagedResponse<FinancialListItemResponse>> GetFinancialScorePageAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, FinancialListQuery query, CancellationToken cancellationToken) => Task.FromResult(new PagedResponse<FinancialListItemResponse>([], 1, query.PageSize, 0));
        public Task<IReadOnlyList<IndicatorSnapshot>> GetIndicatorSnapshotsAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<IndicatorSnapshot>>(indicator is null ? [] : [indicator]);
        public Task<MarketRegimeSnapshot?> GetMarketRegimeAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken) => Task.FromResult<MarketRegimeSnapshot?>(regime);
        public Task<IReadOnlyList<CandidateStock>> GetCandidatesAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken)
        {
            if (tradeDate != importedTradeDate)
            {
                return Task.FromResult<IReadOnlyList<CandidateStock>>([]);
            }

            if (!includeGenericIndustryCandidate)
            {
                return Task.FromResult<IReadOnlyList<CandidateStock>>([candidate]);
            }

            var genericIndustryCandidate = candidate with
            {
                StockCode = "002317",
                StockName = "众生药业",
                IndustryName = "C 制造业"
            };
            return Task.FromResult<IReadOnlyList<CandidateStock>>([candidate, genericIndustryCandidate]);
        }
        public Task<IReadOnlyList<TradeSignal>> GetSignalsAsync(DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TradeSignal>>(tradeDate == importedTradeDate ? [signal] : []);
        public Task<StockProfile?> GetStockProfileAsync(string stockCode, CancellationToken cancellationToken) => Task.FromResult<StockProfile?>(stockCode == profile.StockCode ? profile : null);
        public Task<FinancialSnapshot?> GetLatestFinancialAsync(string stockCode, CancellationToken cancellationToken) => Task.FromResult<FinancialSnapshot?>(stockCode == profile.StockCode ? financial : null);
        public Task<IndicatorSnapshot?> GetIndicatorSnapshotAsync(string stockCode, DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken)
            => Task.FromResult<IndicatorSnapshot?>(stockCode == profile.StockCode && tradeDate == importedTradeDate ? indicator : null);
        public Task<CandidateStock?> GetCandidateAsync(string stockCode, DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken) => Task.FromResult<CandidateStock?>(stockCode == profile.StockCode && tradeDate == importedTradeDate ? candidate : null);
        public Task<TradeSignal?> GetSignalAsync(string stockCode, DateOnly tradeDate, StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken) => Task.FromResult<TradeSignal?>(stockCode == profile.StockCode && tradeDate == importedTradeDate ? signal : null);
        public Task<IReadOnlyList<DailyBar>> GetRecentImportedDailyBarsAsync(string stockCode, DateOnly tradeDate, int maxRows, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DailyBar>>(
                stockCode == profile.StockCode && tradeDate == importedTradeDate
                    ? bars.TakeLast(maxRows).ToList()
                    : []);
    }

    /// <summary>
    /// 采集日志仓储测试替身。
    /// </summary>
    private sealed class FakeIngestionLogRepository(DateTime latestIngestionAtUtc) : IIngestionLogRepository
    {
        public Task<DateTime?> GetLatestSuccessfulIngestionAtUtcAsync(CancellationToken cancellationToken) => Task.FromResult<DateTime?>(latestIngestionAtUtc);

        public Task<IReadOnlyList<IngestionLogEntry>> GetRecentRunsAsync(int take, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<IngestionLogEntry>>(
                [
                    new IngestionLogEntry("daily-snapshot", true, true, latestIngestionAtUtc),
                    new IngestionLogEntry("signals", true, true, latestIngestionAtUtc.AddMinutes(-15))
                ]);
    }

    /// <summary>
    /// 领域同步运行日志仓储测试替身。
    /// </summary>
    private sealed class FakeDomainSyncRunRepository(DateTime latestFinishedAtUtc, DateOnly tradeDate, DateOnly financialReportDate) : IDomainSyncRunRepository
    {
        public Task AddRunAsync(DomainSyncRunEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<DateTime?> GetLatestSuccessfulFinishedAtUtcAsync(CancellationToken cancellationToken)
            => Task.FromResult<DateTime?>(latestFinishedAtUtc);

        public Task<DateTime?> GetLatestSuccessfulFinishedAtUtcAsync(StrategySnapshotVersion snapshotVersion, CancellationToken cancellationToken)
            => Task.FromResult<DateTime?>(latestFinishedAtUtc);

        public Task<IReadOnlyList<DomainSyncRunEntry>> GetRecentRunsAsync(int take, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DomainSyncRunEntry>>(
                [
                    new DomainSyncRunEntry("domain-market-sync", "startup", StrategySnapshotVersion.EndOfDayFinal.ToValue(), "Succeeded", true, true, tradeDate, financialReportDate, latestFinishedAtUtc.AddMinutes(-2), latestFinishedAtUtc, "trade-date-gap"),
                    new DomainSyncRunEntry("domain-market-sync", "poll", StrategySnapshotVersion.EndOfDayFinal.ToValue(), "Succeeded", true, true, tradeDate, financialReportDate, latestFinishedAtUtc.AddMinutes(-10), latestFinishedAtUtc.AddMinutes(-8), "financial-gap")
                ]);
    }

    private sealed class InMemorySimulatedTradingRepository : ISimulatedTradingRepository
    {
        private readonly List<SimulatedPositionState> _positions = [];
        private readonly List<SimulatedTradeHistoryItemResponse> _history = [];
        private int _nextPositionId = 1;
        private int _nextHistoryId = 1;

        public Task<int> AddPositionAsync(SimulatedPositionDraft draft, CancellationToken cancellationToken)
        {
            var id = _nextPositionId++;
            _positions.Add(new SimulatedPositionState(
                id,
                draft.StockCode,
                draft.StockName,
                draft.IndustryName,
                draft.StrategyType,
                draft.SnapshotVersion,
                draft.TradeDate,
                draft.EntryPrice,
                draft.StopLossPrice,
                draft.TargetPrice,
                draft.Quantity,
                draft.InvestedCapital,
                draft.Status,
                draft.OpenedAtUtc,
                null,
                null,
                null,
                null,
                null,
                draft.Notes));
            return Task.FromResult(id);
        }

        public Task<IReadOnlyList<SimulatedPositionState>> GetPositionsAsync(bool includeClosed, CancellationToken cancellationToken)
        {
            IReadOnlyList<SimulatedPositionState> result = includeClosed ? _positions.ToList() : _positions.Where(item => item.Status == "持有中").ToList();
            return Task.FromResult(result);
        }

        public Task<SimulatedPositionState?> GetPositionAsync(int id, CancellationToken cancellationToken)
        {
            return Task.FromResult(_positions.FirstOrDefault(item => item.Id == id));
        }

        public Task<SimulatedPositionState?> ClosePositionAsync(int id, DateOnly tradeDate, decimal exitPrice, string? notes, CancellationToken cancellationToken)
        {
            var position = _positions.FirstOrDefault(item => item.Id == id);
            if (position is null)
            {
                return Task.FromResult<SimulatedPositionState?>(null);
            }

            var realizedAmount = Math.Round((exitPrice - position.EntryPrice) * position.Quantity, 2, MidpointRounding.AwayFromZero);
            var realizedPct = Math.Round((exitPrice - position.EntryPrice) / position.EntryPrice * 100m, 2, MidpointRounding.AwayFromZero);
            var status = exitPrice <= position.StopLossPrice ? "已触发止损" : exitPrice >= position.TargetPrice ? "已达到止盈目标" : "已手动卖出";

            var updated = position with
            {
                Status = status,
                ClosedAtUtc = DateTime.UtcNow,
                ClosedTradeDate = tradeDate,
                ExitPrice = exitPrice,
                RealizedProfitAmount = realizedAmount,
                RealizedProfitPct = realizedPct,
                Notes = notes
            };

            _positions.Remove(position);
            _positions.Add(updated);
            return Task.FromResult<SimulatedPositionState?>(updated);
        }

        public Task AddTradeHistoryAsync(SimulatedTradeHistoryDraft draft, CancellationToken cancellationToken)
        {
            _history.Add(new SimulatedTradeHistoryItemResponse(
                _nextHistoryId++,
                draft.PositionId,
                draft.ActionType,
                draft.StockCode,
                draft.StockName,
                draft.TradeDate,
                draft.Price,
                draft.Quantity,
                draft.Amount,
                draft.Summary,
                draft.CreatedAtUtc));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SimulatedTradeHistoryItemResponse>> GetHistoryAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<SimulatedTradeHistoryItemResponse>>(_history.OrderByDescending(item => item.CreatedAtUtc).ToList());
        }
    }

    private sealed class InMemoryBacktestRunRepository : IBacktestRunRepository
    {
        private readonly List<BacktestRunDetailResponse> _runs;
        private int _nextId = 2;

        public InMemoryBacktestRunRepository(bool isApproved = true)
        {
            _runs =
        [
            new(
                1,
                "a-share-20k-v1",
                StrategySnapshotVersion.EndOfDayFinal.ToValue(),
                TradeDate.AddDays(-3),
                TradeDate,
                1,
                100m,
                12m,
                12m,
                -2m,
                0m,
                2m,
                12m,
                3m,
                100m,
                0,
                24m,
                0,
                isApproved,
                isApproved ? [] : ["backtest_not_approved"],
                5m,
                DateTime.UtcNow.AddMinutes(-5),
                [new BacktestEquityPointResponse(TradeDate, 112m, 12m)],
                [new BacktestTradeItemResponse(TradeDate, "600001", "Alpha Tech", "突破", 25.36m, 28.40m, 12m, 12m, -2m, true, false, 300, 7613m, 913m, 2)])
        ];
        }

        public Task<int> AddRunAsync(BacktestRunDraft draft, CancellationToken cancellationToken)
        {
            var id = _nextId++;
            _runs.Add(new BacktestRunDetailResponse(
                id,
                draft.StrategyVersion,
                draft.SnapshotVersion,
                draft.StartDate,
                draft.EndDate,
                draft.SampleTradeCount,
                draft.WinRatePct,
                draft.AverageReturnPct,
                draft.AverageMaxGainPct,
                draft.AverageMaxDrawdownPct,
                draft.ProfitLossRatio,
                draft.MaxDrawdownPct,
                draft.TotalReturnPct,
                draft.BenchmarkReturnPct,
                draft.DataCoveragePct,
                draft.SkippedTradeDays,
                draft.AnnualTradeCount,
                draft.MaxConsecutiveLosses,
                draft.IsApproved,
                string.IsNullOrWhiteSpace(draft.FailureReasons) ? [] : draft.FailureReasons.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                draft.AverageHoldingDays,
                draft.CreatedAtUtc,
                draft.EquityCurve,
                draft.Trades));
            return Task.FromResult(id);
        }

        public Task<IReadOnlyList<BacktestRunListItemResponse>> GetRunsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<BacktestRunListItemResponse>>(
                _runs
                    .OrderByDescending(item => item.CreatedAtUtc)
                    .Select(item => new BacktestRunListItemResponse(
                        item.Id,
                        item.StrategyVersion,
                        item.SnapshotVersion,
                        item.StartDate,
                        item.EndDate,
                        item.SampleTradeCount,
                        item.WinRatePct,
                        item.AverageReturnPct,
                        item.ProfitLossRatio,
                        item.MaxDrawdownPct,
                        item.TotalReturnPct,
                        item.BenchmarkReturnPct,
                        item.DataCoveragePct,
                        item.SkippedTradeDays,
                        item.AnnualTradeCount,
                        item.MaxConsecutiveLosses,
                        item.IsApproved,
                        item.CreatedAtUtc))
                    .ToList());
        }

        public Task<BacktestRunDetailResponse?> GetRunAsync(int id, CancellationToken cancellationToken)
        {
            return Task.FromResult(_runs.FirstOrDefault(item => item.Id == id));
        }

        public Task<BacktestRunDetailResponse?> GetLatestRunAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_runs.OrderByDescending(item => item.CreatedAtUtc).FirstOrDefault());
        }
    }

    /// <summary>
    /// 学习复盘仓储的内存替身，用于控制器测试。
    /// </summary>
    private sealed class InMemoryLearningReviewRepository : ILearningReviewRepository
    {
        private readonly List<LearningReviewItemResponse> _reviews = [];
        private int _nextId = 1;

        public Task<IReadOnlyList<LearningReviewItemResponse>> GetReviewsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<LearningReviewItemResponse>>(_reviews.OrderByDescending(item => item.UpdatedAtUtc).ToList());
        }

        public Task<IReadOnlyList<LearningReviewItemResponse>> GetReviewsByStockAsync(string stockCode, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<LearningReviewItemResponse>>(
                _reviews
                    .Where(item => item.StockCode == stockCode)
                    .OrderByDescending(item => item.UpdatedAtUtc)
                    .ToList());
        }

        public Task<LearningReviewItemResponse> SaveReviewAsync(LearningReviewDraft draft, CancellationToken cancellationToken)
        {
            var timestamp = draft.TimestampUtc;

            if (draft.Id is int existingId)
            {
                var existingIndex = _reviews.FindIndex(item => item.Id == existingId);
                if (existingIndex < 0)
                {
                    throw new InvalidOperationException("要更新的复盘记录不存在。");
                }

                var existing = _reviews[existingIndex];
                var updated = new LearningReviewItemResponse(
                    existing.Id,
                    draft.PositionId,
                    draft.StockCode,
                    draft.StockName,
                    draft.TradeDate,
                    draft.SnapshotVersion,
                    draft.BuyReason,
                    draft.MarketContext,
                    draft.ExecutionDiscipline,
                    draft.ResultSummary,
                    draft.ImprovementPlan,
                    draft.ErrorTags,
                    draft.IsStrategyAligned,
                    draft.FollowedStopLoss,
                    draft.FollowedTakeProfit,
                    draft.ModifiedPlanDuringTrade,
                    draft.FollowedGapRule,
                    existing.CreatedAtUtc,
                    timestamp);

                _reviews[existingIndex] = updated;
                return Task.FromResult(updated);
            }

            var created = new LearningReviewItemResponse(
                _nextId++,
                draft.PositionId,
                draft.StockCode,
                draft.StockName,
                draft.TradeDate,
                draft.SnapshotVersion,
                draft.BuyReason,
                draft.MarketContext,
                draft.ExecutionDiscipline,
                draft.ResultSummary,
                draft.ImprovementPlan,
                draft.ErrorTags,
                draft.IsStrategyAligned,
                draft.FollowedStopLoss,
                draft.FollowedTakeProfit,
                draft.ModifiedPlanDuringTrade,
                draft.FollowedGapRule,
                timestamp,
                timestamp);

            _reviews.Add(created);
            return Task.FromResult(created);
        }
    }
}

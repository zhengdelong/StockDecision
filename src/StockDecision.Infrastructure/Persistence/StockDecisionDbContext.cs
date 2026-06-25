using Microsoft.EntityFrameworkCore;

namespace StockDecision.Infrastructure.Persistence;

/// <summary>
/// StockDecision 的 EF Core 数据库上下文。
/// </summary>
public sealed class StockDecisionDbContext(DbContextOptions<StockDecisionDbContext> options) : DbContext(options)
{
    /// <summary>原始股票基础信息表。</summary>
    public DbSet<RawStockRow> RawStocks => Set<RawStockRow>();
    public DbSet<LatestRawStockRow> LatestRawStocks => Set<LatestRawStockRow>();
    /// <summary>原始股票日线表。</summary>
    public DbSet<RawDailyBarRow> RawDailyBars => Set<RawDailyBarRow>();
    /// <summary>原始指数日线表。</summary>
    public DbSet<RawMarketIndexBarRow> RawMarketIndexBars => Set<RawMarketIndexBarRow>();
    /// <summary>原始行业统计表。</summary>
    public DbSet<RawIndustryDailyStatRow> RawIndustryDailyStats => Set<RawIndustryDailyStatRow>();
    /// <summary>原始财务快照表。</summary>
    public DbSet<RawFinancialSnapshotRow> RawFinancialSnapshots => Set<RawFinancialSnapshotRow>();
    /// <summary>采集日志表。</summary>
    public DbSet<DataIngestionLogRow> DataIngestionLogs => Set<DataIngestionLogRow>();
    /// <summary>领域同步运行日志表。</summary>
    public DbSet<DomainSyncRunRow> DomainSyncRuns => Set<DomainSyncRunRow>();

    /// <summary>股票画像快照表。</summary>
    public DbSet<MarketStockProfileEntity> MarketStockProfiles => Set<MarketStockProfileEntity>();
    /// <summary>领域层股票日线表。</summary>
    public DbSet<MarketDailyBarEntity> MarketDailyBars => Set<MarketDailyBarEntity>();
    /// <summary>领域层指数日线表。</summary>
    public DbSet<MarketIndexBarEntity> MarketIndexBars => Set<MarketIndexBarEntity>();
    /// <summary>领域层行业统计表。</summary>
    public DbSet<MarketIndustryDailyStatEntity> MarketIndustryDailyStats => Set<MarketIndustryDailyStatEntity>();
    /// <summary>领域层财务快照表。</summary>
    public DbSet<MarketFinancialSnapshotEntity> MarketFinancialSnapshots => Set<MarketFinancialSnapshotEntity>();
    /// <summary>技术指标快照表。</summary>
    public DbSet<StrategyIndicatorSnapshotEntity> StrategyIndicatorSnapshots => Set<StrategyIndicatorSnapshotEntity>();
    /// <summary>市场环境快照表。</summary>
    public DbSet<StrategyMarketRegimeEntity> StrategyMarketRegimes => Set<StrategyMarketRegimeEntity>();
    /// <summary>候选股结果表。</summary>
    public DbSet<StrategyCandidateEntity> StrategyCandidates => Set<StrategyCandidateEntity>();
    /// <summary>交易信号结果表。</summary>
    public DbSet<StrategyTradeSignalEntity> StrategyTradeSignals => Set<StrategyTradeSignalEntity>();
    public DbSet<SimulatedPositionEntity> SimulatedPositions => Set<SimulatedPositionEntity>();
    public DbSet<SimulatedTradeHistoryEntity> SimulatedTradeHistories => Set<SimulatedTradeHistoryEntity>();
    public DbSet<BacktestRunEntity> BacktestRuns => Set<BacktestRunEntity>();
    public DbSet<BacktestTradeResultEntity> BacktestTradeResults => Set<BacktestTradeResultEntity>();
    public DbSet<BacktestEquityPointEntity> BacktestEquityPoints => Set<BacktestEquityPointEntity>();
    public DbSet<LearningReviewEntity> LearningReviews => Set<LearningReviewEntity>();

    /// <summary>
    /// 配置所有原始表、领域快照表与策略结果表的映射关系。
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RawStockRow>(entity =>
        {
            entity.ToTable("raw_stocks");
            entity.HasKey(static item => item.Id);
            entity.Property(static item => item.StockCode).HasColumnName("stock_code");
            entity.Property(static item => item.StockName).HasColumnName("stock_name");
            entity.Property(static item => item.IndustryName).HasColumnName("industry_name");
            entity.Property(static item => item.IsActive).HasColumnName("is_active");
            entity.Property(static item => item.IsSt).HasColumnName("is_st");
            entity.Property(static item => item.IsDelistingRisk).HasColumnName("is_delisting_risk");
            entity.Property(static item => item.ListDate).HasColumnName("list_date");
            entity.Property(static item => item.Pe).HasColumnName("pe").HasPrecision(18, 4);
            entity.Property(static item => item.Pb).HasColumnName("pb").HasPrecision(18, 4);
            entity.Property(static item => item.TurnoverRate).HasColumnName("turnover_rate").HasPrecision(10, 4);
            entity.Property(static item => item.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<LatestRawStockRow>(entity =>
        {
            entity.ToTable("latest_raw_stocks");
            entity.HasKey(static item => item.StockCode);
            entity.Property(static item => item.StockCode).HasColumnName("stock_code");
            entity.Property(static item => item.StockName).HasColumnName("stock_name");
            entity.Property(static item => item.IndustryName).HasColumnName("industry_name");
            entity.Property(static item => item.IsActive).HasColumnName("is_active");
            entity.Property(static item => item.IsSt).HasColumnName("is_st");
            entity.Property(static item => item.IsDelistingRisk).HasColumnName("is_delisting_risk");
            entity.Property(static item => item.ListDate).HasColumnName("list_date");
            entity.Property(static item => item.InterfaceName).HasColumnName("interface_name");
            entity.Property(static item => item.BatchId).HasColumnName("batch_id");
            entity.Property(static item => item.FetchedAt).HasColumnName("fetched_at");
            entity.Property(static item => item.CreatedAt).HasColumnName("created_at");
            entity.Property(static item => item.UpdatedAt).HasColumnName("updated_at");
            entity.Property(static item => item.StockCode).HasMaxLength(16);
            entity.Property(static item => item.StockName).HasMaxLength(64);
            entity.Property(static item => item.IndustryName).HasMaxLength(64);
            entity.Property(static item => item.InterfaceName).HasMaxLength(64);
            entity.Property(static item => item.BatchId).HasMaxLength(64);
        });

        modelBuilder.Entity<RawDailyBarRow>(entity =>
        {
            entity.ToTable("raw_daily_bars");
            entity.HasKey(static item => item.Id);
            entity.Property(static item => item.StockCode).HasColumnName("stock_code");
            entity.Property(static item => item.TradeDate).HasColumnName("trade_date");
            entity.Property(static item => item.Open).HasColumnName("open").HasPrecision(18, 4);
            entity.Property(static item => item.High).HasColumnName("high").HasPrecision(18, 4);
            entity.Property(static item => item.Low).HasColumnName("low").HasPrecision(18, 4);
            entity.Property(static item => item.Close).HasColumnName("close").HasPrecision(18, 4);
            entity.Property(static item => item.Volume).HasColumnName("volume");
            entity.Property(static item => item.Amount).HasColumnName("amount").HasPrecision(20, 2);
            entity.Property(static item => item.PctChange).HasColumnName("pct_change").HasPrecision(10, 4);
            entity.Property(static item => item.TurnoverRate).HasColumnName("turnover_rate").HasPrecision(10, 4);
        });

        modelBuilder.Entity<RawMarketIndexBarRow>(entity =>
        {
            entity.ToTable("raw_market_index_bars");
            entity.HasKey(static item => item.Id);
            entity.Property(static item => item.IndexCode).HasColumnName("index_code");
            entity.Property(static item => item.IndexName).HasColumnName("index_name");
            entity.Property(static item => item.TradeDate).HasColumnName("trade_date");
            entity.Property(static item => item.Close).HasColumnName("close").HasPrecision(18, 4);
        });

        modelBuilder.Entity<RawIndustryDailyStatRow>(entity =>
        {
            entity.ToTable("raw_industry_daily_stats");
            entity.HasKey(static item => item.Id);
            entity.Property(static item => item.IndustryCode).HasColumnName("industry_code");
            entity.Property(static item => item.IndustryName).HasColumnName("industry_name");
            entity.Property(static item => item.TradeDate).HasColumnName("trade_date");
            entity.Property(static item => item.PctChange20d).HasColumnName("pct_change_20d").HasPrecision(10, 4);
            entity.Property(static item => item.Rank20d).HasColumnName("rank_20d");
        });

        modelBuilder.Entity<RawFinancialSnapshotRow>(entity =>
        {
            entity.ToTable("raw_financial_snapshots");
            entity.HasKey(static item => item.Id);
            entity.Property(static item => item.StockCode).HasColumnName("stock_code");
            entity.Property(static item => item.ReportDate).HasColumnName("report_date");
            entity.Property(static item => item.Pe).HasColumnName("pe").HasPrecision(18, 4);
            entity.Property(static item => item.Pb).HasColumnName("pb").HasPrecision(18, 4);
            entity.Property(static item => item.Roe).HasColumnName("roe").HasPrecision(18, 4);
            entity.Property(static item => item.RevenueYoy).HasColumnName("revenue_yoy").HasPrecision(18, 4);
            entity.Property(static item => item.NetProfitYoy).HasColumnName("net_profit_yoy").HasPrecision(18, 4);
            entity.Property(static item => item.FreeFloatMarketCap).HasColumnName("free_float_market_cap").HasPrecision(20, 2);
        });

        modelBuilder.Entity<DataIngestionLogRow>(entity =>
        {
            entity.ToTable("data_ingestion_logs");
            entity.HasKey(static item => item.Id);
            entity.Property(static item => item.TargetScope).HasColumnName("target_scope");
            entity.Property(static item => item.IsComplete).HasColumnName("is_complete");
            entity.Property(static item => item.IsSignalEligible).HasColumnName("is_signal_eligible");
            entity.Property(static item => item.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<DomainSyncRunRow>(entity =>
        {
            entity.ToTable("domain_sync_runs");
            entity.HasKey(static item => item.Id);
            entity.Property(static item => item.Id).HasColumnName("id");
            entity.Property(static item => item.JobName).HasColumnName("job_name").HasMaxLength(64);
            entity.Property(static item => item.TriggerKind).HasColumnName("trigger_kind").HasMaxLength(32);
            entity.Property(static item => item.SnapshotVersion).HasColumnName("snapshot_version").HasMaxLength(32);
            entity.Property(static item => item.Status).HasColumnName("status").HasMaxLength(32);
            entity.Property(static item => item.DataUpdated).HasColumnName("data_updated");
            entity.Property(static item => item.IsSignalEligible).HasColumnName("is_signal_eligible");
            entity.Property(static item => item.EffectiveTradeDate).HasColumnName("effective_trade_date");
            entity.Property(static item => item.FinancialReportDate).HasColumnName("financial_report_date");
            entity.Property(static item => item.StartedAt).HasColumnName("started_at");
            entity.Property(static item => item.FinishedAt).HasColumnName("finished_at");
            entity.Property(static item => item.Summary).HasColumnName("summary").HasMaxLength(512);
            entity.Property(static item => item.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(static item => item.CreatedAt);
        });

        modelBuilder.Entity<MarketStockProfileEntity>(entity =>
        {
            entity.ToTable("market_stock_profiles");
            entity.HasKey(static item => new { item.StockCode, item.TradeDate });
            entity.Property(static item => item.StockCode).HasColumnName("stock_code");
            entity.Property(static item => item.TradeDate).HasColumnName("trade_date");
            entity.Property(static item => item.StockName).HasColumnName("stock_name");
            entity.Property(static item => item.IndustryName).HasColumnName("industry_name");
            entity.Property(static item => item.IsActive).HasColumnName("is_active");
            entity.Property(static item => item.IsSt).HasColumnName("is_st");
            entity.Property(static item => item.IsDelistingRisk).HasColumnName("is_delisting_risk");
            entity.Property(static item => item.ListDate).HasColumnName("list_date");
            entity.Property(static item => item.LatestPrice).HasColumnName("latest_price");
            entity.Property(static item => item.Pe).HasColumnName("pe");
            entity.Property(static item => item.Pb).HasColumnName("pb");
            entity.Property(static item => item.FreeFloatMarketCap).HasColumnName("free_float_market_cap");
            entity.Property(static item => item.TurnoverRate).HasColumnName("turnover_rate");
            entity.Property(static item => item.AverageAmount20d).HasColumnName("average_amount20d");
            entity.Property(static item => item.StockCode).HasMaxLength(16);
            entity.Property(static item => item.StockName).HasMaxLength(64);
            entity.Property(static item => item.IndustryName).HasMaxLength(64);
            entity.Property(static item => item.LatestPrice).HasPrecision(18, 4);
            entity.Property(static item => item.Pe).HasPrecision(18, 4);
            entity.Property(static item => item.Pb).HasPrecision(18, 4);
            entity.Property(static item => item.FreeFloatMarketCap).HasPrecision(20, 2);
            entity.Property(static item => item.TurnoverRate).HasPrecision(10, 4);
            entity.Property(static item => item.AverageAmount20d).HasPrecision(20, 2);
        });

        modelBuilder.Entity<MarketDailyBarEntity>(entity =>
        {
            entity.ToTable("market_daily_bars");
            entity.HasKey(static item => new { item.StockCode, item.TradeDate });
            entity.Property(static item => item.StockCode).HasColumnName("stock_code");
            entity.Property(static item => item.TradeDate).HasColumnName("trade_date");
            entity.Property(static item => item.Open).HasColumnName("open");
            entity.Property(static item => item.High).HasColumnName("high");
            entity.Property(static item => item.Low).HasColumnName("low");
            entity.Property(static item => item.Close).HasColumnName("close");
            entity.Property(static item => item.Volume).HasColumnName("volume");
            entity.Property(static item => item.Amount).HasColumnName("amount");
            entity.Property(static item => item.PctChange).HasColumnName("pct_change");
            entity.Property(static item => item.TurnoverRate).HasColumnName("turnover_rate");
            entity.Property(static item => item.Open).HasPrecision(18, 4);
            entity.Property(static item => item.High).HasPrecision(18, 4);
            entity.Property(static item => item.Low).HasPrecision(18, 4);
            entity.Property(static item => item.Close).HasPrecision(18, 4);
            entity.Property(static item => item.Amount).HasPrecision(20, 2);
            entity.Property(static item => item.PctChange).HasPrecision(10, 4);
            entity.Property(static item => item.TurnoverRate).HasPrecision(10, 4);
        });

        modelBuilder.Entity<MarketIndexBarEntity>(entity =>
        {
            entity.ToTable("market_index_bars");
            entity.HasKey(static item => new { item.IndexCode, item.TradeDate });
            entity.Property(static item => item.IndexCode).HasColumnName("index_code");
            entity.Property(static item => item.TradeDate).HasColumnName("trade_date");
            entity.Property(static item => item.IndexName).HasColumnName("index_name");
            entity.Property(static item => item.Close).HasColumnName("close");
            entity.Property(static item => item.IndexName).HasMaxLength(64);
            entity.Property(static item => item.Close).HasPrecision(18, 4);
        });

        modelBuilder.Entity<MarketIndustryDailyStatEntity>(entity =>
        {
            entity.ToTable("market_industry_daily_stats");
            entity.HasKey(static item => new { item.IndustryCode, item.TradeDate });
            entity.Property(static item => item.IndustryCode).HasColumnName("industry_code");
            entity.Property(static item => item.TradeDate).HasColumnName("trade_date");
            entity.Property(static item => item.IndustryName).HasColumnName("industry_name");
            entity.Property(static item => item.PctChange20d).HasColumnName("pct_change_20d");
            entity.Property(static item => item.Rank20d).HasColumnName("rank_20d");
            entity.Property(static item => item.IndustryName).HasMaxLength(64);
            entity.Property(static item => item.PctChange20d).HasPrecision(10, 4);
        });

        modelBuilder.Entity<MarketFinancialSnapshotEntity>(entity =>
        {
            entity.ToTable("market_financial_snapshots");
            entity.HasKey(static item => new { item.StockCode, item.ReportDate });
            entity.Property(static item => item.StockCode).HasColumnName("stock_code");
            entity.Property(static item => item.ReportDate).HasColumnName("report_date");
            entity.Property(static item => item.Pe).HasColumnName("pe");
            entity.Property(static item => item.Pb).HasColumnName("pb");
            entity.Property(static item => item.Roe).HasColumnName("roe");
            entity.Property(static item => item.RevenueYoy).HasColumnName("revenue_yoy");
            entity.Property(static item => item.NetProfitYoy).HasColumnName("net_profit_yoy");
            entity.Property(static item => item.FreeFloatMarketCap).HasColumnName("free_float_market_cap");
            entity.Property(static item => item.Pe).HasPrecision(18, 4);
            entity.Property(static item => item.Pb).HasPrecision(18, 4);
            entity.Property(static item => item.Roe).HasPrecision(18, 4);
            entity.Property(static item => item.RevenueYoy).HasPrecision(18, 4);
            entity.Property(static item => item.NetProfitYoy).HasPrecision(18, 4);
            entity.Property(static item => item.FreeFloatMarketCap).HasPrecision(20, 2);
        });

        modelBuilder.Entity<StrategyIndicatorSnapshotEntity>(entity =>
        {
            entity.ToTable("strategy_indicator_snapshots");
            entity.HasKey(static item => new { item.StockCode, item.TradeDate, item.SnapshotVersion });
            entity.Property(static item => item.StockCode).HasColumnName("stock_code");
            entity.Property(static item => item.TradeDate).HasColumnName("trade_date");
            entity.Property(static item => item.SnapshotVersion).HasColumnName("snapshot_version");
            entity.Property(static item => item.Close).HasColumnName("close");
            entity.Property(static item => item.Ma20).HasColumnName("ma20");
            entity.Property(static item => item.Ma60).HasColumnName("ma60");
            entity.Property(static item => item.Ma120).HasColumnName("ma120");
            entity.Property(static item => item.Atr14).HasColumnName("atr14");
            entity.Property(static item => item.Return20d).HasColumnName("return20d");
            entity.Property(static item => item.Return60d).HasColumnName("return60d");
            entity.Property(static item => item.RelativeStrengthScore).HasColumnName("relative_strength_score");
            entity.Property(static item => item.Is20DayBreakout).HasColumnName("is20_day_breakout");
            entity.Property(static item => item.IsMa20Upward).HasColumnName("is_ma20_upward");
            entity.Property(static item => item.IsBullishStacked).HasColumnName("is_bullish_stacked");
            entity.Property(static item => item.DistanceToMa20Pct).HasColumnName("distance_to_ma20_pct");
            entity.Property(static item => item.TurnoverRate).HasColumnName("turnover_rate");
            entity.Property(static item => item.Close).HasPrecision(18, 4);
            entity.Property(static item => item.Ma20).HasPrecision(18, 4);
            entity.Property(static item => item.Ma60).HasPrecision(18, 4);
            entity.Property(static item => item.Ma120).HasPrecision(18, 4);
            entity.Property(static item => item.Atr14).HasPrecision(18, 4);
            entity.Property(static item => item.Return20d).HasPrecision(10, 4);
            entity.Property(static item => item.Return60d).HasPrecision(10, 4);
            entity.Property(static item => item.RelativeStrengthScore).HasPrecision(10, 4);
            entity.Property(static item => item.DistanceToMa20Pct).HasPrecision(10, 4);
            entity.Property(static item => item.TurnoverRate).HasPrecision(10, 4);
            entity.Property(static item => item.SnapshotVersion).HasMaxLength(32);
        });

        modelBuilder.Entity<StrategyMarketRegimeEntity>(entity =>
        {
            entity.ToTable("strategy_market_regimes");
            entity.HasKey(static item => new { item.TradeDate, item.SnapshotVersion });
            entity.Property(static item => item.TradeDate).HasColumnName("trade_date");
            entity.Property(static item => item.SnapshotVersion).HasColumnName("snapshot_version");
            entity.Property(static item => item.Regime).HasColumnName("regime");
            entity.Property(static item => item.ConfirmedIndexCount).HasColumnName("confirmed_index_count");
            entity.Property(static item => item.IsSignalEligible).HasColumnName("is_signal_eligible");
            entity.Property(static item => item.Summary).HasColumnName("summary");
            entity.Property(static item => item.SnapshotVersion).HasMaxLength(32);
            entity.Property(static item => item.Regime).HasMaxLength(32);
            entity.Property(static item => item.Summary).HasMaxLength(256);
        });

        modelBuilder.Entity<StrategyCandidateEntity>(entity =>
        {
            entity.ToTable("strategy_candidates");
            entity.HasKey(static item => new { item.TradeDate, item.SnapshotVersion, item.StockCode, item.StrategyType });
            entity.Property(static item => item.TradeDate).HasColumnName("trade_date");
            entity.Property(static item => item.SnapshotVersion).HasColumnName("snapshot_version");
            entity.Property(static item => item.StockCode).HasColumnName("stock_code");
            entity.Property(static item => item.StockName).HasColumnName("stock_name");
            entity.Property(static item => item.IndustryName).HasColumnName("industry_name");
            entity.Property(static item => item.Grade).HasColumnName("grade");
            entity.Property(static item => item.StrategyType).HasColumnName("strategy_type");
            entity.Property(static item => item.IsTradable).HasColumnName("is_tradable");
            entity.Property(static item => item.TotalScore).HasColumnName("total_score");
            entity.Property(static item => item.RelativeStrengthScorePart).HasColumnName("relative_strength_score_part");
            entity.Property(static item => item.TrendScorePart).HasColumnName("trend_score_part");
            entity.Property(static item => item.VolumePriceScorePart).HasColumnName("volume_price_score_part");
            entity.Property(static item => item.FundamentalScorePart).HasColumnName("fundamental_score_part");
            entity.Property(static item => item.Close).HasColumnName("close");
            entity.Property(static item => item.Ma20).HasColumnName("ma20");
            entity.Property(static item => item.Ma60).HasColumnName("ma60");
            entity.Property(static item => item.Ma120).HasColumnName("ma120");
            entity.Property(static item => item.Atr14).HasColumnName("atr14");
            entity.Property(static item => item.RelativeStrengthScore).HasColumnName("relative_strength_score");
            entity.Property(static item => item.Pe).HasColumnName("pe");
            entity.Property(static item => item.Pb).HasColumnName("pb");
            entity.Property(static item => item.Roe).HasColumnName("roe");
            entity.Property(static item => item.StopLossPrice).HasColumnName("stop_loss_price");
            entity.Property(static item => item.TargetPrice).HasColumnName("target_price");
            entity.Property(static item => item.RiskRewardRatio).HasColumnName("risk_reward_ratio");
            entity.Property(static item => item.Explanation).HasColumnName("explanation");
            entity.Property(static item => item.StockName).HasMaxLength(64);
            entity.Property(static item => item.IndustryName).HasMaxLength(64);
            entity.Property(static item => item.SnapshotVersion).HasMaxLength(32);
            entity.Property(static item => item.Grade).HasMaxLength(8);
            entity.Property(static item => item.StrategyType).HasMaxLength(32);
            entity.Property(static item => item.TotalScore).HasPrecision(10, 4);
            entity.Property(static item => item.RelativeStrengthScorePart).HasPrecision(10, 4);
            entity.Property(static item => item.TrendScorePart).HasPrecision(10, 4);
            entity.Property(static item => item.VolumePriceScorePart).HasPrecision(10, 4);
            entity.Property(static item => item.FundamentalScorePart).HasPrecision(10, 4);
            entity.Property(static item => item.Close).HasPrecision(18, 4);
            entity.Property(static item => item.Ma20).HasPrecision(18, 4);
            entity.Property(static item => item.Ma60).HasPrecision(18, 4);
            entity.Property(static item => item.Ma120).HasPrecision(18, 4);
            entity.Property(static item => item.Atr14).HasPrecision(18, 4);
            entity.Property(static item => item.RelativeStrengthScore).HasPrecision(10, 4);
            entity.Property(static item => item.Pe).HasPrecision(18, 4);
            entity.Property(static item => item.Pb).HasPrecision(18, 4);
            entity.Property(static item => item.Roe).HasPrecision(18, 4);
            entity.Property(static item => item.StopLossPrice).HasPrecision(18, 4);
            entity.Property(static item => item.TargetPrice).HasPrecision(18, 4);
            entity.Property(static item => item.RiskRewardRatio).HasPrecision(10, 4);
            entity.Property(static item => item.Explanation).HasColumnType("longtext");
        });

        modelBuilder.Entity<StrategyTradeSignalEntity>(entity =>
        {
            entity.ToTable("strategy_trade_signals");
            entity.HasKey(static item => new { item.TradeDate, item.SnapshotVersion, item.StockCode, item.StrategyType });
            entity.Property(static item => item.TradeDate).HasColumnName("trade_date");
            entity.Property(static item => item.SnapshotVersion).HasColumnName("snapshot_version");
            entity.Property(static item => item.StockCode).HasColumnName("stock_code");
            entity.Property(static item => item.StockName).HasColumnName("stock_name");
            entity.Property(static item => item.IndustryName).HasColumnName("industry_name");
            entity.Property(static item => item.StrategyType).HasColumnName("strategy_type");
            entity.Property(static item => item.TotalScore).HasColumnName("total_score");
            entity.Property(static item => item.RelativeStrengthScorePart).HasColumnName("relative_strength_score_part");
            entity.Property(static item => item.TrendScorePart).HasColumnName("trend_score_part");
            entity.Property(static item => item.VolumePriceScorePart).HasColumnName("volume_price_score_part");
            entity.Property(static item => item.FundamentalScorePart).HasColumnName("fundamental_score_part");
            entity.Property(static item => item.TriggerPrice).HasColumnName("trigger_price");
            entity.Property(static item => item.StopLossPrice).HasColumnName("stop_loss_price");
            entity.Property(static item => item.TargetPrice).HasColumnName("target_price");
            entity.Property(static item => item.RiskRewardRatio).HasColumnName("risk_reward_ratio");
            entity.Property(static item => item.SuggestedCapital).HasColumnName("suggested_capital");
            entity.Property(static item => item.EstimatedShares).HasColumnName("estimated_shares");
            entity.Property(static item => item.Explanation).HasColumnName("explanation");
            entity.Property(static item => item.GeneratedAtUtc).HasColumnName("generated_at_utc");
            entity.Property(static item => item.StockName).HasMaxLength(64);
            entity.Property(static item => item.IndustryName).HasMaxLength(64);
            entity.Property(static item => item.SnapshotVersion).HasMaxLength(32);
            entity.Property(static item => item.StrategyType).HasMaxLength(32);
            entity.Property(static item => item.TotalScore).HasPrecision(10, 4);
            entity.Property(static item => item.RelativeStrengthScorePart).HasPrecision(10, 4);
            entity.Property(static item => item.TrendScorePart).HasPrecision(10, 4);
            entity.Property(static item => item.VolumePriceScorePart).HasPrecision(10, 4);
            entity.Property(static item => item.FundamentalScorePart).HasPrecision(10, 4);
            entity.Property(static item => item.TriggerPrice).HasPrecision(18, 4);
            entity.Property(static item => item.StopLossPrice).HasPrecision(18, 4);
            entity.Property(static item => item.TargetPrice).HasPrecision(18, 4);
            entity.Property(static item => item.RiskRewardRatio).HasPrecision(10, 4);
            entity.Property(static item => item.SuggestedCapital).HasPrecision(18, 2);
            entity.Property(static item => item.Explanation).HasColumnType("longtext");
        });

        modelBuilder.Entity<SimulatedPositionEntity>(entity =>
        {
            entity.ToTable("simulated_positions");
            entity.HasKey(static item => item.Id);
            entity.Property(static item => item.Id).HasColumnName("id");
            entity.Property(static item => item.StockCode).HasColumnName("stock_code").HasMaxLength(16);
            entity.Property(static item => item.StockName).HasColumnName("stock_name").HasMaxLength(64);
            entity.Property(static item => item.IndustryName).HasColumnName("industry_name").HasMaxLength(64);
            entity.Property(static item => item.StrategyType).HasColumnName("strategy_type").HasMaxLength(32);
            entity.Property(static item => item.SnapshotVersion).HasColumnName("snapshot_version").HasMaxLength(32);
            entity.Property(static item => item.TradeDate).HasColumnName("trade_date");
            entity.Property(static item => item.EntryPrice).HasColumnName("entry_price").HasPrecision(18, 4);
            entity.Property(static item => item.StopLossPrice).HasColumnName("stop_loss_price").HasPrecision(18, 4);
            entity.Property(static item => item.TargetPrice).HasColumnName("target_price").HasPrecision(18, 4);
            entity.Property(static item => item.Quantity).HasColumnName("quantity");
            entity.Property(static item => item.InvestedCapital).HasColumnName("invested_capital").HasPrecision(18, 2);
            entity.Property(static item => item.Status).HasColumnName("status").HasMaxLength(32);
            entity.Property(static item => item.OpenedAtUtc).HasColumnName("opened_at_utc");
            entity.Property(static item => item.ClosedAtUtc).HasColumnName("closed_at_utc");
            entity.Property(static item => item.ClosedTradeDate).HasColumnName("closed_trade_date");
            entity.Property(static item => item.ExitPrice).HasColumnName("exit_price").HasPrecision(18, 4);
            entity.Property(static item => item.RealizedProfitAmount).HasColumnName("realized_profit_amount").HasPrecision(18, 2);
            entity.Property(static item => item.RealizedProfitPct).HasColumnName("realized_profit_pct").HasPrecision(10, 4);
            entity.Property(static item => item.Notes).HasColumnName("notes").HasColumnType("longtext");
            entity.HasIndex(static item => item.Status);
            entity.HasIndex(static item => item.StockCode);
        });

        modelBuilder.Entity<SimulatedTradeHistoryEntity>(entity =>
        {
            entity.ToTable("simulated_trade_histories");
            entity.HasKey(static item => item.Id);
            entity.Property(static item => item.Id).HasColumnName("id");
            entity.Property(static item => item.PositionId).HasColumnName("position_id");
            entity.Property(static item => item.ActionType).HasColumnName("action_type").HasMaxLength(16);
            entity.Property(static item => item.StockCode).HasColumnName("stock_code").HasMaxLength(16);
            entity.Property(static item => item.StockName).HasColumnName("stock_name").HasMaxLength(64);
            entity.Property(static item => item.TradeDate).HasColumnName("trade_date");
            entity.Property(static item => item.Price).HasColumnName("price").HasPrecision(18, 4);
            entity.Property(static item => item.Quantity).HasColumnName("quantity");
            entity.Property(static item => item.Amount).HasColumnName("amount").HasPrecision(18, 2);
            entity.Property(static item => item.Summary).HasColumnName("summary").HasMaxLength(256);
            entity.Property(static item => item.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.HasIndex(static item => item.PositionId);
            entity.HasIndex(static item => item.CreatedAtUtc);
        });

        modelBuilder.Entity<BacktestRunEntity>(entity =>
        {
            entity.ToTable("backtest_runs");
            entity.HasKey(static item => item.Id);
            entity.Property(static item => item.Id).HasColumnName("id");
            entity.Property(static item => item.StrategyVersion).HasColumnName("strategy_version").HasMaxLength(32);
            entity.Property(static item => item.SnapshotVersion).HasColumnName("snapshot_version").HasMaxLength(32);
            entity.Property(static item => item.StartDate).HasColumnName("start_date");
            entity.Property(static item => item.EndDate).HasColumnName("end_date");
            entity.Property(static item => item.SampleTradeCount).HasColumnName("sample_trade_count");
            entity.Property(static item => item.WinRatePct).HasColumnName("win_rate_pct").HasPrecision(10, 4);
            entity.Property(static item => item.AverageReturnPct).HasColumnName("average_return_pct").HasPrecision(10, 4);
            entity.Property(static item => item.AverageMaxGainPct).HasColumnName("average_max_gain_pct").HasPrecision(10, 4);
            entity.Property(static item => item.AverageMaxDrawdownPct).HasColumnName("average_max_drawdown_pct").HasPrecision(10, 4);
            entity.Property(static item => item.ProfitLossRatio).HasColumnName("profit_loss_ratio").HasPrecision(10, 4);
            entity.Property(static item => item.MaxDrawdownPct).HasColumnName("max_drawdown_pct").HasPrecision(10, 4);
            entity.Property(static item => item.TotalReturnPct).HasColumnName("total_return_pct").HasPrecision(10, 4);
            entity.Property(static item => item.AverageHoldingDays).HasColumnName("average_holding_days").HasPrecision(10, 4);
            entity.Property(static item => item.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.HasIndex(static item => item.CreatedAtUtc);
        });

        modelBuilder.Entity<BacktestTradeResultEntity>(entity =>
        {
            entity.ToTable("backtest_trade_results");
            entity.HasKey(static item => item.Id);
            entity.Property(static item => item.Id).HasColumnName("id");
            entity.Property(static item => item.BacktestRunId).HasColumnName("backtest_run_id");
            entity.Property(static item => item.TradeDate).HasColumnName("trade_date");
            entity.Property(static item => item.StockCode).HasColumnName("stock_code").HasMaxLength(16);
            entity.Property(static item => item.StockName).HasColumnName("stock_name").HasMaxLength(64);
            entity.Property(static item => item.StrategyType).HasColumnName("strategy_type").HasMaxLength(32);
            entity.Property(static item => item.EntryPrice).HasColumnName("entry_price").HasPrecision(18, 4);
            entity.Property(static item => item.ExitPrice).HasColumnName("exit_price").HasPrecision(18, 4);
            entity.Property(static item => item.ReturnPct).HasColumnName("return_pct").HasPrecision(10, 4);
            entity.Property(static item => item.MaxGainPct).HasColumnName("max_gain_pct").HasPrecision(10, 4);
            entity.Property(static item => item.MaxDrawdownPct).HasColumnName("max_drawdown_pct").HasPrecision(10, 4);
            entity.Property(static item => item.HitTarget).HasColumnName("hit_target");
            entity.Property(static item => item.HitStopLoss).HasColumnName("hit_stop_loss");
            entity.HasIndex(static item => item.BacktestRunId);
        });

        modelBuilder.Entity<BacktestEquityPointEntity>(entity =>
        {
            entity.ToTable("backtest_equity_points");
            entity.HasKey(static item => item.Id);
            entity.Property(static item => item.Id).HasColumnName("id");
            entity.Property(static item => item.BacktestRunId).HasColumnName("backtest_run_id");
            entity.Property(static item => item.TradeDate).HasColumnName("trade_date");
            entity.Property(static item => item.Equity).HasColumnName("equity").HasPrecision(18, 4);
            entity.Property(static item => item.ReturnPct).HasColumnName("return_pct").HasPrecision(10, 4);
            entity.HasIndex(static item => item.BacktestRunId);
        });

        modelBuilder.Entity<LearningReviewEntity>(entity =>
        {
            entity.ToTable("learning_reviews");
            entity.HasKey(static item => item.Id);
            entity.Property(static item => item.Id).HasColumnName("id");
            entity.Property(static item => item.PositionId).HasColumnName("position_id");
            entity.Property(static item => item.StockCode).HasColumnName("stock_code").HasMaxLength(16);
            entity.Property(static item => item.StockName).HasColumnName("stock_name").HasMaxLength(64);
            entity.Property(static item => item.TradeDate).HasColumnName("trade_date");
            entity.Property(static item => item.SnapshotVersion).HasColumnName("snapshot_version").HasMaxLength(32);
            entity.Property(static item => item.BuyReason).HasColumnName("buy_reason").HasColumnType("longtext");
            entity.Property(static item => item.MarketContext).HasColumnName("market_context").HasColumnType("longtext");
            entity.Property(static item => item.ExecutionDiscipline).HasColumnName("execution_discipline").HasColumnType("longtext");
            entity.Property(static item => item.ResultSummary).HasColumnName("result_summary").HasColumnType("longtext");
            entity.Property(static item => item.ImprovementPlan).HasColumnName("improvement_plan").HasColumnType("longtext");
            entity.Property(static item => item.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(static item => item.UpdatedAtUtc).HasColumnName("updated_at_utc");
            entity.HasIndex(static item => item.StockCode);
            entity.HasIndex(static item => item.UpdatedAtUtc);
        });
    }
}

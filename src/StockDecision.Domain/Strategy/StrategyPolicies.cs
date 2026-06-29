using StockDecision.Domain.Market;

namespace StockDecision.Domain.Strategy;

/// <summary>
/// 提供策略计算所需的基础指标算法。
/// </summary>
public static class IndicatorMath
{
    /// <summary>
    /// 计算简单移动平均线。
    /// </summary>
    public static decimal CalculateSimpleMovingAverage(IReadOnlyList<DailyBar> bars, int period)
    {
        if (bars.Count < period)
        {
            return 0m;
        }

        return Math.Round(bars.TakeLast(period).Average(static item => item.Close), 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// 计算指定周期的收益率百分比。
    /// </summary>
    public static decimal CalculateReturn(IReadOnlyList<DailyBar> bars, int period)
    {
        if (bars.Count <= period)
        {
            return 0m;
        }

        var latest = bars[^1].Close;
        var baseline = bars[bars.Count - 1 - period].Close;
        if (baseline == 0m)
        {
            return 0m;
        }

        return Math.Round(((latest - baseline) / baseline) * 100m, 4, MidpointRounding.AwayFromZero);
    }

    public static decimal CalculateIndexReturn(IReadOnlyList<MarketIndexBar> bars, int period)
    {
        if (bars.Count <= period)
        {
            return 0m;
        }

        var latest = bars[^1].Close;
        var baseline = bars[bars.Count - 1 - period].Close;
        if (baseline == 0m)
        {
            return 0m;
        }

        return Math.Round(((latest - baseline) / baseline) * 100m, 4, MidpointRounding.AwayFromZero);
    }

    public static decimal CalculateAmountRatio(IReadOnlyList<DailyBar> bars, int period)
    {
        if (bars.Count <= period)
        {
            return 0m;
        }

        var latestAmount = bars[^1].Amount;
        var averageAmount = bars.TakeLast(period + 1).Take(period).Average(static item => item.Amount);
        if (averageAmount <= 0m)
        {
            return 0m;
        }

        return Math.Round(latestAmount / averageAmount, 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// 计算 14 日平均真实波幅。
    /// </summary>
    public static decimal CalculateAtr14(IReadOnlyList<DailyBar> bars)
    {
        if (bars.Count < 15)
        {
            return 0m;
        }

        var trValues = new List<decimal>(capacity: 14);
        for (var index = bars.Count - 14; index < bars.Count; index++)
        {
            var current = bars[index];
            var previousClose = bars[index - 1].Close;
            var trueRange = new[]
            {
                current.High - current.Low,
                Math.Abs(current.High - previousClose),
                Math.Abs(current.Low - previousClose)
            }.Max();

            trValues.Add(trueRange);
        }

        return Math.Round(trValues.Average(), 4, MidpointRounding.AwayFromZero);
    }
}

/// <summary>
/// 根据主要指数走势判断市场环境。
/// </summary>
public static class MarketRegimePolicy
{
    /// <summary>
    /// 评估指定交易日的市场环境。
    /// </summary>
    public static MarketRegimeSnapshot Evaluate(
        DateOnly tradeDate,
        IReadOnlyDictionary<string, IReadOnlyList<MarketIndexBar>> indexBars)
    {
        var confirmations = 0;
        foreach (var pair in indexBars)
        {
            var ordered = pair.Value.OrderBy(static item => item.TradeDate).ToList();
            if (ordered.Count < 25)
            {
                continue;
            }

            var currentClose = ordered[^1].Close;
            var currentMa20 = ordered.TakeLast(20).Average(static item => item.Close);
            var fiveDaysAgoMa20 = ordered.Take(ordered.Count - 5).TakeLast(20).Average(static item => item.Close);

            // 仅在指数站上 20 日线且均线本身上行时计为有效确认，避免把反弹末端误判为强势环境。
            if (currentClose > currentMa20 && currentMa20 > fiveDaysAgoMa20)
            {
                confirmations++;
            }
        }

        var regime = confirmations switch
        {
            3 => MarketSignalEligibility.Strong,
            2 => MarketSignalEligibility.Tradable,
            1 => MarketSignalEligibility.WeakOpportunity,
            _ => MarketSignalEligibility.NoTrade
        };

        return new MarketRegimeSnapshot(
            tradeDate,
            regime,
            confirmations,
            regime != MarketSignalEligibility.NoTrade,
            $"已确认指数数量：{confirmations}/3");
    }
}

public sealed record CandidateScoringContext(
    decimal Return10d,
    decimal IndexReturn20d,
    decimal IndexReturn60d,
    decimal? IndustryReturn20d,
    bool IsMa60Upward,
    decimal AmountRatio1d,
    StockFundFlowSnapshot? StockFundFlow,
    IndustryFundFlowSnapshot? IndustryFundFlow,
    LhbSnapshot? Lhb);

/// <summary>
/// 封装候选股与交易信号的业务规则。
/// </summary>
public static class CandidatePolicy
{
    private const decimal CandidatePoolMinimumScore = 82m;
    private const decimal StrongWatchMinimumScore = 88m;
    private const decimal TradableMinimumScore = 90m;

    /// <summary>
    /// 基于当前画像、指标和财务快照重建评分拆解，供接口按最新规则解释得分过程。
    /// </summary>
    public static CandidateScoreBreakdown DescribeScoreBreakdown(
        StockProfile profile,
        IndicatorSnapshot indicator,
        FinancialSnapshot? financial,
        IndustryDailyStat? industry,
        CandidateScoringContext? context = null)
    {
        return BuildScoreBreakdown(profile, indicator, financial, industry, context);
    }

    public static CandidateListPreview DescribeCandidatePreview(
        DateOnly tradeDate,
        StockProfile profile,
        IndicatorSnapshot indicator,
        FinancialSnapshot? financial,
        IndustryDailyStat? industry,
        MarketRegimeSnapshot regime,
        bool isInTradablePool,
        CandidateScoringContext? context = null)
    {
        var resolvedContext = context ?? BuildDefaultContext(indicator, industry);
        var scoreBreakdown = BuildScoreBreakdown(profile, indicator, financial, industry, resolvedContext);
        var totalScore = scoreBreakdown.TotalScore;
        var strategyType = ResolveStrategyType(indicator, regime);
        var stopLoss = ResolveStopLoss(indicator.Close, indicator.Ma20, strategyType);
        var targetPrice = ResolveTargetPrice(indicator.Close, strategyType);
        var riskReward = CalculateRiskReward(indicator.Close, stopLoss, targetPrice);
        var grade = ResolveGrade(totalScore);
        var eligibility = ResolveEligibility(totalScore, strategyType, regime, isInTradablePool, riskReward);

        return new CandidateListPreview(
            tradeDate,
            profile.StockCode,
            profile.StockName,
            profile.IndustryName,
            grade,
            strategyType,
            eligibility.IsTradable,
            eligibility.Status,
            eligibility.Reason,
            totalScore,
            scoreBreakdown,
            indicator.Close,
            indicator.Ma20,
            indicator.Ma60,
            indicator.Atr14,
            indicator.RelativeStrengthScore,
            stopLoss,
            targetPrice,
            riskReward,
            BuildExplanation(profile, indicator, industry, regime, scoreBreakdown, riskReward, eligibility.Reason, resolvedContext));
    }

    /// <summary>
    /// 根据画像、指标和市场环境评估候选股。
    /// </summary>
    public static CandidateStock? Evaluate(
        DateOnly tradeDate,
        StockProfile profile,
        IndicatorSnapshot indicator,
        FinancialSnapshot? financial,
        IndustryDailyStat? industry,
        CandidateScoringContext context,
        MarketRegimeSnapshot regime,
        bool isInTradablePool,
        bool isInWatchPool)
    {
        if (!isInWatchPool)
        {
            return null;
        }

        if (!profile.IsActive || profile.IsSt || profile.IsDelistingRisk)
        {
            return null;
        }

        // 新股历史样本不足且波动特征不稳定，直接排除。
        if (profile.ListDate is DateOnly listDate && listDate > tradeDate.AddDays(-250))
        {
            return null;
        }

        // 主趋势至少要站上 60 日线，否则不进入候选池。
        if (indicator.Close <= 0m || indicator.Ma60 <= 0m || indicator.Close <= indicator.Ma60)
        {
            return null;
        }

        var atrPct = indicator.Close == 0m ? 0m : indicator.Atr14 / indicator.Close * 100m;
        if (atrPct > 7m)
        {
            return null;
        }

        if (profile.LatestPrice is < 5m or > 80m)
        {
            return null;
        }

        if (profile.AverageAmount20d is < 200_000_000m)
        {
            return null;
        }

        if (context.Return10d > 30m)
        {
            return null;
        }

        var scoreBreakdown = BuildScoreBreakdown(profile, indicator, financial, industry, context);
        var totalScore = scoreBreakdown.TotalScore;
        if (totalScore < CandidatePoolMinimumScore)
        {
            return null;
        }

        var strategyType = ResolveStrategyType(indicator, regime);
        var stopLoss = ResolveStopLoss(indicator.Close, indicator.Ma20, strategyType);
        var targetPrice = ResolveTargetPrice(indicator.Close, strategyType);
        var riskReward = CalculateRiskReward(indicator.Close, stopLoss, targetPrice);
        var grade = ResolveGrade(totalScore);
        var eligibility = ResolveEligibility(totalScore, strategyType, regime, isInTradablePool, riskReward);

        return new CandidateStock(
            tradeDate,
            profile.StockCode,
            profile.StockName,
            profile.IndustryName,
            grade,
            strategyType,
            eligibility.IsTradable,
            eligibility.Status,
            eligibility.Reason,
            totalScore,
            scoreBreakdown,
            indicator.Close,
            indicator.Ma20,
            indicator.Ma60,
            indicator.Ma120,
            indicator.Atr14,
            indicator.RelativeStrengthScore,
            financial?.Pe ?? profile.Pe,
            financial?.Pb ?? profile.Pb,
            financial?.Roe,
            stopLoss,
            targetPrice,
            riskReward,
            BuildExplanation(profile, indicator, industry, regime, scoreBreakdown, riskReward, eligibility.Reason, context));
    }

    /// <summary>
    /// 基于候选股和市场环境生成最终交易信号。
    /// </summary>
    public static TradeSignal? BuildSignal(CandidateStock candidate, MarketRegimeSnapshot regime)
    {
        if (!candidate.IsTradable || candidate.RiskRewardRatio < 1.8m || regime.Regime == MarketSignalEligibility.NoTrade)
        {
            return null;
        }

        var suggestedCapital = regime.Regime switch
        {
            MarketSignalEligibility.Strong => 10_000m,
            MarketSignalEligibility.Tradable => 8_000m,
            MarketSignalEligibility.WeakOpportunity => candidate.StrategyType == StrategyType.PullbackToMa20 ? 8_000m : 0m,
            _ => 0m
        };

        if (suggestedCapital <= 0m)
        {
            return null;
        }

        // A 股以 100 股为一手，仓位建议需要向下取整到整手。
        var estimatedShares = (int)(Math.Floor(suggestedCapital / candidate.Close / 100m) * 100m);
        if (estimatedShares <= 0)
        {
            return null;
        }

        return new TradeSignal(
            candidate.TradeDate,
            candidate.StockCode,
            candidate.StockName,
            candidate.IndustryName,
            candidate.StrategyType,
            candidate.EligibilityStatus,
            candidate.EligibilityReason,
            candidate.TotalScore,
            candidate.ScoreBreakdown,
            candidate.Close,
            candidate.StopLossPrice,
            candidate.TargetPrice,
            candidate.RiskRewardRatio,
            suggestedCapital,
            estimatedShares,
            candidate.Explanation,
            DateTime.UtcNow);
    }

    /// <summary>
    /// 构造四维评分拆解。
    /// </summary>
    private static CandidateScoreBreakdown BuildScoreBreakdown(
        StockProfile profile,
        IndicatorSnapshot indicator,
        FinancialSnapshot? financial,
        IndustryDailyStat? industry,
        CandidateScoringContext? context)
    {
        var resolvedContext = context ?? BuildDefaultContext(indicator, industry);
        var details = new List<ScoreRuleDetail>();
        var rs20ExcessVsIndex = indicator.Return20d - resolvedContext.IndexReturn20d;
        var rs20ExcessVsIndustry = indicator.Return20d - (resolvedContext.IndustryReturn20d ?? 0m);
        var rs60ExcessVsIndex = indicator.Return60d - resolvedContext.IndexReturn60d;

        AddRule(details, "rs20ExcessVsIndex", "相对强弱", "20日超额收益强于指数", 8m, rs20ExcessVsIndex > 0m, $"当前 {rs20ExcessVsIndex:0.##}%");
        AddRule(details, "rs20ExcessVsIndustry", "相对强弱", "20日超额收益强于行业", 7m, rs20ExcessVsIndustry > 0m, $"当前 {rs20ExcessVsIndustry:0.##}%");
        AddRule(details, "rs60ExcessVsIndex", "相对强弱", "60日超额收益强于指数", 7m, rs60ExcessVsIndex > 0m, $"当前 {rs60ExcessVsIndex:0.##}%");
        AddRule(details, "relativeStrengthPercentile", "相对强弱", "市场相对强度分位不低于80", 8m, indicator.RelativeStrengthScore >= 80m, $"当前 {indicator.RelativeStrengthScore:0.##}");

        AddRule(details, "closeAboveMa20", "趋势质量", "收盘站上MA20", 4m, indicator.Close > indicator.Ma20, $"收盘 {indicator.Close:0.##} / MA20 {indicator.Ma20:0.##}");
        AddRule(details, "closeAboveMa60", "趋势质量", "收盘站上MA60", 5m, indicator.Close > indicator.Ma60, $"收盘 {indicator.Close:0.##} / MA60 {indicator.Ma60:0.##}");
        AddRule(details, "closeAboveMa120", "趋势质量", "收盘站上MA120", 2m, indicator.Close > indicator.Ma120, $"收盘 {indicator.Close:0.##} / MA120 {indicator.Ma120:0.##}");
        AddRule(details, "bullishStacked", "趋势质量", "均线多头排列", 6m, indicator.IsBullishStacked, $"MA20 {indicator.Ma20:0.##} / MA60 {indicator.Ma60:0.##} / MA120 {indicator.Ma120:0.##}");
        AddRule(details, "ma20Upward", "趋势质量", "MA20上行", 3m, indicator.IsMa20Upward, $"偏离 {indicator.DistanceToMa20Pct:0.##}%");
        AddRule(details, "ma60Upward", "趋势质量", "MA60上行", 2m, resolvedContext.IsMa60Upward, $"MA60 {indicator.Ma60:0.##}");
        AddRule(details, "breakout20d", "趋势质量", "20日收盘突破", 2m, indicator.Is20DayBreakout, $"收盘 {indicator.Close:0.##}");
        AddRule(details, "distanceToMa20", "趋势质量", "距离MA20不超过10%", 1m, indicator.DistanceToMa20Pct <= 10m, $"当前 {indicator.DistanceToMa20Pct:0.##}%");

        var rawTurnoverRate = profile.TurnoverRate ?? indicator.TurnoverRate ?? 0m;
        var turnoverRate = rawTurnoverRate <= 1m ? rawTurnoverRate * 100m : rawTurnoverRate;
        var atrPct = indicator.Close == 0m ? 0m : indicator.Atr14 / indicator.Close * 100m;
        AddRule(details, "amountRatio1d", "量价确认", "放量确认", 6m, resolvedContext.AmountRatio1d >= 1.5m, $"当前 {resolvedContext.AmountRatio1d:0.##}x");
        AddRule(details, "turnoverHealthy", "量价确认", "换手率健康", 4m, turnoverRate is >= 2m and <= 8m, $"当前 {turnoverRate:0.##}%");
        AddRule(details, "liquidityExcellent", "量价确认", "流动性优秀", 4m, profile.AverageAmount20d >= 1_500_000_000m, $"当前 {(profile.AverageAmount20d / 100_000_000m):0.##} 亿");
        AddRule(details, "liquidityGood", "量价确认", "流动性良好", 2m, profile.AverageAmount20d is >= 500_000_000m and < 1_500_000_000m, $"当前 {(profile.AverageAmount20d / 100_000_000m):0.##} 亿");
        AddRule(details, "amountNotShrinking", "量价确认", "成交额未明显萎缩", 3m, resolvedContext.AmountRatio1d >= 0.8m, $"当前 {resolvedContext.AmountRatio1d:0.##}x");
        AddRule(details, "volumePriceStable", "量价确认", "量价配合稳定", 3m, turnoverRate <= 12m && resolvedContext.Return10d <= 25m, $"换手 {turnoverRate:0.##}% / 10日涨幅 {resolvedContext.Return10d:0.##}%");
        AddRule(details, "industryTop10", "量价确认", "行业强度前10（观察）", 0m, (industry?.Rank20d ?? int.MaxValue) <= 10, $"当前排名 {industry?.Rank20d?.ToString() ?? "无"}");
        AddRule(details, "fundFlow1dPositive", "量价确认", "1日主力净流入为正（观察）", 0m, (resolvedContext.StockFundFlow?.MainNetPct ?? 0m) > 0m, $"当前 {resolvedContext.StockFundFlow?.MainNetPct?.ToString("0.##") ?? "无"}%");
        AddRule(details, "fundFlow5dPercentile", "量价确认", "5日资金流分位较高（观察）", 0m, (resolvedContext.StockFundFlow?.RankPercentile5d ?? 0m) >= 80m, $"当前 {resolvedContext.StockFundFlow?.RankPercentile5d?.ToString("0.##") ?? "无"}");
        AddRule(details, "industryFundFlowPercentile", "量价确认", "行业资金流较强（观察）", 0m, (resolvedContext.IndustryFundFlow?.RankPercentile ?? 0m) >= 80m, $"当前 {resolvedContext.IndustryFundFlow?.RankPercentile?.ToString("0.##") ?? "无"}");
        AddRule(details, "lhbInstitutionNetBuy", "量价确认", "机构净买入（观察）", 0m, resolvedContext.Lhb?.IsInstitutionNetBuy == true, resolvedContext.Lhb?.Reason ?? "无");

        AddRule(details, "riskAtrControlled", "风险纪律", "波动率可控", 2m, atrPct <= 5m, $"ATR/收盘 {atrPct:0.##}%");
        AddRule(details, "riskNotOverheated", "风险纪律", "短期涨幅不过热", 3m, resolvedContext.Return10d <= 20m, $"当前 {resolvedContext.Return10d:0.##}%");
        AddRule(details, "riskNearMa20", "风险纪律", "不明显追高", 2m, indicator.DistanceToMa20Pct <= 8m, $"距离MA20 {indicator.DistanceToMa20Pct:0.##}%");
        AddRule(details, "riskTurnoverNotOverheated", "风险纪律", "换手不过热", 2m, turnoverRate <= 12m, $"当前 {turnoverRate:0.##}%");
        AddRule(details, "riskNormalDistance", "风险纪律", "仍在计划容忍区", 1m, indicator.DistanceToMa20Pct <= 12m, $"距离MA20 {indicator.DistanceToMa20Pct:0.##}%");
        AddPenaltyRule(details, "turnoverOverheated", "风险纪律", "换手过热", 1m, turnoverRate > 12m, $"当前 {turnoverRate:0.##}%");
        AddPenaltyRule(details, "turnoverSpeculative", "风险纪律", "极端换手风险", 1m, turnoverRate > 20m, $"当前 {turnoverRate:0.##}%");
        AddPenaltyRule(details, "lhbRiskFlag", "风险纪律", "龙虎榜风险标签", 1m, !string.IsNullOrWhiteSpace(resolvedContext.Lhb?.RiskFlags), resolvedContext.Lhb?.RiskFlags ?? "无");

        var financialIsUsable = IsFinancialReportUsable(profile.SnapshotDate, financial);
        var scoringFinancial = financialIsUsable ? financial : null;
        AddRule(details, "financialFreshness", "基本面质量", "财报时效有效", 0m, financialIsUsable, financial is null ? "无" : $"报告期 {financial.ReportDate:yyyy-MM-dd}");
        AddRule(details, "roe", "基本面质量", "ROE健康", 4m, (scoringFinancial?.Roe ?? 0m) >= 8m, $"当前 {scoringFinancial?.Roe?.ToString("0.##") ?? "无"}");
        AddRule(details, "revenueYoy", "基本面质量", "营收同比为正", 3m, (scoringFinancial?.RevenueYoy ?? 0m) > 0m, $"当前 {scoringFinancial?.RevenueYoy?.ToString("0.##") ?? "无"}%");
        AddRule(details, "netProfitYoy", "基本面质量", "净利润同比为正", 3m, (scoringFinancial?.NetProfitYoy ?? 0m) > 0m, $"当前 {scoringFinancial?.NetProfitYoy?.ToString("0.##") ?? "无"}%");
        AddRule(details, "cashFlowPositive", "基本面质量", "经营现金流为正", 2m, (scoringFinancial?.OperatingCashFlow ?? scoringFinancial?.OperatingCashFlowNet ?? 0m) > 0m, $"当前 {(scoringFinancial?.OperatingCashFlow ?? scoringFinancial?.OperatingCashFlowNet)?.ToString("0.##") ?? "无"}");
        AddRule(details, "qualityGuard", "基本面质量", "毛利率/负债率无极端异常", 1m, IsFinancialQualityHealthy(scoringFinancial), $"毛利率 {scoringFinancial?.GrossMargin?.ToString("0.##") ?? "无"} / 负债率 {scoringFinancial?.DebtToAssetRatio?.ToString("0.##") ?? "无"}");
        AddRule(details, "valuationGuard", "基本面质量", "估值无极端异常", 2m, (scoringFinancial?.Pe ?? profile.Pe) is > 0m && (scoringFinancial?.Pb ?? profile.Pb) is > 0m and <= 8m, $"PE {(scoringFinancial?.Pe ?? profile.Pe)?.ToString("0.##") ?? "无"} / PB {(scoringFinancial?.Pb ?? profile.Pb)?.ToString("0.##") ?? "无"}");

        var relativeStrength = details.Where(static item => item.Dimension == "相对强弱").Sum(static item => item.Score);
        var trend = details.Where(static item => item.Dimension == "趋势质量").Sum(static item => item.Score);
        var volumePrice = Math.Max(0m, details.Where(static item => item.Dimension == "量价确认").Sum(static item => item.Score));
        var fundamental = details.Where(static item => item.Dimension == "基本面质量").Sum(static item => item.Score);
        var riskDiscipline = Math.Max(0m, details.Where(static item => item.Dimension == "风险纪律").Sum(static item => item.Score));

        var financialMissingCount = 0;
        if (scoringFinancial?.Roe is null) financialMissingCount++;
        if (scoringFinancial?.RevenueYoy is null) financialMissingCount++;
        if (scoringFinancial?.NetProfitYoy is null) financialMissingCount++;
        if (financialMissingCount >= 3)
        {
            fundamental = Math.Min(fundamental, 4m);
        }

        if (!financialIsUsable)
        {
            fundamental = 0m;
        }

        return new CandidateScoreBreakdown(relativeStrength, trend, volumePrice, fundamental, riskDiscipline, details);
    }

    /// <summary>
    /// 根据形态和市场环境判断更适合的策略类型。
    /// </summary>
    private static StrategyType ResolveStrategyType(IndicatorSnapshot indicator, MarketRegimeSnapshot regime)
    {
        if (indicator.Is20DayBreakout)
        {
            return regime.Regime == MarketSignalEligibility.WeakOpportunity
                ? StrategyType.WatchBreakout
                : StrategyType.Breakout;
        }

        var nearMa20 = indicator.DistanceToMa20Pct <= 3m && indicator.Close >= indicator.Ma20 && indicator.Ma20 > indicator.Ma60;
        if (nearMa20)
        {
            return regime.Regime == MarketSignalEligibility.NoTrade
                ? StrategyType.WatchPullback
                : StrategyType.PullbackToMa20;
        }

        return indicator.DistanceToMa20Pct > 10m
            ? StrategyType.WatchPullback
            : StrategyType.WatchBreakout;
    }

    /// <summary>
    /// 将总分映射为候选评级。
    /// </summary>
    private static CandidateGrade ResolveGrade(decimal totalScore)
    {
        if (totalScore >= 90m)
        {
            return CandidateGrade.A;
        }

        if (totalScore >= 88m)
        {
            return CandidateGrade.B;
        }

        return totalScore >= 80m ? CandidateGrade.C : CandidateGrade.D;
    }

    private static CandidateEligibilityDecision ResolveEligibility(
        decimal totalScore,
        StrategyType strategyType,
        MarketRegimeSnapshot regime,
        bool isInTradablePool,
        decimal riskReward)
    {
        if (!isInTradablePool)
        {
            return new CandidateEligibilityDecision(false, "observe_only", "当前账户权限未覆盖该市场，只保留观察资格。");
        }

        if (regime.Regime == MarketSignalEligibility.NoTrade)
        {
            return new CandidateEligibilityDecision(false, "observe_only", "当前市场状态不允许开新仓，只保留观察。");
        }

        if (regime.Regime == MarketSignalEligibility.WeakOpportunity && strategyType == StrategyType.Breakout)
        {
            return new CandidateEligibilityDecision(false, "observe_only", "弱机会市场不执行突破策略，先观察。");
        }

        if (totalScore < StrongWatchMinimumScore)
        {
            return new CandidateEligibilityDecision(false, "study_only", "评分低于 88 分，仅作为学习观察。");
        }

        if (totalScore < TradableMinimumScore)
        {
            return new CandidateEligibilityDecision(false, "strong_watch", "评分达到强观察区间，但还未达到 90 分可执行门槛。");
        }

        if (riskReward < 1.8m)
        {
            return new CandidateEligibilityDecision(false, "observe_only", "预期盈亏比低于 1.8，只保留观察。");
        }

        return new CandidateEligibilityDecision(true, "tradable", "满足当前执行条件。");
    }

    /// <summary>
    /// 添加单条评分规则，并记录证据文本，供前端逐条展示。
    /// </summary>
    private static void AddRule(
        ICollection<ScoreRuleDetail> details,
        string key,
        string dimension,
        string label,
        decimal maxScore,
        bool hit,
        string evidence)
    {
        details.Add(new ScoreRuleDetail(key, dimension, label, hit ? maxScore : 0m, maxScore, hit, evidence));
    }

    private static bool IsFinancialReportUsable(DateOnly tradeDate, FinancialSnapshot? financial)
    {
        if (financial is null)
        {
            return false;
        }

        return financial.ReportDate >= tradeDate.AddDays(-370);
    }

    private static bool IsFinancialQualityHealthy(FinancialSnapshot? financial)
    {
        if (financial is null)
        {
            return false;
        }

        var grossMarginHealthy = financial.GrossMargin is null or > 0m;
        var debtHealthy = financial.DebtToAssetRatio is null or (>= 0m and <= 85m);
        return grossMarginHealthy && debtHealthy;
    }

    private static void AddPenaltyRule(
        ICollection<ScoreRuleDetail> details,
        string key,
        string dimension,
        string label,
        decimal penaltyScore,
        bool hit,
        string evidence)
    {
        details.Add(new ScoreRuleDetail(key, dimension, label, hit ? -penaltyScore : 0m, penaltyScore, hit, evidence));
    }

    /// <summary>
    /// 计算止损价。
    /// </summary>
    private static decimal ResolveStopLoss(decimal close, decimal ma20, StrategyType strategyType)
    {
        // 同时比较技术止损与资金止损，取更保守的一侧，避免止损过近被噪音扫掉。
        var technicalStop = strategyType == StrategyType.PullbackToMa20
            ? ma20 * 0.98m
            : close * 0.97m;
        var capitalStop = close - 0.04m * close;
        return Math.Round(Math.Max(technicalStop, capitalStop), 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// 计算目标价。
    /// </summary>
    private static decimal ResolveTargetPrice(decimal close, StrategyType strategyType)
    {
        var multiplier = strategyType == StrategyType.PullbackToMa20 ? 1.10m : 1.12m;
        return Math.Round(close * multiplier, 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// 计算风险收益比。
    /// </summary>
    private static decimal CalculateRiskReward(decimal close, decimal stopLoss, decimal targetPrice)
    {
        var risk = close - stopLoss;
        if (risk <= 0m)
        {
            return 0m;
        }

        return Math.Round((targetPrice - close) / risk, 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// 生成候选股解释文案。
    /// </summary>
    private static string BuildExplanation(
        StockProfile profile,
        IndicatorSnapshot indicator,
        IndustryDailyStat? industry,
        MarketRegimeSnapshot regime,
        CandidateScoreBreakdown breakdown,
        decimal riskReward,
        string eligibilityReason,
        CandidateScoringContext context)
    {
        var strengths = new List<string>();
        var risks = new List<string>();

        if (indicator.RelativeStrengthScore >= 80m)
        {
            strengths.Add($"相对强度分位 {indicator.RelativeStrengthScore:0.#}");
        }

        if (indicator.IsBullishStacked)
        {
            strengths.Add("均线多头排列");
        }

        if (indicator.IsMa20Upward)
        {
            strengths.Add("MA20 保持上行");
        }

        if (indicator.Is20DayBreakout)
        {
            strengths.Add("出现 20 日收盘突破");
        }

        if ((industry?.Rank20d ?? int.MaxValue) <= 10)
        {
            strengths.Add($"行业强度排名前 {industry!.Rank20d}");
        }

        if ((context.AmountRatio1d) >= 1.5m)
        {
            strengths.Add($"放量系数 {context.AmountRatio1d:0.##}x");
        }

        if ((context.StockFundFlow?.MainNetPct ?? 0m) > 0m)
        {
            strengths.Add($"主力净流入 {context.StockFundFlow!.MainNetPct:0.##}%");
        }

        if (context.Lhb?.IsInstitutionNetBuy == true)
        {
            strengths.Add("龙虎榜机构净买入");
        }

        if (indicator.DistanceToMa20Pct > 10m)
        {
            risks.Add($"距离 MA20 偏离 {indicator.DistanceToMa20Pct:0.##}%");
        }

        if ((context.Return10d) > 25m)
        {
            risks.Add($"近 10 日涨幅偏热 {context.Return10d:0.##}%");
        }

        if ((context.StockFundFlow?.MainNetPct ?? 0m) < 0m)
        {
            risks.Add($"主力净流出 {context.StockFundFlow!.MainNetPct:0.##}%");
        }

        if (!string.IsNullOrWhiteSpace(context.Lhb?.RiskFlags))
        {
            risks.Add($"龙虎榜风险标签：{context.Lhb.RiskFlags}");
        }

        if (riskReward < 1.8m)
        {
            risks.Add($"风险收益比仅 {riskReward:0.##}");
        }

        var summary = $"{profile.StockName} 当前总分 {breakdown.TotalScore:0.#} 分，处于{FormatMarketRegime(regime.Regime)}环境，现阶段结论：{eligibilityReason}";
        var trendLine = $"趋势上，收盘 {indicator.Close:0.##}，MA20 {indicator.Ma20:0.##}，MA60 {indicator.Ma60:0.##}，风险收益比 {riskReward:0.##}。";
        var strengthLine = strengths.Count > 0
            ? $"主要加分项：{string.Join("、", strengths.Take(4))}。"
            : "主要加分项：当前没有特别突出的强确认信号。";
        var riskLine = risks.Count > 0
            ? $"需要留意：{string.Join("、", risks.Take(3))}。"
            : "需要留意：当前没有明显的额外风险标签，重点仍是按计划执行。";

        return $"{summary}{trendLine}{strengthLine}{riskLine}";
    }

    private static CandidateScoringContext BuildDefaultContext(IndicatorSnapshot indicator, IndustryDailyStat? industry)
        => new(
            0m,
            0m,
            0m,
            industry?.PctChange20d,
            false,
            0m,
            null,
            null,
            null);

    /// <summary>
    /// 将市场环境枚举转成中文说明。
    /// </summary>
    private static string FormatMarketRegime(MarketSignalEligibility regime)
    {
        return regime switch
        {
            MarketSignalEligibility.Strong => "强势",
            MarketSignalEligibility.Tradable => "可交易",
            MarketSignalEligibility.WeakOpportunity => "弱机会",
            _ => "不交易"
        };
    }

    private sealed record CandidateEligibilityDecision(bool IsTradable, string Status, string Reason);
}

public sealed record CandidateListPreview(
    DateOnly TradeDate,
    string StockCode,
    string StockName,
    string? IndustryName,
    CandidateGrade Grade,
    StrategyType StrategyType,
    bool IsTradable,
    string EligibilityStatus,
    string EligibilityReason,
    decimal TotalScore,
    CandidateScoreBreakdown ScoreBreakdown,
    decimal Close,
    decimal Ma20,
    decimal Ma60,
    decimal Atr14,
    decimal RelativeStrengthScore,
    decimal StopLossPrice,
    decimal TargetPrice,
    decimal RiskRewardRatio,
    string Explanation);

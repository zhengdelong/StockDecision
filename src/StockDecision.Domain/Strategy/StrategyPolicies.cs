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

/// <summary>
/// 封装候选股与交易信号的业务规则。
/// </summary>
public static class CandidatePolicy
{
    private const decimal CandidatePoolMinimumScore = 60m;
    private const decimal TradableMinimumScore = 60m;

    /// <summary>
    /// 基于当前画像、指标和财务快照重建评分拆解，供接口按最新规则解释得分过程。
    /// </summary>
    public static CandidateScoreBreakdown DescribeScoreBreakdown(
        StockProfile profile,
        IndicatorSnapshot indicator,
        FinancialSnapshot? financial,
        IndustryDailyStat? industry)
    {
        return BuildScoreBreakdown(profile, indicator, financial, industry);
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
        MarketRegimeSnapshot regime)
    {
        if (!profile.IsActive || profile.IsSt || profile.IsDelistingRisk)
        {
            return null;
        }

        // 新股历史样本不足且波动特征不稳定，直接排除。
        if (profile.ListDate is DateOnly listDate && listDate > tradeDate.AddDays(-365))
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

        var scoreBreakdown = BuildScoreBreakdown(profile, indicator, financial, industry);
        var totalScore = scoreBreakdown.TotalScore;
        if (totalScore < CandidatePoolMinimumScore)
        {
            return null;
        }

        var strategyType = ResolveStrategyType(indicator, regime);
        var isTradable = totalScore >= TradableMinimumScore &&
            strategyType is StrategyType.Breakout or StrategyType.PullbackToMa20 &&
            regime.Regime != MarketSignalEligibility.NoTrade;

        var stopLoss = ResolveStopLoss(indicator.Close, indicator.Ma20, strategyType);
        var targetPrice = ResolveTargetPrice(indicator.Close, strategyType);
        var riskReward = CalculateRiskReward(indicator.Close, stopLoss, targetPrice);
        var grade = ResolveGrade(totalScore);

        return new CandidateStock(
            tradeDate,
            profile.StockCode,
            profile.StockName,
            profile.IndustryName,
            grade,
            strategyType,
            isTradable,
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
            BuildExplanation(profile, indicator, industry, regime, scoreBreakdown, riskReward, isTradable));
    }

    /// <summary>
    /// 基于候选股和市场环境生成最终交易信号。
    /// </summary>
    public static TradeSignal? BuildSignal(CandidateStock candidate, MarketRegimeSnapshot regime)
    {
        if (!candidate.IsTradable || candidate.RiskRewardRatio < 2m || regime.Regime == MarketSignalEligibility.NoTrade)
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
        IndustryDailyStat? industry)
    {
        var details = new List<ScoreRuleDetail>();

        AddRule(details, "return20d", "相对强弱", "20日收益率为正", 10m, indicator.Return20d > 0m, $"当前 {indicator.Return20d:0.##}%");
        AddRule(details, "return60d", "相对强弱", "60日收益率为正", 8m, indicator.Return60d > 0m, $"当前 {indicator.Return60d:0.##}%");
        AddRule(details, "relativeStrength", "相对强弱", "相对强弱得分为正", 7m, indicator.RelativeStrengthScore > 0m, $"当前 {indicator.RelativeStrengthScore:0.##}");
        AddRule(details, "distanceToMa20Rs", "相对强弱", "距MA20不超过10%", 5m, indicator.DistanceToMa20Pct <= 10m, $"当前 {indicator.DistanceToMa20Pct:0.##}%");

        AddRule(details, "closeAboveMa60", "趋势", "收盘价站上MA60", 6m, indicator.Close > indicator.Ma60, $"收盘 {indicator.Close:0.##} / MA60 {indicator.Ma60:0.##}");
        AddRule(details, "bullishStacked", "趋势", "均线多头排列", 8m, indicator.IsBullishStacked, $"MA20 {indicator.Ma20:0.##} / MA60 {indicator.Ma60:0.##} / MA120 {indicator.Ma120:0.##}");
        AddRule(details, "closeAboveMa20", "趋势", "收盘价站上MA20", 4m, indicator.Close > indicator.Ma20, $"收盘 {indicator.Close:0.##} / MA20 {indicator.Ma20:0.##}");
        AddRule(details, "ma20Upward", "趋势", "MA20保持上行", 4m, indicator.IsMa20Upward, $"距离MA20 {indicator.DistanceToMa20Pct:0.##}%");
        AddRule(details, "distanceToMa20Trend", "趋势", "距MA20不超过10%", 3m, indicator.DistanceToMa20Pct <= 10m, $"当前 {indicator.DistanceToMa20Pct:0.##}%");

        var turnoverRate = profile.TurnoverRate ?? indicator.TurnoverRate ?? 0m;
        AddRule(details, "breakout20d", "量价", "创出20日收盘突破", 7m, indicator.Is20DayBreakout, $"收盘 {indicator.Close:0.##}");
        AddRule(details, "turnoverRange", "量价", "换手率在2%-8%", 4m, turnoverRate is >= 2m and <= 8m, $"当前 {turnoverRate:0.##}%");
        AddRule(details, "averageAmount20d", "量价", "20日均成交额达标", profile.AverageAmount20d is >= 500_000_000m ? 4m : 2m, true, $"当前 {(profile.AverageAmount20d / 100_000_000m):0.##} 亿");
        AddRule(details, "industryRank", "量价", "行业20日强度排名前10", 5m, (industry?.Rank20d ?? int.MaxValue) <= 10, $"当前排名 {industry?.Rank20d?.ToString() ?? "无"}");

        AddRule(details, "netProfitYoy", "基本面", "净利润同比为正", 5m, (financial?.NetProfitYoy ?? 0m) > 0m, $"当前 {financial?.NetProfitYoy?.ToString("0.##") ?? "无"}%");
        AddRule(details, "revenueYoy", "基本面", "营收同比为正", 4m, (financial?.RevenueYoy ?? 0m) > 0m, $"当前 {financial?.RevenueYoy?.ToString("0.##") ?? "无"}%");
        AddRule(details, "roe", "基本面", "ROE 不低于 8", 5m, (financial?.Roe ?? 0m) >= 8m, $"当前 {financial?.Roe?.ToString("0.##") ?? "无"}");
        AddRule(details, "pe", "基本面", "PE 位于 0-80", 3m, (financial?.Pe ?? profile.Pe) is > 0m and <= 80m, $"当前 {(financial?.Pe ?? profile.Pe)?.ToString("0.##") ?? "无"}");
        AddRule(details, "pb", "基本面", "PB 位于 0-8", 3m, (financial?.Pb ?? profile.Pb) is > 0m and <= 8m, $"当前 {(financial?.Pb ?? profile.Pb)?.ToString("0.##") ?? "无"}");

        var relativeStrength = details.Where(static item => item.Dimension == "相对强弱").Sum(static item => item.Score);
        var trend = details.Where(static item => item.Dimension == "趋势").Sum(static item => item.Score);
        var volumePrice = details.Where(static item => item.Dimension == "量价").Sum(static item => item.Score);
        var fundamental = details.Where(static item => item.Dimension == "基本面").Sum(static item => item.Score);

        return new CandidateScoreBreakdown(relativeStrength, trend, volumePrice, fundamental, details);
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
        if (totalScore >= 60m)
        {
            return CandidateGrade.A;
        }

        if (totalScore >= 55m)
        {
            return CandidateGrade.B;
        }

        return CandidateGrade.C;
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
        bool isTradable)
    {
        return
            $"{profile.StockName} 当前总分 {breakdown.TotalScore:0.#} 分。"
            + $"市场环境：{FormatMarketRegime(regime.Regime)}。"
            + $"行业强度排名：{industry?.Rank20d?.ToString() ?? "暂无"}。"
            + $"收盘价 {indicator.Close:0.##}，MA20 {indicator.Ma20:0.##}，MA60 {indicator.Ma60:0.##}。"
            + $"风险收益比 {riskReward:0.##}。"
            + (isTradable ? "当前满足可执行条件。" : "当前仅建议观察。");
    }

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
}

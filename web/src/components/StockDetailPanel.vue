<script setup lang="ts">
import { computed, defineAsyncComponent } from 'vue'
import type { ScoreRuleDetail, StockDetailResponse, TradeExecutionPlan } from '../types'
import { formatNumber, formatPercent } from '../utils/formatters'
import {
  buildChartStats,
  buildScoreRows,
  buildSimpleMovingAverageSeries,
  resolveDistanceLabel,
} from '../utils/stockChart'

interface GroupedScoreDetail {
  dimension: string
  items: ScoreRuleDetail[]
  totalScore: number
  totalMaxScore: number
  hitCount: number
}

const StockPriceChart = defineAsyncComponent(() => import('./StockPriceChart.vue'))

/**
 * 个股详情面板，承载价格走势、成交量、评分拆解和执行计划。
 */
const props = defineProps<{
  isDetailLoading: boolean
  isLoading: boolean
  stockDetail: StockDetailResponse | null
}>()

const recentBarsPreview = computed(() => props.stockDetail?.recentBars.slice(-5).reverse() ?? [])
const headlineScore = computed(() => props.stockDetail?.candidate?.totalScore ?? props.stockDetail?.signal?.totalScore ?? null)
const hasDifferentScoringIndustry = computed(() => {
  const detail = props.stockDetail
  return !!detail?.scoringIndustryName && detail.scoringIndustryName !== detail.industryName
})

function formatScoreCell(detail: ScoreRuleDetail): string {
  if (detail.maxScore === 0) {
    return detail.hit ? '有效' : '失效'
  }

  return `${formatNumber(detail.score, 0)} / ${formatNumber(detail.maxScore, 0)}`
}

function formatHitCell(detail: ScoreRuleDetail): string {
  if (detail.maxScore === 0) {
    return detail.hit ? '有效' : '失效'
  }

  return detail.hit ? '是' : '否'
}

const latestChangePct = computed(() => {
  const bars = props.stockDetail?.recentBars ?? []
  if (bars.length < 2) {
    return null
  }

  const latest = bars[bars.length - 1]?.close ?? 0
  const previous = bars[bars.length - 2]?.close ?? 0
  if (!previous) {
    return null
  }

  return ((latest - previous) / previous) * 100
})

/**
 * 详情图里的均线全部按历史收盘价连续计算，保证线条完整。
 */
const ma10Series = computed(() => buildSimpleMovingAverageSeries(props.stockDetail?.recentBars ?? [], 10))
const ma20Series = computed(() => buildSimpleMovingAverageSeries(props.stockDetail?.recentBars ?? [], 20))
const ma60Series = computed(() => buildSimpleMovingAverageSeries(props.stockDetail?.recentBars ?? [], 60))

const chartStats = computed(() =>
  buildChartStats(props.stockDetail?.recentBars ?? [], [
    ma10Series.value,
    ma20Series.value,
    ma60Series.value,
  ]),
)

const latestMa10 = computed(() => {
  const value = ma10Series.value[ma10Series.value.length - 1]
  return typeof value === 'number' ? value : null
})

const latestMa20 = computed(() => {
  const value = ma20Series.value[ma20Series.value.length - 1]
  return typeof value === 'number' ? value : props.stockDetail?.indicator?.ma20 ?? null
})

const latestMa60 = computed(() => {
  const value = ma60Series.value[ma60Series.value.length - 1]
  return typeof value === 'number' ? value : props.stockDetail?.indicator?.ma60 ?? null
})

const groupedScoreDetails = computed<GroupedScoreDetail[]>(() => {
  const details = props.stockDetail?.candidate?.scoreDetails ?? []
  const groups = new Map<string, GroupedScoreDetail>()

  details.forEach((detail) => {
    const existing = groups.get(detail.dimension)
    if (existing) {
      existing.items.push(detail)
      existing.totalScore += detail.score
      existing.totalMaxScore += detail.maxScore
      existing.hitCount += detail.hit ? 1 : 0
      return
    }

    groups.set(detail.dimension, {
      dimension: detail.dimension,
      items: [detail],
      totalScore: detail.score,
      totalMaxScore: detail.maxScore,
      hitCount: detail.hit ? 1 : 0,
    })
  })

  return Array.from(groups.values())
})

const explanationHighlights = computed(() => {
  const candidate = props.stockDetail?.candidate
  const signal = props.stockDetail?.signal
  const latestBar = props.stockDetail?.latestBar

  if (!candidate || !latestBar) {
    return []
  }

  return [
    `总分 ${formatNumber(candidate.totalScore, 1)}`,
    `策略 ${candidate.strategyType}`,
    `收盘价 ${formatNumber(latestBar.close)}`,
    `MA20 ${formatNumber(latestMa20.value)}`,
    `MA60 ${formatNumber(latestMa60.value)}`,
    `成交量 ${formatNumber(latestBar.volume / 10000, 2)} 万股`,
    `成交额 ${formatNumber(latestBar.amount / 100000000, 2)} 亿`,
    signal ? `风险收益比 ${formatNumber(signal.riskRewardRatio)}` : null,
    signal ? `止损价 ${formatNumber(signal.stopLossPrice)}` : null,
    signal ? `目标价 ${formatNumber(signal.targetPrice)}` : null,
  ].filter((item): item is string => Boolean(item))
})

const decisionSummaryCards = computed(() => {
  const detail = props.stockDetail
  if (!detail) {
    return []
  }

  const candidate = detail.candidate
  const signal = detail.signal
  const plan = resolvedExecutionPlan.value
  const cards = []

  cards.push({
    key: 'score',
    tone: candidate?.totalScore != null && candidate.totalScore >= 90 ? 'reward' : 'neutral',
    label: '当前结论',
    value: signal ? '可执行信号' : candidate ? candidate.eligibilityStatus : '仅详情观察',
    detail: candidate ? `${candidate.strategyType} · 总分 ${formatNumber(candidate.totalScore, 1)}` : '暂无候选评分结果',
  })

  cards.push({
    key: 'trend',
    tone: detail.indicator?.isBullishStacked ? 'reward' : 'neutral',
    label: '趋势状态',
    value: detail.indicator?.isBullishStacked ? '多头趋势' : '趋势待确认',
    detail: detail.indicator
      ? `MA20 ${detail.indicator.isMa20Upward ? '上行' : '走平'} · 20日收益 ${formatPercent(detail.indicator.return20d)}`
      : '暂无指标快照',
  })

  cards.push({
    key: 'capital',
    tone: signal ? 'reward' : plan ? 'neutral' : 'risk',
    label: '执行动作',
    value: signal ? '按计划跟踪' : plan ? '等待触发' : '暂无计划',
    detail: plan
      ? `触发 ${formatNumber(plan.triggerPrice)} · 止损 ${formatNumber(plan.stopLossPrice)} · 目标 ${formatNumber(plan.targetPrice)}`
      : '当前没有生成执行计划',
  })

  return cards
})

const fundFlowInsight = computed(() => {
  const fundFlow = props.stockDetail?.fundFlow
  if (!fundFlow) {
    return null
  }

  const stockPositive = (fundFlow.mainNetPct ?? 0) > 0
  const industryPositive = (fundFlow.industryMainNetPct ?? 0) > 0
  const percentile = fundFlow.rankPercentile5d ?? 0
  const industryPercentile = fundFlow.industryRankPercentile ?? 0

  if (stockPositive && industryPositive && percentile >= 80 && industryPercentile >= 80) {
    return '资金确认较强：个股与所属行业都处于净流入状态，且分位靠前，说明这波上涨不只是单点异动。'
  }

  if (stockPositive && industryPositive) {
    return '资金确认偏正面：个股和行业资金都没有拖后腿，可以把它看成趋势延续的辅助证据。'
  }

  if (!stockPositive && industryPositive) {
    return '行业资金偏强，但个股主力资金跟进还不够，说明板块热度在，个股承接还要继续观察。'
  }

  if (stockPositive && !industryPositive) {
    return '个股有资金承接，但行业资金没有同步转强，更像局部博弈，持续性要打折。'
  }

  return '资金确认偏弱：个股和行业都没有给出明显净流入信号，当前更适合当作评分参考，而不是独立买点理由。'
})

const lhbInsight = computed(() => {
  const lhb = props.stockDetail?.lhb
  if (!lhb) {
    return null
  }

  if (lhb.isOnLhbToday && lhb.isInstitutionNetBuy) {
    return '龙虎榜偏正面：今天上榜且机构净买入为正，说明短线关注度高，同时有一定机构参与度。'
  }

  if (lhb.isOnLhbToday && lhb.riskFlags) {
    return `龙虎榜有分歧：虽然今天上榜，但伴随风险标签 ${lhb.riskFlags}，需要防止情绪过热后的大波动。`
  }

  if (lhb.recent20dLhbCount >= 2) {
    return '近期多次上榜，说明这只票处在资金反复博弈区，弹性通常更高，但波动也更大。'
  }

  return '龙虎榜没有给出太强的额外确认信号，可以把它视为中性信息。'
})

const executionChecklist = computed(() => {
  const plan = resolvedExecutionPlan.value
  const scenario = executionScenario.value
  if (!plan) {
    return []
  }

  return [
    {
      key: 'trigger',
      tone: 'neutral',
      label: '买入前',
      value: `等价格接近 ${formatNumber(plan.triggerPrice)}`,
      detail: `若高开超过 ${formatPercent(plan.maxEntryGapPct)} 或未满足计划中的买入规则，先不追。`,
    },
    {
      key: 'risk',
      tone: scenario && scenario.riskPct >= 5 ? 'risk' : 'neutral',
      label: '止损纪律',
      value: `失守 ${formatNumber(plan.stopLossPrice)} 就退出`,
      detail: scenario
        ? `按建议股数估算，单笔最大亏损约 ${formatNumber(scenario.riskAmount, 0)}。`
        : '优先遵守固定止损，不用主观扛单。',
    },
    {
      key: 'profit',
      tone: plan.riskRewardRatio >= 2 ? 'reward' : 'neutral',
      label: '止盈预期',
      value: `先看 ${formatNumber(plan.targetPrice)}`,
      detail: `当前风险收益比 ${formatNumber(plan.riskRewardRatio)}，持仓最长 ${plan.maxHoldingDays} 天。`,
    },
  ]
})

/**
 * 用最近一根 K 线与近几日均量生成直观解读，降低用户读图门槛。
 */
const priceVolumeInsight = computed(() => {
  const bars = props.stockDetail?.recentBars ?? []
  if (bars.length < 2) {
    return null
  }

  const latest = bars[bars.length - 1]
  const previous = bars[bars.length - 2]
  if (!latest || !previous || previous.close <= 0) {
    return null
  }

  const referenceBars = bars.slice(Math.max(0, bars.length - 6), bars.length - 1)
  const averageVolume =
    referenceBars.length > 0 ? referenceBars.reduce((sum, item) => sum + item.volume, 0) / referenceBars.length : latest.volume
  const volumeRatio = averageVolume > 0 ? latest.volume / averageVolume : 1
  const priceChangePct = ((latest.close - previous.close) / previous.close) * 100
  const ma20 = latestMa20.value
  const ma60 = latestMa60.value
  const aboveMa20 = typeof ma20 === 'number' ? latest.close >= ma20 : false
  const aboveMa60 = typeof ma60 === 'number' ? latest.close >= ma60 : false
  const volumeText = `量比近 ${referenceBars.length || 1} 日均量 ${formatNumber(volumeRatio, 2)} 倍`

  if (priceChangePct >= 1.5 && volumeRatio >= 1.4) {
    return `量价联动：放量走强，${volumeText}。若后续仍能站稳 MA20${props.stockDetail?.signal ? '与触发价' : ''}，趋势有继续扩散的基础。`
  }

  if (priceChangePct <= -1.5 && volumeRatio >= 1.4) {
    return `量价联动：放量下跌，${volumeText}。这通常意味着抛压集中释放，短线先看止损纪律，不宜只凭反弹预期加仓。`
  }

  if (Math.abs(priceChangePct) <= 1 && volumeRatio <= 0.85 && aboveMa20) {
    return `量价联动：缩量整理，${volumeText}。价格仍在 MA20 之上，更多像强势股的消化震荡，等待下一次放量确认。`
  }

  if (priceChangePct > 0 && volumeRatio >= 1 && aboveMa20 && aboveMa60) {
    return `量价联动：温和放量上行，${volumeText}。中短期均线仍在价格下方，趋势延续性尚可，但更适合等突破确认而不是盲目追高。`
  }

  return `量价联动：量能一般，${volumeText}。价格还在整理阶段，先观察是否重新站稳关键均线${props.stockDetail?.signal ? '并接近触发价' : ''}。`
})

/**
 * 把执行计划转成更直观的仓位推演，方便非专业用户理解一笔交易要承担的风险。
 */
const executionScenario = computed(() => {
  const plan = resolvedExecutionPlan.value
  if (!plan || plan.triggerPrice <= 0) {
    return null
  }

  const shares = Math.max(plan.estimatedShares ?? 0, 0)
  const riskPerShare = Math.max(plan.triggerPrice - plan.stopLossPrice, 0)
  const rewardPerShare = Math.max(plan.targetPrice - plan.triggerPrice, 0)
  const estimatedCost = plan.triggerPrice * shares
  const riskAmount = riskPerShare * shares
  const rewardAmount = rewardPerShare * shares
  const riskPct = plan.triggerPrice > 0 ? (riskPerShare / plan.triggerPrice) * 100 : 0
  const rewardPct = plan.triggerPrice > 0 ? (rewardPerShare / plan.triggerPrice) * 100 : 0
  const capitalUsagePct = (plan.suggestedCapital ?? 0) > 0 ? (estimatedCost / (plan.suggestedCapital ?? 0)) * 100 : 0

  let judgement = '仓位与止损设置基本可执行，重点看触发后是否有持续放量。'
  if (riskPct >= 5) {
    judgement = '单笔止损空间偏大，若要参与，最好降低仓位，避免一笔交易吃掉过多回撤。'
  } else if (plan.riskRewardRatio < 2) {
    judgement = '风险收益比偏弱，除非你对趋势延续有更强把握，否则不算理想出手点。'
  } else if (capitalUsagePct > 105) {
    judgement = '按当前触发价估算，建议股数略高于建议资金，执行时需要向下取整控制仓位。'
  } else if (rewardPct >= 10 && riskPct <= 3) {
    judgement = '盈亏比与回撤控制都比较友好，属于更标准的趋势跟随型计划。'
  }

  return {
    riskPerShare,
    rewardPerShare,
    estimatedCost,
    riskAmount,
    rewardAmount,
    riskPct,
    rewardPct,
    capitalUsagePct,
    judgement,
  }
})

const resolvedExecutionPlan = computed<TradeExecutionPlan | null>(() => {
  return props.stockDetail?.signal?.executionPlan ?? props.stockDetail?.candidate?.executionPlan ?? null
})

const executionSections = computed(() => {
  const plan = resolvedExecutionPlan.value
  if (!plan) {
    return []
  }

  return [
    { key: 'entry', title: '买入规则', items: plan.entryRules },
    { key: 'hold', title: '持有规则', items: plan.holdRules },
    { key: 'exit', title: '卖出规则', items: plan.exitRules },
    { key: 'invalidate', title: '失效条件', items: plan.invalidationRules },
  ]
})

</script>

<template>
  <aside class="detail-panel">
    <div v-if="props.isDetailLoading" class="detail-loading">正在加载个股详情...</div>

    <template v-else-if="props.stockDetail">
      <header class="detail-header">
        <div class="detail-header-main">
          <p class="eyebrow detail-code">{{ props.stockDetail.stockCode }}</p>
          <div class="detail-title-row">
            <h2>{{ props.stockDetail.stockName }}</h2>
            <div v-if="headlineScore != null" class="detail-score-badge">
              <span>总分</span>
              <strong>{{ formatNumber(headlineScore, 1) }}</strong>
            </div>
          </div>
          <div class="detail-industry-lines">
            <p class="muted">展示行业：{{ props.stockDetail.industryName ?? '未知行业' }}</p>
            <p v-if="hasDifferentScoringIndustry" class="muted">评分行业：{{ props.stockDetail.scoringIndustryName }}</p>
          </div>
        </div>
        <div class="detail-badges">
          <span class="pill neutral">{{ props.stockDetail.tradeDate }}</span>
          <span class="pill neutral">{{ props.stockDetail.snapshotVersionName }}</span>
          <span class="pill soft">{{ resolveDistanceLabel(props.stockDetail) }}</span>
        </div>
      </header>

      <section v-if="decisionSummaryCards.length" class="detail-block">
        <div class="panel-head">
          <div>
            <p class="card-label">决策摘要</p>
            <h3>先看结论，再看细节</h3>
          </div>
        </div>
        <div class="decision-grid">
          <article v-for="card in decisionSummaryCards" :key="card.key" :class="['decision-card', card.tone]">
            <span>{{ card.label }}</span>
            <strong>{{ card.value }}</strong>
            <small>{{ card.detail }}</small>
          </article>
        </div>
      </section>

      <section class="detail-block chart-block">
        <div class="panel-head">
          <div>
            <p class="card-label">价格走势</p>
            <h3>收盘价、均线、成交量与 MACD</h3>
          </div>
          <div v-if="chartStats" class="chart-summary">
            <span>区间 {{ formatNumber(chartStats.min) }} - {{ formatNumber(chartStats.max) }}</span>
            <strong :class="latestChangePct != null && latestChangePct >= 0 ? 'up' : 'down'">
              {{ latestChangePct == null ? '暂无数据' : formatPercent(latestChangePct) }}
            </strong>
          </div>
        </div>

        <div class="chart-metrics">
          <div class="chart-metric">
            <span>收盘价</span>
            <strong>{{ formatNumber(props.stockDetail.latestBar.close) }}</strong>
          </div>
          <div class="chart-metric">
            <span>MA10</span>
            <strong>{{ formatNumber(latestMa10) }}</strong>
          </div>
          <div class="chart-metric">
            <span>MA20</span>
            <strong>{{ formatNumber(latestMa20) }}</strong>
          </div>
          <div class="chart-metric">
            <span>MA60</span>
            <strong>{{ formatNumber(latestMa60) }}</strong>
          </div>
        </div>

        <div v-if="priceVolumeInsight" class="insight-banner">
          <strong>量价解读</strong>
          <span>{{ priceVolumeInsight }}</span>
        </div>

        <div v-if="props.stockDetail.recentBars.length" class="svg-shell">
          <StockPriceChart :stock-detail="props.stockDetail" />
          <div class="chart-footnote">
            <span>Red = up / positive, Green = down / negative</span>
            <span>横轴：交易日</span>
            <span>上图为价格与均线，中图为成交量，下图为 MACD</span>
          </div>
        </div>
        <div v-else class="empty-mini">K 线数量不足，暂时无法绘图。</div>
      </section>

      <section class="detail-block">
        <div class="panel-head">
          <div>
            <p class="card-label">价格快照</p>
            <h3>最新日线</h3>
          </div>
        </div>
        <div class="detail-grid">
          <div><span>开盘价</span><strong>{{ formatNumber(props.stockDetail.latestBar.open) }}</strong></div>
          <div><span>收盘价</span><strong>{{ formatNumber(props.stockDetail.latestBar.close) }}</strong></div>
          <div><span>最高价</span><strong>{{ formatNumber(props.stockDetail.latestBar.high) }}</strong></div>
          <div><span>最低价</span><strong>{{ formatNumber(props.stockDetail.latestBar.low) }}</strong></div>
          <div><span>成交量</span><strong>{{ formatNumber(props.stockDetail.latestBar.volume / 10000, 2) }} 万股</strong></div>
          <div><span>成交额</span><strong>{{ formatNumber(props.stockDetail.latestBar.amount / 100000000, 2) }} 亿</strong></div>
        </div>
      </section>

      <section v-if="props.stockDetail.indicator" class="detail-block">
        <div class="panel-head">
          <div>
            <p class="card-label">指标快照</p>
            <h3>趋势与波动</h3>
          </div>
        </div>
        <div class="detail-grid">
          <div><span>MA10</span><strong>{{ formatNumber(latestMa10) }}</strong></div>
          <div><span>MA20</span><strong>{{ formatNumber(latestMa20) }}</strong></div>
          <div><span>MA60</span><strong>{{ formatNumber(latestMa60) }}</strong></div>
          <div><span>MA120</span><strong>{{ formatNumber(props.stockDetail.indicator.ma120) }}</strong></div>
          <div><span>ATR14</span><strong>{{ formatNumber(props.stockDetail.indicator.atr14) }}</strong></div>
          <div><span>20日收益率</span><strong>{{ formatPercent(props.stockDetail.indicator.return20d) }}</strong></div>
          <div><span>60日收益率</span><strong>{{ formatPercent(props.stockDetail.indicator.return60d) }}</strong></div>
          <div><span>相对强弱分</span><strong>{{ formatNumber(props.stockDetail.indicator.relativeStrengthScore, 1) }}</strong></div>
          <div><span>距 MA20 偏离</span><strong>{{ formatPercent(props.stockDetail.indicator.distanceToMa20Pct) }}</strong></div>
        </div>
        <div class="flag-row">
          <span :class="['pill', props.stockDetail.indicator.is20DayBreakout ? 'positive' : 'neutral']">20日突破</span>
          <span :class="['pill', props.stockDetail.indicator.isMa20Upward ? 'positive' : 'neutral']">MA20 上行</span>
          <span :class="['pill', props.stockDetail.indicator.isBullishStacked ? 'positive' : 'neutral']">多头排列</span>
        </div>
      </section>

      <section v-if="props.stockDetail.candidate" class="detail-block">
        <div class="panel-head">
          <div>
            <p class="card-label">候选评分</p>
            <h3>维度得分拆解</h3>
          </div>
          <span class="score-total">{{ formatNumber(props.stockDetail.candidate.totalScore, 1) }}</span>
        </div>

        <div class="score-stack">
          <div v-for="row in buildScoreRows(props.stockDetail.candidate.scoreBreakdown)" :key="row.key" class="score-row">
            <div class="score-meta">
              <span>{{ row.label }}</span>
              <strong>{{ formatNumber(row.value, 1) }} / {{ row.max }}</strong>
            </div>
            <div class="score-track">
              <div class="score-fill" :style="{ width: `${Math.min((row.value / row.max) * 100, 100)}%` }" />
            </div>
          </div>
        </div>

        <div v-if="groupedScoreDetails.length" class="score-groups">
          <details v-for="group in groupedScoreDetails" :key="group.dimension" class="score-group-card" open>
            <summary class="score-group-summary">
              <div>
                <strong>{{ group.dimension }}</strong>
                <span>{{ group.hitCount }} / {{ group.items.length }} 条规则命中</span>
              </div>
              <b>{{ formatNumber(group.totalScore, 0) }} / {{ formatNumber(group.totalMaxScore, 0) }}</b>
            </summary>

            <div class="table-shell detail-table-shell grouped-detail-table">
              <table class="data-table detail-data-table">
                <thead>
                  <tr>
                    <th>规则</th>
                    <th>得分</th>
                    <th>命中</th>
                    <th>依据</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="detail in group.items" :key="detail.key">
                    <td>{{ detail.label }}</td>
                    <td>{{ formatScoreCell(detail) }}</td>
                    <td>{{ formatHitCell(detail) }}</td>
                    <td>{{ detail.evidence }}</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </details>
        </div>

        <div v-if="explanationHighlights.length" class="explanation-tags">
          <span v-for="tag in explanationHighlights" :key="tag" class="metric-tag">{{ tag }}</span>
        </div>
        <p class="detail-copy">{{ props.stockDetail.candidate.explanation }}</p>
      </section>

      <section v-if="resolvedExecutionPlan" class="detail-block">
        <div class="panel-head">
          <div>
            <p class="card-label">执行计划</p>
            <h3>规则化计划</h3>
          </div>
        </div>
        <div class="detail-grid">
          <div><span>计划类型</span><strong>{{ resolvedExecutionPlan.planType }}</strong></div>
          <div><span>当前状态</span><strong>{{ resolvedExecutionPlan.status }}</strong></div>
          <div><span>触发价</span><strong>{{ formatNumber(resolvedExecutionPlan.triggerPrice) }}</strong></div>
          <div><span>止损价</span><strong>{{ formatNumber(resolvedExecutionPlan.stopLossPrice) }}</strong></div>
          <div><span>目标价</span><strong>{{ formatNumber(resolvedExecutionPlan.targetPrice) }}</strong></div>
          <div><span>风险收益比</span><strong>{{ formatNumber(resolvedExecutionPlan.riskRewardRatio) }}</strong></div>
          <div><span>观察期</span><strong>{{ resolvedExecutionPlan.observationDays }} 天</strong></div>
          <div><span>持仓上限</span><strong>{{ resolvedExecutionPlan.maxHoldingDays }} 天</strong></div>
          <div><span>允许高开</span><strong>{{ formatPercent(resolvedExecutionPlan.maxEntryGapPct) }}</strong></div>
          <div><span>建议资金</span><strong>{{ formatNumber(resolvedExecutionPlan.suggestedCapital, 0) }}</strong></div>
          <div><span>建议股数</span><strong>{{ resolvedExecutionPlan.estimatedShares ?? '--' }}</strong></div>
        </div>
        <div class="execution-note">
          <strong>计划摘要</strong>
          <span>{{ resolvedExecutionPlan.summary }}</span>
        </div>
        <div v-if="executionScenario" class="scenario-grid">
          <div class="scenario-card risk">
            <span>每股最大风险</span>
            <strong>{{ formatNumber(executionScenario.riskPerShare) }}</strong>
            <small>{{ formatPercent(executionScenario.riskPct) }}</small>
          </div>
          <div class="scenario-card reward">
            <span>每股目标空间</span>
            <strong>{{ formatNumber(executionScenario.rewardPerShare) }}</strong>
            <small>{{ formatPercent(executionScenario.rewardPct) }}</small>
          </div>
          <div class="scenario-card neutral">
            <span>触发后预计占用</span>
            <strong>{{ formatNumber(executionScenario.estimatedCost, 0) }}</strong>
            <small>约为建议资金的 {{ formatPercent(executionScenario.capitalUsagePct) }}</small>
          </div>
          <div class="scenario-card neutral">
            <span>整笔最大亏损 / 目标收益</span>
            <strong>{{ formatNumber(executionScenario.riskAmount, 0) }} / {{ formatNumber(executionScenario.rewardAmount, 0) }}</strong>
            <small>按建议股数估算</small>
          </div>
        </div>
        <div v-if="executionScenario" class="execution-note">
          <strong>入场推演</strong>
          <span>{{ executionScenario.judgement }}</span>
        </div>
        <div v-if="executionChecklist.length" class="decision-grid compact">
          <article v-for="item in executionChecklist" :key="item.key" :class="['decision-card', item.tone]">
            <span>{{ item.label }}</span>
            <strong>{{ item.value }}</strong>
            <small>{{ item.detail }}</small>
          </article>
        </div>
        <div class="score-groups">
          <details v-for="section in executionSections" :key="section.key" class="score-group-card" open>
            <summary class="score-group-summary">
              <div>
                <strong>{{ section.title }}</strong>
                <span>{{ section.items.length }} 条</span>
              </div>
            </summary>
            <div class="mini-bars">
              <div v-for="rule in section.items" :key="`${section.key}-${rule.label}`" class="mini-bar-row">
                <span>{{ rule.label }}</span>
                <strong>{{ rule.value }}</strong>
                <small>{{ rule.description }}</small>
              </div>
            </div>
          </details>
        </div>
      </section>

      <section v-if="props.stockDetail.financial" class="detail-block">
        <div class="panel-head">
          <div>
            <p class="card-label">财务快照</p>
            <h3>最新财务数据</h3>
          </div>
        </div>
        <div class="detail-grid">
          <div><span>PE</span><strong>{{ formatNumber(props.stockDetail.financial.pe) }}</strong></div>
          <div><span>PB</span><strong>{{ formatNumber(props.stockDetail.financial.pb) }}</strong></div>
          <div><span>ROE</span><strong>{{ formatNumber(props.stockDetail.financial.roe) }}</strong></div>
          <div><span>营收同比</span><strong>{{ formatPercent(props.stockDetail.financial.revenueYoy) }}</strong></div>
          <div><span>净利润同比</span><strong>{{ formatPercent(props.stockDetail.financial.netProfitYoy) }}</strong></div>
          <div><span>报告期</span><strong>{{ props.stockDetail.financial.reportDate }}</strong></div>
        </div>
      </section>

      <section v-if="props.stockDetail.fundFlow" class="detail-block">
        <div class="panel-head">
          <div>
            <p class="card-label">资金流向</p>
            <h3>主力与行业资金确认</h3>
          </div>
        </div>
        <div class="detail-grid">
          <div><span>个股主力净额</span><strong>{{ formatNumber(props.stockDetail.fundFlow.mainNetAmount, 0) }}</strong></div>
          <div><span>个股主力净占比</span><strong>{{ formatPercent(props.stockDetail.fundFlow.mainNetPct) }}</strong></div>
          <div><span>超大单净额</span><strong>{{ formatNumber(props.stockDetail.fundFlow.superLargeNetAmount, 0) }}</strong></div>
          <div><span>超大单净占比</span><strong>{{ formatPercent(props.stockDetail.fundFlow.superLargeNetPct) }}</strong></div>
          <div><span>5日资金分位</span><strong>{{ formatPercent(props.stockDetail.fundFlow.rankPercentile5d) }}</strong></div>
          <div><span>行业主力净额</span><strong>{{ formatNumber(props.stockDetail.fundFlow.industryMainNetAmount, 0) }}</strong></div>
          <div><span>行业主力净占比</span><strong>{{ formatPercent(props.stockDetail.fundFlow.industryMainNetPct) }}</strong></div>
          <div><span>行业资金排名</span><strong>{{ props.stockDetail.fundFlow.industryRank ?? '--' }}</strong></div>
          <div><span>行业资金分位</span><strong>{{ formatPercent(props.stockDetail.fundFlow.industryRankPercentile) }}</strong></div>
        </div>
        <div v-if="fundFlowInsight" class="execution-note">
          <strong>资金判断</strong>
          <span>{{ fundFlowInsight }}</span>
        </div>
      </section>

      <section v-if="props.stockDetail.lhb" class="detail-block">
        <div class="panel-head">
          <div>
            <p class="card-label">龙虎榜事件</p>
            <h3>机构参与与热度标签</h3>
          </div>
        </div>
        <div class="detail-grid">
          <div><span>今日上榜</span><strong>{{ props.stockDetail.lhb.isOnLhbToday ? '是' : '否' }}</strong></div>
          <div><span>上榜日期</span><strong>{{ props.stockDetail.lhb.tradeDate ?? '--' }}</strong></div>
          <div><span>上榜原因</span><strong>{{ props.stockDetail.lhb.reason ?? '--' }}</strong></div>
          <div><span>净买额</span><strong>{{ formatNumber(props.stockDetail.lhb.netAmount, 0) }}</strong></div>
          <div><span>机构净买额</span><strong>{{ formatNumber(props.stockDetail.lhb.institutionNetAmount, 0) }}</strong></div>
          <div><span>机构买入家数</span><strong>{{ props.stockDetail.lhb.institutionBuyCount ?? '--' }}</strong></div>
          <div><span>机构净买入</span><strong>{{ props.stockDetail.lhb.isInstitutionNetBuy ? '是' : '否' }}</strong></div>
          <div><span>20日上榜次数</span><strong>{{ props.stockDetail.lhb.recent20dLhbCount }}</strong></div>
          <div><span>距上次上榜</span><strong>{{ props.stockDetail.lhb.daysSinceLastLhb ?? '--' }}</strong></div>
        </div>
        <div v-if="lhbInsight" class="execution-note">
          <strong>龙虎榜判断</strong>
          <span>{{ lhbInsight }}</span>
        </div>
        <div v-if="props.stockDetail.lhb.riskFlags" class="execution-note">
          <strong>风险标签</strong>
          <span>{{ props.stockDetail.lhb.riskFlags }}</span>
        </div>
      </section>

      <section class="detail-block">
        <div class="panel-head">
          <div>
            <p class="card-label">近期 K 线</p>
            <h3>最近 5 个交易日</h3>
          </div>
        </div>
        <div class="mini-bars">
          <div v-for="bar in recentBarsPreview" :key="bar.tradeDate" class="mini-bar-row">
            <span>{{ bar.tradeDate }}</span>
            <strong>{{ formatNumber(bar.close) }}</strong>
            <small>高 {{ formatNumber(bar.high) }} / 低 {{ formatNumber(bar.low) }} / 量 {{ formatNumber(bar.volume / 10000, 2) }} 万股</small>
          </div>
        </div>
      </section>
    </template>

    <div v-else class="empty-panel">
      {{ props.isLoading ? '正在加载工作台...' : '尚未选择股票。' }}
    </div>
  </aside>
</template>

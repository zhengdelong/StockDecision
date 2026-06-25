<script setup lang="ts">
import { computed, defineAsyncComponent } from 'vue'
import type { ScoreRuleDetail, StockDetailResponse } from '../types'
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
  const signal = props.stockDetail?.signal
  if (!signal || signal.triggerPrice <= 0) {
    return null
  }

  const riskPerShare = Math.max(signal.triggerPrice - signal.stopLossPrice, 0)
  const rewardPerShare = Math.max(signal.targetPrice - signal.triggerPrice, 0)
  const estimatedCost = signal.triggerPrice * Math.max(signal.estimatedShares, 0)
  const riskAmount = riskPerShare * Math.max(signal.estimatedShares, 0)
  const rewardAmount = rewardPerShare * Math.max(signal.estimatedShares, 0)
  const riskPct = signal.triggerPrice > 0 ? (riskPerShare / signal.triggerPrice) * 100 : 0
  const rewardPct = signal.triggerPrice > 0 ? (rewardPerShare / signal.triggerPrice) * 100 : 0
  const capitalUsagePct = signal.suggestedCapital > 0 ? (estimatedCost / signal.suggestedCapital) * 100 : 0

  let judgement = '仓位与止损设置基本可执行，重点看触发后是否有持续放量。'
  if (riskPct >= 5) {
    judgement = '单笔止损空间偏大，若要参与，最好降低仓位，避免一笔交易吃掉过多回撤。'
  } else if (signal.riskRewardRatio < 2) {
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

</script>

<template>
  <aside class="detail-panel">
    <div v-if="props.isDetailLoading" class="detail-loading">正在加载个股详情...</div>

    <template v-else-if="props.stockDetail">
      <header class="detail-header">
        <div>
          <p class="eyebrow detail-code">{{ props.stockDetail.stockCode }}</p>
          <h2>{{ props.stockDetail.stockName }}</h2>
          <p class="muted">{{ props.stockDetail.industryName ?? '未知行业' }}</p>
        </div>
        <div class="detail-badges">
          <span class="pill neutral">{{ props.stockDetail.tradeDate }}</span>
          <span class="pill neutral">{{ props.stockDetail.snapshotVersionName }}</span>
          <span class="pill soft">{{ resolveDistanceLabel(props.stockDetail) }}</span>
        </div>
      </header>

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
                    <td>{{ formatNumber(detail.score, 0) }} / {{ formatNumber(detail.maxScore, 0) }}</td>
                    <td>{{ detail.hit ? '是' : '否' }}</td>
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

      <section v-if="props.stockDetail.signal" class="detail-block">
        <div class="panel-head">
          <div>
            <p class="card-label">执行计划</p>
            <h3>信号设置</h3>
          </div>
        </div>
        <div class="detail-grid">
          <div><span>触发价</span><strong>{{ formatNumber(props.stockDetail.signal.triggerPrice) }}</strong></div>
          <div><span>止损价</span><strong>{{ formatNumber(props.stockDetail.signal.stopLossPrice) }}</strong></div>
          <div><span>目标价</span><strong>{{ formatNumber(props.stockDetail.signal.targetPrice) }}</strong></div>
          <div><span>风险收益比</span><strong>{{ formatNumber(props.stockDetail.signal.riskRewardRatio) }}</strong></div>
          <div><span>建议资金</span><strong>{{ formatNumber(props.stockDetail.signal.suggestedCapital, 0) }}</strong></div>
          <div><span>建议股数</span><strong>{{ props.stockDetail.signal.estimatedShares }}</strong></div>
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

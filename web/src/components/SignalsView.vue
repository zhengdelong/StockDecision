<script setup lang="ts">
import type { SignalSortMode } from '../composables/useSignalDesk'
import type { SignalItem } from '../types'
import { formatNumber, formatPercent } from '../utils/formatters'

/**
 * 信号页聚焦可执行清单，不复用候选池的打分过滤项。
 */
const props = defineProps<{
  isLoading: boolean
  searchText: string
  selectedStockCode: string
  selectedTradeDate: string
  signalPageIndex: number
  signals: SignalItem[]
  sortMode: SignalSortMode
  totalCount: number
  totalPages: number
}>()

const emit = defineEmits<{
  (event: 'update:searchText', value: string): void
  (event: 'update:selectedTradeDate', value: string): void
  (event: 'update:sortMode', value: SignalSortMode): void
  (event: 'apply'): void
  (event: 'move-page', step: number): void
  (event: 'reset-date'): void
  (event: 'select-stock', stockCode: string): void
}>()

/**
 * 信号的仓位占用比例，用于快速判断是否接近满仓。
 */
function resolveCapitalUsagePct(item: SignalItem): number | null {
  if (item.suggestedCapital <= 0) {
    return null
  }

  return ((item.triggerPrice * item.estimatedShares) / item.suggestedCapital) * 100
}

/**
 * 将价格计划折算为百分比，方便用户横向比较风险和目标空间。
 */
function resolveExecutionPercentages(item: SignalItem): { riskPct: number | null; rewardPct: number | null } {
  if (item.triggerPrice <= 0) {
    return { riskPct: null, rewardPct: null }
  }

  return {
    riskPct: ((item.triggerPrice - item.stopLossPrice) / item.triggerPrice) * 100,
    rewardPct: ((item.targetPrice - item.triggerPrice) / item.triggerPrice) * 100,
  }
}

/**
 * 用中文总结当前信号的执行质量。
 */
function resolveExecutionLabel(item: SignalItem): string {
  const riskReward = item.riskRewardRatio
  const percentages = resolveExecutionPercentages(item)

  if ((percentages.riskPct ?? 0) >= 5) {
    return '止损偏宽'
  }

  if (riskReward >= 3.5) {
    return '执行质量高'
  }

  if (riskReward >= 2) {
    return '可以跟踪'
  }

  return '需要挑选'
}
</script>

<template>
  <div class="table-section">
    <div class="toolbar">
      <label class="field">
        <span>交易日</span>
        <input
          :value="props.selectedTradeDate"
          type="date"
          @input="emit('update:selectedTradeDate', ($event.target as HTMLInputElement).value)"
        />
      </label>
      <label class="field search-field">
        <span>搜索</span>
        <input
          :value="props.searchText"
          type="text"
          placeholder="代码、名称、行业"
          @input="emit('update:searchText', ($event.target as HTMLInputElement).value)"
        />
      </label>
      <label class="field">
        <span>排序</span>
        <select
          :value="props.sortMode"
          @change="emit('update:sortMode', ($event.target as HTMLSelectElement).value as SignalSortMode)"
        >
          <option value="score">总分</option>
          <option value="rr">风险收益比</option>
          <option value="capital">建议资金</option>
        </select>
      </label>
      <button class="minor-button" :disabled="!props.selectedTradeDate || props.isLoading" @click="emit('apply')">应用筛选</button>
      <button class="minor-button light" :disabled="props.isLoading" @click="emit('reset-date')">最新交易日</button>
      <div class="toolbar-summary">
        当前页 {{ props.signals.length }} 条，共 {{ props.totalCount }} 条，第 {{ props.signalPageIndex }} 页
      </div>
    </div>

    <div v-if="props.signals.length" class="table-shell">
      <table class="data-table signals-table">
        <thead>
          <tr>
            <th>股票</th>
            <th>策略与评分</th>
            <th>价格计划</th>
            <th>风险回报</th>
            <th>仓位计划</th>
            <th>执行结论</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="item in props.signals"
            :key="item.stockCode"
            :class="{ selected: props.selectedStockCode === item.stockCode }"
            @click="emit('select-stock', item.stockCode)"
          >
            <td>
              <div class="candidate-cell-main">
                <strong>{{ item.stockName }}</strong>
                <span>{{ item.stockCode }}{{ item.industryName ? ` / ${item.industryName}` : '' }}</span>
              </div>
            </td>
            <td>
              <div class="candidate-score-cell">
                <div class="candidate-score-head">
                  <strong>{{ item.strategyType }}</strong>
                  <span>{{ formatNumber(item.totalScore, 1) }} 分</span>
                </div>
                <div class="candidate-score-track">
                  <div class="candidate-score-fill" :style="{ width: `${Math.min(item.totalScore, 100)}%` }" />
                </div>
                <small>{{ resolveExecutionLabel(item) }}</small>
              </div>
            </td>
            <td>
              <div class="candidate-plan-cell">
                <strong>触发 {{ formatNumber(item.triggerPrice) }}</strong>
                <span>止损 {{ formatNumber(item.stopLossPrice) }} / 目标 {{ formatNumber(item.targetPrice) }}</span>
              </div>
            </td>
            <td>
              <div class="candidate-risk-cell">
                <strong>{{ formatNumber(item.riskRewardRatio) }}</strong>
                <span>
                  风险 {{ formatPercent(resolveExecutionPercentages(item).riskPct) }} / 空间 {{ formatPercent(resolveExecutionPercentages(item).rewardPct) }}
                </span>
                <small>盈亏比</small>
              </div>
            </td>
            <td>
              <div class="candidate-plan-cell">
                <strong>{{ formatNumber(item.suggestedCapital, 0) }}</strong>
                <span>约 {{ item.estimatedShares }} 股 / 占用 {{ formatPercent(resolveCapitalUsagePct(item)) }}</span>
              </div>
            </td>
            <td>
              <div class="candidate-status-cell">
                <span class="pill positive">可执行</span>
                <small>{{ resolveExecutionLabel(item) }}</small>
              </div>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <div v-if="props.totalCount > 0" class="pager">
      <button class="minor-button light" :disabled="props.signalPageIndex <= 1" @click="emit('move-page', -1)">上一页</button>
      <span>第 {{ props.signalPageIndex }} / {{ props.totalPages }} 页</span>
      <button class="minor-button light" :disabled="props.signalPageIndex >= props.totalPages" @click="emit('move-page', 1)">下一页</button>
    </div>
    <div v-else class="empty-state">当前筛选条件下没有交易信号。</div>
  </div>
</template>

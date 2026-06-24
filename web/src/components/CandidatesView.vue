<script setup lang="ts">
import type { CandidateItem } from '../types'
import type { CandidateSortMode } from '../composables/useSignalDesk'
import { formatNumber, formatPercent } from '../utils/formatters'

/**
 * 候选池页面，负责展示服务端分页结果和筛选表单。
 */
const props = defineProps<{
  candidatePageIndex: number
  candidates: CandidateItem[]
  isLoading: boolean
  minScore: number
  onlyTradable: boolean
  searchText: string
  selectedStockCode: string
  selectedTradeDate: string
  sortMode: CandidateSortMode
  totalCount: number
  totalPages: number
}>()

const emit = defineEmits<{
  (event: 'update:minScore', value: number): void
  (event: 'update:onlyTradable', value: boolean): void
  (event: 'update:searchText', value: string): void
  (event: 'update:selectedTradeDate', value: string): void
  (event: 'update:sortMode', value: CandidateSortMode): void
  (event: 'apply'): void
  (event: 'move-page', step: number): void
  (event: 'reset-date'): void
  (event: 'select-stock', stockCode: string): void
}>()

/**
 * 根据候选股总分给出更直观的强弱文案。
 */
function resolveScoreLabel(item: CandidateItem): string {
  if (item.totalScore >= 75) {
    return '高优先级'
  }

  if (item.totalScore >= 68) {
    return '继续跟踪'
  }

  return '一般观察'
}

/**
 * 用风险收益比区分当前计划的性价比。
 */
function resolveRiskRewardLabel(item: CandidateItem): string {
  if (item.riskRewardRatio >= 3.5) {
    return '盈亏比优秀'
  }

  if (item.riskRewardRatio >= 2) {
    return '盈亏比尚可'
  }

  return '盈亏比偏弱'
}

/**
 * 把止损和目标空间折算成百分比，方便横向比较。
 */
function resolvePlanPercentages(item: CandidateItem): { riskPct: number | null; rewardPct: number | null } {
  if (item.close <= 0) {
    return { riskPct: null, rewardPct: null }
  }

  return {
    riskPct: ((item.close - item.stopLossPrice) / item.close) * 100,
    rewardPct: ((item.targetPrice - item.close) / item.close) * 100,
  }
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
        <span>最低分</span>
        <input
          :value="props.minScore"
          type="number"
          min="0"
          max="100"
          @input="emit('update:minScore', Number(($event.target as HTMLInputElement).value))"
        />
      </label>
      <label class="field">
        <span>排序</span>
        <select
          :value="props.sortMode"
          @change="emit('update:sortMode', ($event.target as HTMLSelectElement).value as CandidateSortMode)"
        >
          <option value="score">总分</option>
          <option value="rr">风险收益比</option>
          <option value="close">收盘价</option>
        </select>
      </label>
      <label class="field field-check">
        <input
          :checked="props.onlyTradable"
          type="checkbox"
          @change="emit('update:onlyTradable', ($event.target as HTMLInputElement).checked)"
        />
        <span>仅看可执行</span>
      </label>
      <button class="minor-button" :disabled="!props.selectedTradeDate || props.isLoading" @click="emit('apply')">应用筛选</button>
      <button class="minor-button light" :disabled="props.isLoading" @click="emit('reset-date')">最新交易日</button>
      <div class="toolbar-summary">
        当前页 {{ props.candidates.length }} 条，共 {{ props.totalCount }} 条，第 {{ props.candidatePageIndex }} 页
      </div>
    </div>

    <div v-if="props.candidates.length" class="table-shell">
      <table class="data-table candidates-table">
        <thead>
          <tr>
            <th>股票</th>
            <th>行业</th>
            <th>评分</th>
            <th>交易计划</th>
            <th>风险回报</th>
            <th>状态</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="item in props.candidates"
            :key="item.stockCode"
            :class="{ selected: props.selectedStockCode === item.stockCode }"
            @click="emit('select-stock', item.stockCode)"
          >
            <td>
              <div class="candidate-cell-main">
                <strong>{{ item.stockName }}</strong>
                <span>{{ item.stockCode }}</span>
              </div>
            </td>
            <td>
              <div class="candidate-cell-main">
                <strong>{{ item.industryName ?? '-' }}</strong>
                <span>{{ item.strategyType }}</span>
              </div>
            </td>
            <td>
              <div class="candidate-score-cell">
                <div class="candidate-score-head">
                  <strong>{{ formatNumber(item.totalScore, 1) }}</strong>
                  <span>{{ item.grade }}</span>
                </div>
                <div class="candidate-score-track">
                  <div class="candidate-score-fill" :style="{ width: `${Math.min(item.totalScore, 100)}%` }" />
                </div>
                <small>{{ resolveScoreLabel(item) }}</small>
              </div>
            </td>
            <td>
              <div class="candidate-plan-cell">
                <strong>收 {{ formatNumber(item.close) }}</strong>
                <span>止损 {{ formatNumber(item.stopLossPrice) }} / 目标 {{ formatNumber(item.targetPrice) }}</span>
              </div>
            </td>
            <td>
              <div class="candidate-risk-cell">
                <strong>{{ formatNumber(item.riskRewardRatio) }}</strong>
                <span>
                  风险 {{ formatPercent(resolvePlanPercentages(item).riskPct) }} / 空间 {{ formatPercent(resolvePlanPercentages(item).rewardPct) }}
                </span>
                <small>{{ resolveRiskRewardLabel(item) }}</small>
              </div>
            </td>
            <td>
              <div class="candidate-status-cell">
                <span :class="['pill', item.isTradable ? 'positive' : 'neutral']">
                  {{ item.isTradable ? '可执行' : '观察' }}
                </span>
                <small>{{ item.isTradable ? '满足当前执行条件' : '先放入观察名单' }}</small>
              </div>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <div v-if="props.totalCount > 0" class="pager">
      <button class="minor-button light" :disabled="props.candidatePageIndex <= 1" @click="emit('move-page', -1)">上一页</button>
      <span>第 {{ props.candidatePageIndex }} / {{ props.totalPages }} 页</span>
      <button class="minor-button light" :disabled="props.candidatePageIndex >= props.totalPages" @click="emit('move-page', 1)">下一页</button>
    </div>
    <div v-else class="empty-state">当前筛选条件下没有候选股。</div>
  </div>
</template>

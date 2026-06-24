<script setup lang="ts">
import type { FinancialSortMode } from '../composables/useSignalDesk'
import type { FinancialItem } from '../types'
import { formatNumber, formatPercent } from '../utils/formatters'

/**
 * 财务页面聚焦横向比较，不复用行业/候选页的表单字段。
 */
const props = defineProps<{
  financialPageIndex: number
  financials: FinancialItem[]
  isLoading: boolean
  minRoe: number
  positiveGrowthOnly: boolean
  searchText: string
  sortMode: FinancialSortMode
  totalCount: number
  totalPages: number
}>()

const emit = defineEmits<{
  (event: 'update:minRoe', value: number): void
  (event: 'update:positiveGrowthOnly', value: boolean): void
  (event: 'update:searchText', value: string): void
  (event: 'update:sortMode', value: FinancialSortMode): void
  (event: 'apply'): void
  (event: 'move-page', step: number): void
}>()
</script>

<template>
  <div class="table-section">
    <div class="toolbar">
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
        <span>最低 ROE</span>
        <input
          :value="props.minRoe"
          type="number"
          min="0"
          max="100"
          @input="emit('update:minRoe', Number(($event.target as HTMLInputElement).value))"
        />
      </label>
      <label class="field">
        <span>排序</span>
        <select
          :value="props.sortMode"
          @change="emit('update:sortMode', ($event.target as HTMLSelectElement).value as FinancialSortMode)"
        >
          <option value="roe">ROE</option>
          <option value="revenue">营收同比</option>
          <option value="profit">净利润同比</option>
          <option value="marketCap">流通市值</option>
        </select>
      </label>
      <label class="field field-check">
        <input
          :checked="props.positiveGrowthOnly"
          type="checkbox"
          @change="emit('update:positiveGrowthOnly', ($event.target as HTMLInputElement).checked)"
        />
        <span>仅看正增长</span>
      </label>
      <button class="minor-button" :disabled="props.isLoading" @click="emit('apply')">应用筛选</button>
      <div class="toolbar-summary">
        当前页 {{ props.financials.length }} 条，共 {{ props.totalCount }} 条，第 {{ props.financialPageIndex }} 页
      </div>
    </div>

    <div v-if="props.financials.length" class="table-shell">
      <table class="data-table">
        <thead>
          <tr>
            <th>代码</th>
            <th>名称</th>
            <th>行业</th>
            <th>报告期</th>
            <th>PE</th>
            <th>PB</th>
            <th>ROE</th>
            <th>营收同比</th>
            <th>净利润同比</th>
            <th>流通市值</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="item in props.financials" :key="item.stockCode">
            <td>{{ item.stockCode }}</td>
            <td>{{ item.stockName }}</td>
            <td>{{ item.industryName ?? '-' }}</td>
            <td>{{ item.reportDate }}</td>
            <td>{{ formatNumber(item.pe) }}</td>
            <td>{{ formatNumber(item.pb) }}</td>
            <td>{{ formatNumber(item.roe, 1) }}</td>
            <td>{{ formatPercent(item.revenueYoy) }}</td>
            <td>{{ formatPercent(item.netProfitYoy) }}</td>
            <td>{{ formatNumber(item.freeFloatMarketCap, 0) }}</td>
          </tr>
        </tbody>
      </table>
    </div>

    <div v-if="props.totalCount > 0" class="pager">
      <button class="minor-button light" :disabled="props.financialPageIndex <= 1" @click="emit('move-page', -1)">上一页</button>
      <span>第 {{ props.financialPageIndex }} / {{ props.totalPages }} 页</span>
      <button class="minor-button light" :disabled="props.financialPageIndex >= props.totalPages" @click="emit('move-page', 1)">下一页</button>
    </div>
    <div v-else class="empty-state">当前筛选条件下没有财务快照。</div>
  </div>
</template>

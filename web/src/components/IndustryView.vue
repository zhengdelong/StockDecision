<script setup lang="ts">
import type { IndustrySortMode } from '../composables/useSignalDesk'
import type { IndustryItem } from '../types'
import { formatNumber, formatPercent } from '../utils/formatters'

const props = defineProps<{
  industries: IndustryItem[]
  industryPageIndex: number
  isLoading: boolean
  searchText: string
  selectedTradeDate: string
  sortMode: IndustrySortMode
  totalCount: number
  totalPages: number
}>()

const emit = defineEmits<{
  (event: 'update:searchText', value: string): void
  (event: 'update:selectedTradeDate', value: string): void
  (event: 'update:sortMode', value: IndustrySortMode): void
  (event: 'apply'): void
  (event: 'move-page', step: number): void
  (event: 'reset-date'): void
}>()
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
          placeholder="行业代码或名称"
          @input="emit('update:searchText', ($event.target as HTMLInputElement).value)"
        />
      </label>
      <label class="field">
        <span>排序</span>
        <select
          :value="props.sortMode"
          @change="emit('update:sortMode', ($event.target as HTMLSelectElement).value as IndustrySortMode)"
        >
          <option value="strength">20日强度</option>
          <option value="rank">排名</option>
          <option value="candidates">候选数</option>
          <option value="signals">信号数</option>
        </select>
      </label>
      <button class="minor-button" :disabled="!props.selectedTradeDate || props.isLoading" @click="emit('apply')">应用筛选</button>
      <button class="minor-button light" :disabled="props.isLoading" @click="emit('reset-date')">最新交易日</button>
      <div class="toolbar-summary">
        当前页 {{ props.industries.length }} 条，共 {{ props.totalCount }} 条，第 {{ props.industryPageIndex }} 页
      </div>
    </div>

    <div v-if="props.industries.length" class="table-shell">
      <table class="data-table">
        <thead>
          <tr>
            <th>代码</th>
            <th>行业</th>
            <th>20日强度</th>
            <th>排名</th>
            <th>候选数</th>
            <th>信号数</th>
            <th>最高候选分</th>
            <th>最高信号分</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="item in props.industries" :key="`${item.industryCode}-${item.industryName}`">
            <td>{{ item.industryCode || '-' }}</td>
            <td>{{ item.industryName }}</td>
            <td>{{ formatPercent(item.pctChange20d) }}</td>
            <td>{{ item.rank20d === 2147483647 ? '-' : item.rank20d }}</td>
            <td>{{ item.candidateCount }}</td>
            <td>{{ item.signalCount }}</td>
            <td>{{ formatNumber(item.topCandidateScore, 1) }}</td>
            <td>{{ formatNumber(item.topSignalScore, 1) }}</td>
          </tr>
        </tbody>
      </table>
    </div>

    <div v-if="props.totalCount > 0" class="pager">
      <button class="minor-button light" :disabled="props.industryPageIndex <= 1" @click="emit('move-page', -1)">上一页</button>
      <span>第 {{ props.industryPageIndex }} / {{ props.totalPages }} 页</span>
      <button class="minor-button light" :disabled="props.industryPageIndex >= props.totalPages" @click="emit('move-page', 1)">下一页</button>
    </div>
    <div v-else class="empty-state">当前筛选条件下没有行业数据。</div>
  </div>
</template>

<script setup lang="ts">
import type { CandidateItem, DashboardResponse } from '../types'
import { formatDate, formatNumber } from '../utils/formatters'

/**
 * 顶部概览区，固定展示全局快照和头部重点标的。
 */
defineProps<{
  dashboard: DashboardResponse | null
  regimeTone: string
  topCandidates: CandidateItem[]
}>()

const emit = defineEmits<{
  (event: 'select-stock', stockCode: string): void
}>()
</script>

<template>
  <section class="overview-grid">
    <article class="overview-card regime-card">
      <p class="card-label">市场环境</p>
      <h2>{{ dashboard?.marketRegime ?? '未知' }}</h2>
      <p class="card-copy">
        {{ dashboard?.isSignalEligible ? '当前环境允许生成并执行交易信号。' : '当前环境仅建议观察。' }}
      </p>
      <span :class="['pill', regimeTone]">
        {{ dashboard?.isSignalEligible ? '可交易环境' : '观察环境' }}
      </span>
    </article>

    <article class="overview-card">
      <p class="card-label">快照状态</p>
      <dl class="stat-list">
        <div><dt>交易日</dt><dd>{{ formatDate(dashboard?.tradeDate) }}</dd></div>
        <div><dt>结果版本</dt><dd>{{ dashboard?.snapshotVersionName ?? '-' }}</dd></div>
        <div><dt>最近采集</dt><dd>{{ formatDate(dashboard?.latestIngestionAtUtc) }}</dd></div>
        <div><dt>数据完整</dt><dd>{{ dashboard?.isDataComplete ? '是' : '否' }}</dd></div>
      </dl>
    </article>

    <article class="overview-card">
      <p class="card-label">流程计数</p>
      <div class="count-grid">
        <div><span>候选池</span><strong>{{ dashboard?.candidateCount ?? 0 }}</strong></div>
        <div><span>信号数</span><strong>{{ dashboard?.signalCount ?? 0 }}</strong></div>
      </div>
    </article>

    <article class="overview-card">
      <p class="card-label">重点个股</p>
      <div v-if="topCandidates.length" class="top-list">
        <button
          v-for="item in topCandidates"
          :key="item.stockCode"
          class="top-row"
          @click="emit('select-stock', item.stockCode)"
        >
          <div>
            <strong>{{ item.stockName }}</strong>
            <span>{{ item.stockCode }} / {{ item.strategyType }}</span>
          </div>
          <b>{{ formatNumber(item.totalScore, 1) }}</b>
        </button>
      </div>
      <p v-else class="empty-copy">当前还没有候选股数据。</p>
    </article>
  </section>
</template>

<script setup lang="ts">
import type { CandidateItem } from '../types'
import { formatNumber } from '../utils/formatters'

/**
 * 工作台首页，强调流程说明和高优先级候选股。
 */
defineProps<{
  topCandidates: CandidateItem[]
}>()

const emit = defineEmits<{
  (event: 'select-stock', stockCode: string): void
}>()
</script>

<template>
  <div class="dashboard-layout">
    <article class="panel-card">
      <div class="panel-head">
        <div>
          <p class="card-label">执行路径</p>
          <h3>当前处理流程</h3>
        </div>
      </div>
      <ul class="timeline-list">
        <li>先把原始采集表同步到领域快照表。</li>
        <li>再计算技术指标与市场环境。</li>
        <li>随后按相对强弱、趋势、量价和基本面为候选股打分排序。</li>
        <li>最后从候选池中过滤出更可执行的交易信号。</li>
      </ul>
    </article>

    <article class="panel-card">
      <div class="panel-head">
        <div>
          <p class="card-label">关注重点</p>
          <h3>当前高分候选股</h3>
        </div>
      </div>
      <div v-if="topCandidates.length" class="compact-list">
        <button
          v-for="item in topCandidates"
          :key="item.stockCode"
          class="compact-row"
          @click="emit('select-stock', item.stockCode)"
        >
          <div>
            <strong>{{ item.stockName }}</strong>
            <span>{{ item.industryName ?? '未知行业' }}</span>
          </div>
          <div class="compact-meta">
            <span>{{ item.grade }}</span>
            <b>{{ formatNumber(item.totalScore, 1) }}</b>
          </div>
        </button>
      </div>
      <p v-else class="empty-copy">当前快照还没有返回候选股数据。</p>
    </article>
  </div>
</template>

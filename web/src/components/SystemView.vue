<script setup lang="ts">
import type { SystemHealthResponse, SystemSummaryResponse } from '../types'
import { formatNumber } from '../utils/formatters'

defineProps<{
  about: SystemSummaryResponse | null
  health: SystemHealthResponse | null
  isLoading: boolean
}>()
</script>

<template>
  <div class="system-layout">
    <article class="panel-card">
      <div class="panel-head">
        <div>
          <p class="card-label">服务健康</p>
          <h3>API 运行状态</h3>
        </div>
        <span :class="['pill', health?.status === '正常' ? 'positive' : 'warning']">
          {{ health?.status ?? (isLoading ? '加载中' : '未知') }}
        </span>
      </div>

      <div v-if="health" class="detail-grid">
        <div><span>服务名</span><strong>{{ health.service }}</strong></div>
        <div><span>策略版本</span><strong>{{ health.strategy }}</strong></div>
        <div><span>状态</span><strong>{{ health.status }}</strong></div>
      </div>
      <div v-else class="empty-state">系统健康状态暂时不可用。</div>
    </article>

    <article class="panel-card">
      <div class="panel-head">
        <div>
          <p class="card-label">系统信息</p>
          <h3>决策引擎概览</h3>
        </div>
      </div>

      <div v-if="about" class="system-stack">
        <div class="detail-grid">
          <div><span>名称</span><strong>{{ about.name }}</strong></div>
          <div><span>模式</span><strong>{{ about.mode }}</strong></div>
          <div><span>资金规模</span><strong>{{ formatNumber(about.capital, 0) }}</strong></div>
          <div><span>版本</span><strong>{{ about.strategyVersion }}</strong></div>
        </div>

        <div class="document-block">
          <p class="card-label">参考文档</p>
          <ul class="document-list">
            <li v-for="document in about.documents" :key="document">{{ document }}</li>
          </ul>
        </div>
      </div>
      <div v-else class="empty-state">系统摘要暂时不可用。</div>
    </article>
  </div>
</template>

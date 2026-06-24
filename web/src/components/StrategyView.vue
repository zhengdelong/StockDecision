<script setup lang="ts">
import type { StrategyExplanationResponse } from '../types'
import { formatNumber } from '../utils/formatters'

defineProps<{
  explanation: StrategyExplanationResponse | null
}>()
</script>

<template>
  <div class="system-layout">
    <article class="panel-card">
      <div class="panel-head">
        <div>
          <p class="card-label">策略版本</p>
          <h3>{{ explanation?.strategyVersion ?? '暂无版本信息' }}</h3>
        </div>
      </div>

      <div v-if="explanation" class="system-stack">
        <article v-for="section in explanation.sections" :key="section.title" class="document-block">
          <p class="card-label">{{ section.title }}</p>
          <ul class="document-list">
            <li v-for="item in section.items" :key="item">{{ item }}</li>
          </ul>
        </article>
      </div>
      <div v-else class="empty-state">当前还没有可展示的策略说明。</div>
    </article>

    <article class="panel-card">
      <div class="panel-head">
        <div>
          <p class="card-label">评分维度</p>
          <h3>候选股分数是怎么组成的</h3>
        </div>
      </div>

      <div v-if="explanation" class="system-stack">
        <article v-for="dimension in explanation.scoreDimensions" :key="dimension.name" class="document-block">
          <p class="card-label">{{ dimension.name }} / {{ formatNumber(dimension.maxScore, 0) }}</p>
          <ul class="document-list">
            <li v-for="rule in dimension.rules" :key="rule">{{ rule }}</li>
          </ul>
        </article>
      </div>
    </article>

    <article class="panel-card">
      <div class="panel-head">
        <div>
          <p class="card-label">执行规则</p>
          <h3>什么时候看，什么时候做，什么时候放弃</h3>
        </div>
      </div>

      <div v-if="explanation" class="detail-grid">
        <div v-for="rule in explanation.executionRules" :key="rule.name">
          <span>{{ rule.name }}</span>
          <strong>{{ rule.description }}</strong>
        </div>
      </div>
    </article>
  </div>
</template>

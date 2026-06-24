<script setup lang="ts">
import { reactive, watch } from 'vue'
import type { LearningReviewOverviewResponse, StockDetailResponse } from '../types'
import { formatDateTime } from '../utils/formatters'

const props = defineProps<{
  isSubmitting: boolean
  overview: LearningReviewOverviewResponse | null
  stockDetail: StockDetailResponse | null
  tradeDate: string
}>()

const emit = defineEmits<{
  (
    event: 'save-review',
    payload: {
      stockCode: string
      stockName: string
      tradeDate: string
      buyReason: string
      marketContext: string
      executionDiscipline: string
      resultSummary: string
      improvementPlan: string
    }
  ): void
}>()

const form = reactive({
  buyReason: '',
  marketContext: '',
  executionDiscipline: '',
  resultSummary: '',
  improvementPlan: '',
})

watch(
  () => props.stockDetail?.stockCode,
  () => {
    form.buyReason = ''
    form.marketContext = ''
    form.executionDiscipline = ''
    form.resultSummary = ''
    form.improvementPlan = ''
  },
)

function submit() {
  if (!props.stockDetail) {
    return
  }

  emit('save-review', {
    stockCode: props.stockDetail.stockCode,
    stockName: props.stockDetail.stockName,
    tradeDate: props.stockDetail.tradeDate,
    buyReason: form.buyReason,
    marketContext: form.marketContext,
    executionDiscipline: form.executionDiscipline,
    resultSummary: form.resultSummary,
    improvementPlan: form.improvementPlan,
  })
}
</script>

<template>
  <div class="system-layout">
    <article class="panel-card">
      <div class="panel-head">
        <div>
          <p class="card-label">复盘提示</p>
          <h3>把每一笔交易变成下一次进步的素材</h3>
        </div>
      </div>

      <ul v-if="overview?.reviewPrompts?.length" class="document-list">
        <li v-for="prompt in overview.reviewPrompts" :key="prompt">{{ prompt }}</li>
      </ul>
      <div v-else class="empty-state">当前还没有复盘提示。</div>
    </article>

    <article class="panel-card">
      <div class="panel-head">
        <div>
          <p class="card-label">记录复盘</p>
          <h3>{{ stockDetail ? `${stockDetail.stockCode} ${stockDetail.stockName}` : '先选择一只股票' }}</h3>
        </div>
      </div>

      <div v-if="stockDetail" class="toolbar" style="display: grid; gap: 14px;">
        <label class="field" style="display: grid; gap: 8px;">
          <span>买入原因</span>
          <input v-model="form.buyReason" type="text" placeholder="为什么当时想买它？">
        </label>
        <label class="field" style="display: grid; gap: 8px;">
          <span>市场环境</span>
          <input v-model="form.marketContext" type="text" placeholder="大盘、行业、个股位置是否支持这次执行？">
        </label>
        <label class="field" style="display: grid; gap: 8px;">
          <span>纪律执行情况</span>
          <input v-model="form.executionDiscipline" type="text" placeholder="有没有按止损、止盈、仓位规则执行？">
        </label>
        <label class="field" style="display: grid; gap: 8px;">
          <span>实际结果</span>
          <input v-model="form.resultSummary" type="text" placeholder="最后赚了还是亏了？主要原因是什么？">
        </label>
        <label class="field" style="display: grid; gap: 8px;">
          <span>下次改进点</span>
          <input v-model="form.improvementPlan" type="text" placeholder="如果再来一次，你最想改掉什么？">
        </label>
        <button class="minor-button" :disabled="isSubmitting" @click="submit">
          {{ isSubmitting ? '正在保存...' : '保存复盘记录' }}
        </button>
      </div>
      <div v-else class="empty-state">先在候选池、信号列表或个股详情里选中一只股票，再写复盘。</div>
    </article>

    <article class="panel-card">
      <div class="panel-head">
        <div>
          <p class="card-label">历史复盘</p>
          <h3>最近写过的复盘记录</h3>
        </div>
      </div>

      <div v-if="overview?.reviews?.length" class="table-shell">
        <table class="data-table">
          <thead>
            <tr>
              <th>更新时间</th>
              <th>代码</th>
              <th>名称</th>
              <th>交易日</th>
              <th>买入原因</th>
              <th>市场环境</th>
              <th>纪律执行</th>
              <th>结果</th>
              <th>改进点</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="item in overview.reviews" :key="item.id">
              <td>{{ formatDateTime(item.updatedAtUtc) }}</td>
              <td>{{ item.stockCode }}</td>
              <td>{{ item.stockName }}</td>
              <td>{{ item.tradeDate }}</td>
              <td>{{ item.buyReason }}</td>
              <td>{{ item.marketContext }}</td>
              <td>{{ item.executionDiscipline }}</td>
              <td>{{ item.resultSummary }}</td>
              <td>{{ item.improvementPlan }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <div v-else class="empty-state">还没有保存过复盘记录。</div>
    </article>
  </div>
</template>

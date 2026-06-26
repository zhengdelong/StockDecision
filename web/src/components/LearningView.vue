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
      errorTags: string[]
      isStrategyAligned: boolean
      followedStopLoss: boolean
      followedTakeProfit: boolean
      modifiedPlanDuringTrade: boolean
      followedGapRule: boolean
    }
  ): void
}>()

const form = reactive({
  buyReason: '',
  marketContext: '',
  executionDiscipline: '',
  resultSummary: '',
  improvementPlan: '',
  errorTags: '',
})

watch(
  () => props.stockDetail?.stockCode,
  () => {
    form.buyReason = ''
    form.marketContext = ''
    form.executionDiscipline = ''
    form.resultSummary = ''
    form.improvementPlan = ''
    form.errorTags = ''
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
    errorTags: form.errorTags.split(/[，,]/).map(item => item.trim()).filter(Boolean),
    isStrategyAligned: !form.errorTags.includes('策略外交易'),
    followedStopLoss: !form.errorTags.includes('不按止损'),
    followedTakeProfit: !form.errorTags.includes('过早止盈'),
    modifiedPlanDuringTrade: form.errorTags.includes('情绪化交易'),
    followedGapRule: !form.errorTags.includes('追高'),
  })
}
</script>

<template>
  <div class="system-layout">
    <article class="panel-card">
      <div class="panel-head">
        <div>
          <p class="card-label">学习进度</p>
          <h3>先看纪律有没有稳定下来</h3>
        </div>
      </div>

      <div v-if="overview?.progressSummary" class="detail-grid">
        <div><span>复盘数量</span><strong>{{ overview.progressSummary.reviewCount }}</strong></div>
        <div><span>策略内交易</span><strong>{{ overview.progressSummary.strategyAlignedTradeCount }}</strong></div>
        <div><span>策略外交易</span><strong>{{ overview.progressSummary.offStrategyTradeCount }}</strong></div>
        <div><span>连续按止损</span><strong>{{ overview.progressSummary.consecutiveStopLossFollowCount }}</strong></div>
        <div><span>连续守高开规则</span><strong>{{ overview.progressSummary.consecutiveGapRuleFollowCount }}</strong></div>
      </div>
      <div v-if="overview?.errorTagStats?.length" class="toolbar" style="padding-top: 0;">
        <span v-for="item in overview.errorTagStats" :key="item.tag" class="pill neutral">
          {{ item.tag }} {{ item.count }}
        </span>
      </div>
    </article>

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
        <label class="field" style="display: grid; gap: 8px;">
          <span>错误标签</span>
          <input v-model="form.errorTags" type="text" placeholder="例如：追高, 不按止损, 情绪化交易">
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
              <th>标签</th>
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
              <td>{{ item.errorTags.join('、') }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <div v-else class="empty-state">还没有保存过复盘记录。</div>
    </article>
  </div>
</template>

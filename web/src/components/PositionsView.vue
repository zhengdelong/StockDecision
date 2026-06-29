<script setup lang="ts">
import { computed, reactive } from 'vue'
import type { SimulatedPositionItem, SimulatedTradeHistoryItem, StockDetailResponse } from '../types'
import { formatDateTime, formatNumber, formatPercent } from '../utils/formatters'

const props = defineProps<{
  history: SimulatedTradeHistoryItem[]
  isSubmitting: boolean
  positions: SimulatedPositionItem[]
  stockDetail: StockDetailResponse | null
  tradeDate: string
}>()

const emit = defineEmits<{
  (event: 'simulate-buy', payload: { entryPrice?: number; quantity?: number; notes?: string }): void
  (event: 'simulate-sell', payload: { positionId: number }): void
}>()

const buyForm = reactive({
  entryPrice: '',
  quantity: '',
  notes: '',
})

const selectedSignalSummary = computed(() => {
  if (!props.stockDetail?.signal) {
    return null
  }

  return {
    stockCode: props.stockDetail.stockCode,
    stockName: props.stockDetail.stockName,
    strategyType: props.stockDetail.signal.strategyType,
    triggerPrice: props.stockDetail.signal.triggerPrice,
    suggestedCapital: props.stockDetail.signal.suggestedCapital,
    estimatedShares: props.stockDetail.signal.estimatedShares,
  }
})

function submitBuy() {
  emit('simulate-buy', {
    entryPrice: buyForm.entryPrice ? Number(buyForm.entryPrice) : undefined,
    quantity: buyForm.quantity ? Number(buyForm.quantity) : undefined,
    notes: buyForm.notes || undefined,
  })
}

/**
 * 统一计算持仓的风险、目标和当前偏离，供表格和卡片共用。
 */
function resolvePositionMetrics(position: SimulatedPositionItem) {
  const latest = position.latestPrice ?? position.entryPrice
  const riskPct = position.entryPrice > 0 ? ((position.entryPrice - position.stopLossPrice) / position.entryPrice) * 100 : null
  const targetPct = position.entryPrice > 0 ? ((position.targetPrice - position.entryPrice) / position.entryPrice) * 100 : null
  const distanceToStopPct = latest > 0 ? ((latest - position.stopLossPrice) / latest) * 100 : null
  const distanceToTargetPct = latest > 0 ? ((position.targetPrice - latest) / latest) * 100 : null
  const marketValue = latest * position.quantity

  return {
    latest,
    riskPct,
    targetPct,
    distanceToStopPct,
    distanceToTargetPct,
    marketValue,
  }
}

function resolvePositionTone(position: SimulatedPositionItem): 'positive' | 'warning' | 'neutral' {
  if (position.adviceStatus === 'profit' || position.adviceStatus === 'take_profit') {
    return 'positive'
  }

  if (position.adviceStatus === 'risk' || position.adviceStatus === 'timeout') {
    return 'warning'
  }

  return 'neutral'
}

function resolvePositionJudgement(position: SimulatedPositionItem): string {
  return position.adviceTitle
}
</script>

<template>
  <div class="system-layout">
    <article class="panel-card">
      <div class="panel-head">
        <div>
          <p class="card-label">模拟买入</p>
          <h3>先练流程，再谈实盘</h3>
        </div>
      </div>

      <div v-if="selectedSignalSummary" class="detail-grid">
        <div><span>当前选中股票</span><strong>{{ selectedSignalSummary.stockCode }} {{ selectedSignalSummary.stockName }}</strong></div>
        <div><span>信号类型</span><strong>{{ selectedSignalSummary.strategyType }}</strong></div>
        <div><span>建议触发价</span><strong>{{ formatNumber(selectedSignalSummary.triggerPrice) }}</strong></div>
        <div><span>系统预估股数</span><strong>{{ selectedSignalSummary.estimatedShares }}</strong></div>
        <div><span>系统建议投入</span><strong>{{ formatNumber(selectedSignalSummary.suggestedCapital) }}</strong></div>
        <div><span>交易日</span><strong>{{ tradeDate }}</strong></div>
      </div>
      <div v-else class="empty-state">先在候选池、信号列表或个股详情里选中一只股票，再来做模拟买入。</div>

      <div class="toolbar">
        <label class="field">
          <span>买入价</span>
          <input v-model="buyForm.entryPrice" type="number" step="0.01" placeholder="留空则用触发价">
        </label>
        <label class="field">
          <span>股数</span>
          <input v-model="buyForm.quantity" type="number" step="100" placeholder="留空则用系统预估">
        </label>
        <label class="field search-field">
          <span>备注</span>
          <input v-model="buyForm.notes" type="text" placeholder="例如：只做练手，不追高">
        </label>
        <button class="minor-button" :disabled="!selectedSignalSummary || isSubmitting" @click="submitBuy">
          {{ isSubmitting ? '正在提交...' : '创建模拟买入' }}
        </button>
      </div>
    </article>

    <article class="panel-card">
      <div class="panel-head">
        <div>
          <p class="card-label">当前持仓</p>
          <h3>只看还没结束的模拟仓位</h3>
        </div>
      </div>

      <div v-if="positions.length" class="table-shell">
        <table class="data-table positions-table">
          <thead>
            <tr>
              <th>持仓</th>
              <th>价格状态</th>
              <th>盈亏表现</th>
              <th>风险计划</th>
              <th>仓位规模</th>
              <th>当前判断</th>
              <th>操作</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="position in positions" :key="position.id">
              <td>
                <div class="candidate-cell-main">
                  <strong>{{ position.stockName }}</strong>
                  <span>{{ position.stockCode }}{{ position.industryName ? ` / ${position.industryName}` : '' }}</span>
                  <small>{{ position.strategyType }} / 开仓 {{ position.tradeDate }} / 已持有 {{ position.heldDays }} 天</small>
                </div>
              </td>
              <td>
                <div class="candidate-plan-cell">
                  <strong>买 {{ formatNumber(position.entryPrice) }} / 新 {{ formatNumber(position.latestPrice) }}</strong>
                  <span>市值 {{ formatNumber(resolvePositionMetrics(position).marketValue, 0) }}</span>
                </div>
              </td>
              <td>
                <div class="candidate-risk-cell">
                  <strong :class="resolvePositionTone(position) === 'positive' ? 'text-up' : resolvePositionTone(position) === 'warning' ? 'text-down' : ''">
                    {{ formatNumber(position.floatingProfitAmount, 0) }}
                  </strong>
                  <span>收益率 {{ formatPercent(position.floatingProfitPct) }}</span>
                  <small>{{ position.latestTradeDate ? `更新于 ${position.latestTradeDate}` : '暂无最新价日期' }}</small>
                </div>
              </td>
              <td>
                <div class="candidate-plan-cell">
                  <strong>止损 {{ formatNumber(position.stopLossPrice) }} / 目标 {{ formatNumber(position.targetPrice) }}</strong>
                  <span>
                    风险 {{ formatPercent(resolvePositionMetrics(position).riskPct) }} / 空间 {{ formatPercent(resolvePositionMetrics(position).targetPct) }}
                  </span>
                  <small>
                    离止损 {{ formatPercent(resolvePositionMetrics(position).distanceToStopPct) }} / 离目标 {{ formatPercent(resolvePositionMetrics(position).distanceToTargetPct) }}
                  </small>
                </div>
              </td>
              <td>
                <div class="candidate-plan-cell">
                  <strong>{{ position.quantity }} 股</strong>
                  <span>投入 {{ formatNumber(position.investedCapital, 0) }}</span>
                  <small>{{ position.notes ?? '无备注' }}</small>
                </div>
              </td>
              <td>
                <div class="candidate-status-cell">
                  <span :class="['pill', resolvePositionTone(position)]">
                    {{ position.adviceTitle }}
                  </span>
                  <small>{{ position.adviceText }}</small>
                  <small v-if="position.adviceTags.length">{{ position.adviceTags.join(' / ') }}</small>
                </div>
              </td>
              <td>
                <button class="minor-button light" :disabled="isSubmitting" @click="emit('simulate-sell', { positionId: position.id })">
                  按最新价卖出
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <div v-else class="empty-state">当前还没有持有中的模拟仓位。</div>
    </article>

    <article class="panel-card">
      <div class="panel-head">
        <div>
          <p class="card-label">历史流水</p>
          <h3>把每一次买入和卖出都记下来</h3>
        </div>
      </div>

      <div v-if="history.length" class="table-shell">
        <table class="data-table">
          <thead>
            <tr>
              <th>时间</th>
              <th>动作</th>
              <th>代码</th>
              <th>名称</th>
              <th>交易日</th>
              <th>价格</th>
              <th>股数</th>
              <th>金额</th>
              <th>摘要</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="item in history" :key="item.id">
              <td>{{ formatDateTime(item.createdAtUtc) }}</td>
              <td>{{ item.actionType }}</td>
              <td>{{ item.stockCode }}</td>
              <td>{{ item.stockName }}</td>
              <td>{{ item.tradeDate }}</td>
              <td>{{ formatNumber(item.price) }}</td>
              <td>{{ item.quantity }}</td>
              <td>{{ formatNumber(item.amount) }}</td>
              <td>{{ item.summary }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <div v-else class="empty-state">还没有历史交易流水。</div>
    </article>
  </div>
</template>

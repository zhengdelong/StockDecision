<script setup lang="ts">
import { reactive } from 'vue'
import type { BacktestRunDetail, BacktestRunListItem, SnapshotVersion } from '../types'
import { formatDateTime, formatNumber, formatPercent } from '../utils/formatters'

const props = defineProps<{
  isRunning: boolean
  latestTradeDate: string
  runs: BacktestRunListItem[]
  selectedRun: BacktestRunDetail | null
  snapshotVersion: SnapshotVersion
}>()

const emit = defineEmits<{
  (event: 'run', payload: { startDate: string; endDate: string; maxSignalsPerDay: number; maxHoldingDays: number }): void
  (event: 'select-run', id: number): void
}>()

const form = reactive({
  startDate: '',
  endDate: '',
  maxSignalsPerDay: 5,
  maxHoldingDays: 5,
})

function submit() {
  emit('run', {
    startDate: form.startDate,
    endDate: form.endDate || props.latestTradeDate,
    maxSignalsPerDay: form.maxSignalsPerDay,
    maxHoldingDays: form.maxHoldingDays,
  })
}

function formatExitLabel(hitStopLoss: boolean, hitTarget: boolean) {
  if (hitStopLoss) {
    return '止损退出'
  }

  if (hitTarget) {
    return '止盈退出'
  }

  return '到期退出'
}
</script>

<template>
  <div class="system-layout">
    <article class="panel-card">
      <div class="panel-head">
        <div>
          <p class="card-label">执行回测</p>
          <h3>先用历史数据验证策略，再决定是否采用</h3>
        </div>
      </div>

      <div class="toolbar">
        <label class="field">
          <span>开始日期</span>
          <input v-model="form.startDate" type="date">
        </label>
        <label class="field">
          <span>结束日期</span>
          <input v-model="form.endDate" type="date">
        </label>
        <label class="field">
          <span>每日最多信号</span>
          <input v-model="form.maxSignalsPerDay" type="number" min="1" max="20">
        </label>
        <label class="field">
          <span>最多持有天数</span>
          <input v-model="form.maxHoldingDays" type="number" min="1" max="20">
        </label>
        <button class="minor-button" :disabled="isRunning || !form.startDate" @click="submit">
          {{ isRunning ? '回测执行中...' : '开始回测' }}
        </button>
      </div>
      <p class="workspace-copy">当前回测快照版本：{{ snapshotVersion }}。建议先验证近一段区间，再逐步拉长时间范围。</p>
      <p v-if="selectedRun" class="workspace-copy">
        当前准入状态：{{ selectedRun.isApproved ? '已通过' : '未通过' }}
        <template v-if="!selectedRun.isApproved && selectedRun.failureReasons.length">
          。原因：{{ selectedRun.failureReasons.join('；') }}
        </template>
      </p>
    </article>

    <article class="panel-card">
      <div class="panel-head">
        <div>
          <p class="card-label">历史回测</p>
          <h3>最近执行过的回测记录</h3>
        </div>
      </div>

      <div v-if="runs.length" class="table-shell">
        <table class="data-table">
          <thead>
            <tr>
              <th>区间</th>
              <th>样本数</th>
              <th>胜率</th>
              <th>平均收益</th>
              <th>盈亏比</th>
              <th>最大回撤</th>
              <th>总收益</th>
              <th>基准收益</th>
              <th>准入</th>
              <th>生成时间</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="run in runs"
              :key="run.id"
              :class="{ selected: selectedRun?.id === run.id }"
              @click="emit('select-run', run.id)"
            >
              <td>{{ run.startDate }} ~ {{ run.endDate }}</td>
              <td>{{ run.sampleTradeCount }}</td>
              <td>{{ formatPercent(run.winRatePct) }}</td>
              <td>{{ formatPercent(run.averageReturnPct) }}</td>
              <td>{{ formatNumber(run.profitLossRatio) }}</td>
              <td>{{ formatPercent(run.maxDrawdownPct) }}</td>
              <td>{{ formatPercent(run.totalReturnPct) }}</td>
              <td>{{ formatPercent(run.benchmarkReturnPct) }}</td>
              <td>{{ run.isApproved ? '通过' : '未通过' }}</td>
              <td>{{ formatDateTime(run.createdAtUtc) }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <div v-else class="empty-state">还没有历史回测记录。</div>
    </article>

    <article class="panel-card">
      <div class="panel-head">
        <div>
          <p class="card-label">回测详情</p>
          <h3>拆开看收益、回撤和交易明细</h3>
        </div>
      </div>

      <div v-if="selectedRun" class="detail-grid">
        <div><span>策略版本</span><strong>{{ selectedRun.strategyVersion }}</strong></div>
        <div><span>快照版本</span><strong>{{ selectedRun.snapshotVersion }}</strong></div>
        <div><span>样本交易数</span><strong>{{ selectedRun.sampleTradeCount }}</strong></div>
        <div><span>胜率</span><strong>{{ formatPercent(selectedRun.winRatePct) }}</strong></div>
        <div><span>平均收益</span><strong>{{ formatPercent(selectedRun.averageReturnPct) }}</strong></div>
        <div><span>平均最大盈利</span><strong>{{ formatPercent(selectedRun.averageMaxGainPct) }}</strong></div>
        <div><span>平均最大回撤</span><strong>{{ formatPercent(selectedRun.averageMaxDrawdownPct) }}</strong></div>
        <div><span>盈亏比</span><strong>{{ formatNumber(selectedRun.profitLossRatio) }}</strong></div>
        <div><span>总收益</span><strong>{{ formatPercent(selectedRun.totalReturnPct) }}</strong></div>
        <div><span>基准收益</span><strong>{{ formatPercent(selectedRun.benchmarkReturnPct) }}</strong></div>
        <div><span>最大回撤</span><strong>{{ formatPercent(selectedRun.maxDrawdownPct) }}</strong></div>
        <div><span>数据覆盖率</span><strong>{{ formatPercent(selectedRun.dataCoveragePct) }}</strong></div>
        <div><span>跳过交易日</span><strong>{{ selectedRun.skippedTradeDays }}</strong></div>
        <div><span>年化交易次数</span><strong>{{ formatNumber(selectedRun.annualTradeCount) }}</strong></div>
        <div><span>连续亏损上限</span><strong>{{ selectedRun.maxConsecutiveLosses }}</strong></div>
        <div><span>准入状态</span><strong>{{ selectedRun.isApproved ? '已通过' : '未通过' }}</strong></div>
        <div><span>平均持有天数</span><strong>{{ formatNumber(selectedRun.averageHoldingDays) }}</strong></div>
        <div><span>生成时间</span><strong>{{ formatDateTime(selectedRun.createdAtUtc) }}</strong></div>
      </div>
      <div v-else class="empty-state">先执行一次回测，或从上面的历史回测里打开一条结果。</div>

      <div v-if="selectedRun?.trades?.length" class="table-shell" style="margin-top: 16px;">
        <table class="data-table">
          <thead>
            <tr>
              <th>日期</th>
              <th>代码</th>
              <th>名称</th>
              <th>策略</th>
              <th>入场价</th>
              <th>出场价</th>
              <th>股数</th>
              <th>投入资金</th>
              <th>收益额</th>
              <th>收益率</th>
              <th>最大盈利</th>
              <th>最大回撤</th>
              <th>持有天数</th>
              <th>退出类型</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="trade in selectedRun.trades" :key="`${trade.tradeDate}-${trade.stockCode}`">
              <td>{{ trade.tradeDate }}</td>
              <td>{{ trade.stockCode }}</td>
              <td>{{ trade.stockName }}</td>
              <td>{{ trade.strategyType }}</td>
              <td>{{ formatNumber(trade.entryPrice) }}</td>
              <td>{{ formatNumber(trade.exitPrice) }}</td>
              <td>{{ trade.quantity }}</td>
              <td>{{ formatNumber(trade.investedCapital) }}</td>
              <td>{{ formatNumber(trade.profitAmount) }}</td>
              <td>{{ formatPercent(trade.returnPct) }}</td>
              <td>{{ formatPercent(trade.maxGainPct) }}</td>
              <td>{{ formatPercent(trade.maxDrawdownPct) }}</td>
              <td>{{ trade.maxHoldingDays }}</td>
              <td>{{ formatExitLabel(trade.hitStopLoss, trade.hitTarget) }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </article>
  </div>
</template>

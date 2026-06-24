<script setup lang="ts">
import type { SnapshotVersion, TaskCenterOverviewResponse } from '../types'
import { formatDate } from '../utils/formatters'

defineProps<{
  isLoading: boolean
  isTriggering: boolean
  overview: TaskCenterOverviewResponse | null
}>()

const emit = defineEmits<{
  (event: 'trigger-snapshot', snapshotVersion: SnapshotVersion): void
}>()

function formatCollectorScope(scope: string): string {
  switch (scope) {
    case 'sync-daily-stocks': return '股票快照采集'
    case 'sync-daily-bars': return '股票日线采集'
    case 'sync-daily-indices': return '指数日线采集'
    case 'sync-daily-industries': return '行业强度采集'
    case 'sync-end-of-day-final-bars': return '收盘正式版采集'
    case 'sync-end-of-day-retry-1600': return '收盘补拉 16:00'
    case 'sync-end-of-day-retry-1630': return '收盘补拉 16:30'
    case 'sync-end-of-day-retry-1700': return '收盘补拉 17:00'
    case 'sync-end-of-day-retry-1800': return '收盘补拉 18:00'
    case 'sync-financials': return '财务采集'
    default: return scope
  }
}
</script>

<template>
  <div class="system-layout">
    <article class="panel-card">
      <div class="panel-head">
        <div>
          <p class="card-label">任务快照</p>
          <h3>当前执行上下文</h3>
        </div>
        <span :class="['pill', overview?.isSignalEligible ? 'positive' : 'warning']">
          {{ overview?.isSignalEligible ? '允许出信号' : '仅观察' }}
        </span>
      </div>

      <div v-if="overview" class="detail-grid">
        <div><span>交易日</span><strong>{{ formatDate(overview.tradeDate) }}</strong></div>
        <div><span>当前版本</span><strong>{{ overview.snapshotVersionName }}</strong></div>
        <div><span>最近采集</span><strong>{{ formatDate(overview.latestSuccessfulIngestionAtUtc) }}</strong></div>
        <div><span>最近领域同步</span><strong>{{ formatDate(overview.domainSyncStatus.latestSuccessfulDomainSyncAtUtc) }}</strong></div>
        <div><span>正式版生成</span><strong>{{ formatDate(overview.domainSyncStatus.latestSuccessfulEndOfDayFinalAtUtc) }}</strong></div>
        <div><span>市场环境</span><strong>{{ overview.marketRegime }}</strong></div>
        <div><span>候选池</span><strong>{{ overview.candidateCount }}</strong></div>
        <div><span>交易信号</span><strong>{{ overview.signalCount }}</strong></div>
      </div>
      <div v-else class="empty-state">任务概览暂时不可用。</div>

      <div v-if="overview?.statusMessages?.length" class="task-status-list">
        <p v-for="message in overview.statusMessages" :key="message" class="task-status-item">
          {{ message }}
        </p>
      </div>

      <div class="task-actions">
        <button class="minor-button" :disabled="isTriggering" @click="emit('trigger-snapshot', 'end_of_day_final')">
          {{ isTriggering ? '正在生成...' : '立即生成正式结果' }}
        </button>
      </div>
    </article>

    <article class="panel-card">
      <div class="panel-head">
        <div>
          <p class="card-label">领域同步</p>
          <h3>原始层到领域层的新鲜度</h3>
        </div>
        <span :class="['pill', overview?.domainSyncStatus.requiresSync ? 'warning' : 'positive']">
          {{ overview?.domainSyncStatus.requiresSync ? '待同步' : '已同步' }}
        </span>
      </div>

      <div v-if="overview" class="detail-grid">
        <div><span>原始交易日</span><strong>{{ formatDate(overview.domainSyncStatus.latestRawTradeDate) }}</strong></div>
        <div><span>已导入交易日</span><strong>{{ formatDate(overview.domainSyncStatus.latestImportedTradeDate) }}</strong></div>
        <div><span>原始财报期</span><strong>{{ formatDate(overview.domainSyncStatus.latestRawFinancialReportDate) }}</strong></div>
        <div><span>已导入财报期</span><strong>{{ formatDate(overview.domainSyncStatus.latestImportedFinancialReportDate) }}</strong></div>
        <div><span>交易日差异</span><strong>{{ overview.domainSyncStatus.hasTradeDateGap ? '是' : '否' }}</strong></div>
        <div><span>财报期差异</span><strong>{{ overview.domainSyncStatus.hasFinancialGap ? '是' : '否' }}</strong></div>
      </div>
    </article>

    <article class="panel-card">
      <div class="panel-head">
        <div>
          <p class="card-label">定时任务</p>
          <h3>自动执行任务</h3>
        </div>
      </div>

      <div v-if="overview?.schedules?.length" class="table-shell">
        <table class="data-table">
          <thead>
            <tr>
              <th>任务</th>
              <th>调度时间</th>
              <th>说明</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="schedule in overview.schedules" :key="schedule.name">
              <td>{{ schedule.name }}</td>
              <td>{{ schedule.schedule }}</td>
              <td>{{ schedule.description }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <div v-else class="empty-state">暂无定时任务元数据。</div>
    </article>

    <article class="panel-card">
      <div class="panel-head">
        <div>
          <p class="card-label">采集记录</p>
          <h3>最近原始层采集记录</h3>
        </div>
      </div>

      <div v-if="overview?.collectorRuns?.length" class="table-shell">
        <table class="data-table">
          <thead>
            <tr>
              <th>范围</th>
              <th>创建时间</th>
              <th>完成状态</th>
              <th>允许出信号</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="run in overview.collectorRuns" :key="`${run.targetScope}-${run.createdAtUtc}`">
              <td>{{ formatCollectorScope(run.targetScope) }}</td>
              <td>{{ formatDate(run.createdAtUtc) }}</td>
              <td>
                <span :class="['pill', run.isComplete ? 'positive' : 'warning']">
                  {{ run.isComplete ? '完成' : '执行中' }}
                </span>
              </td>
              <td>
                <span :class="['pill', run.isSignalEligible ? 'positive' : 'neutral']">
                  {{ run.isSignalEligible ? '是' : '否' }}
                </span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <div v-else class="empty-state">
        {{ isLoading ? '正在加载采集记录...' : '暂无采集记录。' }}
      </div>
    </article>

    <article class="panel-card">
      <div class="panel-head">
        <div>
          <p class="card-label">领域同步记录</p>
          <h3>最近领域层刷新记录</h3>
        </div>
      </div>

      <div v-if="overview?.domainSyncRuns?.length" class="table-shell">
        <table class="data-table">
          <thead>
            <tr>
              <th>触发方式</th>
              <th>版本</th>
              <th>状态</th>
              <th>交易日</th>
              <th>财报期</th>
              <th>开始时间</th>
              <th>摘要</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="run in overview.domainSyncRuns" :key="`${run.jobName}-${run.startedAtUtc}`">
              <td>{{ run.triggerKind }}</td>
              <td>{{ run.snapshotVersion }}</td>
              <td>
                <span :class="['pill', run.status === 'Succeeded' || run.status === '成功' ? 'positive' : 'warning']">
                  {{ run.status }}
                </span>
              </td>
              <td>{{ formatDate(run.effectiveTradeDate) }}</td>
              <td>{{ formatDate(run.financialReportDate) }}</td>
              <td>{{ formatDate(run.startedAtUtc) }}</td>
              <td>{{ run.summary }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <div v-else class="empty-state">
        {{ isLoading ? '正在加载领域同步记录...' : '暂无领域同步记录。' }}
      </div>
    </article>
  </div>
</template>

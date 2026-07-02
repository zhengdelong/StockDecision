<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import AppSidebar, { type ViewMode } from './components/AppSidebar.vue'
import BacktestView from './components/BacktestView.vue'
import CandidatesView from './components/CandidatesView.vue'
import DashboardView from './components/DashboardView.vue'
import FinancialView from './components/FinancialView.vue'
import FundFlowView from './components/FundFlowView.vue'
import IndustryView from './components/IndustryView.vue'
import LearningView from './components/LearningView.vue'
import OverviewCards from './components/OverviewCards.vue'
import PositionsView from './components/PositionsView.vue'
import SignalsView from './components/SignalsView.vue'
import StockDetailPanel from './components/StockDetailPanel.vue'
import StrategyView from './components/StrategyView.vue'
import SystemView from './components/SystemView.vue'
import TaskCenterView from './components/TaskCenterView.vue'
import { useSignalDesk } from './composables/useSignalDesk'
import { formatDate } from './utils/formatters'

const activeView = ref<ViewMode>('dashboard')
const desk = useSignalDesk()

const selectedStockCode = computed(() => desk.stockDetail.value?.stockCode ?? '')
const regimeTone = computed(() => (desk.dashboard.value?.isSignalEligible ? 'positive' : 'warning'))

const currentViewTitle = computed(() => {
  switch (activeView.value) {
    case 'candidates': return '候选池审阅'
    case 'signals': return '交易信号清单'
    case 'positions': return '模拟交易与持仓'
    case 'industries': return '行业强度看板'
    case 'fundFlows': return '资金流向'
    case 'financials': return '全市场股票评分'
    case 'strategy': return '策略解释'
    case 'learning': return '学习复盘'
    case 'backtests': return '回测验证'
    case 'tasks': return '任务中心'
    case 'system': return '系统状态'
    default: return '收盘决策工作台'
  }
})

const currentViewSummary = computed(() => {
  switch (activeView.value) {
    case 'candidates': return '按交易日、评分和可交易状态筛选候选股，并快速切换到个股详情。'
    case 'signals': return '聚焦可执行信号、建议仓位和风控价位，便于当天复核。'
    case 'positions': return '先做模拟买卖，再跟踪盈亏、止损止盈和历史流水。'
    case 'industries': return '横向比较行业热度、候选分布和信号集中度。'
    case 'fundFlows': return '跟踪行业和个股主力资金流向，识别资金异动与候选池交叉机会。'
    case 'financials': return '查看全市场可评分股票的综合打分，并结合 PE、PB、ROE 和增长指标做横向比较。'
    case 'strategy': return '把评分逻辑、环境过滤和执行规则拆开讲清楚，便于学习。'
    case 'learning': return '把每次交易记录成复盘素材，沉淀成以后可重复使用的规则。'
    case 'backtests': return '用历史信号做同步回测，查看收益、回撤、盈亏比和样本明细。'
    case 'tasks': return '查看采集、同步和快照任务的最近执行情况，确认今天的数据是否到位。'
    case 'system': return '查看 API 健康状态、策略版本、运行模式和当前引用文档。'
    default: return '把收盘后的选股、信号、模拟交易、解释、回测和复盘放在同一个工作台里。'
  }
})

const overviewHiddenViews = ['system', 'tasks', 'financials', 'strategy', 'learning', 'backtests', 'positions', 'fundFlows']
const showsOverviewCards = computed(() => !overviewHiddenViews.includes(activeView.value))
const showsDetailPanel = computed(() => ['dashboard', 'candidates', 'signals', 'financials', 'learning', 'fundFlows'].includes(activeView.value))
const usesDetailDrawer = computed(() => ['candidates', 'signals', 'financials', 'fundFlows'].includes(activeView.value))
const showsDockedDetailPanel = computed(() => showsDetailPanel.value && !usesDetailDrawer.value)
const showsDetailDrawer = computed(() => showsDetailPanel.value && usesDetailDrawer.value)
const isDetailDrawerOpen = ref(false)

const detailSummary = computed(() => {
  const detail = desk.stockDetail.value
  if (!detail) {
    return null
  }

  return {
    stockCode: detail.stockCode,
    stockName: detail.stockName,
    industryName: detail.industryName ?? '未分类行业',
    totalScore: detail.candidate?.totalScore ?? detail.signal?.totalScore ?? null,
    tradeDate: detail.tradeDate,
    snapshotVersionName: detail.snapshotVersionName,
  }
})

function handleSelectStock(stockCode: string) {
  desk.selectStock(stockCode)
  if (usesDetailDrawer.value) {
    isDetailDrawerOpen.value = true
  }
}

function openDetailDrawer() {
  if (detailSummary.value || desk.isDetailLoading.value) {
    isDetailDrawerOpen.value = true
  }
}

function closeDetailDrawer() {
  isDetailDrawerOpen.value = false
}

watch(activeView, (view) => {
  if (!['candidates', 'signals', 'financials', 'fundFlows'].includes(view)) {
    isDetailDrawerOpen.value = false
  }
})

watch(
  () => desk.stockDetail.value?.stockCode,
  (stockCode) => {
    if (!stockCode && !desk.isDetailLoading.value) {
      isDetailDrawerOpen.value = false
    }
  },
)
</script>

<template>
  <main class="app-shell">
    <div class="layout-grid">
      <AppSidebar
        :active-view="activeView"
        :candidate-count="desk.dashboard.value?.candidateCount ?? 0"
        :signal-count="desk.dashboard.value?.signalCount ?? 0"
        :trade-date="formatDate(desk.dashboard.value?.tradeDate)"
        @change-view="activeView = $event"
      />

      <section class="main-stage">
        <section class="workspace-head">
          <div class="workspace-title">
            <p class="eyebrow">A 股收盘决策流程</p>
            <h2>{{ currentViewTitle }}</h2>
            <p class="workspace-copy">{{ currentViewSummary }}</p>
          </div>

          <div class="workspace-actions">
            <button class="action-button primary" :disabled="desk.isLoading.value || desk.isTriggeringSnapshot.value" @click="desk.loadHomeData">
              {{ desk.isLoading.value ? '正在刷新...' : '刷新快照' }}
            </button>
          </div>
        </section>

        <OverviewCards
          v-if="showsOverviewCards"
          :dashboard="desk.dashboard.value"
          :regime-tone="regimeTone"
          :top-candidates="desk.topCandidates.value"
          @select-stock="handleSelectStock"
        />

        <section v-if="desk.errorMessage.value" class="error-banner">
          {{ desk.errorMessage.value }}
        </section>

        <section :class="['workspace', { 'single-panel': !showsDockedDetailPanel }]">
          <section class="primary-panel">
            <section v-if="showsDetailDrawer" class="detail-drawer-toolbar">
              <div class="detail-drawer-summary">
                <p class="card-label">个股详情交互</p>
                <template v-if="detailSummary">
                  <strong>{{ detailSummary.stockName }} · {{ detailSummary.stockCode }}</strong>
                  <span>
                    {{ detailSummary.industryName }}
                    <template v-if="detailSummary.totalScore != null"> · 总分 {{ detailSummary.totalScore.toFixed(1) }}</template>
                    · {{ detailSummary.tradeDate }} · {{ detailSummary.snapshotVersionName }}
                  </span>
                </template>
                <template v-else>
                  <strong>列表优先，详情按需展开</strong>
                  <span>点击候选池或交易信号中的任意一行，在右侧抽屉查看完整个股详情。</span>
                </template>
              </div>
              <div class="detail-drawer-actions">
                <button
                  class="minor-button light"
                  :disabled="(!detailSummary && !desk.isDetailLoading.value) || desk.isLoading.value"
                  @click="isDetailDrawerOpen ? closeDetailDrawer() : openDetailDrawer()"
                >
                  {{ isDetailDrawerOpen ? '收起个股详情' : '展开个股详情' }}
                </button>
              </div>
            </section>

            <DashboardView
              v-if="activeView === 'dashboard'"
              :top-candidates="desk.topCandidates.value"
              @select-stock="handleSelectStock"
            />

            <CandidatesView
              v-else-if="activeView === 'candidates'"
              :candidate-page-index="desk.candidatePageIndex.value"
              :candidates="desk.candidates.value"
              :is-loading="desk.isLoading.value"
              :min-score="desk.minScore.value"
              :only-tradable="desk.onlyTradable.value"
              :search-text="desk.searchText.value"
              :selected-stock-code="selectedStockCode"
              :selected-trade-date="desk.selectedTradeDate.value"
              :sort-mode="desk.candidateSortMode.value"
              :total-count="desk.candidateTotalCount.value"
              :total-pages="desk.totalCandidatePages.value"
              @apply="desk.applyFilters"
              @move-page="desk.moveCandidatePage"
              @reset-date="desk.resetTradeDate"
              @select-stock="handleSelectStock"
              @update:min-score="desk.minScore.value = $event"
              @update:only-tradable="desk.onlyTradable.value = $event"
              @update:search-text="desk.searchText.value = $event"
              @update:selected-trade-date="desk.selectedTradeDate.value = $event"
              @update:sort-mode="desk.candidateSortMode.value = $event"
            />

            <SignalsView
              v-else-if="activeView === 'signals'"
              :is-loading="desk.isLoading.value"
              :search-text="desk.searchText.value"
              :selected-stock-code="selectedStockCode"
              :selected-trade-date="desk.selectedTradeDate.value"
              :signal-page-index="desk.signalPageIndex.value"
              :signals="desk.signals.value"
              :sort-mode="desk.signalSortMode.value"
              :total-count="desk.signalTotalCount.value"
              :total-pages="desk.totalSignalPages.value"
              @apply="desk.applyFilters"
              @move-page="desk.moveSignalPage"
              @reset-date="desk.resetTradeDate"
              @select-stock="handleSelectStock"
              @update:search-text="desk.searchText.value = $event"
              @update:selected-trade-date="desk.selectedTradeDate.value = $event"
              @update:sort-mode="desk.signalSortMode.value = $event"
            />

            <PositionsView
              v-else-if="activeView === 'positions'"
              :history="desk.positionHistory.value"
              :is-submitting="desk.isSubmittingPosition.value"
              :positions="desk.positions.value"
              :stock-detail="desk.stockDetail.value"
              :trade-date="desk.selectedTradeDate.value"
              @simulate-buy="desk.createSimulatedBuy"
              @simulate-sell="desk.closePosition($event.positionId)"
            />

            <IndustryView
              v-else-if="activeView === 'industries'"
              :industries="desk.industries.value"
              :industry-page-index="desk.industryPageIndex.value"
              :is-loading="desk.isLoading.value"
              :search-text="desk.searchText.value"
              :selected-trade-date="desk.selectedTradeDate.value"
              :sort-mode="desk.industrySortMode.value"
              :total-count="desk.industryTotalCount.value"
              :total-pages="desk.totalIndustryPages.value"
              @apply="desk.applyFilters"
              @move-page="desk.moveIndustryPage"
              @reset-date="desk.resetTradeDate"
              @update:search-text="desk.searchText.value = $event"
              @update:selected-trade-date="desk.selectedTradeDate.value = $event"
              @update:sort-mode="desk.industrySortMode.value = $event"
            />

            <FundFlowView
              v-else-if="activeView === 'fundFlows'"
              :direction="desk.fundFlowDirection.value"
              :industry-fund-flow-page-index="desk.industryFundFlowPageIndex.value"
              :industry-fund-flows="desk.industryFundFlows.value"
              :industry-sort-mode="desk.industryFundFlowSortMode.value"
              :industry-total-count="desk.industryFundFlowTotalCount.value"
              :industry-total-pages="desk.totalIndustryFundFlowPages.value"
              :is-loading="desk.isLoading.value"
              :search-text="desk.searchText.value"
              :selected-stock-code="selectedStockCode"
              :selected-trade-date="desk.selectedTradeDate.value"
              :stock-fund-flow-page-index="desk.stockFundFlowPageIndex.value"
              :stock-fund-flows="desk.stockFundFlows.value"
              :stock-sort-mode="desk.stockFundFlowSortMode.value"
              :stock-total-count="desk.stockFundFlowTotalCount.value"
              :stock-total-pages="desk.totalStockFundFlowPages.value"
              @apply="desk.applyFilters"
              @move-industry-page="desk.moveIndustryFundFlowPage"
              @move-stock-page="desk.moveStockFundFlowPage"
              @reset-date="desk.resetTradeDate"
              @select-stock="handleSelectStock"
              @update:direction="desk.fundFlowDirection.value = $event"
              @update:industry-sort-mode="desk.industryFundFlowSortMode.value = $event"
              @update:search-text="desk.searchText.value = $event"
              @update:selected-trade-date="desk.selectedTradeDate.value = $event"
              @update:stock-sort-mode="desk.stockFundFlowSortMode.value = $event"
            />

            <FinancialView
              v-else-if="activeView === 'financials'"
              :financial-page-index="desk.financialPageIndex.value"
              :financials="desk.financials.value"
              :is-loading="desk.isLoading.value"
              :min-roe="desk.minRoe.value"
              :positive-growth-only="desk.positiveGrowthOnly.value"
              :search-text="desk.searchText.value"
              :selected-stock-code="selectedStockCode"
              :sort-mode="desk.financialSortMode.value"
              :total-count="desk.financialTotalCount.value"
              :total-pages="desk.totalFinancialPages.value"
              @apply="desk.applyFilters"
              @move-page="desk.moveFinancialPage"
              @select-stock="handleSelectStock"
              @update:min-roe="desk.minRoe.value = $event"
              @update:positive-growth-only="desk.positiveGrowthOnly.value = $event"
              @update:search-text="desk.searchText.value = $event"
              @update:sort-mode="desk.financialSortMode.value = $event"
            />

            <StrategyView
              v-else-if="activeView === 'strategy'"
              :explanation="desk.strategyExplanation.value"
            />

            <LearningView
              v-else-if="activeView === 'learning'"
              :is-submitting="desk.isSubmittingLearningReview.value"
              :overview="desk.learningOverview.value"
              :stock-detail="desk.stockDetail.value"
              :trade-date="desk.selectedTradeDate.value"
              @save-review="desk.createLearningReview"
            />

            <BacktestView
              v-else-if="activeView === 'backtests'"
              :is-running="desk.isRunningBacktest.value"
              :latest-trade-date="desk.selectedTradeDate.value"
              :runs="desk.backtestRuns.value"
              :selected-run="desk.selectedBacktestRun.value"
              :snapshot-version="desk.selectedSnapshotVersion.value"
              @run="desk.executeBacktest"
              @select-run="desk.selectBacktestRun"
            />

            <TaskCenterView
              v-else-if="activeView === 'tasks'"
              :is-loading="desk.isLoading.value"
              :is-triggering="desk.isTriggeringSnapshot.value"
              :overview="desk.taskCenterOverview.value"
              @trigger-snapshot="desk.triggerSnapshot"
            />

            <SystemView
              v-else
              :about="desk.systemAbout.value"
              :health="desk.systemHealth.value"
              :is-loading="desk.isLoading.value"
            />
          </section>

          <StockDetailPanel
            v-if="showsDockedDetailPanel"
            :is-detail-loading="desk.isDetailLoading.value"
            :is-loading="desk.isLoading.value"
            :stock-detail="desk.stockDetail.value"
          />
        </section>

        <transition name="detail-drawer">
          <div v-if="showsDetailDrawer && isDetailDrawerOpen" class="detail-drawer-backdrop" @click="closeDetailDrawer">
            <section class="detail-drawer-sheet" @click.stop>
              <header class="detail-drawer-head">
                <div>
                  <p class="card-label">个股详情</p>
                  <h3 v-if="detailSummary">{{ detailSummary.stockName }} · {{ detailSummary.stockCode }}</h3>
                  <p v-if="detailSummary" class="muted">
                    {{ detailSummary.industryName }}
                    <template v-if="detailSummary.totalScore != null"> · 总分 {{ detailSummary.totalScore.toFixed(1) }}</template>
                    · {{ detailSummary.tradeDate }} · {{ detailSummary.snapshotVersionName }}
                  </p>
                  <p v-else class="muted">正在加载个股详情...</p>
                </div>
                <button class="minor-button light" @click="closeDetailDrawer">收起</button>
              </header>

              <StockDetailPanel
                :is-detail-loading="desk.isDetailLoading.value"
                :is-loading="desk.isLoading.value"
                :stock-detail="desk.stockDetail.value"
              />
            </section>
          </div>
        </transition>
      </section>
    </div>
  </main>
</template>

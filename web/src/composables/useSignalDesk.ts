import { computed, onMounted, ref } from 'vue'
import {
  fetchAllPositions,
  fetchBacktestOverview,
  fetchBacktestRunDetail,
  fetchBacktestRuns,
  fetchCandidates,
  fetchDashboard,
  fetchFinancials,
  fetchIndustryFundFlows,
  fetchIndustries,
  fetchLearningReviews,
  fetchPositionHistory,
  fetchPositions,
  fetchSignals,
  fetchStockFundFlows,
  fetchStockDetail,
  fetchStrategyExplanation,
  fetchSystemAbout,
  fetchSystemHealth,
  fetchTaskCenterOverview,
  runBacktest,
  saveLearningReview,
  simulateBuy,
  simulateSell,
  triggerDomainSync,
} from '../api'
import type {
  BacktestOverviewResponse,
  BacktestRunDetail,
  BacktestRunListItem,
  CandidateItem,
  CandidateQuery,
  DashboardResponse,
  FinancialItem,
  FinancialQuery,
  IndustryFundFlowItem,
  IndustryItem,
  IndustryQuery,
  LearningReviewOverviewResponse,
  PagedResponse,
  SignalItem,
  SignalQuery,
  SimulatedPositionItem,
  SimulatedTradeHistoryItem,
  SnapshotVersion,
  StockFundFlowItem,
  StockDetailResponse,
  StrategyExplanationResponse,
  SystemHealthResponse,
  SystemSummaryResponse,
  TaskCenterOverviewResponse,
} from '../types'

export type CandidateSortMode = 'score' | 'rr' | 'close'
export type SignalSortMode = 'score' | 'rr' | 'capital'
export type IndustrySortMode = 'strength' | 'rank' | 'candidates' | 'signals'
export type FinancialSortMode = 'score' | 'roe' | 'revenue' | 'profit' | 'marketCap'
export type StockFundFlowSortMode = 'percentile' | 'main' | 'mainPct' | 'superLargePct' | 'score'
export type IndustryFundFlowSortMode = 'rank' | 'percentile' | 'main' | 'candidates' | 'signals'
export type FundFlowDirection = 'all' | 'inflow' | 'outflow'

const pageSize = 10

export function useSignalDesk() {
  const dashboard = ref<DashboardResponse | null>(null)
  const candidatePage = ref<PagedResponse<CandidateItem> | null>(null)
  const signalPage = ref<PagedResponse<SignalItem> | null>(null)
  const industryPage = ref<PagedResponse<IndustryItem> | null>(null)
  const stockFundFlowPage = ref<PagedResponse<StockFundFlowItem> | null>(null)
  const industryFundFlowPage = ref<PagedResponse<IndustryFundFlowItem> | null>(null)
  const financialPage = ref<PagedResponse<FinancialItem> | null>(null)
  const stockDetail = ref<StockDetailResponse | null>(null)
  const taskCenterOverview = ref<TaskCenterOverviewResponse | null>(null)
  const strategyExplanation = ref<StrategyExplanationResponse | null>(null)
  const backtestOverview = ref<BacktestOverviewResponse | null>(null)
  const backtestRuns = ref<BacktestRunListItem[]>([])
  const selectedBacktestRun = ref<BacktestRunDetail | null>(null)
  const positions = ref<SimulatedPositionItem[]>([])
  const allPositions = ref<SimulatedPositionItem[]>([])
  const positionHistory = ref<SimulatedTradeHistoryItem[]>([])
  const learningOverview = ref<LearningReviewOverviewResponse | null>(null)
  const systemHealth = ref<SystemHealthResponse | null>(null)
  const systemAbout = ref<SystemSummaryResponse | null>(null)
  const isLoading = ref(false)
  const isDetailLoading = ref(false)
  const isTriggeringSnapshot = ref(false)
  const isSubmittingPosition = ref(false)
  const isSubmittingLearningReview = ref(false)
  const isRunningBacktest = ref(false)
  const errorMessage = ref('')
  const minScore = ref(60)
  const minRoe = ref(0)
  const onlyTradable = ref(false)
  const positiveGrowthOnly = ref(false)
  const searchText = ref('')
  const selectedTradeDate = ref('')
  const selectedSnapshotVersion = ref<SnapshotVersion>('end_of_day_final')
  const candidateSortMode = ref<CandidateSortMode>('score')
  const signalSortMode = ref<SignalSortMode>('score')
  const industrySortMode = ref<IndustrySortMode>('strength')
  const financialSortMode = ref<FinancialSortMode>('score')
  const stockFundFlowSortMode = ref<StockFundFlowSortMode>('percentile')
  const industryFundFlowSortMode = ref<IndustryFundFlowSortMode>('rank')
  const fundFlowDirection = ref<FundFlowDirection>('all')
  const candidatePageIndex = ref(1)
  const signalPageIndex = ref(1)
  const industryPageIndex = ref(1)
  const stockFundFlowPageIndex = ref(1)
  const industryFundFlowPageIndex = ref(1)
  const financialPageIndex = ref(1)

  const candidates = computed(() => candidatePage.value?.items ?? [])
  const signals = computed(() => signalPage.value?.items ?? [])
  const industries = computed(() => industryPage.value?.items ?? [])
  const stockFundFlows = computed(() => stockFundFlowPage.value?.items ?? [])
  const industryFundFlows = computed(() => industryFundFlowPage.value?.items ?? [])
  const financials = computed(() => financialPage.value?.items ?? [])
  const candidateTotalCount = computed(() => candidatePage.value?.totalCount ?? 0)
  const signalTotalCount = computed(() => signalPage.value?.totalCount ?? 0)
  const industryTotalCount = computed(() => industryPage.value?.totalCount ?? 0)
  const stockFundFlowTotalCount = computed(() => stockFundFlowPage.value?.totalCount ?? 0)
  const industryFundFlowTotalCount = computed(() => industryFundFlowPage.value?.totalCount ?? 0)
  const financialTotalCount = computed(() => financialPage.value?.totalCount ?? 0)
  const totalCandidatePages = computed(() => Math.max(1, Math.ceil(candidateTotalCount.value / pageSize)))
  const totalSignalPages = computed(() => Math.max(1, Math.ceil(signalTotalCount.value / pageSize)))
  const totalIndustryPages = computed(() => Math.max(1, Math.ceil(industryTotalCount.value / pageSize)))
  const totalStockFundFlowPages = computed(() => Math.max(1, Math.ceil(stockFundFlowTotalCount.value / pageSize)))
  const totalIndustryFundFlowPages = computed(() => Math.max(1, Math.ceil(industryFundFlowTotalCount.value / pageSize)))
  const totalFinancialPages = computed(() => Math.max(1, Math.ceil(financialTotalCount.value / pageSize)))
  const topCandidates = computed(() => candidates.value.slice(0, 3))

  function buildCandidateQuery(page: number): CandidateQuery {
    return {
      date: selectedTradeDate.value || undefined,
      snapshotVersion: selectedSnapshotVersion.value,
      search: searchText.value || undefined,
      minScore: minScore.value,
      onlyTradable: onlyTradable.value || undefined,
      sortBy: candidateSortMode.value,
      page,
      pageSize,
    }
  }

  function buildSignalQuery(page: number): SignalQuery {
    return {
      date: selectedTradeDate.value || undefined,
      snapshotVersion: selectedSnapshotVersion.value,
      search: searchText.value || undefined,
      sortBy: signalSortMode.value,
      page,
      pageSize,
    }
  }

  function buildIndustryQuery(page: number): IndustryQuery {
    return {
      date: selectedTradeDate.value || undefined,
      snapshotVersion: selectedSnapshotVersion.value,
      search: searchText.value || undefined,
      sortBy: industrySortMode.value,
      page,
      pageSize,
    }
  }

  function buildFinancialQuery(page: number): FinancialQuery {
    return {
      date: selectedTradeDate.value || undefined,
      snapshotVersion: selectedSnapshotVersion.value,
      search: searchText.value || undefined,
      sortBy: financialSortMode.value,
      minRoe: minRoe.value > 0 ? minRoe.value : undefined,
      positiveGrowthOnly: positiveGrowthOnly.value || undefined,
      page,
      pageSize,
    }
  }

  function buildStockFundFlowQuery(page: number) {
    return {
      date: selectedTradeDate.value || undefined,
      snapshotVersion: selectedSnapshotVersion.value,
      search: searchText.value || undefined,
      sortBy: stockFundFlowSortMode.value,
      direction: fundFlowDirection.value,
      page,
      pageSize,
    }
  }

  function buildIndustryFundFlowQuery(page: number) {
    return {
      date: selectedTradeDate.value || undefined,
      snapshotVersion: selectedSnapshotVersion.value,
      search: searchText.value || undefined,
      sortBy: industryFundFlowSortMode.value,
      direction: fundFlowDirection.value,
      page,
      pageSize,
    }
  }

  async function loadPositions() {
    const [openPositions, historyItems, fullPositions] = await Promise.all([
      fetchPositions(),
      fetchPositionHistory(),
      fetchAllPositions(),
    ])

    positions.value = openPositions
    positionHistory.value = historyItems
    allPositions.value = fullPositions
  }

  async function loadBacktests() {
    const [overview, runs] = await Promise.all([fetchBacktestOverview(), fetchBacktestRuns()])
    backtestOverview.value = overview
    backtestRuns.value = runs
    if (runs[0]) {
      selectedBacktestRun.value = await fetchBacktestRunDetail(runs[0].id)
    } else {
      selectedBacktestRun.value = null
    }
  }

  async function loadLearningOverview(stockCode?: string, stockName?: string) {
    learningOverview.value = await fetchLearningReviews(
      stockCode,
      stockName,
      selectedTradeDate.value || undefined,
      selectedSnapshotVersion.value,
    )
  }

  async function loadHomeData() {
    isLoading.value = true
    errorMessage.value = ''

    try {
      const [
        dashboardResponse,
        healthResponse,
        aboutResponse,
        taskOverviewResponse,
        strategyExplanationResponse,
      ] = await Promise.all([
        fetchDashboard(selectedSnapshotVersion.value),
        fetchSystemHealth(),
        fetchSystemAbout(),
        fetchTaskCenterOverview(selectedSnapshotVersion.value),
        fetchStrategyExplanation(),
      ])

      dashboard.value = dashboardResponse
      systemHealth.value = healthResponse
      systemAbout.value = aboutResponse
      taskCenterOverview.value = taskOverviewResponse
      strategyExplanation.value = strategyExplanationResponse
      selectedTradeDate.value = dashboardResponse.tradeDate ?? ''
      selectedSnapshotVersion.value = dashboardResponse.snapshotVersion
      candidatePageIndex.value = 1
      signalPageIndex.value = 1
      industryPageIndex.value = 1
      stockFundFlowPageIndex.value = 1
      industryFundFlowPageIndex.value = 1
      financialPageIndex.value = 1

      await Promise.all([
        loadCandidatePage(1),
        loadSignalPage(1),
        loadIndustryPage(1),
        loadStockFundFlowPage(1),
        loadIndustryFundFlowPage(1),
        loadFinancialPage(1),
        loadPositions(),
        loadBacktests(),
        loadLearningOverview(),
      ])

      const preferredCode = signals.value[0]?.stockCode ?? candidates.value[0]?.stockCode ?? financials.value[0]?.stockCode
      if (preferredCode) {
        await selectStock(preferredCode, false)
      } else {
        stockDetail.value = null
      }
    } catch (error) {
      errorMessage.value = error instanceof Error ? error.message : '加载数据失败。'
    } finally {
      isLoading.value = false
    }
  }

  async function triggerSnapshot(_version: SnapshotVersion) {
    isTriggeringSnapshot.value = true
    errorMessage.value = ''
    try {
      await triggerDomainSync()
      selectedSnapshotVersion.value = 'end_of_day_final'
      await loadHomeData()
    } catch (error) {
      errorMessage.value = error instanceof Error ? error.message : '手动生成正式结果失败。'
    } finally {
      isTriggeringSnapshot.value = false
    }
  }

  async function loadCandidatePage(page: number) {
    candidatePage.value = await fetchCandidates(buildCandidateQuery(page))
    candidatePageIndex.value = candidatePage.value.page
  }

  async function loadSignalPage(page: number) {
    signalPage.value = await fetchSignals(buildSignalQuery(page))
    signalPageIndex.value = signalPage.value.page
  }

  async function loadIndustryPage(page: number) {
    industryPage.value = await fetchIndustries(buildIndustryQuery(page))
    industryPageIndex.value = industryPage.value.page
  }

  async function loadFinancialPage(page: number) {
    financialPage.value = await fetchFinancials(buildFinancialQuery(page))
    financialPageIndex.value = financialPage.value.page
  }

  async function loadStockFundFlowPage(page: number) {
    stockFundFlowPage.value = await fetchStockFundFlows(buildStockFundFlowQuery(page))
    stockFundFlowPageIndex.value = stockFundFlowPage.value.page
  }

  async function loadIndustryFundFlowPage(page: number) {
    industryFundFlowPage.value = await fetchIndustryFundFlows(buildIndustryFundFlowQuery(page))
    industryFundFlowPageIndex.value = industryFundFlowPage.value.page
  }

  async function selectStock(stockCode: string, showPanelLoading = true) {
    if (!selectedTradeDate.value) {
      return
    }

    if (showPanelLoading) {
      isDetailLoading.value = true
    }

    errorMessage.value = ''
    try {
      const detail = await fetchStockDetail(stockCode, selectedTradeDate.value, selectedSnapshotVersion.value)
      stockDetail.value = detail
      await loadLearningOverview(detail.stockCode, detail.stockName)
    } catch (error) {
      errorMessage.value = error instanceof Error ? error.message : '加载个股详情失败。'
    } finally {
      if (showPanelLoading) {
        isDetailLoading.value = false
      }
    }
  }

  async function applyFilters() {
    if (!selectedTradeDate.value) {
      return
    }

    isLoading.value = true
    errorMessage.value = ''
    try {
      await Promise.all([loadCandidatePage(1), loadSignalPage(1), loadIndustryPage(1), loadStockFundFlowPage(1), loadIndustryFundFlowPage(1), loadFinancialPage(1)])
      const selectedCode = stockDetail.value?.stockCode
      const preferredCode =
        selectedCode && (
          candidates.value.some((item) => item.stockCode === selectedCode) ||
          signals.value.some((item) => item.stockCode === selectedCode) ||
          stockFundFlows.value.some((item) => item.stockCode === selectedCode) ||
          financials.value.some((item) => item.stockCode === selectedCode))
          ? selectedCode
          : signals.value[0]?.stockCode ?? candidates.value[0]?.stockCode ?? stockFundFlows.value[0]?.stockCode ?? financials.value[0]?.stockCode

      if (preferredCode) {
        await selectStock(preferredCode, false)
      } else {
        stockDetail.value = null
        await loadLearningOverview()
      }
    } catch (error) {
      errorMessage.value = error instanceof Error ? error.message : '应用筛选条件失败。'
    } finally {
      isLoading.value = false
    }
  }

  async function createSimulatedBuy(payload: { entryPrice?: number; quantity?: number; notes?: string }) {
    if (!stockDetail.value?.signal) {
      errorMessage.value = '请先选中一条带有交易信号的股票。'
      return
    }

    isSubmittingPosition.value = true
    errorMessage.value = ''
    try {
      await simulateBuy({
        stockCode: stockDetail.value.stockCode,
        tradeDate: stockDetail.value.tradeDate,
        snapshotVersion: selectedSnapshotVersion.value,
        entryPrice: payload.entryPrice,
        quantity: payload.quantity,
        notes: payload.notes,
      })
      await loadPositions()
    } catch (error) {
      errorMessage.value = error instanceof Error ? error.message : '创建模拟买入失败。'
    } finally {
      isSubmittingPosition.value = false
    }
  }

  async function createLearningReview(payload: {
    stockCode: string
    stockName: string
    tradeDate: string
    buyReason: string
    marketContext: string
    executionDiscipline: string
    resultSummary: string
    improvementPlan: string
  }) {
    isSubmittingLearningReview.value = true
    errorMessage.value = ''
    try {
      await saveLearningReview({
        stockCode: payload.stockCode,
        stockName: payload.stockName,
        tradeDate: payload.tradeDate,
        snapshotVersion: selectedSnapshotVersion.value,
        buyReason: payload.buyReason,
        marketContext: payload.marketContext,
        executionDiscipline: payload.executionDiscipline,
        resultSummary: payload.resultSummary,
        improvementPlan: payload.improvementPlan,
      })

      await loadLearningOverview(payload.stockCode, payload.stockName)
    } catch (error) {
      errorMessage.value = error instanceof Error ? error.message : '保存复盘记录失败。'
    } finally {
      isSubmittingLearningReview.value = false
    }
  }

  async function closePosition(positionId: number) {
    isSubmittingPosition.value = true
    errorMessage.value = ''
    try {
      await simulateSell(positionId, {})
      await loadPositions()
    } catch (error) {
      errorMessage.value = error instanceof Error ? error.message : '模拟卖出失败。'
    } finally {
      isSubmittingPosition.value = false
    }
  }

  async function executeBacktest(payload: { startDate: string; endDate: string; maxSignalsPerDay: number; maxHoldingDays: number }) {
    isRunningBacktest.value = true
    errorMessage.value = ''
    try {
      selectedBacktestRun.value = await runBacktest({
        startDate: payload.startDate,
        endDate: payload.endDate,
        snapshotVersion: selectedSnapshotVersion.value,
        maxSignalsPerDay: payload.maxSignalsPerDay,
        maxHoldingDays: payload.maxHoldingDays,
      })
      await loadBacktests()
    } catch (error) {
      errorMessage.value = error instanceof Error ? error.message : '执行回测失败。'
    } finally {
      isRunningBacktest.value = false
    }
  }

  async function selectBacktestRun(id: number) {
    errorMessage.value = ''
    try {
      selectedBacktestRun.value = await fetchBacktestRunDetail(id)
    } catch (error) {
      errorMessage.value = error instanceof Error ? error.message : '加载回测详情失败。'
    }
  }

  function resetTradeDate() {
    if (!dashboard.value?.tradeDate) {
      return
    }

    selectedTradeDate.value = dashboard.value.tradeDate
    void applyFilters()
  }

  function moveCandidatePage(step: number) {
    const nextPage = Math.min(totalCandidatePages.value, Math.max(1, candidatePageIndex.value + step))
    if (nextPage !== candidatePageIndex.value) {
      void loadCandidatePage(nextPage)
    }
  }

  function moveSignalPage(step: number) {
    const nextPage = Math.min(totalSignalPages.value, Math.max(1, signalPageIndex.value + step))
    if (nextPage !== signalPageIndex.value) {
      void loadSignalPage(nextPage)
    }
  }

  function moveIndustryPage(step: number) {
    const nextPage = Math.min(totalIndustryPages.value, Math.max(1, industryPageIndex.value + step))
    if (nextPage !== industryPageIndex.value) {
      void loadIndustryPage(nextPage)
    }
  }

  function moveFinancialPage(step: number) {
    const nextPage = Math.min(totalFinancialPages.value, Math.max(1, financialPageIndex.value + step))
    if (nextPage !== financialPageIndex.value) {
      void loadFinancialPage(nextPage)
    }
  }

  function moveStockFundFlowPage(step: number) {
    const nextPage = Math.min(totalStockFundFlowPages.value, Math.max(1, stockFundFlowPageIndex.value + step))
    if (nextPage !== stockFundFlowPageIndex.value) {
      void loadStockFundFlowPage(nextPage)
    }
  }

  function moveIndustryFundFlowPage(step: number) {
    const nextPage = Math.min(totalIndustryFundFlowPages.value, Math.max(1, industryFundFlowPageIndex.value + step))
    if (nextPage !== industryFundFlowPageIndex.value) {
      void loadIndustryFundFlowPage(nextPage)
    }
  }

  onMounted(() => {
    void loadHomeData()
  })

  return {
    allPositions,
    applyFilters,
    backtestOverview,
    backtestRuns,
    candidatePageIndex,
    candidateSortMode,
    candidateTotalCount,
    candidates,
    closePosition,
    createLearningReview,
    createSimulatedBuy,
    dashboard,
    errorMessage,
    executeBacktest,
    financialPageIndex,
    financialSortMode,
    financialTotalCount,
    financials,
    fundFlowDirection,
    industries,
    industryFundFlows,
    industryFundFlowPageIndex,
    industryFundFlowSortMode,
    industryFundFlowTotalCount,
    industryPageIndex,
    industrySortMode,
    industryTotalCount,
    isDetailLoading,
    isLoading,
    isRunningBacktest,
    isSubmittingLearningReview,
    isSubmittingPosition,
    isTriggeringSnapshot,
    learningOverview,
    loadHomeData,
    minRoe,
    minScore,
    moveCandidatePage,
    moveFinancialPage,
    moveIndustryFundFlowPage,
    moveIndustryPage,
    moveSignalPage,
    moveStockFundFlowPage,
    onlyTradable,
    positionHistory,
    positions,
    positiveGrowthOnly,
    resetTradeDate,
    searchText,
    selectedBacktestRun,
    selectedSnapshotVersion,
    selectBacktestRun,
    selectStock,
    selectedTradeDate,
    signalPageIndex,
    signalSortMode,
    signalTotalCount,
    signals,
    stockFundFlows,
    stockFundFlowPageIndex,
    stockFundFlowSortMode,
    stockFundFlowTotalCount,
    stockDetail,
    strategyExplanation,
    systemAbout,
    systemHealth,
    taskCenterOverview,
    topCandidates,
    triggerSnapshot,
    totalCandidatePages,
    totalFinancialPages,
    totalIndustryFundFlowPages,
    totalIndustryPages,
    totalSignalPages,
    totalStockFundFlowPages,
  }
}

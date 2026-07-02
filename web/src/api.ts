import type {
  BacktestOverviewResponse,
  BacktestRunDetail,
  BacktestRunListItem,
  BacktestRunRequest,
  CandidateItem,
  CandidateQuery,
  DashboardResponse,
  DomainSyncRunItem,
  FinancialItem,
  FinancialQuery,
  FundFlowQuery,
  IndustryItem,
  IndustryFundFlowItem,
  IndustryQuery,
  LearningReviewOverviewResponse,
  PagedResponse,
  SaveLearningReviewRequest,
  SignalItem,
  SignalQuery,
  SimulateBuyRequest,
  SimulateSellRequest,
  SimulatedPositionItem,
  SimulatedTradeHistoryItem,
  SnapshotVersion,
  StockFundFlowItem,
  StockDetailResponse,
  StrategyExplanationResponse,
  SystemHealthResponse,
  SystemSummaryResponse,
  TaskCenterOverviewResponse,
} from './types'

const API_BASE = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080'

async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`)
  if (!response.ok) {
    throw new Error(`接口请求失败：${response.status}`)
  }

  return response.json() as Promise<T>
}

async function postJson<TResponse, TRequest>(path: string, payload: TRequest): Promise<TResponse> {
  const response = await fetch(`${API_BASE}${path}`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(payload),
  })

  if (!response.ok) {
    const errorText = await response.text()
    throw new Error(errorText || `接口请求失败：${response.status}`)
  }

  return response.json() as Promise<TResponse>
}

function buildQuery(query: object): string {
  const searchParams = new URLSearchParams()
  Object.entries(query as Record<string, string | number | boolean | undefined>).forEach(([key, value]) => {
    if (value !== undefined && value !== '') {
      searchParams.set(key, String(value))
    }
  })

  const suffix = searchParams.toString()
  return suffix ? `?${suffix}` : ''
}

export function fetchDashboard(snapshotVersion: SnapshotVersion): Promise<DashboardResponse> {
  return getJson<DashboardResponse>(`/api/dashboard${buildQuery({ snapshotVersion })}`)
}

export function fetchCandidates(query: CandidateQuery = {}): Promise<PagedResponse<CandidateItem>> {
  return getJson<PagedResponse<CandidateItem>>(`/api/candidates${buildQuery(query)}`)
}

export function fetchSignals(query: SignalQuery = {}): Promise<PagedResponse<SignalItem>> {
  return getJson<PagedResponse<SignalItem>>(`/api/signals${buildQuery(query)}`)
}

export function fetchIndustries(query: IndustryQuery = {}): Promise<PagedResponse<IndustryItem>> {
  return getJson<PagedResponse<IndustryItem>>(`/api/industries${buildQuery(query)}`)
}

export function fetchStockFundFlows(query: FundFlowQuery = {}): Promise<PagedResponse<StockFundFlowItem>> {
  return getJson<PagedResponse<StockFundFlowItem>>(`/api/fund-flows/stocks${buildQuery(query)}`)
}

export function fetchIndustryFundFlows(query: FundFlowQuery = {}): Promise<PagedResponse<IndustryFundFlowItem>> {
  return getJson<PagedResponse<IndustryFundFlowItem>>(`/api/fund-flows/industries${buildQuery(query)}`)
}

export function fetchFinancials(query: FinancialQuery = {}): Promise<PagedResponse<FinancialItem>> {
  return getJson<PagedResponse<FinancialItem>>(`/api/financials${buildQuery(query)}`)
}

export function fetchStockDetail(stockCode: string, date: string | undefined, snapshotVersion: SnapshotVersion): Promise<StockDetailResponse> {
  return getJson<StockDetailResponse>(`/api/stocks/${stockCode}${buildQuery({ date, snapshotVersion })}`)
}

export function fetchSystemHealth(): Promise<SystemHealthResponse> {
  return getJson<SystemHealthResponse>('/api/health')
}

export function fetchSystemAbout(): Promise<SystemSummaryResponse> {
  return getJson<SystemSummaryResponse>('/api/about')
}

export function fetchTaskCenterOverview(snapshotVersion: SnapshotVersion): Promise<TaskCenterOverviewResponse> {
  return getJson<TaskCenterOverviewResponse>(`/api/tasks/overview${buildQuery({ snapshotVersion })}`)
}

export async function triggerDomainSync(): Promise<DomainSyncRunItem> {
  const response = await fetch(`${API_BASE}/api/tasks/domain-sync`, {
    method: 'POST',
  })

  if (!response.ok) {
    throw new Error(`手动生成快照失败：${response.status}`)
  }

  return response.json() as Promise<DomainSyncRunItem>
}

export function fetchStrategyExplanation(): Promise<StrategyExplanationResponse> {
  return getJson<StrategyExplanationResponse>('/api/strategy/explanation')
}

export function fetchBacktestOverview(): Promise<BacktestOverviewResponse> {
  return getJson<BacktestOverviewResponse>('/api/backtests/overview')
}

export function fetchBacktestRuns(): Promise<BacktestRunListItem[]> {
  return getJson<BacktestRunListItem[]>('/api/backtests')
}

export function fetchBacktestRunDetail(id: number): Promise<BacktestRunDetail> {
  return getJson<BacktestRunDetail>(`/api/backtests/${id}`)
}

export function runBacktest(request: BacktestRunRequest): Promise<BacktestRunDetail> {
  return postJson<BacktestRunDetail, BacktestRunRequest>('/api/backtests/run', request)
}

export function fetchPositions(): Promise<SimulatedPositionItem[]> {
  return getJson<SimulatedPositionItem[]>('/api/positions')
}

export function fetchAllPositions(): Promise<SimulatedPositionItem[]> {
  return getJson<SimulatedPositionItem[]>('/api/positions/all')
}

export function fetchPositionHistory(): Promise<SimulatedTradeHistoryItem[]> {
  return getJson<SimulatedTradeHistoryItem[]>('/api/positions/history')
}

export function simulateBuy(request: SimulateBuyRequest): Promise<SimulatedPositionItem> {
  return postJson<SimulatedPositionItem, SimulateBuyRequest>('/api/positions/simulate-buy', request)
}

export function simulateSell(positionId: number, request: SimulateSellRequest): Promise<SimulatedPositionItem> {
  return postJson<SimulatedPositionItem, SimulateSellRequest>(`/api/positions/${positionId}/sell`, request)
}

export function fetchLearningReviews(stockCode?: string, stockName?: string, tradeDate?: string, snapshotVersion?: string): Promise<LearningReviewOverviewResponse> {
  return getJson<LearningReviewOverviewResponse>(`/api/learning/reviews${buildQuery({ stockCode, stockName, tradeDate, snapshotVersion })}`)
}

export function saveLearningReview(request: SaveLearningReviewRequest): Promise<LearningReviewOverviewResponse['reviews'][number]> {
  return postJson<LearningReviewOverviewResponse['reviews'][number], SaveLearningReviewRequest>('/api/learning/reviews', request)
}

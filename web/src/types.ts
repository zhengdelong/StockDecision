export type SnapshotVersion = 'end_of_day_final'

export interface DashboardMetric {
  key: string
  label: string
  value: string
  tone: string
}

export interface DashboardResponse {
  tradeDate: string | null
  snapshotVersion: SnapshotVersion
  snapshotVersionName: string
  isDataComplete: boolean
  isSignalEligible: boolean
  marketRegime: string
  candidateCount: number
  signalCount: number
  latestIngestionAtUtc: string | null
  metrics: DashboardMetric[]
}

export interface PagedResponse<TItem> {
  items: TItem[]
  page: number
  pageSize: number
  totalCount: number
}

export interface ScoreBreakdown {
  relativeStrengthScore: number
  trendScore: number
  volumePriceScore: number
  fundamentalScore: number
  totalScore: number
}

export interface ScoreRuleDetail {
  key: string
  dimension: string
  label: string
  score: number
  maxScore: number
  hit: boolean
  evidence: string
}

export interface CandidateItem {
  stockCode: string
  stockName: string
  industryName: string | null
  grade: string
  strategyType: string
  isTradable: boolean
  totalScore: number
  scoreBreakdown: ScoreBreakdown
  close: number
  ma20: number
  ma60: number
  atr14: number
  relativeStrengthScore: number
  stopLossPrice: number
  targetPrice: number
  riskRewardRatio: number
  explanation: string
  scoreDetails: ScoreRuleDetail[]
}

export interface SignalItem {
  stockCode: string
  stockName: string
  industryName: string | null
  strategyType: string
  totalScore: number
  scoreBreakdown: ScoreBreakdown
  triggerPrice: number
  stopLossPrice: number
  targetPrice: number
  riskRewardRatio: number
  suggestedCapital: number
  estimatedShares: number
  explanation: string
  generatedAtUtc: string
}

export interface IndustryItem {
  industryCode: string
  industryName: string
  tradeDate: string
  pctChange20d: number
  rank20d: number
  candidateCount: number
  signalCount: number
  topCandidateScore: number | null
  topSignalScore: number | null
}

export interface FinancialItem {
  stockCode: string
  stockName: string
  industryName: string | null
  reportDate: string
  pe: number | null
  pb: number | null
  roe: number | null
  revenueYoy: number | null
  netProfitYoy: number | null
  freeFloatMarketCap: number | null
}

export interface PriceSeriesPoint {
  tradeDate: string
  open: number
  high: number
  low: number
  close: number
  volume: number
  amount: number
  ma20: number | null
  ma60: number | null
  ma120: number | null
}

export interface FinancialSummary {
  reportDate: string
  pe: number | null
  pb: number | null
  roe: number | null
  revenueYoy: number | null
  netProfitYoy: number | null
}

export interface IndicatorSummary {
  close: number
  ma20: number
  ma60: number
  ma120: number
  atr14: number
  return20d: number
  return60d: number
  relativeStrengthScore: number
  is20DayBreakout: boolean
  isMa20Upward: boolean
  isBullishStacked: boolean
  distanceToMa20Pct: number
}

export interface StockDetailResponse {
  stockCode: string
  stockName: string
  industryName: string | null
  tradeDate: string
  snapshotVersion: SnapshotVersion
  snapshotVersionName: string
  latestBar: PriceSeriesPoint
  financial: FinancialSummary | null
  indicator: IndicatorSummary | null
  candidate: CandidateItem | null
  signal: SignalItem | null
  recentBars: PriceSeriesPoint[]
}

export interface CandidateQuery {
  date?: string
  snapshotVersion?: SnapshotVersion
  search?: string
  minScore?: number
  onlyTradable?: boolean
  sortBy?: 'score' | 'rr' | 'close'
  page?: number
  pageSize?: number
}

export interface SignalQuery {
  date?: string
  snapshotVersion?: SnapshotVersion
  search?: string
  sortBy?: 'score' | 'rr' | 'capital'
  page?: number
  pageSize?: number
}

export interface IndustryQuery {
  date?: string
  snapshotVersion?: SnapshotVersion
  search?: string
  sortBy?: 'strength' | 'rank' | 'candidates' | 'signals'
  page?: number
  pageSize?: number
}

export interface FinancialQuery {
  search?: string
  sortBy?: 'roe' | 'revenue' | 'profit' | 'marketCap'
  minRoe?: number
  positiveGrowthOnly?: boolean
  page?: number
  pageSize?: number
}

export interface SystemHealthResponse {
  service: string
  status: string
  strategy: string
}

export interface SystemSummaryResponse {
  name: string
  capital: number
  mode: string
  strategyVersion: string
  documents: string[]
}

export interface TaskRunItem {
  targetScope: string
  isComplete: boolean
  isSignalEligible: boolean
  createdAtUtc: string
}

export interface DomainSyncStatus {
  latestRawTradeDate: string | null
  latestImportedTradeDate: string | null
  latestRawFinancialReportDate: string | null
  latestImportedFinancialReportDate: string | null
  latestSuccessfulDomainSyncAtUtc: string | null
  latestSuccessfulEndOfDayFinalAtUtc: string | null
  requiresSync: boolean
  hasTradeDateGap: boolean
  hasFinancialGap: boolean
}

export interface TaskScheduleItem {
  name: string
  schedule: string
  description: string
}

export interface DomainSyncRunItem {
  jobName: string
  triggerKind: string
  snapshotVersion: string
  status: string
  dataUpdated: boolean
  isSignalEligible: boolean
  effectiveTradeDate: string | null
  financialReportDate: string | null
  startedAtUtc: string
  finishedAtUtc: string | null
  summary: string
}

export interface TaskCenterOverviewResponse {
  tradeDate: string | null
  snapshotVersion: SnapshotVersion
  snapshotVersionName: string
  latestSuccessfulIngestionAtUtc: string | null
  domainSyncStatus: DomainSyncStatus
  candidateCount: number
  signalCount: number
  marketRegime: string
  isSignalEligible: boolean
  schedules: TaskScheduleItem[]
  statusMessages: string[]
  collectorRuns: TaskRunItem[]
  domainSyncRuns: DomainSyncRunItem[]
}

export interface StrategyRuleSection {
  title: string
  items: string[]
}

export interface StrategyScoreDimension {
  name: string
  maxScore: number
  rules: string[]
}

export interface StrategyExecutionRule {
  name: string
  description: string
}

export interface StrategyExplanationResponse {
  strategyVersion: string
  sections: StrategyRuleSection[]
  scoreDimensions: StrategyScoreDimension[]
  executionRules: StrategyExecutionRule[]
}

export interface BacktestTradeItem {
  tradeDate: string
  stockCode: string
  stockName: string
  strategyType: string
  entryPrice: number
  exitPrice: number
  returnPct: number
  maxGainPct: number
  maxDrawdownPct: number
  hitTarget: boolean
  hitStopLoss: boolean
  quantity: number
  investedCapital: number
  profitAmount: number
  maxHoldingDays: number
}

export interface BacktestOverviewResponse {
  strategyVersion: string
  sampleTradeCount: number
  winRatePct: number
  averageReturnPct: number
  averageMaxGainPct: number
  averageMaxDrawdownPct: number
  trades: BacktestTradeItem[]
}

export interface SimulatedPositionItem {
  id: number
  stockCode: string
  stockName: string
  industryName: string | null
  strategyType: string
  snapshotVersion: SnapshotVersion
  tradeDate: string
  entryPrice: number
  stopLossPrice: number
  targetPrice: number
  quantity: number
  investedCapital: number
  latestPrice: number | null
  latestTradeDate: string | null
  floatingProfitAmount: number
  floatingProfitPct: number
  status: string
  openedAtUtc: string
  closedAtUtc: string | null
  exitPrice: number | null
  realizedProfitAmount: number | null
  realizedProfitPct: number | null
  notes: string | null
}

export interface SimulatedTradeHistoryItem {
  id: number
  positionId: number
  actionType: string
  stockCode: string
  stockName: string
  tradeDate: string
  price: number
  quantity: number
  amount: number
  summary: string
  createdAtUtc: string
}

export interface SimulateBuyRequest {
  stockCode: string
  tradeDate?: string
  snapshotVersion?: SnapshotVersion
  entryPrice?: number
  quantity?: number
  notes?: string
}

export interface SimulateSellRequest {
  exitPrice?: number
  tradeDate?: string
  notes?: string
}

export interface BacktestRunRequest {
  startDate: string
  endDate: string
  snapshotVersion?: SnapshotVersion
  maxSignalsPerDay: number
  maxHoldingDays: number
}

export interface BacktestEquityPoint {
  tradeDate: string
  equity: number
  returnPct: number
}

export interface BacktestRunListItem {
  id: number
  strategyVersion: string
  snapshotVersion: string
  startDate: string
  endDate: string
  sampleTradeCount: number
  winRatePct: number
  averageReturnPct: number
  profitLossRatio: number
  maxDrawdownPct: number
  totalReturnPct: number
  createdAtUtc: string
}

export interface BacktestRunDetail {
  id: number
  strategyVersion: string
  snapshotVersion: string
  startDate: string
  endDate: string
  sampleTradeCount: number
  winRatePct: number
  averageReturnPct: number
  averageMaxGainPct: number
  averageMaxDrawdownPct: number
  profitLossRatio: number
  maxDrawdownPct: number
  totalReturnPct: number
  averageHoldingDays: number
  createdAtUtc: string
  equityCurve: BacktestEquityPoint[]
  trades: BacktestTradeItem[]
}

export interface LearningReviewItem {
  id: number
  positionId: number | null
  stockCode: string
  stockName: string
  tradeDate: string
  snapshotVersion: string
  buyReason: string
  marketContext: string
  executionDiscipline: string
  resultSummary: string
  improvementPlan: string
  createdAtUtc: string
  updatedAtUtc: string
}

export interface LearningReviewOverviewResponse {
  stockCode: string | null
  stockName: string | null
  tradeDate: string | null
  snapshotVersion: string
  reviewPrompts: string[]
  reviews: LearningReviewItem[]
}

export interface SaveLearningReviewRequest {
  id?: number
  positionId?: number
  stockCode: string
  stockName: string
  tradeDate: string
  snapshotVersion?: string
  buyReason: string
  marketContext: string
  executionDiscipline: string
  resultSummary: string
  improvementPlan: string
}

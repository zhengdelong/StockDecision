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
  isBacktestApproved: boolean
  backtestStatusNote: string
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
  riskDisciplineScore: number
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

export interface ExecutionPlanRule {
  label: string
  value: string
  description: string
}

export interface TradeExecutionPlan {
  planType: string
  status: string
  summary: string
  referencePrice: number
  triggerPrice: number
  stopLossPrice: number
  targetPrice: number
  riskRewardRatio: number
  suggestedCapital: number | null
  estimatedShares: number | null
  observationDays: number
  maxHoldingDays: number
  maxEntryGapPct: number
  entryRules: ExecutionPlanRule[]
  holdRules: ExecutionPlanRule[]
  exitRules: ExecutionPlanRule[]
  invalidationRules: ExecutionPlanRule[]
}

export interface CandidateItem {
  stockCode: string
  stockName: string
  industryName: string | null
  grade: string
  strategyType: string
  isTradable: boolean
  eligibilityStatus: string
  eligibilityReasons: string[]
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
  executionPlan: TradeExecutionPlan | null
}

export interface SignalItem {
  stockCode: string
  stockName: string
  industryName: string | null
  strategyType: string
  eligibilityStatus: string
  eligibilityReasons: string[]
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
  executionPlan: TradeExecutionPlan | null
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
  reportDate: string | null
  totalScore: number | null
  pe: number | null
  pb: number | null
  roe: number | null
  revenueYoy: number | null
  netProfitYoy: number | null
  freeFloatMarketCap: number | null
  operatingCashFlow: number | null
  grossMargin: number | null
  debtToAssetRatio: number | null
  operatingCashFlowNet: number | null
  announcementDate: string | null
  dataSourcePriority: string | null
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
  operatingCashFlow: number | null
  grossMargin: number | null
  debtToAssetRatio: number | null
  operatingCashFlowNet: number | null
  announcementDate: string | null
  dataSourcePriority: string | null
}

export interface FundFlowSummary {
  tradeDate: string
  mainNetAmount: number | null
  mainNetPct: number | null
  superLargeNetAmount: number | null
  superLargeNetPct: number | null
  rankPercentile5d: number | null
  industryMainNetAmount: number | null
  industryMainNetPct: number | null
  industryRank: number | null
  industryRankPercentile: number | null
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

export interface LhbSummary {
  isOnLhbToday: boolean
  tradeDate: string | null
  reason: string | null
  netAmount: number | null
  institutionNetAmount: number | null
  institutionBuyCount: number | null
  isInstitutionNetBuy: boolean
  recent20dLhbCount: number
  daysSinceLastLhb: number | null
  riskFlags: string | null
}

export interface StockDetailResponse {
  stockCode: string
  stockName: string
  industryName: string | null
  scoringIndustryName: string | null
  tradeDate: string
  snapshotVersion: SnapshotVersion
  snapshotVersionName: string
  latestBar: PriceSeriesPoint
  financial: FinancialSummary | null
  indicator: IndicatorSummary | null
  fundFlow: FundFlowSummary | null
  lhb: LhbSummary | null
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

export interface FundFlowQuery {
  date?: string
  snapshotVersion?: SnapshotVersion
  search?: string
  sortBy?: 'percentile' | 'main' | 'mainPct' | 'superLargePct' | 'score' | 'rank' | 'candidates' | 'signals'
  direction?: 'all' | 'inflow' | 'outflow'
  page?: number
  pageSize?: number
}

export interface StockFundFlowItem {
  stockCode: string
  stockName: string
  industryName: string | null
  tradeDate: string
  mainNetAmount: number | null
  mainNetPct: number | null
  superLargeNetAmount: number | null
  superLargeNetPct: number | null
  rankPercentile5d: number | null
  totalScore: number | null
  eligibilityStatus: string | null
  isCandidate: boolean
  isTradable: boolean
}

export interface IndustryFundFlowItem {
  industryName: string
  tradeDate: string
  mainNetAmount: number | null
  mainNetPct: number | null
  rank: number | null
  rankPercentile: number | null
  candidateCount: number
  signalCount: number
  topCandidateScore: number | null
  topSignalScore: number | null
}

export interface FinancialQuery {
  date?: string
  snapshotVersion?: SnapshotVersion
  search?: string
  sortBy?: 'score' | 'roe' | 'revenue' | 'profit' | 'marketCap'
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
  benchmarkReturnPct: number
  dataCoveragePct: number
  skippedTradeDays: number
  annualTradeCount: number
  maxConsecutiveLosses: number
  isApproved: boolean
  failureReasons: string[]
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
  heldDays: number
  status: string
  adviceStatus: string
  adviceTitle: string
  adviceText: string
  adviceTags: string[]
  executionPlan: TradeExecutionPlan | null
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
  benchmarkReturnPct: number
  dataCoveragePct: number
  skippedTradeDays: number
  annualTradeCount: number
  maxConsecutiveLosses: number
  isApproved: boolean
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
  benchmarkReturnPct: number
  dataCoveragePct: number
  skippedTradeDays: number
  annualTradeCount: number
  maxConsecutiveLosses: number
  isApproved: boolean
  failureReasons: string[]
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
  errorTags: string[]
  isStrategyAligned: boolean
  followedStopLoss: boolean
  followedTakeProfit: boolean
  modifiedPlanDuringTrade: boolean
  followedGapRule: boolean
  createdAtUtc: string
  updatedAtUtc: string
}

export interface LearningProgressSummary {
  reviewCount: number
  strategyAlignedTradeCount: number
  offStrategyTradeCount: number
  consecutiveStopLossFollowCount: number
  consecutiveGapRuleFollowCount: number
}

export interface LearningErrorTagStat {
  tag: string
  count: number
}

export interface LearningReviewOverviewResponse {
  stockCode: string | null
  stockName: string | null
  tradeDate: string | null
  snapshotVersion: string
  reviewPrompts: string[]
  progressSummary: LearningProgressSummary
  errorTagStats: LearningErrorTagStat[]
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
  errorTags?: string[]
  isStrategyAligned?: boolean
  followedStopLoss?: boolean
  followedTakeProfit?: boolean
  modifiedPlanDuringTrade?: boolean
  followedGapRule?: boolean
}

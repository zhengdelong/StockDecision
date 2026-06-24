import type { PriceSeriesPoint, ScoreBreakdown, StockDetailResponse } from '../types'

export interface ChartScale {
  min: number
  max: number
  span: number
}

export interface ChartPoint {
  x: number
  y: number
  value: number
  index: number
}

/**
 * 将评分拆解转换成可直接渲染的进度条行。
 */
export function buildScoreRows(scoreBreakdown: ScoreBreakdown | null | undefined) {
  if (!scoreBreakdown) {
    return []
  }

  return [
    { key: 'rs', label: '相对强弱', value: scoreBreakdown.relativeStrengthScore, max: 30 },
    { key: 'trend', label: '趋势', value: scoreBreakdown.trendScore, max: 25 },
    { key: 'volume', label: '量价', value: scoreBreakdown.volumePriceScore, max: 25 },
    { key: 'fundamental', label: '基本面', value: scoreBreakdown.fundamentalScore, max: 20 },
  ]
}

/**
 * 根据收盘价序列计算简单移动平均线，前几个不足窗口的数据返回空值。
 */
export function buildSimpleMovingAverageSeries(points: PriceSeriesPoint[], windowSize: number): Array<number | null> {
  if (windowSize <= 0) {
    return points.map(() => null)
  }

  return points.map((_, index) => {
    if (index + 1 < windowSize) {
      return null
    }

    const window = points.slice(index + 1 - windowSize, index + 1)
    const sum = window.reduce((total, item) => total + item.close, 0)
    return sum / window.length
  })
}

/**
 * 为多条价格线计算统一的坐标缩放范围，保证均线和收盘价共用同一坐标系。
 */
export function buildChartScale(seriesCollection: Array<Array<number | null>>): ChartScale | null {
  const values = seriesCollection.flat().filter((item): item is number => typeof item === 'number')
  if (values.length < 2) {
    return null
  }

  const rawMin = Math.min(...values)
  const rawMax = Math.max(...values)
  const padding = Math.max((rawMax - rawMin) * 0.08, rawMax * 0.01, 0.01)
  const min = Math.max(0, rawMin - padding)
  const max = rawMax + padding
  return {
    min,
    max,
    span: max - min || 1,
  }
}

/**
 * 将数值映射到图表纵坐标。
 */
export function resolveChartY(value: number, scale: ChartScale, height = 160, padding = 14): number {
  return height - padding - ((value - scale.min) / scale.span) * (height - padding * 2)
}

/**
 * 将索引映射到图表横坐标。
 */
export function resolveChartX(index: number, pointCount: number, width = 320, padding = 14): number {
  const xStep = (width - padding * 2) / Math.max(pointCount - 1, 1)
  return padding + index * xStep
}

/**
 * 生成简洁 SVG 折线路径，允许外部传入统一缩放范围。
 */
export function buildLinePath(
  points: PriceSeriesPoint[],
  selector: (item: PriceSeriesPoint, index: number) => number | null,
  options?: {
    width?: number
    height?: number
    padding?: number
    scale?: ChartScale | null
  },
): string {
  const width = options?.width ?? 320
  const height = options?.height ?? 160
  const padding = options?.padding ?? 14
  const values = points.map(selector).filter((item): item is number => typeof item === 'number')
  const scale = options?.scale ?? buildChartScale([values])

  if (points.length < 2 || values.length < 2 || !scale) {
    return ''
  }

  let hasStarted = false

  return points
    .map((point, index) => {
      const value = selector(point, index)
      if (value == null) {
        return null
      }

      const x = resolveChartX(index, points.length, width, padding)
      const y = resolveChartY(value, scale, height, padding)
      const command = hasStarted ? 'L' : 'M'
      hasStarted = true
      return `${command} ${x.toFixed(2)} ${y.toFixed(2)}`
    })
    .filter((item): item is string => item !== null)
    .join(' ')
}

/**
 * 生成价格区域底图，帮助用户快速识别价格运行重心。
 */
export function buildAreaPath(
  points: PriceSeriesPoint[],
  selector: (item: PriceSeriesPoint, index: number) => number | null,
  options?: {
    width?: number
    height?: number
    padding?: number
    scale?: ChartScale | null
  },
): string {
  const width = options?.width ?? 320
  const height = options?.height ?? 160
  const padding = options?.padding ?? 14
  const scale = options?.scale ?? buildChartScale([points.map(selector).filter((item): item is number => typeof item === 'number')])

  if (points.length < 2 || !scale) {
    return ''
  }

  const validPoints = points
    .map((point, index) => {
      const value = selector(point, index)
      if (value == null) {
        return null
      }

      return {
        x: resolveChartX(index, points.length, width, padding),
        y: resolveChartY(value, scale, height, padding),
      }
    })
    .filter((item): item is { x: number; y: number } => item !== null)

  if (validPoints.length < 2) {
    return ''
  }

  const topPath = validPoints
    .map((point, index) => `${index === 0 ? 'M' : 'L'} ${point.x.toFixed(2)} ${point.y.toFixed(2)}`)
    .join(' ')

  const baselineY = height - padding
  const lastPoint = validPoints[validPoints.length - 1]
  const firstPoint = validPoints[0]
  return `${topPath} L ${lastPoint.x.toFixed(2)} ${baselineY.toFixed(2)} L ${firstPoint.x.toFixed(2)} ${baselineY.toFixed(2)} Z`
}

/**
 * 生成折线每个有效点的位置，用于悬浮信息和高亮。
 */
export function buildChartPoints(
  points: PriceSeriesPoint[],
  selector: (item: PriceSeriesPoint, index: number) => number | null,
  options: {
    width?: number
    height?: number
    padding?: number
    scale: ChartScale | null
  },
): Array<ChartPoint | null> {
  const width = options.width ?? 320
  const height = options.height ?? 160
  const padding = options.padding ?? 14
  const scale = options.scale

  if (!scale) {
    return points.map(() => null)
  }

  return points.map((point, index) => {
    const value = selector(point, index)
    if (value == null) {
      return null
    }

    return {
      x: resolveChartX(index, points.length, width, padding),
      y: resolveChartY(value, scale, height, padding),
      value,
      index,
    }
  })
}

/**
 * 构建纵轴刻度，帮助读图时快速识别价格区间。
 */
export function buildYAxisTicks(scale: ChartScale | null, tickCount = 4): number[] {
  if (!scale) {
    return []
  }

  return Array.from({ length: tickCount }, (_, index) => {
    const ratio = tickCount === 1 ? 0 : index / (tickCount - 1)
    return scale.max - scale.span * ratio
  })
}

/**
 * 构建横轴刻度，默认取首、中、尾三个交易日。
 */
export function buildXAxisTicks(points: PriceSeriesPoint[]): Array<{ index: number; label: string }> {
  if (points.length === 0) {
    return []
  }

  const indexes = new Set<number>([0, Math.floor((points.length - 1) / 2), points.length - 1])
  return Array.from(indexes)
    .sort((left, right) => left - right)
    .map((index) => ({
      index,
      label: points[index]?.tradeDate.slice(5) ?? '',
    }))
}

/**
 * 计算图表展示区间，给详情头部展示价格范围。
 */
export function buildChartStats(points: PriceSeriesPoint[], extraSeries: Array<Array<number | null>> = []) {
  const scale = buildChartScale([[...points.map((item) => item.close)], ...extraSeries])
  if (!scale) {
    return null
  }

  return {
    min: scale.min,
    max: scale.max,
  }
}

/**
 * 将均线距离转换为更易理解的文案标签。
 */
export function resolveDistanceLabel(detail: StockDetailResponse | null): string {
  const distance = detail?.indicator?.distanceToMa20Pct
  if (distance == null) {
    return '暂无数据'
  }

  if (distance > 8) {
    return '偏离较大'
  }

  if (distance >= 0) {
    return '贴近均线'
  }

  return '低于 MA20'
}

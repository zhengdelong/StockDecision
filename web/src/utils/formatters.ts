/**
 * 格式化日期字段，缺失时返回占位文本。
 */
export function formatDate(value: string | null | undefined): string {
  return value ?? '暂无数据'
}

/**
 * 格式化数值字段，统一控制小数位。
 */
export function formatNumber(value: number | null | undefined, digits = 2): string {
  return typeof value === 'number' ? value.toFixed(digits) : '-'
}

/**
 * 格式化百分比字段，缺失时返回占位文本。
 */
export function formatPercent(value: number | null | undefined, digits = 2): string {
  return typeof value === 'number' ? `${value.toFixed(digits)}%` : '-'
}

/**
 * 格式化日期时间字段，统一到本地可读形式。
 */
export function formatDateTime(value: string | null | undefined): string {
  if (!value) {
    return '-'
  }

  const parsed = new Date(value)
  if (Number.isNaN(parsed.getTime())) {
    return value
  }

  return parsed.toLocaleString('zh-CN', { hour12: false })
}

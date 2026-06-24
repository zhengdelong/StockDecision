<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { BarChart, CandlestickChart, LineChart } from 'echarts/charts'
import {
  GridComponent,
  LegendComponent,
  TooltipComponent,
  DataZoomComponent,
  MarkLineComponent,
} from 'echarts/components'
import { init, use } from 'echarts/core'
import { CanvasRenderer } from 'echarts/renderers'
import type { StockDetailResponse } from '../types'
import { formatNumber } from '../utils/formatters'
import { buildSimpleMovingAverageSeries } from '../utils/stockChart'

use([LineChart, BarChart, CandlestickChart, GridComponent, LegendComponent, TooltipComponent, DataZoomComponent, MarkLineComponent, CanvasRenderer])

const props = defineProps<{
  stockDetail: StockDetailResponse
}>()

const chartContainer = ref<HTMLDivElement | null>(null)
let chartInstance: any = null

/**
 * 图表内部也独立计算均线，避免父组件为了图表背负额外的大依赖。
 */
const ma10Series = computed(() => buildSimpleMovingAverageSeries(props.stockDetail.recentBars ?? [], 10))
const ma20Series = computed(() => buildSimpleMovingAverageSeries(props.stockDetail.recentBars ?? [], 20))
const ma60Series = computed(() => buildSimpleMovingAverageSeries(props.stockDetail.recentBars ?? [], 60))

function buildEmaSeries(values: number[], period: number): number[] {
  if (!values.length || period <= 0) {
    return []
  }

  const multiplier = 2 / (period + 1)
  const result: number[] = []

  values.forEach((value, index) => {
    if (index === 0) {
      result.push(value)
      return
    }

    result.push((value - result[index - 1]) * multiplier + result[index - 1])
  })

  return result
}

const macdSeries = computed(() => {
  const closes = (props.stockDetail.recentBars ?? []).map((item) => item.close)
  const ema12 = buildEmaSeries(closes, 12)
  const ema26 = buildEmaSeries(closes, 26)
  const dif = closes.map((_, index) => ema12[index] - ema26[index])
  const dea = buildEmaSeries(dif, 9)
  const histogram = dif.map((value, index) => (value - dea[index]) * 2)

  return { dif, dea, histogram }
})

function buildDetailChartOption(): any {
  const bars = props.stockDetail.recentBars ?? []
  const signal = props.stockDetail.signal
  const dates = bars.map((item) => item.tradeDate.slice(5))
  const candleSeries = bars.map((item) => [item.open, item.close, item.low, item.high])
  const volumeSeries = bars.map((item, index) => {
    const previous = index > 0 ? bars[index - 1]?.close ?? item.close : item.open
    return {
      value: item.volume / 10000,
      itemStyle: {
        color: item.close >= previous ? 'rgba(34, 197, 94, 0.78)' : 'rgba(239, 68, 68, 0.74)',
      },
    }
  })
  const macdHistogramSeries = macdSeries.value.histogram.map((value) => ({
    value,
    itemStyle: {
      color: value >= 0 ? 'rgba(239, 68, 68, 0.72)' : 'rgba(34, 197, 94, 0.72)',
    },
  }))

  const markLineData = signal
    ? [
        { yAxis: signal.triggerPrice, name: '触发价' },
        { yAxis: signal.stopLossPrice, name: '止损价' },
        { yAxis: signal.targetPrice, name: '目标价' },
      ]
    : []

  return {
    animation: false,
    grid: [
      { left: 44, right: 72, top: 24, height: 190 },
      { left: 44, right: 72, top: 244, height: 72 },
      { left: 44, right: 72, top: 340, height: 78 },
    ],
    legend: {
      bottom: 0,
      left: 18,
      icon: 'circle',
      itemWidth: 10,
      itemHeight: 10,
      textStyle: { color: '#475569', fontSize: 12 },
      data: ['K线', 'MA10', 'MA20', 'MA60', '成交量', 'DIF', 'DEA', 'MACD'],
    },
    tooltip: {
      trigger: 'axis',
      axisPointer: {
        type: 'cross',
        link: [{ xAxisIndex: [0, 1] }],
        label: { backgroundColor: '#0f172a' },
        crossStyle: { color: 'rgba(15, 23, 42, 0.35)' },
      },
      backgroundColor: 'rgba(255,255,255,0.97)',
      borderColor: 'rgba(148, 163, 184, 0.24)',
      borderWidth: 1,
      padding: 10,
      textStyle: { color: '#334155' },
      formatter: (params: any) => {
        const dataIndex = params?.find((item: any) => item.seriesName === 'K线')?.dataIndex ?? params?.[0]?.dataIndex ?? 0
        const bar = bars[dataIndex]
        if (!bar) {
          return ''
        }

        return [
          `<div style="font-weight:700;color:#0f172a;margin-bottom:4px;">${bar.tradeDate}</div>`,
          `开盘：${formatNumber(bar.open)}`,
          `最高：${formatNumber(bar.high)}`,
          `最低：${formatNumber(bar.low)}`,
          `收盘：${formatNumber(bar.close)}`,
          `MA10：${formatNumber(ma10Series.value[dataIndex])}`,
          `MA20：${formatNumber(ma20Series.value[dataIndex])}`,
          `MA60：${formatNumber(ma60Series.value[dataIndex])}`,
          `成交量：${formatNumber(bar.volume / 10000, 2)} 万股`,
          `成交额：${formatNumber(bar.amount / 100000000, 2)} 亿`,
          `DIF：${formatNumber(macdSeries.value.dif[dataIndex])}`,
          `DEA：${formatNumber(macdSeries.value.dea[dataIndex])}`,
          `MACD：${formatNumber(macdSeries.value.histogram[dataIndex])}`,
        ].join('<br/>')
      },
    },
    xAxis: [
      {
        type: 'category',
        data: dates,
        boundaryGap: true,
        axisLine: { lineStyle: { color: '#cbd5e1' } },
        axisTick: { show: false },
        axisLabel: { color: '#94a3b8', interval: Math.max(Math.floor(dates.length / 4), 0) },
        splitLine: { show: false },
      },
      {
        type: 'category',
        gridIndex: 1,
        data: dates,
        boundaryGap: true,
        axisLine: { lineStyle: { color: '#cbd5e1' } },
        axisTick: { show: false },
        axisLabel: { color: '#94a3b8', interval: Math.max(Math.floor(dates.length / 4), 0) },
      },
      {
        type: 'category',
        gridIndex: 2,
        data: dates,
        boundaryGap: true,
        axisLine: { lineStyle: { color: '#cbd5e1' } },
        axisTick: { show: false },
        axisLabel: { color: '#94a3b8', interval: Math.max(Math.floor(dates.length / 4), 0) },
      },
    ],
    yAxis: [
      {
        type: 'value',
        name: '价格（元）',
        nameGap: 18,
        nameTextStyle: { color: '#475569', fontWeight: 700, padding: [0, 0, 8, 0] },
        scale: true,
        position: 'right',
        axisLine: { show: false },
        axisTick: { show: false },
        axisLabel: { color: '#64748b', margin: 10 },
        splitLine: { lineStyle: { color: 'rgba(148, 163, 184, 0.24)', type: 'dashed' } },
      },
      {
        type: 'value',
        gridIndex: 1,
        name: '成交量（万股）',
        nameGap: 18,
        nameTextStyle: { color: '#475569', fontWeight: 700, padding: [0, 0, 8, 0] },
        position: 'right',
        axisLine: { show: false },
        axisTick: { show: false },
        axisLabel: { color: '#64748b', margin: 10 },
        splitLine: { show: false },
      },
      {
        type: 'value',
        gridIndex: 2,
        name: 'MACD',
        nameGap: 16,
        nameTextStyle: { color: '#475569', fontWeight: 700, padding: [0, 0, 6, 0] },
        position: 'right',
        axisLine: { show: false },
        axisTick: { show: false },
        axisLabel: { color: '#64748b', margin: 10 },
        splitLine: { lineStyle: { color: 'rgba(148, 163, 184, 0.16)', type: 'dashed' } },
      },
    ],
    dataZoom: [{ type: 'inside', xAxisIndex: [0, 1, 2], start: 0, end: 100 }],
    series: [
      {
        name: 'K线',
        type: 'candlestick',
        data: candleSeries,
        itemStyle: {
          color: '#ef4444',
          color0: '#22c55e',
          borderColor: '#ef4444',
          borderColor0: '#22c55e',
        },
        markLine: markLineData.length
          ? {
              symbol: 'none',
              label: {
                color: '#0f172a',
                fontWeight: 700,
                distance: 6,
                position: 'insideEndTop',
                backgroundColor: 'rgba(255,255,255,0.88)',
                borderRadius: 8,
                padding: [2, 6],
                formatter: (item: any) => `${item.name} ${formatNumber(item.value)}`,
              },
              lineStyle: { type: 'dashed', width: 2 },
              data: markLineData.map((item, index) => ({
                ...item,
                lineStyle: {
                  color: index === 0 ? '#2563eb' : index === 1 ? '#ef4444' : '#16a34a',
                },
              })),
            }
          : undefined,
      },
      {
        name: 'MA10',
        type: 'line',
        data: ma10Series.value,
        showSymbol: false,
        smooth: 0.12,
        lineStyle: { width: 2, color: '#8b5cf6' },
      },
      {
        name: 'MA20',
        type: 'line',
        data: ma20Series.value,
        showSymbol: false,
        smooth: 0.12,
        lineStyle: { width: 2, color: '#0ea5e9' },
      },
      {
        name: 'MA60',
        type: 'line',
        data: ma60Series.value,
        showSymbol: false,
        smooth: 0.12,
        lineStyle: { width: 2, color: '#f59e0b' },
      },
      {
        name: '成交量',
        type: 'bar',
        xAxisIndex: 1,
        yAxisIndex: 1,
        barMaxWidth: 9,
        data: volumeSeries,
      },
      {
        name: 'MACD',
        type: 'bar',
        xAxisIndex: 2,
        yAxisIndex: 2,
        barMaxWidth: 8,
        data: macdHistogramSeries,
      },
      {
        name: 'DIF',
        type: 'line',
        xAxisIndex: 2,
        yAxisIndex: 2,
        data: macdSeries.value.dif,
        showSymbol: false,
        smooth: 0.12,
        lineStyle: { width: 1.8, color: '#2563eb' },
      },
      {
        name: 'DEA',
        type: 'line',
        xAxisIndex: 2,
        yAxisIndex: 2,
        data: macdSeries.value.dea,
        showSymbol: false,
        smooth: 0.12,
        lineStyle: { width: 1.8, color: '#7c3aed' },
      },
    ],
  }
}

async function renderChart() {
  await nextTick()

  if (!chartContainer.value || !props.stockDetail.recentBars.length) {
    return
  }

  if (!chartInstance) {
    chartInstance = init(chartContainer.value)
  }

  chartInstance?.setOption(buildDetailChartOption(), true)
  chartInstance?.resize()
}

function handleChartResize() {
  chartInstance?.resize()
}

onMounted(() => {
  window.addEventListener('resize', handleChartResize)
})

onBeforeUnmount(() => {
  window.removeEventListener('resize', handleChartResize)
  chartInstance?.dispose()
  chartInstance = null
})

watch(
  () => [
    props.stockDetail.stockCode,
    props.stockDetail.tradeDate,
    props.stockDetail.recentBars.length,
    props.stockDetail.latestBar.close,
  ],
  () => {
    renderChart()
  },
  { immediate: true },
)
</script>

<template>
  <div ref="chartContainer" class="echart-shell"></div>
</template>

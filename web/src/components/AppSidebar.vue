<script setup lang="ts">
export type ViewMode =
  | 'dashboard'
  | 'candidates'
  | 'signals'
  | 'positions'
  | 'industries'
  | 'fundFlows'
  | 'financials'
  | 'strategy'
  | 'learning'
  | 'backtests'
  | 'tasks'
  | 'system'

interface MenuItem {
  key: ViewMode
  title: string
  caption: string
}

interface MenuGroup {
  title: string
  items: MenuItem[]
}

const props = defineProps<{
  activeView: ViewMode
  candidateCount: number
  signalCount: number
  tradeDate: string
}>()

const emit = defineEmits<{
  (event: 'change-view', view: ViewMode): void
}>()

const menuGroups: MenuGroup[] = [
  {
    title: '主线',
    items: [
      { key: 'dashboard', title: '工作台', caption: '收盘总览和核心状态' },
      { key: 'candidates', title: '候选池', caption: '筛选值得继续观察的股票' },
      { key: 'signals', title: '交易信号', caption: '查看可执行信号和风控价位' },
      { key: 'positions', title: '模拟持仓', caption: '记录买卖和盈亏变化' },
    ],
  },
  {
    title: '分析',
    items: [
      { key: 'industries', title: '行业强度', caption: '比较行业热度和信号密度' },
      { key: 'fundFlows', title: '资金流向', caption: '查看行业和个股资金榜' },
      { key: 'financials', title: '全市场评分', caption: '查看所有股票的综合打分' },
      { key: 'strategy', title: '策略解释', caption: '用中文拆解评分和规则' },
      { key: 'learning', title: '学习复盘', caption: '沉淀交易经验' },
      { key: 'backtests', title: '回测验证', caption: '查看历史表现和回撤' },
    ],
  },
  {
    title: '管理',
    items: [
      { key: 'tasks', title: '任务中心', caption: '查看采集和同步任务' },
      { key: 'system', title: '系统状态', caption: '查看接口健康和版本' },
    ],
  },
]
</script>

<template>
  <aside class="sidebar">
    <div class="sidebar-brand">
      <p class="sidebar-eyebrow">A 股决策台</p>
      <h1>StockDecision</h1>
      <p class="sidebar-copy">把收盘后的选股、信号、复盘和回测放在同一个工作台里。</p>
    </div>

    <section class="sidebar-status">
      <p class="sidebar-label">当前概览</p>
      <dl>
        <div><dt>交易日</dt><dd>{{ props.tradeDate }}</dd></div>
        <div><dt>候选池</dt><dd>{{ props.candidateCount }}</dd></div>
        <div><dt>信号数</dt><dd>{{ props.signalCount }}</dd></div>
      </dl>
    </section>

    <nav class="sidebar-menu" aria-label="主菜单">
      <section v-for="group in menuGroups" :key="group.title" class="sidebar-group">
        <p class="sidebar-group-title">{{ group.title }}</p>
        <button
          v-for="item in group.items"
          :key="item.key"
          :class="['menu-button', { active: props.activeView === item.key }]"
          @click="emit('change-view', item.key)"
        >
          <strong>{{ item.title }}</strong>
          <span>{{ item.caption }}</span>
        </button>
      </section>
    </nav>
  </aside>
</template>

<script setup lang="ts">
export type ViewMode =
  | 'dashboard'
  | 'candidates'
  | 'signals'
  | 'positions'
  | 'industries'
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

const props = defineProps<{
  activeView: ViewMode
  candidateCount: number
  signalCount: number
  tradeDate: string
}>()

const emit = defineEmits<{
  (event: 'change-view', view: ViewMode): void
}>()

const menuItems: MenuItem[] = [
  { key: 'dashboard', title: '工作台', caption: '查看全局快照和收盘后的决策入口' },
  { key: 'candidates', title: '候选池', caption: '筛选并排序值得继续观察的股票' },
  { key: 'signals', title: '交易信号', caption: '查看可执行信号、仓位建议和风控价位' },
  { key: 'positions', title: '模拟持仓', caption: '记录模拟买卖、盈亏变化和交易流水' },
  { key: 'industries', title: '行业强度', caption: '比较行业热度、候选数量和信号密度' },
  { key: 'financials', title: '财务质量', caption: '从盈利能力和成长质量筛选基本面' },
  { key: 'strategy', title: '策略解释', caption: '用中文解释评分、过滤和执行规则' },
  { key: 'learning', title: '学习复盘', caption: '记录交易反思，把经验沉淀成可复用规则' },
  { key: 'backtests', title: '回测验证', caption: '运行历史回测并查看收益、回撤和样本明细' },
  { key: 'tasks', title: '任务中心', caption: '查看采集、同步和快照任务是否按计划运行' },
  { key: 'system', title: '系统状态', caption: '查看 API、策略版本和当前引用文档' },
]
</script>

<template>
  <aside class="sidebar">
    <div class="sidebar-brand">
      <p class="sidebar-eyebrow">A 股决策台</p>
      <h1>StockDecision</h1>
      <p class="sidebar-copy">
        把收盘后的候选筛选、交易信号、模拟持仓、回测验证、行业与财务分析放在同一个工作台里。
      </p>
    </div>

    <nav class="sidebar-menu" aria-label="主菜单">
      <button
        v-for="item in menuItems"
        :key="item.key"
        :class="['menu-button', { active: props.activeView === item.key }]"
        @click="emit('change-view', item.key)"
      >
        <strong>{{ item.title }}</strong>
        <span>{{ item.caption }}</span>
      </button>
    </nav>

    <section class="sidebar-status">
      <p class="sidebar-label">当前快照</p>
      <dl>
        <div><dt>交易日</dt><dd>{{ props.tradeDate }}</dd></div>
        <div><dt>候选池</dt><dd>{{ props.candidateCount }}</dd></div>
        <div><dt>信号数</dt><dd>{{ props.signalCount }}</dd></div>
      </dl>
    </section>
  </aside>
</template>

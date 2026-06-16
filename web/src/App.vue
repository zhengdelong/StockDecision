<script setup lang="ts">
const documents = [
  '指标词典',
  '2万元交易策略',
  '数据源与字段',
  '回测规则',
  '学习与复盘',
]
</script>

<template>
  <main class="shell">
    <section class="header">
      <p class="eyebrow">A股收盘后决策辅助</p>
      <h1>2万元本金盈利导向策略工作台</h1>
      <p class="summary">
        第一阶段先完成策略文档、数据边界和系统骨架；后续实现候选股、交易计划、回测和学习复盘。
      </p>
    </section>

    <section class="panel">
      <h2>已落盘文档</h2>
      <ul>
        <li v-for="item in documents" :key="item">{{ item }}</li>
      </ul>
    </section>
  </main>
</template>

<style scoped>
.shell {
  max-width: 1080px;
  margin: 0 auto;
  padding: 48px 24px;
  color: #1f2937;
  font-family: Inter, 'Microsoft YaHei', sans-serif;
}

.header {
  border-bottom: 1px solid #d8dee9;
  padding-bottom: 28px;
}

.eyebrow {
  color: #0f766e;
  font-size: 14px;
  font-weight: 700;
}

h1 {
  margin: 8px 0 12px;
  font-size: 36px;
  line-height: 1.2;
}

.summary {
  max-width: 760px;
  color: #4b5563;
  line-height: 1.7;
}

.panel {
  margin-top: 28px;
  border: 1px solid #d8dee9;
  border-radius: 8px;
  padding: 20px;
}

.panel h2 {
  margin-top: 0;
  font-size: 20px;
}

li {
  line-height: 1.9;
}
</style>

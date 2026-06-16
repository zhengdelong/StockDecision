# 数据源与字段

## 数据源原则

v1 只使用免费数据源，优先使用 AKShare。Python 采集层负责适配数据源，C# 后端只读取标准化数据库，不直接依赖第三方数据接口。

数据库采用 MySQL 8.4，字符集统一使用 `utf8mb4`。Python 采集器只写 raw 表，DDD 后端的 Application/Infrastructure 层再把 raw 数据导入领域表。交易规则、指标计算、评分和信号生成只能在 C# 领域/Application 层执行。

## AKShare 优先接口

| 数据类型 | AKShare 接口 | 用途 |
|---|---|---|
| A 股历史行情 | `stock_zh_a_hist` | 日线开高低收、成交量、成交额、涨跌幅、换手率 |
| A 股实时快照 | `stock_zh_a_spot_em` | 全市场报价、成交额、估值字段 |
| 财务指标 | `stock_financial_analysis_indicator` | ROE、净利润、营收等排雷字段 |
| 指数行情 | 指数相关接口 | 沪深300、中证500、创业板指市场环境判断 |
| 行业板块 | 行业板块相关接口 | 板块强度、行业排名 |

AKShare 文档地址：

```text
https://akshare.akfamily.xyz/data/stock/stock.html
```

## MySQL 表分层

MySQL 表分两类：

1. raw 表：由 Python 采集器写入，只负责保存免费数据源的标准化结果和采集日志。
2. domain 表：由 .NET Application/Infrastructure 写入，承载领域模型、策略结果、交易信号、模拟持仓、回测和学习复盘。

Python 不能直接写交易信号、评分、持仓和回测表，避免把交易规则分散到两个技术栈里。

### raw 表

| 表名 | 写入方 | 用途 |
|---|---|---|
| `raw_stocks` | Python collector | 股票基础信息原始导入 |
| `raw_daily_bars` | Python collector | 日线行情原始导入 |
| `raw_financial_snapshots` | Python collector | 估值和财务快照原始导入 |
| `raw_market_index_bars` | Python collector | 指数行情原始导入 |
| `raw_industry_daily_stats` | Python collector | 行业强度原始导入 |
| `data_ingestion_logs` | Python collector | 采集日志、缺失字段、异常信息 |

### domain 表

| 表名 | 写入方 | 用途 |
|---|---|---|
| `stocks` | .NET Application | 股票领域数据 |
| `daily_bars` | .NET Application | 清洗后的日线行情 |
| `financial_snapshots` | .NET Application | 清洗后的估值财务 |
| `market_indices` | .NET Application | 指数行情 |
| `industry_daily_stats` | .NET Application | 行业强度 |
| `technical_indicators` | .NET Application | 技术指标结果 |
| `stock_scores` | .NET Application | 评分结果 |
| `trade_signals` | .NET Application | 交易信号 |
| `positions` | .NET Application | 模拟持仓 |
| `backtest_runs` | .NET Application | 回测任务 |
| `backtest_trades` | .NET Application | 回测交易 |
| `learning_notes` | .NET Application | 学习解释和复盘 |

## 行情字段

表：`daily_bars`

| 字段 | 类型 | 说明 | 必需 |
|---|---|---|---|
| `stock_code` | string | 股票代码 | 是 |
| `trade_date` | date | 交易日期 | 是 |
| `open` | decimal | 开盘价 | 是 |
| `high` | decimal | 最高价 | 是 |
| `low` | decimal | 最低价 | 是 |
| `close` | decimal | 收盘价 | 是 |
| `volume` | long | 成交量 | 是 |
| `amount` | decimal | 成交额 | 是 |
| `amplitude` | decimal | 振幅 | 否 |
| `pct_change` | decimal | 涨跌幅 | 是 |
| `turnover_rate` | decimal | 换手率 | 否 |
| `adjust_type` | string | 复权类型 | 是 |

行情数据用于：

- 计算均线。
- 判断突破和回踩。
- 计算成交额放大。
- 计算 ATR14。
- 回测买卖。

## 股票基础字段

表：`stocks`

| 字段 | 类型 | 说明 |
|---|---|---|
| `code` | string | 股票代码 |
| `name` | string | 股票名称 |
| `market` | string | 交易市场 |
| `industry` | string | 所属行业 |
| `list_date` | date | 上市日期 |
| `is_st` | bool | 是否 ST |
| `is_delisting_risk` | bool | 是否退市风险 |
| `is_active` | bool | 是否仍在交易 |

基础字段用于股票池过滤。

## 财务与估值字段

表：`financial_snapshots`

| 字段 | 类型 | 说明 | 用途 |
|---|---|---|---|
| `stock_code` | string | 股票代码 | 关联 |
| `report_date` | date | 报告日期 | 判断新旧 |
| `pe` | decimal | 市盈率 | 估值过滤 |
| `pb` | decimal | 市净率 | 估值过滤 |
| `roe` | decimal | 净资产收益率 | 质量过滤 |
| `revenue_yoy` | decimal | 营收同比 | 基本面兜底 |
| `net_profit_yoy` | decimal | 净利润同比 | 排雷 |
| `free_float_market_cap` | decimal | 流通市值 | 流动性辅助 |
| `operating_cash_flow` | decimal | 经营现金流 | 后续扩展 |

财务数据只做排雷和加分，不作为主买入依据。

## 指数与行业字段

表：`market_indices`

| 字段 | 类型 | 说明 |
|---|---|---|
| `index_code` | string | 指数代码 |
| `index_name` | string | 指数名称 |
| `trade_date` | date | 日期 |
| `open` | decimal | 开盘 |
| `high` | decimal | 最高 |
| `low` | decimal | 最低 |
| `close` | decimal | 收盘 |
| `amount` | decimal | 成交额 |

表：`industry_daily_stats`

| 字段 | 类型 | 说明 |
|---|---|---|
| `industry_code` | string | 行业代码 |
| `industry_name` | string | 行业名称 |
| `trade_date` | date | 日期 |
| `pct_change_20d` | decimal | 行业 20 日涨幅 |
| `rank_20d` | int | 行业 20 日排名 |
| `member_count` | int | 成分股数量 |

## 指标字段

表：`technical_indicators`

| 字段 | 说明 |
|---|---|
| `ma20` | 20 日均线 |
| `ma60` | 60 日均线 |
| `ma120` | 120 日均线 |
| `atr14` | 14 日平均真实波幅 |
| `pct_change_10d` | 10 日涨幅 |
| `pct_change_20d` | 20 日涨幅 |
| `pct_change_60d` | 60 日涨幅 |
| `rs_rank_20d` | 20 日相对强度排名百分位 |
| `rs_rank_60d` | 60 日相对强度排名百分位 |
| `is_20d_high` | 是否 20 日收盘新高 |
| `amount_avg_5d` | 5 日平均成交额 |
| `amount_avg_20d` | 20 日平均成交额 |
| `distance_to_ma20` | 收盘价距离 MA20 的比例 |
| `has_long_upper_shadow` | 是否长上影 |

## 缺失处理

### 行情缺失

- 缺少开高低收：该股票该日不参与计算。
- 连续缺失超过 5 个交易日：标记为数据异常。
- 回测期间缺少买卖价格：跳过对应交易，不用前值填充。

### 财务缺失

- PE、PB、ROE、营收同比、净利润同比缺失时，不直接淘汰。
- 如果缺失超过 3 个核心财务字段，则基本面分最高只能得 8 分。
- 如果净利润同比明确低于 -30%，直接触发财务排雷。

### 行业缺失

- 行业缺失时，不参与强势板块加分。
- 行业缺失股票不能进入强势突破策略，只能进入学习观察。

## 数据质量日志

每次采集必须记录：

- 数据源名称。
- 接口名称。
- 拉取开始时间和结束时间。
- 成功记录数。
- 失败记录数。
- 缺失字段统计。
- 异常股票列表。

如果当日关键数据采集失败，系统不能生成“可买”信号，只能展示“数据不足，暂停交易计划”。

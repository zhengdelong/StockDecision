# 数据源与字段

## 数据源原则

v1 只使用免费数据源，优先使用 AKShare。Python 采集层负责适配数据源，C# 后端只读取标准化数据库，不直接依赖第三方数据接口。

数据库采用 MySQL 8.4，字符集统一使用 `utf8mb4`。Python 采集器只写 raw 表，DDD 后端的 Application/Infrastructure 层再把 raw 数据导入领域表。交易规则、指标计算、评分和信号生成只能在 C# 领域/Application 层执行。

## AKShare 适用性与约束

### 为什么 v1 先用 AKShare

- 免费，可覆盖 A 股日线、指数、财务、行业等核心数据。
- Python 接入成本低，适合先把“选股和回测闭环”跑起来。
- 社区使用广，后续如果某些接口变动，替换成本低于直接对接多个网站。

### 已知限制

AKShare 不是交易所官方统一 API 网关，而是对多个公开站点数据做适配。限制主要来自上游站点，不是 AKShare 自己提供一个明确的“每日 10 万次”这种硬配额。

基于 AKShare 官方文档和答疑页，可以确认三件事：

1. 官方没有公开统一的固定 IP 次数上限或统一 QPS 上限。
2. 出现 `ReadTimeout` 时，官方建议降低访问频率。
3. 某些上游数据源访问过于频繁时，可能需要更换 IP 后重试。

因此系统设计不能假设“免费接口永远稳定可无限并发”，必须把限流、重试、回填和降级当成一等公民。

### v1 运行假设

v1 不追求分钟级、秒级交易信号，只做收盘后日线决策，因此按“低频、保守、可回补”的方式使用 AKShare：

- 只拉日线，不拉盘口、逐笔、Level-2。
- 优先使用单次可返回较多历史数据的接口，减少总请求数。
- 不在盘中高频轮询。
- 对容易被风控的接口使用串行或小批量模式。
- 任何关键数据当日不完整时，直接禁止生成“可买”信号。

## AKShare 优先接口

| 数据类型 | AKShare 接口 | 抓取粒度 | 用途 |
|---|---|---|---|
| 股票列表/基础信息 | `stock_zh_a_spot_em` | 全市场快照 | 股票代码、名称、最新价、成交额、PE/PB 等基础字段 |
| A 股历史行情 | `stock_zh_a_hist` | 单股票、按日期区间 | 日线开高低收、成交量、成交额、涨跌幅、换手率 |
| 财务指标 | `stock_financial_analysis_indicator` | 单股票、按报告期序列 | ROE、营收同比、净利润同比等排雷字段 |
| 指数历史行情 | 指数相关日线接口 | 单指数、按日期区间 | 沪深300、中证500、创业板指市场环境判断 |
| 行业/板块快照 | 行业板块相关接口 | 全量或板块维度 | 板块强度、行业排名、板块热度 |

AKShare 文档地址：

```text
https://akshare.akfamily.xyz/data/stock/stock.html
https://akshare.akfamily.xyz/answer.html
```

## 采集范围与数据量

### v1 股票池范围

先限制在与你的 2 万本金相匹配、流动性相对较好的范围：

- 沪深主板
- 创业板
- 排除 ST、*ST、退市整理
- 排除上市未满 250 个交易日的新股

这个范围通常是 3500 到 4500 只股票级别，不含北交所、不含 ETF、不含可转债。

### 全量初始化需要拉多少数据

#### 1. 股票基础信息

- 数据量：全市场 1 份快照
- 更新频率：每天 1 次
- 用途：更新股票列表、名称、行业、是否活跃

#### 2. 个股日线

这是回测和指标计算的最大头。

v1 建议：

- 回测最少保留 5 个完整自然年日线
- 实际初始化拉取 6 到 8 年日线
- 推荐默认值：最近 8 年

原因：

- `MA120` 需要至少 120 个交易日预热。
- 20 日、60 日、120 日均线和相对强度排名都需要预热区。
- 只拉 5 年会导致回测起点附近样本偏脏，前几个月大量指标不稳定。

粗略量级估算：

- 股票数按 4000 只估算
- 8 年交易日按 8 * 250 = 2000 日估算
- 日线记录量约 `4000 * 2000 = 8,000,000` 行

这不是小数据量，但对 MySQL 完全可承受，前提是：

- raw 表和 domain 表都建好 `(stock_code, trade_date)` 复合索引
- 批量写入
- 分阶段导入，不要一次性把所有指标也算完

#### 3. 指数日线

至少拉以下 3 个指数最近 10 年：

- 沪深300
- 中证500
- 创业板指

10 年足够覆盖牛熊切换，数据量很小，优先一次拉全。

#### 4. 财务与估值快照

v1 不是做基本面深度因子，所以不需要把十几年财报全补齐。

建议范围：

- 最近 12 个报告期的财务指标
- 最近 2 到 3 年为重点使用区间
- 当天估值快照以日级或收盘后快照为准

#### 5. 行业/板块数据

建议至少保留最近 3 年行业日度强弱数据，足够支撑行业排名和相对强弱加分。

## 增量采集节奏

### 每日运行窗口

#### 15:30 - 16:30

拉取：

- 股票列表快照
- 个股日线增量
- 指数日线增量
- 行业/板块日度数据

说明：

- A 股收盘后再跑，避免拿到未收盘或盘中变动数据。
- 若 15:30 刚开始时部分上游站点未完全更新，可在 16:10 再补一轮。

#### 18:00 - 21:00

拉取：

- 财务指标
- 估值补充字段
- 当天失败补拉任务

说明：

- 财务类接口稳定性通常不如纯行情，放在晚间单独跑。

#### 21:00 - 22:00

C# Worker 执行：

- raw 数据完整性检查
- raw 导入 domain
- 指标计算
- 评分计算
- 信号生成
- 回测快检

如果任一关键数据集未完成，则当天只允许输出“观察”状态，不输出“可买”。

## 限流、重试与封禁防护

### 官方信息能确认到什么程度

截至当前文档版本，AKShare 官方文档没有给出统一的“每 IP 每分钟多少次”硬阈值。官方答疑页明确提到：

- 遇到 `ReadTimeout` 时要降低访问频率
- 某些场景可以更换 IP 后重试

因此下面的频率不是“AKShare 官方上限”，而是本系统为了稳定运行自行采用的保守工程策略。

### v1 保守频率策略

#### 股票列表与全市场快照类接口

例如 `stock_zh_a_spot_em` 这类一次返回大量股票的接口：

- 每次任务最多调用 1 到 3 次
- 两次调用之间至少间隔 3 秒
- 不做循环轮询

#### 单股票历史日线类接口

例如 `stock_zh_a_hist`：

- 初始化阶段：串行或最多 3 并发
- 单请求之间 sleep `0.3s ~ 0.8s`
- 每处理 100 只股票，额外休眠 20 到 60 秒

如果按 4000 只股票估算，首次全量初始化可能需要数小时到十几小时，这是正常的。v1 接受“慢但稳”，不接受“快但今天能跑明天被封”。

#### 财务类接口

这类接口更容易超时或字段波动：

- 默认串行
- 单请求之间 sleep `0.8s ~ 1.5s`
- 连续失败 5 次后，暂停当前财务任务 10 分钟

### 重试策略

- 第 1 次失败：等待 5 秒重试
- 第 2 次失败：等待 15 秒重试
- 第 3 次失败：等待 60 秒重试
- 同一股票/同一接口最多重试 3 次

超过 3 次仍失败：

- 写入 `data_ingestion_logs`
- 标记该股票/该日期采集失败
- 不阻塞其他股票继续采集

### 熔断策略

如果某接口出现以下任一情况，则触发熔断，停止当前批次：

- 连续失败 20 次
- 5 分钟内失败率超过 30%
- 上游返回明显的反爬页面或空白结构

熔断后动作：

- 结束当前任务
- 记录日志
- 将当天状态标记为“数据不完整”
- 当天禁止生成“可买”信号

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

## raw 表关键审计字段

除了业务字段，raw 层必须统一保留下列审计字段：

| 字段 | 说明 |
|---|---|
| `source_name` | 数据源名称，如 `akshare` |
| `interface_name` | AKShare 接口名 |
| `symbol` | 股票代码或指数代码 |
| `trade_date` | 行情对应日期 |
| `report_date` | 财务报告期 |
| `fetched_at` | 实际抓取时间 |
| `batch_id` | 本次采集批次号 |
| `is_incremental` | 是否增量任务 |
| `payload_hash` | 原始内容哈希，辅助去重 |
| `retry_count` | 重试次数 |
| `missing_field_count` | 缺失字段数量 |
| `ingestion_status` | `success` / `partial` / `failed` |
| `error_message` | 错误信息摘要 |

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
- 若某交易日全市场日线覆盖率低于 95%，当天全局状态记为“不可交易日”。

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
- 是否触发重试。
- 是否触发熔断。
- 是否允许当日策略继续执行。

如果当日关键数据采集失败，系统不能生成“可买”信号，只能展示“数据不足，暂停交易计划”。

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

- 观察池默认包含沪深主板和创业板
- 可交易池由账户权限开关控制，例如是否允许创业板
- 排除 ST、*ST、退市整理
- 排除上市未满 250 个交易日的新股

这个范围通常是 3500 到 4500 只股票级别，不含北交所、不含 ETF、不含可转债。

### 可交易池开关

v1 必须支持账户权限开关，不能把“哪些市场可下单”写死在代码里。

最少支持：

- `EnableMainBoard`
- `EnableChiNext`

推荐后续扩展：

- `EnableBeijingBoard`
- `EnableETF`

规则：

- 开关只控制“能不能进入可买可执行结果”
- 不控制“能不能进入观察和学习结果”
- 因此同一只股票可以处于观察池，但不在可交易池

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

## 最稳初始化方案

如果你的首要目标是：

- 不被限流
- 不因为一次全量初始化把 IP 风险拉高
- 不因为中断导致整批重跑

那么 v1 初始化必须采用“最稳方案”，而不是“最快方案”。

### 初始化总原则

- 不盘中抓取，只在收盘后或晚间抓取
- 不追求一天抓完
- 不做高并发
- 不因单批失败重跑全部
- 不在 raw 数据还没抓稳时同步做全量指标重算

### 初始化执行顺序

按下面顺序分阶段执行：

1. 指数日线
2. 股票基础信息
3. 个股历史日线
4. 财务快照
5. 行业/板块数据
6. raw -> domain 导入
7. 指标计算
8. 评分与回测

这样做的原因是：

- 指数和股票基础信息量小，先抓可以尽早验证链路
- 个股日线是大头，必须单独控速
- 财务和行业数据不应和日线初始化混跑

### 最稳参数

#### 适用范围

适用于首次初始化 5 到 6 年历史数据，目标股票池约 1500 到 4000 只。

#### 推荐执行参数

| 项目 | 最稳值 |
|---|---:|
| 运行时段 | 每天晚间 2 到 4 小时 |
| 单股票日线接口并发数 | 1 |
| 单请求间隔 | 0.8 秒 |
| 每批股票数量 | 50 只 |
| 每批暂停时间 | 60 秒 |
| 财务接口并发数 | 1 |
| 财务接口单请求间隔 | 1.5 秒 |
| 单股票最大重试次数 | 3 次 |
| 单日最大连续失败数 | 20 次 |

这套参数的核心目标不是快，而是把自己控制在“正常低频使用者”范围内。

### 预计执行方式

如果按 4000 只股票估算，并且每批 50 只：

- 约 80 批
- 每批股票之间按单请求间隔逐个抓
- 每批结束休息 60 秒

这意味着初始化很可能要分多天完成。这个结果是可接受的，也是推荐的。

### 续跑与检查点

初始化任务必须支持续跑，不能设计成“中断就从头再来”。

至少要记录这些信息：

| 字段 | 说明 |
|---|---|
| `job_type` | 任务类型，如 `daily_bar_bootstrap` |
| `batch_id` | 当前批次号 |
| `stock_code` | 股票代码 |
| `status` | `pending` / `success` / `failed` / `skipped` |
| `last_success_date` | 最后成功抓取日期 |
| `retry_count` | 已重试次数 |
| `error_message` | 最近一次错误摘要 |
| `updated_at` | 最近更新时间 |

续跑规则：

- 已成功股票不重复抓
- 失败股票进入下一轮重试队列
- 中断后从下一个未完成批次继续

### 初始化期间禁止事项

- 不允许盘中抓全市场历史日线
- 不允许 10 并发以上抓单股票历史接口
- 不允许失败后立即无限重试
- 不允许“今天必须抓完”驱动高频重跑
- 不允许 raw 未完成时直接产出交易信号

### 初始化完成标准

以下条件同时满足，才算初始化完成：

- 指数历史日线已补齐目标范围
- 股票基础信息已完成至少一次全量快照
- 目标股票池历史日线覆盖率 >= 95%
- 财务快照已完成最近 12 个报告期的可用抓取
- 行业数据已完成目标范围初始化
- raw 导入 domain 成功
- 核心指标已完成首轮计算
- 回测已完成一次基础跑通

如果以上任一条件不满足：

- 初始化状态保持为 `partial`
- 系统不得展示“可买”信号
- 只允许展示“数据初始化未完成”

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

## 建议表结构

下面是 v1 建议直接落到 MySQL 里的表结构方向。这里先定义“存什么、主键怎么定、索引怎么建”，字段精度以后可以在 EF Core Migration 里一比一实现。

### raw 层表结构

#### `raw_stocks`

```sql
CREATE TABLE raw_stocks (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    stock_code VARCHAR(16) NOT NULL,
    stock_name VARCHAR(64) NOT NULL,
    market VARCHAR(16) NULL,
    industry_name VARCHAR(64) NULL,
    list_date DATE NULL,
    is_st TINYINT(1) NOT NULL DEFAULT 0,
    is_delisting_risk TINYINT(1) NOT NULL DEFAULT 0,
    is_active TINYINT(1) NOT NULL DEFAULT 1,
    source_name VARCHAR(32) NOT NULL,
    interface_name VARCHAR(64) NOT NULL,
    fetched_at DATETIME(6) NOT NULL,
    batch_id VARCHAR(64) NOT NULL,
    payload_hash CHAR(64) NULL,
    raw_payload JSON NULL,
    created_at DATETIME(6) NOT NULL,
    UNIQUE KEY uk_raw_stocks_batch_code (batch_id, stock_code),
    KEY idx_raw_stocks_code (stock_code),
    KEY idx_raw_stocks_fetched_at (fetched_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
```

#### `raw_daily_bars`

```sql
CREATE TABLE raw_daily_bars (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    stock_code VARCHAR(16) NOT NULL,
    trade_date DATE NOT NULL,
    open DECIMAL(18,4) NULL,
    high DECIMAL(18,4) NULL,
    low DECIMAL(18,4) NULL,
    close DECIMAL(18,4) NULL,
    volume BIGINT NULL,
    amount DECIMAL(20,2) NULL,
    amplitude DECIMAL(10,4) NULL,
    pct_change DECIMAL(10,4) NULL,
    turnover_rate DECIMAL(10,4) NULL,
    adjust_type VARCHAR(16) NOT NULL,
    source_name VARCHAR(32) NOT NULL,
    interface_name VARCHAR(64) NOT NULL,
    fetched_at DATETIME(6) NOT NULL,
    batch_id VARCHAR(64) NOT NULL,
    is_incremental TINYINT(1) NOT NULL DEFAULT 0,
    payload_hash CHAR(64) NULL,
    retry_count INT NOT NULL DEFAULT 0,
    missing_field_count INT NOT NULL DEFAULT 0,
    ingestion_status VARCHAR(16) NOT NULL,
    error_message VARCHAR(512) NULL,
    raw_payload JSON NULL,
    created_at DATETIME(6) NOT NULL,
    UNIQUE KEY uk_raw_daily_bars_code_date_adjust (stock_code, trade_date, adjust_type),
    KEY idx_raw_daily_bars_trade_date (trade_date),
    KEY idx_raw_daily_bars_batch_id (batch_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
```

#### `raw_financial_snapshots`

```sql
CREATE TABLE raw_financial_snapshots (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    stock_code VARCHAR(16) NOT NULL,
    report_date DATE NOT NULL,
    pe DECIMAL(18,4) NULL,
    pb DECIMAL(18,4) NULL,
    roe DECIMAL(18,4) NULL,
    revenue_yoy DECIMAL(18,4) NULL,
    net_profit_yoy DECIMAL(18,4) NULL,
    free_float_market_cap DECIMAL(20,2) NULL,
    operating_cash_flow DECIMAL(20,2) NULL,
    source_name VARCHAR(32) NOT NULL,
    interface_name VARCHAR(64) NOT NULL,
    fetched_at DATETIME(6) NOT NULL,
    batch_id VARCHAR(64) NOT NULL,
    payload_hash CHAR(64) NULL,
    retry_count INT NOT NULL DEFAULT 0,
    missing_field_count INT NOT NULL DEFAULT 0,
    ingestion_status VARCHAR(16) NOT NULL,
    error_message VARCHAR(512) NULL,
    raw_payload JSON NULL,
    created_at DATETIME(6) NOT NULL,
    UNIQUE KEY uk_raw_financial_code_report (stock_code, report_date),
    KEY idx_raw_financial_batch_id (batch_id),
    KEY idx_raw_financial_report_date (report_date)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
```

#### `raw_market_index_bars`

```sql
CREATE TABLE raw_market_index_bars (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    index_code VARCHAR(16) NOT NULL,
    index_name VARCHAR(64) NOT NULL,
    trade_date DATE NOT NULL,
    open DECIMAL(18,4) NULL,
    high DECIMAL(18,4) NULL,
    low DECIMAL(18,4) NULL,
    close DECIMAL(18,4) NULL,
    amount DECIMAL(20,2) NULL,
    source_name VARCHAR(32) NOT NULL,
    interface_name VARCHAR(64) NOT NULL,
    fetched_at DATETIME(6) NOT NULL,
    batch_id VARCHAR(64) NOT NULL,
    payload_hash CHAR(64) NULL,
    retry_count INT NOT NULL DEFAULT 0,
    ingestion_status VARCHAR(16) NOT NULL,
    error_message VARCHAR(512) NULL,
    raw_payload JSON NULL,
    created_at DATETIME(6) NOT NULL,
    UNIQUE KEY uk_raw_index_code_date (index_code, trade_date),
    KEY idx_raw_index_trade_date (trade_date)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
```

#### `raw_industry_daily_stats`

```sql
CREATE TABLE raw_industry_daily_stats (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    industry_code VARCHAR(32) NOT NULL,
    industry_name VARCHAR(64) NOT NULL,
    trade_date DATE NOT NULL,
    pct_change_20d DECIMAL(10,4) NULL,
    rank_20d INT NULL,
    member_count INT NULL,
    source_name VARCHAR(32) NOT NULL,
    interface_name VARCHAR(64) NOT NULL,
    fetched_at DATETIME(6) NOT NULL,
    batch_id VARCHAR(64) NOT NULL,
    payload_hash CHAR(64) NULL,
    retry_count INT NOT NULL DEFAULT 0,
    ingestion_status VARCHAR(16) NOT NULL,
    error_message VARCHAR(512) NULL,
    raw_payload JSON NULL,
    created_at DATETIME(6) NOT NULL,
    UNIQUE KEY uk_raw_industry_code_date (industry_code, trade_date),
    KEY idx_raw_industry_trade_date (trade_date)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
```

#### `data_ingestion_logs`

```sql
CREATE TABLE data_ingestion_logs (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    batch_id VARCHAR(64) NOT NULL,
    source_name VARCHAR(32) NOT NULL,
    interface_name VARCHAR(64) NOT NULL,
    target_scope VARCHAR(64) NOT NULL,
    trade_date DATE NULL,
    report_date DATE NULL,
    started_at DATETIME(6) NOT NULL,
    finished_at DATETIME(6) NULL,
    success_count INT NOT NULL DEFAULT 0,
    failure_count INT NOT NULL DEFAULT 0,
    missing_field_count INT NOT NULL DEFAULT 0,
    consecutive_failure_count INT NOT NULL DEFAULT 0,
    window_failure_rate DECIMAL(8,4) NULL,
    is_incremental TINYINT(1) NOT NULL DEFAULT 0,
    is_complete TINYINT(1) NOT NULL DEFAULT 0,
    is_signal_eligible TINYINT(1) NOT NULL DEFAULT 0,
    circuit_breaker_opened TINYINT(1) NOT NULL DEFAULT 0,
    error_message VARCHAR(1024) NULL,
    created_at DATETIME(6) NOT NULL,
    KEY idx_ingestion_logs_batch_id (batch_id),
    KEY idx_ingestion_logs_trade_date (trade_date),
    KEY idx_ingestion_logs_interface_name (interface_name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
```

### domain 层核心表结构

#### `stocks`

```sql
CREATE TABLE stocks (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    code VARCHAR(16) NOT NULL,
    name VARCHAR(64) NOT NULL,
    market VARCHAR(16) NOT NULL,
    industry VARCHAR(64) NULL,
    list_date DATE NULL,
    is_st TINYINT(1) NOT NULL DEFAULT 0,
    is_delisting_risk TINYINT(1) NOT NULL DEFAULT 0,
    is_active TINYINT(1) NOT NULL DEFAULT 1,
    updated_at DATETIME(6) NOT NULL,
    UNIQUE KEY uk_stocks_code (code),
    KEY idx_stocks_market_active (market, is_active)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
```

#### `daily_bars`

```sql
CREATE TABLE daily_bars (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    stock_id BIGINT NOT NULL,
    stock_code VARCHAR(16) NOT NULL,
    trade_date DATE NOT NULL,
    open DECIMAL(18,4) NOT NULL,
    high DECIMAL(18,4) NOT NULL,
    low DECIMAL(18,4) NOT NULL,
    close DECIMAL(18,4) NOT NULL,
    volume BIGINT NOT NULL,
    amount DECIMAL(20,2) NOT NULL,
    amplitude DECIMAL(10,4) NULL,
    pct_change DECIMAL(10,4) NOT NULL,
    turnover_rate DECIMAL(10,4) NULL,
    adjust_type VARCHAR(16) NOT NULL,
    imported_at DATETIME(6) NOT NULL,
    UNIQUE KEY uk_daily_bars_code_date_adjust (stock_code, trade_date, adjust_type),
    KEY idx_daily_bars_stock_id_date (stock_id, trade_date),
    KEY idx_daily_bars_trade_date (trade_date)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
```

#### `financial_snapshots`

```sql
CREATE TABLE financial_snapshots (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    stock_id BIGINT NOT NULL,
    stock_code VARCHAR(16) NOT NULL,
    report_date DATE NOT NULL,
    pe DECIMAL(18,4) NULL,
    pb DECIMAL(18,4) NULL,
    roe DECIMAL(18,4) NULL,
    revenue_yoy DECIMAL(18,4) NULL,
    net_profit_yoy DECIMAL(18,4) NULL,
    free_float_market_cap DECIMAL(20,2) NULL,
    operating_cash_flow DECIMAL(20,2) NULL,
    imported_at DATETIME(6) NOT NULL,
    UNIQUE KEY uk_financial_snapshots_code_report (stock_code, report_date),
    KEY idx_financial_snapshots_stock_id_report (stock_id, report_date)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
```

#### `market_indices`

```sql
CREATE TABLE market_indices (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    index_code VARCHAR(16) NOT NULL,
    index_name VARCHAR(64) NOT NULL,
    trade_date DATE NOT NULL,
    open DECIMAL(18,4) NOT NULL,
    high DECIMAL(18,4) NOT NULL,
    low DECIMAL(18,4) NOT NULL,
    close DECIMAL(18,4) NOT NULL,
    amount DECIMAL(20,2) NULL,
    imported_at DATETIME(6) NOT NULL,
    UNIQUE KEY uk_market_indices_code_date (index_code, trade_date),
    KEY idx_market_indices_trade_date (trade_date)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
```

#### `industry_daily_stats`

```sql
CREATE TABLE industry_daily_stats (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    industry_code VARCHAR(32) NOT NULL,
    industry_name VARCHAR(64) NOT NULL,
    trade_date DATE NOT NULL,
    pct_change_20d DECIMAL(10,4) NULL,
    rank_20d INT NULL,
    member_count INT NULL,
    imported_at DATETIME(6) NOT NULL,
    UNIQUE KEY uk_industry_daily_stats_code_date (industry_code, trade_date),
    KEY idx_industry_daily_stats_trade_date (trade_date)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
```

#### `technical_indicators`

```sql
CREATE TABLE technical_indicators (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    stock_id BIGINT NOT NULL,
    stock_code VARCHAR(16) NOT NULL,
    trade_date DATE NOT NULL,
    ma20 DECIMAL(18,4) NULL,
    ma60 DECIMAL(18,4) NULL,
    ma120 DECIMAL(18,4) NULL,
    atr14 DECIMAL(18,4) NULL,
    pct_change_10d DECIMAL(10,4) NULL,
    pct_change_20d DECIMAL(10,4) NULL,
    pct_change_60d DECIMAL(10,4) NULL,
    rs_rank_20d DECIMAL(10,4) NULL,
    rs_rank_60d DECIMAL(10,4) NULL,
    is_20d_high TINYINT(1) NOT NULL DEFAULT 0,
    amount_avg_5d DECIMAL(20,2) NULL,
    amount_avg_20d DECIMAL(20,2) NULL,
    distance_to_ma20 DECIMAL(10,4) NULL,
    has_long_upper_shadow TINYINT(1) NOT NULL DEFAULT 0,
    strategy_version VARCHAR(32) NOT NULL,
    calculated_at DATETIME(6) NOT NULL,
    UNIQUE KEY uk_technical_indicators_code_date_version (stock_code, trade_date, strategy_version),
    KEY idx_technical_indicators_trade_date (trade_date)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
```

#### `stock_scores`

```sql
CREATE TABLE stock_scores (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    stock_id BIGINT NOT NULL,
    stock_code VARCHAR(16) NOT NULL,
    trade_date DATE NOT NULL,
    strategy_version VARCHAR(32) NOT NULL,
    total_score DECIMAL(10,2) NOT NULL,
    relative_strength_score DECIMAL(10,2) NOT NULL,
    trend_score DECIMAL(10,2) NOT NULL,
    volume_price_score DECIMAL(10,2) NOT NULL,
    fundamental_score DECIMAL(10,2) NOT NULL,
    grade VARCHAR(4) NOT NULL,
    score_reason TEXT NULL,
    calculated_at DATETIME(6) NOT NULL,
    UNIQUE KEY uk_stock_scores_code_date_version (stock_code, trade_date, strategy_version),
    KEY idx_stock_scores_trade_date_grade (trade_date, grade)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
```

#### `trade_signals`

```sql
CREATE TABLE trade_signals (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    stock_id BIGINT NOT NULL,
    stock_code VARCHAR(16) NOT NULL,
    signal_date DATE NOT NULL,
    strategy_version VARCHAR(32) NOT NULL,
    strategy_type VARCHAR(32) NOT NULL,
    signal_type VARCHAR(16) NOT NULL,
    signal_status VARCHAR(16) NOT NULL,
    suggested_buy_amount DECIMAL(20,2) NULL,
    trigger_price DECIMAL(18,4) NULL,
    stop_loss_price DECIMAL(18,4) NULL,
    target_price DECIMAL(18,4) NULL,
    expected_profit DECIMAL(20,2) NULL,
    max_loss_amount DECIMAL(20,2) NULL,
    risk_reward_ratio DECIMAL(10,4) NULL,
    explanation TEXT NULL,
    created_at DATETIME(6) NOT NULL,
    UNIQUE KEY uk_trade_signals_code_date_strategy (stock_code, signal_date, strategy_version, strategy_type, signal_type),
    KEY idx_trade_signals_signal_date_status (signal_date, signal_status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
```

#### `positions`

```sql
CREATE TABLE positions (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    stock_id BIGINT NOT NULL,
    stock_code VARCHAR(16) NOT NULL,
    strategy_version VARCHAR(32) NOT NULL,
    entry_signal_id BIGINT NULL,
    entry_date DATE NOT NULL,
    entry_price DECIMAL(18,4) NOT NULL,
    quantity INT NOT NULL,
    cost_amount DECIMAL(20,2) NOT NULL,
    latest_price DECIMAL(18,4) NULL,
    unrealized_pnl DECIMAL(20,2) NULL,
    stop_loss_price DECIMAL(18,4) NULL,
    status VARCHAR(16) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    KEY idx_positions_status_entry_date (status, entry_date),
    KEY idx_positions_stock_code_status (stock_code, status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
```

#### `backtest_runs`

```sql
CREATE TABLE backtest_runs (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    strategy_version VARCHAR(32) NOT NULL,
    strategy_scope VARCHAR(64) NOT NULL,
    start_date DATE NOT NULL,
    end_date DATE NOT NULL,
    initial_capital DECIMAL(20,2) NOT NULL,
    final_capital DECIMAL(20,2) NULL,
    total_return_rate DECIMAL(10,4) NULL,
    annual_return_rate DECIMAL(10,4) NULL,
    max_drawdown_rate DECIMAL(10,4) NULL,
    win_rate DECIMAL(10,4) NULL,
    profit_loss_ratio DECIMAL(10,4) NULL,
    trade_count INT NOT NULL DEFAULT 0,
    skipped_trade_days INT NOT NULL DEFAULT 0,
    data_coverage_rate DECIMAL(10,4) NULL,
    status VARCHAR(16) NOT NULL,
    summary_json JSON NULL,
    created_at DATETIME(6) NOT NULL,
    completed_at DATETIME(6) NULL,
    KEY idx_backtest_runs_version_period (strategy_version, start_date, end_date),
    KEY idx_backtest_runs_status (status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
```

#### `backtest_trades`

```sql
CREATE TABLE backtest_trades (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    backtest_run_id BIGINT NOT NULL,
    stock_code VARCHAR(16) NOT NULL,
    strategy_type VARCHAR(32) NOT NULL,
    entry_date DATE NOT NULL,
    exit_date DATE NULL,
    entry_price DECIMAL(18,4) NOT NULL,
    exit_price DECIMAL(18,4) NULL,
    quantity INT NOT NULL,
    gross_pnl DECIMAL(20,2) NULL,
    net_pnl DECIMAL(20,2) NULL,
    total_fee DECIMAL(20,2) NULL,
    return_rate DECIMAL(10,4) NULL,
    exit_reason VARCHAR(32) NULL,
    created_at DATETIME(6) NOT NULL,
    KEY idx_backtest_trades_run_id (backtest_run_id),
    KEY idx_backtest_trades_stock_code (stock_code),
    KEY idx_backtest_trades_entry_date (entry_date)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
```

#### `learning_notes`

```sql
CREATE TABLE learning_notes (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    stock_code VARCHAR(16) NOT NULL,
    signal_id BIGINT NULL,
    trade_date DATE NULL,
    note_type VARCHAR(32) NOT NULL,
    title VARCHAR(128) NOT NULL,
    content TEXT NOT NULL,
    tags VARCHAR(256) NULL,
    created_at DATETIME(6) NOT NULL,
    KEY idx_learning_notes_stock_code (stock_code),
    KEY idx_learning_notes_trade_date (trade_date),
    KEY idx_learning_notes_note_type (note_type)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
```

### 设计约束

- raw 表保留 `raw_payload`，方便接口字段变动时回放和排查。
- domain 表不保留大块原始 JSON，避免领域表变成垃圾仓库。
- 所有“按日期查”的表都必须建日期索引。
- 所有“股票 + 日期”的事实表都必须建复合唯一键，防止重复导入。
- `strategy_version` 必须进入指标、评分、信号、回测相关表，避免以后改规则时覆盖旧结果。
- 金额字段统一优先用 `DECIMAL(20,2)`，价格优先用 `DECIMAL(18,4)`，比率优先用 `DECIMAL(10,4)`。

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

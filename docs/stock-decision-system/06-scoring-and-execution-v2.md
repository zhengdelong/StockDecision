# 个股评分与交易执行计划 v2

## 1. 目标与定位

本规格定义 `a-share-20k-v2` 的正式策略规则，覆盖以下两部分：

- 个股评分机制重构
- 交易执行计划与持仓管理

本版本仍然是 `收盘后日线决策系统`，不是分时追板系统，也不是自动交易程序。系统目标是：

1. 在现有趋势框架下，提升候选股与交易信号的质量。
2. 为每只股票输出可执行的买入、止损、持有、退出计划。
3. 用免费数据源增加“资金确认”和“事件扰动”识别能力。
4. 让回测、前端展示、持仓管理与实际执行口径保持一致。

本版本的新增数据：

- 资金流向
- 龙虎榜

它们只作为 `辅助确认因子 / 风险标签 / 解释增强`，不作为单独的收益保证。

## 2. 旧版问题审计

### 2.1 相对强弱名不副实

当前实现中 `RelativeStrengthScore` 实际等于 `20 日涨幅`，本质不是“相对强弱”，而是绝对涨幅的重复表达。

问题：

- 与 `Return20d` 重复描述同一件事。
- 没有体现相对指数、相对行业、相对全市场的超额表现。
- 导致“相对强弱”维度实际上只是趋势延续维度的重复加分。

v2 改法：

- `RelativeStrengthScore` 不再直接等于 `Return20d`。
- 相对强弱改为超额收益与分位数体系。

### 2.2 重复计分

当前规则中 `距离 MA20 不超过 10%` 同时出现在多个维度里。

问题：

- 同一形态约束跨维度重复加分。
- 总分容易被单一走势特征放大。

v2 改法：

- 每条规则只允许归属一个评分维度。
- `距离 MA20` 仅归属趋势质量。

### 2.3 量价规则区分度不足

当前 `averageAmount20d` 规则近似无条件给分，只是分值不同。

问题：

- 不能体现流动性差异。
- 容易把“仅仅够交易”和“非常容易交易”的股票都打成高分。

v2 改法：

- 流动性先做硬过滤，再做分层加分。
- 成交额和换手率都做区间化处理。

### 2.4 PE/PB 固定阈值跨行业失真

当前使用统一 `PE <= 80`、`PB <= 8`。

问题：

- 银行、周期、消费、科技的估值口径不同。
- 固定阈值只能作为异常过滤，不适合作强评分。

v2 改法：

- 估值从“核心加分项”降级为“温和约束项”。
- 先保留异常估值惩罚，不直接做行业相对估值分位。

### 2.5 风险收益比过度参与评分

当前 `RR` 的一部分来自目标价和止损价的经验常数。

问题：

- 经验参数会把打分结果“造强”。
- 容易把执行计划误当 alpha 来源。

v2 改法：

- `RR` 不再主导总分。
- `RR` 只作为执行准入门槛和解释字段。

### 2.6 市场环境粒度过粗

当前市场环境只适合作总开关，不适合指导策略细分。

问题：

- 突破策略与回踩策略在弱市中的容忍度不同。
- 只用一个总开关会让解释不足。

v2 改法：

- 市场状态继续保留 `Strong / Tradable / WeakOpportunity / NoTrade`
- 但在执行层区分：
  - 是否允许突破型信号
  - 是否仅允许回踩型观察或执行

### 2.7 只有信号，没有完整执行计划

当前系统虽然给出了：

- 触发价
- 止损价
- 目标价

但没有明确：

- 什么时候买
- 高开多少放弃
- 最多持有多久
- 什么时候视为计划失效
- 什么时候按趋势走坏退出

v2 改法：

- 新增正式的 `TradeExecutionPlan` 规格。

### 2.8 回测与用户看到的执行规则没有完全统一

问题：

- 用户看到的是“信号 + 数字”
- 回测跑的是“内部简化执行”
- 结果与真实使用口径不完全一致

v2 改法：

- 买入、止损、目标位、最大持有天数、超时退出、趋势失效退出统一进入回测。

### 2.9 缺少资金确认与事件扰动识别

问题：

- 仅用价格和成交额，无法识别主力持续流入。
- 无法识别龙虎榜事件中的机构参与和高热风险。

v2 改法：

- 接入资金流与龙虎榜，并作为量价确认和风险标签的补充。

## 3. v2 评分体系

### 3.1 总体结构

总分固定为 `100`，分为四个维度：

- `相对强弱 30`
- `趋势质量 30`
- `量价确认 25`
- `基本面质量 15`

执行准入独立于总分：

- 市场状态
- 回测准入
- 账户权限
- 风险收益比

### 3.2 硬过滤条件

以下条件不满足时，不进入评分：

- 不在观察池
- ST、*ST、退市整理、退市风险
- 上市不满 `250` 个交易日
- 收盘价 `<= MA60`
- `ATR14 / Close > 7%`
- 最新价格 `< 5` 或 `> 80`
- `20 日平均成交额 < 2 亿元`
- 最近 `10` 日涨幅 `> 30%`

### 3.3 相对强弱 30 分

#### 原始指标

- `return20d`
- `return60d`
- `industry_pct_change_20d`
- `index_return20d`
- `index_return60d`
- `rs20_excess_vs_index = return20d - index_return20d`
- `rs20_excess_vs_industry = return20d - industry_pct_change_20d`
- `rs60_excess_vs_index = return60d - index_return60d`
- `rs_market_percentile`

#### 评分规则

| 规则 | 条件 | 分数 |
|---|---|---:|
| 20日超额收益强 | `rs20_excess_vs_index > 0` | 8 |
| 行业内更强 | `rs20_excess_vs_industry > 0` | 7 |
| 60日超额收益强 | `rs60_excess_vs_index > 0` | 7 |
| 市场相对强度分位高 | `rs_market_percentile >= 80` | 5 |
| 未过热 | `10日涨幅 <= 25%` | 3 |

#### 说明

- 这里不再使用“距离 MA20 不超过 10%”。
- 这里不再直接使用 `RelativeStrengthScore = return20d`。

### 3.4 趋势质量 30 分

#### 原始指标

- `close`
- `ma20`
- `ma60`
- `ma120`
- `is_ma20_upward`
- `is_ma60_upward`
- `is_bullish_stacked`
- `is_20day_breakout`
- `distance_to_ma20_pct`
- `atr14_pct = atr14 / close * 100`

#### 评分规则

| 规则 | 条件 | 分数 |
|---|---|---:|
| 收盘站上 MA20 | `close > ma20` | 4 |
| 收盘站上 MA60 | `close > ma60` | 5 |
| 收盘站上 MA120 | `close > ma120` | 3 |
| 均线多头排列 | `ma20 > ma60 > ma120` | 8 |
| MA20 上行 | `is_ma20_upward` | 4 |
| MA60 上行 | `is_ma60_upward` | 3 |
| 20日收盘突破 | `is_20day_breakout` | 2 |
| 距离 MA20 不过远 | `distance_to_ma20_pct <= 10` | 1 |

#### 说明

- `distance_to_ma20_pct` 只在本维度出现一次。
- `atr14_pct` 不加分，只用于约束波动。

### 3.5 量价确认 25 分

#### 原始指标

- `turnover_rate`
- `average_amount_20d`
- `amount_today / average_amount_20d`
- `industry_rank_20d`
- `fund_flow_1d_main_net_pct`
- `fund_flow_3d_main_net_pct`
- `fund_flow_5d_rank_percentile`
- `industry_fund_flow_rank_percentile`
- `lhb_recent_20d_count`
- `lhb_institution_net_buy`
- `lhb_risk_flag`

#### 评分规则

| 规则 | 条件 | 分数 |
|---|---|---:|
| 放量确认 | `amount_ratio_1d >= 1.5` | 6 |
| 换手率健康 | `2% <= turnover_rate <= 8%` | 4 |
| 流动性优秀 | `average_amount_20d >= 15 亿` | 4 |
| 流动性良好 | `5 亿 <= average_amount_20d < 15 亿` | 2 |
| 行业强度前10 | `industry_rank_20d <= 10` | 3 |
| 1日主力净流入为正 | `fund_flow_1d_main_net_pct > 0` | 2 |
| 3日主力净流入持续为正 | `fund_flow_3d_main_net_pct > 0` | 2 |
| 5日资金流分位较高 | `fund_flow_5d_rank_percentile >= 80` | 1 |
| 行业资金流较强 | `industry_fund_flow_rank_percentile >= 80` | 1 |

#### 龙虎榜增强与惩罚

- `机构净买入`：额外 `+1`
- `高热风险标签`：额外 `-1 ~ -3`

#### 说明

- 龙虎榜不单独作为第五维。
- 量价确认维度允许出现负分修正，但本维度最低不低于 `0`。

### 3.6 基本面质量 15 分

#### 原始指标

- `roe`
- `revenue_yoy`
- `net_profit_yoy`
- `pe`
- `pb`
- `financial_missing_count`

#### 评分规则

| 规则 | 条件 | 分数 |
|---|---|---:|
| ROE 健康 | `roe >= 8` | 5 |
| 营收同比为正 | `revenue_yoy > 0` | 4 |
| 净利润同比为正 | `net_profit_yoy > 0` | 4 |
| 估值无极端异常 | `pe > 0` 且 `pb > 0` 且 `pb <= 8` | 2 |

#### 缺失降级

- 核心财务字段缺失 `>= 3` 项：
  - 本维度最高只给 `6` 分
  - 打上 `财务信息不足` 标签

### 3.7 分级与准入

| 总分 | 状态 |
|---:|---|
| `>= 90` | A，优先候选 |
| `88-89.99` | B+，强观察 |
| `85-87.99` | B，观察 |
| `80-84.99` | C，学习观察 |
| `< 80` | D，不进入候选池 |

执行准入：

- 候选池下限：`80`
- 可执行观察下限：`88`
- 可执行信号默认下限：`90`
- 同时满足：
  - 在可交易池
  - 市场状态允许
  - 回测准入通过
  - `RR >= 1.8`

## 4. 资金流向接入规格

### 4.1 数据源

使用现有 collector 依赖的 AKShare：

- `stock_individual_fund_flow`
- `stock_individual_fund_flow_rank`
- 行业资金流接口

底层来源主要为东方财富公开页面。

### 4.2 原始表

新增：

- `raw_stock_fund_flows`
- `raw_industry_fund_flows`

#### raw_stock_fund_flows

主键建议：

- `stock_code + trade_date`

字段：

- `stock_code`
- `trade_date`
- `main_net_amount`
- `main_net_pct`
- `super_large_net_amount`
- `super_large_net_pct`
- `large_net_amount`
- `large_net_pct`
- `medium_net_amount`
- `medium_net_pct`
- `small_net_amount`
- `small_net_pct`
- `source_name`
- `interface_name`
- `fetched_at`
- `batch_id`
- `payload_hash`
- `retry_count`
- `missing_field_count`
- `ingestion_status`
- `error_message`
- `raw_payload`
- `created_at`

#### raw_industry_fund_flows

主键建议：

- `industry_name + trade_date`

字段：

- `industry_name`
- `trade_date`
- `main_net_amount`
- `main_net_pct`
- `rank`
- 审计字段同上

### 4.3 领域层

新增领域快照：

- `StockFundFlowSnapshot`
- `IndustryFundFlowSnapshot`

新增市场表：

- `market_stock_fund_flows`
- `market_industry_fund_flows`

### 4.4 缺失与降级策略

- 资金流缺失不阻塞全系统。
- 当日单股资金流缺失：
  - 不给资金流正分
  - 打 `资金流缺失` 标签
- 行业资金流缺失：
  - 不做行业资金流加分
- 若整日资金流抓取失败：
  - 允许候选生成
  - 禁止把“资金确认”写成肯定语气

## 5. 龙虎榜接入规格

### 5.1 数据源

使用现有 AKShare 能力：

- `stock_lhb_detail_em`
- `stock_lhb_stock_detail_date_em`
- `stock_lhb_stock_detail_em`

### 5.2 原始表

新增：

- `raw_lhb_events`
- `raw_lhb_stock_summaries`

#### raw_lhb_events

主键建议：

- `stock_code + trade_date + side + seat_name`

字段：

- `stock_code`
- `trade_date`
- `side`
- `seat_name`
- `seat_type`
- `buy_amount`
- `sell_amount`
- `net_amount`
- `reason`
- 审计字段同 raw 表统一规范

#### raw_lhb_stock_summaries

主键建议：

- `stock_code + trade_date`

字段：

- `stock_code`
- `trade_date`
- `reason`
- `buy_top5_amount`
- `sell_top5_amount`
- `net_amount`
- `institution_buy_amount`
- `institution_sell_amount`
- `institution_net_amount`
- `institution_buy_count`
- `is_institution_net_buy`
- 审计字段同 raw 表统一规范

### 5.3 领域层

新增：

- `LhbSnapshot`

新增市场表：

- `market_lhb_snapshots`

领域聚合字段：

- `is_on_lhb_today`
- `lhb_reason`
- `buy_top5_amount`
- `sell_top5_amount`
- `net_amount`
- `institution_net_buy_amount`
- `is_institution_net_buy`
- `recent_20d_lhb_count`
- `days_since_last_lhb`
- `risk_flags`

### 5.4 使用原则

正向增强：

- `机构净买入`
- `近 1-3 日存在机构参与`

风险标签：

- 连续上榜次数过多
- 纯游资席位主导
- 上榜后短期过热
- 离 MA20 太远仍高热冲顶

默认策略：

- 龙虎榜不作为硬过滤
- 龙虎榜不单独生成买点
- 龙虎榜只增强解释、量价确认和风险提示

## 6. 交易执行计划

### 6.1 目标

系统对每只候选或信号股必须输出：

- 何时买
- 什么情况下放弃
- 初始止损
- 趋势失效止损
- 目标位退出
- 最多持有多久
- 当前为什么是这个计划

### 6.2 执行计划对象

统一定义 `TradeExecutionPlan`：

- `entry_rule`
- `risk_rule`
- `hold_rule`
- `exit_rule`
- `invalidation_rule`
- `teaching_explanation`

### 6.3 Breakout 买入规则

适用：

- `Breakout`
- `WatchBreakout`

默认规则：

- 次日最大允许高开：`3%`
- 观察窗口：`2 个交易日`
- 若开盘高开超过 `3%`，计划失效，原因：`高开过多不追`
- 若观察窗口内始终未有效触发突破，计划失效
- 若触发日前收盘已重新跌回关键突破区下方，计划失效

### 6.4 PullbackToMa20 买入规则

适用：

- `PullbackToMa20`
- `WatchPullback`

默认规则：

- 次日最大允许高开：`2%`
- 观察窗口：`3 个交易日`
- 回踩 MA20 容忍区：`MA20 上下 2%`
- 若收盘跌破 MA20 且次日不能修复，计划失效

### 6.5 初始止损

#### Breakout

- 技术止损：`close * 0.97`
- 资金止损：基于单笔最大亏损计算
- 最终止损：取更保守者

#### PullbackToMa20

- 技术止损：`MA20 * 0.98`
- 资金止损：基于单笔最大亏损计算
- 最终止损：取更保守者

### 6.6 趋势失效止损

#### Breakout

任一满足则建议退出：

- 买入后 `2-3` 天仍无法站稳突破位
- 收盘重新跌回关键均线下
- 放量长阴破坏突破结构

#### PullbackToMa20

任一满足则建议退出：

- 收盘有效跌破 MA20
- MA20 拐头向下
- 回踩后 `2` 天内无法重回强势

### 6.7 目标位与止盈

默认目标位：

- `Breakout = entry * 1.12`
- `PullbackToMa20 = entry * 1.10`

止盈原则：

- 默认不做复杂分批止盈
- 达到目标位时给出 `建议止盈`
- 若趋势极强，可在持仓页以“移动止损继续持有”形式解释，但 v2 默认回测口径仍以单目标退出为主

### 6.8 最大持有天数

默认固定：

- `Breakout = 6 个交易日`
- `PullbackToMa20 = 10 个交易日`

到期后若未达目标位，且走势没有超预期增强，则建议退出。

### 6.9 风险收益比准入

- `RR < 1.8`：不可执行
- `1.8 <= RR < 2.2`：弱通过
- `RR >= 2.2`：正常通过

`RR` 不再提供大量加分，只作为执行 gate 与解释项。

### 6.10 计划失效条件

任一满足则进入 `PlanInvalidated`：

- 高开超过允许阈值
- 超过观察窗口未触发
- 形态走坏
- 市场状态降级
- 回测准入失效

## 7. 持仓管理规则

### 7.1 输出目标

持仓页必须对每只持仓输出：

- 已持有天数
- 当前计划阶段
- 当前止损价
- 当前目标位
- 今日建议动作
- 中文说明

### 7.2 建议动作枚举

- `继续持有`
- `提高警惕`
- `接近退出`
- `建议止盈`
- `建议止损`
- `计划失效`

### 7.3 每日跟踪规则

#### Breakout

- 若继续站在关键均线之上，继续持有
- 若逼近第 `6` 天仍无明显进展，接近退出
- 若达到目标位，建议止盈
- 若回到突破位下方且走弱，建议止损或趋势退出

#### PullbackToMa20

- 若继续围绕 MA20 向上运行，继续持有
- 若逼近第 `10` 天仍无扩散，接近退出
- 若跌破 MA20 且修复失败，建议止损

### 7.4 移动止损

v2 默认只在持仓页显示，不直接进入首轮评分：

- 达到明显盈利后，允许将止损上移到成本附近
- 但默认回测第一版不引入复杂分批，只允许简单移动止损扩展

## 8. 领域模型、数据库与接口变更

### 8.1 新增 raw 表

- `raw_stock_fund_flows`
- `raw_industry_fund_flows`
- `raw_lhb_events`
- `raw_lhb_stock_summaries`

### 8.2 新增 market/domain 表

- `market_stock_fund_flows`
- `market_industry_fund_flows`
- `market_lhb_snapshots`

### 8.3 新增领域对象

- `StockFundFlowSnapshot`
- `IndustryFundFlowSnapshot`
- `LhbSnapshot`
- `TradeExecutionPlan`
- `PositionManagementAdvice`

### 8.4 Repository 扩展

`IRawMarketDataRepository` 新增：

- 按日读取资金流原始快照
- 按日读取龙虎榜汇总快照

`IMarketDataRepository` 新增：

- 按股票读取资金流快照
- 按行业读取资金流快照
- 按股票读取龙虎榜快照
- 写入资金流领域快照
- 写入龙虎榜领域快照

### 8.5 API/DTO 扩展

新增 DTO：

- `TradeExecutionPlanResponse`
- `EntryRuleResponse`
- `HoldRuleResponse`
- `ExitRuleResponse`
- `PositionManagementAdviceResponse`

更新现有响应：

- `CandidateListItemResponse`
- `SignalListItemResponse`
- `StockDetailResponse`
- `SimulatedPositionItemResponse`

新增字段至少包含：

- 计划摘要
- 最大持有天数
- 计划失效原因
- 资金流摘要
- 龙虎榜摘要
- 风险标签

### 8.6 前端展示

个股抽屉新增卡片：

- `交易计划`
- `资金流向`
- `龙虎榜事件`

持仓页新增：

- `持仓管理建议`
- `已持有天数`
- `当前计划状态`

列表页保持简洁，只增加简短标签：

- `资金流转强`
- `机构参与`
- `龙虎榜事件`

## 9. 回测与验收规格

### 9.1 回测必须同步的规则

回测必须使用与 v2 执行计划一致的口径：

- 最大高开限制
- 观察窗口失效
- 初始止损
- 趋势失效退出
- 目标位退出
- 最大持有天数

### 9.2 新增回测指标

- 平均持有天数
- 目标位退出次数
- 止损退出次数
- 趋势失效退出次数
- 超时退出次数
- 数据覆盖率

### 9.3 验收方式

v2 不以“必须盈利”作为唯一目标，而以“相对 v1 有增量”为准。

至少满足以下之一：

- 扣成本后总收益提升
- 盈亏比提升
- 最大回撤下降
- 弱市误报减少

必须增加：

- `v1 vs v2` 同区间对比
- 滚动窗口对比
- 样本外观察

### 9.4 数据覆盖要求

若资金流或龙虎榜覆盖率不足：

- 回测报告必须单独显示覆盖率
- 不允许用低覆盖结果做策略准入

## 10. 覆盖关系与版本管理

本文件是 `a-share-20k-v2` 的正式规格。

它对以下内容具有覆盖效力：

- `02-trading-strategy-20k.md` 中与评分、买点、止损、止盈、持有计划相关的旧规则
- `04-backtest-rules.md` 中与执行计划和退出口径相关的旧规则
- `03-data-source-and-fields.md` 中未定义的资金流和龙虎榜数据源与字段

版本规则：

- `v1` 结果保留，用于对比与回溯
- `v2` 使用新的 `strategy_version`
- 不允许以 v2 规则覆盖旧 v1 历史结果

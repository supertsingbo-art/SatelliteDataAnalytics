# 卫星测试数据预处理与一致性比对平台

## 原型设计文档（中保真线框 + Figma 规范）

### V1.1

本文档在《总体设计文档》《需求分析说明文档》基础上，给出可交付 UI/前端的**中保真线框说明**：页面信息结构、字段清单、表格列定义、关键交互与 **Figma Frame 命名规范**。本地独立 HTML 原型见 `prototype/index.html`。

---

## 1. 设计范围与角色可见性

| 页面/能力 | 管理员 | 分析员 | 只读员 |
|-----------|--------|--------|--------|
| 登录 | ✓ | ✓ | ✓ |
| 工作台 | ✓ | ✓ | ✓ |
| 任务监控（触发/重试/取消） | ✓ | 触发/补跑/重试（按策略） | 仅查看 |
| 品质数据与标记（预处理结果） | ✓ | ✓ | 仅查看 |
| 模板中心（新建/发布/灰度） | ✓ | ✓ | 仅查看 |
| 算法仓库（上传） | ✓ | ✓（可灰度） | ✗ |
| 比对报告（下载） | ✓ | ✓ | ✓ |
| 审计与血缘 | ✓ | 部分 | 仅列表 |

---

## 2. 全局布局（所有业务页）

### 2.1 区域划分

- **顶栏（高度 56px）**  
  - 左：Logo、产品名、环境标签（DEV/UAT/PROD）  
  - 中：全局搜索（占位：任务 RunId / 卫星 ID / 测试批次）  
  - 右：帮助、当前用户、退出  

- **左侧导航（宽度 220px，可折叠）**  
  - 工作台  
  - 任务监控  
  - **品质数据与标记**（预处理写入 ClickHouse 后的明细与质量标记）  
  - 模板中心（子菜单见下）  
  - 算法仓库  
  - 比对报告  
  - 审计与血缘  

- **主内容区**  
  - 面包屑  
  - 页面标题 + 主操作（Primary 一个）  
  - 筛选条（折叠）  
  - 内容区（表格 / 表单 / 分栏）  

### 2.2 全局约束展示（固定文案区，可选组件）

在「任务详情」「数据预览」相关 Tab 展示：

- 业务约束：**不插值、不去重**；离群值**保留并标记**。

---

## 3. 页面级中保真线框

### 3.1 登录 `/login`

**字段**

| 字段 | 类型 | 校验 |
|------|------|------|
| username | 文本 | 必填 |
| password | 密码 | 必填 |
| rememberMe | 开关 | 可选 |

**操作**：登录、忘记密码（可选二期）

**状态**：错误提示区（认证失败原因，不暴露敏感细节）

---

### 3.2 工作台 `/dashboard`

**模块 A：指标卡片（一行 4 格）**

| 卡片 | 指标说明 |
|------|----------|
| 今日任务 | 成功数 / 失败数 / 运行中 |
| 失败待处理 | 需人工关注的失败任务数 |
| 最近 24h 报告 | 生成报告数量 |

**模块 B：最近任务表格**

| 列名 | 字段 key | 宽度建议 |
|------|----------|----------|
| RunId | runId | 180 |
| 类型 | jobType | 120 |
| 卫星 | satelliteId | 100 |
| 测试批次 | testBatchId | 140 |
| 时间窗 | timeWindow | 220 |
| 状态 | status | 100 |
| 耗时 | durationSec | 100 |
| 操作 | actions | 120 |

`jobType` 枚举：`PREPROCESS` | `ALGORITHM` | `COMPARE` | `REPORT`

`status` 枚举：`PENDING` | `RUNNING` | `SUCCESS` | `FAILED` | `CANCELLED` | `TIMEOUT`

**主操作**：跳转任务监控、新建筛选模板（权限控制）

---

### 3.3 任务监控 `/jobs`

**筛选条字段**

| 字段 | 类型 | 说明 |
|------|------|------|
| satelliteId | 下拉/搜索 | 可选 |
| testBatchId | 文本 | 可选 |
| jobType | 多选 | 可选 |
| status | 多选 | 可选 |
| timeRange | 日期时间范围 | 默认近 7 天 |
| templateVersion | 文本 | 可选 |

**表格列（主列表）**

| 列名 | 字段 key | 说明 |
|------|----------|------|
| RunId | runId | 可点击进详情 |
| 触发方式 | triggerType | `SCHEDULED` / `MANUAL` / `BACKFILL` |
| 任务类型 | jobType | 见上 |
| 卫星 | satelliteId | |
| 测试批次 | testBatchId | |
| 时间窗 | windowStart ~ windowEnd | ISO 显示 |
| 模板版本 | templateVersionSnapshot | 快照串 |
| 状态 | status | Tag 色 |
| 开始时间 | startedAt | |
| 结束时间 | endedAt | |
| 超时 | isTimeout | 布尔 |
| 操作 | actions | 查看 / 重试 / 取消 |

**批量操作**（分析员+）：补跑、回补（打开侧栏表单）

**行内操作**

- 查看详情（抽屉或 `/jobs/:runId`）
- 重试（失败且允许时）
- 取消（运行中且允许时）

---

### 3.4 任务详情 `/jobs/:runId`

**页头**

- 标题：`任务 {runId}`
- Tag：状态、是否超时、任务类型
- 操作：重试、取消、导出日志（权限控制）

**Tab 1：概览**

| 分组 | 字段 |
|------|------|
| 输入 | satelliteId, testBatchId, windowStart, windowEnd |
| 模板 | filterTemplateVersion, algorithmTemplateVersion, reportTemplateVersion（如有） |
| 灰度 | grayScope 摘要（卫星/批次/时间窗） |
| 触发 | triggerType, createdBy, createdAt |
| 约束说明 | 固定文案：不插值、不去重；离群保留标记 |

**独立页 / Tab：品质与标记**

- 与「预处理完成」强关联：跳转至 **`/quality`（或 `quality-data.html` 原型）**，展示本任务对应的 ClickHouse 高品质明细及标记字段（见下节 3.5）。
- 任务详情顶栏可提供快捷按钮：「打开品质数据与标记」。

**Tab 2：阶段流水线（建议）**

| 阶段 | 状态 | 开始 | 结束 | 说明 |
|------|------|------|------|------|
| 预处理 | | | | |
| 算法 | | | | |
| 比对 | | | | |
| 报告 | | | | |

（一期可为单任务单类型，多阶段为扩展预留）

**Tab 3：日志**

- 结构化日志表格：时间、级别、步骤、消息
- 原始日志下载链接（如有）

**Tab 4：血缘**

- 文本时间线或简化图：Mongo 范围 → ClickHouse batch → compare result id → MinIO report object

**Tab 5：失败信息**（仅失败/超时）

- errorCode、message、stack 摘要、建议动作

---

### 3.5 品质数据与标记 `/quality`（`prototype/quality-data.html`）

**页面目标**：展示预处理任务写入 **ClickHouse** 的高品质明细，体现**数据标记**能力（非插值、非去重前提下的离群与质量元数据）。

**筛选条字段**

| 字段 | 说明 |
|------|------|
| batch_run_id / runId | 关联预处理任务运行 ID |
| satelliteId、testBatchId | 业务过滤 |
| paramId | 参数过滤 |
| 时间范围 | ts 起止 |
| 仅看离群 | 快速过滤 is_outlier = true |

**摘要卡片（可选）**

- 总点数、离群点数、正常点数、当前生效的离群规则/模板版本摘要

**明细表格列（与库表对齐）**

| 列名 | 字段 key | 说明 |
|------|----------|------|
| 时间戳 | ts | 统一时基 |
| 参数 | paramId | |
| 单位 | unit | |
| 原始值 | raw_value | 来自解析后的原始侧 |
| 处理值 | processed_value | 写入分析层（可与 raw 相同，预留换算） |
| 是否离群 | is_outlier | 布尔或枚举 |
| 判定方法 | outlier_method | 阈值 / 3σ / IQR / MAD 等 |
| 原因 | outlier_reason | 可读说明 |
| 模板版本 | filter_template_version 等 | 可追溯 |
| 质量标志 | quality_flags | 位标志或标签串 |

**行样式**：离群行可用浅色背景区分（仅 UI 提示，**不表示数据被删除**）。

**导出**：CSV/Parquet（权限控制，原型可用「导出」按钮占位）。

---

### 3.6 模板中心 — 筛选模板

#### 3.6.1 列表 `/templates/filter`

**表格列**

| 列名 | 字段 key |
|------|----------|
| 名称 | name |
| 当前版本 | currentVersion |
| 状态 | status（DRAFT/PUBLISHED/DISABLED） |
| 灰度摘要 | graySummary |
| 更新时间 | updatedAt |
| 操作 | actions |

**操作**：新建、复制、编辑、发布、停用、灰度配置

#### 3.6.2 编辑器 `/templates/filter/:id/edit`

**左栏：基础信息**

| 字段 | 类型 | 校验 |
|------|------|------|
| name | 文本 | 必填，最长 64 |
| description | 多行 | 可选 |
| tags | 标签多选 | 可选 |

**右栏：灰度发布**

| 字段 | 类型 |
|------|------|
| gray.satelliteIds | 多选/标签 |
| gray.testBatchIds | 多选/标签 |
| gray.timeWindow | 起止时间（可选，表示仅在该窗内生效） |

**规则构建区**

**A. 条件参数（用于求有效时间段）**

- 参数列表表格：paramId、比较关系、下限、上限、单位（只读来自 ICD 可选）
- 逻辑：AND/OR/NOT 组合（树形编辑器或受控表达式 + 语法校验）

**B. 时间约束**

| 字段 | 类型 | 说明 |
|------|------|------|
| minDurationSec | 数字 | 持续满足最短时长 |
| bufferBeforeSec | 数字 | 边界前扩 |
| bufferAfterSec | 数字 | 边界后扩 |

**C. 目标参数（在有效段内抽取）**

- 表格：paramId、别名（可选）、备注  
- 支持 CSV/Excel 批量导入 paramId

**底部操作**

- 保存草稿、校验规则、发布、预览有效段（小时间窗试跑）

**预览区（可选）**

- 时间轴：有效段高亮条带（示意）

---

### 3.7 模板中心 — 算法模板 `/templates/algorithm/:id/edit`

**布局**：上工具栏 | 左侧组件库 | 中间连线画布 | 右侧参数配置（借鉴 Dify、Coze 等大模型工作流编排平台的交互模式，**采用 React Flow 框架实现**）

**节点库分组（一级）**

- 统计：max, min, mean, variance, std, rms  
- 时域/趋势：trend（定义具体子类型在配置里）  
- 频域：fft / psd（一期可只 fft）  
- 相关：corr  
- 包络：envelope  
- 自定义：python / js（灰度开关）

**画布节点通用属性**

| 字段 | 说明 |
|------|------|
| nodeId | 画布内唯一 |
| nodeType | 枚举 |
| title | 展示名 |
| inputs | 绑定上游输出名 |
| params | JSON，按节点类型 schema 校验 |

**校验提示区**（右侧或底部固定）

- 未连线、类型不匹配、缺必填 param、非法循环（若启用循环）

**一期策略**：默认「线性编排」模式；「高级模式」开关打开后显示分支/并行/循环节点。

---

### 3.8 模板中心 — 报告模板 `/templates/report/:id/edit`

**字段**

| 字段 | 类型 |
|------|------|
| name | 文本 |
| description | 多行 |
| coverFields | 键值列表（标题、版本、卫星、批次、日期等） |
| chapterOutline | 有序章节树（标题、占位符块） |
| placeholders | 与比对结果字段绑定映射表 |

**必选校验（按钮「校验模板」）**

- 存在封面区块  
- 至少一章正文  
- 页码域已插入（按 Word 模板约定字段名）

**操作**：保存、校验、上传 docx 模板文件、生成样例报告

---

### 3.9 算法仓库 `/algorithms`

**表格列**

| 列名 | 字段 key |
|------|----------|
| 名称 | name |
| 语言 | lang（python/javascript） |
| 版本 | version |
| 状态 | status |
| 上传人 | uploadedBy |
| 更新时间 | updatedAt |
| 操作 | detail / download |

**上传向导步骤**

1. 选择语言、填写名称与说明  
2. 上传包（zip/tgz 按约定）  
3. 依赖白名单确认（展示锁定列表）  
4. 提交审核（若需要）/ 直接生效（按角色）

---

### 3.10 比对报告 `/reports`

**筛选**：satelliteId、testBatchId、结论 decision、时间范围

**表格列**

| 列名 | 字段 key |
|------|----------|
| 报告标题 | title |
| 生成时间 | generatedAt |
| 关联 RunId | runId |
| 结论摘要 | decisionSummary |
| 操作 | view / download |

**详情页 `/reports/:id`**

- 摘要、异常明细表、下载 Word、关联血缘链接

---

### 3.11 审计与血缘 `/audit`

**审计列表列**

| 列名 | 字段 key |
|------|----------|
| 时间 | actionAt |
| 操作人 | operator |
| 动作 | action |
| 对象类型 | targetType |
| 对象 ID | targetId |

**血缘查询**：输入 runId，展示简化 DAG 或时间线（与任务详情 Tab4 一致）

---

## 4. 关键交互与文案

| 场景 | 交互 |
|------|------|
| 补跑/重跑/回补 | 二次确认弹窗 + 必填原因（审计） |
| 长任务 | 列表显示阶段或仅状态+耗时；可选轮询间隔提示 |
| 超时 | status=TIMEOUT + isTimeout=true，操作引导查看失败 Tab |
| 只读员 | 主操作按钮隐藏或禁用 |

---

## 5. Figma 命名规范（建议）

### 5.1 Page 命名

`00-Cover` | `01-Foundation` | `02-Flows` | `03-Screens` | `04-Components` | `05-Archive`

### 5.2 Frame 命名（页面）

格式：`SCR_{模块}_{页面}_{状态}`

示例：

- `SCR_Auth_Login_Default`
- `SCR_Dashboard_Home`
- `SCR_Jobs_List_Filtered`
- `SCR_Jobs_Detail_Running`
- `SCR_Quality_Data`（品质数据与标记）
- `SCR_Tpl_Filter_List`
- `SCR_Tpl_Filter_Edit_Draft`
- `SCR_Tpl_Algorithm_Editor_Linear`
- `SCR_Tpl_Report_Edit`
- `SCR_Report_List`
- `SCR_Audit_Lineage`

### 5.3 Component 命名

格式：`CMP_{类型}_{名称}_{变体}`

示例：

- `CMP_Nav_Side_Default`
- `CMP_Table_Jobs_Default`
- `CMP_Tag_Status_Success`
- `CMP_Modal_Confirm_Danger`

### 5.4 Auto Layout 建议

- 页面 Frame：方向 Vertical，padding 24  
- 表格区：固定最小高度，表头 Sticky（长列表）

---

## 6. 与设计文档的对应关系

| 原型模块 | 需求/设计文档要点 |
|----------|-------------------|
| 筛选模板 | AND/OR/NOT、持续时长、边界缓冲、灰度 |
| 任务 | Hangfire 触发、补跑重跑回补、8h 超时 |
| 品质数据与标记 | ClickHouse 明细、raw/processed、离群与 quality_flags |
| 高品质数据 | 不插值不去重、离群标记入库 |
| 报告 | Word、多模板、封面/章节/页码 |
| 审计 | 全链路可追溯 |

---

## 7. 修订记录

| 版本 | 日期 | 说明 |
|------|------|------|
| V1.0 | 2026-04-12 | 首版：中保真字段与 Figma 规范 |
| V1.1 | 2026-04-12 | 增加「品质数据与标记」页面说明；独立 HTML 原型见 `prototype/` |
| V1.2 | 2026-04-12 | 关联《PostgreSQL元数据与对外接口设计说明》：开放 API、Webhook、`run_id` 语义 |

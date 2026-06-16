---
name: 筛选模板RuleTp替换方案
overview: 基于 RuleTpView 的“指令+参数+表达式”模式重构当前筛选模板有效时间段提取能力：配置阶段指令来自 command_cache、参数来自 param_cache；执行阶段从海量接口拉取对应历史数据参与求值，指令历史严格按“查询指令表”API实现。比较符保留 `> >= < <= = != between`。
todos:
  - id: schema-contract
    content: 定义新 conditionConfig 配置契约并实现 ruleTree 兼容策略
    status: completed
  - id: backend-parser-evaluator
    content: 实现表达式解析/求值与参数条件比较符映射（含 = 与 between）
    status: completed
  - id: instruction-provider
    content: 配置期接 command_cache/param_cache，执行期按海量接口“查询指令表”API + 参数历史API实现条件时间段计算
    status: completed
  - id: pipeline-integration
    content: 将新条件引擎接入 PreprocessPipeline 替换现有 ruleTree 主流程
    status: completed
  - id: frontend-editor
    content: 重构 FilterTemplateEditor 为“指令+参数+表达式”混合式编辑器
    status: completed
  - id: validation-and-tests
    content: 补齐前后端校验与单测/回归用例（表达式优先级、兼容旧模板）
    status: completed
isProject: false
---

# 模板治理 / 筛选模板 重构计划（对齐 RuleTpView）

## 目标与边界

- 以 `RuleTpView` 的交互与表达式规则为蓝本，替换当前扁平 `ruleTree` 配置模式。参考实现：
  - [example/AIRTPPlatform/Modules/AnalysisModule/Views/RuleTpView.xaml](example/AIRTPPlatform/Modules/AnalysisModule/Views/RuleTpView.xaml)
  - [example/AIRTPPlatform/Modules/AnalysisModule/Views/RuleTpView.xaml.cs](example/AIRTPPlatform/Modules/AnalysisModule/Views/RuleTpView.xaml.cs)
  - [example/AIRTPPlatform/Modules/AnalysisModule/ViewModels/RuleTpViewModel.cs](example/AIRTPPlatform/Modules/AnalysisModule/ViewModels/RuleTpViewModel.cs)
- 新规则必须包含三部分：**指令条件**、**参数条件**、**表达式**（混合式 UI）。
- 参数比较符固定为：`>`、`>=`、`<`、`<=`、`=`、`!=`、`between`（替换 RuleTp 原有参数判断方式）。
- 表达式运算规则遵循 RuleTp：`&&`、`||`、`(`、`)`（并按表达式优先级求值）。
- 配置阶段数据源固定为：**指令从本项目指令缓存（`command_cache`）选择**，**参数从本项目参数缓存（`param_cache`）选择**。
- 运行规则时，条件求值所需历史数据统一从海量接口获取：指令历史严格走海量接口文件中“查询指令表”API，参数历史走海量参数历史查询接口（与接口文档对齐）。

---

## 现状差异（需要被替换）

- 当前前端是“参数行 + 顶层 AND/OR”的扁平规则编辑：
  - [frontend/src/pages/templates/FilterTemplateEditor.tsx](frontend/src/pages/templates/FilterTemplateEditor.tsx)
- 当前后端以 `ruleTree` 递归求值（AND/OR/NOT），无“指令条件 + 表达式解析器”：
  - [src/SatelliteData.Application/Pipeline/RuleTreeSegmentEvaluator.cs](src/SatelliteData.Application/Pipeline/RuleTreeSegmentEvaluator.cs)
- 当前模板校验器围绕 `ruleTree`：
  - [src/SatelliteData.Application/Templates/FilterTemplateValidator.cs](src/SatelliteData.Application/Templates/FilterTemplateValidator.cs)

---

## 设计思路（核心）

### 1) 配置模型从 `ruleTree` 切换为 `conditionConfig + expression`

在 `config_json` 中新增（并逐步替代 `ruleTree`）：

- `conditionConfig.instructions`
  - `startCommands[]`（指令 ID + 与/或关系）
  - `endCommands[]`（指令 ID + 与/或关系）
- `conditionConfig.parameters[]`
  - `conditionId`（如 `P1`）
  - `paramId`
  - `operator`（`> >= < <= = != between`）
  - `value`
- `conditionConfig.expression`
  - 例如：`(I1 && P1) || P2`

兼容策略：
- 已有模板 `ruleTree` 保留可读；保存 Draft 时统一迁移到新结构（可回写 `ruleTree` 兼容字段，短期双写，后续下线）。

配置来源约束：
- `conditionConfig.instructions` 的候选项来源为本系统资产缓存 `command_cache`。
- `conditionConfig.parameters` 的候选项来源为本系统资产缓存 `param_cache`。

### 2) 表达式求值改为“布尔条件 -> 时间段集合运算”

- 先分别计算每个条件的时间段：
  - 参数条件：运行期从海量接口拉参数历史时序，按比较符（含 `between`）转为候选时间段
  - 指令条件：运行期按海量接口文件“查询指令表”API读取历史指令时间，形成候选时间段
- 再将表达式 AST 映射为区间运算：
  - `&&` -> 交集
  - `||` -> 并集
  - 括号 -> 显式优先级
- 最后套用 `durationSeconds` 与边界裁剪。

```mermaid
flowchart TD
    config[filter_template.config_json] --> parse[解析conditionConfig与expression]
    parse --> paramEval[参数条件逐条求段]
    parse --> instEval[指令条件逐条求段]
    paramEval --> symbolMap[按conditionId构建段集合映射]
    instEval --> symbolMap
    symbolMap --> exprEval[表达式AST求值: &&交集 ||并集 括号优先]
    exprEval --> postFilter[durationSeconds过滤+窗口裁剪]
    postFilter --> validRanges[有效时间段]
```

### 3) 前端改为 RuleTp 风格混合编辑

在“1. 有效时间段提取规则”中替换为三块：
- 指令条件区（起始/结束指令 + 与/或 + 增删）
- 参数条件区（保留现有比较符集合）
- 表达式编辑区（插入按钮 `&& || ( )` + 语法校验 + 条件 token 快速插入）

---

## 实施步骤

### A. 后端模型与校验层

1. 扩展模板配置契约与校验器
   - 修改 [src/SatelliteData.Application/Templates/FilterTemplateValidator.cs](src/SatelliteData.Application/Templates/FilterTemplateValidator.cs)
   - 新增 `conditionConfig` 校验：
     - conditionId 唯一
     - `operator` 白名单（`> >= < <= = != between`）
     - `between` 仅允许 2 值
     - expression 仅允许合法 token/括号/逻辑符
2. 兼容旧 `ruleTree`
   - 在模板读取/保存链路中增加适配（优先新结构，回退旧结构）
   - 位置：
     - [src/SatelliteData.Application/Templates/FilterTemplateService.cs](src/SatelliteData.Application/Templates/FilterTemplateService.cs)
     - [src/SatelliteData.Application/Templates/FilterTemplateContracts.cs](src/SatelliteData.Application/Templates/FilterTemplateContracts.cs)

### B. 后端执行引擎重构

3. 新增表达式解析与区间求值组件
   - 新建 `ConditionExpressionParser`（词法+语法）
   - 新建 `ConditionExpressionEvaluator`（AST -> 区间交并）
4. 参数条件求值复用现有逻辑
   - 从 [src/SatelliteData.Application/Pipeline/RuleTreeSegmentEvaluator.cs](src/SatelliteData.Application/Pipeline/RuleTreeSegmentEvaluator.cs) 抽出叶子比较能力（含 `between`）
   - `=` 与历史 `==` 做兼容映射
5. 新增指令历史读取与求值
   - 在基础设施层扩展海量接口客户端（历史数据查询）：
     - [src/SatelliteData.Infrastructure/HttpClients/MassDataApiClient.cs](src/SatelliteData.Infrastructure/HttpClients/MassDataApiClient.cs)
   - 在应用层新增 `IConditionHistoryProvider`（统一封装指令历史 + 参数历史读取）
   - 配置时继续使用缓存仓储：`command_cache` / `param_cache`；执行时切换为海量历史接口，其中指令历史固定走“查询指令表”API
   - 与 `PreprocessPipeline` 对接：
     - [src/SatelliteData.Application/Pipeline/PreprocessPipeline.cs](src/SatelliteData.Application/Pipeline/PreprocessPipeline.cs)
   - DI 注入：
     - [src/SatelliteData.Infrastructure/Pipeline/PipelineServiceCollectionExtensions.cs](src/SatelliteData.Infrastructure/Pipeline/PipelineServiceCollectionExtensions.cs)

### C. 前端编辑器替换

6. 重构筛选模板编辑页
   - [frontend/src/pages/templates/FilterTemplateEditor.tsx](frontend/src/pages/templates/FilterTemplateEditor.tsx)
   - 用“指令区 + 参数区 + 表达式编辑器”替换现有扁平规则表
   - 比较符选项改为：`> >= < <= = != between`
7. 更新前端类型与 API 契约
   - [frontend/src/api/types.ts](frontend/src/api/types.ts)
   - [frontend/src/api/templates.ts](frontend/src/api/templates.ts)
   - 支持表达式校验调用（建议新增 validate 接口）

### D. 测试与回归

8. 后端单测与集成测试
   - 表达式优先级与括号
   - `between` / `=` / `!=` 边界
   - 指令+参数混合表达式
   - 旧 `ruleTree` 兼容读取
   - 位置：
     - [tests/SatelliteData.UnitTests](tests/SatelliteData.UnitTests)
9. 前端交互回归
   - 条件增删、token 插入、语法提示、保存/发布、旧模板回显

---

## 风险与应对

- **风险1：海量历史接口（指令/参数）返回格式不稳定**
  - 应对：在 `MassDataApiClient` 做多字段兼容映射、响应兜底与接口异常分级日志。
- **风险2：旧模板大量存在，直接切换会破坏编辑体验**
  - 应对：双读双写过渡（新结构优先，旧结构可回退），发布后逐步迁移。
- **风险3：表达式错误导致执行期失败**
  - 应对：前端“语法检查”+后端保存前强校验，阻断非法模板发布。

---

## 交付结果（完成后）

- 筛选模板“有效时间段提取规则”与 RuleTp 思路一致：支持**指令条件 + 参数条件 + 表达式**。
- 参数比较符完全按当前项目要求（`> >= < <= = != between`）。
- 预处理执行链路可按表达式规则稳定产出有效时间段，兼容历史模板。
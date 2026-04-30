# PostgreSQL 元数据与对外接口设计说明

## V1.0

本文档汇总 **PostgreSQL 中建议保存的元数据表级设计**，以及 **第三方系统通过 HTTPS/REST 获取预处理与算法结果** 的接口模式（含异步与 Webhook）。与《需求分析说明文档 V1.1》《总体设计文档 V1.1》配套使用。

---

## 第一部分：PostgreSQL 元数据范围

### 1.1 存储原则

| 存 PostgreSQL | 不存 PostgreSQL（放其他库） |
|---------------|-----------------------------|
| 模板定义与版本、灰度规则 | 原始遥测长表 → MongoDB |
| 任务状态、幂等键、Hangfire 映射 | 高品质逐点明细 → ClickHouse |
| 用户与角色、审计日志 | 大对象本体 → MinIO |
| 对象存储索引（bucket/key/checksum） | |
| 第三方 API 客户端、回调配置与投递日志 | |
| （可选）ML 数据集快照元数据 | |

### 1.2 表清单（逻辑实体）

以下为逻辑表名与核心字段意图，实施时可按团队规范调整命名与类型。

#### 用户与权限
- `users`：账号、密码哈希、启用状态  
- `roles`：admin / analyst / viewer  
- `user_roles`：多对多  
- `user_scope`（可选）：按卫星/项目限制

#### 模板与版本
- `filter_templates`、`filter_template_versions`（config_json）  
- `algorithm_templates`、`algorithm_template_versions`（config_json）  
- `report_templates`、`report_template_versions`  
- `template_gray_rules`：模板类型、版本 ID、卫星/批次/时间窗范围、优先级

#### 任务与流水线
- `job_runs`：`id`（即对外 `run_id`）、`job_type`、`trigger_type`、`status`、卫星/批次/时间窗、各模板版本 FK、`idempotency_key`（唯一）、`parent_run_id`、`hangfire_job_id`（可选）、错误信息、时间戳  
- `job_run_stages`（可选）：各阶段状态

#### 对象存储索引
- `stored_objects`：bucket、object_key、version_id、sha256、kind（report/algorithm_package/export…）、`ref_run_id`

#### 第三方与回调
- `api_clients`：client_id、api_key_hash、状态、IP 白名单（可选）  
- `client_callbacks`：默认 callbackUrl、签名密钥版本  
- `callback_deliveries`：event_id、目标 URL、HTTP 状态、重试次数、payload 摘要或 json

#### 审计
- `audit_logs`：操作人、动作、资源类型与 ID、前后 JSON、request_id、时间

#### ML（可选）
- `ml_dataset_snapshots`：名称、definition_json（run 列表、时间窗）、norm 文件指针、channel_map_json

**索引建议**：`job_runs(idempotency_key)` 唯一、`job_runs(status, created_at)`、`audit_logs(resource_type, resource_id, created_at)`、`stored_objects(kind, ref_run_id)`。

---

## 第二部分：对外 HTTPS / REST 接口模式

### 2.1 协议与风格

- **HTTPS + REST + JSON**，版本前缀如 `/v1/`  
- 公开契约以 **OpenAPI 3** 维护  

### 2.2 同步查询类（示例）

| 方法 | 路径（示例） | 说明 |
|------|----------------|------|
| GET | `/v1/runs/{runId}` | 任务时间窗、卫星、批次、状态、模板版本 |
| GET | `/v1/runs/{runId}/metrics` | 算法汇总：max/min/mean/std/variance 等 |
| GET | `/v1/runs/{runId}/metrics/envelope` | 包络线；点集大时返回 **预签名 URL** 或分页 |

查询类接口带鉴权头（Bearer 或 API Key 方案以安全设计为准）。

### 2.3 异步长任务 + 轮询

1. `POST /v1/jobs`（或 `/v1/exports`）创建任务，请求体可含 `callbackUrl`（可覆盖客户端默认）。  
2. 响应：**HTTP 202 Accepted**，Body 含 `jobId` / `runId`。  
3. 第三方 **`GET /v1/jobs/{jobId}`** 轮询直至 `SUCCEEDED` / `FAILED` / `TIMEOUT`。  
4. 终态响应中带 `result` 或下载链接摘要。

### 2.4 Webhook（处理完成后推送）

- 任务完成后，本系统作为 **HTTP 客户端** 向第三方 `callbackUrl` 发送 **`POST`**。  
- Body 建议包含：`eventType`（如 job.completed）、**`eventId`（幂等）**、`jobId`、`status`、`finishedAt`、结果或 **`metricsSummaryUrl` / `downloadUrl`**（指向本系统受控下载或 MinIO 预签名）。  
- **HMAC-SHA256** 签名（共享密钥，分版本）、时间戳防重放。  
- 非 2xx 时 **指数退避重试**，结果记入 `callback_deliveries`。  

### 2.5 安全与运维

- API Key 仅存 **哈希**；支持吊销与轮换  
- 限流、按 client 配额  
- 统一错误体：`code`、`message`、`requestId`  

---

## 第三部分：与 ClickHouse、run_id 的衔接

- 第三方通过 `runId` 关联本系统一次任务实例；算法汇总结果应能按 **`run_id`** 过滤。  
- 高品质明细数据在 ClickHouse 中**全局唯一且轻量化**，取消行级 `run_id` 与模板描述，仅由联合主键 `(satellite, batch, param, ts)` 及数据值、轻量标记组成。
- 第三方查询时无需指定 `run_id` 即可读取连续时间段的高品质点数据。
- 任务上下文、离群原因等追溯信息抽离到 PostgreSQL 中按 **“参数+时间窗”** 维度维护（如 `hq_param_metadata` 表）。前端需要展示原因时，只需用测点的时间去元数据表中反查当时生效的 `run_id` 和判定规则，即可拼接出完整的描述，避免重复存储数亿行相同的文本。  

---

## 第四部分：修订记录

| 版本 | 日期 | 说明 |
|------|------|------|
| V1.1 | 2026-04-21 | 接口说明更新：ClickHouse 明细表轻量化抽离行级溯源，改由 PG 元数据按时间窗承载 |

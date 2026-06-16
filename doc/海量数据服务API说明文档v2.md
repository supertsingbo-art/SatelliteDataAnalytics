# MassDataServer 海量数据服务接口 API 说明文档 v2

本文档面向第三方调用方，详细说明 MassDataServer 提供的 **v1** 与 **v2** 版本的 Web API 与 gRPC 接口。文档基于源码权威定义，修正了早期版本中路由与版本不一致的问题。

**文档版本**：v2（2026-06）  
**适用范围**：第三方调用方、集成测试、运维参考  
**与旧文档关系**：旧版 `海量数据服务API说明文档.md` 实质为 v2 草稿；本文档为 **双版本权威参考**，保留旧文档不变。

---

## 1. 基本信息

### 1.1 服务端点概览

| 项目 | v1 | v2 |
|------|----|----|
| Web API 前缀 | `/api/v1/mass-data` | `/api/v2/mass-data` |
| gRPC Package | `MassPlatform` | `mass_platform.v2`（C# 命名空间 `MassPlatform.V2`） |
| gRPC Service | `MassDataServer` + `DataReceiveServer` | `MassDataServer`（数采下载能力合并） |
| Proto 文件 | `MassServerProtos/v1/MassDataServer.proto`、`DataReceiveServer.proto`、`MassModels.proto` | `MassServerProtos/v2/mass_data_server_v2.proto`、`mass_models_v2.proto` |
| 监听端口 | HTTP/REST: **5000**；gRPC 优先: **5005**（HTTP/2） | 同左 |
| 配置管理 API | `/api/Config`（无版本号，Swagger 隐藏） | 同左 |

### 1.2 端口与协议

- **5000**：HTTP/1.1（REST Web API） + HTTP/2（gRPC 兼容）
- **5005**：HTTP/2 only（推荐 gRPC 客户端连接）
- Docker 映射：主机 5008 → 容器 8080

### 1.3 安全与运维约束

- **无 API 认证**：所有接口匿名访问（`anonymousAuthentication: true`）
- **CORS**：`MassDataCors` 策略；`Cors:AllowedOrigins` 为空时拒绝跨域
- **限流**：`RateLimitingMiddleware` —— 每 IP 每分钟 100 请求，超限返回 **429**（纯文本 `"请求过于频繁，请稍后重试"`）
- **性能日志**：慢请求（>1s）记录日志，不改变状态码

### 1.4 在线资源

- Swagger UI：`/swagger`（v1/v2 分组）
- OpenAPI JSON：`/swagger/v1/swagger.json`、`/swagger/v2/swagger.json`
- 健康检查：`/healthz`

---

## 2. 通用约定

### 2.1 卫星定位主键

多数接口需以下三元组唯一定位目标卫星：

| 参数 | 说明 |
|------|------|
| taskNo | 型号代号 |
| satNo | 卫星代号 |
| dbStage | 阶段代号 |

### 2.2 时间字段

- Web API：`DateTime`（JSON 字符串，建议 ISO-8601 UTC，例如 `2026-03-19T00:00:00Z`）
- gRPC：`google.protobuf.Timestamp`

### 2.3 Web API 错误码约定（v1 & v2 共享）

| 状态码 | 说明 | 响应体 |
|--------|------|--------|
| 200 | 成功 | 请求特定 DTO |
| 400 | 参数错误（`ArgumentException`） | `{ "error": "<message>" }` |
| 404 | 卫星不存在（`SatelliteNotFoundException`） | `{ "error": "<message>" }` |
| 408 | 请求取消或超时（`OperationCanceledException`） | `{ "error": "请求已取消或超时" }` |
| 429 | 限流 | 纯文本（非 JSON） |
| 500 | 服务内部错误 | `{ "error": "<operation message>", "detail": "<ex.Message>" }` |

### 2.4 接口通用 Response 结构

**列表型响应**（v1 默认）：

```json
{
  "datas": [ ... ]
}
```

**信封型响应**（v2 历史查询类接口）：

```json
{
  "satNo": "SAT_01",
  "taskNo": "TASK_A",
  "datas": [ ... ]
}
```

**流式 HTTP 响应**（`IAsyncEnumerable<T>` 返回）：

响应体为顶层 JSON 数组 `[{...}, {...}, ...]`，元素随服务端枚举逐步序列化写出。客户端需使用 `fetch` + `ReadableStream`（或 gRPC 流）做边收边处理。

---

## 3. v1 与 v2 能力对照表

### 3.1 Web API 差异摘要

| 能力 | v1 | v2 |
|------|:--:|:--:|
| 端点总数 | 22 | 35 |
| 卫星/基础库/判读查询（核心） | 有 | 有 |
| `query/parameters/para-aggregate` (+ stream) | 无 | 有 |
| `query/instructions/stream` | 无 | 有 |
| 聚合/统计/数据量统计 | 无 | 有 |
| 判据规则 CRUD (`judge/rules/*`) | 无 | 有 |
| `files/resolve` | 无 | 有 |
| 参数条件 `pkgParaIds` | `{ pid, ids[] }` | `{ pid, id[], rtDelayFlag, dataProvider }` |
| 包/指令 ID 表示 | `int[]`（服务端补默认 flag） | `Tuple` 数组 |

### 3.2 gRPC 差异摘要

| 能力 | v1 | v2 |
|------|:--:|:--:|
| `DataReceiveServer` 独立服务 | 有 | 无（能力合并到 `MassDataServer`） |
| `QueryParaConformity` | 有（无 REST 对应） | 替换为 `QueryParaAggregate` |
| `QuerygInstStream` | 无 | 有 |
| 统计/判据 CRUD | 无 | 有 |
| `DownLoadRitsConfigFile` | 有（v1 only） | 无 |
| 二进制字段（fd/pd/cd） | `string` | `bytes` |
| 参数 `pv` | `string` | `double` |

---

## 4. 版本策略与迁移提示

- **v1**：稳定子集，适合仅需基础查询的旧集成
- **v2**：功能完整版，推荐新开发使用
- **迁移路径**：路径前缀 `/api/v1/mass-data` → `/api/v2/mass-data`；gRPC package `MassPlatform` → `mass_platform.v2`
- 详细迁移指南见附录 C

---

# 5. Web API v1（`api/v1/mass-data`）

**控制器**：`MassDataApiV1Controller.cs`  
**请求模型**：`V1/MassDataApiV1Models.cs`  
**响应 DTO**：复用 `MassDataApiController.cs` 底部共享类型（`MassDataListResponse<T>` 等）

v1 提供 22 个端点，是 v2 的功能子集。历史查询请求体使用简化字段（无 `rtDelayFlag`/`dataProvider`/`chId`/`rtFlag`）。

---

## 5.1 卫星信息

### 5.1.1 获取可用卫星列表

**URL**  
`GET /api/v1/mass-data/satellites`

**HTTP 请求方式**  
GET

**参数**  
无

**返回结果**  
```json
{
  "datas": [
    {
      "taskNo": "TASK_A",
      "taskName": "型号A",
      "dbStage": "DEV",
      "satNo": "SAT_01",
      "satName": "卫星01",
      "enabled": true
    }
  ]
}
```

**返回字段说明**  
| 返回值字段 | 字段类型 | 字段说明 |
|-----------|---------|---------|
| taskNo | string | 型号代号 |
| taskName | string | 型号名称 |
| dbStage | string | 阶段代号 |
| satNo | string | 卫星代号 |
| satName | string | 卫星名称 |
| enabled | bool | 是否启用 |

**对应 gRPC 方法**  
`GetAllSatsFromMassServer(google.protobuf.Empty) returns (gSatCollectionReply)`

---

### 5.1.2 获取卫星连接配置

**URL**  
`POST /api/v1/mass-data/satellite/config`

**HTTP 请求方式**  
POST

**请求体**（`SatelliteLookupRequest`）
```json
{
  "taskNo": "TASK_A",
  "satNo": "SAT_01",
  "dbStage": "DEV"
}
```

**返回结果**  
`SatelliteConnectionDto`（含 basicConn、mongoQueryConn、judgeConn 等连接字符串及 mqtt/signalR 地址）

**对应 gRPC 方法**  
`GetSatCfg(gSatliteThumb) returns (satconncfg)`

---

### 5.1.3 获取消息队列配置

**URL**  
`POST /api/v1/mass-data/satellite/message-queue`

**请求体** 同上

**返回结果**  
`MessageQueueInfoDto`（url、exchangeType、exchangeName、queues[]）

**对应 gRPC 方法**  
`GetMessageQueueInfo(gSatliteThumb) returns (gMsgQueueInfoReply)`

---

### 5.1.4 获取 Redis 配置

**URL**  
`POST /api/v1/mass-data/satellite/redis`

**请求体** 同上

**返回结果**  
`RedisInfoDto`

**对应 gRPC 方法**  
`GetRedisInfo(gSatliteThumb) returns (gRedisInfoReply)`

---

## 5.2 基础库数据查询

### 5.2.1 查询参数表

**URL**  
`GET /api/v1/mass-data/basic/parameters?taskNo=...&satNo=...&dbStage=...`  
或  
`POST /api/v1/mass-data/basic/parameters`

**请求体 / Query**  
`SatelliteLookupRequest`

**返回结果**  
`MassDataListResponse<BasicParameterDto>`

**对应 gRPC 方法**  
`GetBasicDbPara(gSatliteThumb) returns (gView_Paras)`

---

### 5.2.2 查询指令表

**URL**  
`POST /api/v1/mass-data/basic/commands`

**请求体**  
`SatelliteLookupRequest`

**返回结果**  
`MassDataListResponse<BasicCommandDto>`

**对应 gRPC 方法**  
`GetBasicDbCmd(gSatliteThumb) returns (gView_Cmds)`

---

### 5.2.3 查询包表

**URL**  
`POST /api/v1/mass-data/basic/packages`

**请求体** 同上

**返回结果**  
`MassDataListResponse<BasicPackageDto>`

**对应 gRPC 方法**  
`GetBasicDbPkg(gSatliteThumb) returns (gView_Pkgs)`

---

### 5.2.4 查询系统层级关系

**URL**  
`POST /api/v1/mass-data/basic/relations`

**请求体** 同上

**返回结果**  
`BasicRelationResponse`（pkgHirberDatas、cmdHirberDatas）

**对应 gRPC 方法**  
`GetBasicDbRelation(gSatliteThumb) returns (gView_Sys_Relates)`

---

### 5.2.5 查询指令判据表

**URL**  
`POST /api/v1/mass-data/basic/cmd-judges`

**请求体** 同上

**返回结果**  
`MassDataListResponse<BasicCommandJudgeDto>`

**对应 gRPC 方法**  
`GetBasicDbCmdJudges(gSatliteThumb) returns (gView_Cmdjudges)`

---

## 5.3 历史数据查询

### 5.3.1 查询参数数据

**URL**  
`POST /api/v1/mass-data/query/parameters`

**请求体**（`V1ParameterQueryRequest`）
```json
{
  "taskNo": "TASK_A",
  "satNo": "SAT_01",
  "dbStage": "DEV",
  "fromDt": "2026-03-19T00:00:00Z",
  "toDt": "2026-03-19T01:00:00Z",
  "pkgParaIds": [
    { "pid": 1001, "ids": [2001, 2002] }
  ],
  "limitTotal": -1
}
```

**返回结果**  
`MassDataListResponse<ParameterDto>`

**流式版本**  
`POST /api/v1/mass-data/query/parameters/stream` —— 返回顶层 JSON 数组流

**对应 gRPC 方法**  
`QuerygPara(gQueryCondParaReq) returns (stream gParaCollect)`

---

### 5.3.2 查询指令数据

**URL**  
`POST /api/v1/mass-data/query/instructions`

**请求体**（`V1InstructionQueryRequest`）
```json
{
  "taskNo": "TASK_A",
  "satNo": "SAT_01",
  "dbStage": "DEV",
  "fromDt": "...",
  "toDt": "...",
  "instIds": [1001, 1002]
}
```
（服务端将 instIds 映射为 `(id, 32)`）

**返回结果**  
`MassDataListResponse<InstructionDto>`

**对应 gRPC 方法**  
`QuerygInst(gQueryCondInstReq) returns (gInstCollect)`

---

### 5.3.3 查询包数据

**URL**  
`POST /api/v1/mass-data/query/packages`

**请求体**（`V1PackageQueryRequest`）
```json
{
  "taskNo": "...",
  "satNo": "...",
  "dbStage": "...",
  "fromDt": "...",
  "toDt": "...",
  "pkgIds": [3001, 3002]
}
```
（服务端将 pkgIds 映射为 `(id, 0, 0)`）

**返回结果**  
`MassDataListResponse<PackageDto>`

**流式版本**  
`POST /api/v1/mass-data/query/packages/stream`

**对应 gRPC 方法**  
`QuerygPkg(gQueryCondPkgReq) returns (stream gPkgCollect)`

---

### 5.3.4 查询帧数据

**URL**  
`POST /api/v1/mass-data/query/frames`

**请求体**（`V1FrameQueryRequest`）
```json
{
  "taskNo": "...",
  "satNo": "...",
  "dbStage": "...",
  "fromDt": "...",
  "toDt": "..."
}
```
（服务端固定 chId=0, rtFlag=0）

**返回结果**  
`MassDataListResponse<FrameDto>`

**流式版本**  
`POST /api/v1/mass-data/query/frames/stream`

**对应 gRPC 方法**  
`QuerygFrame(gQueryCondFrameReq) returns (stream gFrameCollect)`

---

## 5.4 判读

### 5.4.1 获取判读信息

**URL**  
`POST /api/v1/mass-data/judge/infos`

**请求体**  
`SatelliteLookupRequest`

**返回结果**  
`MassDataListResponse<JudgeInfoDto>`

**对应 gRPC 方法**  
`GetJudgeInfos(gSatliteThumb) returns (gJudgeInfoCollect)`

---

### 5.4.2 查询判读结果

**URL**  
`POST /api/v1/mass-data/judge/results`

**请求体**（`V1JudgeResultQueryRequest`）
```json
{
  "taskNo": "...",
  "satNo": "...",
  "dbStage": "...",
  "fromDt": "...",
  "toDt": "...",
  "judgeCodes": ["CODE_01"]
}
```

**返回结果**  
`MassDataListResponse<JudgeResultDto>`

**对应 gRPC 方法**  
`QuerygJudgeResults(gQueryCondJudgeReusltReq) returns (stream gJudgeResultCollect)`

---

## 5.5 v1 独有说明

- 无 `para-aggregate`、聚合统计、数据量统计、判据规则 CRUD、数采文件下载
- `QueryParaConformity` 仅存在于 v1 gRPC，无对应 REST 端点
- `DownLoadRitsConfigFile` 仅存在于 v1 `DataReceiveServer` gRPC

---

---

# 6. Web API v2（`api/v2/mass-data`）

**控制器**：`MassDataApiController.cs`  
**DTO 定义**：同文件底部 `#region Request / Response DTOs`

v2 在 v1 基础上新增 13 个端点（para-aggregate、统计、判据规则 CRUD、数采文件等），并使用更丰富的请求字段与信封型响应。

**注意**：以下仅列出 v2 独有或增强的端点。完整 v2 能力与旧文档 §3–§7 一致，路径前缀已统一修正为 `/api/v2/mass-data`。

---

## 6.1 v2 独有 / 增强端点

### 6.1.1 复合参数查询（与 gRPC QueryParaAggregate 对齐）

**URL**  
`POST /api/v2/mass-data/query/parameters/para-aggregate`  
`POST /api/v2/mass-data/query/parameters/para-aggregate/stream`

**请求体**（`ParameterParaAggregateQueryRequest`）  
包含 `containInst`、`splitPage`、`skipNum`、`limitNum` 等分页与指令合并控制。

**返回结果**  
`MassDataListResponse<CompositeParameterPointDto>`（或流式顶层数组）

**对应 gRPC 方法**  
`QueryParaAggregate(GQueryCondParaAggregateReq) returns (stream GparaCompositeResult)`

---

### 6.1.2 指令流式查询

**URL**  
`POST /api/v2/mass-data/query/instructions/stream`

**说明**：当前返回整包 `MassDataEnvelopeResponse<InstructionDto>`（非 IAsyncEnumerable 顶层数组）。如需边序列化边传输，请使用 gRPC `QuerygInstStream`。

**对应 gRPC 方法**  
`QuerygInstStream(GQueryCondInstReq) returns (stream GInstCollect)`

---

### 6.1.3 参数聚合统计

**URL**  
`POST /api/v2/mass-data/query/parameters/aggregate`

**请求体**（`ParameterAggregationQueryRequest`）  
`parameterIds[]`、`intervalSeconds`、`aggregationType`（Average/Min/Max 等）

**返回结果**  
`MassDataEnvelopeResponse<ParameterAggregationDto>`

**对应 gRPC 方法**  
`AggregateParameters(GQueryCondParaAggReq) returns (GParaAggregationCollect)`

---

### 6.1.4 单参数统计信息

**URL**  
`POST /api/v2/mass-data/query/parameters/statistics`

**请求体**（`ParameterStatisticsQueryRequest`）

**返回结果**  
`ParameterStatisticsDto`（totalCount、first/lastTimestamp、min/max/avg/stdDev）

**对应 gRPC 方法**  
`GetParameterStatistics(GQueryCondParaStatisticsReq) returns (GParameterStatisticsReply)`

---

### 6.1.5 数据量统计

**URL**  
`POST /api/v2/mass-data/query/data-volume`

**请求体**（`DataVolumeStatisticsQueryRequest`）

**返回结果**  
`DataVolumeStatisticsDto`（各类型计数、totalCount、totalSizeBytes、oldest/newestTimestamp）

**对应 gRPC 方法**  
`GetDataVolumeStatistics(GQueryCondDataVolumeReq) returns (GDataVolumeStatisticsReply)`

---

### 6.1.6 判据规则管理（CRUD）

| 方法 | URL | 说明 |
|------|-----|------|
| POST | `/api/v2/mass-data/judge/rules` | 获取规则列表 |
| POST | `/api/v2/mass-data/judge/rules/by-code` | 按 code 查询 |
| POST | `/api/v2/mass-data/judge/rules/upsert` | 新增/更新（返回 id） |
| POST | `/api/v2/mass-data/judge/rules/delete` | 删除（200 空 body） |

**对应 gRPC 方法**  
`GetJudgeRules`、`GetJudgeRuleByCode`、`UpsertJudgeRule`、`DeleteJudgeRule`

---

### 6.1.7 数采文件下载

**URL**  
`POST /api/v2/mass-data/files/resolve`

**请求体**（`DownloadResolveFilesRequest`）  
`absoluteDir` + 卫星三元组

**返回结果**  
`MassDataListResponse<DataFileDto>`

**对应 gRPC 方法**  
`DownLoadTMResolveFiles(GDownCfgRevoleFileReq) returns (stream GDatafile)`

---

## 6.2 v2 请求体增强示例

```json
// ParameterQueryRequest（v2）
{
  "pkgParaIds": [{
    "pid": 1001,
    "id": [2001, 2002],
    "rtDelayFlag": 0,
    "dataProvider": 0
  }]
}

// FrameQueryRequest（v2）
{
  "chId": 0,
  "rtFlag": 0
}

// InstructionQueryRequest（v2）
{
  "instIds": [[1001, 32]]
}

// PackageQueryRequest（v2）
{
  "pkgIds": [[3001, 0, 0]]
}
```

完整字段与旧文档 §3–§7 保持一致，路径已更新。

---

---

# 7. gRPC v1 接口

## 7.1 `MassPlatform.MassDataServer`（16 RPC）

**Proto 文件**：`MassServerProtos/v1/MassDataServer.proto`  
**实现**：`MassDataServiceV1Impl.cs`  
**Package**：`MassPlatform`

### 7.1.1 卫星与配置（4 RPC）

| RPC 方法 | 请求类型 | 返回类型 | 流式 |
|----------|---------|---------|------|
| GetAllSatsFromMassServer | google.protobuf.Empty | gSatCollectionReply | Unary |
| GetSatCfg | gSatliteThumb | satconncfg | Unary |
| GetMessageQueueInfo | gSatliteThumb | gMsgQueueInfoReply | Unary |
| GetRedisInfo | gSatliteThumb | gRedisInfoReply | Unary |

### 7.1.2 基础库（5 RPC）

| RPC 方法 | 请求类型 | 返回类型 | 流式 |
|----------|---------|---------|------|
| GetBasicDbPara | gSatliteThumb | gView_Paras | Unary |
| GetBasicDbCmd | gSatliteThumb | gView_Cmds | Unary |
| GetBasicDbPkg | gSatliteThumb | gView_Pkgs | Unary |
| GetBasicDbRelation | gSatliteThumb | gView_Sys_Relates | Unary |
| GetBasicDbCmdJudges | gSatliteThumb | gView_Cmdjudges | Unary |

### 7.1.3 历史查询（6 RPC）

| RPC 方法 | 请求类型 | 返回类型 | 流式 |
|----------|---------|---------|------|
| QuerygPara | gQueryCondParaReq | stream gParaCollect | Server streaming |
| QueryParaConformity | gQueryCondParaReq | stream gparaConformityResult | Server streaming（**v1 gRPC 独有**） |
| QuerygInst | gQueryCondInstReq | gInstCollect | Unary |
| QuerygPkg | gQueryCondPkgReq | stream gPkgCollect | Server streaming |
| QuerygFrame | gQueryCondFrameReq | stream gFrameCollect | Server streaming |

### 7.1.4 判读（2 RPC）

| RPC 方法 | 请求类型 | 返回类型 | 流式 |
|----------|---------|---------|------|
| GetJudgeInfos | gSatliteThumb | gJudgeInfoCollect | Unary |
| QuerygJudgeResults | gQueryCondJudgeReusltReq | stream gJudgeResultCollect | Server streaming |

**关键消息类型**（`MassModels.proto`）：`gSatliteThumb`、`gQueryCondParaReq`、`gPkgParaRelate`、`gQueryCondInstReq` 等。

---

## 7.2 `MassPlatform.DataReceiveServer`（4 RPC，v1 only）

**Proto 文件**：`MassServerProtos/v1/DataReceiveServer.proto`  
**实现**：`DataReceiveServiceV1Impl.cs`

| RPC 方法 | 请求类型 | 返回类型 | 流式 |
|----------|---------|---------|------|
| GetAllSatsFromTaskDb | gTaskDbReq | gSatCollectionReply | Unary |
| GetSatCfg | gSatliteThumb | satconncfg | Unary |
| DownLoadTMResolveFiles | gSatliteThumb | stream gDatafile | Server streaming |
| DownLoadRitsConfigFile | gRitsReq | stream gDatafile | Server streaming |

**本地消息**：

- `gRitsReq`：taskno、dbstage、satnos[]、absolutedir
- `gDatafile`：filename、filetype、datas（bytes）

---

## 7.3 v1 gRPC C# 调用示例

```csharp
using Grpc.Net.Client;
using MassPlatform;
using Google.Protobuf.WellKnownTypes;

var channel = GrpcChannel.ForAddress("http://localhost:5005");
var client = new MassDataServer.MassDataServerClient(channel);
var dataReceiveClient = new DataReceiveServer.DataReceiveServerClient(channel);

// 1. 获取卫星列表
var satList = await client.GetAllSatsFromMassServerAsync(new Empty());

// 2. 查询参数（流式）
var paraReq = new gQueryCondParaReq { Fromdt = ..., Todt = ... };
paraReq.Parainfos.Add(new gPkgParaRelate { Taskno = "TASK_A", Satno = "SAT_01", ... });
using var paraCall = client.QuerygPara(paraReq);
await foreach (var batch in paraCall.ResponseStream.ReadAllAsync())
{
    // 处理 batch.Datas
}

// 3. 数采文件下载（DataReceiveServer）
var fileReq = new gSatliteThumb { Taskno = "...", Satno = "...", Dbstage = "..." };
using var fileCall = dataReceiveClient.DownLoadTMResolveFiles(fileReq);
await foreach (var f in fileCall.ResponseStream.ReadAllAsync())
{
    // 保存 f.Datas
}
```

---

---

# 8. gRPC v2 接口

## 8.1 `mass_platform.v2.MassDataServer`（25 RPC）

**Proto 文件**：`MassServerProtos/v2/mass_data_server_v2.proto`  
**实现**：`MassDataServiceImpl.cs`  
**Package**：`mass_platform.v2`（C# `MassPlatform.V2`）

### 8.1.1 卫星与配置（4 RPC）

| RPC 方法 | 请求类型 | 返回类型 | 流式 |
|----------|---------|---------|------|
| GetAllSatsFromMassServer | google.protobuf.Empty | GSatCollectionReply | Unary |
| GetSatCfg | GSatelliteThumb | Satconncfg | Unary |
| GetMessageQueueInfo | GSatelliteThumb | GMsgQueueInfoReply | Unary |
| GetRedisInfo | GSatelliteThumb | GRedisInfoReply | Unary |

### 8.1.2 基础库（5 RPC）

同 v1，但类型名为 PascalCase（GViewParas 等）。

### 8.1.3 查询与统计（9 RPC）

| RPC 方法 | 请求类型 | 返回类型 | 流式 |
|----------|---------|---------|------|
| QuerygPara | GQueryCondParaReq | stream GParaCollect | Server streaming |
| QueryParaAggregate | GQueryCondParaAggregateReq | stream GparaCompositeResult | Server streaming |
| QuerygInst | GQueryCondInstReq | GInstCollect | Unary |
| QuerygInstStream | GQueryCondInstReq | stream GInstCollect | Server streaming |
| QuerygPkg | GQueryCondPkgReq | stream GPkgCollect | Server streaming |
| QuerygFrame | GQueryCondFrameReq | stream GFrameCollect | Server streaming |
| AggregateParameters | GQueryCondParaAggReq | GParaAggregationCollect | Unary |
| GetParameterStatistics | GQueryCondParaStatisticsReq | GParameterStatisticsReply | Unary |
| GetDataVolumeStatistics | GQueryCondDataVolumeReq | GDataVolumeStatisticsReply | Unary |

### 8.1.4 判读与判据规则（6 RPC）

| RPC 方法 | 请求类型 | 返回类型 | 流式 |
|----------|---------|---------|------|
| GetJudgeInfos | GSatelliteThumb | GJudgeInfoCollect | Unary |
| QuerygJudgeResults | GQueryCondJudgeReusltReq | stream GJudgeResultCollect | Server streaming |
| GetJudgeRules | GSatRuleDbTarget | GJsonEntityCollection | Unary |
| GetJudgeRuleByCode | GRuleCodeReq | GJsonEntity | Unary |
| UpsertJudgeRule | GJudgeRuleUpsertReq | GEntityIdReq | Unary |
| DeleteJudgeRule | GJudgeRuleDeleteReq | google.protobuf.Empty | Unary |

### 8.1.5 数采文件（1 RPC）

| RPC 方法 | 请求类型 | 返回类型 | 流式 |
|----------|---------|---------|------|
| DownLoadTMResolveFiles | GDownCfgRevoleFileReq | stream GDatafile | Server streaming |

**Server-streaming 批次策略**：实现中参数约 2000 条/批，帧约 5000 条/批。

---

## 8.2 v2 gRPC C# 调用示例

```csharp
using Grpc.Net.Client;
using MassPlatform.V2;
using Google.Protobuf.WellKnownTypes;

var channel = GrpcChannel.ForAddress("http://localhost:5005");
var client = new MassDataServer.MassDataServerClient(channel);

// 1. 复合参数流式查询
var aggParaReq = new GQueryCondParaAggregateReq { Fromdt = ..., Todt = ..., Containinst = true };
aggParaReq.Parainfos.Add(new GPkgParaRelate { Taskno = "...", ... });
using var aggCall = client.QueryParaAggregate(aggParaReq);
await foreach (var batch in aggCall.ResponseStream.ReadAllAsync()) { ... }

// 2. 判据规则 upsert
var upsertReq = new GJudgeRuleUpsertReq
{
    Target = new GSatRuleDbTarget { Taskno = "...", Satno = "...", Dbstage = "..." },
    Entity = new GJsonEntity { Data = "{\"code\":\"NEW_RULE\"}" }
};
var upsertReply = await client.UpsertJudgeRuleAsync(upsertReq);
```

完整示例见旧文档附录 B（namespace 改为 `MassPlatform.V2`）。

---

---

# 9. 平台配置管理 API（`/api/Config`）

独立于卫星数据服务版本，由 `ConfigController` 提供。Swagger 隐藏（`[ApiExplorerSettings(IgnoreApi = true)]`）。

| 方法 | URL | 说明 |
|------|-----|------|
| GET | `/api/Config` | 获取当前配置文件内容 |
| POST | `/api/Config` | 保存/更新配置（验证后写入） |
| POST | `/api/Config/validate` | 仅验证格式 |
| POST | `/api/Config/import-legacy` | 导入旧版 allServerPlats.json（合并覆盖） |
| GET | `/api/Config/info` | 获取配置路径、存在性、修改时间 |
| POST | `/api/Config/backup` | 备份当前配置 |

响应格式示例（成功）：
```json
{ "success": true, "message": "...", "path": "...", "warnings": [] }
```

错误时返回 400/500 + `{ "valid": false, "errors": [...] }`。

---

# 附录 A：REST ↔ gRPC 方法映射表（精简）

| 功能 | v1 REST | v2 REST | v1 gRPC | v2 gRPC |
|------|---------|---------|---------|---------|
| 获取卫星列表 | `GET .../satellites` | 同左 | `GetAllSatsFromMassServer` | 同左 |
| 参数查询 | `POST .../query/parameters` | 同左 | `QuerygPara` | `QuerygPara` |
| 复合参数 | — | `.../para-aggregate` | `QueryParaConformity` | `QueryParaAggregate` |
| 指令流式 | — | `.../instructions/stream` | — | `QuerygInstStream` |
| 数采文件 | — | `.../files/resolve` | `DataReceiveServer.DownLoadTMResolveFiles` | `DownLoadTMResolveFiles` |
| 判据规则 CRUD | — | `.../judge/rules/*` | — | `GetJudgeRules` 等 4 个 |

完整 45 个 RPC 见第 7–8 章。

---

# 附录 B：建议调用顺序

**v1 流程**：
1. `GET /api/v1/mass-data/satellites`
2. `POST /api/v1/mass-data/satellite/config`
3. 基础库查询 → 历史查询（parameters/instructions/packages/frames）
4. 判读 infos/results

**v2 流程**（推荐）：
1–3 同 v1（路径 v2）
4. 视需调用 para-aggregate、aggregate、statistics、data-volume
5. 判据规则 CRUD
6. files/resolve

---

# 附录 C：从 v1 迁移到 v2 指南

1. **路径前缀**：`/api/v1/mass-data` → `/api/v2/mass-data`
2. **请求体字段**：
   - `pkgParaIds[].ids` → `id[]` + 新增 `rtDelayFlag`、`dataProvider`
   - `instIds`/`pkgIds` 改为 Tuple 数组
   - Frame 新增 `chId`、`rtFlag`
3. **gRPC**：package `MassPlatform` → `mass_platform.v2`；类型名前缀 `g` → `G`
4. **DataReceiveServer** → v2 `MassDataServer.DownLoadTMResolveFiles`
5. **新增能力**：直接使用 v2 统计、规则管理、复合参数接口

---

## 说明

- Web API 与 gRPC 能力基本对齐，JSON camelCase，proto snake_case。
- 流式接口优先使用 gRPC server-streaming 或 HTTP 顶层 JSON 数组。
- Swagger `/swagger` 可在线调试 v1/v2 分组。
- 配置管理独立于版本。

**文档结束**
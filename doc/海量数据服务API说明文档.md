# MassDataServer 海量数据服务接口 API 文档

本文档面向第三方调用方，详细说明 MassDataServer 提供的 Web API 与 gRPC 接口。

## 1. 基本信息

| 项目 | 说明 |
|------|------|
| Web API 基础路由 | `/api/mass-data`（卫星数据服务）、`/api/config`（平台配置管理） |
| gRPC Service | `MassPlatform.MassDataServer` |
| Proto 文件 | `MassServerProtos/MassDataServer.proto`、`MassServerProtos/MassModels.proto` |
| 支持格式 | JSON |
| 字符编码 | UTF-8 |

## 2. 通用约定

### 2.1 卫星定位主键

多数接口都需要以下三元组唯一定位目标卫星：

| 参数 | 说明 |
|------|------|
| taskNo | 型号代号 |
| satNo | 卫星代号 |
| dbStage | 阶段代号 |

### 2.2 时间字段

- Web API：`DateTime`（JSON 字符串，建议 ISO-8601 UTC，例如 `2026-03-19T00:00:00Z`）
- gRPC：`google.protobuf.Timestamp`

### 2.3 Web API 错误码约定

| 状态码 | 说明 |
|--------|------|
| 200 | 成功 |
| 400 | 参数错误 |
| 404 | 卫星不存在 |
| 408 | 请求取消或超时 |
| 500 | 服务内部错误 |

错误体示例：

```json
{
  "error": "错误摘要",
  "detail": "详细错误信息"
}
```

### 2.4 接口通用 Response 结构

Web API 大部分 Response Body 均为如下 JSON 结构之一：

**列表型响应**：

| 返回值字段 | 字段类型 | 字段说明 |
|-----------|---------|---------|
| datas | array | 数据列表 |
| satNo | string | 卫星代号（信封型） |
| taskNo | string | 型号代号（信封型） |

```json
{
  "datas": [ ... ]
}
```

或（信封型）：

```json
{
  "satNo": "SAT_01",
  "taskNo": "TASK_A",
  "datas": [ ... ]
}
```

部分历史查询接口另提供 **HTTP 顶层 JSON 数组流式** 路由（控制器返回 `IAsyncEnumerable<T>`）：响应体为 **`[ {...}, {...}, ... ]`**，元素随服务端枚举逐步序列化写出，利于降低服务端序列化峰值内存；客户端需使用 `fetch` + `ReadableStream`（或 gRPC 流）做边收边处理，勿仅用 `response.json()` 等待整包（浏览器仍会缓冲，但可配合增量解析）。示例见仓库 `samples/MassDataServer.ApiTestWeb` 中「流式消费」区块。

---

# 3. 卫星配置管理

## 3.1 获取可用卫星列表

获取海量数据平台中所有可用卫星的概要信息。

**URL**

`GET /api/mass-data/satellites`

**支持格式**

JSON

**HTTP请求方式**

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

**对应 gRPC方法**

`GetAllSatsFromMassServer(google.protobuf.Empty) returns (gSatCollectionReply)`

---

## 3.2 获取卫星连接配置

获取指定卫星的所有数据库连接配置信息。

**URL**

`POST /api/mass-data/satellite/config`

**支持格式**

JSON

**HTTP请求方式**

POST

**参数**

| 参数名 | 必选 | 类型 | 说明 |
|--------|------|------|------|
| taskNo | true | string | 型号代号 |
| satNo | true | string | 卫星代号 |
| dbStage | true | string | 阶段代号 |

**返回结果**

```json
{
  "basicConn": "Data Source=...;Initial Catalog=...;...",
  "mongoQueryConn": "mongodb://...",
  "judgeConn": "mongodb://...",
  "judgeMgrConn": "mongodb://...",
  "analysisConn": "mongodb://...",
  "displayConn": "mongodb://...",
  "mqttUrl": "amqp://...",
  "signalRUrl": "http://...",
  "cfgConn": "mongodb://..."
}
```

**返回字段说明**

| 返回值字段 | 字段类型 | 字段说明 |
|-----------|---------|---------|
| basicConn | string | SQL Server 基础库连接字符串 |
| mongoQueryConn | string | MongoDB 历史查询数据库连接字符串 |
| judgeConn | string | MongoDB 判读结果数据库连接字符串 |
| judgeMgrConn | string | MongoDB 判据管理数据库连接字符串 |
| analysisConn | string | MongoDB 分析数据库连接字符串 |
| displayConn | string | MongoDB 显示数据库连接字符串 |
| mqttUrl | string | RabbitMQ 消息队列连接地址 |
| signalRUrl | string | SignalR 实时推送地址 |
| cfgConn | string | MongoDB 配置数据库连接字符串 |

**对应 gRPC方法**

`GetSatCfg(gSatelliteThumb) returns (satconncfg)`

---

## 3.3 获取消息队列配置

获取指定卫星的 RabbitMQ 消息队列配置（Exchange 名称、路由 Key 等）。

**URL**

`POST /api/mass-data/satellite/message-queue`

**支持格式**

JSON

**HTTP请求方式**

POST

**参数**

| 参数名 | 必选 | 类型 | 说明 |
|--------|------|------|------|
| taskNo | true | string | 型号代号 |
| satNo | true | string | 卫星代号 |
| dbStage | true | string | 阶段代号 |

**返回结果**

```json
{
  "url": "amqp://...",
  "exchangeType": "direct",
  "exchangeName": "TASK_A_SAT_01_EX",
  "queues": [
    {
      "key": "PARA_DATA",
      "queueName": "TASK_A_SAT_01_PARA_Q",
      "exchangeName": "TASK_A_SAT_01_EX"
    }
  ]
}
```

**返回字段说明**

| 返回值字段 | 字段类型 | 字段说明 |
|-----------|---------|---------|
| url | string | RabbitMQ 连接地址 |
| exchangeType | string | Exchange 类型（本系统使用 direct） |
| exchangeName | string | Exchange 名称 |
| queues | array | 队列列表 |

**queues 字段说明**

| 返回值字段 | 字段类型 | 字段说明 |
|-----------|---------|---------|
| key | string | 路由 Key |
| queueName | string | 队列名称 |
| exchangeName | string | 所属 Exchange 名称 |

**对应 gRPC方法**

`GetMessageQueueInfo(gSatelliteThumb) returns (gMsgQueueInfoReply)`

---

## 3.4 获取 Redis 配置

获取指定卫星的 Redis 缓存配置信息。

**URL**

`POST /api/mass-data/satellite/redis`

**支持格式**

JSON

**HTTP请求方式**

POST

**参数**

| 参数名 | 必选 | 类型 | 说明 |
|--------|------|------|------|
| taskNo | true | string | 型号代号 |
| satNo | true | string | 卫星代号 |
| dbStage | true | string | 阶段代号 |

**返回结果**

```json
{
  "url": "192.168.1.100:6379",
  "pwd": "***",
  "dbIndex": 0,
  "keys": ["KEY_SAT_01", "KEY_SAT_01_STATUS"]
}
```

**返回字段说明**

| 返回值字段 | 字段类型 | 字段说明 |
|-----------|---------|---------|
| url | string | Redis 连接地址 |
| pwd | string | Redis 密码 |
| dbIndex | int | Redis 数据库索引 |
| keys | array[string] | Redis Key 列表 |

**对应 gRPC方法**

`GetRedisInfo(gSatelliteThumb) returns (gRedisInfoReply)`

---

# 4. 基础库数据查询

基础库数据存储于 SQL Server，为基础静态元数据（参数定义、指令定义等），响应结果会被缓存 3 分钟。

## 4.1 查询参数表

获取指定卫星的基础库参数定义表。

**URL**

`POST /api/mass-data/basic/parameters`

**支持格式**

JSON

**HTTP请求方式**

POST

**参数**

| 参数名 | 必选 | 类型 | 说明 |
|--------|------|------|------|
| taskNo | true | string | 型号代号 |
| satNo | true | string | 卫星代号 |
| dbStage | true | string | 阶段代号 |

**返回结果**

```json
{
  "datas": [
    {
      "paraId": 1001,
      "prmSysId": 1,
      "paraCode": "P_CODE_001",
      "paraType": 1,
      "paraTypeChar": "A",
      "paraTypeDesc": "模拟量",
      "paraDesc": "参数描述",
      "minValue": -100.0,
      "maxValue": 100.0,
      "updateTime": 10,
      "valueDesc": "电压",
      "validFlag": 1,
      "watchFlag": 1,
      "parameterType": 1,
      "editGroup": "G1",
      "procId": 1,
      "procDesc": "处理方法描述",
      "paraMask": "0xFF"
    }
  ]
}
```

**返回字段说明**

| 返回值字段 | 字段类型 | 字段说明 |
|-----------|---------|---------|
| paraId | int | 参数ID |
| prmSysId | int | 参数所属系统ID |
| paraCode | string | 参数代号 |
| paraType | int | 参数类型 |
| paraTypeChar | string | 参数类型字符 |
| paraTypeDesc | string | 参数类型描述 |
| paraDesc | string | 参数描述 |
| minValue | double | 最小值 |
| maxValue | double | 最大值 |
| updateTime | int | 更新周期(ms) |
| valueDesc | string | 值描述 |
| validFlag | int | 有效标志 |
| watchFlag | int | 监视标志 |
| parameterType | int | 参数分类 |
| editGroup | string | 编辑分组 |
| procId | int | 处理方法ID |
| procDesc | string | 处理方法描述 |
| paraMask | string | 参数掩码 |

**对应 gRPC方法**

`GetBasicDbPara(gSatelliteThumb) returns (gView_Paras)`

### GET 方式（Query 传卫星三元组）

与 POST **数据一致**，便于浏览器直链或缓存；卫星标识通过 **QueryString** 传递。

**URL**

`GET /api/mass-data/basic/parameters?taskNo={taskNo}&satNo={satNo}&dbStage={dbStage}`

**HTTP请求方式**

GET

**参数**

| 参数名 | 必选 | 类型 | 说明 |
|--------|------|------|------|
| taskNo | true | string | 型号代号 |
| satNo | true | string | 卫星代号 |
| dbStage | true | string | 阶段代号 |

**返回结果 / 字段说明**

与上文 POST 相同（`MassDataListResponse<BasicParameterDto>`）。

---

## 4.2 查询指令表

获取指定卫星的基础库指令定义表。

**URL**

`POST /api/mass-data/basic/commands`

**支持格式**

JSON

**HTTP请求方式**

POST

**参数**

| 参数名 | 必选 | 类型 | 说明 |
|--------|------|------|------|
| taskNo | true | string | 型号代号 |
| satNo | true | string | 卫星代号 |
| dbStage | true | string | 阶段代号 |

**返回结果**

```json
{
  "datas": [
    {
      "cmdId": 1001,
      "cmdSysId": 1,
      "cmdCode": "CMD_001",
      "cmdType": 1,
      "cmdDesc": "指令描述",
      "cmdLen": 16,
      "cmdData": "A0 B1 C2",
      "exeTime": 100,
      "cmdLevel": 1,
      "validFlag": 1,
      "isStarMiddleCmd": false,
      "singnl": "SIGNAL_01",
      "allowCheckData": true
    }
  ]
}
```

**返回字段说明**

| 返回值字段 | 字段类型 | 字段说明 |
|-----------|---------|---------|
| cmdId | int | 指令ID |
| cmdSysId | int | 指令所属系统ID |
| cmdCode | string | 指令代号 |
| cmdType | int | 指令类型 |
| cmdDesc | string | 指令描述 |
| cmdLen | int | 指令长度 |
| cmdData | string | 指令数据（码字） |
| exeTime | int | 执行时间(ms) |
| cmdLevel | int | 指令级别 |
| validFlag | int | 有效标志 |
| isStarMiddleCmd | bool | 是否星上中间指令 |
| singnl | string | 信号标识 |
| allowCheckData | bool | 是否允许数据校验 |

**对应 gRPC方法**

`GetBasicDbCmd(gSatelliteThumb) returns (gView_Cmds)`

---

## 4.3 查询包表

获取指定卫星的基础库包定义表。

**URL**

`POST /api/mass-data/basic/packages`

**支持格式**

JSON

**HTTP请求方式**

POST

**参数**

| 参数名 | 必选 | 类型 | 说明 |
|--------|------|------|------|
| taskNo | true | string | 型号代号 |
| satNo | true | string | 卫星代号 |
| dbStage | true | string | 阶段代号 |

**返回结果**

```json
{
  "datas": [
    {
      "sysId": 1,
      "pkgFlag": "PKG_FLAG_01",
      "pkgLen": 128,
      "subFlag": "SUB_01",
      "pkgDesc": "包描述",
      "updateTime": 10,
      "validFlag": 1,
      "pkgFlagAssist": "ASSIST_01",
      "sysCode": "SYS_001"
    }
  ]
}
```

**返回字段说明**

| 返回值字段 | 字段类型 | 字段说明 |
|-----------|---------|---------|
| sysId | int | 系统ID |
| pkgFlag | string | 包标识 |
| pkgLen | int | 包长度 |
| subFlag | string | 子标志 |
| pkgDesc | string | 包描述 |
| updateTime | int | 更新周期(ms) |
| validFlag | int | 有效标志 |
| pkgFlagAssist | string | 辅助包标识 |
| sysCode | string | 系统代号 |

**对应 gRPC方法**

`GetBasicDbPkg(gSatelliteThumb) returns (gView_Pkgs)`

---

## 4.4 查询系统层级关系

获取指定卫星的基础库层级关系（包层级与指令层级）。

**URL**

`POST /api/mass-data/basic/relations`

**支持格式**

JSON

**HTTP请求方式**

POST

**参数**

| 参数名 | 必选 | 类型 | 说明 |
|--------|------|------|------|
| taskNo | true | string | 型号代号 |
| satNo | true | string | 卫星代号 |
| dbStage | true | string | 阶段代号 |

**返回结果**

```json
{
  "pkgHirberDatas": [
    {
      "sysId": 1,
      "sysCode": "SYS_001",
      "sysDesc": "系统描述",
      "fatherSysId": 0,
      "level": 1,
      "sysType": 1
    }
  ],
  "cmdHirberDatas": [
    {
      "sysId": 10,
      "sysCode": "CMD_SYS_001",
      "sysDesc": "指令系统描述",
      "fatherSysId": 1,
      "level": 2,
      "sysType": 2
    }
  ]
}
```

**返回字段说明**

| 返回值字段 | 字段类型 | 字段说明 |
|-----------|---------|---------|
| pkgHirberDatas | array | 包层级关系列表 |
| cmdHirberDatas | array | 指令层级关系列表 |

**层级关系字段说明**

| 返回值字段 | 字段类型 | 字段说明 |
|-----------|---------|---------|
| sysId | int | 系统ID |
| sysCode | string | 系统代号 |
| sysDesc | string | 系统描述 |
| fatherSysId | int | 父系统ID |
| level | int | 层级 |
| sysType | int | 系统类型 |

**对应 gRPC方法**

`GetBasicDbRelation(gSatelliteThumb) returns (gView_Sys_Relates)`

---

## 4.5 查询指令判据表

获取指定卫星的基础库指令判据定义表。

**URL**

`POST /api/mass-data/basic/cmd-judges`

**支持格式**

JSON

**HTTP请求方式**

POST

**参数**

| 参数名 | 必选 | 类型 | 说明 |
|--------|------|------|------|
| taskNo | true | string | 型号代号 |
| satNo | true | string | 卫星代号 |
| dbStage | true | string | 阶段代号 |

**返回结果**

```json
{
  "datas": [
    {
      "cmdId": 1001,
      "judgeId": 1,
      "paraId": 2001,
      "judgeType": 1,
      "iDnValue": "0",
      "iUpValue": "100",
      "rDnValue": "-10",
      "rUpValue": "10",
      "vDnValue": "0",
      "vUpValue": "5",
      "judgeTime": 500
    }
  ]
}
```

**返回字段说明**

| 返回值字段 | 字段类型 | 字段说明 |
|-----------|---------|---------|
| cmdId | int | 指令ID |
| judgeId | int | 判据ID |
| paraId | int | 参数ID |
| judgeType | int | 判据类型 |
| iDnValue | string | 注入判据下限 |
| iUpValue | string | 注入判据上限 |
| rDnValue | string | 遥测判据下限 |
| rUpValue | string | 遥测判据上限 |
| vDnValue | string | 验证判据下限 |
| vUpValue | string | 验证判据上限 |
| judgeTime | int | 判读时间(ms) |

**对应 gRPC方法**

`GetBasicDbCmdJudges(gSatelliteThumb) returns (gView_Cmdjudges)`

---

# 5. 历史数据查询与统计

历史数据存储于 MongoDB 时序集合，数据量可能非常大。

- **gRPC**：`QuerygPara` 等以 **分批发流**（如 `gParaCollect` 批次）返回，每批可含多条记录，客户端应逐批 `ReadAllAsync` / `MoveNext` 消费。
- **Web API**：`POST /api/mass-data/query/parameters` 等为 **一次性缓冲** 后返回 `datas` 列表；另有 **`…/stream` 后缀** 的路由返回 **`IAsyncEnumerable<T>`**，HTTP 体为 **顶层 JSON 数组**（`[` 起逐元素写出），与 gRPC 批次形态不同，客户端需按字节流增量解析数组元素（参见 §5.1.1 等）。

## 5.1 查询参数数据

根据查询条件获取卫星历史参数数据（**一次性返回** `datas` 列表；服务端内部流式读取后聚合）。

**URL**

`POST /api/mass-data/query/parameters`

**支持格式**

JSON

**HTTP请求方式**

POST

**参数**

| 参数名 | 必选 | 类型 | 说明 |
|--------|------|------|------|
| taskNo | true | string | 型号代号 |
| satNo | true | string | 卫星代号 |
| dbStage | true | string | 阶段代号 |
| fromDt | true | DateTime | 查询起始时间（ISO-8601 UTC） |
| toDt | true | DateTime | 查询结束时间（ISO-8601 UTC） |
| pkgParaIds | true | array | 包-参数关系列表 |
| limitTotal | false | int | 最大返回条数，`-1` 表示不限制（默认 `-1`） |

**pkgParaIds 子字段说明**

| 参数名 | 必选 | 类型 | 说明 |
|--------|------|------|------|
| pid | true | int | 包ID |
| id | true | array[int] | 该包下的参数ID列表 |
| rtDelayFlag | false | int | 实时延迟标志 |
| dataProvider | false | int | 数据提供者 |

**返回结果**

```json
{
  "datas": [
    {
      "id": 2001,
      "pid": 1001,
      "pv": "12.34",
      "sv": "1234",
      "pd": "OK",
      "dt": "2026-03-19T00:00:10Z",
      "st": "2026-03-19T00:00:11Z",
      "dtTicks": 638780448100000000,
      "pc": "PARA_CODE_1",
      "satNo": "SAT_01",
      "taskNo": "TASK_A"
    }
  ]
}
```

**返回字段说明**

| 返回值字段 | 字段类型 | 字段说明 |
|-----------|---------|---------|
| id | int | 参数ID |
| pid | int | 所属包ID |
| pv | string | 工程值 |
| sv | string | 源码值 |
| pd | string | 参数值描述（枚举型时为枚举描述） |
| dt | DateTime | 参数采集时间 |
| st | DateTime | 参数入库时间 |
| dtTicks | long | 采集时间Ticks |
| pc | string | 参数代号 |
| satNo | string | 卫星代号 |
| taskNo | string | 型号代号 |

**对应 gRPC方法**

`QuerygPara(gQueryCondParaReq) returns (stream gParaCollect)`

### 5.1.1 HTTP 流式 JSON 数组：`POST /api/mass-data/query/parameters/stream`

**说明**

- 请求体与 **§5.1** 相同（含 `limitTotal`）。
- 响应 `Content-Type: application/json`，**Body 为顶层 JSON 数组**：`[ ParameterDto, ... ]`，无外层 `datas` 信封。
- 服务端按枚举 **逐元素序列化**；客户端建议使用 **ReadableStream** 边收边解析（勿假设整包一次性到达）。

**URL**

`POST /api/mass-data/query/parameters/stream`

**对应 gRPC方法**

与 §5.1 相同：`QuerygPara`（语义对齐；HTTP 为单卫星 JSON 数组形态）。

---

## 5.2 查询复合参数（与 gRPC QueryParaAggregate 对齐）

按时间对齐的 **指令 + 参数** 合成结果（与 `AggregateParameters` / `query/parameters/aggregate` 不同：后者为时间桶聚合统计）。

### 5.2.1 一次性返回：`POST /api/mass-data/query/parameters/para-aggregate`

**URL**

`POST /api/mass-data/query/parameters/para-aggregate`

**HTTP请求方式**

POST

**参数**

| 参数名 | 必选 | 类型 | 说明 |
|--------|------|------|------|
| taskNo | true | string | 型号代号 |
| satNo | true | string | 卫星代号 |
| dbStage | true | string | 阶段代号 |
| fromDt | true | DateTime | 查询起始时间（ISO-8601 UTC） |
| toDt | true | DateTime | 查询结束时间（ISO-8601 UTC） |
| pkgParaIds | true | array | 包-参数关系列表（子字段见 **§5.1「pkgParaIds 子字段说明」**） |
| containInst | false | bool | 是否包含指令数据（默认 `true`） |
| splitPage | false | bool | 是否启用 Mongo 管道内 skip/limit |
| skipNum | false | int | 与 `splitPage` 配合 |
| limitNum | false | int | 与 `splitPage` 配合 |

**返回结果**

`MassDataListResponse<CompositeParameterPointDto>`：

```json
{
  "datas": [
    {
      "taskNo": "TASK_A",
      "satNo": "SAT_01",
      "dbStage": "DEV",
      "time": "2026-03-19T00:00:10Z",
      "instDatas": [{ "ci": 1, "cc": "CMD", "cd": "", "cn": "" }],
      "paraDatas": [{ "id": 2001, "pv": 12.34, "pd": "", "sv": "" }]
    }
  ]
}
```

**对应 gRPC方法**

`QueryParaAggregate(gQueryCondParaAggregateReq) returns (stream gparaCompositeResult)`（gRPC 侧按批封装多条 `gparaComposite`；Web API 单卫星下列表或数组流为扁平元素序列）。

### 5.2.2 HTTP 流式 JSON 数组：`POST /api/mass-data/query/parameters/para-aggregate/stream`

请求体同 §5.2.1；响应为顶层数组 `[ CompositeParameterPointDto, ... ]`。

---

## 5.3 查询指令数据

根据查询条件获取卫星指令数据（一次性返回全部结果）。

**URL**

`POST /api/mass-data/query/instructions`

**支持格式**

JSON

**HTTP请求方式**

POST

**参数**

| 参数名 | 必选 | 类型 | 说明 |
|--------|------|------|------|
| taskNo | true | string | 型号代号 |
| satNo | true | string | 卫星代号 |
| dbStage | true | string | 阶段代号 |
| fromDt | true | DateTime | 查询起始时间（ISO-8601 UTC） |
| toDt | true | DateTime | 查询结束时间（ISO-8601 UTC） |
| instIds | true | array | 指令查询列表，每个元素为 `[指令ID, channelId]` |

**返回结果**

```json
{
  "satNo": "SAT_01",
  "taskNo": "TASK_A",
  "datas": [
    {
      "ci": 1001,
      "cc": "CMD_001",
      "cd": "A0 B1 C2",
      "cn": "指令名称",
      "et": "2026-03-19T00:00:10Z",
      "cJudgeInfo": "",
      "relativeParas": [
        {
          "id": 2001,
          "pid": 1001,
          "pv": "12.34",
          "sv": "1234",
          "pd": "OK",
          "dt": "2026-03-19T00:00:10Z",
          "st": "2026-03-19T00:00:11Z",
          "dtTicks": 638780448100000000,
          "pc": "PARA_CODE_1",
          "satNo": "SAT_01",
          "taskNo": "TASK_A"
        }
      ]
    }
  ]
}
```

**返回字段说明**

| 返回值字段 | 字段类型 | 字段说明 |
|-----------|---------|---------|
| ci | int | 指令ID |
| cc | string | 指令代号 |
| cd | string | 指令数据（十六进制码字） |
| cn | string | 指令名称 |
| et | DateTime | 指令发送时间 |
| cJudgeInfo | string | 指令判读信息 |
| relativeParas | array | 关联的参数数据列表 |

> 若数据量极大，**gRPC** 请优先使用 `QuerygInstStream` 分批发流；**Web API** 的 `query/instructions/stream` 当前实现为服务端先枚举再 **整包 JSON 返回**（与 `query/instructions` 同一信封结构），并非 HTTP 顶层 JSON 数组流。

**对应 gRPC方法**

`QuerygInst(gQueryCondInstReq) returns (gInstCollect)`

---

## 5.4 Web API：`POST /api/mass-data/query/instructions/stream`

与 **§5.3** 请求体一致；服务端内部以流式枚举读取指令后，**聚合为完整 `datas` 列表** 再序列化为单次 HTTP 响应（`MassDataEnvelopeResponse<InstructionDto>`），形态与 §5.3 相同。

> 若需 **HTTP 顶层 JSON 数组** 边写边出，请使用 `query/parameters/stream`、`query/parameters/para-aggregate/stream`、`query/packages/stream`、`query/frames/stream` 等 `IAsyncEnumerable` 路由（见各节子条款）。

**URL**

`POST /api/mass-data/query/instructions/stream`

**对应 gRPC方法**

`QuerygInstStream(gQueryCondInstReq) returns (stream gInstCollect)`（gRPC 侧为分批发流；与当前 Web API 缓冲实现不完全等价）。

---

## 5.5 查询包数据

根据查询条件获取卫星包数据（**一次性返回** `satNo` / `taskNo` + `datas` 列表）。

**URL**

`POST /api/mass-data/query/packages`

**支持格式**

JSON

**HTTP请求方式**

POST

**参数**

| 参数名 | 必选 | 类型 | 说明 |
|--------|------|------|------|
| taskNo | true | string | 型号代号 |
| satNo | true | string | 卫星代号 |
| dbStage | true | string | 阶段代号 |
| fromDt | true | DateTime | 查询起始时间（ISO-8601 UTC） |
| toDt | true | DateTime | 查询结束时间（ISO-8601 UTC） |
| pkgIds | true | array | 包查询列表，每个元素为 `(包ID, chId, rtFlag)` |

**返回结果**

```json
{
  "satNo": "SAT_01",
  "taskNo": "TASK_A",
  "datas": [
    {
      "pi": 1001,
      "pc": "PKG_CODE_01",
      "pd": "A0 B1 C2 ...",
      "pt": "2026-03-19T00:00:10Z"
    }
  ]
}
```

**返回字段说明**

| 返回值字段 | 字段类型 | 字段说明 |
|-----------|---------|---------|
| pi | int | 包ID |
| pc | string | 包代号 |
| pd | string | 包数据（十六进制） |
| pt | DateTime | 包采集时间 |

**对应 gRPC方法**

`QuerygPkg(gQueryCondPkgReq) returns (stream gPkgCollect)`

### 5.5.1 HTTP 流式 JSON 数组：`POST /api/mass-data/query/packages/stream`

请求体与 **§5.5** 相同；响应体为顶层数组 `[ PackageDto, ... ]`（无 `satNo`/`taskNo` 信封，卫星信息在请求 JSON 中）。

---

## 5.6 查询帧数据

根据查询条件获取卫星帧数据（**一次性返回** 信封列表）。

**URL**

`POST /api/mass-data/query/frames`

**支持格式**

JSON

**HTTP请求方式**

POST

**参数**

| 参数名 | 必选 | 类型 | 说明 |
|--------|------|------|------|
| taskNo | true | string | 型号代号 |
| satNo | true | string | 卫星代号 |
| dbStage | true | string | 阶段代号 |
| fromDt | true | DateTime | 查询起始时间（ISO-8601 UTC） |
| toDt | true | DateTime | 查询结束时间（ISO-8601 UTC） |
| chId | false | int | 通道ID |
| rtFlag | false | int | 实时延迟标志 |

**返回结果**

```json
{
  "satNo": "SAT_01",
  "taskNo": "TASK_A",
  "datas": [
    {
      "ft": "2026-03-19T00:00:10Z",
      "fno": 1,
      "fd": "A0 B1 C2 D3 ..."
    }
  ]
}
```

**返回字段说明**

| 返回值字段 | 字段类型 | 字段说明 |
|-----------|---------|---------|
| ft | DateTime | 帧时间 |
| fno | int | 秒内帧序号 |
| fd | string | 帧数据（十六进制） |

**对应 gRPC方法**

`QuerygFrame(gQueryCondFrameReq) returns (stream gFrameCollect)`

### 5.6.1 HTTP 流式 JSON 数组：`POST /api/mass-data/query/frames/stream`

请求体与 **§5.6** 相同；响应体为顶层数组 `[ FrameDto, ... ]`。

---

## 5.7 参数聚合统计

对指定参数在指定时间范围内按指定聚合方式和时间粒度进行聚合计算。

**URL**

`POST /api/mass-data/query/parameters/aggregate`

**支持格式**

JSON

**HTTP请求方式**

POST

**参数**

| 参数名 | 必选 | 类型 | 说明 |
|--------|------|------|------|
| taskNo | true | string | 型号代号 |
| satNo | true | string | 卫星代号 |
| dbStage | true | string | 阶段代号 |
| fromDt | true | DateTime | 查询起始时间（ISO-8601 UTC） |
| toDt | true | DateTime | 查询结束时间（ISO-8601 UTC） |
| parameterIds | true | array[int] | 参数ID列表 |
| intervalSeconds | true | int | 聚合时间粒度（秒） |
| aggregationType | true | string | 聚合类型：Average / Max / Min / Sum / Count / First / Last / StdDev |

**返回结果**

```json
{
  "satNo": "SAT_01",
  "taskNo": "TASK_A",
  "datas": [
    {
      "timeBucket": "2026-03-19T00:00:00Z",
      "parameterId": 2001,
      "parameterCode": "PARA_CODE_1",
      "value": 11.2,
      "count": 60
    }
  ]
}
```

**返回字段说明**

| 返回值字段 | 字段类型 | 字段说明 |
|-----------|---------|---------|
| timeBucket | DateTime | 聚合时间桶起始时间 |
| parameterId | int | 参数ID |
| parameterCode | string | 参数代号 |
| value | double | 聚合值 |
| count | long | 参与聚合的数据点数 |

**对应 gRPC方法**

`AggregateParameters(gQueryCondParaAggReq) returns (gParaAggregationCollect)`

---

## 5.8 单参数统计信息

获取指定单个参数在指定时间范围内的统计概要信息（总数、起止时间、最值、均值、标准差）。

**URL**

`POST /api/mass-data/query/parameters/statistics`

**支持格式**

JSON

**HTTP请求方式**

POST

**参数**

| 参数名 | 必选 | 类型 | 说明 |
|--------|------|------|------|
| taskNo | true | string | 型号代号 |
| satNo | true | string | 卫星代号 |
| dbStage | true | string | 阶段代号 |
| fromDt | true | DateTime | 查询起始时间（ISO-8601 UTC） |
| toDt | true | DateTime | 查询结束时间（ISO-8601 UTC） |
| parameterId | true | int | 参数ID |

**返回结果**

```json
{
  "parameterId": 2001,
  "parameterCode": "PARA_CODE_1",
  "totalCount": 3600,
  "firstTimestamp": "2026-03-19T00:00:00Z",
  "lastTimestamp": "2026-03-19T00:59:59Z",
  "minValue": 0.5,
  "maxValue": 99.8,
  "averageValue": 50.2,
  "stdDevValue": 12.3
}
```

**返回字段说明**

| 返回值字段 | 字段类型 | 字段说明 |
|-----------|---------|---------|
| parameterId | int | 参数ID |
| parameterCode | string | 参数代号 |
| totalCount | long | 数据点总数 |
| firstTimestamp | DateTime | 第一条数据时间 |
| lastTimestamp | DateTime | 最后一条数据时间 |
| minValue | double | 最小值 |
| maxValue | double | 最大值 |
| averageValue | double | 平均值 |
| stdDevValue | double | 标准差 |

**对应 gRPC方法**

`GetParameterStatistics(gQueryCondParaStatisticsReq) returns (gParameterStatisticsReply)`

---

## 5.9 数据量统计

获取指定卫星在指定时间范围内的各类数据总量与存储大小统计。

**URL**

`POST /api/mass-data/query/data-volume`

**支持格式**

JSON

**HTTP请求方式**

POST

**参数**

| 参数名 | 必选 | 类型 | 说明 |
|--------|------|------|------|
| taskNo | true | string | 型号代号 |
| satNo | true | string | 卫星代号 |
| dbStage | true | string | 阶段代号 |
| fromDt | true | DateTime | 查询起始时间（ISO-8601 UTC） |
| toDt | true | DateTime | 查询结束时间（ISO-8601 UTC） |

**返回结果**

```json
{
  "parameterCount": 120000,
  "instructionCount": 500,
  "packageCount": 10000,
  "frameCount": 60000,
  "judgeResultCount": 2000,
  "totalCount": 192500,
  "totalSizeBytes": 52428800,
  "oldestTimestamp": "2026-03-19T00:00:00Z",
  "newestTimestamp": "2026-03-19T01:00:00Z"
}
```

**返回字段说明**

| 返回值字段 | 字段类型 | 字段说明 |
|-----------|---------|---------|
| parameterCount | long | 参数数据条数 |
| instructionCount | long | 指令数据条数 |
| packageCount | long | 包数据条数 |
| frameCount | long | 帧数据条数 |
| judgeResultCount | long | 判读结果条数 |
| totalCount | long | 总数据条数 |
| totalSizeBytes | long | 总存储大小（字节） |
| oldestTimestamp | DateTime | 最早数据时间 |
| newestTimestamp | DateTime | 最新数据时间 |

**对应 gRPC方法**

`GetDataVolumeStatistics(gQueryCondDataVolumeReq) returns (gDataVolumeStatisticsReply)`

---

# 6. 判读与判据规则

## 6.1 获取判读信息

获取指定卫星的所有判据规则信息列表。

**URL**

`POST /api/mass-data/judge/infos`

**支持格式**

JSON

**HTTP请求方式**

POST

**参数**

| 参数名 | 必选 | 类型 | 说明 |
|--------|------|------|------|
| taskNo | true | string | 型号代号 |
| satNo | true | string | 卫星代号 |
| dbStage | true | string | 阶段代号 |

**返回结果**

```json
{
  "satNo": "SAT_01",
  "taskNo": "TASK_A",
  "datas": [
    {
      "code": "JUDGE_001",
      "desc": "判据描述",
      "group": "G1",
      "ruleType": "Threshold",
      "resultValueType": "Double",
      "resultValueDesc": "电压",
      "lastTime": "2026-03-19T00:00:00Z",
      "data": "{\"ruleDetail\": \"...\"}",
      "statue": 1,
      "tags": ["tag1", "tag2"],
      "id": "507f1f77bcf86cd799439011",
      "isEnable": true,
      "isResultDisplay": true,
      "isResultInDb": true
    }
  ]
}
```

**返回字段说明**

| 返回值字段 | 字段类型 | 字段说明 |
|-----------|---------|---------|
| code | string | 判据代号 |
| desc | string | 判据描述 |
| group | string | 判据分组 |
| ruleType | string | 判据规则类型 |
| resultValueType | string | 结果值类型 |
| resultValueDesc | string | 结果值描述 |
| lastTime | DateTime | 最后更新时间 |
| data | string | 判据规则JSON内容 |
| statue | int | 状态 |
| tags | array[string] | 标签列表 |
| id | string | MongoDB 文档ID |
| isEnable | bool | 是否启用 |
| isResultDisplay | bool | 是否显示结果 |
| isResultInDb | bool | 是否结果入库 |

**对应 gRPC方法**

`GetJudgeInfos(gSatelliteThumb) returns (gJudgeInfoCollect)`

---

## 6.2 查询判读结果

根据查询条件获取卫星判读结果数据（流式）。

**URL**

`POST /api/mass-data/judge/results`

**支持格式**

JSON

**HTTP请求方式**

POST

**参数**

| 参数名 | 必选 | 类型 | 说明 |
|--------|------|------|------|
| taskNo | true | string | 型号代号 |
| satNo | true | string | 卫星代号 |
| dbStage | true | string | 阶段代号 |
| fromDt | true | DateTime | 查询起始时间（ISO-8601 UTC） |
| toDt | true | DateTime | 查询结束时间（ISO-8601 UTC） |
| judgeCodes | true | array[string] | 判据代号列表 |

**返回结果**

```json
{
  "satNo": "SAT_01",
  "taskNo": "TASK_A",
  "datas": [
    {
      "judgeRuleCode": "JUDGE_001",
      "judgeValue": "123.45",
      "judgeValueDesc": "正常",
      "createTime": "2026-03-19T00:00:10Z"
    }
  ]
}
```

**返回字段说明**

| 返回值字段 | 字段类型 | 字段说明 |
|-----------|---------|---------|
| judgeRuleCode | string | 判据规则代号 |
| judgeValue | string | 判读结果值 |
| judgeValueDesc | string | 判读结果描述 |
| createTime | DateTime | 结果生成时间 |

**对应 gRPC方法**

`QuerygJudgeResults(gQueryCondJudgeReusltReq) returns (stream gJudgeResultCollect)`

---

## 6.3 获取判据规则列表

获取指定卫星判据规则数据库中的所有判据规则（JSON 文档）。

**URL**

`POST /api/mass-data/judge/rules`

**支持格式**

JSON

**HTTP请求方式**

POST

**参数**

| 参数名 | 必选 | 类型 | 说明 |
|--------|------|------|------|
| taskNo | true | string | 型号代号 |
| satNo | true | string | 卫星代号 |
| dbStage | true | string | 阶段代号 |

**返回结果**

```json
{
  "datas": [
    {
      "id": "507f1f77bcf86cd799439011",
      "data": "{\"code\":\"RULE_001\",\"name\":\"规则名称\",...}"
    }
  ]
}
```

**返回字段说明**

| 返回值字段 | 字段类型 | 字段说明 |
|-----------|---------|---------|
| id | string | MongoDB 文档ID |
| data | string | 判据规则JSON内容 |

**对应 gRPC方法**

`GetJudgeRules(gSatRuleDbTarget) returns (gJsonEntityCollection)`

---

## 6.4 按 Code 获取单条判据规则

根据判据代号（code）获取单条判据规则。

**URL**

`POST /api/mass-data/judge/rules/by-code`

**支持格式**

JSON

**HTTP请求方式**

POST

**参数**

| 参数名 | 必选 | 类型 | 说明 |
|--------|------|------|------|
| taskNo | true | string | 型号代号 |
| satNo | true | string | 卫星代号 |
| dbStage | true | string | 阶段代号 |
| code | true | string | 判据代号 |

**返回结果**

```json
{
  "id": "507f1f77bcf86cd799439011",
  "data": "{\"code\":\"RULE_001\",\"name\":\"规则名称\",...}"
}
```

**返回字段说明**

| 返回值字段 | 字段类型 | 字段说明 |
|-----------|---------|---------|
| id | string | MongoDB 文档ID |
| data | string | 判据规则JSON内容 |

**对应 gRPC方法**

`GetJudgeRuleByCode(gRuleCodeReq) returns (gJsonEntity)`

---

## 6.5 新增/更新判据规则

新增或更新一条判据规则（id 为空时新增，否则更新）。

**URL**

`POST /api/mass-data/judge/rules/upsert`

**支持格式**

JSON

**HTTP请求方式**

POST

**参数**

| 参数名 | 必选 | 类型 | 说明 |
|--------|------|------|------|
| taskNo | true | string | 型号代号 |
| satNo | true | string | 卫星代号 |
| dbStage | true | string | 阶段代号 |
| id | false | string | MongoDB 文档ID（null/空时新增，否则更新） |
| data | true | string | 判据规则JSON内容 |

**返回结果**

```json
{
  "id": "507f1f77bcf86cd799439011"
}
```

**返回字段说明**

| 返回值字段 | 字段类型 | 字段说明 |
|-----------|---------|---------|
| id | string | 新增或更新后的文档ID |

**对应 gRPC方法**

`UpsertJudgeRule(gJudgeRuleUpsertReq) returns (gEntityIdReq)`

---

## 6.6 删除判据规则

根据文档ID删除一条判据规则。

**URL**

`POST /api/mass-data/judge/rules/delete`

**支持格式**

JSON

**HTTP请求方式**

POST

**参数**

| 参数名 | 必选 | 类型 | 说明 |
|--------|------|------|------|
| taskNo | true | string | 型号代号 |
| satNo | true | string | 卫星代号 |
| dbStage | true | string | 阶段代号 |
| id | true | string | 要删除的判据规则文档ID |

**返回结果**

HTTP 200 OK（无响应体）

**对应 gRPC方法**

`DeleteJudgeRule(gJudgeRuleDeleteReq) returns (google.protobuf.Empty)`

---

# 7. 数采文件下载

## 7.1 下载解析配置文件

下载指定卫星的数采解析配置文件（DLL、XML、INI、SubFlag 等）。

**URL**

`POST /api/mass-data/files/resolve`

**支持格式**

JSON

**HTTP请求方式**

POST

**参数**

| 参数名 | 必选 | 类型 | 说明 |
|--------|------|------|------|
| taskNo | true | string | 型号代号 |
| satNo | true | string | 卫星代号 |
| dbStage | true | string | 阶段代号 |
| absoluteDir | true | string | 配置文件绝对目录路径（GridFS 查询键） |

**返回结果**

```json
{
  "datas": [
    {
      "dirPath": "/configs/TASK_A/SAT_01/",
      "fileType": "TEXT",
      "fileName": "resolve.xml",
      "datas": "<xml>...</xml>"
    }
  ]
}
```

**返回字段说明**

| 返回值字段 | 字段类型 | 字段说明 |
|-----------|---------|---------|
| dirPath | string | 文件目录路径 |
| fileType | string | 文件类型枚举：DIR / TEXT / BYTES |
| fileName | string | 文件名 |
| datas | bytes | 文件内容（bytes 类型为二进制） |

**对应 gRPC方法**

`DownLoadTMResolveFiles(gDownCfgRevoleFileReq) returns (stream gDatafile)`

---

# 8. 平台配置管理

以下接口独立于卫星数据服务，路由前缀为 `/api/config`，用于管理平台配置文件 `allServerPlats.json`。

## 8.1 获取平台配置文件

获取平台全局配置文件内容。

**URL**

`GET /api/config`

**支持格式**

JSON

**HTTP请求方式**

GET

**参数**

无

**返回结果**

```json
{
  "serverPlats": [
    {
      "TaskDbMongoUrl": "mongodb://...",
      "satitems": [
        {
          "satelliteNo": "SAT_01",
          "taskNo": "TASK_A",
          "enabled": true
        }
      ]
    }
  ]
}
```

> 返回原始 JSON 内容，结构按配置文件实际内容。

---

## 8.2 保存/更新配置文件

保存或覆盖平台配置文件。

**URL**

`POST /api/config`

**支持格式**

JSON

**HTTP请求方式**

POST

**参数**

请求体为完整的配置文件 JSON 对象。

**返回结果**

```json
{
  "success": true,
  "message": "配置保存成功",
  "path": "C:\\...\\Config\\allServerPlats.json"
}
```

**返回字段说明**

| 返回值字段 | 字段类型 | 字段说明 |
|-----------|---------|---------|
| success | bool | 是否保存成功 |
| message | string | 结果消息 |
| path | string | 配置文件绝对路径 |

---

## 8.3 验证配置文件格式

验证传入的配置JSON结构是否符合规范。

**URL**

`POST /api/config/validate`

**支持格式**

JSON

**HTTP请求方式**

POST

**参数**

请求体为待验证的配置文件 JSON 对象（须包含 `serverPlats` 节点）。

**返回结果**

```json
{
  "valid": true,
  "errors": [],
  "warnings": ["第 1 个平台的第 1 个卫星未设置 Enabled 字段，建议设置为 true"]
}
```

**返回字段说明**

| 返回值字段 | 字段类型 | 字段说明 |
|-----------|---------|---------|
| valid | bool | 整体校验是否通过 |
| errors | array[string] | 错误信息列表 |
| warnings | array[string] | 警告信息列表 |

---

## 8.4 获取配置文件路径信息

获取配置文件路径、是否存在、最后修改时间等元信息。

**URL**

`GET /api/config/info`

**支持格式**

JSON

**HTTP请求方式**

GET

**参数**

无

**返回结果**

```json
{
  "path": "C:\\...\\Config\\allServerPlats.json",
  "exists": true,
  "directory": "C:\\...\\Config",
  "lastModified": "2026-05-10 14:30:00"
}
```

**返回字段说明**

| 返回值字段 | 字段类型 | 字段说明 |
|-----------|---------|---------|
| path | string | 配置文件绝对路径 |
| exists | bool | 配置文件是否存在 |
| directory | string | 配置文件所在目录 |
| lastModified | string | 最后修改时间（格式 yyyy-MM-dd HH:mm:ss） |

---

## 8.5 备份配置文件

备份当前配置文件到 `backup/` 子目录，自动保留最近5个备份。

**URL**

`POST /api/config/backup`

**支持格式**

JSON

**HTTP请求方式**

POST

**参数**

无

**返回结果**

```json
{
  "success": true,
  "backupPath": "C:\\...\\Config\\backup\\allServerPlats_20260510_143000.json"
}
```

**返回字段说明**

| 返回值字段 | 字段类型 | 字段说明 |
|-----------|---------|---------|
| success | bool | 是否备份成功 |
| backupPath | string | 备份文件绝对路径 |

---

# 附录A：gRPC 接口清单

以下为 `MassPlatform.MassDataServer` 服务的全部 25 个 gRPC 方法清单：

## 卫星与配置

| RPC 方法 | 请求类型 | 返回类型 |
|----------|---------|---------|
| GetAllSatsFromMassServer | google.protobuf.Empty | gSatCollectionReply |
| GetSatCfg | gSatelliteThumb | satconncfg |
| GetMessageQueueInfo | gSatelliteThumb | gMsgQueueInfoReply |
| GetRedisInfo | gSatelliteThumb | gRedisInfoReply |

## 基础库

| RPC 方法 | 请求类型 | 返回类型 |
|----------|---------|---------|
| GetBasicDbPara | gSatelliteThumb | gView_Paras |
| GetBasicDbCmd | gSatelliteThumb | gView_Cmds |
| GetBasicDbPkg | gSatelliteThumb | gView_Pkgs |
| GetBasicDbRelation | gSatelliteThumb | gView_Sys_Relates |
| GetBasicDbCmdJudges | gSatelliteThumb | gView_Cmdjudges |

## 查询与统计

| RPC 方法 | 请求类型 | 返回类型 |
|----------|---------|---------|
| QuerygPara | gQueryCondParaReq | stream gParaCollect |
| QueryParaAggregate | gQueryCondParaAggregateReq | stream gparaCompositeResult |
| QuerygInst | gQueryCondInstReq | gInstCollect |
| QuerygInstStream | gQueryCondInstReq | stream gInstCollect |
| QuerygPkg | gQueryCondPkgReq | stream gPkgCollect |
| QuerygFrame | gQueryCondFrameReq | stream gFrameCollect |
| AggregateParameters | gQueryCondParaAggReq | gParaAggregationCollect |
| GetParameterStatistics | gQueryCondParaStatisticsReq | gParameterStatisticsReply |
| GetDataVolumeStatistics | gQueryCondDataVolumeReq | gDataVolumeStatisticsReply |

## 判读与判据规则

| RPC 方法 | 请求类型 | 返回类型 |
|----------|---------|---------|
| GetJudgeInfos | gSatelliteThumb | gJudgeInfoCollect |
| QuerygJudgeResults | gQueryCondJudgeReusltReq | stream gJudgeResultCollect |
| GetJudgeRules | gSatRuleDbTarget | gJsonEntityCollection |
| GetJudgeRuleByCode | gRuleCodeReq | gJsonEntity |
| UpsertJudgeRule | gJudgeRuleUpsertReq | gEntityIdReq |
| DeleteJudgeRule | gJudgeRuleDeleteReq | google.protobuf.Empty |

## 数采文件

| RPC 方法 | 请求类型 | 返回类型 |
|----------|---------|---------|
| DownLoadTMResolveFiles | gDownCfgRevoleFileReq | stream gDatafile |

---

# 附录B：gRPC 调用示例（C#）

```csharp
using Grpc.Net.Client;
using MassPlatform;
using Google.Protobuf.WellKnownTypes;

var channel = GrpcChannel.ForAddress("https://localhost:5001");
var client = new MassDataServer.MassDataServerClient(channel);

// 1. 获取卫星列表
var satList = await client.GetAllSatsFromMassServerAsync(new Empty());

// 2. 获取卫星配置
var sat = new GSatelliteThumb { Taskno = "TASK_A", Dbstage = "DEV", Satno = "SAT_01" };
var cfg = await client.GetSatCfgAsync(sat);

// 3. 查询参数数据（gRPC 流式批次）
var paraReq = new GQueryCondParaReq
{
    Fromdt = Timestamp.FromDateTime(DateTime.UtcNow.AddHours(-1)),
    Todt = Timestamp.FromDateTime(DateTime.UtcNow),
    Limittotal = -1
};
paraReq.Parainfos.Add(new GPkgParaRelate
{
    Taskno = "TASK_A", Satno = "SAT_01", Dbstage = "DEV",
    Pkgid = 1001, Paraid = 2001
});

using var paraCall = client.QuerygPara(paraReq);
await foreach (var batch in paraCall.ResponseStream.ReadAllAsync())
{
    foreach (var para in batch.Datas)
    {
        Console.WriteLine($"[{para.Dt}] ParaId={para.Id} Pv={para.Pv}");
    }
}

// 4. 复合参数 QueryParaAggregate（gRPC 流式批次）
var aggParaReq = new GQueryCondParaAggregateReq
{
    Fromdt = Timestamp.FromDateTime(DateTime.UtcNow.AddHours(-1)),
    Todt = Timestamp.FromDateTime(DateTime.UtcNow),
    Containinst = true,
    BlSplitPage = false,
    Skipnum = 0,
    Limitnum = 1000
};
aggParaReq.Parainfos.Add(new GPkgParaRelate
{
    Taskno = "TASK_A", Satno = "SAT_01", Dbstage = "DEV",
    Pkgid = 1001, Paraid = 2001, Chid = 0, Rtflag = 0
});
using var aggParaCall = client.QueryParaAggregate(aggParaReq);
await foreach (var batch in aggParaCall.ResponseStream.ReadAllAsync())
{
    Console.WriteLine($"batch composites={batch.Datas.Count}");
}

// 5. 聚合统计
var aggReq = new GQueryCondParaAggReq
{
    Taskno = "TASK_A", Satno = "SAT_01", Dbstage = "DEV",
    Fromdt = Timestamp.FromDateTime(DateTime.UtcNow.AddHours(-1)),
    Todt = Timestamp.FromDateTime(DateTime.UtcNow),
    Intervalseconds = 60,
    Aggregationtype = GAggregationType.Average
};
aggReq.Paraids.Add(2001);
var aggReply = await client.AggregateParametersAsync(aggReq);

// 6. 判据规则管理
var ruleDb = new GSatRuleDbTarget { Taskno = "TASK_A", Dbstage = "DEV", Satno = "SAT_01" };
var rules = await client.GetJudgeRulesAsync(ruleDb);
var upsertReq = new GJudgeRuleUpsertReq
{
    Target = ruleDb,
    Entity = new GJsonEntity { Id = "", Data = "{\"code\":\"RULE_NEW\",\"name\":\"新规则\"}" }
};
var upsertReply = await client.UpsertJudgeRuleAsync(upsertReq);
await client.DeleteJudgeRuleAsync(new GJudgeRuleDeleteReq { Target = ruleDb, Id = upsertReply.Id });

// 7. 流式查询指令
var instReq = new GQueryCondInstReq
{
    Fromdt = Timestamp.FromDateTime(DateTime.UtcNow.AddHours(-1)),
    Todt = Timestamp.FromDateTime(DateTime.UtcNow)
};
instReq.Instinfos.Add(new GInstInfoReq { Taskno = "TASK_A", Satno = "SAT_01", Dbstage = "DEV", Instid = 1001 });
using var instCall = client.QuerygInstStream(instReq);
await foreach (var batch in instCall.ResponseStream.ReadAllAsync())
{
    foreach (var inst in batch.Datas)
    {
        Console.WriteLine($"[{inst.Et}] CmdId={inst.Ci} CmdCode={inst.Cc}");
    }
}
```

---

# 附录C：建议调用顺序

1. `GET /api/mass-data/satellites` / `GetAllSatsFromMassServer` → 获取可用卫星列表
2. 选择 `(taskNo, satNo, dbStage)` 三元组后，调用配置接口获取连接信息
3. 按需调用基础库接口（`GET`/`POST` `basic/parameters` 及指令、包、关系、判据表）
4. 进入历史数据查询或统计接口
5. 如需管理判据规则，调用 `judge/rules/*` 或对应 gRPC 方法
6. 如需下载数采文件，调用 `files/resolve` 或 `DownLoadTMResolveFiles`

---

## 说明

- Web API 与 gRPC 能力是对齐的，JSON 字段命名遵循 camelCase，proto 字段命名为 snake_case。
- 已移除 Web API `query/parameter-conformity`（原占位接口）；复合参数请使用 `query/parameters/para-aggregate` 及 `…/stream`，与 gRPC `QueryParaAggregate` 对齐。
- `query/instructions` 与 `query/instructions/stream`（Web API）当前均为 **整包 JSON 信封** 返回；**gRPC** `QuerygInstStream` 为分批发流。若需 **HTTP 顶层 JSON 数组** 边序列化边传输，请使用 `query/parameters/stream`、`query/parameters/para-aggregate/stream`、`query/packages/stream`、`query/frames/stream`。
- 判据规则管理（`judge/rules/*`）在 Web API 与 gRPC 中有同等能力。
- 平台配置管理接口（`/api/config`）独立于卫星数据服务接口。
- 若部署开启 Swagger，可通过 `/swagger` 查看在线调试页面。

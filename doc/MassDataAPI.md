# MassDataServer Web API 与 gRPC 接口使用说明

本文档面向第三方调用方，说明本项目当前可用的 Web API 与 gRPC 接口。

## 1. 基本信息

- Web API 基础路由：`/api/mass-data`
- gRPC Service：`MassPlatform.MassDataServer`
- Proto 文件：
  - `MassServerProtos/MassDataServer.proto`
  - `MassServerProtos/MassModels.proto`

## 2. 通用约定

### 2.1 卫星定位主键

多数接口都需要以下三元组唯一定位目标卫星：

- `taskNo`
- `satNo`
- `dbStage`

Web API 中通常使用 `SatelliteLookupRequest`（或其派生请求）承载上述字段。
gRPC 中通常使用 `gSatelliteThumb`（或各类 QueryReq）承载上述字段。

### 2.2 时间字段

- Web API：`DateTime`（JSON 字符串，建议 ISO-8601 UTC，例如 `2026-03-19T00:00:00Z`）
- gRPC：`google.protobuf.Timestamp`

### 2.3 Web API 错误码约定

- `200`：成功
- `400`：参数错误（`ArgumentException`）
- `404`：卫星不存在（`SatelliteNotFoundException`）
- `408`：请求取消或超时
- `500`：服务内部错误

错误体示例：

```json
{
  "error": "获取卫星配置失败",
  "detail": "..."
}
```

## 3. Web API 接口清单

以下为 `MassDataApiController` 当前公开接口。

### 3.1 卫星与连接信息

- `GET /api/mass-data/satellites`  
  获取可用卫星列表
- `POST /api/mass-data/satellite/config`  
  获取卫星连接配置
- `POST /api/mass-data/satellite/message-queue`  
  获取 MQ 配置
- `POST /api/mass-data/satellite/redis`  
  获取 Redis 配置

### 3.2 基础库

- `POST /api/mass-data/basic/parameters`
- `POST /api/mass-data/basic/commands`
- `POST /api/mass-data/basic/packages`
- `POST /api/mass-data/basic/relations`
- `POST /api/mass-data/basic/cmd-judges`

### 3.3 历史查询与统计

- `POST /api/mass-data/query/parameters`
- `POST /api/mass-data/query/parameter-conformity`（当前返回空集合占位）
- `POST /api/mass-data/query/instructions`
- `POST /api/mass-data/query/packages`
- `POST /api/mass-data/query/frames`
- `POST /api/mass-data/query/parameters/aggregate`
- `POST /api/mass-data/query/parameters/statistics`
- `POST /api/mass-data/query/data-volume`

### 3.4 判读与文件

- `POST /api/mass-data/judge/infos`
- `POST /api/mass-data/judge/results`
- `POST /api/mass-data/files/resolve`

## 4. Web API 关键请求示例

## 4.1 获取卫星配置

### 请求

`POST /api/mass-data/satellite/config`

```json
{
  "taskNo": "TASK_A",
  "taskName": "型号A",
  "dbStage": "DEV",
  "satNo": "SAT_01",
  "satName": "卫星01"
}
```

### 响应（示例）

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

## 4.2 查询参数

### 请求

`POST /api/mass-data/query/parameters`

```json
{
  "taskNo": "TASK_A",
  "taskName": "型号A",
  "dbStage": "DEV",
  "satNo": "SAT_01",
  "satName": "卫星01",
  "fromDt": "2026-03-19T00:00:00Z",
  "toDt": "2026-03-19T01:00:00Z",
  "pkgParaIds": [
    {
      "pid": 1001,
      "id": 2001,
      "rtDelayFlag": 0,
      "dataProvider": 0
    }
  ]
}
```

### 响应（示例）

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

## 4.3 参数聚合统计

### 请求

`POST /api/mass-data/query/parameters/aggregate`

```json
{
  "taskNo": "TASK_A",
  "taskName": "型号A",
  "dbStage": "DEV",
  "satNo": "SAT_01",
  "satName": "卫星01",
  "fromDt": "2026-03-19T00:00:00Z",
  "toDt": "2026-03-19T01:00:00Z",
  "parameterIds": [2001, 2002],
  "intervalSeconds": 60,
  "aggregationType": "Average"
}
```

### 响应（示例）

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

## 5. gRPC 接口清单（MassDataServer 服务）

### 5.1 卫星与配置

- `GetAllSatsFromMassServer(google.protobuf.Empty) returns (gSatCollectionReply)`
- `GetSatCfg(gSatelliteThumb) returns (satconncfg)`
- `GetMessageQueueInfo(gSatelliteThumb) returns (gMsgQueueInfoReply)`
- `GetRedisInfo(gSatelliteThumb) returns (gRedisInfoReply)`

### 5.2 基础库

- `GetBasicDbPara(gSatelliteThumb) returns (gView_Paras)`
- `GetBasicDbCmd(gSatelliteThumb) returns (gView_Cmds)`
- `GetBasicDbPkg(gSatelliteThumb) returns (gView_Pkgs)`
- `GetBasicDbRelation(gSatelliteThumb) returns (gView_Sys_Relates)`
- `GetBasicDbCmdJudges(gSatelliteThumb) returns (gView_Cmdjudges)`

### 5.3 查询与统计

- `QuerygPara(gQueryCondParaReq) returns (stream gParaCollect)`
- `QueryParaConformity(gQueryCondParaReq) returns (stream gparaConformityResult)`
- `QuerygInst(gQueryCondInstReq) returns (gInstCollect)`
- `QuerygPkg(gQueryCondPkgReq) returns (stream gPkgCollect)`
- `QuerygFrame(gQueryCondFrameReq) returns (stream gFrameCollect)`
- `AggregateParameters(gQueryCondParaAggReq) returns (gParaAggregationCollect)`
- `GetParameterStatistics(gQueryCondParaStatisticsReq) returns (gParameterStatisticsReply)`
- `GetDataVolumeStatistics(gQueryCondDataVolumeReq) returns (gDataVolumeStatisticsReply)`

### 5.4 判读与文件

- `GetJudgeInfos(gSatelliteThumb) returns (gJudgeInfoCollect)`
- `QuerygJudgeResults(gQueryCondJudgeReusltReq) returns (stream gJudgeResultCollect)`
- `DownLoadTMResolveFiles(gDownCfgRevoleFileReq) returns (stream gDatafile)`

## 6. gRPC 调用示例（C#）

```csharp
using Grpc.Net.Client;
using MassPlatform;
using Google.Protobuf.WellKnownTypes;

var channel = GrpcChannel.ForAddress("https://localhost:5001");
var client = new MassDataServer.MassDataServerClient(channel);

var sat = new GSatelliteThumb
{
    Taskno = "TASK_A",
    Dbstage = "DEV",
    Satno = "SAT_01"
};

var cfg = await client.GetSatCfgAsync(sat);
Console.WriteLine(cfg.Mongoqueryconn);

var aggReq = new GQueryCondParaAggReq
{
    Taskno = "TASK_A",
    Satno = "SAT_01",
    Dbstage = "DEV",
    Fromdt = Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.UtcNow.AddHours(-1), DateTimeKind.Utc)),
    Todt = Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)),
    Intervalseconds = 60,
    Aggregationtype = GAggregationType.Average
};
aggReq.Paraids.Add(2001);

var aggReply = await client.AggregateParametersAsync(aggReq);
Console.WriteLine(aggReply.Datas.Count);
```

## 7. 建议调用顺序

1. `satellites` / `GetAllSatsFromMassServer` 先拿可用星列表
2. 选择 `(taskNo, satNo, dbStage)` 后，调用配置接口拿连接信息
3. 根据场景调用基础库接口（参数、指令、包、关系）
4. 进入历史查询或统计接口
5. 如需数采文件，调用 `files/resolve` 或 `DownLoadTMResolveFiles`

## 8. 说明

- Web API 与 gRPC 能力是对齐的，字段命名风格不同（JSON 通常 camelCase，proto 通常小写字段名）。
- `query/parameter-conformity` 当前为占位实现，返回空数据集合。
- 若部署开启 Swagger，可通过 `/swagger` 查看在线调试页面。

# MassDataServer 海量数据服务 API 说明（更新版）

本文档基于当前代码实现更新，覆盖：

- Web API `v1` / `v2`
- gRPC `V1` / `V2`（含 `MassDataJudgeServer`）
- `doc/` 下示例与当前路由/字段差异

---

## 1. 基本信息

| 项目 | 说明 |
|---|---|
| Web API v1 基础路由 | `/api/v1/mass-data` |
| Web API v2 基础路由 | `/api/v2/mass-data` |
| 配置管理路由 | `/api/config` |
| gRPC（v1） | `MassPlatform.MassDataServer`、`MassPlatform.DataReceiveServer` |
| gRPC（v2） | `MassPlatform.V2.MassDataServer`、`MassPlatform.V2.MassDataJudgeServer` |
| Proto（v2） | `MassServerProtos/v2/mass_data_server_v2.proto`、`mass_data_server_judge_v2.proto`、`mass_models_v2.proto` |
| 编码格式 | UTF-8，JSON（Web API） |

---

## 2. 版本与主键约定

### 2.1 卫星主键

- `v2`：`taskNo + satNo`（已移除 `dbStage`）
- `v1`：`taskNo + satNo + dbStage`

### 2.2 时间字段

- Web API：`DateTime`（建议 ISO-8601 UTC，如 `2026-07-02T12:00:00Z`）
- gRPC：`google.protobuf.Timestamp`

### 2.3 常见响应包裹

```json
{ "datas": [ ... ] }
```

```json
{ "taskNo": "T", "satNo": "S", "datas": [ ... ] }
```

### 2.4 Web API 错误码

| 状态码 | 说明 |
|---|---|
| 200 | 成功 |
| 400 | 参数错误 |
| 404 | 卫星不存在 |
| 408 | 请求取消/超时 |
| 500 | 服务内部错误 |

---

## 3. Web API v2 详细参数说明

> 路由前缀：`/api/v2/mass-data`。除特别说明外，请求/响应均为 JSON，`Content-Type: application/json`。

> **是否必填项**：`是` 表示业务上必须提供且非空；`否` 表示可选或使用默认值；`—` 表示不适用（如流式顶层结构说明）。

### 3.1 卫星与基础信息（`/api/v2/mass-data`）

#### 获取可用卫星列表

- **方法**: `GET`
- **路径**: `/api/v2/mass-data/satellites`
- **说明**: 返回当前配置中启用的卫星摘要列表

**请求参数**
_无请求体（无 Query 参数）_

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | array(SatelliteThumbDto) | 否 | [] |
| datas[].taskNo | string | 否 | "TASK_A" |
| datas[].taskName | string | 否 | "任务A" |
| datas[].satNo | string | 否 | "SAT_01" |
| datas[].satName | string | 否 | "卫星01" |
| datas[].enabled | bool | 否 | true |


#### 获取卫星连接配置

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/satellite/config`
- **说明**: 返回基础库、判读、MQ 等连接字符串

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| basicConn | string | 否 | "mongodb://..." |
| mongoQueryConn | string | 否 | "" |
| judgeConn | string | 否 | "" |
| judgeMgrConn | string | 否 | "" |
| analysisConn | string | 否 | "" |
| displayConn | string | 否 | "" |
| mqttUrl | string | 否 | "" |
| signalRUrl | string | 否 | "" |
| cfgConn | string | 否 | "" |


#### 获取 MQ 配置

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/satellite/message-queue`
- **说明**: 返回 RabbitMQ 连接与队列定义

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| url | string | 否 | "amqp://..." |
| exchangeType | string | 否 | "topic" |
| exchangeName | string | 否 | "mass.exchange" |
| queues | array(MessageQueueItemDto) | 否 | [] |
| queues[].key | string | 否 | "param" |
| queues[].queueName | string | 否 | "param.queue" |
| queues[].exchangeName | string | 否 | "mass.exchange" |


#### 获取 Redis 配置

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/satellite/redis`
- **说明**: 返回 Redis 连接与键列表

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| url | string | 否 | "127.0.0.1:6379" |
| pwd | string | 否 | "" |
| dbIndex | int | 否 | 0 |
| keys | array(string) | 否 | ["key1"] |


#### 直连 TaskDb 获取卫星列表

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/satellites-from-taskdb`
- **说明**: 不依赖本地 allServerPlats 配置，直连 TaskDb Mongo

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskDbIp | string | 是 | "127.0.0.1" |
| taskDbPort | int | 是 | 27017 |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | array(SatelliteThumbDto) | 否 | [] |
| datas[].taskNo | string | 否 | "TASK_A" |
| datas[].taskName | string | 否 | "任务A" |
| datas[].satNo | string | 否 | "SAT_01" |
| datas[].satName | string | 否 | "卫星01" |
| datas[].enabled | bool | 否 | true |


### 3.2 基础库（`/api/v2/mass-data`）

#### 获取参数定义（GET）

- **方法**: `GET`
- **路径**: `/api/v2/mass-data/basic/parameters`
- **说明**: Query 传参，与 POST 返回相同

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| （Query）taskNo | string | 是 | "TASK_A" |
| （Query）satNo | string | 是 | "SAT_01" |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | array(BasicParameterDto) | 否 | [] |
| datas[].paraId | int | 否 | 100108 |
| datas[].prmSysId | int | 否 | 1 |
| datas[].paraCode | string | 否 | "P001" |
| datas[].paraType | int | 否 | 1 |
| datas[].paraTypeChar | string | 否 | "F" |
| datas[].paraTypeDesc | string | 否 | "浮点" |
| datas[].paraDesc | string | 否 | "温度" |
| datas[].minValue | double | 否 | 0 |
| datas[].maxValue | double | 否 | 100 |
| datas[].updateTime | int | 否 | 0 |
| datas[].valueDesc | string | 否 | "" |
| datas[].validFlag | int | 否 | 1 |
| datas[].watchFlag | int | 否 | 0 |
| datas[].parameterType | int | 否 | 0 |
| datas[].editGroup | string | 否 | "" |
| datas[].procId | int | 否 | 0 |
| datas[].procDesc | string | 否 | "" |
| datas[].paraMask | string | 否 | "" |


#### 获取参数定义（POST）

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/basic/parameters`
- **说明**: 返回基础库参数元数据

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | array(BasicParameterDto) | 否 | [] |
| datas[].paraId | int | 否 | 100108 |
| datas[].prmSysId | int | 否 | 1 |
| datas[].paraCode | string | 否 | "P001" |
| datas[].paraType | int | 否 | 1 |
| datas[].paraTypeChar | string | 否 | "F" |
| datas[].paraTypeDesc | string | 否 | "浮点" |
| datas[].paraDesc | string | 否 | "温度" |
| datas[].minValue | double | 否 | 0 |
| datas[].maxValue | double | 否 | 100 |
| datas[].updateTime | int | 否 | 0 |
| datas[].valueDesc | string | 否 | "" |
| datas[].validFlag | int | 否 | 1 |
| datas[].watchFlag | int | 否 | 0 |
| datas[].parameterType | int | 否 | 0 |
| datas[].editGroup | string | 否 | "" |
| datas[].procId | int | 否 | 0 |
| datas[].procDesc | string | 否 | "" |
| datas[].paraMask | string | 否 | "" |


#### 获取指令定义

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/basic/commands`
- **说明**: 基础库指令元数据

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | array(BasicCommandDto) | 否 | [] |
| datas[].cmdId | int | 否 | 101 |
| datas[].cmdSysId | int | 否 | 1 |
| datas[].cmdCode | string | 否 | "CMD01" |
| datas[].cmdType | int | 否 | 1 |
| datas[].cmdDesc | string | 否 | "指令" |
| datas[].cmdLen | int | 否 | 16 |
| datas[].cmdData | string | 否 | "AA BB" |
| datas[].exeTime | int | 否 | 0 |
| datas[].cmdLevel | int | 否 | 0 |
| datas[].validFlag | int | 否 | 1 |
| datas[].isStarMiddleCmd | bool | 否 | false |
| datas[].singnl | string | 否 | "" |
| datas[].allowCheckData | bool | 否 | true |
| datas[].editGroup | string | 否 | "" |
| datas[].businessGroup | string | 否 | "" |


#### 获取包定义

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/basic/packages`
- **说明**: 基础库包元数据

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | array(BasicPackageDto) | 否 | [] |
| datas[].sysId | int | 否 | 37 |
| datas[].pkgFlag | string | 否 | "0x01" |
| datas[].pkgLen | int | 否 | 128 |
| datas[].subFlag | string | 否 | "" |
| datas[].pkgDesc | string | 否 | "包描述" |
| datas[].updateTime | int | 否 | 0 |
| datas[].validFlag | int | 否 | 1 |
| datas[].pkgFlagAssist | string | 否 | "" |
| datas[].sysCode | string | 否 | "PKG" |


#### 获取层级关系

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/basic/relations`
- **说明**: 包/指令层级树

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| pkgHirberDatas | array(BasicRelationDto) | 否 | [] |
| pkgHirberDatas[].sysId | int | 否 | 1 |
| pkgHirberDatas[].sysCode | string | 否 | "SYS" |
| pkgHirberDatas[].sysDesc | string | 否 | "系统" |
| pkgHirberDatas[].fatherSysId | int | 否 | 0 |
| pkgHirberDatas[].level | int | 否 | 1 |
| pkgHirberDatas[].sysType | int | 否 | 1 |
| cmdHirberDatas | array(BasicRelationDto) | 否 | [] |
| cmdHirberDatas[].sysId | int | 否 | 2 |


#### 获取指令判据

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/basic/cmd-judges`
- **说明**: 指令关联判据定义

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | array(BasicCommandJudgeDto) | 否 | [] |
| datas[].cmdId | int | 否 | 101 |
| datas[].judgeId | int | 否 | 1 |
| datas[].paraId | int | 否 | 100108 |
| datas[].judgeType | int | 否 | 1 |
| datas[].iDnValue | string | 否 | "0" |
| datas[].iUpValue | string | 否 | "100" |
| datas[].rDnValue | string | 否 | "" |
| datas[].rUpValue | string | 否 | "" |
| datas[].vDnValue | string | 否 | "" |
| datas[].vUpValue | string | 否 | "" |
| datas[].judgeTime | int | 否 | 0 |


### 3.3 单星历史查询与统计（`/api/v2/mass-data`）

#### 参数查询

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/query/parameters`
- **说明**: 缓冲返回全部参数点

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |
| fromDt | DateTime? | 否 | "2026-07-02T00:00:00Z" |
| toDt | DateTime? | 否 | "2026-07-02T01:00:00Z" |
| pkgParaIds | array(PackageParameterConditionDto) | 否 | [] |
| pkgParaIds[].pid | int | 是 | 37 |
| pkgParaIds[].id | array(int) | 是 | [100108, 100109] |
| pkgParaIds[].rtDelayFlag | int | 否 | 0 |
| pkgParaIds[].dataProvider | int | 否 | 0 |
| limitTotal | int | 否 | -1 |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | array(ParameterDto) | 否 | [] |
| datas[].id | int | 否 | 100108 |
| datas[].pid | int | 否 | 37 |
| datas[].pv | string | 否 | "25.6" |
| datas[].sv | string | 否 | "..." |
| datas[].pd | string | 否 | "" |
| datas[].dt | DateTime | 否 | "2026-07-02T00:01:00Z" |
| datas[].st | DateTime | 否 | "2026-07-02T00:01:01Z" |
| datas[].dtTicks | long | 否 | 638000000000000000 |
| datas[].pc | string | 否 | "P001" |
| datas[].satNo | string | 否 | "SAT_01" |
| datas[].taskNo | string | 否 | "TASK_A" |


#### 参数查询（流式）

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/query/parameters/stream`
- **说明**: 顶层 JSON 数组流式输出

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |
| fromDt | DateTime? | 否 | "2026-07-02T00:00:00Z" |
| toDt | DateTime? | 否 | "2026-07-02T01:00:00Z" |
| pkgParaIds | array(PackageParameterConditionDto) | 否 | [] |
| pkgParaIds[].pid | int | 是 | 37 |
| pkgParaIds[].id | array(int) | 是 | [100108, 100109] |
| pkgParaIds[].rtDelayFlag | int | 否 | 0 |
| pkgParaIds[].dataProvider | int | 否 | 0 |
| limitTotal | int | 否 | -1 |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| （流式） | array(ParameterDto) | — | 顶层 JSON 数组，元素字段同 ParameterDto |


#### 复合参数查询

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/query/parameters/para-aggregate`
- **说明**: 指令+参数按时间点合成

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |
| fromDt | DateTime? | 否 | "2026-07-02T00:00:00Z" |
| toDt | DateTime? | 否 | "2026-07-02T01:00:00Z" |
| pkgParaIds | array(PackageParameterConditionDto) | 否 | [] |
| pkgParaIds[].pid | int | 是 | 37 |
| pkgParaIds[].id | array(int) | 是 | [100108, 100109] |
| pkgParaIds[].rtDelayFlag | int | 否 | 0 |
| pkgParaIds[].dataProvider | int | 否 | 0 |
| containInst | bool | 否 | true |
| splitPage | bool | 否 | false |
| skipNum | int | 否 | 0 |
| limitNum | int | 否 | 0 |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | array(CompositeParameterPointDto) | 否 | [] |
| datas[].taskNo | string | 否 | "TASK_A" |
| datas[].satNo | string | 否 | "SAT_01" |
| datas[].dbStage | string | 否 | "" |
| datas[].time | DateTime | 否 | "2026-07-02T00:01:00Z" |
| datas[].instDatas | array(CompositeInstDto) | 否 | [] |
| datas[].instDatas[].ci | int | 否 | 101 |
| datas[].instDatas[].cc | string | 否 | "CMD01" |
| datas[].instDatas[].cd | string | 否 | "AA BB" |
| datas[].instDatas[].cn | string | 否 | "指令名" |
| datas[].paraDatas | array(CompositeParaDto) | 否 | [] |
| datas[].paraDatas[].id | int | 否 | 100108 |
| datas[].paraDatas[].pv | double | 否 | 25.6 |
| datas[].paraDatas[].pd | string | 否 | "" |
| datas[].paraDatas[].sv | string | 否 | "" |


#### 复合参数查询（流式）

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/query/parameters/para-aggregate/stream`
- **说明**: 顶层 JSON 数组流式输出 CompositeParameterPointDto

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |
| fromDt | DateTime? | 否 | "2026-07-02T00:00:00Z" |
| toDt | DateTime? | 否 | "2026-07-02T01:00:00Z" |
| pkgParaIds | array(PackageParameterConditionDto) | 否 | [] |
| pkgParaIds[].pid | int | 是 | 37 |
| pkgParaIds[].id | array(int) | 是 | [100108, 100109] |
| pkgParaIds[].rtDelayFlag | int | 否 | 0 |
| pkgParaIds[].dataProvider | int | 否 | 0 |
| containInst | bool | 否 | true |
| splitPage | bool | 否 | false |
| skipNum | int | 否 | 0 |
| limitNum | int | 否 | 0 |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| （流式） | array(CompositeParameterPointDto) | — | 字段同复合参数查询 datas[] |


#### 指令查询

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/query/instructions`
- **说明**: 信封结构返回

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |
| fromDt | DateTime? | 否 | "2026-07-02T00:00:00Z" |
| toDt | DateTime? | 否 | "2026-07-02T01:00:00Z" |
| instIds | array([int,int]) | 否 | [[101, 32]] |
| instIds[] | Tuple&lt;cmdId,chId&gt; | 否 | [101, 32] |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 否 | "TASK_A" |
| satNo | string | 否 | "SAT_01" |
| datas | array(InstructionDto) | 否 | [] |
| datas[].ci | int | 否 | 101 |
| datas[].cc | string | 否 | "CMD01" |
| datas[].cd | string | 否 | "AA BB CC" |
| datas[].cn | string | 否 | "指令名" |
| datas[].et | DateTime | 否 | "2026-07-02T00:01:00Z" |
| datas[].cJudgeInfo | string | 否 | "" |
| datas[].relativeParas | array(ParameterDto) | 否 | [] |


#### 指令查询（stream 路由）

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/query/instructions/stream`
- **说明**: 当前实现为缓冲信封，非顶层数组流

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |
| fromDt | DateTime? | 否 | "2026-07-02T00:00:00Z" |
| toDt | DateTime? | 否 | "2026-07-02T01:00:00Z" |
| instIds | array([int,int]) | 否 | [[101, 32]] |
| instIds[] | Tuple&lt;cmdId,chId&gt; | 否 | [101, 32] |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 否 | "TASK_A" |
| satNo | string | 否 | "SAT_01" |
| datas | array(InstructionDto) | 否 | [] |
| datas[].ci | int | 否 | 101 |
| datas[].cc | string | 否 | "CMD01" |
| datas[].cd | string | 否 | "AA BB CC" |
| datas[].cn | string | 否 | "指令名" |
| datas[].et | DateTime | 否 | "2026-07-02T00:01:00Z" |
| datas[].cJudgeInfo | string | 否 | "" |
| datas[].relativeParas | array(ParameterDto) | 否 | [] |


#### 包查询

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/query/packages`
- **说明**: 信封结构返回

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |
| fromDt | DateTime? | 否 | "2026-07-02T00:00:00Z" |
| toDt | DateTime? | 否 | "2026-07-02T01:00:00Z" |
| pkgIds | array([int,int,int]) | 否 | [[37, 0, 0]] |
| pkgIds[] | Tuple&lt;pkgId,chId,rtFlag&gt; | 否 | [37, 0, 0] |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 否 | "TASK_A" |
| satNo | string | 否 | "SAT_01" |
| datas | array(PackageDto) | 否 | [] |
| datas[].pi | int | 否 | 37 |
| datas[].pc | string | 否 | "PKG01" |
| datas[].pd | string | 否 | "AA BB" |
| datas[].pt | DateTime | 否 | "2026-07-02T00:01:00Z" |


#### 包查询（流式）

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/query/packages/stream`
- **说明**: 顶层 JSON 数组流式输出 PackageDto

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |
| fromDt | DateTime? | 否 | "2026-07-02T00:00:00Z" |
| toDt | DateTime? | 否 | "2026-07-02T01:00:00Z" |
| pkgIds | array([int,int,int]) | 否 | [[37, 0, 0]] |
| pkgIds[] | Tuple&lt;pkgId,chId,rtFlag&gt; | 否 | [37, 0, 0] |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| （流式） | array(PackageDto) | — | 字段同 datas[] |


#### 帧查询

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/query/frames`
- **说明**: 信封结构返回

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |
| fromDt | DateTime? | 否 | "2026-07-02T00:00:00Z" |
| toDt | DateTime? | 否 | "2026-07-02T01:00:00Z" |
| chId | int | 否 | 0 |
| rtFlag | int | 否 | 0 |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 否 | "TASK_A" |
| satNo | string | 否 | "SAT_01" |
| datas | array(FrameDto) | 否 | [] |
| datas[].ft | DateTime | 否 | "2026-07-02T00:01:00Z" |
| datas[].fno | int | 否 | 1 |
| datas[].fd | string | 否 | "AA BB" |


#### 帧查询（流式）

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/query/frames/stream`
- **说明**: 顶层 JSON 数组流式输出 FrameDto

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |
| fromDt | DateTime? | 否 | "2026-07-02T00:00:00Z" |
| toDt | DateTime? | 否 | "2026-07-02T01:00:00Z" |
| chId | int | 否 | 0 |
| rtFlag | int | 否 | 0 |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| （流式） | array(FrameDto) | — | 字段同 datas[] |


#### 参数聚合统计

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/query/parameters/aggregate`
- **说明**: 按时间桶聚合

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |
| fromDt | DateTime? | 否 | "2026-07-02T00:00:00Z" |
| toDt | DateTime? | 否 | "2026-07-02T01:00:00Z" |
| parameterIds | array(int) | 否 | [100108] |
| intervalSeconds | int | 否 | 60 |
| aggregationType | string | 否 | "Average" |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | array(ParameterAggregationDto) | 否 | [] |
| datas[].timeBucket | DateTime | 否 | "2026-07-02T00:00:00Z" |
| datas[].parameterId | int | 否 | 100108 |
| datas[].parameterCode | string | 否 | "P001" |
| datas[].value | double | 否 | 25.6 |
| datas[].count | long | 否 | 10 |


#### 单参数统计

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/query/parameters/statistics`
- **说明**: 最大/最小/均值等

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |
| fromDt | DateTime? | 否 | "2026-07-02T00:00:00Z" |
| toDt | DateTime? | 否 | "2026-07-02T01:00:00Z" |
| parameterId | int | 是 | 100108 |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| parameterId | int | 否 | 100108 |
| parameterCode | string | 否 | "P001" |
| totalCount | long | 否 | 1000 |
| firstTimestamp | DateTime | 否 | "2026-07-02T00:00:00Z" |
| lastTimestamp | DateTime | 否 | "2026-07-02T01:00:00Z" |
| minValue | double | 否 | 20.1 |
| maxValue | double | 否 | 30.5 |
| averageValue | double | 否 | 25.3 |
| stdDevValue | double | 否 | 1.2 |


#### 数据量统计

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/query/data-volume`
- **说明**: 各类型数据量与存储大小

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |
| fromDt | DateTime? | 否 | "2026-07-02T00:00:00Z" |
| toDt | DateTime? | 否 | "2026-07-02T01:00:00Z" |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| parameterCount | long | 否 | 10000 |
| instructionCount | long | 否 | 100 |
| packageCount | long | 否 | 500 |
| frameCount | long | 否 | 200 |
| judgeResultCount | long | 否 | 50 |
| totalCount | long | 否 | 10850 |
| totalSizeBytes | long | 否 | 1048576 |
| oldestTimestamp | DateTime | 否 | "2026-07-01T00:00:00Z" |
| newestTimestamp | DateTime | 否 | "2026-07-02T01:00:00Z" |


### 3.4 判读结果与 JSON 规则（`/api/v2/mass-data`）

#### 判读信息列表

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/judge/infos`
- **说明**: 已发布判读规则元信息

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | array(JudgeInfoDto) | 否 | [] |
| datas[].code | string | 否 | "JR001" |
| datas[].desc | string | 否 | "规则描述" |
| datas[].group | string | 否 | "G1" |
| datas[].ruleType | string | 否 | "TypeA" |
| datas[].resultValueType | string | 否 | "int" |
| datas[].resultValueDesc | string | 否 | "" |
| datas[].lastTime | DateTime | 否 | "2026-07-02T00:00:00Z" |
| datas[].data | object | 否 | {} |
| datas[].tags | array(string) | 否 | ["tag1"] |
| datas[].id | string | 否 | "..." |
| datas[].isEnable | bool | 否 | true |
| datas[].isResultDisplay | bool | 否 | true |
| datas[].isResultInDb | bool | 否 | true |


#### 判读结果查询

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/judge/results`
- **说明**: 历史判读结果

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |
| fromDt | DateTime? | 否 | "2026-07-02T00:00:00Z" |
| toDt | DateTime? | 否 | "2026-07-02T01:00:00Z" |
| judgeCodes | array(string) | 否 | ["JR001"] |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | array(JudgeResultDto) | 否 | [] |
| datas[].judgeRuleCode | string | 否 | "JR001" |
| datas[].judgeValue | string | 否 | "1" |
| datas[].judgeValueDesc | string | 否 | "正常" |
| datas[].createTime | DateTime | 否 | "2026-07-02T00:01:00Z" |


#### JSON 规则列表

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/judge/rules`
- **说明**: Mongo JSON 规则文档列表

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | array(JudgeRuleEntityDto) | 否 | [] |
| datas[].id | string | 否 | "..." |
| datas[].data | string | 否 | "{}" |


#### 按 Code 查 JSON 规则

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/judge/rules/by-code`
- **说明**: 单条 JSON 规则

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |
| code | string | 是 | "JR001" |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 否 | "..." |
| data | string | 否 | "{}" |


#### 新增/更新 JSON 规则

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/judge/rules/upsert`
- **说明**: id 为空则新增

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |
| id | string | 否 | "..." |
| data | string | 是 | "{}" |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 否 | "新文档Id" |


#### 删除 JSON 规则

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/judge/rules/delete`
- **说明**: 按 id 删除

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |
| id | string | 是 | "..." |

**响应参数**
_无响应体（HTTP 200，空 body）_


### 3.5 数采文件（`/api/v2/mass-data`）

#### 下载解析配置文件

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/files/resolve`
- **说明**: 按绝对目录下载解析配置

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |
| absoluteDir | string | 是 | "test\\" |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| dirPath | string | 否 | "C:\cfg" |
| fileType | string | 否 | "ini" |
| fileName | string | 否 | "config.ini" |
| datas | byte[] | 否 | null |


#### 下载 Rits 配置文件

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/files/rits-config`
- **说明**: 多星 INI 合并为 RitsConfig.ini

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNos | array(string) | 是 | ["SAT_01","SAT_02"] |
| absoluteDir | string | 否 | "test\\" |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| fileName | string | 否 | "RitsConfig.ini" |
| fileType | string | 否 | "byte" |
| content | string | 否 | "[Section]\n..." |


### 3.6 多星历史查询（`/api/v2/mass-data`）

> 缓冲接口返回 `List&lt;CollectDto&gt;`；流式接口顶层为 JSON 数组，元素结构相同。

#### 多星参数查询

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/query/multi-sat/parameters`
- **说明**: 按星分组缓冲返回

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| fromDt | DateTime? | 否 | "2026-07-02T00:00:00Z" |
| toDt | DateTime? | 否 | "2026-07-02T01:00:00Z" |
| limitTotal | int | 否 | -1 |
| items | array(MultiSatParameterItem) | 否 | [] |
| items[].taskNo | string | 是 | "TASK_A" |
| items[].satNo | string | 是 | "SAT_01" |
| items[].pkgId | int | 是 | 37 |
| items[].paraId | int | 是 | 100108 |
| items[].rtFlag | int | 否 | 0 |
| items[].chId | int | 否 | 0 |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| （数组元素） | MultiSatParameterCollectDto | — |  |
| taskNo | string | 否 | "TASK_A" |
| satNo | string | 否 | "SAT_01" |
| datas | array(ParameterDto) | 否 | [] |


#### 多星参数查询（流式）

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/query/multi-sat/parameters/stream`
- **说明**: 按星分组流式输出

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| fromDt | DateTime? | 否 | "2026-07-02T00:00:00Z" |
| toDt | DateTime? | 否 | "2026-07-02T01:00:00Z" |
| limitTotal | int | 否 | -1 |
| items | array(MultiSatParameterItem) | 否 | [] |
| items[].taskNo | string | 是 | "TASK_A" |
| items[].satNo | string | 是 | "SAT_01" |
| items[].pkgId | int | 是 | 37 |
| items[].paraId | int | 是 | 100108 |
| items[].rtFlag | int | 否 | 0 |
| items[].chId | int | 否 | 0 |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| （数组元素） | MultiSatParameterCollectDto | — |  |
| taskNo | string | 否 | "TASK_A" |
| satNo | string | 否 | "SAT_01" |
| datas | array(ParameterDto) | 否 | [] |


#### 多星复合参数查询

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/query/multi-sat/parameters/aggregate`
- **说明**: 按星分组复合参数

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| fromDt | DateTime? | 否 | "2026-07-02T00:00:00Z" |
| toDt | DateTime? | 否 | "2026-07-02T01:00:00Z" |
| containInst | bool | 否 | true |
| blSplitPage | bool | 否 | false |
| skipNum | int | 否 | 0 |
| limitNum | int | 否 | 0 |
| items | array(MultiSatParaAggregateItem) | 否 | [] |
| items[].taskNo | string | 是 | "TASK_A" |
| items[].satNo | string | 是 | "SAT_01" |
| items[].pkgId | int | 是 | 37 |
| items[].paraId | int | 是 | 100108 |
| items[].rtFlag | int | 否 | 0 |
| items[].chId | int | 否 | 0 |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 否 | "TASK_A" |
| satNo | string | 否 | "SAT_01" |
| datas | array(CompositeParameterPointDto) | 否 | [] |


#### 多星复合参数（流式）

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/query/multi-sat/parameters/aggregate/stream`
- **说明**: 流式按星输出

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| fromDt | DateTime? | 否 | "2026-07-02T00:00:00Z" |
| toDt | DateTime? | 否 | "2026-07-02T01:00:00Z" |
| containInst | bool | 否 | true |
| blSplitPage | bool | 否 | false |
| skipNum | int | 否 | 0 |
| limitNum | int | 否 | 0 |
| items | array(MultiSatParaAggregateItem) | 否 | [] |
| items[].taskNo | string | 是 | "TASK_A" |
| items[].satNo | string | 是 | "SAT_01" |
| items[].pkgId | int | 是 | 37 |
| items[].paraId | int | 是 | 100108 |
| items[].rtFlag | int | 否 | 0 |
| items[].chId | int | 否 | 0 |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| （流式元素） | MultiSatParaAggregateCollectDto | — | 含 taskNo/satNo/datas |


#### 多星指令查询

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/query/multi-sat/instructions`
- **说明**: 按星分组指令

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| fromDt | DateTime? | 否 | "2026-07-02T00:00:00Z" |
| toDt | DateTime? | 否 | "2026-07-02T01:00:00Z" |
| items | array(MultiSatInstructionItem) | 否 | [] |
| items[].taskNo | string | 是 | "TASK_A" |
| items[].satNo | string | 是 | "SAT_01" |
| items[].instId | int | 是 | 101 |
| items[].chId | int | 否 | 32 |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 否 | "TASK_A" |
| satNo | string | 否 | "SAT_01" |
| datas | array(InstructionDto) | 否 | [] |


#### 多星指令（流式）

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/query/multi-sat/instructions/stream`
- **说明**: 流式

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| fromDt | DateTime? | 否 | "2026-07-02T00:00:00Z" |
| toDt | DateTime? | 否 | "2026-07-02T01:00:00Z" |
| items | array(MultiSatInstructionItem) | 否 | [] |
| items[].taskNo | string | 是 | "TASK_A" |
| items[].satNo | string | 是 | "SAT_01" |
| items[].instId | int | 是 | 101 |
| items[].chId | int | 否 | 32 |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| （流式元素） | MultiSatInstructionCollectDto | — |  |


#### 多星包查询

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/query/multi-sat/packages`
- **说明**: 按星分组包数据

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| fromDt | DateTime? | 否 | "2026-07-02T00:00:00Z" |
| toDt | DateTime? | 否 | "2026-07-02T01:00:00Z" |
| items | array(MultiSatPackageItem) | 否 | [] |
| items[].taskNo | string | 是 | "TASK_A" |
| items[].satNo | string | 是 | "SAT_01" |
| items[].pkgId | int | 是 | 37 |
| items[].chId | int | 否 | 0 |
| items[].rtFlag | int | 否 | 0 |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 否 | "TASK_A" |
| satNo | string | 否 | "SAT_01" |
| datas | array(PackageDto) | 否 | [] |


#### 多星包（流式）

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/query/multi-sat/packages/stream`
- **说明**: 流式

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| fromDt | DateTime? | 否 | "2026-07-02T00:00:00Z" |
| toDt | DateTime? | 否 | "2026-07-02T01:00:00Z" |
| items | array(MultiSatPackageItem) | 否 | [] |
| items[].taskNo | string | 是 | "TASK_A" |
| items[].satNo | string | 是 | "SAT_01" |
| items[].pkgId | int | 是 | 37 |
| items[].chId | int | 否 | 0 |
| items[].rtFlag | int | 否 | 0 |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| （流式元素） | MultiSatPackageCollectDto | — |  |


#### 多星帧查询

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/query/multi-sat/frames`
- **说明**: 按星分组帧数据

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| fromDt | DateTime? | 否 | "2026-07-02T00:00:00Z" |
| toDt | DateTime? | 否 | "2026-07-02T01:00:00Z" |
| items | array(MultiSatFrameItem) | 否 | [] |
| items[].taskNo | string | 是 | "TASK_A" |
| items[].satNo | string | 是 | "SAT_01" |
| items[].chId | int | 否 | 0 |
| items[].rtFlag | int | 否 | 0 |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 否 | "TASK_A" |
| satNo | string | 否 | "SAT_01" |
| datas | array(FrameDto) | 否 | [] |


#### 多星帧（流式）

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/query/multi-sat/frames/stream`
- **说明**: 流式

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| fromDt | DateTime? | 否 | "2026-07-02T00:00:00Z" |
| toDt | DateTime? | 否 | "2026-07-02T01:00:00Z" |
| items | array(MultiSatFrameItem) | 否 | [] |
| items[].taskNo | string | 是 | "TASK_A" |
| items[].satNo | string | 是 | "SAT_01" |
| items[].chId | int | 否 | 0 |
| items[].rtFlag | int | 否 | 0 |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| （流式元素） | MultiSatFrameCollectDto | — |  |


### 3.7 判读管理（`/api/v2/mass-data`）

#### 获取判读卫星配置列表

- **方法**: `GET`
- **路径**: `/api/v2/mass-data/judge/mgr/sat-configs`
- **说明**: 全部卫星判读库连接配置

**请求参数**
_无请求体（无 Query 参数）_

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | array(JudgeMgrSatConfigDto) | 否 | [] |
| datas[].id | string | 否 | "..." |
| datas[].ver | string | 否 | "1" |
| datas[].taskName | string | 否 | "任务A" |
| datas[].satNo | string | 否 | "SAT_01" |
| datas[].satName | string | 否 | "卫星01" |
| datas[].stage | string | 否 | "在轨" |
| datas[].bdbIp | string | 否 | "127.0.0.1" |
| datas[].bdbPort | int | 否 | 27017 |
| datas[].dbName | string | 否 | "basicdb" |
| datas[].bdbUserName | string | 否 | "user" |
| datas[].bdbUserPsw | string | 否 | "***" |
| datas[].judgeDbIp | string | 否 | "127.0.0.1" |
| datas[].judgeDbPort | int | 否 | 27017 |
| datas[].judgeDbUserName | string | 否 | "user" |
| datas[].judgeDbPsw | string | 否 | "***" |
| datas[].judgeDbName | string | 否 | "judgedb" |
| datas[].judgeSatGroupId | string | 否 | "..." |


#### 新增/更新判读卫星配置

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/judge/mgr/sat-configs/upsert`
- **说明**: Upsert

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| entity | JudgeMgrSatConfigDto | 是 | {} |
| entity.id | string | 否 | "..." |
| entity.ver | string | 否 | "1" |
| entity.taskName | string | 否 | "任务A" |
| entity.satNo | string | 否 | "SAT_01" |
| entity.satName | string | 否 | "卫星01" |
| entity.stage | string | 否 | "在轨" |
| entity.bdbIp | string | 否 | "127.0.0.1" |
| entity.bdbPort | int | 否 | 27017 |
| entity.dbName | string | 否 | "basicdb" |
| entity.judgeDbIp | string | 否 | "127.0.0.1" |
| entity.judgeSatGroupId | string | 否 | "..." |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 否 | "507f1f77bcf86cd799439011" |


#### 删除判读卫星配置

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/judge/mgr/sat-configs/delete`
- **说明**: 按 id 删除

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 是 | "507f1f77bcf86cd799439011" |

**响应参数**
_无响应体（HTTP 200，空 body）_


#### 获取判读卫星分组列表

- **方法**: `GET`
- **路径**: `/api/v2/mass-data/judge/sat-groups`
- **说明**: 全部分组

**请求参数**
_无请求体（无 Query 参数）_

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | array(JudgeSatGroupDto) | 否 | [] |
| datas[].id | string | 否 | "..." |
| datas[].name | string | 否 | "分组1" |


#### 分页查询卫星分组

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/judge/sat-groups/query`
- **说明**: 关键字+分页

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| keyword | string | 否 | "" |
| pageIndex | int | 否 | 0 |
| pageSize | int | 否 | 20 |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| totalCount | int | 否 | 100 |
| datas | array(JudgeSatGroupDto) | 否 | [] |
| datas[].id | string | 否 | "..." |
| datas[].name | string | 否 | "分组1" |


#### 新增/更新卫星分组

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/judge/sat-groups/upsert`
- **说明**: Upsert

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| entity | JudgeSatGroupDto | 是 | {} |
| entity.id | string | 否 | "..." |
| entity.name | string | 否 | "分组1" |
| entity.satMembersStr | string | 否 | "SAT_01,SAT_02" |
| entity.satTemplatesStr | string | 否 | "" |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 否 | "507f1f77bcf86cd799439011" |


#### 删除卫星分组

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/judge/sat-groups/delete`
- **说明**: 按 id 删除

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 是 | "507f1f77bcf86cd799439011" |

**响应参数**
_无响应体（HTTP 200，空 body）_


#### 获取规则分组列表

- **方法**: `GET`
- **路径**: `/api/v2/mass-data/judge/rule-groups`
- **说明**: 全部规则分组

**请求参数**
_无请求体（无 Query 参数）_

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | array(JudgeRuleGroupDto) | 否 | [] |
| datas[].id | string | 否 | "..." |
| datas[].name | string | 否 | "G1" |
| datas[].nameCh | string | 否 | "分组一" |


#### 新增/更新规则分组

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/judge/rule-groups/upsert`
- **说明**: Upsert

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| entity | JudgeRuleGroupDto | 是 | {} |
| entity.id | string | 否 | "..." |
| entity.name | string | 否 | "G1" |
| entity.nameCh | string | 否 | "分组一" |
| entity.desc | string | 否 | "" |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 否 | "507f1f77bcf86cd799439011" |


#### 删除规则分组

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/judge/rule-groups/delete`
- **说明**: 按 id 删除

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 是 | "507f1f77bcf86cd799439011" |

**响应参数**
_无响应体（HTTP 200，空 body）_


#### 获取卫星模板列表

- **方法**: `GET`
- **路径**: `/api/v2/mass-data/judge/sat-templates`
- **说明**: 全部模板

**请求参数**
_无请求体（无 Query 参数）_

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| totalCount | int | 否 | 50 |
| datas | array(JudgeSatTemplateDto) | 否 | [] |
| datas[].id | string | 否 | "..." |
| datas[].name | string | 否 | "模板1" |


#### 分页查询卫星模板

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/judge/sat-templates/query`
- **说明**: 关键字+分页

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| keyword | string | 否 | "" |
| pageIndex | int | 否 | 0 |
| pageSize | int | 否 | 20 |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| totalCount | int | 否 | 50 |
| datas | array(JudgeSatTemplateDto) | 否 | [] |
| datas[].id | string | 否 | "..." |
| datas[].name | string | 否 | "模板1" |


#### 新增/更新卫星模板

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/judge/sat-templates/upsert`
- **说明**: 含规则模型列表

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| entity | JudgeSatTemplateDto | 是 | {} |
| entity.id | string | 否 | "..." |
| entity.name | string | 否 | "模板1" |
| entity.sourceVer | string | 否 | "1" |
| entity.judgeSatGroupId | string | 否 | "..." |
| entity.judgeRuleModels | array(JudgeRuleTemplateDto) | 否 | [] |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 否 | "507f1f77bcf86cd799439011" |


#### 删除卫星模板

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/judge/sat-templates/delete`
- **说明**: 按 id 删除

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 是 | "507f1f77bcf86cd799439011" |

**响应参数**
_无响应体（HTTP 200，空 body）_


### 3.8 判读规则编辑与标签（`/api/v2/mass-data`）

> `collectionType`：`0`=Published（已发布），`1`=Edit（编辑库）。

#### 分页查询规则

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/judge/rules4edit/query`
- **说明**: rules4edit 分页

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | SatelliteLookupRequest | 是 | {"taskNo":"TASK_A","satNo":"SAT_01"} |
| target.taskNo | string | 是 | "TASK_A" |
| target.satNo | string | 是 | "SAT_01" |
| collectionType | int (JudgeRuleCollectionType) | 否 | 1 |
| ruleTypes | array(string) | 否 | [] |
| ruleGroup | string | 否 | "" |
| keyword | string | 否 | "" |
| paramText | string | 否 | "" |
| pageIndex | int | 否 | 0 |
| pageSize | int | 否 | 500 |
| sortField | string | 否 | "Code" |
| sortAsc | bool | 否 | true |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| totalCount | int | 否 | 100 |
| datas[].id | string | 否 | "..." |
| datas[].code | string | 否 | "JR001" |
| datas[].desc | string | 否 | "规则" |
| datas[].group | string | 否 | "G1" |
| datas[].ruleType | string | 否 | "TypeA" |
| datas[].resultValueType | string | 否 | "int" |
| datas[].resultValueDesc | string | 否 | "" |
| datas[].lastTime | DateTime | 否 | "2026-07-02T00:00:00Z" |
| datas[].data | object | 否 | {} |
| datas[].isResultInDb | bool | 否 | true |
| datas[].isResultDisplay | bool | 否 | true |
| datas[].tag | array(string) | 否 | ["t1"] |
| datas[].isEnable | bool | 否 | true |
| datas[].applyByModelCode | string | 否 | "" |
| datas[].applyByModelJudgeRuleCode | string | 否 | "" |
| datas[].isSameAsModel | bool | 否 | false |


#### 统计规则数量

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/judge/rules4edit/count`
- **说明**: 与 query 相同筛选条件

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | SatelliteLookupRequest | 是 | {"taskNo":"TASK_A","satNo":"SAT_01"} |
| target.taskNo | string | 是 | "TASK_A" |
| target.satNo | string | 是 | "SAT_01" |
| collectionType | int (JudgeRuleCollectionType) | 否 | 1 |
| ruleTypes | array(string) | 否 | [] |
| ruleGroup | string | 否 | "" |
| keyword | string | 否 | "" |
| paramText | string | 否 | "" |
| pageIndex | int | 否 | 0 |
| pageSize | int | 否 | 500 |
| sortField | string | 否 | "Code" |
| sortAsc | bool | 否 | true |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| totalCount | int | 否 | 100 |


#### 按 Code 获取规则

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/judge/rules4edit/by-code`
- **说明**: 单条编辑库规则

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | SatelliteLookupRequest | 是 | {"taskNo":"TASK_A","satNo":"SAT_01"} |
| target.taskNo | string | 是 | "TASK_A" |
| target.satNo | string | 是 | "SAT_01" |
| code | string | 是 | "JR001" |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 否 | "..." |
| code | string | 否 | "JR001" |
| datas[].code | string | 否 | "JR001" |
| datas[].desc | string | 否 | "规则" |
| datas[].group | string | 否 | "G1" |
| datas[].ruleType | string | 否 | "TypeA" |
| datas[].resultValueType | string | 否 | "int" |
| datas[].resultValueDesc | string | 否 | "" |
| datas[].lastTime | DateTime | 否 | "2026-07-02T00:00:00Z" |
| datas[].data | object | 否 | {} |
| datas[].isResultInDb | bool | 否 | true |
| datas[].isResultDisplay | bool | 否 | true |
| datas[].tag | array(string) | 否 | ["t1"] |
| datas[].isEnable | bool | 否 | true |
| datas[].applyByModelCode | string | 否 | "" |
| datas[].applyByModelJudgeRuleCode | string | 否 | "" |
| datas[].isSameAsModel | bool | 否 | false |


#### 新增/更新单条规则

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/judge/rules4edit/upsert`
- **说明**: entities 取首条

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | SatelliteLookupRequest | 是 | {"taskNo":"TASK_A","satNo":"SAT_01"} |
| target.taskNo | string | 是 | "TASK_A" |
| target.satNo | string | 是 | "SAT_01" |
| collectionType | int | 否 | 1 |
| entities | array(JudgeRule4EditDto) | 是 | [{...}] |
| entities[].code | string | 是 | "JR001" |
| entities[].data | object | 否 | {} |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 否 | "..." |


#### 批量新增/更新规则

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/judge/rules4edit/batch-upsert`
- **说明**: 批量 Upsert

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | SatelliteLookupRequest | 是 | {"taskNo":"TASK_A","satNo":"SAT_01"} |
| target.taskNo | string | 是 | "TASK_A" |
| target.satNo | string | 是 | "SAT_01" |
| collectionType | int | 否 | 1 |
| entities | array(JudgeRule4EditDto) | 是 | [{...}] |
| entities[].code | string | 是 | "JR001" |
| entities[].data | object | 否 | {} |

**响应参数**
_无响应体（HTTP 200，空 body）_


#### 删除规则

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/judge/rules4edit/delete`
- **说明**: ids 或 codes 二选一

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | SatelliteLookupRequest | 是 | {"taskNo":"TASK_A","satNo":"SAT_01"} |
| target.taskNo | string | 是 | "TASK_A" |
| target.satNo | string | 是 | "SAT_01" |
| collectionType | int | 否 | 1 |
| ids | array(string) | 否 | ["..."] |
| codes | array(string) | 否 | ["JR001"] |

**响应参数**
_无响应体（HTTP 200，空 body）_


#### 批量删除规则

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/judge/rules4edit/batch-delete`
- **说明**: 同 delete

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | SatelliteLookupRequest | 是 | {"taskNo":"TASK_A","satNo":"SAT_01"} |
| target.taskNo | string | 是 | "TASK_A" |
| target.satNo | string | 是 | "SAT_01" |
| collectionType | int | 否 | 1 |
| ids | array(string) | 否 | ["..."] |
| codes | array(string) | 否 | ["JR001"] |

**响应参数**
_无响应体（HTTP 200，空 body）_


#### 清空规则

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/judge/rules4edit/delete-all`
- **说明**: 清空目标集合

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | SatelliteLookupRequest | 是 | {"taskNo":"TASK_A","satNo":"SAT_01"} |
| target.taskNo | string | 是 | "TASK_A" |
| target.satNo | string | 是 | "SAT_01" |
| collectionType | int | 否 | 1 |
| ids | array(string) | 否 | ["..."] |
| codes | array(string) | 否 | ["JR001"] |

**响应参数**
_无响应体（HTTP 200，空 body）_


#### 发布全部规则

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/judge/rules4edit/publish-all`
- **说明**: Edit → Published

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | SatelliteLookupRequest | 是 | {"taskNo":"TASK_A","satNo":"SAT_01"} |
| target.taskNo | string | 是 | "TASK_A" |
| target.satNo | string | 是 | "SAT_01" |

**响应参数**
_无响应体（HTTP 200，空 body）_


#### 获取标签列表

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/judge/tags`
- **说明**: 目标卫星下全部标签

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | SatelliteLookupRequest | 是 | {"taskNo":"TASK_A","satNo":"SAT_01"} |
| target.taskNo | string | 是 | "TASK_A" |
| target.satNo | string | 是 | "SAT_01" |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | array(JudgeTagDto) | 否 | [] |
| datas[].id | string | 否 | "..." |
| datas[].tagName | string | 否 | "重要" |
| datas[].enable | bool | 否 | true |
| datas[].judgeCodes | array(string) | 否 | ["JR001"] |


#### 新增/更新标签

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/judge/tags/upsert`
- **说明**: Upsert 标签

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | SatelliteLookupRequest | 是 | {"taskNo":"TASK_A","satNo":"SAT_01"} |
| target.taskNo | string | 是 | "TASK_A" |
| target.satNo | string | 是 | "SAT_01" |
| entity | JudgeTagDto | 是 | {} |
| entity.id | string | 否 | "..." |
| entity.tagName | string | 否 | "重要" |
| entity.enable | bool | 否 | true |
| entity.judgeCodes | array(string) | 否 | ["JR001"] |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 否 | "..." |


#### 删除标签

- **方法**: `POST`
- **路径**: `/api/v2/mass-data/judge/tags/delete`
- **说明**: 按 id 删除

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | SatelliteLookupRequest | 是 | {"taskNo":"TASK_A","satNo":"SAT_01"} |
| target.taskNo | string | 是 | "TASK_A" |
| target.satNo | string | 是 | "SAT_01" |
| id | string | 是 | "..." |

**响应参数**
_无响应体（HTTP 200，空 body）_



### 3.9 v2 Web API 错误响应（通用）

| HTTP 状态码 | 响应体字段 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|---|
| 400 | error | string | 否 | "参数错误描述" |
| 404 | error | string | 否 | "卫星不存在" |
| 408 | error | string | 否 | "请求已取消或超时" |
| 500 | error | string | 否 | "操作失败摘要" |
| 500 | detail | string | 否 | "异常详情" |

## 4. Web API v1 详细参数说明

> 路由前缀：`/api/v1/mass-data`。卫星标识字段为 `taskNo` + `satNo`（与 gRPC v1 一致；配置中 `Ver` 用于区分阶段）。

### 4.1 卫星与基础信息（`/api/v1/mass-data`）

#### 获取卫星列表

- **方法**: `GET`
- **路径**: `/api/v1/mass-data/satellites`
- **说明**: v1 卫星摘要

**请求参数**
_无请求体（无 Query 参数）_

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | array(SatelliteThumbDto) | 否 | [] |
| datas[].taskNo | string | 否 | "TASK_A" |
| datas[].taskName | string | 否 | "任务A" |
| datas[].satNo | string | 否 | "SAT_01" |
| datas[].satName | string | 否 | "卫星01" |
| datas[].enabled | bool | 否 | true |


#### 获取卫星配置

- **方法**: `POST`
- **路径**: `/api/v1/mass-data/satellite/config`
- **说明**: 连接配置

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| basicConn | string | 否 | "mongodb://..." |
| mongoQueryConn | string | 否 | "" |
| judgeConn | string | 否 | "" |
| judgeMgrConn | string | 否 | "" |
| analysisConn | string | 否 | "" |
| displayConn | string | 否 | "" |
| mqttUrl | string | 否 | "" |
| signalRUrl | string | 否 | "" |
| cfgConn | string | 否 | "" |


#### 获取 MQ 配置

- **方法**: `POST`
- **路径**: `/api/v1/mass-data/satellite/message-queue`
- **说明**: MQ

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| url | string | 否 | "amqp://..." |
| exchangeType | string | 否 | "topic" |
| exchangeName | string | 否 | "mass.exchange" |
| queues | array(MessageQueueItemDto) | 否 | [] |
| queues[].key | string | 否 | "param" |
| queues[].queueName | string | 否 | "param.queue" |
| queues[].exchangeName | string | 否 | "mass.exchange" |


#### 获取 Redis 配置

- **方法**: `POST`
- **路径**: `/api/v1/mass-data/satellite/redis`
- **说明**: Redis

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| url | string | 否 | "127.0.0.1:6379" |
| pwd | string | 否 | "" |
| dbIndex | int | 否 | 0 |
| keys | array(string) | 否 | ["key1"] |


### 4.2 基础库（`/api/v1/mass-data`）

#### 参数定义 GET

- **方法**: `GET`
- **路径**: `/api/v1/mass-data/basic/parameters`
- **说明**: Query 传参

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| （Query）taskNo | string | 是 | "TASK_A" |
| （Query）satNo | string | 是 | "SAT_01" |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | array(BasicParameterDto) | 否 | [] |
| datas[].paraId | int | 否 | 100108 |
| datas[].prmSysId | int | 否 | 1 |
| datas[].paraCode | string | 否 | "P001" |
| datas[].paraType | int | 否 | 1 |
| datas[].paraTypeChar | string | 否 | "F" |
| datas[].paraTypeDesc | string | 否 | "浮点" |
| datas[].paraDesc | string | 否 | "温度" |
| datas[].minValue | double | 否 | 0 |
| datas[].maxValue | double | 否 | 100 |
| datas[].updateTime | int | 否 | 0 |
| datas[].valueDesc | string | 否 | "" |
| datas[].validFlag | int | 否 | 1 |
| datas[].watchFlag | int | 否 | 0 |
| datas[].parameterType | int | 否 | 0 |
| datas[].editGroup | string | 否 | "" |
| datas[].procId | int | 否 | 0 |
| datas[].procDesc | string | 否 | "" |
| datas[].paraMask | string | 否 | "" |


#### 参数定义 POST

- **方法**: `POST`
- **路径**: `/api/v1/mass-data/basic/parameters`
- **说明**: Body 传参

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | array(BasicParameterDto) | 否 | [] |
| datas[].paraId | int | 否 | 100108 |
| datas[].prmSysId | int | 否 | 1 |
| datas[].paraCode | string | 否 | "P001" |
| datas[].paraType | int | 否 | 1 |
| datas[].paraTypeChar | string | 否 | "F" |
| datas[].paraTypeDesc | string | 否 | "浮点" |
| datas[].paraDesc | string | 否 | "温度" |
| datas[].minValue | double | 否 | 0 |
| datas[].maxValue | double | 否 | 100 |
| datas[].updateTime | int | 否 | 0 |
| datas[].valueDesc | string | 否 | "" |
| datas[].validFlag | int | 否 | 1 |
| datas[].watchFlag | int | 否 | 0 |
| datas[].parameterType | int | 否 | 0 |
| datas[].editGroup | string | 否 | "" |
| datas[].procId | int | 否 | 0 |
| datas[].procDesc | string | 否 | "" |
| datas[].paraMask | string | 否 | "" |


#### 指令定义

- **方法**: `POST`
- **路径**: `/api/v1/mass-data/basic/commands`
- **说明**: 

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | array(BasicCommandDto) | 否 | [] |


#### 包定义

- **方法**: `POST`
- **路径**: `/api/v1/mass-data/basic/packages`
- **说明**: 

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | array(BasicPackageDto) | 否 | [] |


#### 层级关系

- **方法**: `POST`
- **路径**: `/api/v1/mass-data/basic/relations`
- **说明**: 

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| pkgHirberDatas | array(BasicRelationDto) | 否 | [] |
| cmdHirberDatas | array(BasicRelationDto) | 否 | [] |


#### 指令判据

- **方法**: `POST`
- **路径**: `/api/v1/mass-data/basic/cmd-judges`
- **说明**: 

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | array(BasicCommandJudgeDto) | 否 | [] |


### 4.3 数据查询（`/api/v1/mass-data`）

#### 参数查询

- **方法**: `POST`
- **路径**: `/api/v1/mass-data/query/parameters`
- **说明**: v1 条件结构（ids 非 id）

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |
| fromDt | DateTime? | 否 | "2026-07-02T00:00:00Z" |
| toDt | DateTime? | 否 | "2026-07-02T01:00:00Z" |
| pkgParaIds | array(V1PackageParameterConditionDto) | 否 | [] |
| pkgParaIds[].pid | int | 是 | 37 |
| pkgParaIds[].ids | array(int) | 是 | [100108] |
| limitTotal | int | 否 | -1 |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | array(ParameterDto) | 否 | [] |
| datas[].id | int | 否 | 100108 |
| datas[].pid | int | 否 | 37 |
| datas[].pv | string | 否 | "25.6" |
| datas[].sv | string | 否 | "..." |
| datas[].pd | string | 否 | "" |
| datas[].dt | DateTime | 否 | "2026-07-02T00:01:00Z" |
| datas[].st | DateTime | 否 | "2026-07-02T00:01:01Z" |
| datas[].dtTicks | long | 否 | 638000000000000000 |
| datas[].pc | string | 否 | "P001" |
| datas[].satNo | string | 否 | "SAT_01" |
| datas[].taskNo | string | 否 | "TASK_A" |


#### 参数查询流式

- **方法**: `POST`
- **路径**: `/api/v1/mass-data/query/parameters/stream`
- **说明**: 顶层数组

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |
| fromDt | DateTime? | 否 | "2026-07-02T00:00:00Z" |
| toDt | DateTime? | 否 | "2026-07-02T01:00:00Z" |
| pkgParaIds | array(V1PackageParameterConditionDto) | 否 | [] |
| pkgParaIds[].pid | int | 是 | 37 |
| pkgParaIds[].ids | array(int) | 是 | [100108] |
| limitTotal | int | 否 | -1 |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| （流式） | array(ParameterDto) | — | 顶层 JSON 数组，元素字段同 ParameterDto |


#### 指令查询

- **方法**: `POST`
- **路径**: `/api/v1/mass-data/query/instructions`
- **说明**: instIds 为 int 列表，内部 chId=32

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |
| fromDt | DateTime? | 否 | "2026-07-02T00:00:00Z" |
| toDt | DateTime? | 否 | "2026-07-02T01:00:00Z" |
| instIds | array(int) | 否 | [101] |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | array(InstructionDto) | 否 | [] |


#### 包查询

- **方法**: `POST`
- **路径**: `/api/v1/mass-data/query/packages`
- **说明**: pkgIds 为 int 列表

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |
| fromDt | DateTime? | 否 | "2026-07-02T00:00:00Z" |
| toDt | DateTime? | 否 | "2026-07-02T01:00:00Z" |
| pkgIds | array(int) | 否 | [37] |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | array(PackageDto) | 否 | [] |


#### 包查询流式

- **方法**: `POST`
- **路径**: `/api/v1/mass-data/query/packages/stream`
- **说明**: 顶层数组

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |
| fromDt | DateTime? | 否 | "2026-07-02T00:00:00Z" |
| toDt | DateTime? | 否 | "2026-07-02T01:00:00Z" |
| pkgIds | array(int) | 否 | [37] |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| （流式） | array(PackageDto) | — |  |


#### 帧查询

- **方法**: `POST`
- **路径**: `/api/v1/mass-data/query/frames`
- **说明**: chId/rtFlag 固定 0

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |
| fromDt | DateTime? | 否 | "2026-07-02T00:00:00Z" |
| toDt | DateTime? | 否 | "2026-07-02T01:00:00Z" |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | array(FrameDto) | 否 | [] |


#### 帧查询流式

- **方法**: `POST`
- **路径**: `/api/v1/mass-data/query/frames/stream`
- **说明**: 顶层数组

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |
| fromDt | DateTime? | 否 | "2026-07-02T00:00:00Z" |
| toDt | DateTime? | 否 | "2026-07-02T01:00:00Z" |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| （流式） | array(FrameDto) | — |  |


### 4.4 判读（`/api/v1/mass-data`）

#### 判读信息

- **方法**: `POST`
- **路径**: `/api/v1/mass-data/judge/infos`
- **说明**: 

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | array(JudgeInfoDto) | 否 | [] |
| datas[].code | string | 否 | "JR001" |
| datas[].desc | string | 否 | "规则描述" |
| datas[].group | string | 否 | "G1" |
| datas[].ruleType | string | 否 | "TypeA" |
| datas[].resultValueType | string | 否 | "int" |
| datas[].resultValueDesc | string | 否 | "" |
| datas[].lastTime | DateTime | 否 | "2026-07-02T00:00:00Z" |
| datas[].data | object | 否 | {} |
| datas[].tags | array(string) | 否 | ["tag1"] |
| datas[].id | string | 否 | "..." |
| datas[].isEnable | bool | 否 | true |
| datas[].isResultDisplay | bool | 否 | true |
| datas[].isResultInDb | bool | 否 | true |


#### 判读结果

- **方法**: `POST`
- **路径**: `/api/v1/mass-data/judge/results`
- **说明**: 

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskNo | string | 是 | "TASK_A" |
| satNo | string | 是 | "SAT_01" |
| fromDt | DateTime? | 否 | "2026-07-02T00:00:00Z" |
| toDt | DateTime? | 否 | "2026-07-02T01:00:00Z" |
| judgeCodes | array(string) | 否 | ["JR001"] |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | array(JudgeResultDto) | 否 | [] |
| datas[].judgeRuleCode | string | 否 | "JR001" |
| datas[].judgeValue | string | 否 | "1" |
| datas[].judgeValueDesc | string | 否 | "正常" |
| datas[].createTime | DateTime | 否 | "2026-07-02T00:01:00Z" |

---

## 5. gRPC v2 详细接口说明

> 包名 `mass_platform.v2`，C# 命名空间 `MassPlatform.V2`。
> Proto 来源：`MassServerProtos/v2/mass_data_server_v2.proto`、`mass_data_server_judge_v2.proto`、`mass_models_v2.proto`。
> 字段类型为 proto3 标量或 `google.protobuf.Timestamp`；`repeated` 表示数组。
> 「是否必填项」：proto3 字段均为可选，此处按业务强依赖标注（如 `taskno`/`satno` 标「是」）。
> 流式 RPC（`stream`）的响应字段表描述**流中每个消息**的字段结构。

> 与 Web API 的差异：v2 gRPC `GSatelliteThumb` 不含 `dbstage` 字段；gRPC 仅有 `QueryMultSatParaAggregate`（多星复合参数），无单星复合参数 RPC。

### 5.1 MassDataServer（58 个 RPC）

服务定义见 `mass_data_server_v2.proto`，按职责分为：卫星配置(5)、基础库(5)、历史查询/统计(16)、文件下载(2)、判读管理与规则编辑(30)。

#### GetAllSatsFromMassServer

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `google.protobuf.Empty`
- **响应消息**: `GSatCollectionReply`

**请求参数**
_无字段_

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | repeated GSatelliteThumb | 否 | [] |
| datas[].taskno | string | 是 | "TASK_A" |
| datas[].taskname | string | 否 | "任务A" |
| datas[].satno | string | 是 | "SAT_01" |
| datas[].satname | string | 否 | "卫星01" |


#### GetAllSatsFromTaskDb

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GTaskDbReq`
- **响应消息**: `GSatCollectionReply`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskdbip | string | 是 | "127.0.0.1" |
| taskdbport | int32 | 是 | 27017 |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | repeated GSatelliteThumb | 否 | [] |
| datas[].taskno | string | 是 | "TASK_A" |
| datas[].taskname | string | 否 | "任务A" |
| datas[].satno | string | 是 | "SAT_01" |
| datas[].satname | string | 否 | "卫星01" |


#### GetSatCfg

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GSatelliteThumb`
- **响应消息**: `Satconncfg`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| taskname | string | 否 | "任务A" |
| satno | string | 是 | "SAT_01" |
| satname | string | 否 | "卫星01" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| basicconn | string | 否 | "mongodb://..." |
| mongoqueryconn | string | 否 | "" |
| judgeconn | string | 否 | "" |
| judgemgrconn | string | 否 | "" |
| analysisconn | string | 否 | "" |
| displayconn | string | 否 | "" |
| mqtturl | string | 否 | "" |
| signalrurl | string | 否 | "" |
| cfgconn | string | 否 | "" |


#### GetMessageQueueInfo

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GSatelliteThumb`
- **响应消息**: `GMsgQueueInfoReply`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| taskname | string | 否 | "任务A" |
| satno | string | 是 | "SAT_01" |
| satname | string | 否 | "卫星01" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| url | string | 否 | "amqp://..." |
| exchangetype | string | 否 | "direct" |
| exchangename | string | 否 | "mass.exchange" |
| queues | repeated GMsgQueueItem | 否 | [] |
| queues[].key | string | 否 | "param" |
| queues[].queuename | string | 否 | "param.queue" |
| queues[].exchangename | string | 否 | "mass.exchange" |


#### GetRedisInfo

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GSatelliteThumb`
- **响应消息**: `GRedisInfoReply`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| taskname | string | 否 | "任务A" |
| satno | string | 是 | "SAT_01" |
| satname | string | 否 | "卫星01" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| url | string | 否 | "amqp://..." |
| pwd | string | 否 | "" |
| dbindex | int32 | 否 | 0 |
| keys | repeated string | 否 | ["key1"] |


#### GetBasicDbPara

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GSatelliteThumb`
- **响应消息**: `GViewParas`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| taskname | string | 否 | "任务A" |
| satno | string | 是 | "SAT_01" |
| satname | string | 否 | "卫星01" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | repeated ViewPara | 否 | [] |
| datas[].para_id | int32 | 否 | 0 |
| datas[].prm_sys_id | int32 | 否 | 0 |
| datas[].para_code | string | 否 | "" |
| datas[].para_type | int32 | 否 | 0 |
| datas[].para_type_char | string | 否 | "" |
| datas[].para_type_desc | string | 否 | "" |
| datas[].para_desc | string | 否 | "" |
| datas[].min_value | double | 否 | 0.0 |
| datas[].max_value | double | 否 | 0.0 |
| datas[].update_time | int32 | 否 | 0 |
| datas[].value_desc | string | 否 | "" |
| datas[].valid_flag | int32 | 否 | 0 |
| datas[].watch_flag | int32 | 否 | 0 |
| datas[].parameter_type | int32 | 否 | 0 |
| datas[].edit_group | string | 否 | "" |
| datas[].proc_id | int32 | 否 | 0 |
| datas[].proc_desc | string | 否 | "" |
| datas[].para_mask | string | 否 | "" |


#### GetBasicDbCmd

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GSatelliteThumb`
- **响应消息**: `GViewCmds`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| taskname | string | 否 | "任务A" |
| satno | string | 是 | "SAT_01" |
| satname | string | 否 | "卫星01" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | repeated ViewCmd | 否 | [] |
| datas[].cmd_id | int32 | 否 | 0 |
| datas[].cmd_sys_id | int32 | 否 | 0 |
| datas[].cmd_code | string | 否 | "" |
| datas[].cmd_type | int32 | 否 | 0 |
| datas[].cmd_desc | string | 否 | "" |
| datas[].cmd_len | int32 | 否 | 0 |
| datas[].cmd_data | string | 否 | "" |
| datas[].exe_time | int32 | 否 | 0 |
| datas[].cmd_level | int32 | 否 | 0 |
| datas[].valid_flag | int32 | 否 | 0 |
| datas[].is_starmiddle_cmd | bool | 否 | false |
| datas[].singnl | string | 否 | "" |
| datas[].allow_check_data | bool | 否 | false |
| datas[].edit_group | string | 否 | "" |
| datas[].business_group | string | 否 | "" |


#### GetBasicDbPkg

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GSatelliteThumb`
- **响应消息**: `GViewPkgs`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| taskname | string | 否 | "任务A" |
| satno | string | 是 | "SAT_01" |
| satname | string | 否 | "卫星01" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | repeated ViewPkg | 否 | [] |
| datas[].sys_id | int32 | 否 | 0 |
| datas[].pkg_flag | string | 否 | "" |
| datas[].pkg_len | int32 | 否 | 0 |
| datas[].sub_flag | string | 否 | "" |
| datas[].pkg_desc | string | 否 | "" |
| datas[].update_time | int32 | 否 | 0 |
| datas[].valid_flag | int32 | 否 | 0 |
| datas[].pkg_flag_assist | string | 否 | "" |
| datas[].sys_code | string | 否 | "" |


#### GetBasicDbRelation

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GSatelliteThumb`
- **响应消息**: `GViewSysRelates`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| taskname | string | 否 | "任务A" |
| satno | string | 是 | "SAT_01" |
| satname | string | 否 | "卫星01" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| pkg_hirber_datas | repeated ViewSysRelation | 否 | [] |
| pkg_hirber_datas[].sys_id | int32 | 否 | 0 |
| pkg_hirber_datas[].sys_code | string | 否 | "" |
| pkg_hirber_datas[].sys_desc | string | 否 | "" |
| pkg_hirber_datas[].father_sys_id | int32 | 否 | 0 |
| pkg_hirber_datas[].level | int32 | 否 | 0 |
| pkg_hirber_datas[].sys_type | int32 | 否 | 0 |
| cmd_hirber_datas | repeated ViewSysRelation | 否 | [] |
| cmd_hirber_datas[].sys_id | int32 | 否 | 0 |
| cmd_hirber_datas[].sys_code | string | 否 | "" |
| cmd_hirber_datas[].sys_desc | string | 否 | "" |
| cmd_hirber_datas[].father_sys_id | int32 | 否 | 0 |
| cmd_hirber_datas[].level | int32 | 否 | 0 |
| cmd_hirber_datas[].sys_type | int32 | 否 | 0 |


#### GetBasicDbCmdJudges

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GSatelliteThumb`
- **响应消息**: `GViewCmdjudges`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| taskname | string | 否 | "任务A" |
| satno | string | 是 | "SAT_01" |
| satname | string | 否 | "卫星01" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | repeated ViewCmdjudge | 否 | [] |
| datas[].cmd_id | int32 | 否 | 0 |
| datas[].judge_id | int32 | 否 | 0 |
| datas[].para_id | int32 | 否 | 0 |
| datas[].judge_type | int32 | 否 | 0 |
| datas[].i_dn_value | string | 否 | "" |
| datas[].i_up_value | string | 否 | "" |
| datas[].r_dn_value | string | 否 | "" |
| datas[].r_up_value | string | 否 | "" |
| datas[].v_dn_value | string | 否 | "" |
| datas[].v_up_value | string | 否 | "" |
| datas[].judge_time | int32 | 否 | 0 |


#### QuerygPara

- **服务**: `MassDataServer`
- **调用类型**: Server streaming
- **请求消息**: `GQueryCondParaReq`
- **响应消息**: `stream GParaCollect`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| fromdt | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| todt | Timestamp | 否 | "2026-07-02T01:00:00Z" |
| pkgparaids | repeated GPkgParaRelate | 否 | [] |
| pkgparaids[].pid | int32 | 否 | 37 |
| pkgparaids[].ids | repeated int32 | 否 | [100108] |
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | repeated Gpara | 否 | [] |
| datas[].id | int32 | 是 | "507f1f77bcf86cd799439011" |
| datas[].pkgid | int32 | 否 | 37 |
| datas[].pv | double | 否 | 25.6 |
| datas[].sv | string | 否 | "..." |
| datas[].pd | string | 否 | "" |
| datas[].dt | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| datas[].st | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| datas[].pc | string | 否 | "P001" |
| datas[].SatNo | string | 是 | "SAT_01" |
| datas[].TaskNo | string | 是 | "TASK_A" |
| datas[].chid | int32 | 否 | 0 |
| datas[].rtflag | int32 | 否 | 0 |
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |


#### QueryMultSatgPara

- **服务**: `MassDataServer`
- **调用类型**: Server streaming
- **请求消息**: `GQueryCondMultSatParaReq`
- **响应消息**: `stream GParaCollect`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| fromdt | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| todt | Timestamp | 否 | "2026-07-02T01:00:00Z" |
| parainfos | repeated GPkgParaMultSatRelate | 否 | [] |
| parainfos[].taskno | string | 是 | "TASK_A" |
| parainfos[].satno | string | 是 | "SAT_01" |
| parainfos[].pkgid | int32 | 否 | 37 |
| parainfos[].paraid | int32 | 否 | 100108 |
| parainfos[].chid | int32 | 否 | 0 |
| parainfos[].rtflag | int32 | 否 | 0 |
| limittotal | int32 | 否 | -1 |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | repeated Gpara | 否 | [] |
| datas[].id | int32 | 是 | "507f1f77bcf86cd799439011" |
| datas[].pkgid | int32 | 否 | 37 |
| datas[].pv | double | 否 | 25.6 |
| datas[].sv | string | 否 | "..." |
| datas[].pd | string | 否 | "" |
| datas[].dt | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| datas[].st | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| datas[].pc | string | 否 | "P001" |
| datas[].SatNo | string | 是 | "SAT_01" |
| datas[].TaskNo | string | 是 | "TASK_A" |
| datas[].chid | int32 | 否 | 0 |
| datas[].rtflag | int32 | 否 | 0 |
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |


#### QueryMultSatParaAggregate

- **服务**: `MassDataServer`
- **调用类型**: Server streaming
- **请求消息**: `GQueryCondMultSatParaAggregateReq`
- **响应消息**: `stream GparaCompositeResult`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| fromdt | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| todt | Timestamp | 否 | "2026-07-02T01:00:00Z" |
| parainfos | repeated GPkgParaMultSatRelate | 否 | [] |
| parainfos[].taskno | string | 是 | "TASK_A" |
| parainfos[].satno | string | 是 | "SAT_01" |
| parainfos[].pkgid | int32 | 否 | 37 |
| parainfos[].paraid | int32 | 否 | 100108 |
| parainfos[].chid | int32 | 否 | 0 |
| parainfos[].rtflag | int32 | 否 | 0 |
| containinst | bool | 否 | true |
| bl_split_page | bool | 否 | false |
| skipnum | int32 | 否 | 0 |
| limitnum | int32 | 否 | 0 |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | repeated GparaComposite | 否 | [] |
| datas[].time | Timestamp | 否 | "2026-07-02T00:01:00Z" |
| datas[].instdatas | repeated GindicatorSimple | 否 | [] |
| datas[].paradatas | repeated GparaSimple | 否 | [] |
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |


#### QuerygInst

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GQueryCondInstReq`
- **响应消息**: `GInstCollect`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| fromdt | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| todt | Timestamp | 否 | "2026-07-02T01:00:00Z" |
| instids | repeated int32 | 否 | [101] |
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | repeated Gindicator | 否 | [] |
| datas[].ci | int32 | 否 | 101 |
| datas[].cc | string | 否 | "CMD01" |
| datas[].cd | string | 否 | "AA BB" |
| datas[].cn | string | 否 | "指令名" |
| datas[].et | Timestamp | 否 | "2026-07-02T00:01:00Z" |
| datas[].c_judgeinfo | string | 否 | "" |
| datas[].relative_paras | repeated Gpara | 否 | [] |
| datas[].chid | int32 | 否 | 0 |
| datas[].st | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |


#### QueryMultSatgInst

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GQueryCondMultSatInstReq`
- **响应消息**: `GInstCollect`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| fromdt | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| todt | Timestamp | 否 | "2026-07-02T01:00:00Z" |
| instinfos | repeated GInstInfoReq | 否 | [] |
| instinfos[].taskno | string | 是 | "TASK_A" |
| instinfos[].satno | string | 是 | "SAT_01" |
| instinfos[].instid | int32 | 否 | 101 |
| instinfos[].chid | int32 | 否 | 0 |
| instinfos[].rtflag | int32 | 否 | 0 |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | repeated Gindicator | 否 | [] |
| datas[].ci | int32 | 否 | 101 |
| datas[].cc | string | 否 | "CMD01" |
| datas[].cd | string | 否 | "AA BB" |
| datas[].cn | string | 否 | "指令名" |
| datas[].et | Timestamp | 否 | "2026-07-02T00:01:00Z" |
| datas[].c_judgeinfo | string | 否 | "" |
| datas[].relative_paras | repeated Gpara | 否 | [] |
| datas[].chid | int32 | 否 | 0 |
| datas[].st | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |


#### QuerygInstStream

- **服务**: `MassDataServer`
- **调用类型**: Server streaming
- **请求消息**: `GQueryCondInstReq`
- **响应消息**: `stream GInstCollect`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| fromdt | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| todt | Timestamp | 否 | "2026-07-02T01:00:00Z" |
| instids | repeated int32 | 否 | [101] |
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | repeated Gindicator | 否 | [] |
| datas[].ci | int32 | 否 | 101 |
| datas[].cc | string | 否 | "CMD01" |
| datas[].cd | string | 否 | "AA BB" |
| datas[].cn | string | 否 | "指令名" |
| datas[].et | Timestamp | 否 | "2026-07-02T00:01:00Z" |
| datas[].c_judgeinfo | string | 否 | "" |
| datas[].relative_paras | repeated Gpara | 否 | [] |
| datas[].chid | int32 | 否 | 0 |
| datas[].st | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |


#### QueryMultSatgInstStream

- **服务**: `MassDataServer`
- **调用类型**: Server streaming
- **请求消息**: `GQueryCondMultSatInstReq`
- **响应消息**: `stream GInstCollect`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| fromdt | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| todt | Timestamp | 否 | "2026-07-02T01:00:00Z" |
| instinfos | repeated GInstInfoReq | 否 | [] |
| instinfos[].taskno | string | 是 | "TASK_A" |
| instinfos[].satno | string | 是 | "SAT_01" |
| instinfos[].instid | int32 | 否 | 101 |
| instinfos[].chid | int32 | 否 | 0 |
| instinfos[].rtflag | int32 | 否 | 0 |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | repeated Gindicator | 否 | [] |
| datas[].ci | int32 | 否 | 101 |
| datas[].cc | string | 否 | "CMD01" |
| datas[].cd | string | 否 | "AA BB" |
| datas[].cn | string | 否 | "指令名" |
| datas[].et | Timestamp | 否 | "2026-07-02T00:01:00Z" |
| datas[].c_judgeinfo | string | 否 | "" |
| datas[].relative_paras | repeated Gpara | 否 | [] |
| datas[].chid | int32 | 否 | 0 |
| datas[].st | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |


#### QuerygPkg

- **服务**: `MassDataServer`
- **调用类型**: Server streaming
- **请求消息**: `GQueryCondPkgReq`
- **响应消息**: `stream GPkgCollect`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| fromdt | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| todt | Timestamp | 否 | "2026-07-02T01:00:00Z" |
| pkgids | repeated int32 | 否 | [37] |
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | repeated Gpkg | 否 | [] |
| datas[].pi | int32 | 否 | 37 |
| datas[].pc | string | 否 | "P001" |
| datas[].pd | string | 否 | "" |
| datas[].pt | Timestamp | 否 | "2026-07-02T00:01:00Z" |
| datas[].chid | int32 | 否 | 0 |
| datas[].rtflag | int32 | 否 | 0 |
| datas[].st | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |


#### QueryMultSatgPkg

- **服务**: `MassDataServer`
- **调用类型**: Server streaming
- **请求消息**: `GQueryCondMultSatPkgReq`
- **响应消息**: `stream GPkgCollect`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| fromdt | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| todt | Timestamp | 否 | "2026-07-02T01:00:00Z" |
| pkginfos | repeated GPkgInfoReq | 否 | [] |
| pkginfos[].taskno | string | 是 | "TASK_A" |
| pkginfos[].satno | string | 是 | "SAT_01" |
| pkginfos[].pkgid | int32 | 否 | 37 |
| pkginfos[].chid | int32 | 否 | 0 |
| pkginfos[].rtflag | int32 | 否 | 0 |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | repeated Gpkg | 否 | [] |
| datas[].pi | int32 | 否 | 37 |
| datas[].pc | string | 否 | "P001" |
| datas[].pd | string | 否 | "" |
| datas[].pt | Timestamp | 否 | "2026-07-02T00:01:00Z" |
| datas[].chid | int32 | 否 | 0 |
| datas[].rtflag | int32 | 否 | 0 |
| datas[].st | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |


#### QuerygFrame

- **服务**: `MassDataServer`
- **调用类型**: Server streaming
- **请求消息**: `GQueryCondFrameReq`
- **响应消息**: `stream GFrameCollect`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| fromdt | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| todt | Timestamp | 否 | "2026-07-02T01:00:00Z" |
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | repeated Gframe | 否 | [] |
| datas[].ft | Timestamp | 否 | "2026-07-02T00:01:00Z" |
| datas[].fno | int32 | 否 | 1 |
| datas[].fd | string | 否 | "AA BB" |
| datas[].chid | int32 | 否 | 0 |
| datas[].rtflag | int32 | 否 | 0 |
| datas[].st | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |


#### QueryMultSatgFrame

- **服务**: `MassDataServer`
- **调用类型**: Server streaming
- **请求消息**: `GQueryCondMultSatFrameReq`
- **响应消息**: `stream GFrameCollect`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| fromdt | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| todt | Timestamp | 否 | "2026-07-02T01:00:00Z" |
| frameinfos | repeated GFrameInfoReq | 否 | [] |
| frameinfos[].taskno | string | 是 | "TASK_A" |
| frameinfos[].satno | string | 是 | "SAT_01" |
| frameinfos[].chid | int32 | 否 | 0 |
| frameinfos[].rtflag | int32 | 否 | 0 |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | repeated Gframe | 否 | [] |
| datas[].ft | Timestamp | 否 | "2026-07-02T00:01:00Z" |
| datas[].fno | int32 | 否 | 1 |
| datas[].fd | string | 否 | "AA BB" |
| datas[].chid | int32 | 否 | 0 |
| datas[].rtflag | int32 | 否 | 0 |
| datas[].st | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |


#### AggregateParameters

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GQueryCondParaAggReq`
- **响应消息**: `GParaAggregationCollect`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |
| fromdt | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| todt | Timestamp | 否 | "2026-07-02T01:00:00Z" |
| paraids | repeated int32 | 否 | [100108] |
| intervalseconds | int32 | 否 | 60 |
| aggregationtype | GAggregationType | 否 | AVERAGE |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | repeated GParaAggregation | 否 | [] |
| datas[].timebucket | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| datas[].parameterid | int32 | 否 | 100108 |
| datas[].parametercode | string | 否 | "P001" |
| datas[].value | double | 否 | 25.6 |
| datas[].count | int64 | 否 | 10 |
| satno | string | 是 | "SAT_01" |
| taskno | string | 是 | "TASK_A" |


#### GetParameterStatistics

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GQueryCondParaStatisticsReq`
- **响应消息**: `GParameterStatisticsReply`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |
| fromdt | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| todt | Timestamp | 否 | "2026-07-02T01:00:00Z" |
| paraid | int32 | 否 | 100108 |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| parameterid | int32 | 否 | 100108 |
| parametercode | string | 否 | "P001" |
| totalcount | int64 | 否 | 1000 |
| firsttimestamp | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| lasttimestamp | Timestamp | 否 | "2026-07-02T01:00:00Z" |
| minvalue | double | 否 | 20.1 |
| maxvalue | double | 否 | 30.5 |
| averagevalue | double | 否 | 25.3 |
| stddevvalue | double | 否 | 1.2 |
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |


#### GetDataVolumeStatistics

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GQueryCondDataVolumeReq`
- **响应消息**: `GDataVolumeStatisticsReply`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |
| fromdt | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| todt | Timestamp | 否 | "2026-07-02T01:00:00Z" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| parametercount | int64 | 否 | 10000 |
| instructioncount | int64 | 否 | 100 |
| packagecount | int64 | 否 | 500 |
| framecount | int64 | 否 | 200 |
| judgeresultcount | int64 | 否 | 50 |
| totalcount | int64 | 否 | 1000 |
| totalsizebytes | int64 | 否 | 1048576 |
| oldesttimestamp | Timestamp | 否 | "2026-07-01T00:00:00Z" |
| newesttimestamp | Timestamp | 否 | "2026-07-02T01:00:00Z" |
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |


#### GetJudgeInfos

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GSatelliteThumb`
- **响应消息**: `GJudgeInfoCollect`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| taskname | string | 否 | "任务A" |
| satno | string | 是 | "SAT_01" |
| satname | string | 否 | "卫星01" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | repeated Gjudgeinfo | 否 | [] |
| datas[].code | string | 是 | "JR001" |
| datas[].desc | string | 否 | "" |
| datas[].group | string | 否 | "" |
| datas[].rule_type | string | 否 | "TypeA" |
| datas[].resultvaluetype | string | 否 | "int" |
| datas[].resultvaluedesc | string | 否 | "" |
| datas[].last_time | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| datas[].data | string | 否 | "{}" |
| datas[].statue | int32 | 否 | 0 |
| datas[].tags | repeated string | 否 | ["t1"] |
| datas[].id | string | 是 | "507f1f77bcf86cd799439011" |
| datas[].is_enable | bool | 否 | true |
| datas[].is_result_display | bool | 否 | true |
| datas[].is_result_in_db | bool | 否 | true |
| datas[].apply_by_model_code | string | 否 | "" |
| datas[].apply_by_model_judge_rule_code | string | 否 | "" |
| datas[].is_same_as_model | bool | 否 | false |
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |


#### QuerygJudgeResults

- **服务**: `MassDataServer`
- **调用类型**: Server streaming
- **请求消息**: `GQueryCondJudgeReusltReq`
- **响应消息**: `stream GJudgeResultCollect`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |
| fromdt | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| todt | Timestamp | 否 | "2026-07-02T01:00:00Z" |
| judgecodes | repeated string | 否 | ["JR001"] |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | repeated Gjudgeresult | 否 | [] |
| datas[].judgerulecode | string | 否 | "JR001" |
| datas[].judgevalue | string | 否 | "1" |
| datas[].judgevaluedesc | string | 否 | "正常" |
| datas[].createtime | Timestamp | 否 | "2026-07-02T00:01:00Z" |
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |


#### DownLoadTMResolveFiles

- **服务**: `MassDataServer`
- **调用类型**: Server streaming
- **请求消息**: `GDownCfgRevoleFileReq`
- **响应消息**: `stream GDatafile`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |
| absolutedir | string | 是 | "test\\" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| filename | string | 否 | "config.ini" |
| filetype | Filetypeenum | 否 | BYTES |
| datas | bytes | 否 | "<base64>" |
| dirpath | string | 否 | "C:\cfg" |


#### DownLoadRitsConfigFile

- **服务**: `MassDataServer`
- **调用类型**: Server streaming
- **请求消息**: `GDownRitsConfigFileReq`
- **响应消息**: `stream GDatafile`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| satnos | repeated string | 否 | ["SAT_01","SAT_02"] |
| absolutedir | string | 是 | "test\\" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| filename | string | 否 | "config.ini" |
| filetype | Filetypeenum | 否 | BYTES |
| datas | bytes | 否 | "<base64>" |
| dirpath | string | 否 | "C:\cfg" |


#### GetJudgeMgrSatConfigs

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `google.protobuf.Empty`
- **响应消息**: `GJudgeMgrSatConfigCollection`

**请求参数**
_无字段_

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | repeated GJudgeMgrSatConfig | 否 | [] |
| datas[].id | string | 是 | "507f1f77bcf86cd799439011" |
| datas[].ver | string | 否 | "1" |
| datas[].task_name | string | 否 | "" |
| datas[].sat_no | string | 否 | "SAT_01" |
| datas[].sat_name | string | 否 | "" |
| datas[].stage | string | 否 | "在轨" |
| datas[].bdb_ip | string | 否 | "" |
| datas[].bdb_port | int32 | 否 | 0 |
| datas[].db_name | string | 否 | "" |
| datas[].bdb_user_name | string | 否 | "" |
| datas[].bdb_user_psw | string | 否 | "" |
| datas[].judge_db_ip | string | 否 | "" |
| datas[].judge_db_port | int32 | 否 | 0 |
| datas[].judge_db_user_name | string | 否 | "" |
| datas[].judge_db_psw | string | 否 | "" |
| datas[].judge_db_name | string | 否 | "" |
| datas[].judge_sat_group_id | string | 否 | "..." |


#### UpsertJudgeMgrSatConfig

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeMgrSatConfigUpsertReq`
- **响应消息**: `GEntityIdReq`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| entity | GJudgeMgrSatConfig | 否 | {} |
| entity.id | string | 是 | "507f1f77bcf86cd799439011" |
| entity.ver | string | 否 | "1" |
| entity.task_name | string | 否 | "" |
| entity.sat_no | string | 否 | "SAT_01" |
| entity.sat_name | string | 否 | "" |
| entity.stage | string | 否 | "在轨" |
| entity.bdb_ip | string | 否 | "" |
| entity.bdb_port | int32 | 否 | 0 |
| entity.db_name | string | 否 | "" |
| entity.bdb_user_name | string | 否 | "" |
| entity.bdb_user_psw | string | 否 | "" |
| entity.judge_db_ip | string | 否 | "" |
| entity.judge_db_port | int32 | 否 | 0 |
| entity.judge_db_user_name | string | 否 | "" |
| entity.judge_db_psw | string | 否 | "" |
| entity.judge_db_name | string | 否 | "" |
| entity.judge_sat_group_id | string | 否 | "..." |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 是 | "507f1f77bcf86cd799439011" |


#### DeleteJudgeMgrSatConfig

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeMgrSatConfigDeleteReq`
- **响应消息**: `google.protobuf.Empty`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 是 | "507f1f77bcf86cd799439011" |

**响应参数**
_无字段_


#### GetJudgeSatGroups

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `google.protobuf.Empty`
- **响应消息**: `GJudgeSatGroupCollection`

**请求参数**
_无字段_

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | repeated GJudgeSatGroup | 否 | [] |
| datas[].id | string | 是 | "507f1f77bcf86cd799439011" |
| datas[].name | string | 否 | "分组1" |
| datas[].sat_members_str | string | 否 | "SAT_01,SAT_02" |
| datas[].sat_templates_str | string | 否 | "" |


#### QueryJudgeSatGroups

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeSatGroupQueryReq`
- **响应消息**: `GJudgeSatGroupPageReply`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| keyword | string | 否 | "" |
| page_index | int32 | 否 | 0 |
| page_size | int32 | 否 | 20 |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| total_count | int32 | 否 | 0 |
| datas | repeated GJudgeSatGroup | 否 | [] |
| datas[].id | string | 是 | "507f1f77bcf86cd799439011" |
| datas[].name | string | 否 | "分组1" |
| datas[].sat_members_str | string | 否 | "SAT_01,SAT_02" |
| datas[].sat_templates_str | string | 否 | "" |


#### UpsertJudgeSatGroup

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeSatGroupUpsertReq`
- **响应消息**: `GEntityIdReq`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| entity | GJudgeSatGroup | 否 | {} |
| entity.id | string | 是 | "507f1f77bcf86cd799439011" |
| entity.name | string | 否 | "分组1" |
| entity.sat_members_str | string | 否 | "SAT_01,SAT_02" |
| entity.sat_templates_str | string | 否 | "" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 是 | "507f1f77bcf86cd799439011" |


#### DeleteJudgeSatGroup

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeSatGroupDeleteReq`
- **响应消息**: `google.protobuf.Empty`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 是 | "507f1f77bcf86cd799439011" |

**响应参数**
_无字段_


#### GetJudgeRuleGroups

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `google.protobuf.Empty`
- **响应消息**: `GJudgeRuleGroupCollection`

**请求参数**
_无字段_

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | repeated GJudgeRuleGroup | 否 | [] |
| datas[].id | string | 是 | "507f1f77bcf86cd799439011" |
| datas[].name | string | 否 | "分组1" |
| datas[].name_ch | string | 否 | "分组一" |
| datas[].desc | string | 否 | "" |


#### UpsertJudgeRuleGroup

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeRuleGroupUpsertReq`
- **响应消息**: `GEntityIdReq`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| entity | GJudgeRuleGroup | 否 | {} |
| entity.id | string | 是 | "507f1f77bcf86cd799439011" |
| entity.name | string | 否 | "分组1" |
| entity.name_ch | string | 否 | "分组一" |
| entity.desc | string | 否 | "" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 是 | "507f1f77bcf86cd799439011" |


#### DeleteJudgeRuleGroup

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeRuleGroupDeleteReq`
- **响应消息**: `google.protobuf.Empty`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 是 | "507f1f77bcf86cd799439011" |

**响应参数**
_无字段_


#### GetJudgeSatTemplates

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `google.protobuf.Empty`
- **响应消息**: `GJudgeSatTemplateCollection`

**请求参数**
_无字段_

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | repeated GJudgeSatTemplate | 否 | [] |
| datas[].id | string | 是 | "507f1f77bcf86cd799439011" |
| datas[].name | string | 否 | "分组1" |
| datas[].source_ver | string | 否 | "1" |
| datas[].source_sat_name | string | 否 | "SAT_01" |
| datas[].source_stage | string | 否 | "在轨" |
| datas[].desc | string | 否 | "" |
| datas[].tag | repeated string | 否 | ["t1"] |
| datas[].judge_sat_group_id | string | 否 | "..." |
| datas[].judge_rule_models | repeated GJudgeRuleTemplate | 否 | [] |
| datas[].para_template_infos | repeated GParaTemplateInfo | 否 | [] |
| datas[].cmd_template_infos | repeated GCmdTemplateInfo | 否 | [] |
| datas[].pkg_template_infos | repeated GPkgTemplateInfo | 否 | [] |
| datas[].create_time | Timestamp | 否 | "2026-07-02T00:00:00Z" |


#### QueryJudgeSatTemplates

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeSatTemplateQueryReq`
- **响应消息**: `GJudgeSatTemplatePageReply`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| keyword | string | 否 | "" |
| page_index | int32 | 否 | 0 |
| page_size | int32 | 否 | 20 |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| total_count | int32 | 否 | 0 |
| datas | repeated GJudgeSatTemplate | 否 | [] |
| datas[].id | string | 是 | "507f1f77bcf86cd799439011" |
| datas[].name | string | 否 | "分组1" |
| datas[].source_ver | string | 否 | "1" |
| datas[].source_sat_name | string | 否 | "SAT_01" |
| datas[].source_stage | string | 否 | "在轨" |
| datas[].desc | string | 否 | "" |
| datas[].tag | repeated string | 否 | ["t1"] |
| datas[].judge_sat_group_id | string | 否 | "..." |
| datas[].judge_rule_models | repeated GJudgeRuleTemplate | 否 | [] |
| datas[].para_template_infos | repeated GParaTemplateInfo | 否 | [] |
| datas[].cmd_template_infos | repeated GCmdTemplateInfo | 否 | [] |
| datas[].pkg_template_infos | repeated GPkgTemplateInfo | 否 | [] |
| datas[].create_time | Timestamp | 否 | "2026-07-02T00:00:00Z" |


#### UpsertJudgeSatTemplate

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeSatTemplateUpsertReq`
- **响应消息**: `GEntityIdReq`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| entity | GJudgeSatTemplate | 否 | {} |
| entity.id | string | 是 | "507f1f77bcf86cd799439011" |
| entity.name | string | 否 | "分组1" |
| entity.source_ver | string | 否 | "1" |
| entity.source_sat_name | string | 否 | "SAT_01" |
| entity.source_stage | string | 否 | "在轨" |
| entity.desc | string | 否 | "" |
| entity.tag | repeated string | 否 | ["t1"] |
| entity.judge_sat_group_id | string | 否 | "..." |
| entity.judge_rule_models | repeated GJudgeRuleTemplate | 否 | [] |
| entity.para_template_infos | repeated GParaTemplateInfo | 否 | [] |
| entity.cmd_template_infos | repeated GCmdTemplateInfo | 否 | [] |
| entity.pkg_template_infos | repeated GPkgTemplateInfo | 否 | [] |
| entity.create_time | Timestamp | 否 | "2026-07-02T00:00:00Z" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 是 | "507f1f77bcf86cd799439011" |


#### DeleteJudgeSatTemplate

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeSatTemplateDeleteReq`
- **响应消息**: `google.protobuf.Empty`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 是 | "507f1f77bcf86cd799439011" |

**响应参数**
_无字段_


#### QueryJudgeRules4Edit

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeRuleQueryReq`
- **响应消息**: `GJudgeRulePageReply`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | GSatRuleDbTarget | 否 | {"taskno":"TASK_A","satno":"SAT_01"} |
| target.taskno | string | 是 | "TASK_A" |
| target.satno | string | 是 | "SAT_01" |
| collection_type | GJudgeRuleCollection | 否 | GJudgeRuleCollection |
| rule_types | repeated string | 否 | [] |
| rule_group | string | 否 | "" |
| keyword | string | 否 | "" |
| param_text | string | 否 | "" |
| tree_filter_type | GTreeFilterType | 否 | TREE_FILTER_ALL |
| package_sys_id | int32 | 否 | 0 |
| parameter_id | int32 | 否 | 0 |
| parent_rule_codes | repeated string | 否 | [] |
| ref_para_ids | repeated string | 否 | [] |
| page_index | int32 | 否 | 0 |
| page_size | int32 | 否 | 20 |
| sort_field | string | 否 | "Code" |
| sort_asc | bool | 否 | true |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| total_count | int32 | 否 | 0 |
| datas | repeated Gjudgeinfo | 否 | [] |
| datas[].code | string | 是 | "JR001" |
| datas[].desc | string | 否 | "" |
| datas[].group | string | 否 | "" |
| datas[].rule_type | string | 否 | "TypeA" |
| datas[].resultvaluetype | string | 否 | "int" |
| datas[].resultvaluedesc | string | 否 | "" |
| datas[].last_time | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| datas[].data | string | 否 | "{}" |
| datas[].statue | int32 | 否 | 0 |
| datas[].tags | repeated string | 否 | ["t1"] |
| datas[].id | string | 是 | "507f1f77bcf86cd799439011" |
| datas[].is_enable | bool | 否 | true |
| datas[].is_result_display | bool | 否 | true |
| datas[].is_result_in_db | bool | 否 | true |
| datas[].apply_by_model_code | string | 否 | "" |
| datas[].apply_by_model_judge_rule_code | string | 否 | "" |
| datas[].is_same_as_model | bool | 否 | false |


#### CountJudgeRules4Edit

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeRuleQueryReq`
- **响应消息**: `GJudgeRuleCountReply`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | GSatRuleDbTarget | 否 | {"taskno":"TASK_A","satno":"SAT_01"} |
| target.taskno | string | 是 | "TASK_A" |
| target.satno | string | 是 | "SAT_01" |
| collection_type | GJudgeRuleCollection | 否 | GJudgeRuleCollection |
| rule_types | repeated string | 否 | [] |
| rule_group | string | 否 | "" |
| keyword | string | 否 | "" |
| param_text | string | 否 | "" |
| tree_filter_type | GTreeFilterType | 否 | TREE_FILTER_ALL |
| package_sys_id | int32 | 否 | 0 |
| parameter_id | int32 | 否 | 0 |
| parent_rule_codes | repeated string | 否 | [] |
| ref_para_ids | repeated string | 否 | [] |
| page_index | int32 | 否 | 0 |
| page_size | int32 | 否 | 20 |
| sort_field | string | 否 | "Code" |
| sort_asc | bool | 否 | true |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| total_count | int32 | 否 | 0 |


#### GetJudgeRule4EditByCode

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GRuleCodeReq`
- **响应消息**: `Gjudgeinfo`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | GSatRuleDbTarget | 否 | {"taskno":"TASK_A","satno":"SAT_01"} |
| target.taskno | string | 是 | "TASK_A" |
| target.satno | string | 是 | "SAT_01" |
| code | string | 是 | "JR001" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| code | string | 是 | "JR001" |
| desc | string | 否 | "" |
| group | string | 否 | "" |
| rule_type | string | 否 | "TypeA" |
| resultvaluetype | string | 否 | "int" |
| resultvaluedesc | string | 否 | "" |
| last_time | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| data | string | 否 | "{}" |
| statue | int32 | 否 | 0 |
| tags | repeated string | 否 | ["t1"] |
| id | string | 是 | "507f1f77bcf86cd799439011" |
| is_enable | bool | 否 | true |
| is_result_display | bool | 否 | true |
| is_result_in_db | bool | 否 | true |
| apply_by_model_code | string | 否 | "" |
| apply_by_model_judge_rule_code | string | 否 | "" |
| is_same_as_model | bool | 否 | false |


#### UpsertJudgeRule4Edit

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeRuleBatchUpsertReq`
- **响应消息**: `GEntityIdReq`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | GSatRuleDbTarget | 否 | {"taskno":"TASK_A","satno":"SAT_01"} |
| target.taskno | string | 是 | "TASK_A" |
| target.satno | string | 是 | "SAT_01" |
| collection_type | GJudgeRuleCollection | 否 | GJudgeRuleCollection |
| entities | repeated Gjudgeinfo | 否 | [] |
| entities[].code | string | 是 | "JR001" |
| entities[].desc | string | 否 | "" |
| entities[].group | string | 否 | "" |
| entities[].rule_type | string | 否 | "TypeA" |
| entities[].resultvaluetype | string | 否 | "int" |
| entities[].resultvaluedesc | string | 否 | "" |
| entities[].last_time | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| entities[].data | string | 否 | "{}" |
| entities[].statue | int32 | 否 | 0 |
| entities[].tags | repeated string | 否 | ["t1"] |
| entities[].id | string | 是 | "507f1f77bcf86cd799439011" |
| entities[].is_enable | bool | 否 | true |
| entities[].is_result_display | bool | 否 | true |
| entities[].is_result_in_db | bool | 否 | true |
| entities[].apply_by_model_code | string | 否 | "" |
| entities[].apply_by_model_judge_rule_code | string | 否 | "" |
| entities[].is_same_as_model | bool | 否 | false |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 是 | "507f1f77bcf86cd799439011" |


#### BatchUpsertJudgeRules4Edit

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeRuleBatchUpsertReq`
- **响应消息**: `google.protobuf.Empty`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | GSatRuleDbTarget | 否 | {"taskno":"TASK_A","satno":"SAT_01"} |
| target.taskno | string | 是 | "TASK_A" |
| target.satno | string | 是 | "SAT_01" |
| collection_type | GJudgeRuleCollection | 否 | GJudgeRuleCollection |
| entities | repeated Gjudgeinfo | 否 | [] |
| entities[].code | string | 是 | "JR001" |
| entities[].desc | string | 否 | "" |
| entities[].group | string | 否 | "" |
| entities[].rule_type | string | 否 | "TypeA" |
| entities[].resultvaluetype | string | 否 | "int" |
| entities[].resultvaluedesc | string | 否 | "" |
| entities[].last_time | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| entities[].data | string | 否 | "{}" |
| entities[].statue | int32 | 否 | 0 |
| entities[].tags | repeated string | 否 | ["t1"] |
| entities[].id | string | 是 | "507f1f77bcf86cd799439011" |
| entities[].is_enable | bool | 否 | true |
| entities[].is_result_display | bool | 否 | true |
| entities[].is_result_in_db | bool | 否 | true |
| entities[].apply_by_model_code | string | 否 | "" |
| entities[].apply_by_model_judge_rule_code | string | 否 | "" |
| entities[].is_same_as_model | bool | 否 | false |

**响应参数**
_无字段_


#### DeleteJudgeRule4Edit

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeRuleBatchDeleteReq`
- **响应消息**: `google.protobuf.Empty`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | GSatRuleDbTarget | 否 | {"taskno":"TASK_A","satno":"SAT_01"} |
| target.taskno | string | 是 | "TASK_A" |
| target.satno | string | 是 | "SAT_01" |
| collection_type | GJudgeRuleCollection | 否 | GJudgeRuleCollection |
| ids | repeated string | 否 | [100108] |
| codes | repeated string | 否 | [] |

**响应参数**
_无字段_


#### BatchDeleteJudgeRules4Edit

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeRuleBatchDeleteReq`
- **响应消息**: `google.protobuf.Empty`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | GSatRuleDbTarget | 否 | {"taskno":"TASK_A","satno":"SAT_01"} |
| target.taskno | string | 是 | "TASK_A" |
| target.satno | string | 是 | "SAT_01" |
| collection_type | GJudgeRuleCollection | 否 | GJudgeRuleCollection |
| ids | repeated string | 否 | [100108] |
| codes | repeated string | 否 | [] |

**响应参数**
_无字段_


#### DeleteAllJudgeRules4Edit

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeRuleBatchDeleteReq`
- **响应消息**: `google.protobuf.Empty`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | GSatRuleDbTarget | 否 | {"taskno":"TASK_A","satno":"SAT_01"} |
| target.taskno | string | 是 | "TASK_A" |
| target.satno | string | 是 | "SAT_01" |
| collection_type | GJudgeRuleCollection | 否 | GJudgeRuleCollection |
| ids | repeated string | 否 | [100108] |
| codes | repeated string | 否 | [] |

**响应参数**
_无字段_


#### PublishAllJudgeRules

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeRulePublishReq`
- **响应消息**: `google.protobuf.Empty`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | GSatRuleDbTarget | 否 | {"taskno":"TASK_A","satno":"SAT_01"} |
| target.taskno | string | 是 | "TASK_A" |
| target.satno | string | 是 | "SAT_01" |

**响应参数**
_无字段_


#### GetJudgeTags

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GSatRuleDbTarget`
- **响应消息**: `GJudgeTagCollection`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | repeated GJudgeTag | 否 | [] |
| datas[].id | string | 是 | "507f1f77bcf86cd799439011" |
| datas[].tag_name | string | 否 | "重要" |
| datas[].judge_codes | repeated string | 否 | ["JR001"] |
| datas[].enable | bool | 否 | true |


#### UpsertJudgeTag

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeTagUpsertReq`
- **响应消息**: `GEntityIdReq`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | GSatRuleDbTarget | 否 | {"taskno":"TASK_A","satno":"SAT_01"} |
| target.taskno | string | 是 | "TASK_A" |
| target.satno | string | 是 | "SAT_01" |
| entity | GJudgeTag | 否 | {} |
| entity.id | string | 是 | "507f1f77bcf86cd799439011" |
| entity.tag_name | string | 否 | "重要" |
| entity.judge_codes | repeated string | 否 | ["JR001"] |
| entity.enable | bool | 否 | true |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 是 | "507f1f77bcf86cd799439011" |


#### DeleteJudgeTag

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeTagDeleteReq`
- **响应消息**: `google.protobuf.Empty`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | GSatRuleDbTarget | 否 | {"taskno":"TASK_A","satno":"SAT_01"} |
| target.taskno | string | 是 | "TASK_A" |
| target.satno | string | 是 | "SAT_01" |
| id | string | 是 | "507f1f77bcf86cd799439011" |

**响应参数**
_无字段_


#### GetJudgeRules

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GSatRuleDbTarget`
- **响应消息**: `GJsonEntityCollection`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | repeated GJsonEntity | 否 | [] |
| datas[].id | string | 是 | "507f1f77bcf86cd799439011" |
| datas[].data | string | 否 | "{}" |


#### GetJudgeRuleByCode

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GRuleCodeReq`
- **响应消息**: `GJsonEntity`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | GSatRuleDbTarget | 否 | {"taskno":"TASK_A","satno":"SAT_01"} |
| target.taskno | string | 是 | "TASK_A" |
| target.satno | string | 是 | "SAT_01" |
| code | string | 是 | "JR001" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 是 | "507f1f77bcf86cd799439011" |
| data | string | 否 | "{}" |


#### UpsertJudgeRule

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeRuleUpsertReq`
- **响应消息**: `GEntityIdReq`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | GSatRuleDbTarget | 否 | {"taskno":"TASK_A","satno":"SAT_01"} |
| target.taskno | string | 是 | "TASK_A" |
| target.satno | string | 是 | "SAT_01" |
| entity | GJsonEntity | 否 | {} |
| entity.id | string | 是 | "507f1f77bcf86cd799439011" |
| entity.data | string | 否 | "{}" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 是 | "507f1f77bcf86cd799439011" |


#### DeleteJudgeRule

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeRuleDeleteReq`
- **响应消息**: `google.protobuf.Empty`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | GSatRuleDbTarget | 否 | {"taskno":"TASK_A","satno":"SAT_01"} |
| target.taskno | string | 是 | "TASK_A" |
| target.satno | string | 是 | "SAT_01" |
| id | string | 是 | "507f1f77bcf86cd799439011" |

**响应参数**
_无字段_



### 5.2 MassDataJudgeServer（30 个 RPC）

判读专用服务，RPC 与 `MassDataServer` 中判读管理部分一一对应，便于客户端按需接入。下列参数表完整展开（不省略）。

#### GetJudgeMgrSatConfigs

- **服务**: `MassDataJudgeServer`
- **调用类型**: Unary
- **请求消息**: `google.protobuf.Empty`
- **响应消息**: `GJudgeMgrSatConfigCollection`

**请求参数**
_无字段_

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | repeated GJudgeMgrSatConfig | 否 | [] |
| datas[].id | string | 是 | "507f1f77bcf86cd799439011" |
| datas[].ver | string | 否 | "1" |
| datas[].task_name | string | 否 | "" |
| datas[].sat_no | string | 否 | "SAT_01" |
| datas[].sat_name | string | 否 | "" |
| datas[].stage | string | 否 | "在轨" |
| datas[].bdb_ip | string | 否 | "" |
| datas[].bdb_port | int32 | 否 | 0 |
| datas[].db_name | string | 否 | "" |
| datas[].bdb_user_name | string | 否 | "" |
| datas[].bdb_user_psw | string | 否 | "" |
| datas[].judge_db_ip | string | 否 | "" |
| datas[].judge_db_port | int32 | 否 | 0 |
| datas[].judge_db_user_name | string | 否 | "" |
| datas[].judge_db_psw | string | 否 | "" |
| datas[].judge_db_name | string | 否 | "" |
| datas[].judge_sat_group_id | string | 否 | "..." |


#### UpsertJudgeMgrSatConfig

- **服务**: `MassDataJudgeServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeMgrSatConfigUpsertReq`
- **响应消息**: `GEntityIdReq`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| entity | GJudgeMgrSatConfig | 否 | {} |
| entity.id | string | 是 | "507f1f77bcf86cd799439011" |
| entity.ver | string | 否 | "1" |
| entity.task_name | string | 否 | "" |
| entity.sat_no | string | 否 | "SAT_01" |
| entity.sat_name | string | 否 | "" |
| entity.stage | string | 否 | "在轨" |
| entity.bdb_ip | string | 否 | "" |
| entity.bdb_port | int32 | 否 | 0 |
| entity.db_name | string | 否 | "" |
| entity.bdb_user_name | string | 否 | "" |
| entity.bdb_user_psw | string | 否 | "" |
| entity.judge_db_ip | string | 否 | "" |
| entity.judge_db_port | int32 | 否 | 0 |
| entity.judge_db_user_name | string | 否 | "" |
| entity.judge_db_psw | string | 否 | "" |
| entity.judge_db_name | string | 否 | "" |
| entity.judge_sat_group_id | string | 否 | "..." |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 是 | "507f1f77bcf86cd799439011" |


#### DeleteJudgeMgrSatConfig

- **服务**: `MassDataJudgeServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeMgrSatConfigDeleteReq`
- **响应消息**: `google.protobuf.Empty`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 是 | "507f1f77bcf86cd799439011" |

**响应参数**
_无字段_


#### GetJudgeSatGroups

- **服务**: `MassDataJudgeServer`
- **调用类型**: Unary
- **请求消息**: `google.protobuf.Empty`
- **响应消息**: `GJudgeSatGroupCollection`

**请求参数**
_无字段_

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | repeated GJudgeSatGroup | 否 | [] |
| datas[].id | string | 是 | "507f1f77bcf86cd799439011" |
| datas[].name | string | 否 | "分组1" |
| datas[].sat_members_str | string | 否 | "SAT_01,SAT_02" |
| datas[].sat_templates_str | string | 否 | "" |


#### QueryJudgeSatGroups

- **服务**: `MassDataJudgeServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeSatGroupQueryReq`
- **响应消息**: `GJudgeSatGroupPageReply`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| keyword | string | 否 | "" |
| page_index | int32 | 否 | 0 |
| page_size | int32 | 否 | 20 |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| total_count | int32 | 否 | 0 |
| datas | repeated GJudgeSatGroup | 否 | [] |
| datas[].id | string | 是 | "507f1f77bcf86cd799439011" |
| datas[].name | string | 否 | "分组1" |
| datas[].sat_members_str | string | 否 | "SAT_01,SAT_02" |
| datas[].sat_templates_str | string | 否 | "" |


#### UpsertJudgeSatGroup

- **服务**: `MassDataJudgeServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeSatGroupUpsertReq`
- **响应消息**: `GEntityIdReq`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| entity | GJudgeSatGroup | 否 | {} |
| entity.id | string | 是 | "507f1f77bcf86cd799439011" |
| entity.name | string | 否 | "分组1" |
| entity.sat_members_str | string | 否 | "SAT_01,SAT_02" |
| entity.sat_templates_str | string | 否 | "" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 是 | "507f1f77bcf86cd799439011" |


#### DeleteJudgeSatGroup

- **服务**: `MassDataJudgeServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeSatGroupDeleteReq`
- **响应消息**: `google.protobuf.Empty`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 是 | "507f1f77bcf86cd799439011" |

**响应参数**
_无字段_


#### GetJudgeRuleGroups

- **服务**: `MassDataJudgeServer`
- **调用类型**: Unary
- **请求消息**: `google.protobuf.Empty`
- **响应消息**: `GJudgeRuleGroupCollection`

**请求参数**
_无字段_

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | repeated GJudgeRuleGroup | 否 | [] |
| datas[].id | string | 是 | "507f1f77bcf86cd799439011" |
| datas[].name | string | 否 | "分组1" |
| datas[].name_ch | string | 否 | "分组一" |
| datas[].desc | string | 否 | "" |


#### UpsertJudgeRuleGroup

- **服务**: `MassDataJudgeServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeRuleGroupUpsertReq`
- **响应消息**: `GEntityIdReq`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| entity | GJudgeRuleGroup | 否 | {} |
| entity.id | string | 是 | "507f1f77bcf86cd799439011" |
| entity.name | string | 否 | "分组1" |
| entity.name_ch | string | 否 | "分组一" |
| entity.desc | string | 否 | "" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 是 | "507f1f77bcf86cd799439011" |


#### DeleteJudgeRuleGroup

- **服务**: `MassDataJudgeServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeRuleGroupDeleteReq`
- **响应消息**: `google.protobuf.Empty`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 是 | "507f1f77bcf86cd799439011" |

**响应参数**
_无字段_


#### GetJudgeSatTemplates

- **服务**: `MassDataJudgeServer`
- **调用类型**: Unary
- **请求消息**: `google.protobuf.Empty`
- **响应消息**: `GJudgeSatTemplateCollection`

**请求参数**
_无字段_

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | repeated GJudgeSatTemplate | 否 | [] |
| datas[].id | string | 是 | "507f1f77bcf86cd799439011" |
| datas[].name | string | 否 | "分组1" |
| datas[].source_ver | string | 否 | "1" |
| datas[].source_sat_name | string | 否 | "SAT_01" |
| datas[].source_stage | string | 否 | "在轨" |
| datas[].desc | string | 否 | "" |
| datas[].tag | repeated string | 否 | ["t1"] |
| datas[].judge_sat_group_id | string | 否 | "..." |
| datas[].judge_rule_models | repeated GJudgeRuleTemplate | 否 | [] |
| datas[].para_template_infos | repeated GParaTemplateInfo | 否 | [] |
| datas[].cmd_template_infos | repeated GCmdTemplateInfo | 否 | [] |
| datas[].pkg_template_infos | repeated GPkgTemplateInfo | 否 | [] |
| datas[].create_time | Timestamp | 否 | "2026-07-02T00:00:00Z" |


#### QueryJudgeSatTemplates

- **服务**: `MassDataJudgeServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeSatTemplateQueryReq`
- **响应消息**: `GJudgeSatTemplatePageReply`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| keyword | string | 否 | "" |
| page_index | int32 | 否 | 0 |
| page_size | int32 | 否 | 20 |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| total_count | int32 | 否 | 0 |
| datas | repeated GJudgeSatTemplate | 否 | [] |
| datas[].id | string | 是 | "507f1f77bcf86cd799439011" |
| datas[].name | string | 否 | "分组1" |
| datas[].source_ver | string | 否 | "1" |
| datas[].source_sat_name | string | 否 | "SAT_01" |
| datas[].source_stage | string | 否 | "在轨" |
| datas[].desc | string | 否 | "" |
| datas[].tag | repeated string | 否 | ["t1"] |
| datas[].judge_sat_group_id | string | 否 | "..." |
| datas[].judge_rule_models | repeated GJudgeRuleTemplate | 否 | [] |
| datas[].para_template_infos | repeated GParaTemplateInfo | 否 | [] |
| datas[].cmd_template_infos | repeated GCmdTemplateInfo | 否 | [] |
| datas[].pkg_template_infos | repeated GPkgTemplateInfo | 否 | [] |
| datas[].create_time | Timestamp | 否 | "2026-07-02T00:00:00Z" |


#### UpsertJudgeSatTemplate

- **服务**: `MassDataJudgeServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeSatTemplateUpsertReq`
- **响应消息**: `GEntityIdReq`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| entity | GJudgeSatTemplate | 否 | {} |
| entity.id | string | 是 | "507f1f77bcf86cd799439011" |
| entity.name | string | 否 | "分组1" |
| entity.source_ver | string | 否 | "1" |
| entity.source_sat_name | string | 否 | "SAT_01" |
| entity.source_stage | string | 否 | "在轨" |
| entity.desc | string | 否 | "" |
| entity.tag | repeated string | 否 | ["t1"] |
| entity.judge_sat_group_id | string | 否 | "..." |
| entity.judge_rule_models | repeated GJudgeRuleTemplate | 否 | [] |
| entity.para_template_infos | repeated GParaTemplateInfo | 否 | [] |
| entity.cmd_template_infos | repeated GCmdTemplateInfo | 否 | [] |
| entity.pkg_template_infos | repeated GPkgTemplateInfo | 否 | [] |
| entity.create_time | Timestamp | 否 | "2026-07-02T00:00:00Z" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 是 | "507f1f77bcf86cd799439011" |


#### DeleteJudgeSatTemplate

- **服务**: `MassDataJudgeServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeSatTemplateDeleteReq`
- **响应消息**: `google.protobuf.Empty`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 是 | "507f1f77bcf86cd799439011" |

**响应参数**
_无字段_


#### QueryJudgeRules4Edit

- **服务**: `MassDataJudgeServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeRuleQueryReq`
- **响应消息**: `GJudgeRulePageReply`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | GSatRuleDbTarget | 否 | {"taskno":"TASK_A","satno":"SAT_01"} |
| target.taskno | string | 是 | "TASK_A" |
| target.satno | string | 是 | "SAT_01" |
| collection_type | GJudgeRuleCollection | 否 | GJudgeRuleCollection |
| rule_types | repeated string | 否 | [] |
| rule_group | string | 否 | "" |
| keyword | string | 否 | "" |
| param_text | string | 否 | "" |
| tree_filter_type | GTreeFilterType | 否 | TREE_FILTER_ALL |
| package_sys_id | int32 | 否 | 0 |
| parameter_id | int32 | 否 | 0 |
| parent_rule_codes | repeated string | 否 | [] |
| ref_para_ids | repeated string | 否 | [] |
| page_index | int32 | 否 | 0 |
| page_size | int32 | 否 | 20 |
| sort_field | string | 否 | "Code" |
| sort_asc | bool | 否 | true |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| total_count | int32 | 否 | 0 |
| datas | repeated Gjudgeinfo | 否 | [] |
| datas[].code | string | 是 | "JR001" |
| datas[].desc | string | 否 | "" |
| datas[].group | string | 否 | "" |
| datas[].rule_type | string | 否 | "TypeA" |
| datas[].resultvaluetype | string | 否 | "int" |
| datas[].resultvaluedesc | string | 否 | "" |
| datas[].last_time | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| datas[].data | string | 否 | "{}" |
| datas[].statue | int32 | 否 | 0 |
| datas[].tags | repeated string | 否 | ["t1"] |
| datas[].id | string | 是 | "507f1f77bcf86cd799439011" |
| datas[].is_enable | bool | 否 | true |
| datas[].is_result_display | bool | 否 | true |
| datas[].is_result_in_db | bool | 否 | true |
| datas[].apply_by_model_code | string | 否 | "" |
| datas[].apply_by_model_judge_rule_code | string | 否 | "" |
| datas[].is_same_as_model | bool | 否 | false |


#### CountJudgeRules4Edit

- **服务**: `MassDataJudgeServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeRuleQueryReq`
- **响应消息**: `GJudgeRuleCountReply`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | GSatRuleDbTarget | 否 | {"taskno":"TASK_A","satno":"SAT_01"} |
| target.taskno | string | 是 | "TASK_A" |
| target.satno | string | 是 | "SAT_01" |
| collection_type | GJudgeRuleCollection | 否 | GJudgeRuleCollection |
| rule_types | repeated string | 否 | [] |
| rule_group | string | 否 | "" |
| keyword | string | 否 | "" |
| param_text | string | 否 | "" |
| tree_filter_type | GTreeFilterType | 否 | TREE_FILTER_ALL |
| package_sys_id | int32 | 否 | 0 |
| parameter_id | int32 | 否 | 0 |
| parent_rule_codes | repeated string | 否 | [] |
| ref_para_ids | repeated string | 否 | [] |
| page_index | int32 | 否 | 0 |
| page_size | int32 | 否 | 20 |
| sort_field | string | 否 | "Code" |
| sort_asc | bool | 否 | true |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| total_count | int32 | 否 | 0 |


#### GetJudgeRule4EditByCode

- **服务**: `MassDataJudgeServer`
- **调用类型**: Unary
- **请求消息**: `GRuleCodeReq`
- **响应消息**: `Gjudgeinfo`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | GSatRuleDbTarget | 否 | {"taskno":"TASK_A","satno":"SAT_01"} |
| target.taskno | string | 是 | "TASK_A" |
| target.satno | string | 是 | "SAT_01" |
| code | string | 是 | "JR001" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| code | string | 是 | "JR001" |
| desc | string | 否 | "" |
| group | string | 否 | "" |
| rule_type | string | 否 | "TypeA" |
| resultvaluetype | string | 否 | "int" |
| resultvaluedesc | string | 否 | "" |
| last_time | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| data | string | 否 | "{}" |
| statue | int32 | 否 | 0 |
| tags | repeated string | 否 | ["t1"] |
| id | string | 是 | "507f1f77bcf86cd799439011" |
| is_enable | bool | 否 | true |
| is_result_display | bool | 否 | true |
| is_result_in_db | bool | 否 | true |
| apply_by_model_code | string | 否 | "" |
| apply_by_model_judge_rule_code | string | 否 | "" |
| is_same_as_model | bool | 否 | false |


#### UpsertJudgeRule4Edit

- **服务**: `MassDataJudgeServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeRuleBatchUpsertReq`
- **响应消息**: `GEntityIdReq`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | GSatRuleDbTarget | 否 | {"taskno":"TASK_A","satno":"SAT_01"} |
| target.taskno | string | 是 | "TASK_A" |
| target.satno | string | 是 | "SAT_01" |
| collection_type | GJudgeRuleCollection | 否 | GJudgeRuleCollection |
| entities | repeated Gjudgeinfo | 否 | [] |
| entities[].code | string | 是 | "JR001" |
| entities[].desc | string | 否 | "" |
| entities[].group | string | 否 | "" |
| entities[].rule_type | string | 否 | "TypeA" |
| entities[].resultvaluetype | string | 否 | "int" |
| entities[].resultvaluedesc | string | 否 | "" |
| entities[].last_time | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| entities[].data | string | 否 | "{}" |
| entities[].statue | int32 | 否 | 0 |
| entities[].tags | repeated string | 否 | ["t1"] |
| entities[].id | string | 是 | "507f1f77bcf86cd799439011" |
| entities[].is_enable | bool | 否 | true |
| entities[].is_result_display | bool | 否 | true |
| entities[].is_result_in_db | bool | 否 | true |
| entities[].apply_by_model_code | string | 否 | "" |
| entities[].apply_by_model_judge_rule_code | string | 否 | "" |
| entities[].is_same_as_model | bool | 否 | false |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 是 | "507f1f77bcf86cd799439011" |


#### BatchUpsertJudgeRules4Edit

- **服务**: `MassDataJudgeServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeRuleBatchUpsertReq`
- **响应消息**: `google.protobuf.Empty`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | GSatRuleDbTarget | 否 | {"taskno":"TASK_A","satno":"SAT_01"} |
| target.taskno | string | 是 | "TASK_A" |
| target.satno | string | 是 | "SAT_01" |
| collection_type | GJudgeRuleCollection | 否 | GJudgeRuleCollection |
| entities | repeated Gjudgeinfo | 否 | [] |
| entities[].code | string | 是 | "JR001" |
| entities[].desc | string | 否 | "" |
| entities[].group | string | 否 | "" |
| entities[].rule_type | string | 否 | "TypeA" |
| entities[].resultvaluetype | string | 否 | "int" |
| entities[].resultvaluedesc | string | 否 | "" |
| entities[].last_time | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| entities[].data | string | 否 | "{}" |
| entities[].statue | int32 | 否 | 0 |
| entities[].tags | repeated string | 否 | ["t1"] |
| entities[].id | string | 是 | "507f1f77bcf86cd799439011" |
| entities[].is_enable | bool | 否 | true |
| entities[].is_result_display | bool | 否 | true |
| entities[].is_result_in_db | bool | 否 | true |
| entities[].apply_by_model_code | string | 否 | "" |
| entities[].apply_by_model_judge_rule_code | string | 否 | "" |
| entities[].is_same_as_model | bool | 否 | false |

**响应参数**
_无字段_


#### DeleteJudgeRule4Edit

- **服务**: `MassDataJudgeServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeRuleBatchDeleteReq`
- **响应消息**: `google.protobuf.Empty`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | GSatRuleDbTarget | 否 | {"taskno":"TASK_A","satno":"SAT_01"} |
| target.taskno | string | 是 | "TASK_A" |
| target.satno | string | 是 | "SAT_01" |
| collection_type | GJudgeRuleCollection | 否 | GJudgeRuleCollection |
| ids | repeated string | 否 | [100108] |
| codes | repeated string | 否 | [] |

**响应参数**
_无字段_


#### BatchDeleteJudgeRules4Edit

- **服务**: `MassDataJudgeServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeRuleBatchDeleteReq`
- **响应消息**: `google.protobuf.Empty`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | GSatRuleDbTarget | 否 | {"taskno":"TASK_A","satno":"SAT_01"} |
| target.taskno | string | 是 | "TASK_A" |
| target.satno | string | 是 | "SAT_01" |
| collection_type | GJudgeRuleCollection | 否 | GJudgeRuleCollection |
| ids | repeated string | 否 | [100108] |
| codes | repeated string | 否 | [] |

**响应参数**
_无字段_


#### DeleteAllJudgeRules4Edit

- **服务**: `MassDataJudgeServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeRuleBatchDeleteReq`
- **响应消息**: `google.protobuf.Empty`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | GSatRuleDbTarget | 否 | {"taskno":"TASK_A","satno":"SAT_01"} |
| target.taskno | string | 是 | "TASK_A" |
| target.satno | string | 是 | "SAT_01" |
| collection_type | GJudgeRuleCollection | 否 | GJudgeRuleCollection |
| ids | repeated string | 否 | [100108] |
| codes | repeated string | 否 | [] |

**响应参数**
_无字段_


#### PublishAllJudgeRules

- **服务**: `MassDataJudgeServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeRulePublishReq`
- **响应消息**: `google.protobuf.Empty`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | GSatRuleDbTarget | 否 | {"taskno":"TASK_A","satno":"SAT_01"} |
| target.taskno | string | 是 | "TASK_A" |
| target.satno | string | 是 | "SAT_01" |

**响应参数**
_无字段_


#### GetJudgeTags

- **服务**: `MassDataJudgeServer`
- **调用类型**: Unary
- **请求消息**: `GSatRuleDbTarget`
- **响应消息**: `GJudgeTagCollection`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | repeated GJudgeTag | 否 | [] |
| datas[].id | string | 是 | "507f1f77bcf86cd799439011" |
| datas[].tag_name | string | 否 | "重要" |
| datas[].judge_codes | repeated string | 否 | ["JR001"] |
| datas[].enable | bool | 否 | true |


#### UpsertJudgeTag

- **服务**: `MassDataJudgeServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeTagUpsertReq`
- **响应消息**: `GEntityIdReq`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | GSatRuleDbTarget | 否 | {"taskno":"TASK_A","satno":"SAT_01"} |
| target.taskno | string | 是 | "TASK_A" |
| target.satno | string | 是 | "SAT_01" |
| entity | GJudgeTag | 否 | {} |
| entity.id | string | 是 | "507f1f77bcf86cd799439011" |
| entity.tag_name | string | 否 | "重要" |
| entity.judge_codes | repeated string | 否 | ["JR001"] |
| entity.enable | bool | 否 | true |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 是 | "507f1f77bcf86cd799439011" |


#### DeleteJudgeTag

- **服务**: `MassDataJudgeServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeTagDeleteReq`
- **响应消息**: `google.protobuf.Empty`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | GSatRuleDbTarget | 否 | {"taskno":"TASK_A","satno":"SAT_01"} |
| target.taskno | string | 是 | "TASK_A" |
| target.satno | string | 是 | "SAT_01" |
| id | string | 是 | "507f1f77bcf86cd799439011" |

**响应参数**
_无字段_


#### GetJudgeRules

- **服务**: `MassDataJudgeServer`
- **调用类型**: Unary
- **请求消息**: `GSatRuleDbTarget`
- **响应消息**: `GJsonEntityCollection`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| datas | repeated GJsonEntity | 否 | [] |
| datas[].id | string | 是 | "507f1f77bcf86cd799439011" |
| datas[].data | string | 否 | "{}" |


#### GetJudgeRuleByCode

- **服务**: `MassDataJudgeServer`
- **调用类型**: Unary
- **请求消息**: `GRuleCodeReq`
- **响应消息**: `GJsonEntity`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | GSatRuleDbTarget | 否 | {"taskno":"TASK_A","satno":"SAT_01"} |
| target.taskno | string | 是 | "TASK_A" |
| target.satno | string | 是 | "SAT_01" |
| code | string | 是 | "JR001" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 是 | "507f1f77bcf86cd799439011" |
| data | string | 否 | "{}" |


#### UpsertJudgeRule

- **服务**: `MassDataJudgeServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeRuleUpsertReq`
- **响应消息**: `GEntityIdReq`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | GSatRuleDbTarget | 否 | {"taskno":"TASK_A","satno":"SAT_01"} |
| target.taskno | string | 是 | "TASK_A" |
| target.satno | string | 是 | "SAT_01" |
| entity | GJsonEntity | 否 | {} |
| entity.id | string | 是 | "507f1f77bcf86cd799439011" |
| entity.data | string | 否 | "{}" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| id | string | 是 | "507f1f77bcf86cd799439011" |


#### DeleteJudgeRule

- **服务**: `MassDataJudgeServer`
- **调用类型**: Unary
- **请求消息**: `GJudgeRuleDeleteReq`
- **响应消息**: `google.protobuf.Empty`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| target | GSatRuleDbTarget | 否 | {"taskno":"TASK_A","satno":"SAT_01"} |
| target.taskno | string | 是 | "TASK_A" |
| target.satno | string | 是 | "SAT_01" |
| id | string | 是 | "507f1f77bcf86cd799439011" |

**响应参数**
_无字段_



---

## 6. gRPC v1 详细接口说明

> 包名 `MassPlatform`。卫星主键含 `dbstage`（`gSatliteThumb.dbstage`）。
> Proto 来源：`MassServerProtos/v1/MassDataServer.proto`、`DataReceiveServer.proto`、`MassModels.proto`。
> v1 查询条件消息均含 `dbstage` 字段；`gRitsReq`、`gDatafile` 定义于 `DataReceiveServer.proto`。

### 6.1 MassDataServer（16 个 RPC）

#### GetAllSatsFromMassServer

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `google.protobuf.Empty`
- **响应消息**: `gSatCollectionReply`

**请求参数**
_无字段_

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| Datas | repeated gSatliteThumb | 否 | [] |
| Datas[].taskno | string | 是 | "TASK_A" |
| Datas[].taskname | string | 否 | "任务A" |
| Datas[].dbstage | string | 否 | "在轨" |
| Datas[].satno | string | 是 | "SAT_01" |
| Datas[].satname | string | 否 | "卫星01" |
| Datas[].status | int32 | 否 | 0 |


#### GetSatCfg

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `gSatliteThumb`
- **响应消息**: `satconncfg`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| taskname | string | 否 | "任务A" |
| dbstage | string | 否 | "在轨" |
| satno | string | 是 | "SAT_01" |
| satname | string | 否 | "卫星01" |
| status | int32 | 否 | 0 |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| basicconn | string | 否 | "mongodb://..." |
| mongoqueryconn | string | 否 | "" |
| judgeconn | string | 否 | "" |
| judgemgrconn | string | 否 | "" |
| analysisconn | string | 否 | "" |
| displayconn | string | 否 | "" |
| mqtturl | string | 否 | "" |
| signalrurl | string | 否 | "" |
| cfgconn | string | 否 | "" |


#### GetMessageQueueInfo

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `gSatliteThumb`
- **响应消息**: `gMsgQueueInfoReply`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| taskname | string | 否 | "任务A" |
| dbstage | string | 否 | "在轨" |
| satno | string | 是 | "SAT_01" |
| satname | string | 否 | "卫星01" |
| status | int32 | 否 | 0 |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| url | string | 否 | "amqp://..." |
| exchangetype | string | 否 | "direct" |
| exchangename | string | 否 | "mass.exchange" |
| queues | repeated gMsgQueueItem | 否 | [] |
| queues[].key | string | 否 | "param" |
| queues[].queuename | string | 否 | "param.queue" |
| queues[].exchangename | string | 否 | "mass.exchange" |


#### GetRedisInfo

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `gSatliteThumb`
- **响应消息**: `gRedisInfoReply`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| taskname | string | 否 | "任务A" |
| dbstage | string | 否 | "在轨" |
| satno | string | 是 | "SAT_01" |
| satname | string | 否 | "卫星01" |
| status | int32 | 否 | 0 |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| url | string | 否 | "amqp://..." |
| pwd | string | 否 | "" |
| dbindex | int32 | 否 | 0 |
| keys | repeated string | 否 | ["key1"] |


#### GetBasicDbPara

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `gSatliteThumb`
- **响应消息**: `gView_Paras`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| taskname | string | 否 | "任务A" |
| dbstage | string | 否 | "在轨" |
| satno | string | 是 | "SAT_01" |
| satname | string | 否 | "卫星01" |
| status | int32 | 否 | 0 |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| Datas | repeated view_para | 否 | [] |
| Datas[].PARA_ID | int32 | 否 | 100108 |
| Datas[].PRM_SYS_ID | int32 | 否 | 0 |
| Datas[].PARA_CODE | string | 否 | "" |
| Datas[].PARA_TYPE | int32 | 否 | 0 |
| Datas[].PARA_TYPE_CHAR | string | 否 | "" |
| Datas[].PARA_TYPE_DESC | string | 否 | "" |
| Datas[].PARA_DESC | string | 否 | "" |
| Datas[].MIN_VALUE | double | 否 | 0.0 |
| Datas[].MAX_VALUE | double | 否 | 0.0 |
| Datas[].UPDATE_TIME | int32 | 否 | 0 |
| Datas[].VALUE_DESC | string | 否 | "" |
| Datas[].VALID_FLAG | int32 | 否 | 0 |
| Datas[].WATCH_FLAG | int32 | 否 | 0 |
| Datas[].PARAMETER_TYPE | int32 | 否 | 0 |
| Datas[].EDIT_GROUP | string | 否 | "" |
| Datas[].PROC_ID | int32 | 否 | 0 |
| Datas[].PROC_DESC | string | 否 | "" |
| Datas[].PARA_MASK | string | 否 | "" |


#### GetBasicDbCmd

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `gSatliteThumb`
- **响应消息**: `gView_Cmds`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| taskname | string | 否 | "任务A" |
| dbstage | string | 否 | "在轨" |
| satno | string | 是 | "SAT_01" |
| satname | string | 否 | "卫星01" |
| status | int32 | 否 | 0 |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| Datas | repeated view_cmd | 否 | [] |
| Datas[].CMD_ID | int32 | 否 | 101 |
| Datas[].CMD_SYS_ID | int32 | 否 | 0 |
| Datas[].CMD_CODE | string | 否 | "" |
| Datas[].CMD_TYPE | int32 | 否 | 0 |
| Datas[].CMD_DESC | string | 否 | "" |
| Datas[].CMD_LEN | int32 | 否 | 0 |
| Datas[].CMD_DATA | string | 否 | "" |
| Datas[].EXE_TIME | int32 | 否 | 0 |
| Datas[].CMD_LEVEL | int32 | 否 | 0 |
| Datas[].VALID_FLAG | int32 | 否 | 0 |
| Datas[].IS_STARMIDDLE_CMD | bool | 否 | false |
| Datas[].SINGNL | string | 否 | "" |
| Datas[].ALLOW_CHECK_DATA | bool | 否 | false |
| Datas[].EDIT_GROUP | string | 否 | "" |
| Datas[].BUSINESS_GROUP | string | 否 | "" |


#### GetBasicDbPkg

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `gSatliteThumb`
- **响应消息**: `gView_Pkgs`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| taskname | string | 否 | "任务A" |
| dbstage | string | 否 | "在轨" |
| satno | string | 是 | "SAT_01" |
| satname | string | 否 | "卫星01" |
| status | int32 | 否 | 0 |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| Datas | repeated view_pkg | 否 | [] |
| Datas[].SYS_ID | int32 | 否 | 37 |
| Datas[].PKG_FLAG | string | 否 | "" |
| Datas[].PKG_LEN | int32 | 否 | 0 |
| Datas[].SUB_FLAG | string | 否 | "" |
| Datas[].PKG_DESC | string | 否 | "" |
| Datas[].UPDATE_TIME | int32 | 否 | 0 |
| Datas[].VALID_FLAG | int32 | 否 | 0 |
| Datas[].PKG_FLAG_ASSIST | string | 否 | "" |
| Datas[].SYS_CODE | string | 否 | "" |


#### GetBasicDbRelation

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `gSatliteThumb`
- **响应消息**: `gView_Sys_Relates`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| taskname | string | 否 | "任务A" |
| dbstage | string | 否 | "在轨" |
| satno | string | 是 | "SAT_01" |
| satname | string | 否 | "卫星01" |
| status | int32 | 否 | 0 |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| PkgHirberDatas | repeated view_sys_relation | 否 | [] |
| PkgHirberDatas[].SYS_ID | int32 | 否 | 37 |
| PkgHirberDatas[].SYS_CODE | string | 否 | "" |
| PkgHirberDatas[].SYS_DESC | string | 否 | "" |
| PkgHirberDatas[].FATHER_SYS_ID | int32 | 否 | 0 |
| PkgHirberDatas[].LEVEL | int32 | 否 | 0 |
| PkgHirberDatas[].SYS_TYPE | int32 | 否 | 0 |
| CmdHirberDatas | repeated view_sys_relation | 否 | [] |
| CmdHirberDatas[].SYS_ID | int32 | 否 | 37 |
| CmdHirberDatas[].SYS_CODE | string | 否 | "" |
| CmdHirberDatas[].SYS_DESC | string | 否 | "" |
| CmdHirberDatas[].FATHER_SYS_ID | int32 | 否 | 0 |
| CmdHirberDatas[].LEVEL | int32 | 否 | 0 |
| CmdHirberDatas[].SYS_TYPE | int32 | 否 | 0 |


#### GetBasicDbCmdJudges

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `gSatliteThumb`
- **响应消息**: `gView_Cmdjudges`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| taskname | string | 否 | "任务A" |
| dbstage | string | 否 | "在轨" |
| satno | string | 是 | "SAT_01" |
| satname | string | 否 | "卫星01" |
| status | int32 | 否 | 0 |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| Datas | repeated view_cmdjudge | 否 | [] |
| Datas[].CMD_ID | int32 | 否 | 101 |
| Datas[].JUDGE_ID | int32 | 否 | 0 |
| Datas[].PARA_ID | int32 | 否 | 100108 |
| Datas[].JUDGE_TYPE | int32 | 否 | 0 |
| Datas[].I_DN_VALUE | string | 否 | "" |
| Datas[].I_UP_VALUE | string | 否 | "" |
| Datas[].R_DN_VALUE | string | 否 | "" |
| Datas[].R_UP_VALUE | string | 否 | "" |
| Datas[].V_DN_VALUE | string | 否 | "" |
| Datas[].V_UP_VALUE | string | 否 | "" |
| Datas[].JUDGE_TIME | int32 | 否 | 0 |


#### QuerygPara

- **服务**: `MassDataServer`
- **调用类型**: Server streaming
- **请求消息**: `gQueryCondParaReq`
- **响应消息**: `stream gParaCollect`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |
| dbstage | string | 否 | "在轨" |
| fromdt | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| todt | Timestamp | 否 | "2026-07-02T01:00:00Z" |
| pkgparaids | repeated gPkgParaRelate | 否 | [] |
| pkgparaids[].pid | int32 | 否 | 37 |
| pkgparaids[].ids | repeated int32 | 否 | [100108] |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| Datas | repeated gpara | 否 | [] |
| Datas[].id | int32 | 是 | "507f1f77bcf86cd799439011" |
| Datas[].pid | int32 | 否 | 37 |
| Datas[].pv | string | 否 | 25.6 |
| Datas[].sv | string | 否 | "..." |
| Datas[].pd | string | 否 | "" |
| Datas[].dt | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| Datas[].st | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| Datas[].DtTicks | int64 | 否 | 0 |
| Datas[].Pc | string | 否 | "P001" |
| Datas[].SatNo | string | 是 | "SAT_01" |
| Datas[].TaskNo | string | 是 | "TASK_A" |


#### QueryParaConformity

- **服务**: `MassDataServer`
- **调用类型**: Server streaming
- **请求消息**: `gQueryCondParaReq`
- **响应消息**: `stream gparaConformityResult`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |
| dbstage | string | 否 | "在轨" |
| fromdt | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| todt | Timestamp | 否 | "2026-07-02T01:00:00Z" |
| pkgparaids | repeated gPkgParaRelate | 否 | [] |
| pkgparaids[].pid | int32 | 否 | 37 |
| pkgparaids[].ids | repeated int32 | 否 | [100108] |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| SatelliteNo | string | 否 | "" |
| Datas | repeated gparaConformity | 否 | [] |
| Datas[].time | Timestamp | 否 | "2026-07-02T00:01:00Z" |
| Datas[].instdatas | repeated gindicatorSimple | 否 | [] |
| Datas[].paradatas | repeated gparaSimple | 否 | [] |


#### QuerygInst

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `gQueryCondInstReq`
- **响应消息**: `gInstCollect`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |
| dbstage | string | 否 | "在轨" |
| fromdt | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| todt | Timestamp | 否 | "2026-07-02T01:00:00Z" |
| instids | repeated int32 | 否 | [101] |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| Datas | repeated gindicator | 否 | [] |
| Datas[].ci | int32 | 否 | 101 |
| Datas[].cc | string | 否 | "CMD01" |
| Datas[].cd | string | 否 | "AA BB" |
| Datas[].cn | string | 否 | "指令名" |
| Datas[].et | Timestamp | 否 | "2026-07-02T00:01:00Z" |
| Datas[].c_judgeinfo | string | 否 | "" |
| Datas[].RelativeParas | repeated gpara | 否 | [] |
| SatNo | string | 是 | "SAT_01" |
| TaskNo | string | 是 | "TASK_A" |


#### QuerygPkg

- **服务**: `MassDataServer`
- **调用类型**: Server streaming
- **请求消息**: `gQueryCondPkgReq`
- **响应消息**: `stream gPkgCollect`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |
| dbstage | string | 否 | "在轨" |
| fromdt | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| todt | Timestamp | 否 | "2026-07-02T01:00:00Z" |
| pkgids | repeated int32 | 否 | [37] |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| Datas | repeated gpkg | 否 | [] |
| Datas[].pi | int32 | 否 | 37 |
| Datas[].pc | string | 否 | "P001" |
| Datas[].pd | string | 否 | "" |
| Datas[].pt | Timestamp | 否 | "2026-07-02T00:01:00Z" |
| SatNo | string | 是 | "SAT_01" |
| TaskNo | string | 是 | "TASK_A" |


#### QuerygFrame

- **服务**: `MassDataServer`
- **调用类型**: Server streaming
- **请求消息**: `gQueryCondFrameReq`
- **响应消息**: `stream gFrameCollect`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |
| dbstage | string | 否 | "在轨" |
| fromdt | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| todt | Timestamp | 否 | "2026-07-02T01:00:00Z" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| Datas | repeated gframe | 否 | [] |
| Datas[].ft | Timestamp | 否 | "2026-07-02T00:01:00Z" |
| Datas[].fno | int32 | 否 | 1 |
| Datas[].fd | string | 否 | "AA BB" |
| SatNo | string | 是 | "SAT_01" |
| TaskNo | string | 是 | "TASK_A" |


#### GetJudgeInfos

- **服务**: `MassDataServer`
- **调用类型**: Unary
- **请求消息**: `gSatliteThumb`
- **响应消息**: `gJudgeInfoCollect`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| taskname | string | 否 | "任务A" |
| dbstage | string | 否 | "在轨" |
| satno | string | 是 | "SAT_01" |
| satname | string | 否 | "卫星01" |
| status | int32 | 否 | 0 |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| Datas | repeated gjudgeinfo | 否 | [] |
| Datas[].code | string | 是 | "JR001" |
| Datas[].desc | string | 否 | "" |
| Datas[].group | string | 否 | "" |
| Datas[].ruleType | string | 否 | "TypeA" |
| Datas[].resultvaluetype | string | 否 | "int" |
| Datas[].resultvaluedesc | string | 否 | "" |
| Datas[].lastTime | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| Datas[].data | string | 否 | "{}" |
| Datas[].statue | int32 | 否 | 0 |
| Datas[].tags | repeated string | 否 | ["t1"] |
| Datas[].id | string | 是 | "507f1f77bcf86cd799439011" |
| Datas[].IsEnable | bool | 否 | true |
| Datas[].IsResultDisplay | bool | 否 | true |
| Datas[].IsResultInDb | bool | 否 | true |
| SatNo | string | 是 | "SAT_01" |
| TaskNo | string | 是 | "TASK_A" |


#### QuerygJudgeResults

- **服务**: `MassDataServer`
- **调用类型**: Server streaming
- **请求消息**: `gQueryCondJudgeReusltReq`
- **响应消息**: `stream gJudgeResultCollect`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| satno | string | 是 | "SAT_01" |
| dbstage | string | 否 | "在轨" |
| fromdt | Timestamp | 否 | "2026-07-02T00:00:00Z" |
| todt | Timestamp | 否 | "2026-07-02T01:00:00Z" |
| judgecodes | repeated string | 否 | ["JR001"] |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| Datas | repeated gjudgeresult | 否 | [] |
| Datas[].judgerulecode | string | 否 | "JR001" |
| Datas[].judgevalue | string | 否 | "1" |
| Datas[].judgevaluedesc | string | 否 | "正常" |
| Datas[].createtime | Timestamp | 否 | "2026-07-02T00:01:00Z" |
| SatNo | string | 是 | "SAT_01" |
| TaskNo | string | 是 | "TASK_A" |



### 6.2 DataReceiveServer（4 个 RPC）

#### GetAllSatsFromTaskDb

- **服务**: `DataReceiveServer`
- **调用类型**: Unary
- **请求消息**: `gTaskDbReq`
- **响应消息**: `gSatCollectionReply`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskdbip | string | 是 | "127.0.0.1" |
| taskdbport | int32 | 是 | 27017 |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| Datas | repeated gSatliteThumb | 否 | [] |
| Datas[].taskno | string | 是 | "TASK_A" |
| Datas[].taskname | string | 否 | "任务A" |
| Datas[].dbstage | string | 否 | "在轨" |
| Datas[].satno | string | 是 | "SAT_01" |
| Datas[].satname | string | 否 | "卫星01" |
| Datas[].status | int32 | 否 | 0 |


#### GetSatCfg

- **服务**: `DataReceiveServer`
- **调用类型**: Unary
- **请求消息**: `gSatliteThumb`
- **响应消息**: `satconncfg`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| taskname | string | 否 | "任务A" |
| dbstage | string | 否 | "在轨" |
| satno | string | 是 | "SAT_01" |
| satname | string | 否 | "卫星01" |
| status | int32 | 否 | 0 |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| basicconn | string | 否 | "mongodb://..." |
| mongoqueryconn | string | 否 | "" |
| judgeconn | string | 否 | "" |
| judgemgrconn | string | 否 | "" |
| analysisconn | string | 否 | "" |
| displayconn | string | 否 | "" |
| mqtturl | string | 否 | "" |
| signalrurl | string | 否 | "" |
| cfgconn | string | 否 | "" |


#### DownLoadTMResolveFiles

- **服务**: `DataReceiveServer`
- **调用类型**: Server streaming
- **请求消息**: `gSatliteThumb`
- **响应消息**: `stream gDatafile`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| taskname | string | 否 | "任务A" |
| dbstage | string | 否 | "在轨" |
| satno | string | 是 | "SAT_01" |
| satname | string | 否 | "卫星01" |
| status | int32 | 否 | 0 |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| filename | string | 否 | "config.ini" |
| filetype | string | 否 | BYTES |
| datas | bytes | 否 | "<base64>" |


#### DownLoadRitsConfigFile

- **服务**: `DataReceiveServer`
- **调用类型**: Server streaming
- **请求消息**: `gRitsReq`
- **响应消息**: `stream gDatafile`

**请求参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| taskno | string | 是 | "TASK_A" |
| dbstage | string | 否 | "在轨" |
| satnos | repeated string | 否 | ["SAT_01","SAT_02"] |
| absolutedir | string | 是 | "test\\" |

**响应参数**
| 字段名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| filename | string | 否 | "config.ini" |
| filetype | string | 否 | BYTES |
| datas | bytes | 否 | "<base64>" |

---

## 7. 与旧文档相比的关键变更

1. Web API 路由已版本化：`/api/v1/mass-data`、`/api/v2/mass-data`（不再使用旧的 `/api/mass-data` 作为真实服务路由）。
2. v2 卫星主键不含 `dbStage`，仅 `taskNo + satNo`。
3. v2 新增多星查询 10 个端点（`/query/multi-sat/*`）。
4. v2 新增判读管理 14 个端点（sat-config/sat-group/rule-group/sat-template）。
5. v2 新增 rules4edit + tags 12 个端点。
6. v2 新增 `satellites-from-taskdb` 与 `files/rits-config`。
7. 示例程序已同步：
   - `samples/MassDataServer.ApiTestWeb`
   - `samples/MassDataServer.GrpcTestConsole`

---

## 8. 建议调用顺序

1. 获取卫星：`GET /api/v2/mass-data/satellites`
2. 拉取配置：`satellite/config`、`message-queue`、`redis`
3. 拉基础库：`basic/*`
4. 按需查询历史数据：单星 `query/*` 或多星 `query/multi-sat/*`
5. 判读与规则管理：`judge/*`、`judge/mgr/*`、`judge/rules4edit/*`、`judge/tags/*`
6. 文件下载：`files/resolve`、`files/rits-config`

---

## 9. 参考

- Web API 控制器：
  - `MassDataServer/Controllers/MassDataApiController.cs`
  - `MassDataServer/Controllers/MassDataApiV1Controller.cs`
  - `MassDataServer/Controllers/JudgeMgrApiController.cs`
  - `MassDataServer/Controllers/JudgeRuleEditApiController.cs`
- gRPC 实现：
  - `MassDataServer/Services/gRPC/V2/MassDataServiceV2Impl.cs`
  - `MassDataServer/Services/gRPC/V2/Judge/MassDataJudgeServiceImpl.cs`
  - `MassDataServer/Services/gRPC/V1/MassDataServiceV1Impl.cs`
  - `MassDataServer/Services/gRPC/V1/DataReceiveServiceV1Impl.cs`

---

## 附录 A. 配置管理 API 详细参数说明

> 路由前缀：`/api/config`（默认不出现在 Swagger）。

#### 获取配置文件

- **方法**: `GET`
- **路径**: `/api/config`
- **说明**: 返回 allServerPlats JSON 原文

**请求参数**
_无请求体（无 Query 参数）_

**响应参数**
成功时 `Content-Type: application/json`，body 为完整配置文档。
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| （body） | ConfigDocument JSON | — | 见 serverPlats 结构 |


#### 保存配置文件

- **方法**: `POST`
- **路径**: `/api/config`
- **说明**: 校验并写入配置

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| serverPlats | array(ServerPlat) | 是 | [] |
| serverPlats[].taskDbMongoUrl | string | 是 | "127.0.0.1:27017/db" |
| serverPlats[].satitems | array(SatelliteCfg) | 是 | [] |
| serverPlats[].satitems[].satelliteNo | string | 是 | "SAT_01" |
| serverPlats[].satitems[].satelliteName | string | 否 | "卫星01" |
| serverPlats[].satitems[].taskNo | string | 是 | "TASK_A" |
| serverPlats[].satitems[].taskName | string | 否 | "任务A" |
| serverPlats[].satitems[].ver | string | 否 | "1" |
| serverPlats[].satitems[].enabled | bool | 否 | true |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| success | bool | 否 | true |
| message | string | 否 | "配置保存成功" |
| path | string | 否 | "C:\\...\\allServerPlats.json" |
| warnings | array(string) | 否 | [] |


#### 校验配置

- **方法**: `POST`
- **路径**: `/api/config/validate`
- **说明**: 仅校验不写盘

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| serverPlats | array(ServerPlat) | 是 | [] |
| serverPlats[].taskDbMongoUrl | string | 是 | "127.0.0.1:27017/db" |
| serverPlats[].satitems | array(SatelliteCfg) | 是 | [] |
| serverPlats[].satitems[].satelliteNo | string | 是 | "SAT_01" |
| serverPlats[].satitems[].satelliteName | string | 否 | "卫星01" |
| serverPlats[].satitems[].taskNo | string | 是 | "TASK_A" |
| serverPlats[].satitems[].taskName | string | 否 | "任务A" |
| serverPlats[].satitems[].ver | string | 否 | "1" |
| serverPlats[].satitems[].enabled | bool | 否 | true |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| valid | bool | 否 | true |
| errors | array(string) | 否 | [] |
| warnings | array(string) | 否 | [] |


#### 导入旧版配置

- **方法**: `POST`
- **路径**: `/api/config/import-legacy`
- **说明**: Raw JSON body，按平台+taskNo+satNo+ver 合并

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| （body） | ConfigDocument JSON | 是 | 旧版 allServerPlats 格式 |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| success | bool | 否 | true |
| message | string | 否 | "旧版配置导入成功" |
| path | string | 否 | "..." |
| backupPath | string | 否 | "...\\backup\\..." |
| stats | object | 否 | {"addedSatellites":1} |
| warnings | array(string) | 否 | [] |


#### 获取配置路径信息

- **方法**: `GET`
- **路径**: `/api/config/info`
- **说明**: 元数据

**请求参数**
_无请求体（无 Query 参数）_

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| path | string | 否 | "C:\\...\\allServerPlats.json" |
| exists | bool | 否 | true |
| directory | string | 否 | "C:\\..." |
| lastModified | string | 否 | "2026-07-02 12:00:00" |


#### 备份配置文件

- **方法**: `POST`
- **路径**: `/api/config/backup`
- **说明**: 复制到 backup 目录

**请求参数**
_无请求体（无 Query 参数）_

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| success | bool | 否 | true |
| backupPath | string | 否 | "...\\backup\\..." |



### A.1 配置 API 错误响应（通用）

| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| error | string | 否 | "错误描述" |
| detail | string | 否 | "异常详情（500 时）" |

---

## 附录 A. 配置管理 API 详细参数说明

> 路由前缀：`/api/config`（默认不出现在 Swagger）。

#### 获取配置文件

- **方法**: `GET`
- **路径**: `/api/config`
- **说明**: 返回 allServerPlats JSON 原文

**请求参数**
_无请求体（无 Query 参数）_

**响应参数**
成功时 `Content-Type: application/json`，body 为完整配置文档。
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| （body） | ConfigDocument JSON | — | 见 serverPlats 结构 |


#### 保存配置文件

- **方法**: `POST`
- **路径**: `/api/config`
- **说明**: 校验并写入配置

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| serverPlats | array(ServerPlat) | 是 | [] |
| serverPlats[].taskDbMongoUrl | string | 是 | "127.0.0.1:27017/db" |
| serverPlats[].satitems | array(SatelliteCfg) | 是 | [] |
| serverPlats[].satitems[].satelliteNo | string | 是 | "SAT_01" |
| serverPlats[].satitems[].satelliteName | string | 否 | "卫星01" |
| serverPlats[].satitems[].taskNo | string | 是 | "TASK_A" |
| serverPlats[].satitems[].taskName | string | 否 | "任务A" |
| serverPlats[].satitems[].ver | string | 否 | "1" |
| serverPlats[].satitems[].enabled | bool | 否 | true |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| success | bool | 否 | true |
| message | string | 否 | "配置保存成功" |
| path | string | 否 | "C:\\...\\allServerPlats.json" |
| warnings | array(string) | 否 | [] |


#### 校验配置

- **方法**: `POST`
- **路径**: `/api/config/validate`
- **说明**: 仅校验不写盘

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| serverPlats | array(ServerPlat) | 是 | [] |
| serverPlats[].taskDbMongoUrl | string | 是 | "127.0.0.1:27017/db" |
| serverPlats[].satitems | array(SatelliteCfg) | 是 | [] |
| serverPlats[].satitems[].satelliteNo | string | 是 | "SAT_01" |
| serverPlats[].satitems[].satelliteName | string | 否 | "卫星01" |
| serverPlats[].satitems[].taskNo | string | 是 | "TASK_A" |
| serverPlats[].satitems[].taskName | string | 否 | "任务A" |
| serverPlats[].satitems[].ver | string | 否 | "1" |
| serverPlats[].satitems[].enabled | bool | 否 | true |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| valid | bool | 否 | true |
| errors | array(string) | 否 | [] |
| warnings | array(string) | 否 | [] |


#### 导入旧版配置

- **方法**: `POST`
- **路径**: `/api/config/import-legacy`
- **说明**: Raw JSON body，按平台+taskNo+satNo+ver 合并

**请求参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| （body） | ConfigDocument JSON | 是 | 旧版 allServerPlats 格式 |

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| success | bool | 否 | true |
| message | string | 否 | "旧版配置导入成功" |
| path | string | 否 | "..." |
| backupPath | string | 否 | "...\\backup\\..." |
| stats | object | 否 | {"addedSatellites":1} |
| warnings | array(string) | 否 | [] |


#### 获取配置路径信息

- **方法**: `GET`
- **路径**: `/api/config/info`
- **说明**: 元数据

**请求参数**
_无请求体（无 Query 参数）_

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| path | string | 否 | "C:\\...\\allServerPlats.json" |
| exists | bool | 否 | true |
| directory | string | 否 | "C:\\..." |
| lastModified | string | 否 | "2026-07-02 12:00:00" |


#### 备份配置文件

- **方法**: `POST`
- **路径**: `/api/config/backup`
- **说明**: 复制到 backup 目录

**请求参数**
_无请求体（无 Query 参数）_

**响应参数**
| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| success | bool | 否 | true |
| backupPath | string | 否 | "...\\backup\\..." |



### A.1 配置 API 错误响应（通用）

| 参数名称 | 类型 | 是否必填项 | 示例值 |
|---|---|---|---|
| error | string | 否 | "错误描述" |
| detail | string | 否 | "异常详情（500 时）" |

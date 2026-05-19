export interface PagedResult<T> {
  pageNo: number;
  pageSize: number;
  total: number;
  items: T[];
}

export interface DataSourceConfig {
  sourceId: string;
  sourceType:
    | 'MASS_DATA_API'
    | 'SATELLITE_ASSET_API'
    | 'CLICKHOUSE'
    | 'MINIO'
    | 'PG_META';
  sourceName: string;
  endpointUrl: string;
  authType: string;
  authSecretRef: string | null;
  timeoutMs: number;
  enabled: boolean;
  env: string;
  createdAt: string;
  updatedAt: string;
}

export interface MongoConnectionInfo {
  mongoUri: string;
  dbName: string;
  authRef: string | null;
}

export interface SatelliteCache {
  tasookNo: string;
  tasookName: string | null;
  satelliteNo: string;
  satelliteName: string;
  satelliteType: string | null;
  dbStage: string | null;
  mongoInfo: MongoConnectionInfo | null;
  sourceVersion: string | null;
  lastSyncedAt: string;
  cachedParameterCount: number;
  cachedCommandCount: number;
  isEnabled: boolean;
}

/** 卫星列表项：含测试阶段（卫星测试流程规划 teststages 同步至 test_batch_cache） */
export interface SatelliteListItem {
  tasookNo: string;
  tasookName: string | null;
  satelliteNo: string;
  satelliteName: string;
  dbStage: string | null;
  mongoInfo: MongoConnectionInfo | null;
  sourceVersion: string | null;
  lastSyncedAt: string;
  cachedParameterCount: number;
  cachedCommandCount: number;
  isEnabled: boolean;
  developmentPhases: string[];
}

/** GET /api/v1/asset/satellites/{tasookNo}/{satelliteNo}/params 列表项（不含 rawJson） */
export interface ParamCache {
  tasookNo: string;
  satelliteNo: string;
  paraId: number;
  paraCode: string | null;
  paraDesc: string | null;
  /** 后端计算的展示标签：代号 + 描述 */
  displayLabel: string;
  paraTypeDesc: string | null;
  minValue: number | null;
  maxValue: number | null;
  updateTime: number | null;
  procDesc: string | null;
  prmSysId: number | null;
  sourceVersion: string | null;
  lastSyncedAt: string;
}

/** 下拉/表格展示：优先使用后端 displayLabel，否则本地拼接代号 + 描述。 */
export function formatParamCacheLabel(p: ParamCache): string {
  if (p.displayLabel?.trim()) {
    return p.displayLabel.trim();
  }
  const code = p.paraCode?.trim();
  const desc = p.paraDesc?.trim();
  if (code && desc) {
    return `${code} ${desc}`;
  }
  if (code) {
    return code;
  }
  if (desc) {
    return desc;
  }
  return String(p.paraId);
}

export function paramCacheRowKey(p: ParamCache): string {
  return `${p.tasookNo}::${p.satelliteNo}::${p.paraId}`;
}

/** 模板配置 JSON 中的 paramId 字符串（与海量 paraId 一致）。 */
export function paramCacheId(p: ParamCache): string {
  return String(p.paraId);
}

/** GET /api/v1/asset/satellites/{tasookNo}/{satelliteNo}/commands 列表项（不含 rawJson） */
export interface CommandCache {
  tasookNo: string;
  satelliteNo: string;
  cmdId: number;
  cmdCode: string | null;
  cmdDesc: string | null;
  cmdType: number | null;
  cmdLen: number | null;
  exeTime: number | null;
  validFlag: number | null;
  cmdSysId: number | null;
  sourceVersion: string | null;
  lastSyncedAt: string;
}

export function formatCommandCacheLabel(c: CommandCache): string {
  const name = c.cmdCode ?? c.cmdDesc ?? String(c.cmdId);
  return `${c.cmdId}（${name}）`;
}

export function commandCacheRowKey(c: CommandCache): string {
  return `${c.tasookNo}::${c.satelliteNo}::${c.cmdId}`;
}

export interface TestPhase {
  tasookNo: string;
  satelliteNo: string;
  testBatchName: string;
  startTs: string;
  endTs: string;
  sourceVersion: string | null;
  lastSyncedAt: string;
}

export interface MongoConnectionSummary {
  dbName: string;
  authRef: string | null;
  mongoUri: string;
}

export interface SatelliteSyncOutcome {
  tasookNo: string;
  satelliteNo: string;
  parametersSucceeded: boolean;
  commandsSucceeded: boolean;
  testPhasesSucceeded: boolean;
  mongoConfigSucceeded: boolean;
  parameterCount: number;
  commandCount: number;
  testPhaseCount: number;
  failureReason: string | null;
  fullySucceeded: boolean;
}

export interface AssetSyncResult {
  status: 'Succeeded' | 'PartialSucceeded' | 'Failed';
  satelliteCount: number;
  parameterCount: number;
  commandCount: number;
  testBatchCount: number;
  failedSatelliteCount: number;
  syncedAt: string;
  errorCode: string | null;
  errorMessage: string | null;
  outcomes: SatelliteSyncOutcome[];
}

export interface SatelliteGroupNode {
  groupId: string;
  parentGroupId: string | null;
  groupName: string;
  groupPath: string;
  sortOrder: number;
  description: string | null;
  directMemberCount: number;
  descendantMemberCount: number;
  createdAt: string;
  updatedAt: string;
  children: SatelliteGroupNode[];
}

export interface SatelliteGroupMemberDto {
  tasookNo: string;
  satelliteNo: string;
  groupId: string;
  groupPath: string;
}

export type TemplateStatus = 'Draft' | 'Published' | 'Archived';

export interface FilterTemplateView {
  templateId: string;
  version: number;
  templateName: string;
  status: TemplateStatus;
  groupId: string;
  groupPath: string;
  description: string | null;
  createdAt: string;
  updatedAt: string;
  publishedAt: string | null;
}

export interface FilterTemplateDetail {
  view: FilterTemplateView;
  configJson: FilterTemplateConfigJson;
}

export interface FilterTemplateResolvedDetail {
  configJson: FilterTemplateConfigJson;
  resolutionWarnings: string[];
}

export type RuleOperator = '>' | '>=' | '<' | '<=' | '==' | '!=' | 'between';

export interface RuleLeaf {
  paramId: string;
  operator: RuleOperator;
  value: number | string | (number | string)[];
}

export interface RuleGroup {
  op: 'AND' | 'OR' | 'NOT';
  children: RuleNode[];
}

export type RuleNode = RuleLeaf | RuleGroup;

export interface FilterTargetParam {
  paramId: string;
  paramName?: string;
  outlier?: {
    method: 'THRESHOLD' | 'SIGMA' | 'IQR' | 'MAD' | 'HAMPEL';
    min?: number;
    max?: number;
    sigma?: number;
    windowSize?: number;
  };
  boundaryBufferBeforeSec?: number;
  boundaryBufferAfterSec?: number;
}

export interface FilterTemplateConfigJson {
  scope: {
    groupId: string;
    groupPath?: string;
    /** 筛选条件与目标参数所绑定的参考卫星（海量 param_cache 主键）；旧版本可能缺失 */
    referenceTasookNo?: string;
    referenceSatelliteNo?: string;
  };
  timeWindow: {
    mode: 'TEST_BATCH' | 'CUSTOM';
    bufferBeforeSeconds?: number;
    bufferAfterSeconds?: number;
  };
  ruleTree: RuleNode;
  durationSeconds?: number;
  targetParams: FilterTargetParam[];
}

export interface AlgorithmTemplateView {
  templateId: string;
  version: number;
  templateName: string;
  status: TemplateStatus;
  nodeCount: number;
  description: string | null;
  createdAt: string;
  updatedAt: string;
  publishedAt: string | null;
}

export interface AlgorithmTemplateDetail {
  view: AlgorithmTemplateView;
  reactFlowJson: AlgorithmReactFlowJson;
  configJson: AlgorithmConfigJson;
}

export interface AlgorithmReactFlowNode {
  id: string;
  type: string;
  position: { x: number; y: number };
  data: Record<string, unknown>;
}

export interface AlgorithmReactFlowEdge {
  id: string;
  source: string;
  target: string;
  sourceHandle?: string | null;
  targetHandle?: string | null;
}

export interface AlgorithmReactFlowJson {
  nodes: AlgorithmReactFlowNode[];
  edges: AlgorithmReactFlowEdge[];
}

export interface AlgorithmConfigJson {
  dataInputs: Array<{
    nodeRef: string;
    sourceTable: 'hq_param_point' | 'algo_result';
    paramIds: string[];
    valueField: 'processed_value' | 'raw_value';
    includeOutliers: boolean;
    outputName: string;
  }>;
  nodes: Array<{
    nodeRef: string;
    nodeType: string;
    params: Record<string, unknown>;
  }>;
}

export interface AlgorithmTemplateValidationIssue {
  code: string;
  message: string;
  nodeId: string | null;
}

export interface AlgorithmTemplateValidationResult {
  valid: boolean;
  nodeCount: number;
  edgeCount: number;
  issues: AlgorithmTemplateValidationIssue[];
}

export type AlgorithmRuntime = 'Builtin' | 'Python' | 'Js';
export type AlgorithmCategory =
  | 'Source'
  | 'Stats'
  | 'Spectrum'
  | 'Align'
  | 'Cluster'
  | 'Compare'
  | 'Output';

export interface AlgorithmRegistryEntry {
  algorithmCode: string;
  displayName: string;
  version: string;
  runtime: AlgorithmRuntime;
  category: AlgorithmCategory;
  inputsSchemaJson: unknown;
  outputsSchemaJson: unknown;
  paramsSchemaJson: unknown;
  resourcesJson: unknown;
  description: string | null;
}

export type AlgorithmPackageStatus =
  | 'Draft'
  | 'SandboxValidating'
  | 'Published'
  | 'Rejected'
  | 'Archived';

export interface AlgorithmPackageView {
  packageId: string;
  algorithmCode: string;
  displayName: string;
  version: string;
  runtime: AlgorithmRuntime;
  category: AlgorithmCategory;
  status: AlgorithmPackageStatus;
  description: string | null;
  lastError: string | null;
  createdAt: string;
  updatedAt: string;
  publishedAt: string | null;
}

export interface JobAccepted {
  jobId: string | null;
  runId: string | null;
  scheduleId?: string | null;
  status: string;
}

export type PreprocessExecutionMode = 'IMMEDIATE' | 'ONCE_SCHEDULED' | 'DAILY_RECURRING';


export interface JobStatus {
  run_id: string;
  job_id: string;
  status: string;
  progress_percent: number;
  current_step: string | null;
  error_code: string | null;
  error_msg: string | null;
}

/** GET /api/v1/tasks/{runId} 任务详情 */
export interface TaskRunDetail {
  run_id: string;
  job_id: string;
  job_type: string;
  trigger_type: string;
  status: string;
  tasook_no: string;
  satellite_no: string;
  test_batch_name: string | null;
  window_start: string | null;
  window_end: string | null;
  filter_template_id: string | null;
  filter_template_version: number | null;
  filter_template_name: string | null;
  algorithm_template_id: string | null;
  algorithm_template_version: number | null;
  algorithm_template_name: string | null;
  progress_percent: number;
  current_step: string | null;
  start_time: string | null;
  end_time: string | null;
  created_at: string;
  error_code: string | null;
  error_msg: string | null;
  execution_mode: string | null;
  scheduled_at: string | null;
  schedule_id: string | null;
  schedule_daily_time: string | null;
  schedule_interval_days: number | null;
  schedule_effective_from: string | null;
}

/** GET /api/v1/tasks 列表项（RUN 或 SCHEDULE） */
export interface TaskListItemV2 {
  item_type: 'RUN' | 'SCHEDULE';
  item_id: string;
  run_id: string | null;
  schedule_id: string | null;
  job_id: string | null;
  job_type: string;
  execution_mode: string | null;
  /** 是否可点击「执行」（立即待执行 / 每日计划启用） */
  can_execute?: boolean;
  can_delete?: boolean;
  can_re_execute?: boolean;
  can_view_data?: boolean;
  /** 合并后的状态文案（取消为 cancelled） */
  status_summary?: string;
  display_status: string;
  status: string;
  tasook_no: string;
  satellite_no: string;
  test_batch_name: string | null;
  progress_percent: number;
  current_step: string | null;
  scheduled_at: string | null;
  created_at: string;
  end_time: string | null;
}

export interface TaskExecutionRecord {
  run_id: string;
  job_id: string | null;
  status: string;
  display_status: string;
  started_at: string | null;
  ended_at: string | null;
  window_start: string | null;
  window_end: string | null;
  error_code: string | null;
  error_msg: string | null;
}

/** @deprecated 使用 TaskListItemV2 */
export type TaskRunListItem = TaskListItemV2;

export interface TaskProcessedDataColumn {
  param_id: string;
  label: string;
}

export interface TaskProcessedDataCell {
  value: number | null;
  is_outlier: boolean;
}

export interface TaskProcessedDataRow {
  ts: string;
  cells: Record<string, TaskProcessedDataCell>;
}

export interface TaskProcessedData {
  run_id: string;
  columns: TaskProcessedDataColumn[];
  rows: TaskProcessedDataRow[];
  /** 时间点总数（分页按行/时间计） */
  total: number;
  page: number;
  page_size: number;
}

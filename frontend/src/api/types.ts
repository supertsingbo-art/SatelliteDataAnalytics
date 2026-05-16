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
}

/** 卫星列表项：含研制阶段（卫星资产 test-phases 同步至 test_batch_cache） */
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
  developmentPhases: string[];
}

export interface ParamCache {
  tasookNo: string;
  satelliteNo: string;
  paramId: string;
  paramName: string;
  unit: string | null;
  valueType: string | null;
  valueMin: number | null;
  valueMax: number | null;
  sourceVersion: string | null;
  lastSyncedAt: string;
}

export interface TestPhase {
  tasookNo: string;
  satelliteNo: string;
  testBatchId: string;
  scenario: string | null;
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
  jobId: string;
  runId: string;
  status: string;
}


export interface JobStatus {
  run_id: string;
  job_id: string;
  status: string;
  progress_percent: number;
  current_step: string | null;
  error_code: string | null;
  error_msg: string | null;
}

/** GET /api/v1/tasks 列表项 */
export interface TaskRunListItem {
  run_id: string;
  job_id: string;
  job_type: string;
  trigger_type: string;
  status: string;
  tasook_no: string;
  satellite_no: string;
  test_batch_id: string | null;
  progress_percent: number;
  current_step: string | null;
  created_at: string;
  end_time: string | null;
}

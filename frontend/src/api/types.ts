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
  satelliteNo: string;
  satelliteName: string;
  satelliteType: string | null;
  dbStage: string | null;
  mongoInfo: MongoConnectionInfo | null;
  sourceVersion: string | null;
  lastSyncedAt: string;
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
  testPhasesSucceeded: boolean;
  mongoConfigSucceeded: boolean;
  parameterCount: number;
  testPhaseCount: number;
  failureReason: string | null;
  fullySucceeded: boolean;
}

export interface AssetSyncResult {
  status: 'Succeeded' | 'PartialSucceeded' | 'Failed';
  satelliteCount: number;
  parameterCount: number;
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
  scope: { groupId: string; groupPath?: string };
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

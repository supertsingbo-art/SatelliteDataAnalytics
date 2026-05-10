import { request } from './client';
import {
  AssetSyncResult,
  DataSourceConfig,
  MongoConnectionSummary,
  PagedResult,
  ParamCache,
  SatelliteCache,
  TestPhase
} from './types';

const BASE = '/api/v1/asset';

export interface SaveDataSourceConfigRequest {
  sourceType: DataSourceConfig['sourceType'];
  sourceName: string;
  endpointUrl: string;
  authType: string;
  authSecretRef?: string | null;
  timeoutMs: number;
  enabled: boolean;
  env: string;
}

export const assetsApi = {
  listSources: () => request<DataSourceConfig[]>('get', `${BASE}/sources`),
  createSource: (body: SaveDataSourceConfigRequest) =>
    request<DataSourceConfig>('post', `${BASE}/sources`, body),
  updateSource: (sourceId: string, body: SaveDataSourceConfigRequest) =>
    request<DataSourceConfig>('put', `${BASE}/sources/${sourceId}`, body),
  setSourceStatus: (sourceId: string, enabled: boolean) =>
    request<DataSourceConfig>('patch', `${BASE}/sources/${sourceId}/status`, { enabled }),
  testSourceConnection: (sourceId: string) =>
    request<{ success: boolean; message: string; elapsedMs: number | null }>(
      'post',
      `${BASE}/sources/${sourceId}/test`
    ),

  syncAll: () => request<AssetSyncResult>('post', `${BASE}/sync`),
  syncSatellite: (tasookNo: string, satelliteNo: string) =>
    request<AssetSyncResult>(
      'post',
      `${BASE}/satellites/${encodeURIComponent(tasookNo)}/${encodeURIComponent(satelliteNo)}/sync`
    ),
  clearCache: () => request<{ cleared: boolean }>('delete', `${BASE}/cache`),

  listSatellites: (params: { keyword?: string; pageNo?: number; pageSize?: number }) =>
    request<PagedResult<SatelliteCache>>('get', `${BASE}/satellites`, undefined, params),
  getSatellite: (tasookNo: string, satelliteNo: string) =>
    request<SatelliteCache>(
      'get',
      `${BASE}/satellites/${encodeURIComponent(tasookNo)}/${encodeURIComponent(satelliteNo)}`
    ),
  listParams: (
    tasookNo: string,
    satelliteNo: string,
    params: { keyword?: string; pageNo?: number; pageSize?: number }
  ) =>
    request<PagedResult<ParamCache>>(
      'get',
      `${BASE}/satellites/${encodeURIComponent(tasookNo)}/${encodeURIComponent(satelliteNo)}/params`,
      undefined,
      params
    ),
  listTestPhases: (tasookNo: string, satelliteNo: string) =>
    request<TestPhase[]>(
      'get',
      `${BASE}/satellites/${encodeURIComponent(tasookNo)}/${encodeURIComponent(satelliteNo)}/test-phases`
    ),
  getMongoSummary: (tasookNo: string, satelliteNo: string) =>
    request<MongoConnectionSummary>(
      'get',
      `${BASE}/satellites/${encodeURIComponent(tasookNo)}/${encodeURIComponent(satelliteNo)}/mongo-info`
    )
};

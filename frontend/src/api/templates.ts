import { http, request } from './client';
import type { ApiResponse } from './client';
import {
  AlgorithmPackageView,
  AlgorithmRegistryEntry,
  AlgorithmTemplateDetail,
  AlgorithmTemplateValidationResult,
  AlgorithmTemplateView,
  FilterTemplateConfigJson,
  FilterTemplateDetail,
  FilterTemplateResolvedDetail,
  FilterTemplateView,
  PagedResult,
  TemplateStatus
} from './types';

const FILTER_BASE = '/api/v1/templates/filters';
const ALGO_BASE = '/api/v1/templates/algorithms';
const ALGO_REGISTRY_BASE = '/api/v1/algorithms';

export interface CreateFilterTemplateRequest {
  templateName: string;
  groupId: string;
  configJson: FilterTemplateConfigJson;
  description?: string | null;
}

export type UpdateFilterTemplateRequest = CreateFilterTemplateRequest;

export const filterTemplatesApi = {
  list: (params: { groupId?: string; status?: TemplateStatus; keyword?: string; pageNo?: number; pageSize?: number }) =>
    request<PagedResult<FilterTemplateView>>('get', FILTER_BASE, undefined, params),
  applicable: (taskNo: string, satNo: string) =>
    request<FilterTemplateView[]>('get', `${FILTER_BASE}/applicable`, undefined, { taskNo, satNo }),
  versions: (templateId: string) => request<FilterTemplateView[]>('get', `${FILTER_BASE}/${templateId}/versions`),
  detail: (templateId: string, version: number) =>
    request<FilterTemplateDetail>('get', `${FILTER_BASE}/${templateId}/versions/${version}`),
  resolvedConfig: (templateId: string, version: number, taskNo: string, satNo: string) =>
    request<FilterTemplateResolvedDetail>(
      'get',
      `${FILTER_BASE}/${templateId}/versions/${version}/resolved-config`,
      undefined,
      { taskNo, satNo }
    ),
  create: (body: CreateFilterTemplateRequest) => request<FilterTemplateDetail>('post', FILTER_BASE, body),
  update: (templateId: string, version: number, body: UpdateFilterTemplateRequest) =>
    request<FilterTemplateDetail>('put', `${FILTER_BASE}/${templateId}/versions/${version}`, body),
  publish: (templateId: string, version: number) =>
    request<FilterTemplateView>('post', `${FILTER_BASE}/${templateId}/versions/${version}/publish`),
  archive: (templateId: string, version: number) =>
    request<FilterTemplateView>('post', `${FILTER_BASE}/${templateId}/versions/${version}/archive`),
  clone: (templateId: string, sourceVersion?: number) =>
    request<FilterTemplateDetail>('post', `${FILTER_BASE}/${templateId}/clone`, undefined, { sourceVersion }),
  remove: (templateId: string, version: number) =>
    request<{ deleted: boolean }>('delete', `${FILTER_BASE}/${templateId}/versions/${version}`)
};

export interface CreateAlgorithmTemplateRequest {
  templateName: string;
  reactFlowJson: AlgorithmTemplateDetail['reactFlowJson'];
  configJson: AlgorithmTemplateDetail['configJson'];
  description?: string | null;
}

export type UpdateAlgorithmTemplateRequest = CreateAlgorithmTemplateRequest;

export const algoTemplatesApi = {
  list: (params: { status?: TemplateStatus; keyword?: string; pageNo?: number; pageSize?: number }) =>
    request<PagedResult<AlgorithmTemplateView>>('get', ALGO_BASE, undefined, params),
  versions: (templateId: string) => request<AlgorithmTemplateView[]>('get', `${ALGO_BASE}/${templateId}/versions`),
  detail: (templateId: string, version: number) =>
    request<AlgorithmTemplateDetail>('get', `${ALGO_BASE}/${templateId}/versions/${version}`),
  create: (body: CreateAlgorithmTemplateRequest) => request<AlgorithmTemplateDetail>('post', ALGO_BASE, body),
  update: (templateId: string, version: number, body: UpdateAlgorithmTemplateRequest) =>
    request<AlgorithmTemplateDetail>('put', `${ALGO_BASE}/${templateId}/versions/${version}`, body),
  validate: (templateId: string, version: number) =>
    request<AlgorithmTemplateValidationResult>(
      'post',
      `${ALGO_BASE}/${templateId}/versions/${version}/validate`
    ),
  publish: (templateId: string, version: number) =>
    request<AlgorithmTemplateView>('post', `${ALGO_BASE}/${templateId}/versions/${version}/publish`),
  archive: (templateId: string, version: number) =>
    request<AlgorithmTemplateView>('post', `${ALGO_BASE}/${templateId}/versions/${version}/archive`),
  clone: (templateId: string, sourceVersion?: number) =>
    request<AlgorithmTemplateDetail>('post', `${ALGO_BASE}/${templateId}/clone`, undefined, { sourceVersion }),
  remove: (templateId: string, version: number) =>
    request<{ deleted: boolean }>('delete', `${ALGO_BASE}/${templateId}/versions/${version}`),
  trialRun: (
    templateId: string,
    version: number,
    body: { tasookNo: string; satelliteNo: string; testBatchId?: string | null; windowStart?: string | null; windowEnd?: string | null }
  ) =>
    request<{ runId: string; status: string; message: string }>(
      'post',
      `${ALGO_BASE}/${templateId}/versions/${version}/trial-run`,
      body
    )
};

export const algoRegistryApi = {
  registry: () => request<AlgorithmRegistryEntry[]>('get', `${ALGO_REGISTRY_BASE}/registry`),
  packages: (params?: { status?: string; runtime?: string; category?: string }) =>
    request<AlgorithmPackageView[]>('get', `${ALGO_REGISTRY_BASE}/packages`, undefined, params as Record<string, unknown>),
  uploadPackage: async (file: File) => {
    const form = new FormData();
    form.append('file', file);
    const res = await http.post<ApiResponse<{ package_id: string }>>(`${ALGO_REGISTRY_BASE}/packages/upload`, form, {
      headers: { 'Content-Type': 'multipart/form-data' }
    });
    if (!res.data.success) throw new Error(res.data.message);
    return res.data.data!;
  }
};

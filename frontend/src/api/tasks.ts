import { request } from './client';
import type { JobAccepted, JobStatus, TaskRunListItem } from './types';

const TASKS_BASE = '/api/v1/tasks';

export interface CreatePipelineBody {
  tasookNo: string;
  satelliteNo: string;
  testBatchId?: string | null;
  windowStart?: string | null;
  windowEnd?: string | null;
  filterTemplateId?: string | null;
  filterTemplateVersion?: number | null;
  algorithmTemplateId?: string | null;
  algorithmTemplateVersion?: number | null;
  idempotencyKey?: string | null;
}

export interface CreatePreprocessBody {
  tasookNo: string;
  satelliteNo: string;
  testBatchId?: string | null;
  windowStart?: string | null;
  windowEnd?: string | null;
  filterTemplateId?: string | null;
  filterTemplateVersion?: number | null;
  idempotencyKey?: string | null;
}

export const tasksApi = {
  list: (pageSize = 50) =>
    request<TaskRunListItem[]>('get', TASKS_BASE, undefined, { pageSize } as Record<string, unknown>),
  createPipeline: (body: CreatePipelineBody) =>
    request<JobAccepted>('post', `${TASKS_BASE}/pipeline`, {
      tasookNo: body.tasookNo,
      satelliteNo: body.satelliteNo,
      testBatchId: body.testBatchId,
      windowStart: body.windowStart,
      windowEnd: body.windowEnd,
      filterTemplateId: body.filterTemplateId,
      filterTemplateVersion: body.filterTemplateVersion,
      algorithmTemplateId: body.algorithmTemplateId,
      algorithmTemplateVersion: body.algorithmTemplateVersion,
      idempotencyKey: body.idempotencyKey
    }),
  createPreprocess: (body: CreatePreprocessBody) =>
    request<JobAccepted>('post', `${TASKS_BASE}/preprocess`, {
      tasookNo: body.tasookNo,
      satelliteNo: body.satelliteNo,
      testBatchId: body.testBatchId,
      windowStart: body.windowStart,
      windowEnd: body.windowEnd,
      filterTemplateId: body.filterTemplateId,
      filterTemplateVersion: body.filterTemplateVersion,
      idempotencyKey: body.idempotencyKey
    }),
  get: (runId: string) => request<JobStatus>('get', `${TASKS_BASE}/${runId}`)
};

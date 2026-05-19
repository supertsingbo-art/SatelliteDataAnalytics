import { request } from './client';
import type {
  JobAccepted,
  PreprocessExecutionMode,
  TaskExecutionRecord,
  TaskListItemV2,
  TaskRunDetail
} from './types';

const TASKS_BASE = '/api/v1/tasks';

export interface CreatePipelineBody {
  tasookNo: string;
  satelliteNo: string;
  testBatchName?: string | null;
  windowStart?: string | null;
  windowEnd?: string | null;
  filterTemplateId?: string | null;
  filterTemplateVersion?: number | null;
  algorithmTemplateId?: string | null;
  algorithmTemplateVersion?: number | null;
  idempotencyKey?: string | null;
}

export interface CreatePreprocessBody {
  executionMode: PreprocessExecutionMode;
  tasookNo: string;
  satelliteNo: string;
  testBatchName?: string | null;
  windowStart?: string | null;
  windowEnd?: string | null;
  scheduledAt?: string | null;
  dailyTime?: string | null;
  intervalDays?: number | null;
  effectiveFrom?: string | null;
  filterTemplateId?: string | null;
  filterTemplateVersion?: number | null;
  idempotencyKey?: string | null;
}

export interface ExecuteTaskResult {
  display_status: string;
  run_id: string | null;
  schedule_id: string | null;
  job_id: string | null;
  status: string;
}

export const tasksApi = {
  list: (params?: { pageSize?: number }) =>
    request<TaskListItemV2[]>('get', TASKS_BASE, undefined, {
      pageSize: params?.pageSize ?? 50
    } as Record<string, unknown>),
  createPipeline: (body: CreatePipelineBody) =>
    request<JobAccepted>('post', `${TASKS_BASE}/pipeline`, {
      tasookNo: body.tasookNo,
      satelliteNo: body.satelliteNo,
      testBatchName: body.testBatchName,
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
      executionMode: body.executionMode,
      tasookNo: body.tasookNo,
      satelliteNo: body.satelliteNo,
      testBatchName: body.testBatchName,
      windowStart: body.windowStart,
      windowEnd: body.windowEnd,
      scheduledAt: body.scheduledAt,
      dailyTime: body.dailyTime,
      intervalDays: body.intervalDays,
      effectiveFrom: body.effectiveFrom,
      filterTemplateId: body.filterTemplateId,
      filterTemplateVersion: body.filterTemplateVersion,
      idempotencyKey: body.idempotencyKey
    }),
  disableSchedule: (scheduleId: string) =>
    request<{ scheduleId: string; enabled: boolean }>('post', `${TASKS_BASE}/schedules/${scheduleId}/disable`),
  executeRun: (runId: string) =>
    request<ExecuteTaskResult>('post', `${TASKS_BASE}/${runId}/execute`),
  executeSchedule: (scheduleId: string) =>
    request<ExecuteTaskResult>('post', `${TASKS_BASE}/schedules/${scheduleId}/execute`),
  listRunExecutions: (runId: string) =>
    request<TaskExecutionRecord[]>('get', `${TASKS_BASE}/runs/${runId}/executions`),
  listScheduleExecutions: (scheduleId: string) =>
    request<TaskExecutionRecord[]>('get', `${TASKS_BASE}/schedules/${scheduleId}/executions`),
  get: (runId: string) => request<TaskRunDetail>('get', `${TASKS_BASE}/${runId}`),
  cancel: (runId: string) =>
    request<JobAccepted>('post', `${TASKS_BASE}/${runId}/cancel`)
};

import { request } from './client';
import type {
  JobAccepted,
  PreprocessExecutionMode,
  TaskExecutionRecord,
  TaskListItemV2,
  TaskOutlierPoints,
  TaskOutlierSegments,
  TaskValidRanges,
  OutlierReviewSummary,
  OutlierReviewList,
  CompleteOutlierReviewResult,
  TaskProcessedData,
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
    request<JobAccepted>('post', `${TASKS_BASE}/${runId}/cancel`),
  deleteRun: (runId: string) =>
    request<{ runId: string; deleted: boolean }>('delete', `${TASKS_BASE}/${runId}`),
  reExecuteRun: (runId: string) =>
    request<ExecuteTaskResult>('post', `${TASKS_BASE}/${runId}/reexecute`),
  getProcessedData: (runId: string, params?: { page?: number; pageSize?: number }) =>
    request<TaskProcessedData>('get', `${TASKS_BASE}/${runId}/processed-data`, undefined, {
      page: params?.page ?? 1,
      pageSize: params?.pageSize ?? 50
    } as Record<string, unknown>),
  getOutlierPoints: (runId: string, params?: { page?: number; pageSize?: number; paramId?: string; status?: string }) =>
    request<TaskOutlierPoints>('get', `${TASKS_BASE}/${runId}/outlier-points`, undefined, {
      page: params?.page ?? 1,
      pageSize: params?.pageSize ?? 50,
      ...(params?.paramId ? { paramId: params.paramId } : {}),
      ...(params?.status ? { status: params.status } : {})
    } as Record<string, unknown>),
  getOutlierSegments: (runId: string) =>
    request<TaskOutlierSegments>('get', `${TASKS_BASE}/${runId}/outlier-segments`),
  getValidRanges: (runId: string) =>
    request<TaskValidRanges>('get', `${TASKS_BASE}/${runId}/valid-ranges`),
  getOutlierReviewSummary: (runId: string) =>
    request<OutlierReviewSummary>('get', `${TASKS_BASE}/${runId}/outlier-reviews/summary`),
  getOutlierReviews: (runId: string, params?: { page?: number; pageSize?: number; status?: string; paramId?: string }) =>
    request<OutlierReviewList>('get', `${TASKS_BASE}/${runId}/outlier-reviews`, undefined, {
      page: params?.page ?? 1,
      pageSize: params?.pageSize ?? 50,
      ...(params?.status ? { status: params.status } : {}),
      ...(params?.paramId ? { paramId: params.paramId } : {})
    } as Record<string, unknown>),
  submitOutlierReviews: (
    runId: string,
    items: { paramId: string; ts: string; status: string; remark?: string }[]
  ) =>
    request<OutlierReviewSummary>('patch', `${TASKS_BASE}/${runId}/outlier-reviews`, { items }),
  completeOutlierReview: (runId: string) =>
    request<CompleteOutlierReviewResult>('post', `${TASKS_BASE}/${runId}/outlier-reviews/complete`)
};

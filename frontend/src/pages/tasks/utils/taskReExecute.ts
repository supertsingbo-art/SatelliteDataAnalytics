import type { TaskRunDetail } from '@/api/types';

const TERMINAL_STATUSES = new Set(['Succeeded', 'Failed', 'Timeout', 'Cancelled']);

/** 与后端 TaskRunStateHelper.CanReExecuteRun 对齐 */
export function canReExecuteTaskDetail(d: TaskRunDetail): boolean {
  if (!TERMINAL_STATUSES.has(d.status)) {
    return false;
  }
  if (d.execution_mode != null && d.execution_mode !== 'IMMEDIATE') {
    return false;
  }
  if (d.job_type === 'PREPROCESS') {
    return true;
  }
  if (d.job_type === 'PIPELINE' && d.filter_template_id) {
    return true;
  }
  return false;
}

import { message } from 'antd';
import { tasksApi, type RunConflictOptions } from '@/api/tasks';
import type { PreprocessConflictRetryPolicy } from '@/pages/tasks/utils/preprocessConflictModal';

export function buildConflictRetryOptions(
  policy: PreprocessConflictRetryPolicy
): RunConflictOptions {
  return {
    onActiveConflict: 'SKIP',
    onCommittedConflict: policy
  };
}

export async function reExecuteWithConflictPolicy(
  runId: string,
  policy: PreprocessConflictRetryPolicy
): Promise<void> {
  const res = await tasksApi.reExecuteRun(runId, buildConflictRetryOptions(policy));
  message.success(`已重新提交执行（${res.display_status}）`);
}

import { message } from 'antd';
import { tasksApi, type RunConflictOptions } from '@/api/tasks';
import { openPreprocessConflictModal } from '@/pages/tasks/utils/preprocessConflictModal';
import { buildConflictRetryOptions } from '@/pages/tasks/utils/preprocessConflictRetry';

export interface RunPreprocessWithPreflightOptions {
  runId: string;
  detailHref?: string;
  onOpenTemplate?: (templateId: string, version: number) => void;
  execute: (conflictOptions?: RunConflictOptions) => Promise<void>;
}

/** 执行前预检参数冲突；有冲突则弹窗让用户选择覆盖/跳过后再提交。 */
export async function runPreprocessWithPreflight(
  options: RunPreprocessWithPreflightOptions
): Promise<void> {
  const preflight = await tasksApi.conflictPreflight(options.runId);

  if (preflight.plan_error_code) {
    message.error(
      `${preflight.plan_error_code}: ${preflight.plan_error_message ?? '任务预检失败，无法执行'}`
    );
    return;
  }

  if (!preflight.has_conflict) {
    await options.execute();
    return;
  }

  openPreprocessConflictModal({
    errorMsg: preflight.message,
    conflictDetails: preflight.conflict_details,
    canRetry: true,
    detailHref: options.detailHref,
    onOpenTemplate: options.onOpenTemplate,
    onRetry: async (policy) => {
      await options.execute(buildConflictRetryOptions(policy));
    }
  });
}

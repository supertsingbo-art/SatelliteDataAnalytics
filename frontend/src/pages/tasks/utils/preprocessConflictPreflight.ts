import { message } from 'antd';
import { tasksApi, type RunConflictOptions } from '@/api/tasks';
import { openPreprocessConflictModal } from '@/pages/tasks/utils/preprocessConflictModal';
import { buildConflictRetryOptions } from '@/pages/tasks/utils/preprocessConflictRetry';
import { notifyTaskActionError } from '@/pages/tasks/utils/taskActionError';

export interface RunPreprocessWithPreflightOptions {
  runId: string;
  detailHref?: string;
  onOpenTemplate?: (templateId: string, version: number) => void;
  execute: (conflictOptions?: RunConflictOptions) => Promise<void>;
}

async function runExecuteWithErrorHandling(
  options: RunPreprocessWithPreflightOptions,
  conflictOptions?: RunConflictOptions
): Promise<void> {
  try {
    await options.execute(conflictOptions);
  } catch (error) {
    const parsed = notifyTaskActionError(error, '任务提交失败');
    if (parsed?.code === 'PRE_006') {
      openPreprocessConflictModal({
        errorMsg: parsed.message,
        canRetry: true,
        detailHref: options.detailHref,
        onOpenTemplate: options.onOpenTemplate,
        onRetry: async (policy) => {
          await runExecuteWithErrorHandling(
            options,
            buildConflictRetryOptions(policy)
          );
        }
      });
      return;
    }
    throw error;
  }
}

/** 执行前预检参数冲突；有冲突则弹窗让用户选择覆盖/跳过后再提交。 */
export async function runPreprocessWithPreflight(
  options: RunPreprocessWithPreflightOptions
): Promise<void> {
  let preflight;
  try {
    preflight = await tasksApi.conflictPreflight(options.runId);
  } catch (error) {
    notifyTaskActionError(error, '冲突预检失败');
    throw error;
  }

  if (preflight.plan_error_code) {
    message.error(
      `${preflight.plan_error_code}: ${preflight.plan_error_message ?? '任务预检失败，无法执行'}`
    );
    return;
  }

  if (!preflight.has_conflict) {
    await runExecuteWithErrorHandling(options);
    return;
  }

  openPreprocessConflictModal({
    errorMsg: preflight.message,
    conflictDetails: preflight.conflict_details,
    canRetry: true,
    detailHref: options.detailHref,
    onOpenTemplate: options.onOpenTemplate,
    onRetry: async (policy) => {
      await runExecuteWithErrorHandling(options, buildConflictRetryOptions(policy));
    }
  });
}

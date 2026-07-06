import { message } from 'antd';
import { parseApiError, type ParsedApiError } from '@/api/client';

/** 提示任务操作错误；PRE_006 不弹全局 toast，由调用方打开冲突弹窗。 */
export function notifyTaskActionError(
  error: unknown,
  fallback = '操作失败'
): ParsedApiError | null {
  const parsed = parseApiError(error);
  if (parsed?.code === 'PRE_006') {
    return parsed;
  }
  if (parsed) {
    message.error(`${parsed.code}：${parsed.message}`);
    return parsed;
  }
  if (error instanceof Error && error.message) {
    message.error(error.message);
    return null;
  }
  message.error(fallback);
  return null;
}

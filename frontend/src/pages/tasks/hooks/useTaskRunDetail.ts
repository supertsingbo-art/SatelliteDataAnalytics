import { useCallback, useEffect, useState } from 'react';
import { tasksApi } from '@/api/tasks';
import type { TaskRunDetail } from '@/api/types';

const terminalStatuses = ['Succeeded', 'Failed', 'Timeout', 'Cancelled'];

export function useTaskRunDetail(runId: string | null, pollWhileActive = true) {
  const [detail, setDetail] = useState<TaskRunDetail | null>(null);
  const [loading, setLoading] = useState(false);
  const [polling, setPolling] = useState(false);

  const refresh = useCallback(async () => {
    if (!runId) {
      setDetail(null);
      return;
    }
    setLoading(true);
    try {
      const d = await tasksApi.get(runId);
      setDetail(d);
      if (terminalStatuses.includes(d.status)) {
        setPolling(false);
      }
    } catch {
      setPolling(false);
    } finally {
      setLoading(false);
    }
  }, [runId]);

  useEffect(() => {
    if (!runId) {
      setDetail(null);
      setPolling(false);
      return;
    }
    setPolling(pollWhileActive);
    void refresh();
  }, [runId, pollWhileActive, refresh]);

  useEffect(() => {
    if (!runId || !polling) {
      return;
    }
    const id = window.setInterval(() => {
      void refresh();
    }, 2000);
    return () => window.clearInterval(id);
  }, [runId, polling, refresh]);

  return { detail, loading, polling, setPolling, refresh };
}

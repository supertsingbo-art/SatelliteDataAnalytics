import { useCallback, useEffect, useRef, useState } from 'react';
import {
  Button,
  Card,
  Drawer,
  Popconfirm,
  Space,
  Table,
  Tag,
  Typography,
  message
} from 'antd';
import { Link } from 'react-router-dom';
import { ReloadOutlined } from '@ant-design/icons';
import { tasksApi } from '@/api/tasks';
import type { TaskExecutionRecord, TaskListItemV2 } from '@/api/types';

const { Paragraph } = Typography;

function taskDetailPath(jobType: string, runId: string): string {
  const q = `runId=${encodeURIComponent(runId)}`;
  if (jobType === 'PREPROCESS') {
    return `/tasks/preprocess?${q}`;
  }
  return `/tasks/pipeline?${q}`;
}

const executionModeLabel: Record<string, string> = {
  IMMEDIATE: '立即',
  ONCE_SCHEDULED: '一次定时',
  DAILY_INSTANCE: '每日实例',
  DAILY_RECURRING: '每天定时'
};

const statusColor: Record<string, string> = {
  Queued: 'default',
  Running: 'processing',
  Succeeded: 'success',
  Failed: 'error',
  Timeout: 'warning',
  Cancelled: 'default',
  Scheduled: 'default'
};

const displayStatusColor: Record<string, string> = {
  待执行: 'default',
  任务定时中: 'blue',
  任务执行中: 'processing',
  任务执行完毕: 'success'
};

const cancellableStatuses = new Set(['Queued', 'Running']);
function rowKey(r: TaskListItemV2): string {
  return `${r.item_type}:${r.item_id}`;
}

function canExecute(row: TaskListItemV2): boolean {
  if (row.can_execute === true) return true;
  if (row.can_execute === false) return false;
  if (row.item_type === 'SCHEDULE') return true;
  if (row.execution_mode === 'IMMEDIATE' && row.display_status === '待执行') return true;
  return (
    row.item_type === 'RUN' &&
    row.job_type === 'PREPROCESS' &&
    row.status === 'Queued' &&
    (row.execution_mode === 'IMMEDIATE' || row.execution_mode == null) &&
    row.display_status === '待执行'
  );
}

function isImmediateRun(row: TaskListItemV2): boolean {
  return (
    row.item_type === 'RUN' &&
    (row.execution_mode === 'IMMEDIATE' || row.execution_mode == null)
  );
}

export function TasksListPage() {
  const [rows, setRows] = useState<TaskListItemV2[]>([]);
  const [loading, setLoading] = useState(false);
  const [cancellingId, setCancellingId] = useState<string | null>(null);
  const [executingKey, setExecutingKey] = useState<string | null>(null);
  const [detailOpen, setDetailOpen] = useState(false);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detailRows, setDetailRows] = useState<TaskExecutionRecord[]>([]);
  const [detailTitle, setDetailTitle] = useState('');
  const pollRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const data = await tasksApi.list({ pageSize: 100 });
      setRows(data);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  useEffect(() => {
    const needsPoll = rows.some(
      (r) => r.display_status === '任务执行中' || r.display_status === '待执行'
    );
    if (!needsPoll) {
      if (pollRef.current) {
        clearInterval(pollRef.current);
        pollRef.current = null;
      }
      return;
    }
    pollRef.current = setInterval(() => {
      void load();
    }, 2000);
    return () => {
      if (pollRef.current) {
        clearInterval(pollRef.current);
        pollRef.current = null;
      }
    };
  }, [rows, load]);

  const handleCancel = async (runId: string) => {
    setCancellingId(runId);
    try {
      await tasksApi.cancel(runId);
      message.success('任务已取消');
      await load();
    } catch {
      /* axios 已提示 */
    } finally {
      setCancellingId(null);
    }
  };

  const handleExecute = async (row: TaskListItemV2) => {
    const key = rowKey(row);
    setExecutingKey(key);
    try {
      if (row.item_type === 'SCHEDULE' && row.schedule_id) {
        await tasksApi.executeSchedule(row.schedule_id);
        message.success('每天定时计划已启用');
      } else if (row.run_id && isImmediateRun(row)) {
        const res = await tasksApi.executeRun(row.run_id);
        message.success(`任务已提交执行（${res.display_status}）`);
      }
      await load();
    } catch {
      /* axios 已提示 */
    } finally {
      setExecutingKey(null);
    }
  };

  const openExecutions = async (row: TaskListItemV2) => {
    setDetailTitle(row.job_id ?? row.item_id);
    setDetailOpen(true);
    setDetailLoading(true);
    try {
      if (row.item_type === 'SCHEDULE' && row.schedule_id) {
        setDetailRows(await tasksApi.listScheduleExecutions(row.schedule_id));
      } else if (row.run_id) {
        setDetailRows(await tasksApi.listRunExecutions(row.run_id));
      } else {
        setDetailRows([]);
      }
    } finally {
      setDetailLoading(false);
    }
  };

  return (
    <Card
      title="任务列表"
      extra={
        <Space>
          <Button icon={<ReloadOutlined />} onClick={() => void load()}>
            刷新
          </Button>
          <Link to="/tasks/preprocess">
            <Button>新建预处理入仓</Button>
          </Link>
          <Link to="/tasks/pipeline">
            <Button type="primary">新建 PIPELINE</Button>
          </Link>
        </Space>
      }
    >
      <Paragraph type="secondary" style={{ marginBottom: 12 }}>
        立即执行的任务创建后需在列表点击「执行」；「运行明细」展示每次实例的开始/结束与结果。执行中或待执行状态每 2 秒自动刷新。
      </Paragraph>
      <Table<TaskListItemV2>
        loading={loading}
        rowKey={rowKey}
        dataSource={rows}
        pagination={{ pageSize: 20, showSizeChanger: true }}
        scroll={{ x: 1300 }}
        columns={[
          {
            title: '操作',
            key: 'act',
            width: 260,
            fixed: 'left',
            render: (_, r) => (
              <Space size="small" wrap>
                {canExecute(r) && isImmediateRun(r) && (
                  <Button
                    type="primary"
                    size="small"
                    loading={executingKey === rowKey(r)}
                    onClick={() => void handleExecute(r)}
                  >
                    执行
                  </Button>
                )}
                {canExecute(r) && r.item_type === 'SCHEDULE' && (
                  <Button
                    type="default"
                    size="small"
                    loading={executingKey === rowKey(r)}
                    onClick={() => void handleExecute(r)}
                  >
                    启用计划
                  </Button>
                )}
                {r.run_id && <Link to={taskDetailPath(r.job_type, r.run_id)}>详情</Link>}
                <Button
                  type="link"
                  size="small"
                  style={{ padding: 0 }}
                  onClick={() => void openExecutions(r)}
                >
                  运行明细
                </Button>
                {r.run_id && cancellableStatuses.has(r.status) && (
                  <Popconfirm
                    title="确认取消该任务？"
                    description="已运行的任务将在下一检查点停止，状态变为 Cancelled。"
                    onConfirm={() => void handleCancel(r.run_id!)}
                    okText="取消任务"
                    cancelText="返回"
                  >
                    <Button
                      type="link"
                      danger
                      size="small"
                      loading={cancellingId === r.run_id}
                      style={{ padding: 0 }}
                    >
                      取消
                    </Button>
                  </Popconfirm>
                )}
              </Space>
            )
          },
          {
            title: '展示状态',
            dataIndex: 'display_status',
            width: 120,
            render: (s: string) => (
              <Tag color={displayStatusColor[s] ?? 'default'}>{s}</Tag>
            )
          },
          { title: '类型', dataIndex: 'item_type', width: 88 },
          { title: 'job_id', dataIndex: 'job_id', width: 200, ellipsis: true },
          { title: '任务类型', dataIndex: 'job_type', width: 100 },
          {
            title: '处理类型',
            dataIndex: 'execution_mode',
            width: 100,
            render: (m: string | null) => (m ? executionModeLabel[m] ?? m : '—')
          },
          {
            title: '状态',
            dataIndex: 'status',
            width: 100,
            render: (s: string) => <Tag color={statusColor[s] ?? 'default'}>{s}</Tag>
          },
          { title: '型号', dataIndex: 'tasook_no', width: 120 },
          { title: '卫星', dataIndex: 'satellite_no', width: 110 },
          { title: '测试阶段', dataIndex: 'test_batch_name', width: 140, ellipsis: true },
          {
            title: '进度',
            dataIndex: 'progress_percent',
            width: 90,
            render: (p: number) => `${Number(p).toFixed(1)}%`
          },
          { title: '当前步骤', dataIndex: 'current_step', width: 140, ellipsis: true },
          { title: '计划执行', dataIndex: 'scheduled_at', width: 180 },
          { title: '创建时间', dataIndex: 'created_at', width: 180 },
          { title: '结束时间', dataIndex: 'end_time', width: 180 }
        ]}
      />

      <Drawer
        title={`运行明细 — ${detailTitle}`}
        open={detailOpen}
        onClose={() => setDetailOpen(false)}
        width={720}
      >
        <Table<TaskExecutionRecord>
          loading={detailLoading}
          rowKey="run_id"
          dataSource={detailRows}
          pagination={false}
          size="small"
          columns={[
            { title: 'run_id', dataIndex: 'run_id', ellipsis: true },
            { title: '展示状态', dataIndex: 'display_status', width: 110 },
            { title: '状态', dataIndex: 'status', width: 90 },
            { title: '开始', dataIndex: 'started_at', width: 170 },
            { title: '结束', dataIndex: 'ended_at', width: 170 },
            {
              title: '结果',
              key: 'result',
              render: (_, r) =>
                r.error_code ? (
                  <span style={{ color: '#cf1322' }}>
                    {r.error_code}: {r.error_msg}
                  </span>
                ) : (
                  r.status
                )
            }
          ]}
        />
      </Drawer>
    </Card>
  );
}

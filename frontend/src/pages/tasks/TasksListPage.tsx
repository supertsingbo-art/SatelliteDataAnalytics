import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
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
import type { ColumnsType } from 'antd/es/table';
import { Link } from 'react-router-dom';
import { ReloadOutlined } from '@ant-design/icons';
import { tasksApi } from '@/api/tasks';
import type {
  TaskExecutionRecord,
  TaskListItemV2,
  TaskProcessedData,
  TaskProcessedDataColumn
} from '@/api/types';

const { Paragraph, Text } = Typography;

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

const statusSummaryColor = (summary: string): string => {
  const s = summary.toLowerCase();
  if (s.includes('cancelled')) return 'default';
  if (s.includes('执行中') || s.includes('running')) return 'processing';
  if (s.includes('完毕') || s.includes('succeeded')) return 'success';
  if (s.includes('failed') || s.includes('失败')) return 'error';
  if (s.includes('待执行')) return 'default';
  if (s.includes('定时')) return 'blue';
  return 'default';
};

const cancellableStatuses = new Set(['Queued', 'Running']);

function rowKey(r: TaskListItemV2): string {
  return `${r.item_type}:${r.item_id}`;
}

function canExecute(row: TaskListItemV2): boolean {
  if (row.can_execute === true) return true;
  if (row.can_execute === false) return false;
  if (row.item_type === 'SCHEDULE') return true;
  return (
    row.item_type === 'RUN' &&
    row.job_type === 'PREPROCESS' &&
    row.status === 'Queued' &&
    (row.execution_mode === 'IMMEDIATE' || row.execution_mode == null) &&
    row.display_status === '待执行'
  );
}

function isImmediateRun(row: TaskListItemV2): boolean {
  return row.item_type === 'RUN' && (row.execution_mode === 'IMMEDIATE' || row.execution_mode == null);
}

function statusSummaryText(row: TaskListItemV2): string {
  if (row.status_summary) return row.status_summary;
  if (row.status === 'Cancelled') return 'cancelled';
  const parts = [row.display_status, row.status, row.current_step].filter(Boolean);
  return parts.join(' · ');
}

export function TasksListPage() {
  const [rows, setRows] = useState<TaskListItemV2[]>([]);
  const [loading, setLoading] = useState(false);
  const [cancellingId, setCancellingId] = useState<string | null>(null);
  const [executingKey, setExecutingKey] = useState<string | null>(null);
  const [deletingId, setDeletingId] = useState<string | null>(null);
  const [detailOpen, setDetailOpen] = useState(false);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detailRows, setDetailRows] = useState<TaskExecutionRecord[]>([]);
  const [detailTitle, setDetailTitle] = useState('');
  const [dataOpen, setDataOpen] = useState(false);
  const [dataLoading, setDataLoading] = useState(false);
  const [processedData, setProcessedData] = useState<TaskProcessedData | null>(null);
  const [dataTitle, setDataTitle] = useState('');
  const [dataRunId, setDataRunId] = useState<string | null>(null);
  const [dataPage, setDataPage] = useState(1);
  const [dataPageSize, setDataPageSize] = useState(50);
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
      (r) =>
        r.display_status === '任务执行中' ||
        r.display_status === '待执行' ||
        r.status === 'Running'
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

  const handleReExecute = async (row: TaskListItemV2) => {
    if (!row.run_id) return;
    setExecutingKey(rowKey(row));
    try {
      const res = await tasksApi.reExecuteRun(row.run_id);
      message.success(`已重新提交执行（${res.display_status}）`);
      await load();
    } catch {
      /* axios 已提示 */
    } finally {
      setExecutingKey(null);
    }
  };

  const handleDelete = async (runId: string) => {
    setDeletingId(runId);
    try {
      await tasksApi.deleteRun(runId);
      message.success('任务已删除');
      await load();
    } catch {
      /* axios 已提示 */
    } finally {
      setDeletingId(null);
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

  const loadProcessedData = useCallback(async (runId: string, page: number, pageSize: number) => {
    setDataLoading(true);
    try {
      const data = await tasksApi.getProcessedData(runId, { page, pageSize });
      setProcessedData(data);
      setDataPage(data.page);
      setDataPageSize(data.page_size);
    } finally {
      setDataLoading(false);
    }
  }, []);

  const openProcessedData = async (row: TaskListItemV2) => {
    if (!row.run_id) return;
    setDataTitle(row.job_id ?? row.run_id);
    setDataRunId(row.run_id);
    setDataPage(1);
    setDataPageSize(50);
    setDataOpen(true);
    setProcessedData(null);
    await loadProcessedData(row.run_id, 1, 50);
  };

  const handleDataPageChange = (page: number, pageSize: number) => {
    if (!dataRunId) return;
    setDataPage(page);
    setDataPageSize(pageSize);
    void loadProcessedData(dataRunId, page, pageSize);
  };

  const dataColumns = useMemo((): ColumnsType<Record<string, unknown>> => {
    if (!processedData) return [];
    const paramCols: ColumnsType<Record<string, unknown>> = processedData.columns.map(
      (col: TaskProcessedDataColumn) => ({
        title: col.label,
        key: col.param_id,
        width: 120,
        ellipsis: true,
        render: (_: unknown, record: Record<string, unknown>) => {
          const cells = record.cells as Record<string, { value: number | null; is_outlier: boolean }>;
          const cell = cells[col.param_id];
          if (!cell || cell.value == null) return '—';
          const text = Number(cell.value).toFixed(4);
          return cell.is_outlier ? (
            <Text type="danger" strong title="野值">
              {text}
            </Text>
          ) : (
            text
          );
        }
      })
    );
    return [
      {
        title: '时间',
        dataIndex: 'ts',
        key: 'ts',
        width: 200,
        fixed: 'left'
      },
      ...paramCols
    ];
  }, [processedData]);

  const dataTableRows = useMemo(() => {
    if (!processedData) return [];
    return processedData.rows.map((r) => ({
      key: r.ts,
      ts: r.ts,
      cells: r.cells
    }));
  }, [processedData]);

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
        立即任务创建后点击「执行」；已完成任务可「重复执行」或「删除」。「数据明细」展示各参数在各时间点的入仓值，野值以红色标出。
      </Paragraph>
      <Table<TaskListItemV2>
        loading={loading}
        rowKey={rowKey}
        dataSource={rows}
        pagination={{ pageSize: 20, showSizeChanger: true }}
        scroll={{ x: 1400 }}
        columns={[
          {
            title: '操作',
            key: 'act',
            width: 340,
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
                {r.can_re_execute && r.run_id && (
                  <Button
                    size="small"
                    loading={executingKey === rowKey(r)}
                    onClick={() => void handleReExecute(r)}
                  >
                    重复执行
                  </Button>
                )}
                {r.can_view_data && r.run_id && (
                  <Button size="small" onClick={() => void openProcessedData(r)}>
                    数据明细
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
                {r.can_delete && r.run_id && (
                  <Popconfirm
                    title="确认删除该任务？"
                    description="将删除任务记录及关联元数据，ClickHouse 中的点数据仍保留。"
                    onConfirm={() => void handleDelete(r.run_id!)}
                    okText="删除"
                    cancelText="取消"
                  >
                    <Button
                      type="link"
                      danger
                      size="small"
                      loading={deletingId === r.run_id}
                      style={{ padding: 0 }}
                    >
                      删除
                    </Button>
                  </Popconfirm>
                )}
                {r.run_id && cancellableStatuses.has(r.status) && (
                  <Popconfirm
                    title="确认取消该任务？"
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
            title: '任务状态',
            key: 'status_summary',
            width: 220,
            ellipsis: true,
            render: (_, r) => {
              const text = statusSummaryText(r);
              return <Tag color={statusSummaryColor(text)}>{text}</Tag>;
            }
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
          { title: '型号', dataIndex: 'tasook_no', width: 120 },
          { title: '卫星', dataIndex: 'satellite_no', width: 110 },
          { title: '测试阶段', dataIndex: 'test_batch_name', width: 140, ellipsis: true },
          {
            title: '进度',
            dataIndex: 'progress_percent',
            width: 90,
            render: (p: number) => `${Number(p).toFixed(1)}%`
          },
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
            { title: '任务状态', dataIndex: 'display_status', width: 110 },
            { title: '开始', dataIndex: 'started_at', width: 170 },
            { title: '结束', dataIndex: 'ended_at', width: 170 },
            {
              title: '结果',
              key: 'result',
              render: (_, rec) =>
                rec.error_code ? (
                  <span style={{ color: '#cf1322' }}>
                    {rec.error_code}: {rec.error_msg}
                  </span>
                ) : (
                  rec.status
                )
            }
          ]}
        />
      </Drawer>

      <Drawer
        title={`数据明细 — ${dataTitle}`}
        open={dataOpen}
        onClose={() => {
          setDataOpen(false);
          setDataRunId(null);
        }}
        width="90%"
      >
        {processedData && processedData.total > 0 && (
          <Paragraph type="secondary" style={{ marginBottom: 8 }}>
            共 {processedData.total} 个时间点，当前第 {processedData.page} 页（每页 {processedData.page_size}{' '}
            行）。野值以红色标出。
          </Paragraph>
        )}
        <Table
          loading={dataLoading}
          rowKey="key"
          dataSource={dataTableRows}
          columns={dataColumns}
          scroll={{ x: 'max-content', y: 480 }}
          pagination={{
            current: dataPage,
            pageSize: dataPageSize,
            total: processedData?.total ?? 0,
            showSizeChanger: true,
            pageSizeOptions: [20, 50, 100, 200],
            showTotal: (total) => `共 ${total} 个时间点`,
            onChange: (page, pageSize) => handleDataPageChange(page, pageSize)
          }}
          size="small"
        />
      </Drawer>
    </Card>
  );
}

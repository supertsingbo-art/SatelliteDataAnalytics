import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  Alert,
  Badge,
  Button,
  Card,
  Drawer,
  Modal,
  Popconfirm,
  Progress,
  Segmented,
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
  OutlierReviewItem,
  OutlierReviewList,
  OutlierReviewSummary,
  TaskExecutionRecord,
  TaskListItemV2,
  TaskOutlierSegmentItem,
  TaskOutlierSegments,
  TaskProcessedData,
  TaskProcessedDataCell,
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

type DataViewMode = 'all' | 'outlier-points' | 'outlier-segments';

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
  const [dataViewMode, setDataViewMode] = useState<DataViewMode>('all');
  const [reviewSummary, setReviewSummary] = useState<OutlierReviewSummary | null>(null);
  const [reviewList, setReviewList] = useState<OutlierReviewList | null>(null);
  const [reviewStatusFilter, setReviewStatusFilter] = useState<string>('PENDING');
  const [selectedReviewKeys, setSelectedReviewKeys] = useState<string[]>([]);
  const [reviewSubmitting, setReviewSubmitting] = useState(false);
  const [outlierSegments, setOutlierSegments] = useState<TaskOutlierSegments | null>(null);
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

  const loadReviewSummary = useCallback(async (runId: string) => {
    try {
      const summary = await tasksApi.getOutlierReviewSummary(runId);
      setReviewSummary(summary);
    } catch {
      setReviewSummary(null);
    }
  }, []);

  const loadOutlierReviews = useCallback(
    async (runId: string, page: number, pageSize: number, status: string) => {
      setDataLoading(true);
      try {
        const data = await tasksApi.getOutlierReviews(runId, {
          page,
          pageSize,
          status: status === 'ALL' ? undefined : status
        });
        setReviewList(data);
        setDataPage(data.page);
        setDataPageSize(data.page_size);
        setSelectedReviewKeys([]);
      } finally {
        setDataLoading(false);
      }
    },
    []
  );

  const loadOutlierSegments = useCallback(async (runId: string) => {
    setDataLoading(true);
    try {
      const data = await tasksApi.getOutlierSegments(runId);
      setOutlierSegments(data);
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
    setDataViewMode('all');
    setDataOpen(true);
    setProcessedData(null);
    setReviewList(null);
    setReviewSummary(null);
    setReviewStatusFilter('PENDING');
    setOutlierSegments(null);
    void loadReviewSummary(row.run_id);
    await loadProcessedData(row.run_id, 1, 50);
  };

  const handleDataViewModeChange = (mode: DataViewMode) => {
    if (!dataRunId) return;
    setDataViewMode(mode);
    setDataPage(1);
    if (mode === 'all') {
      setReviewList(null);
      setOutlierSegments(null);
      void loadProcessedData(dataRunId, 1, dataPageSize);
    } else if (mode === 'outlier-points') {
      setProcessedData(null);
      setOutlierSegments(null);
      void loadReviewSummary(dataRunId);
      void loadOutlierReviews(dataRunId, 1, dataPageSize, reviewStatusFilter);
    } else {
      setProcessedData(null);
      setReviewList(null);
      void loadOutlierSegments(dataRunId);
    }
  };

  const handleDataPageChange = (page: number, pageSize: number) => {
    if (!dataRunId) return;
    setDataPage(page);
    setDataPageSize(pageSize);
    if (dataViewMode === 'outlier-points') {
      void loadOutlierReviews(dataRunId, page, pageSize, reviewStatusFilter);
    } else if (dataViewMode === 'all') {
      void loadProcessedData(dataRunId, page, pageSize);
    }
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
          const cells = record.cells as Record<string, TaskProcessedDataCell>;
          const cell = cells[col.param_id];
          if (!cell || cell.value == null) return '—';
          const text = Number(cell.value).toFixed(4);
          const status = cell.review_status?.toUpperCase();
          if (status === 'JITTER') {
            return (
              <Text style={{ color: '#389e0d' }} strong title="已确认非离群（抖动）">
                {text}
              </Text>
            );
          }
          if (status === 'CONFIRMED' || cell.is_confirmed_outlier) {
            return (
              <Text style={{ color: '#cf1322' }} strong title="已确认离群">
                {text}
              </Text>
            );
          }
          if (status === 'PENDING' || cell.is_outlier) {
            return (
              <Text style={{ color: '#d46b08' }} strong title="待复核离群候选">
                {text}
              </Text>
            );
          }
          return (
            <Text style={{ color: 'rgba(0, 0, 0, 0.88)' }} title="正常值">
              {text}
            </Text>
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

  const reviewStatusTag = (status: string) => {
    switch (status) {
      case 'CONFIRMED':
        return <Tag color="success">已确认离群</Tag>;
      case 'JITTER':
        return <Tag>抖动</Tag>;
      default:
        return <Tag color="warning">待复核</Tag>;
    }
  };

  const submitReviews = async (items: { paramId: string; ts: string; status: 'CONFIRMED' | 'JITTER' }[]) => {
    if (!dataRunId || items.length === 0) return;
    setReviewSubmitting(true);
    try {
      const summary = await tasksApi.submitOutlierReviews(dataRunId, items);
      setReviewSummary(summary);
      message.success(`已更新 ${items.length} 条复核记录`);
      await loadOutlierReviews(dataRunId, dataPage, dataPageSize, reviewStatusFilter);
      await load();
    } catch {
      /* axios 已提示 */
    } finally {
      setReviewSubmitting(false);
    }
  };

  const handleCompleteReview = () => {
    if (!dataRunId || !reviewSummary) return;
    Modal.confirm({
      title: '完成离群复核',
      content: '将全部已复核结果固化为「已确认离群时间段」，并结束本任务的离群复核流程。是否继续？',
      okText: '完成复核',
      cancelText: '取消',
      onOk: async () => {
        setReviewSubmitting(true);
        try {
          await tasksApi.completeOutlierReview(dataRunId);
          message.success('离群复核已完成');
          await loadReviewSummary(dataRunId);
          if (dataViewMode === 'outlier-segments') {
            await loadOutlierSegments(dataRunId);
          }
          await load();
        } catch {
          /* axios 已提示 */
        } finally {
          setReviewSubmitting(false);
        }
      }
    });
  };

  const reviewTableRows = useMemo(() => {
    if (!reviewList) return [];
    return reviewList.items.map((item) => ({
      key: item.review_id,
      ...item
    }));
  }, [reviewList]);

  const reviewColumns: ColumnsType<OutlierReviewItem & { key: string }> = useMemo(
    () => [
      {
        title: '时间',
        dataIndex: 'ts',
        key: 'ts',
        width: 200,
        fixed: 'left'
      },
      {
        title: '参数',
        dataIndex: 'param_label',
        key: 'param_label',
        width: 200,
        ellipsis: true,
        render: (_: unknown, record) => (
          <span title={record.param_id}>
            {record.param_label}
            <Text type="secondary" style={{ marginLeft: 6, fontSize: 12 }}>
              ({record.param_id})
            </Text>
          </span>
        )
      },
      {
        title: '离群值',
        dataIndex: 'value',
        key: 'value',
        width: 120,
        render: (v: number) => (
          <Text type="danger" strong>
            {Number(v).toFixed(4)}
          </Text>
        )
      },
      {
        title: '判定方法',
        dataIndex: 'outlier_method',
        key: 'outlier_method',
        width: 110
      },
      {
        title: '复核状态',
        dataIndex: 'review_status',
        key: 'review_status',
        width: 110,
        render: (s: string) => reviewStatusTag(s)
      },
      {
        title: '操作',
        key: 'actions',
        width: 180,
        fixed: 'right',
        render: (_: unknown, record) =>
          record.review_status === 'PENDING' ? (
            <Space size="small">
              <Button
                type="link"
                size="small"
                loading={reviewSubmitting}
                onClick={() =>
                  void submitReviews([{ paramId: record.param_id, ts: record.ts, status: 'CONFIRMED' }])
                }
              >
                确认离群
              </Button>
              <Button
                type="link"
                size="small"
                loading={reviewSubmitting}
                onClick={() =>
                  void submitReviews([{ paramId: record.param_id, ts: record.ts, status: 'JITTER' }])
                }
              >
                标为抖动
              </Button>
            </Space>
          ) : (
            '—'
          )
      }
    ],
    [reviewSubmitting]
  );

  const reviewProgressPercent = useMemo(() => {
    if (!reviewSummary || reviewSummary.auto_count === 0) return 100;
    const done = reviewSummary.confirmed_count + reviewSummary.jitter_count;
    return Math.round((done / reviewSummary.auto_count) * 100);
  }, [reviewSummary]);

  const segmentTableRows = useMemo(() => {
    if (!outlierSegments) return [];
    return outlierSegments.items.map((item, idx) => ({
      key: `${item.param_id}-${item.segment_start}-${idx}`,
      ...item
    }));
  }, [outlierSegments]);

  const segmentColumns: ColumnsType<TaskOutlierSegmentItem & { key: string }> = useMemo(
    () => [
      {
        title: '参数',
        dataIndex: 'param_label',
        key: 'param_label',
        width: 200,
        ellipsis: true,
        render: (_: unknown, record) => (
          <span title={record.param_id}>
            {record.param_label}
            <Text type="secondary" style={{ marginLeft: 6, fontSize: 12 }}>
              ({record.param_id})
            </Text>
          </span>
        )
      },
      { title: '段开始', dataIndex: 'segment_start', key: 'segment_start', width: 200 },
      { title: '段结束', dataIndex: 'segment_end', key: 'segment_end', width: 200 },
      {
        title: '持续(秒)',
        dataIndex: 'duration_seconds',
        key: 'duration_seconds',
        width: 100,
        render: (v: number) => Number(v).toFixed(1)
      },
      { title: '判定方法', dataIndex: 'outlier_method', key: 'outlier_method', width: 110 }
    ],
    []
  );

  const dataTotal =
    dataViewMode === 'outlier-points' ? (reviewList?.total ?? 0) : (processedData?.total ?? 0);

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
                  <Badge count={r.outlier_pending_count ?? 0} size="small" offset={[6, 0]}>
                    <Button size="small" onClick={() => void openProcessedData(r)}>
                      数据明细
                    </Button>
                  </Badge>
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
          setDataViewMode('all');
          setProcessedData(null);
          setReviewList(null);
          setReviewSummary(null);
          setOutlierSegments(null);
        }}
        width="90%"
      >
        <Space direction="vertical" style={{ width: '100%', marginBottom: 12 }} size="small">
          <Segmented
            value={dataViewMode}
            onChange={(v) => handleDataViewModeChange(v as DataViewMode)}
            options={[
              { label: '全部数据（矩阵）', value: 'all' },
              { label: '离群点清单', value: 'outlier-points' },
              { label: '离群时间段', value: 'outlier-segments' }
            ]}
          />
          {dataViewMode === 'all' && processedData && processedData.total > 0 && (
            <Paragraph type="secondary" style={{ marginBottom: 0 }}>
              共 {processedData.total} 个时间点，当前第 {processedData.page} 页（每页 {processedData.page_size}{' '}
              行）。橙=待复核离群，红=已确认离群，绿=已确认非离群（抖动），黑=正常值。
            </Paragraph>
          )}
          {dataViewMode === 'outlier-points' && reviewSummary && (
            <>
              <Progress
                percent={reviewProgressPercent}
                status={reviewSummary.pending_count > 0 ? 'active' : 'success'}
                format={() =>
                  `已复核 ${reviewSummary.confirmed_count + reviewSummary.jitter_count} / ${reviewSummary.auto_count}`
                }
              />
              <Space wrap>
                <Text type="secondary">待复核 {reviewSummary.pending_count}</Text>
                <Text type="secondary">已确认 {reviewSummary.confirmed_count}</Text>
                <Text type="secondary">抖动 {reviewSummary.jitter_count}</Text>
                <Segmented
                  size="small"
                  value={reviewStatusFilter}
                  onChange={(v) => {
                    const st = v as string;
                    setReviewStatusFilter(st);
                    if (dataRunId) {
                      setDataPage(1);
                      void loadOutlierReviews(dataRunId, 1, dataPageSize, st);
                    }
                  }}
                  options={[
                    { label: '待复核', value: 'PENDING' },
                    { label: '已确认', value: 'CONFIRMED' },
                    { label: '抖动', value: 'JITTER' },
                    { label: '全部', value: 'ALL' }
                  ]}
                />
                <Button
                  size="small"
                  disabled={selectedReviewKeys.length === 0}
                  loading={reviewSubmitting}
                  onClick={() => {
                    const items = (reviewList?.items ?? [])
                      .filter((i) => selectedReviewKeys.includes(i.review_id) && i.review_status === 'PENDING')
                      .map((i) => ({ paramId: i.param_id, ts: i.ts, status: 'CONFIRMED' as const }));
                    void submitReviews(items);
                  }}
                >
                  批量确认
                </Button>
                <Button
                  size="small"
                  disabled={selectedReviewKeys.length === 0}
                  loading={reviewSubmitting}
                  onClick={() => {
                    const items = (reviewList?.items ?? [])
                      .filter((i) => selectedReviewKeys.includes(i.review_id) && i.review_status === 'PENDING')
                      .map((i) => ({ paramId: i.param_id, ts: i.ts, status: 'JITTER' as const }));
                    void submitReviews(items);
                  }}
                >
                  批量标为抖动
                </Button>
                <Button
                  type="primary"
                  size="small"
                  disabled={(reviewSummary.pending_count ?? 0) > 0 || reviewSummary.auto_count === 0}
                  loading={reviewSubmitting}
                  onClick={handleCompleteReview}
                >
                  完成复核
                </Button>
              </Space>
            </>
          )}
          {dataViewMode === 'outlier-segments' && outlierSegments && (
            <>
              {!outlierSegments.review_completed && (
                <Alert
                  type="info"
                  showIcon
                  message="当前展示算法自动离群时间段；完成全部点复核后，将切换为已确认离群段。"
                />
              )}
              <Paragraph type="secondary" style={{ marginBottom: 0 }}>
                共 {outlierSegments.total} 段（{outlierSegments.segment_kind === 'CONFIRMED' ? '已确认' : '算法自动'}）。
              </Paragraph>
            </>
          )}
        </Space>
        {dataViewMode === 'all' && (
          <Table
            loading={dataLoading}
            rowKey="key"
            dataSource={dataTableRows}
            columns={dataColumns}
            scroll={{ x: 'max-content', y: 480 }}
            pagination={{
              current: dataPage,
              pageSize: dataPageSize,
              total: dataTotal,
              showSizeChanger: true,
              pageSizeOptions: [20, 50, 100, 200],
              showTotal: (total) => `共 ${total} 个时间点`,
              onChange: (page, pageSize) => handleDataPageChange(page, pageSize)
            }}
            size="small"
          />
        )}
        {dataViewMode === 'outlier-points' && (
          <Table
            loading={dataLoading}
            rowKey="key"
            dataSource={reviewTableRows}
            columns={reviewColumns}
            scroll={{ x: 'max-content', y: 480 }}
            rowSelection={{
              selectedRowKeys: selectedReviewKeys,
              onChange: (keys) => setSelectedReviewKeys(keys as string[]),
              getCheckboxProps: (record) => ({ disabled: record.review_status !== 'PENDING' })
            }}
            pagination={{
              current: dataPage,
              pageSize: dataPageSize,
              total: dataTotal,
              showSizeChanger: true,
              pageSizeOptions: [20, 50, 100, 200],
              showTotal: (total) => `共 ${total} 条复核记录`,
              onChange: (page, pageSize) => handleDataPageChange(page, pageSize)
            }}
            size="small"
          />
        )}
        {dataViewMode === 'outlier-segments' && (
          <Table
            loading={dataLoading}
            rowKey="key"
            dataSource={segmentTableRows}
            columns={segmentColumns}
            scroll={{ x: 'max-content', y: 480 }}
            pagination={{
              pageSize: 50,
              showSizeChanger: true,
              pageSizeOptions: [20, 50, 100, 200],
              showTotal: (total) => `共 ${total} 段`,
              hideOnSinglePage: true
            }}
            size="small"
          />
        )}
      </Drawer>
    </Card>
  );
}

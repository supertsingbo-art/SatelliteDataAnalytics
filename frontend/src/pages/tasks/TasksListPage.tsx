import { useCallback, useEffect, useState } from 'react';
import { Button, Card, Popconfirm, Space, Table, Tag, Typography, message } from 'antd';
import { Link } from 'react-router-dom';
import { ReloadOutlined } from '@ant-design/icons';
import { tasksApi } from '@/api/tasks';
import type { TaskRunListItem } from '@/api/types';

const { Paragraph } = Typography;

function taskDetailPath(jobType: string, runId: string): string {
  const q = `runId=${encodeURIComponent(runId)}`;
  if (jobType === 'PREPROCESS') {
    return `/tasks/preprocess?${q}`;
  }
  return `/tasks/pipeline?${q}`;
}

const statusColor: Record<string, string> = {
  Queued: 'default',
  Running: 'processing',
  Succeeded: 'success',
  Failed: 'error',
  Timeout: 'warning',
  Cancelled: 'default'
};

const cancellableStatuses = new Set(['Queued', 'Running']);

export function TasksListPage() {
  const [rows, setRows] = useState<TaskRunListItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [cancellingId, setCancellingId] = useState<string | null>(null);

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
        数据来源：<code>GET /api/v1/tasks</code>（按创建时间倒序）。「详情」按任务类型打开对应页面展示完整信息；运行中或排队中的任务可「取消」。
      </Paragraph>
      <Table<TaskRunListItem>
        loading={loading}
        rowKey={(r) => r.run_id}
        dataSource={rows}
        pagination={{ pageSize: 20, showSizeChanger: true }}
        scroll={{ x: 1200 }}
        columns={[
          {
            title: '操作',
            key: 'act',
            width: 140,
            fixed: 'left',
            render: (_, r) => (
              <Space size="small">
                <Link to={taskDetailPath(r.job_type, r.run_id)}>详情</Link>
                {cancellableStatuses.has(r.status) && (
                  <Popconfirm
                    title="确认取消该任务？"
                    description="已运行的任务将在下一检查点停止，状态变为 Cancelled。"
                    onConfirm={() => void handleCancel(r.run_id)}
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
          { title: 'run_id', dataIndex: 'run_id', width: 280, ellipsis: true },
          { title: 'job_id', dataIndex: 'job_id', width: 200, ellipsis: true },
          { title: '类型', dataIndex: 'job_type', width: 100 },
          { title: '触发', dataIndex: 'trigger_type', width: 88 },
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
          { title: '创建时间', dataIndex: 'created_at', width: 180 },
          { title: '结束时间', dataIndex: 'end_time', width: 180 }
        ]}
      />
    </Card>
  );
}

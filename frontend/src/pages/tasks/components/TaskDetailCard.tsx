import { Card, Descriptions, Progress, Spin, Typography } from 'antd';
import type { TaskRunDetail } from '@/api/types';

const terminalStatuses = new Set(['Succeeded', 'Failed', 'Timeout', 'Cancelled']);

function formatTime(iso: string | null | undefined) {
  if (!iso) return '—';
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString();
}

type Props = {
  detail: TaskRunDetail | null;
  loading?: boolean;
  title?: string;
};

export function TaskDetailCard({ detail, loading, title = '任务详情' }: Props) {
  if (loading && !detail) {
    return (
      <Card size="small" title={title} style={{ marginBottom: 16 }}>
        <Spin />
      </Card>
    );
  }

  if (!detail) {
    return null;
  }

  const progressStatus =
    detail.status === 'Failed'
      ? 'exception'
      : detail.status === 'Succeeded'
        ? 'success'
        : terminalStatuses.has(detail.status)
          ? 'normal'
          : 'active';

  return (
    <Card size="small" title={title} style={{ marginBottom: 16 }}>
      <Descriptions column={2} size="small" bordered>
        <Descriptions.Item label="run_id" span={2}>
          {detail.run_id}
        </Descriptions.Item>
        <Descriptions.Item label="job_id" span={2}>
          {detail.job_id}
        </Descriptions.Item>
        <Descriptions.Item label="类型">{detail.job_type}</Descriptions.Item>
        <Descriptions.Item label="触发">{detail.trigger_type}</Descriptions.Item>
        <Descriptions.Item label="状态">{detail.status}</Descriptions.Item>
        <Descriptions.Item label="当前步骤">{detail.current_step ?? '—'}</Descriptions.Item>
        <Descriptions.Item label="型号">{detail.tasook_no}</Descriptions.Item>
        <Descriptions.Item label="卫星">{detail.satellite_no}</Descriptions.Item>
        <Descriptions.Item label="测试阶段" span={2}>
          {detail.test_batch_name ?? '—'}
        </Descriptions.Item>
        <Descriptions.Item label="开始时间">{formatTime(detail.window_start)}</Descriptions.Item>
        <Descriptions.Item label="结束时间">{formatTime(detail.window_end)}</Descriptions.Item>
        <Descriptions.Item label="筛选模板" span={2}>
          {detail.filter_template_id
            ? `${detail.filter_template_id} v${detail.filter_template_version ?? '?'}`
            : '—'}
        </Descriptions.Item>
        {(detail.job_type === 'PIPELINE' || detail.algorithm_template_id) && (
          <Descriptions.Item label="算法模板" span={2}>
            {detail.algorithm_template_id
              ? `${detail.algorithm_template_id} v${detail.algorithm_template_version ?? '?'}`
              : '—'}
          </Descriptions.Item>
        )}
        <Descriptions.Item label="任务开始">{formatTime(detail.start_time)}</Descriptions.Item>
        <Descriptions.Item label="任务结束">{formatTime(detail.end_time)}</Descriptions.Item>
        <Descriptions.Item label="创建时间" span={2}>
          {formatTime(detail.created_at)}
        </Descriptions.Item>
      </Descriptions>
      <div style={{ marginTop: 16 }}>
        <Typography.Text type="secondary">进度 </Typography.Text>
        <Progress percent={Number(detail.progress_percent)} status={progressStatus} />
      </div>
      {detail.error_code && (
        <Typography.Text type="danger" style={{ display: 'block', marginTop: 8 }}>
          {detail.error_code}: {detail.error_msg}
        </Typography.Text>
      )}
    </Card>
  );
}

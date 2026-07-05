import { Card, Descriptions, Progress, Spin, Typography } from 'antd';
import type { TaskRunDetail } from '@/api/types';
import { PreprocessConflictPanel } from '@/pages/tasks/components/PreprocessConflictPanel';

const terminalStatuses = new Set(['Succeeded', 'Failed', 'Timeout', 'Cancelled']);

function formatTime(iso: string | null | undefined) {
  if (!iso) return '—';
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString();
}

const EXECUTION_MODE_LABEL: Record<string, string> = {
  IMMEDIATE: '一次性立即执行',
  ONCE_SCHEDULED: '一次性指定时间执行',
  DAILY_INSTANCE: '每天定时（实例）',
  DAILY_RECURRING: '每天定时（计划）'
};

function formatTemplateDisplay(
  name: string | null | undefined,
  version: number | null | undefined,
  id: string | null | undefined
) {
  if (!name && !id) return '—';
  const label = name?.trim() || id;
  if (version != null && label) {
    return `${label} v${version}`;
  }
  return label ?? '—';
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
        <Descriptions.Item label="处理类型">
          {detail.execution_mode
            ? (EXECUTION_MODE_LABEL[detail.execution_mode] ?? detail.execution_mode)
            : '—'}
        </Descriptions.Item>
        <Descriptions.Item label="计划执行时间" span={2}>
          {formatTime(detail.scheduled_at)}
        </Descriptions.Item>
        <Descriptions.Item label="当前步骤">{detail.current_step ?? '—'}</Descriptions.Item>
        <Descriptions.Item label="型号">{detail.tasook_no}</Descriptions.Item>
        <Descriptions.Item label="卫星">{detail.satellite_no}</Descriptions.Item>
        <Descriptions.Item label="测试阶段" span={2}>
          {detail.test_batch_name ?? '—'}
        </Descriptions.Item>
        <Descriptions.Item label="开始时间">{formatTime(detail.window_start)}</Descriptions.Item>
        <Descriptions.Item label="结束时间">{formatTime(detail.window_end)}</Descriptions.Item>
        <Descriptions.Item label="筛选模板" span={2}>
          {detail.job_type === 'PIPELINE' && !detail.filter_template_id
            ? '未启用（仅算法）'
            : formatTemplateDisplay(
                detail.filter_template_name,
                detail.filter_template_version,
                detail.filter_template_id
              )}
        </Descriptions.Item>
        {detail.job_type === 'PIPELINE' && (
          <Descriptions.Item label="执行模式" span={2}>
            {detail.filter_template_id ? '预处理 + 算法' : '仅算法（读已有预处理数据）'}
          </Descriptions.Item>
        )}
        {(detail.job_type === 'PIPELINE' || detail.algorithm_template_id) && (
          <Descriptions.Item label="算法模板" span={2}>
            {formatTemplateDisplay(
              detail.algorithm_template_name,
              detail.algorithm_template_version,
              detail.algorithm_template_id
            )}
          </Descriptions.Item>
        )}
        <Descriptions.Item label="任务开始">{formatTime(detail.start_time)}</Descriptions.Item>
        <Descriptions.Item label="任务结束">{formatTime(detail.end_time)}</Descriptions.Item>
        <Descriptions.Item label="创建时间" span={2}>
          {formatTime(detail.created_at)}
        </Descriptions.Item>
        {detail.schedule_id && (
          <>
            <Descriptions.Item label="定时计划 ID" span={2}>
              {detail.schedule_id}
            </Descriptions.Item>
            <Descriptions.Item label="每日时刻">
              {detail.schedule_daily_time ?? '—'}
            </Descriptions.Item>
            <Descriptions.Item label="间隔天数">
              {detail.schedule_interval_days ?? '—'}
            </Descriptions.Item>
            <Descriptions.Item label="计划生效日" span={2}>
              {detail.schedule_effective_from ?? '—'}
            </Descriptions.Item>
          </>
        )}
      </Descriptions>
      <div style={{ marginTop: 16 }}>
        <Typography.Text type="secondary">进度 </Typography.Text>
        <Progress percent={Number(detail.progress_percent)} status={progressStatus} />
      </div>
      {detail.error_code === 'PRE_006' ? (
        <div style={{ marginTop: 12 }}>
          <Typography.Text type="danger" style={{ display: 'block', marginBottom: 8 }}>
            参数时间段冲突（PRE_006）
          </Typography.Text>
          <PreprocessConflictPanel
            errorMsg={detail.error_msg}
            conflictDetails={detail.conflict_details}
            onOpenTemplate={(templateId, version) => {
              window.location.href = `/templates/filters/${encodeURIComponent(templateId)}/versions/${version}`;
            }}
          />
        </div>
      ) : (
        detail.error_code && (
          <Typography.Text type="danger" style={{ display: 'block', marginTop: 8 }}>
            {detail.error_code}: {detail.error_msg}
          </Typography.Text>
        )
      )}
    </Card>
  );
}

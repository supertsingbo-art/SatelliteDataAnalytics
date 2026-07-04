import { Button, List, Space, Typography } from 'antd';
import type { PreprocessConflictDetail } from '@/api/types';

const { Text } = Typography;

export function formatConflictStatus(status: string): string {
  const normalized = status.trim().toUpperCase();
  if (normalized === 'ACTIVE') return '执行中';
  if (normalized === 'COMMITTED') return '已完成';
  return status;
}

export function formatParamDisplay(detail: Pick<PreprocessConflictDetail, 'param_id' | 'param_label'>): string {
  const label = detail.param_label?.trim();
  if (label && label !== detail.param_id) {
    return `${label} (${detail.param_id})`;
  }
  return label || detail.param_id;
}

export function formatTemplateDisplay(
  detail: Pick<
    PreprocessConflictDetail,
    'conflict_filter_template_name' | 'conflict_filter_template_version'
  >
): string {
  const name = detail.conflict_filter_template_name?.trim();
  const version = detail.conflict_filter_template_version;
  if (name) {
    return version != null ? `${name} v${version}` : name;
  }
  return version != null ? `模板 v${version}` : '未知模板';
}

interface PreprocessConflictPanelProps {
  errorMsg?: string | null;
  conflictDetails?: PreprocessConflictDetail[] | null;
  onOpenTemplate?: (templateId: string, version: number) => void;
  compact?: boolean;
}

export function PreprocessConflictPanel({
  errorMsg,
  conflictDetails,
  onOpenTemplate,
  compact = false
}: PreprocessConflictPanelProps) {
  const details = conflictDetails?.filter(Boolean) ?? [];

  if (details.length === 0) {
    return (
      <Space direction="vertical" size={4}>
        <Text type="secondary">
          同一参数在同一时间段内只能有一份高精度数据，当前任务与其他任务的处理结果发生冲突。
        </Text>
        {errorMsg && <Text>{errorMsg}</Text>}
      </Space>
    );
  }

  return (
    <Space direction="vertical" size={compact ? 4 : 8} style={{ width: '100%' }}>
      <Text type="secondary">
        同一参数在同一时间段内只能有一份高精度数据。下列参数与其他任务的处理结果发生冲突：
      </Text>
      <List
        size="small"
        dataSource={details}
        renderItem={(item) => (
          <List.Item style={{ paddingInline: 0 }}>
            <Space direction="vertical" size={2}>
              <Text>
                <Text strong>参数：</Text>
                {formatParamDisplay(item)}
              </Text>
              <Text>
                <Text strong>冲突状态：</Text>
                {formatConflictStatus(item.conflict_status)}
              </Text>
              <Text>
                <Text strong>冲突模板：</Text>
                {formatTemplateDisplay(item)}
                {onOpenTemplate && item.conflict_filter_template_id && (
                  <Button
                    type="link"
                    size="small"
                    style={{ paddingInline: 4, height: 'auto' }}
                    onClick={() =>
                      onOpenTemplate(
                        item.conflict_filter_template_id,
                        item.conflict_filter_template_version ?? 1
                      )
                    }
                  >
                    查看模板
                  </Button>
                )}
              </Text>
              {item.conflict_job_id && (
                <Text>
                  <Text strong>冲突任务：</Text>
                  {item.conflict_job_id}
                </Text>
              )}
            </Space>
          </List.Item>
        )}
      />
    </Space>
  );
}

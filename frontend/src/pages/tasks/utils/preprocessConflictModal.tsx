import { Button, Modal, Space, Typography } from 'antd';
import type { PreprocessConflictDetail } from '@/api/types';
import {
  PreprocessConflictPanel,
  formatConflictStatus
} from '@/pages/tasks/components/PreprocessConflictPanel';

const { Text } = Typography;

export type PreprocessConflictRetryPolicy = 'OVERWRITE' | 'SKIP';

export interface OpenPreprocessConflictModalOptions {
  errorMsg?: string | null;
  conflictDetails?: PreprocessConflictDetail[] | null;
  /** 是否允许带策略重复执行（PRE_006 且任务可 reexecute） */
  canRetry?: boolean;
  onRetry?: (policy: PreprocessConflictRetryPolicy) => Promise<void>;
  detailHref?: string;
  onOpenTemplate?: (templateId: string, version: number) => void;
}

function hasCommittedConflicts(details?: PreprocessConflictDetail[] | null): boolean {
  return (details ?? []).some(
    (d) => d.conflict_status.trim().toUpperCase() === 'COMMITTED'
  );
}

function hasActiveConflicts(details?: PreprocessConflictDetail[] | null): boolean {
  return (details ?? []).some((d) => d.conflict_status.trim().toUpperCase() === 'ACTIVE');
}

export function openPreprocessConflictModal(options: OpenPreprocessConflictModalOptions): void {
  const {
    errorMsg,
    conflictDetails,
    canRetry,
    onRetry,
    detailHref,
    onOpenTemplate
  } = options;

  const panel = (
    <PreprocessConflictPanel
      errorMsg={errorMsg}
      conflictDetails={conflictDetails}
      onOpenTemplate={onOpenTemplate}
      compact={!!canRetry && !!onRetry}
    />
  );

  const rulesBlock = (
    <Space direction="vertical" size={8} style={{ width: '100%' }}>
      <Text>检测到参数时间段冲突，请按下列规则选择处理策略：</Text>
      <Text>
        1) <Text strong>执行中冲突（状态：{formatConflictStatus('ACTIVE')}）</Text>：只能跳过，不支持覆盖。
      </Text>
      <Text>
        2) <Text strong>已完成冲突（状态：{formatConflictStatus('COMMITTED')}）</Text>：可覆盖或跳过。
      </Text>
      {panel}
      {detailHref && <a href={detailHref}>查看任务详情</a>}
      <Text type="secondary">选择「暂不处理」可关闭弹窗，稍后再试（例如等待或先取消冲突任务）。</Text>
    </Space>
  );

  if (canRetry && onRetry) {
    const showOverwrite = hasCommittedConflicts(conflictDetails);
    const showActiveHint = hasActiveConflicts(conflictDetails) && !showOverwrite;

    const modal = Modal.info({
      title: '参数时间段冲突',
      width: 560,
      closable: true,
      maskClosable: true,
      footer: null,
      content: (
        <Space direction="vertical" size={12} style={{ width: '100%' }}>
          {rulesBlock}
          {showActiveHint && (
            <Text type="warning">当前冲突均为执行中任务，请选择「跳过冲突参数并继续」。</Text>
          )}
          <Space wrap>
            {showOverwrite && (
              <Button
                type="primary"
                danger
                onClick={() => {
                  modal.destroy();
                  void onRetry('OVERWRITE');
                }}
              >
                覆盖已完成冲突并继续
              </Button>
            )}
            <Button
              type={showOverwrite ? 'default' : 'primary'}
              onClick={() => {
                modal.destroy();
                void onRetry('SKIP');
              }}
            >
              跳过冲突参数并继续
            </Button>
            <Button onClick={() => modal.destroy()}>暂不处理</Button>
          </Space>
        </Space>
      )
    });
    return;
  }

  Modal.warning({
    title: '参数时间段冲突',
    okText: '知道了',
    width: 560,
    content: (
      <Space direction="vertical" size={12} style={{ width: '100%' }}>
        {panel}
        {detailHref && <a href={detailHref}>查看任务详情</a>}
      </Space>
    )
  });
}

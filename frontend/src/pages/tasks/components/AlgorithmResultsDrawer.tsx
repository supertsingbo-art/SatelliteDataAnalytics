import { useEffect, useMemo, useState } from 'react';
import { Button, Drawer, Modal, Space, Table, Typography } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { tasksApi } from '@/api/tasks';
import type { TaskAlgorithmResultItem } from '@/api/types';

const { Paragraph, Text } = Typography;

type Props = {
  open: boolean;
  title: string;
  runId: string | null;
  onClose: () => void;
};

function formatLocalTime(value: string | null | undefined): string {
  if (!value) return '—';
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return value;
  const pad = (n: number) => n.toString().padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
}

function hasDetailJson(detailJson: string | null | undefined): boolean {
  const text = (detailJson ?? '').trim();
  if (!text || text === '{}' || text === '[]') return false;
  return true;
}

function formatDetailJson(detailJson: string): string {
  try {
    return JSON.stringify(JSON.parse(detailJson), null, 2);
  } catch {
    return detailJson;
  }
}

export function AlgorithmResultsDrawer({ open, title, runId, onClose }: Props) {
  const [loading, setLoading] = useState(false);
  const [items, setItems] = useState<TaskAlgorithmResultItem[]>([]);
  const [detailItem, setDetailItem] = useState<TaskAlgorithmResultItem | null>(null);

  useEffect(() => {
    if (!open || !runId) {
      setItems([]);
      return;
    }

    let cancelled = false;
    (async () => {
      setLoading(true);
      try {
        const data = await tasksApi.getAlgorithmResults(runId);
        if (!cancelled) setItems(data.items ?? []);
      } catch {
        if (!cancelled) setItems([]);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [open, runId]);

  const columns = useMemo<ColumnsType<TaskAlgorithmResultItem>>(
    () => [
      { title: '结果名称', dataIndex: 'metric_name', width: 140, ellipsis: true },
      { title: '算法', dataIndex: 'algorithm_code', width: 120, ellipsis: true },
      { title: '节点', dataIndex: 'node_id', width: 120, ellipsis: true },
      {
        title: '数值',
        dataIndex: 'metric_value',
        width: 120,
        render: (v: number) => (Number.isFinite(v) ? v.toFixed(4) : '—')
      },
      {
        title: '明细',
        key: 'detail',
        width: 80,
        render: (_, row) =>
          hasDetailJson(row.detail_json) ? (
            <Button type="link" size="small" style={{ padding: 0 }} onClick={() => setDetailItem(row)}>
              查看
            </Button>
          ) : (
            '—'
          )
      },
      {
        title: '时间窗',
        key: 'window',
        width: 200,
        ellipsis: true,
        render: (_, row) => `${formatLocalTime(row.window_start)} ~ ${formatLocalTime(row.window_end)}`
      }
    ],
    []
  );

  return (
    <>
      <Drawer title={`算法结果 — ${title}`} open={open} onClose={onClose} width={880}>
        {!loading && items.length === 0 && (
          <Paragraph type="secondary">
            该任务暂无算法结果（请确认模板含「结果落库」或判定节点且算法阶段已成功执行）。
          </Paragraph>
        )}
        <Table<TaskAlgorithmResultItem>
          loading={loading}
          rowKey={(row) => `${row.node_id}:${row.metric_name}:${row.algorithm_code}`}
          dataSource={items}
          columns={columns}
          pagination={items.length > 20 ? { pageSize: 20, showSizeChanger: true } : false}
          size="small"
          scroll={{ x: 760 }}
        />
      </Drawer>

      <Modal
        title={
          detailItem ? (
            <Space direction="vertical" size={0}>
              <Text>{detailItem.metric_name}</Text>
              <Text type="secondary" style={{ fontSize: 12 }}>
                {detailItem.algorithm_code} · {detailItem.node_id}
              </Text>
            </Space>
          ) : (
            '结果明细'
          )
        }
        open={detailItem != null}
        onCancel={() => setDetailItem(null)}
        footer={null}
        width={720}
      >
        <pre style={{ maxHeight: '60vh', overflow: 'auto', margin: 0, fontSize: 12 }}>
          {detailItem ? formatDetailJson(detailItem.detail_json) : ''}
        </pre>
      </Modal>
    </>
  );
}

import { useEffect, useMemo, useState } from 'react';
import { Button, Card, Input, InputNumber, Popconfirm, Space, Switch, Table, Typography, message } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import type { OutlierMarkOption } from '../../api/types';
import { settingsApi } from '../../api/settings';

type EditableMarkRow = OutlierMarkOption & { rowId: string };

const { Text } = Typography;

function toEditableRows(items: OutlierMarkOption[]): EditableMarkRow[] {
  return items
    .slice()
    .sort((a, b) => a.sort_order - b.sort_order)
    .map((item, idx) => ({
      ...item,
      rowId: `${item.mark_code}-${idx}`
    }));
}

export function SystemConfigPage() {
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [rows, setRows] = useState<EditableMarkRow[]>([]);

  const load = async () => {
    setLoading(true);
    try {
      const items = await settingsApi.getOutlierMarks();
      setRows(toEditableRows(items));
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void load();
  }, []);

  const updateRow = (rowId: string, patch: Partial<EditableMarkRow>) => {
    setRows((prev) => prev.map((r) => (r.rowId === rowId ? { ...r, ...patch } : r)));
  };

  const addRow = () => {
    const next = rows.length + 1;
    setRows((prev) => [
      ...prev,
      {
        rowId: `NEW-${Date.now()}-${next}`,
        mark_code: `MARK_${next}`,
        mark_label: `标记${next}`,
        is_outlier: false,
        sort_order: next,
        enabled: true
      }
    ]);
  };

  const save = async () => {
    const payload = rows.map((r) => ({
      mark_code: r.mark_code.trim(),
      mark_label: r.mark_label.trim(),
      is_outlier: r.is_outlier,
      sort_order: r.sort_order ?? 0,
      enabled: r.enabled
    }));
    setSaving(true);
    try {
      const saved = await settingsApi.saveOutlierMarks(payload);
      setRows(toEditableRows(saved));
      message.success('离群标记配置已保存');
    } catch (error) {
      const msg = error instanceof Error ? error.message : '保存失败';
      message.error(msg);
    } finally {
      setSaving(false);
    }
  };

  const columns: ColumnsType<EditableMarkRow> = useMemo(
    () => [
      {
        title: '标记编码',
        dataIndex: 'mark_code',
        width: 180,
        render: (_, row) => (
          <Input value={row.mark_code} onChange={(e) => updateRow(row.rowId, { mark_code: e.target.value })} />
        )
      },
      {
        title: '标记名称',
        dataIndex: 'mark_label',
        width: 220,
        render: (_, row) => (
          <Input value={row.mark_label} onChange={(e) => updateRow(row.rowId, { mark_label: e.target.value })} />
        )
      },
      {
        title: '离群项',
        dataIndex: 'is_outlier',
        width: 120,
        align: 'center',
        render: (_, row) => (
          <Switch
            checked={row.is_outlier}
            onChange={(checked) => {
              setRows((prev) =>
                prev.map((item) => ({
                  ...item,
                  is_outlier: item.rowId === row.rowId ? checked : checked ? false : item.is_outlier
                }))
              );
            }}
          />
        )
      },
      {
        title: '启用',
        dataIndex: 'enabled',
        width: 100,
        align: 'center',
        render: (_, row) => (
          <Switch checked={row.enabled} onChange={(checked) => updateRow(row.rowId, { enabled: checked })} />
        )
      },
      {
        title: '排序',
        dataIndex: 'sort_order',
        width: 120,
        render: (_, row) => (
          <InputNumber
            min={0}
            precision={0}
            value={row.sort_order}
            onChange={(val) => updateRow(row.rowId, { sort_order: Number.isFinite(val as number) ? (val as number) : 0 })}
          />
        )
      },
      {
        title: '操作',
        key: 'actions',
        width: 110,
        render: (_, row) => (
          <Popconfirm title="确认删除该标记？" onConfirm={() => setRows((prev) => prev.filter((x) => x.rowId !== row.rowId))}>
            <Button danger type="link">
              删除
            </Button>
          </Popconfirm>
        )
      }
    ],
    [rows]
  );

  return (
    <Card
      title="系统配置 / 离群标记配置"
      extra={
        <Space>
          <Button onClick={addRow}>新增标记</Button>
          <Button type="primary" loading={saving} onClick={save}>
            保存
          </Button>
        </Space>
      }
    >
      <Space direction="vertical" size={12} style={{ width: '100%' }}>
        <Text type="secondary">启用项中必须且仅有一个“离群项”，至少保留一个“非离群项”。</Text>
        <Table<EditableMarkRow>
          rowKey="rowId"
          loading={loading}
          dataSource={rows}
          columns={columns}
          pagination={false}
          scroll={{ x: 900 }}
        />
      </Space>
    </Card>
  );
}

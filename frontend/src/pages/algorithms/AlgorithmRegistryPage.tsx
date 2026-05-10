import { useEffect, useMemo, useState } from 'react';
import { Card, Input, Space, Table, Tag, Typography } from 'antd';
import { algoRegistryApi } from '@/api/templates';
import { AlgorithmCategory, AlgorithmRegistryEntry, AlgorithmRuntime } from '@/api/types';

const { Text, Paragraph } = Typography;

const RUNTIME_COLOR: Record<AlgorithmRuntime, string> = {
  Builtin: 'blue',
  Python: 'gold',
  Js: 'geekblue'
};

const CATEGORY_LABEL: Record<AlgorithmCategory, string> = {
  Source: '数据输入',
  Stats: '基础统计',
  Spectrum: '频域处理',
  Align: '时序对齐',
  Cluster: '聚类分析',
  Compare: '比对节点',
  Output: '输出与判定'
};

export function AlgorithmRegistryPage() {
  const [entries, setEntries] = useState<AlgorithmRegistryEntry[]>([]);
  const [loading, setLoading] = useState(false);
  const [keyword, setKeyword] = useState('');

  useEffect(() => {
    (async () => {
      setLoading(true);
      try {
        const data = await algoRegistryApi.registry();
        setEntries(data);
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  const filtered = useMemo(() => {
    if (!keyword) return entries;
    const k = keyword.toLowerCase();
    return entries.filter(
      (e) =>
        e.algorithmCode.toLowerCase().includes(k) ||
        e.displayName.toLowerCase().includes(k) ||
        (e.description ?? '').toLowerCase().includes(k)
    );
  }, [entries, keyword]);

  return (
    <Card
      title="算法仓库（已发布注册表）"
      extra={
        <Input.Search
          allowClear
          placeholder="按算法名称 / 编码搜索"
          value={keyword}
          onChange={(e) => setKeyword(e.target.value)}
          style={{ width: 240 }}
        />
      }
    >
      <Paragraph type="secondary" style={{ marginBottom: 12 }}>
        算法仓库注册表是 React Flow 编辑器组件库的唯一数据来源；只展示状态为 Published 且为该 algorithmCode 最新版本的算法包。
        平台预置 12 个内建算法（max / min / mean / variance / stddev / envelope / rms / fft / psd / dominant_freq / threshold_judge / three_sigma_judge）；
        Python / JavaScript 自定义算法须通过沙箱审核后才会自动出现。
      </Paragraph>
      <Table<AlgorithmRegistryEntry>
        loading={loading}
        rowKey={(record) => `${record.algorithmCode}__${record.version}`}
        dataSource={filtered}
        pagination={{ pageSize: 20 }}
        columns={[
          { title: '算法编码', dataIndex: 'algorithmCode', width: 220 },
          { title: '展示名称', dataIndex: 'displayName' },
          {
            title: '运行时',
            dataIndex: 'runtime',
            width: 110,
            render: (rt: AlgorithmRuntime) => <Tag color={RUNTIME_COLOR[rt]}>{rt}</Tag>
          },
          {
            title: '分类',
            dataIndex: 'category',
            width: 140,
            render: (cat: AlgorithmCategory) => CATEGORY_LABEL[cat] ?? cat
          },
          { title: '版本号', dataIndex: 'version', width: 100 },
          {
            title: '说明',
            dataIndex: 'description',
            render: (text: string | null) => <Text type="secondary">{text ?? '-'}</Text>
          },
          {
            title: '资源限制',
            dataIndex: 'resourcesJson',
            width: 220,
            render: (resources: unknown) => (
              <Text code style={{ fontSize: 12 }}>
                {summarizeResources(resources)}
              </Text>
            )
          }
        ]}
      />
      <Space style={{ marginTop: 12 }}>
        <Text type="secondary">
          自定义算法包上传、沙箱审核、撤销与版本管理由独立的「算法仓库管理」模块负责（详见 6.5.4）。
        </Text>
      </Space>
    </Card>
  );
}

function summarizeResources(resources: unknown): string {
  if (!resources || typeof resources !== 'object') return '-';
  const r = resources as { cpu?: number; memory?: string | number; timeoutSeconds?: number };
  const cpu = r.cpu !== undefined ? `cpu=${r.cpu}` : null;
  const mem = r.memory !== undefined ? `mem=${r.memory}` : null;
  const timeout = r.timeoutSeconds !== undefined ? `timeout=${r.timeoutSeconds}s` : null;
  return [cpu, mem, timeout].filter(Boolean).join(' / ') || '-';
}

import { useEffect, useState } from 'react';
import { Button, Card, Table, Tag, Typography, Upload, message } from 'antd';
import { UploadOutlined } from '@ant-design/icons';
import { algoRegistryApi } from '@/api/templates';
import type { AlgorithmCategory, AlgorithmPackageView, AlgorithmRuntime } from '@/api/types';

const { Paragraph } = Typography;

const RUNTIME_COLOR: Record<AlgorithmRuntime, string> = {
  Builtin: 'blue',
  Python: 'gold',
  Js: 'geekblue'
};

const STATUS_COLOR: Record<string, string> = {
  Draft: 'default',
  SandboxValidating: 'processing',
  Published: 'success',
  Rejected: 'error',
  Archived: 'default'
};

export function AlgorithmPackagesPage() {
  const [rows, setRows] = useState<AlgorithmPackageView[]>([]);
  const [loading, setLoading] = useState(false);

  const load = async () => {
    setLoading(true);
    try {
      const data = await algoRegistryApi.packages();
      setRows(data);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void load();
  }, []);

  return (
    <Card
      title="算法包仓库（#repo）"
      extra={
        <Upload
          accept=".zip"
          showUploadList={false}
          beforeUpload={async (file) => {
            try {
              const r = await algoRegistryApi.uploadPackage(file);
              message.success(`已上传 Draft 包 ${r.package_id}`);
              await load();
            } catch {
              /* global handler */
            }
            return false;
          }}
        >
          <Button icon={<UploadOutlined />}>上传 ZIP（含 manifest.json）</Button>
        </Upload>
      }
    >
      <Paragraph type="secondary">
        列表对应后端 <code>GET /api/v1/algorithms/packages</code>；上传走 <code>POST .../packages/upload</code>。
        manifest 字段需含 algorithmCode / algorithmName / category / version / runtime 等（详见设计 6.5.4.6）。
      </Paragraph>
      <Table<AlgorithmPackageView>
        loading={loading}
        rowKey={(r) => r.packageId}
        dataSource={rows}
        pagination={{ pageSize: 20 }}
        columns={[
          { title: '名称', dataIndex: 'displayName' },
          { title: '编码', dataIndex: 'algorithmCode', width: 160 },
          { title: '版本', dataIndex: 'version', width: 100 },
          {
            title: '运行时',
            dataIndex: 'runtime',
            width: 110,
            render: (v: AlgorithmRuntime) => <Tag color={RUNTIME_COLOR[v]}>{v}</Tag>
          },
          {
            title: '分类',
            dataIndex: 'category',
            width: 110,
            render: (v: AlgorithmCategory) => v
          },
          {
            title: '状态',
            dataIndex: 'status',
            width: 140,
            render: (s: string) => <Tag color={STATUS_COLOR[s] ?? 'default'}>{s}</Tag>
          },
          { title: '更新时间', dataIndex: 'updatedAt', width: 200 }
        ]}
      />
    </Card>
  );
}

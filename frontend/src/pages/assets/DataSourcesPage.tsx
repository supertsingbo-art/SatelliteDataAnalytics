import { useEffect, useState } from 'react';
import {
  Button,
  Card,
  Form,
  Input,
  InputNumber,
  Modal,
  Select,
  Space,
  Switch,
  Table,
  Tag,
  Typography,
  message
} from 'antd';
import { ReloadOutlined, ThunderboltOutlined } from '@ant-design/icons';
import { assetsApi, SaveDataSourceConfigRequest } from '@/api/assets';
import { DataSourceConfig } from '@/api/types';

const { Text } = Typography;

const SOURCE_TYPE_OPTIONS: { value: DataSourceConfig['sourceType']; label: string }[] = [
  { value: 'MASS_DATA_API', label: '海量数据接口服务' },
  { value: 'SATELLITE_ASSET_API', label: '卫星测试流程规划服务' },
  { value: 'CLICKHOUSE', label: 'ClickHouse 分析库' },
  { value: 'MINIO', label: 'MinIO 对象存储' },
  { value: 'PG_META', label: 'PostgreSQL 元数据库' }
];

const SOURCE_USAGE: Record<DataSourceConfig['sourceType'], string> = {
  MASS_DATA_API: '提供卫星列表、参数元数据、按星 MongoDB 连接配置',
  SATELLITE_ASSET_API: '提供按星测试阶段（teststagename、fromdt、todt；POST /api/testplan/teststages）',
  CLICKHOUSE: '高品质明细 hq_param_point、算法结果 algo_result 等表',
  MINIO: '算法包、报告与 ML 数据集快照对象存储',
  PG_META: '平台元数据 / 任务血缘 / 模板治理'
};

export function DataSourcesPage() {
  const [data, setData] = useState<DataSourceConfig[]>([]);
  const [loading, setLoading] = useState(false);
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<DataSourceConfig | null>(null);
  const [form] = Form.useForm<SaveDataSourceConfigRequest>();

  const reload = async () => {
    setLoading(true);
    try {
      const list = await assetsApi.listSources();
      setData(list);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    reload();
  }, []);

  const openCreate = () => {
    setEditing(null);
    form.resetFields();
    form.setFieldsValue({
      sourceType: 'MASS_DATA_API',
      authType: 'NONE',
      timeoutMs: 10_000,
      enabled: true,
      env: 'PROD'
    });
    setModalOpen(true);
  };

  const openEdit = (record: DataSourceConfig) => {
    setEditing(record);
    form.setFieldsValue({
      sourceType: record.sourceType,
      sourceName: record.sourceName,
      endpointUrl: record.endpointUrl,
      authType: record.authType,
      authSecretRef: record.authSecretRef ?? undefined,
      timeoutMs: record.timeoutMs,
      enabled: record.enabled,
      env: record.env
    });
    setModalOpen(true);
  };

  const submit = async () => {
    const values = await form.validateFields();
    try {
      if (editing) {
        await assetsApi.updateSource(editing.sourceId, values);
        message.success('数据源配置已更新');
      } else {
        await assetsApi.createSource(values);
        message.success('数据源配置已新增');
      }
      setModalOpen(false);
      reload();
    } catch {
      // 拦截器已弹错误码
    }
  };

  const toggleEnabled = async (record: DataSourceConfig, enabled: boolean) => {
    await assetsApi.setSourceStatus(record.sourceId, enabled);
    message.success(enabled ? '已启用' : '已禁用');
    reload();
  };

  const testConn = async (record: DataSourceConfig) => {
    const result = await assetsApi.testSourceConnection(record.sourceId);
    if (result.success) {
      message.success(`连通性测试通过（耗时 ${result.elapsedMs ?? '-'} ms）`);
    } else {
      message.warning(`连通性测试失败：${result.message}`);
    }
  };

  return (
    <Card
      title="外部 HTTP 服务与内部存储配置"
      extra={
        <Space>
          <Button icon={<ReloadOutlined />} onClick={reload}>
            刷新
          </Button>
          <Button type="primary" onClick={openCreate}>
            新增数据源
          </Button>
        </Space>
      }
    >
      <Text type="secondary" style={{ display: 'block', marginBottom: 12 }}>
        本页仅维护外部服务入口与内部存储配置；按设计文档 6.1.4 安全要求，<Text strong>不允许配置 MONGO 类型</Text>，
        MongoDB 连接信息由海量数据接口按 <Text code>tasook_no + satellite_no + dbStage</Text> 三元组动态返回。
      </Text>

      <Table<DataSourceConfig>
        rowKey="sourceId"
        loading={loading}
        dataSource={data}
        pagination={false}
        columns={[
          {
            title: '配置名称',
            dataIndex: 'sourceName',
            render: (value) => <Text strong>{value}</Text>
          },
          {
            title: '类型',
            dataIndex: 'sourceType',
            render: (value: DataSourceConfig['sourceType']) => {
              const option = SOURCE_TYPE_OPTIONS.find((opt) => opt.value === value);
              return <Tag color="blue">{option?.label ?? value}</Tag>;
            }
          },
          { title: '服务地址 / 连接入口', dataIndex: 'endpointUrl', ellipsis: true },
          {
            title: '用途',
            dataIndex: 'sourceType',
            render: (value: DataSourceConfig['sourceType']) => (
              <Text type="secondary">{SOURCE_USAGE[value]}</Text>
            )
          },
          {
            title: '环境',
            dataIndex: 'env',
            width: 90,
            render: (value: string) => <Tag>{value}</Tag>
          },
          {
            title: '启用状态',
            dataIndex: 'enabled',
            width: 100,
            render: (enabled: boolean, record) => (
              <Switch checked={enabled} onChange={(value) => toggleEnabled(record, value)} />
            )
          },
          {
            title: '操作',
            key: 'actions',
            width: 160,
            render: (_, record) => (
              <Space>
                <Button
                  size="small"
                  icon={<ThunderboltOutlined />}
                  onClick={() => testConn(record)}
                >
                  连通性测试
                </Button>
                <Button size="small" type="link" onClick={() => openEdit(record)}>
                  编辑
                </Button>
              </Space>
            )
          }
        ]}
      />

      <Modal
        title={editing ? '编辑数据源配置' : '新增数据源配置'}
        open={modalOpen}
        onCancel={() => setModalOpen(false)}
        onOk={submit}
        okText={editing ? '保存修改' : '新增'}
        cancelText="取消"
        width={620}
        destroyOnClose
      >
        <Form<SaveDataSourceConfigRequest> layout="vertical" form={form}>
          <Form.Item label="数据源类型" name="sourceType" rules={[{ required: true }]}>
            <Select options={SOURCE_TYPE_OPTIONS} disabled={!!editing} />
          </Form.Item>
          <Form.Item label="配置名称" name="sourceName" rules={[{ required: true, max: 128 }]}>
            <Input placeholder="例如：海量数据接口服务-生产" />
          </Form.Item>
          <Form.Item
            label="服务地址 / 连接入口"
            name="endpointUrl"
            rules={[
              { required: true, message: '请输入服务地址 / 连接入口' },
              {
                validator: (_rule, value) => {
                  if (!value) return Promise.resolve();
                  const sourceType = form.getFieldValue('sourceType') as string;
                  const httpTypes = ['MASS_DATA_API', 'SATELLITE_ASSET_API', 'MINIO'];
                  if (httpTypes.includes(sourceType)) {
                    try {
                      const url = new URL(value);
                      if (!url.protocol.startsWith('http')) {
                        return Promise.reject(new Error('请输入合法的绝对 URL（需以 http/https 开头）'));
                      }
                    } catch {
                      return Promise.reject(new Error('请输入合法的绝对 URL（需以 http/https 开头）'));
                    }
                  }
                  return Promise.resolve();
                }
              }
            ]}
          >
            <Input placeholder="https://mass-data.corp/api" />
          </Form.Item>
          <Form.Item label="鉴权方式" name="authType" rules={[{ required: true }]}>
            <Select
              options={[
                { value: 'NONE', label: '无鉴权' },
                { value: 'BEARER', label: 'Bearer Token' },
                { value: 'API_KEY', label: 'API Key' },
                { value: 'BASIC', label: 'Basic Auth' }
              ]}
            />
          </Form.Item>
          <Form.Item label="密钥引用键 auth_secret_ref" name="authSecretRef">
            <Input placeholder="kms://satdata/mass-data-token" />
          </Form.Item>
          <Form.Item
            label="超时 timeoutMs（毫秒）"
            name="timeoutMs"
            rules={[{ required: true, type: 'number', min: 1000, max: 60_000 }]}
          >
            <InputNumber style={{ width: '100%' }} step={1000} />
          </Form.Item>
          <Form.Item label="环境标识 env" name="env" rules={[{ required: true }]}>
            <Select
              options={[
                { value: 'DEV', label: 'DEV 开发' },
                { value: 'TEST', label: 'TEST 测试' },
                { value: 'STAGE', label: 'STAGE 预发' },
                { value: 'PROD', label: 'PROD 生产' }
              ]}
            />
          </Form.Item>
          <Form.Item label="是否启用" name="enabled" valuePropName="checked">
            <Switch />
          </Form.Item>
        </Form>
      </Modal>
    </Card>
  );
}

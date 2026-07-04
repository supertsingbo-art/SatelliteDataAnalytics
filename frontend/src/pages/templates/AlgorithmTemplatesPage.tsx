import { useEffect, useState } from 'react';
import { Button, Card, Input, Modal, Popconfirm, Select, Space, Table, Tag, Typography, message } from 'antd';
import { PlusOutlined, ReloadOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import dayjs from 'dayjs';
import { algoTemplatesApi } from '@/api/templates';
import { AlgorithmTemplateView, PagedResult, TemplateStatus } from '@/api/types';

const { Text } = Typography;

const statusColor: Record<TemplateStatus, string> = {
  Draft: 'default',
  Published: 'green',
  Archived: 'orange'
};

export function AlgorithmTemplatesPage() {
  const navigate = useNavigate();
  const [data, setData] = useState<PagedResult<AlgorithmTemplateView> | null>(null);
  const [loading, setLoading] = useState(false);
  const [keyword, setKeyword] = useState('');
  const [status, setStatus] = useState<TemplateStatus | undefined>(undefined);
  const [pageNo, setPageNo] = useState(1);
  const [pageSize, setPageSize] = useState(20);

  const reload = async () => {
    setLoading(true);
    try {
      const result = await algoTemplatesApi.list({ keyword, status, pageNo, pageSize });
      setData(result);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    reload();
  }, [pageNo, pageSize, status]);

  const onClone = async (record: AlgorithmTemplateView) => {
    const detail = await algoTemplatesApi.clone(record.templateId, record.version);
    message.success('已复制为新模板');
    navigate(`/templates/algorithms/${detail.view.templateId}/versions/${detail.view.version}`);
  };

  const onPublish = async (record: AlgorithmTemplateView) => {
    await algoTemplatesApi.publish(record.templateId, record.version);
    message.success('模板已发布');
    reload();
  };

  const onArchive = async (record: AlgorithmTemplateView) => {
    await algoTemplatesApi.archive(record.templateId, record.version);
    message.success('模板已归档');
    reload();
  };

  const onDelete = async (record: AlgorithmTemplateView) => {
    const impact = await algoTemplatesApi.deleteImpact(record.templateId);
    const warningText = impact.hasReferences
      ? `该模板已被 ${impact.taskRunCount} 个任务引用（运行中/排队任务 ${impact.runningTaskRunCount} 个）。确认后将一并删除相关 PG 数据。`
      : '该模板及其全部版本将被删除。';

    Modal.confirm({
      title: `确认删除模板「${impact.templateName}」？`,
      content: warningText,
      okText: '确认删除',
      okButtonProps: { danger: true },
      cancelText: '取消',
      async onOk() {
        await algoTemplatesApi.removeTemplate(record.templateId, true);
        message.success('模板已删除');
        await reload();
      }
    });
  };

  return (
    <Card
      title="算法模板"
      extra={
        <Space>
          <Input.Search
            placeholder="按模板名称搜索"
            allowClear
            value={keyword}
            onChange={(e) => setKeyword(e.target.value)}
            onSearch={() => {
              setPageNo(1);
              reload();
            }}
            style={{ width: 240 }}
          />
          <Select
            allowClear
            placeholder="按状态过滤"
            value={status}
            onChange={(v) => setStatus(v)}
            options={[
              { value: 'Draft', label: 'Draft 草稿' },
              { value: 'Published', label: 'Published 已发布' },
              { value: 'Archived', label: 'Archived 已归档' }
            ]}
            style={{ width: 160 }}
          />
          <Button icon={<ReloadOutlined />} onClick={reload}>
            刷新
          </Button>
          <Button type="primary" icon={<PlusOutlined />} onClick={() => navigate('/templates/algorithms/new')}>
            新建算法模板
          </Button>
        </Space>
      }
    >
      <Text type="secondary" style={{ display: 'block', marginBottom: 12 }}>
        算法模板由可视化 DAG 描述（react_flow_json）与运行时配置（config_json）共同组成；
        同一 templateId 多版本共存，Published 后该版本不可修改，只能复制为新模板 Draft；
        发布前由静态校验器检查节点数、源节点、输出节点、无环、运行时白名单与算法包发布状态。
      </Text>
      <Table<AlgorithmTemplateView>
        rowKey={(record) => `${record.templateId}_${record.version}`}
        loading={loading}
        dataSource={data?.items ?? []}
        pagination={{
          current: pageNo,
          pageSize,
          total: data?.total ?? 0,
          onChange: (next, size) => {
            setPageNo(next);
            setPageSize(size);
          }
        }}
        columns={[
          { title: '模板名称', dataIndex: 'templateName' },
          { title: '当前版本', dataIndex: 'version', width: 100 },
          {
            title: '状态',
            dataIndex: 'status',
            width: 110,
            render: (s: TemplateStatus) => <Tag color={statusColor[s]}>{s}</Tag>
          },
          { title: '节点数', dataIndex: 'nodeCount', width: 90 },
          {
            title: '更新时间',
            dataIndex: 'updatedAt',
            render: (value: string) => dayjs(value).format('YYYY-MM-DD HH:mm')
          },
          {
            title: '操作',
            width: 320,
            render: (_, record) => (
              <Space size={6}>
                <Button
                  size="small"
                  type="link"
                  onClick={() =>
                    navigate(`/templates/algorithms/${record.templateId}/versions/${record.version}`)
                  }
                >
                  {record.status === 'Draft' ? '编辑' : '查看'}
                </Button>
                {record.status === 'Draft' && (
                  <Popconfirm title="确认发布该版本？发布后将不可再编辑" onConfirm={() => onPublish(record)}>
                    <Button size="small" type="link">
                      发布
                    </Button>
                  </Popconfirm>
                )}
                <Popconfirm title="确认复制当前模板为新模板？" onConfirm={() => onClone(record)}>
                  <Button size="small" type="link">
                    复制为新模板
                  </Button>
                </Popconfirm>
                {record.status !== 'Archived' && (
                  <Popconfirm title="归档后将不出现在任务创建可选列表" onConfirm={() => onArchive(record)}>
                    <Button size="small" type="link">
                      归档
                    </Button>
                  </Popconfirm>
                )}
                <Button size="small" type="link" danger onClick={() => onDelete(record)}>
                  删除模板
                </Button>
              </Space>
            )
          }
        ]}
      />
    </Card>
  );
}

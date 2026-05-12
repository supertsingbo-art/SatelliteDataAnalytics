import { useEffect, useState } from 'react';
import {
  Alert,
  Button,
  Card,
  Drawer,
  Input,
  Space,
  Table,
  Tabs,
  Tag,
  Typography,
  message
} from 'antd';
import { ReloadOutlined, SyncOutlined } from '@ant-design/icons';
import dayjs from 'dayjs';
import { assetsApi } from '@/api/assets';
import {
  AssetSyncResult,
  MongoConnectionSummary,
  PagedResult,
  ParamCache,
  SatelliteCache,
  TestPhase
} from '@/api/types';

const { Text } = Typography;

export function SatellitesPage() {
  const [data, setData] = useState<PagedResult<SatelliteCache> | null>(null);
  const [keyword, setKeyword] = useState('');
  const [pageNo, setPageNo] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [loading, setLoading] = useState(false);
  const [syncing, setSyncing] = useState(false);
  const [drawer, setDrawer] = useState<SatelliteCache | null>(null);
  const [params, setParams] = useState<PagedResult<ParamCache> | null>(null);
  const [paramPageNo, setParamPageNo] = useState(1);
  const [paramPageSize, setParamPageSize] = useState(20);
  const [paramsLoading, setParamsLoading] = useState(false);
  const [paramKeyword, setParamKeyword] = useState('');
  const [phases, setPhases] = useState<TestPhase[]>([]);
  const [mongoSummary, setMongoSummary] = useState<MongoConnectionSummary | null>(null);
  const [lastSync, setLastSync] = useState<AssetSyncResult | null>(null);

  const reload = async () => {
    setLoading(true);
    try {
      const result = await assetsApi.listSatellites({ keyword, pageNo, pageSize });
      setData(result);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    reload();
  }, [pageNo, pageSize]);

  const triggerSyncAll = async () => {
    setSyncing(true);
    try {
      const result = await assetsApi.syncAll();
      setLastSync(result);
      const text =
        result.status === 'Succeeded'
          ? `资产同步完成：${result.satelliteCount} 颗卫星 / ${result.parameterCount} 条参数 / ${result.commandCount} 条指令 / ${result.testBatchCount} 个测试阶段`
          : `资产同步状态 ${result.status}：${result.failedSatelliteCount} 颗卫星部分失败`;
      if (result.status === 'Succeeded') {
        message.success(text);
      } else {
        message.warning(text);
      }
      reload();
    } finally {
      setSyncing(false);
    }
  };

  const triggerSyncSingle = async (record: SatelliteCache) => {
    const result = await assetsApi.syncSatellite(record.tasookNo, record.satelliteNo);
    if (result.status === 'Succeeded') {
      message.success(`${record.tasookNo}/${record.satelliteNo} 同步完成`);
    } else {
      message.warning(`${record.tasookNo}/${record.satelliteNo} 部分失败：${result.errorMessage ?? ''}`);
    }
    reload();
  };

  const fetchParamsPage = async (
    satellite: SatelliteCache,
    pageNo: number,
    pageSize: number,
    keyword: string
  ) => {
    setParamsLoading(true);
    try {
      const result = await assetsApi.listParams(satellite.tasookNo, satellite.satelliteNo, {
        keyword: keyword.trim() || undefined,
        pageNo,
        pageSize
      });
      setParams(result);
    } finally {
      setParamsLoading(false);
    }
  };

  const openDrawer = async (record: SatelliteCache) => {
    setDrawer(record);
    setParams(null);
    setPhases([]);
    setMongoSummary(null);
    setParamKeyword('');
    setParamPageNo(1);
    setParamPageSize(20);
    const [, phaseList] = await Promise.all([
      fetchParamsPage(record, 1, 20, ''),
      assetsApi.listTestPhases(record.tasookNo, record.satelliteNo)
    ]);
    setPhases(phaseList);
    if (record.mongoInfo) {
      try {
        const summary = await assetsApi.getMongoSummary(record.tasookNo, record.satelliteNo);
        setMongoSummary(summary);
      } catch {
        setMongoSummary(null);
      }
    }
  };

  const refreshParams = async () => {
    if (!drawer) return;
    setParamPageNo(1);
    await fetchParamsPage(drawer, 1, paramPageSize, paramKeyword);
  };

  const closeDrawer = () => {
    setDrawer(null);
    setParams(null);
    setParamKeyword('');
    setParamPageNo(1);
    setParamPageSize(20);
    setPhases([]);
    setMongoSummary(null);
  };

  return (
    <Card
      title="卫星资产缓存"
      extra={
        <Space>
          <Input.Search
            placeholder="按型号 / 卫星 / 名称搜索"
            allowClear
            onSearch={(value) => {
              setKeyword(value);
              setPageNo(1);
              reload();
            }}
            style={{ width: 260 }}
          />
          <Button icon={<ReloadOutlined />} onClick={reload}>
            刷新
          </Button>
          <Button
            type="primary"
            icon={<SyncOutlined spin={syncing} />}
            loading={syncing}
            onClick={triggerSyncAll}
          >
            全量资产同步
          </Button>
        </Space>
      }
    >
      <Text type="secondary" style={{ display: 'block', marginBottom: 12 }}>
        本页展示 satellite_cache、param_cache、command_cache、test_batch_cache 的同步快照。流程为：全量卫星 → 每星参数
        （POST /api/mass-data/basic/parameters）→ 每星指令（POST /api/mass-data/basic/commands）→ 每星测试阶段 → 每星
        Mongo 配置。两列「参数 / 指令同步总量」为最近一次成功写入缓存的条数；在 appsettings 中将 AssetCache:UsePostgreSql
        设为 true 时，数据写入 ConnectionStrings:Postgres 所配置的库（如 demo_db）中的同名表。
      </Text>

      {lastSync && (
        <Alert
          style={{ marginBottom: 12 }}
          type={
            lastSync.status === 'Succeeded'
              ? 'success'
              : lastSync.status === 'PartialSucceeded'
              ? 'warning'
              : 'error'
          }
          showIcon
          message={`最近一次同步：${dayjs(lastSync.syncedAt).format('YYYY-MM-DD HH:mm:ss')} / 状态 ${lastSync.status}`}
          description={
            <span>
              卫星 {lastSync.satelliteCount} 颗 · 参数 {lastSync.parameterCount} 条 · 指令 {lastSync.commandCount} 条 · 测试阶段{' '}
              {lastSync.testBatchCount} 段
              {lastSync.failedSatelliteCount > 0 && (
                <span style={{ marginLeft: 8 }}>· 失败星 {lastSync.failedSatelliteCount}</span>
              )}
              {lastSync.errorMessage && <div>{lastSync.errorMessage}</div>}
            </span>
          }
        />
      )}

      <Table<SatelliteCache>
        rowKey={(record) => `${record.tasookNo}_${record.satelliteNo}`}
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
          { title: '型号代号 tasook_no', dataIndex: 'tasookNo', width: 160 },
          { title: '卫星代号 satellite_no', dataIndex: 'satelliteNo', width: 160 },
          { title: '卫星名称', dataIndex: 'satelliteName' },
          { title: '型号 / 类型', dataIndex: 'satelliteType', render: (v) => v ?? <Text type="secondary">-</Text> },
          {
            title: '研制阶段 db_stage',
            dataIndex: 'dbStage',
            render: (v) => (v ? <Tag>{v}</Tag> : <Text type="secondary">-</Text>)
          },
          {
            title: '参数同步总量',
            dataIndex: 'cachedParameterCount',
            width: 120,
            render: (v: number | undefined) => (v ?? 0).toLocaleString()
          },
          {
            title: '指令同步总量',
            dataIndex: 'cachedCommandCount',
            width: 120,
            render: (v: number | undefined) => (v ?? 0).toLocaleString()
          },
          {
            title: 'Mongo 同步状态',
            dataIndex: 'mongoInfo',
            render: (info) =>
              info ? <Tag color="green">已同步</Tag> : <Tag color="orange">待同步</Tag>
          },
          {
            title: '上次同步',
            dataIndex: 'lastSyncedAt',
            render: (value) => dayjs(value).format('YYYY-MM-DD HH:mm:ss')
          },
          {
            title: '操作',
            key: 'actions',
            width: 200,
            render: (_, record) => (
              <Space>
                <Button size="small" type="link" onClick={() => openDrawer(record)}>
                  查看明细
                </Button>
                <Button size="small" type="link" onClick={() => triggerSyncSingle(record)}>
                  单星刷新
                </Button>
              </Space>
            )
          }
        ]}
      />

      <Drawer
        width={720}
        title={drawer ? `${drawer.tasookNo} / ${drawer.satelliteNo} · ${drawer.satelliteName}` : ''}
        open={!!drawer}
        onClose={closeDrawer}
        destroyOnClose
      >
        {drawer && (
          <Tabs
            defaultActiveKey="params"
            items={[
              {
                key: 'params',
                label: '参数 (param_cache)',
                children: (
                  <>
                    <Space style={{ marginBottom: 12 }}>
                      <Input.Search
                        allowClear
                        placeholder="按 param_id / 名称过滤"
                        value={paramKeyword}
                        onChange={(e) => setParamKeyword(e.target.value)}
                        onSearch={refreshParams}
                        style={{ width: 280 }}
                      />
                    </Space>
                    <Table<ParamCache>
                      size="small"
                      rowKey={(record) => record.paramId}
                      loading={paramsLoading}
                      dataSource={params?.items ?? []}
                      pagination={{
                        current: paramPageNo,
                        pageSize: paramPageSize,
                        total: params?.total ?? 0,
                        showSizeChanger: true,
                        pageSizeOptions: ['20', '50', '100', '200', '500'],
                        showTotal: (total) => `共 ${total} 条`,
                        onChange: (page, size) => {
                          if (!drawer) return;
                          setParamPageNo(page);
                          setParamPageSize(size);
                          void fetchParamsPage(drawer, page, size, paramKeyword);
                        },
                        onShowSizeChange: (_current, size) => {
                          if (!drawer) return;
                          setParamPageNo(1);
                          setParamPageSize(size);
                          void fetchParamsPage(drawer, 1, size, paramKeyword);
                        }
                      }}
                      columns={[
                        { title: 'param_id', dataIndex: 'paramId' },
                        { title: '名称', dataIndex: 'paramName' },
                        { title: '单位', dataIndex: 'unit', width: 80 },
                        { title: '类型', dataIndex: 'valueType', width: 80 },
                        {
                          title: '值域',
                          render: (_, record) =>
                            record.valueMin == null && record.valueMax == null
                              ? '-'
                              : `[${record.valueMin ?? ''}, ${record.valueMax ?? ''}]`
                        }
                      ]}
                    />
                  </>
                )
              },
              {
                key: 'phases',
                label: '测试阶段 (test_batch_cache)',
                children: (
                  <Table<TestPhase>
                    size="small"
                    rowKey={(record) => record.testBatchId}
                    pagination={false}
                    dataSource={phases}
                    columns={[
                      { title: '阶段编号 test_batch_id', dataIndex: 'testBatchId' },
                      { title: '阶段名 scenario', dataIndex: 'scenario' },
                      {
                        title: '起止 UTC',
                        render: (_, record) =>
                          `${dayjs(record.startTs).format('YYYY-MM-DD HH:mm')} ~ ${dayjs(record.endTs).format('YYYY-MM-DD HH:mm')}`
                      }
                    ]}
                  />
                )
              },
              {
                key: 'mongo',
                label: 'Mongo 连接摘要',
                children: mongoSummary ? (
                  <Card size="small">
                    <p>
                      <Text type="secondary">数据库名 dbName：</Text>
                      <Text code>{mongoSummary.dbName}</Text>
                    </p>
                    <p>
                      <Text type="secondary">脱敏后的 mongoUri：</Text>
                      <Text code>{mongoSummary.mongoUri}</Text>
                    </p>
                    <p>
                      <Text type="secondary">密钥引用 authRef：</Text>
                      <Text code>{mongoSummary.authRef ?? '-'}</Text>
                    </p>
                    <Alert
                      type="info"
                      showIcon
                      message="按设计要求，URI 已剥离凭据；明文密码仅以引用形式保存在密钥管理服务。"
                    />
                  </Card>
                ) : (
                  <Alert type="warning" message="该卫星尚未同步到 Mongo 连接信息，请先执行 Step 4 同步" />
                )
              }
            ]}
          />
        )}
      </Drawer>
    </Card>
  );
}

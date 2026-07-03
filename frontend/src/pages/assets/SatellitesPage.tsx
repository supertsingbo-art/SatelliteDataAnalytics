import { useEffect, useState } from 'react';
import {
  Alert,
  Button,
  Card,
  Drawer,
  Input,
  Space,
  Switch,
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
  CommandCache,
  MongoConnectionSummary,
  PagedResult,
  ParamCache,
  SatelliteCache,
  SatelliteListItem,
  TestPhase,
  commandCacheRowKey,
  paramCacheRowKey
} from '@/api/types';
import {
  normalizeSatelliteListItem,
  testPhaseNamesFromCache
} from '@/pages/assets/satelliteTestPhaseUtils';

const DEV_PHASE_TAG_COLORS = ['blue', 'geekblue', 'cyan', 'purple', 'magenta', 'volcano', 'gold', 'green'];

const { Text } = Typography;

function formatParamCell(value: string | number | null | undefined) {
  if (value == null || value === '') {
    return '-';
  }
  return value;
}

const PARAM_CACHE_COLUMNS: Parameters<typeof Table<ParamCache>>[0]['columns'] = [
  { title: '型号代号', dataIndex: 'tasookNo', width: 110, fixed: 'left' },
  { title: '卫星代号', dataIndex: 'satelliteNo', width: 110, fixed: 'left' },
  { title: '参数 ID', dataIndex: 'paraId', width: 80 },
  { title: '参数代号', dataIndex: 'paraCode', width: 120, ellipsis: true, render: (v) => formatParamCell(v) },
  { title: '参数描述', dataIndex: 'paraDesc', width: 140, ellipsis: true, render: (v) => formatParamCell(v) },
  { title: '参数类型', dataIndex: 'paraTypeDesc', width: 100, render: (v) => formatParamCell(v) },
  {
    title: '最小值',
    dataIndex: 'minValue',
    width: 90,
    render: (v: number | null) => formatParamCell(v)
  },
  {
    title: '最大值',
    dataIndex: 'maxValue',
    width: 90,
    render: (v: number | null) => formatParamCell(v)
  },
  {
    title: '更新周期(ms)',
    dataIndex: 'updateTime',
    width: 110,
    render: (v: number | null) => formatParamCell(v)
  },
  { title: '处理方法', dataIndex: 'procDesc', width: 120, ellipsis: true, render: (v) => formatParamCell(v) },
  {
    title: '所属系统 ID',
    dataIndex: 'prmSysId',
    width: 100,
    render: (v: number | null) => formatParamCell(v)
  },
  {
    title: '来源版本',
    dataIndex: 'sourceVersion',
    width: 100,
    render: (v) => formatParamCell(v)
  },
  {
    title: '同步时间',
    dataIndex: 'lastSyncedAt',
    width: 160,
    render: (v: string) => (v ? dayjs(v).format('YYYY-MM-DD HH:mm:ss') : '-')
  }
];

const COMMAND_CACHE_COLUMNS: Parameters<typeof Table<CommandCache>>[0]['columns'] = [
  { title: '型号代号', dataIndex: 'tasookNo', width: 110, fixed: 'left' },
  { title: '卫星代号', dataIndex: 'satelliteNo', width: 110, fixed: 'left' },
  { title: '指令 ID', dataIndex: 'cmdId', width: 80 },
  { title: '指令代号', dataIndex: 'cmdCode', width: 120, ellipsis: true, render: (v) => formatParamCell(v) },
  { title: '指令描述', dataIndex: 'cmdDesc', width: 140, ellipsis: true, render: (v) => formatParamCell(v) },
  {
    title: '指令类型',
    dataIndex: 'cmdType',
    width: 90,
    render: (v: number | null) => formatParamCell(v)
  },
  {
    title: '指令长度',
    dataIndex: 'cmdLen',
    width: 90,
    render: (v: number | null) => formatParamCell(v)
  },
  {
    title: '执行时间(ms)',
    dataIndex: 'exeTime',
    width: 110,
    render: (v: number | null) => formatParamCell(v)
  },
  {
    title: '有效标志',
    dataIndex: 'validFlag',
    width: 90,
    render: (v: number | null) => formatParamCell(v)
  },
  {
    title: '所属系统 ID',
    dataIndex: 'cmdSysId',
    width: 100,
    render: (v: number | null) => formatParamCell(v)
  },
  {
    title: '来源版本',
    dataIndex: 'sourceVersion',
    width: 100,
    render: (v) => formatParamCell(v)
  },
  {
    title: '同步时间',
    dataIndex: 'lastSyncedAt',
    width: 160,
    render: (v: string) => (v ? dayjs(v).format('YYYY-MM-DD HH:mm:ss') : '-')
  }
];

export function SatellitesPage() {
  const [data, setData] = useState<PagedResult<SatelliteListItem> | null>(null);
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
  const [commands, setCommands] = useState<PagedResult<CommandCache> | null>(null);
  const [commandPageNo, setCommandPageNo] = useState(1);
  const [commandPageSize, setCommandPageSize] = useState(20);
  const [commandsLoading, setCommandsLoading] = useState(false);
  const [commandKeyword, setCommandKeyword] = useState('');
  const [phases, setPhases] = useState<TestPhase[]>([]);
  const [mongoSummary, setMongoSummary] = useState<MongoConnectionSummary | null>(null);
  const [lastSync, setLastSync] = useState<AssetSyncResult | null>(null);
  const [togglingKey, setTogglingKey] = useState<string | null>(null);
  const [phaseLabelsLoading, setPhaseLabelsLoading] = useState(false);

  const enrichSatellitesWithTestPhases = async (items: SatelliteListItem[]) => {
    return Promise.all(
      items.map(async (item) => {
        const base = normalizeSatelliteListItem(item);
        try {
          const phases = await assetsApi.listTestPhases(base.tasookNo, base.satelliteNo);
          return {
            ...base,
            developmentPhases: testPhaseNamesFromCache(phases)
          };
        } catch {
          return base;
        }
      })
    );
  };

  const handleToggleEnabled = async (record: SatelliteListItem, isEnabled: boolean) => {
    const key = `${record.tasookNo}_${record.satelliteNo}`;
    setTogglingKey(key);
    try {
      await assetsApi.setSatelliteEnabled(record.tasookNo, record.satelliteNo, isEnabled);
      message.success(isEnabled ? '卫星已启用' : '卫星已禁用');
      await reload();
    } catch {
      message.error('更新启用状态失败');
    } finally {
      setTogglingKey(null);
    }
  };

  const reload = async () => {
    setLoading(true);
    setPhaseLabelsLoading(true);
    try {
      const result = await assetsApi.listSatellites({ keyword, pageNo, pageSize });
      const items = await enrichSatellitesWithTestPhases(result.items);
      setData({ ...result, items });
    } finally {
      setLoading(false);
      setPhaseLabelsLoading(false);
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

  const triggerSyncSingle = async (record: SatelliteListItem) => {
    const result = await assetsApi.syncSatellite(record.tasookNo, record.satelliteNo);
    if (result.status === 'Succeeded') {
      message.success(`${record.tasookNo}/${record.satelliteNo} 同步完成`);
    } else {
      message.warning(`${record.tasookNo}/${record.satelliteNo} 部分失败：${result.errorMessage ?? ''}`);
    }
    reload();
  };

  const fetchParamsPage = async (
    satellite: Pick<SatelliteListItem, 'tasookNo' | 'satelliteNo'>,
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

  const fetchCommandsPage = async (
    satellite: Pick<SatelliteListItem, 'tasookNo' | 'satelliteNo'>,
    pageNo: number,
    pageSize: number,
    keyword: string
  ) => {
    setCommandsLoading(true);
    try {
      const result = await assetsApi.listCommands(satellite.tasookNo, satellite.satelliteNo, {
        keyword: keyword.trim() || undefined,
        pageNo,
        pageSize
      });
      setCommands(result);
    } finally {
      setCommandsLoading(false);
    }
  };

  const openDrawer = async (record: SatelliteListItem) => {
    setParams(null);
    setCommands(null);
    setPhases([]);
    setMongoSummary(null);
    setParamKeyword('');
    setParamPageNo(1);
    setParamPageSize(20);
    setCommandKeyword('');
    setCommandPageNo(1);
    setCommandPageSize(20);
    try {
      const satellite = await assetsApi.getSatellite(record.tasookNo, record.satelliteNo);
      setDrawer(satellite);
    } catch {
      setDrawer({
        ...record,
        satelliteType: null
      });
    }
    const [, , phaseList] = await Promise.all([
      fetchParamsPage(record, 1, 20, ''),
      fetchCommandsPage(record, 1, 20, ''),
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

  const refreshCommands = async () => {
    if (!drawer) return;
    setCommandPageNo(1);
    await fetchCommandsPage(drawer, 1, commandPageSize, commandKeyword);
  };

  const closeDrawer = () => {
    setDrawer(null);
    setParams(null);
    setCommands(null);
    setParamKeyword('');
    setParamPageNo(1);
    setParamPageSize(20);
    setCommandKeyword('');
    setCommandPageNo(1);
    setCommandPageSize(20);
    setPhases([]);
    setMongoSummary(null);
  };

  return (
    <Card
      title="数据同步"
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
        本页展示 satellite_cache、param_cache、command_cache、test_batch_cache 的同步快照（默认由
        AssetCache:UsePostgreSql=true 写入 ConnectionStrings:Postgres 对应库，见 6.1）。流程为：全量卫星 → 每星参数
        （POST /api/v2/mass-data/basic/parameters）→ 每星指令（POST /api/v2/mass-data/basic/commands）→ 每星测试阶段 → 每星
        Mongo 配置。两列「参数 / 指令同步总量」为最近一次成功写入 PostgreSQL 缓存表的条数。禁用卫星后不可作为筛选模板参考卫星，全量同步不会自动改回启用。
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

      <Table<SatelliteListItem>
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
          { title: '型号代号', dataIndex: 'tasookNo', width: 130 },
          {
            title: '型号名称',
            dataIndex: 'tasookName',
            width: 140,
            render: (v) => v ?? <Text type="secondary">-</Text>
          },
          { title: '卫星代号', dataIndex: 'satelliteNo', width: 120 },
          { title: '卫星名称', dataIndex: 'satelliteName', width: 140 },
          {
            title: '测试阶段',
            dataIndex: 'developmentPhases',
            width: 260,
            render: (phases: string[] | undefined) => {
              if (phaseLabelsLoading) {
                return <Text type="secondary">加载中…</Text>;
              }
              if (!phases?.length) {
                return (
                  <Text type="secondary">
                    未同步
                    <br />
                    <Text type="secondary" style={{ fontSize: 12 }}>
                      请执行全量/单星同步
                    </Text>
                  </Text>
                );
              }
              return (
                <Space size={[4, 4]} wrap style={{ maxWidth: 248 }}>
                  {phases.map((name, i) => (
                    <Tag
                      key={`${name}-${i}`}
                      color={DEV_PHASE_TAG_COLORS[i % DEV_PHASE_TAG_COLORS.length]}
                    >
                      {name}
                    </Tag>
                  ))}
                </Space>
              );
            }
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
            title: '启用',
            dataIndex: 'isEnabled',
            width: 130,
            render: (enabled: boolean, record) => {
              const rowKey = `${record.tasookNo}_${record.satelliteNo}`;
              return (
                <Space size={4}>
                  <Switch
                    checked={enabled}
                    loading={togglingKey === rowKey}
                    onChange={(checked) => handleToggleEnabled(record, checked)}
                  />
                  <Tag color={enabled ? 'success' : 'default'}>{enabled ? '启用' : '禁用'}</Tag>
                </Space>
              );
            }
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
        width={Math.min(1200, window.innerWidth - 48)}
        title={
          drawer
            ? `${drawer.tasookName ?? drawer.tasookNo} / ${drawer.satelliteNo} · ${drawer.satelliteName}`
            : ''
        }
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
                        placeholder="按参数 ID / 代号 / 描述过滤"
                        value={paramKeyword}
                        onChange={(e) => setParamKeyword(e.target.value)}
                        onSearch={refreshParams}
                        style={{ width: 280 }}
                      />
                    </Space>
                    <Table<ParamCache>
                      size="small"
                      rowKey={paramCacheRowKey}
                      loading={paramsLoading}
                      dataSource={params?.items ?? []}
                      scroll={{ x: 1500 }}
                      columns={PARAM_CACHE_COLUMNS}
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
                    />
                  </>
                )
              },
              {
                key: 'commands',
                label: '指令 (command_cache)',
                children: (
                  <>
                    <Space style={{ marginBottom: 12 }}>
                      <Input.Search
                        allowClear
                        placeholder="按指令 ID / 代号 / 描述过滤"
                        value={commandKeyword}
                        onChange={(e) => setCommandKeyword(e.target.value)}
                        onSearch={refreshCommands}
                        style={{ width: 280 }}
                      />
                    </Space>
                    <Table<CommandCache>
                      size="small"
                      rowKey={commandCacheRowKey}
                      loading={commandsLoading}
                      dataSource={commands?.items ?? []}
                      scroll={{ x: 1300 }}
                      columns={COMMAND_CACHE_COLUMNS}
                      pagination={{
                        current: commandPageNo,
                        pageSize: commandPageSize,
                        total: commands?.total ?? 0,
                        showSizeChanger: true,
                        pageSizeOptions: ['20', '50', '100', '200', '500'],
                        showTotal: (total) => `共 ${total} 条`,
                        onChange: (page, size) => {
                          if (!drawer) return;
                          setCommandPageNo(page);
                          setCommandPageSize(size);
                          void fetchCommandsPage(drawer, page, size, commandKeyword);
                        },
                        onShowSizeChange: (_current, size) => {
                          if (!drawer) return;
                          setCommandPageNo(1);
                          setCommandPageSize(size);
                          void fetchCommandsPage(drawer, 1, size, commandKeyword);
                        }
                      }}
                    />
                  </>
                )
              },
              {
                key: 'phases',
                label: `测试阶段 (${phases.length})`,
                children: (
                  <>
                    {phases.length > 0 ? (
                      <Space size={[6, 6]} wrap style={{ marginBottom: 12 }}>
                        {phases.map((p, i) => (
                          <Tag
                            key={p.testBatchName}
                            color={DEV_PHASE_TAG_COLORS[i % DEV_PHASE_TAG_COLORS.length]}
                          >
                            {p.testBatchName}
                          </Tag>
                        ))}
                      </Space>
                    ) : (
                      <Alert
                        type="info"
                        showIcon
                        style={{ marginBottom: 12 }}
                        message="暂无测试阶段，请执行同步（卫星测试流程规划 POST /api/testplan/teststages → test_batch_cache）"
                      />
                    )}
                    <Table<TestPhase>
                      size="small"
                      rowKey={(record) => record.testBatchName}
                      pagination={false}
                      dataSource={phases}
                      columns={[
                        {
                          title: '测试阶段',
                          dataIndex: 'testBatchName',
                          render: (v, _record, index) => (
                            <Tag color={DEV_PHASE_TAG_COLORS[index % DEV_PHASE_TAG_COLORS.length]}>
                              {v}
                            </Tag>
                          )
                        },
                        {
                          title: '起止时间 (UTC)',
                          render: (_, record) => (
                            <Text>
                              {dayjs(record.startTs).format('YYYY-MM-DD HH:mm:ss')}
                              <br />
                              <Text type="secondary">~ {dayjs(record.endTs).format('YYYY-MM-DD HH:mm:ss')}</Text>
                            </Text>
                          )
                        }
                      ]}
                    />
                  </>
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

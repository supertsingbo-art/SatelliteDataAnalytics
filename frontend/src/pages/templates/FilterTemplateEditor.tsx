import { useEffect, useState } from 'react';
import {
  Alert,
  Button,
  Card,
  Col,
  Form,
  Input,
  InputNumber,
  Row,
  Select,
  Space,
  Spin,
  Table,
  Tag,
  TreeSelect,
  Typography,
  message
} from 'antd';
import { useNavigate, useParams } from 'react-router-dom';
import { filterTemplatesApi } from '@/api/templates';
import { groupsApi } from '@/api/groups';
import { assetsApi } from '@/api/assets';
import {
  FilterTargetParam,
  FilterTemplateConfigJson,
  ParamCache,
  RuleLeaf,
  RuleOperator,
  SatelliteCache,
  SatelliteGroupNode
} from '@/api/types';

const { Text } = Typography;

interface FlatRuleRow extends RuleLeaf {
  rowId: string;
}

const OPERATOR_OPTIONS: { value: RuleOperator; label: string }[] = [
  { value: '>', label: '>' },
  { value: '>=', label: '≥' },
  { value: '<', label: '<' },
  { value: '<=', label: '≤' },
  { value: '==', label: '=' },
  { value: '!=', label: '≠' },
  { value: 'between', label: 'between' }
];

type OutlierMethod = NonNullable<FilterTargetParam['outlier']>['method'];

const OUTLIER_METHODS: { value: OutlierMethod; label: string }[] = [
  { value: 'THRESHOLD', label: '阈值法 [min, max]' },
  { value: 'SIGMA', label: '统计法 (3σ)' },
  { value: 'IQR', label: 'IQR' },
  { value: 'MAD', label: 'MAD' },
  { value: 'HAMPEL', label: 'Hampel' }
];

function flattenRules(root: FilterTemplateConfigJson['ruleTree']): FlatRuleRow[] {
  if ('paramId' in root) {
    return [{ ...root, rowId: cryptoRandomId() }];
  }
  return root.children.flatMap((child) => flattenRules(child));
}

function cryptoRandomId(): string {
  return Math.random().toString(36).slice(2, 10);
}

interface SelectedSatellite {
  tasookNo: string;
  satelliteNo: string;
}

export function FilterTemplateEditor() {
  const params = useParams();
  const navigate = useNavigate();
  const isNew = !params.templateId;
  const templateId = params.templateId;
  const version = params.version ? Number(params.version) : undefined;

  const [loading, setLoading] = useState(true);
  const [groups, setGroups] = useState<SatelliteGroupNode[]>([]);
  const [satellites, setSatellites] = useState<SatelliteCache[]>([]);
  const [paramOptions, setParamOptions] = useState<ParamCache[]>([]);
  const [referenceSatellite, setReferenceSatellite] = useState<SelectedSatellite | null>(null);

  const [form] = Form.useForm<{
    templateName: string;
    description?: string;
    groupId: string;
    bufferBeforeSeconds: number;
    bufferAfterSeconds: number;
    durationSeconds: number;
  }>();

  const [rows, setRows] = useState<FlatRuleRow[]>([]);
  const [logicOp, setLogicOp] = useState<'AND' | 'OR'>('AND');
  const [targets, setTargets] = useState<FilterTargetParam[]>([]);
  const [editable, setEditable] = useState(true);
  const [status, setStatus] = useState<string>('Draft');

  useEffect(() => {
    (async () => {
      setLoading(true);
      try {
        const [tree, satResult] = await Promise.all([
          groupsApi.getTree(),
          assetsApi.listSatellites({ pageNo: 1, pageSize: 500 })
        ]);
        setGroups(tree);
        setSatellites(satResult.items);

        if (!isNew && templateId && version) {
          const detail = await filterTemplatesApi.detail(templateId, version);
          setStatus(detail.view.status);
          setEditable(detail.view.status === 'Draft');
          form.setFieldsValue({
            templateName: detail.view.templateName,
            description: detail.view.description ?? undefined,
            groupId: detail.view.groupId,
            bufferBeforeSeconds: detail.configJson.timeWindow.bufferBeforeSeconds ?? 0,
            bufferAfterSeconds: detail.configJson.timeWindow.bufferAfterSeconds ?? 0,
            durationSeconds: detail.configJson.durationSeconds ?? 10
          });
          setRows(flattenRules(detail.configJson.ruleTree));
          setTargets(detail.configJson.targetParams);
          if ('op' in detail.configJson.ruleTree) {
            setLogicOp(detail.configJson.ruleTree.op === 'OR' ? 'OR' : 'AND');
          }
        } else {
          form.setFieldsValue({
            templateName: '',
            groupId: tree[0]?.groupId,
            bufferBeforeSeconds: 5,
            bufferAfterSeconds: 5,
            durationSeconds: 10
          });
        }
      } finally {
        setLoading(false);
      }
    })();
  }, [templateId, version]);

  useEffect(() => {
    (async () => {
      if (!referenceSatellite) {
        setParamOptions([]);
        return;
      }
      const result = await assetsApi.listParams(
        referenceSatellite.tasookNo,
        referenceSatellite.satelliteNo,
        { pageNo: 1, pageSize: 500 }
      );
      setParamOptions(result.items);
    })();
  }, [referenceSatellite?.tasookNo, referenceSatellite?.satelliteNo]);

  const buildPayload = (): FilterTemplateConfigJson => {
    const groupId = form.getFieldValue('groupId');
    const ruleTree =
      rows.length === 1
        ? { paramId: rows[0].paramId, operator: rows[0].operator, value: rows[0].value }
        : {
            op: logicOp,
            children: rows.map((row) => ({
              paramId: row.paramId,
              operator: row.operator,
              value: row.value
            }))
          };
    return {
      scope: { groupId },
      timeWindow: {
        mode: 'TEST_BATCH',
        bufferBeforeSeconds: form.getFieldValue('bufferBeforeSeconds') ?? 0,
        bufferAfterSeconds: form.getFieldValue('bufferAfterSeconds') ?? 0
      },
      ruleTree,
      durationSeconds: form.getFieldValue('durationSeconds') ?? 10,
      targetParams: targets
    };
  };

  const onSave = async () => {
    const values = await form.validateFields();
    if (rows.length === 0) {
      message.error('至少需要 1 条参数条件');
      return;
    }
    if (targets.length === 0) {
      message.error('至少需要 1 个目标参数');
      return;
    }

    const config = buildPayload();
    if (isNew) {
      const created = await filterTemplatesApi.create({
        templateName: values.templateName,
        groupId: values.groupId,
        description: values.description ?? null,
        configJson: config
      });
      message.success('已保存为草稿');
      navigate(`/templates/filters/${created.view.templateId}/versions/${created.view.version}`, { replace: true });
    } else if (templateId && version) {
      await filterTemplatesApi.update(templateId, version, {
        templateName: values.templateName,
        groupId: values.groupId,
        description: values.description ?? null,
        configJson: config
      });
      message.success('已更新草稿');
    }
  };

  const onPublish = async () => {
    if (!templateId || !version) return;
    await onSave();
    await filterTemplatesApi.publish(templateId, version);
    message.success('模板已发布');
    setStatus('Published');
    setEditable(false);
  };

  interface GroupTreeNode {
    title: string;
    value: string;
    key: string;
    children?: GroupTreeNode[];
  }

  const groupTreeData = (nodes: SatelliteGroupNode[]): GroupTreeNode[] =>
    nodes.map((node) => ({
      title: `${node.groupName}（${node.groupPath}）`,
      value: node.groupId,
      key: node.groupId,
      children: node.children.length > 0 ? groupTreeData(node.children) : undefined
    }));

  return (
    <Spin spinning={loading}>
      <Card
        title={isNew ? '新建筛选模板' : `编辑筛选模板（版本 V${version}）`}
        extra={
          <Space>
            <Tag>{status}</Tag>
            <Button onClick={() => navigate('/templates/filters')}>返回列表</Button>
            <Button type="primary" disabled={!editable} onClick={onSave}>
              保存草稿
            </Button>
            {!isNew && (
              <Button type="primary" disabled={!editable} onClick={onPublish}>
                发布
              </Button>
            )}
          </Space>
        }
      >
        {!editable && (
          <Alert
            type="warning"
            showIcon
            style={{ marginBottom: 12 }}
            message="该版本已发布或归档，不可修改。请通过列表「克隆新版本」生成新的 Draft。"
          />
        )}

        <Form form={form} layout="vertical" disabled={!editable}>
          <Card type="inner" title="基本信息" style={{ marginBottom: 16 }}>
            <Row gutter={16}>
              <Col span={10}>
                <Form.Item label="模板名称" name="templateName" rules={[{ required: true, max: 256 }]}>
                  <Input placeholder="例如：稳态电压提取模板" />
                </Form.Item>
              </Col>
              <Col span={14}>
                <Form.Item
                  label="适用数据范围（卫星分组）"
                  name="groupId"
                  rules={[{ required: true, message: '请选择归属分组' }]}
                  extra="模板对该分组及其所有后代分组下的卫星可用"
                >
                  <TreeSelect
                    treeData={groupTreeData(groups)}
                    placeholder="请选择归属分组"
                    treeDefaultExpandAll
                    showSearch
                    treeNodeFilterProp="title"
                  />
                </Form.Item>
              </Col>
            </Row>
            <Form.Item label="描述" name="description">
              <Input.TextArea rows={2} />
            </Form.Item>
          </Card>

          <Card
            type="inner"
            title="1. 有效时间段提取规则"
            extra={
              <Space>
                <Text type="secondary">参考卫星（仅用于参数候选）：</Text>
                <Select
                  style={{ width: 220 }}
                  allowClear
                  placeholder="选择参考卫星"
                  options={satellites.map((sat) => ({
                    value: `${sat.tasookNo}||${sat.satelliteNo}`,
                    label: `${sat.tasookNo} / ${sat.satelliteNo}`
                  }))}
                  onChange={(v) => {
                    if (!v) {
                      setReferenceSatellite(null);
                      return;
                    }
                    const [t, s] = v.split('||');
                    setReferenceSatellite({ tasookNo: t, satelliteNo: s });
                  }}
                />
              </Space>
            }
            style={{ marginBottom: 16 }}
          >
            <Space style={{ marginBottom: 12 }}>
              <Text type="secondary">条件之间的逻辑算子：</Text>
              <Select
                value={logicOp}
                onChange={setLogicOp}
                options={[
                  { value: 'AND', label: 'AND' },
                  { value: 'OR', label: 'OR' }
                ]}
                style={{ width: 100 }}
                disabled={!editable}
              />
            </Space>
            <Table<FlatRuleRow>
              size="small"
              rowKey="rowId"
              dataSource={rows}
              pagination={false}
              columns={[
                {
                  title: '参数 paramId',
                  width: 280,
                  render: (_, record) => (
                    <Select
                      style={{ width: '100%' }}
                      value={record.paramId}
                      showSearch
                      placeholder="选择 param_cache 中的参数"
                      onChange={(value) => updateRow(record.rowId, { paramId: value })}
                      options={paramOptions.map((p) => ({
                        value: p.paramId,
                        label: `${p.paramId}（${p.paramName}）`
                      }))}
                      disabled={!editable}
                    />
                  )
                },
                {
                  title: '比较符',
                  width: 100,
                  render: (_, record) => (
                    <Select
                      value={record.operator}
                      onChange={(value: RuleOperator) => updateRow(record.rowId, { operator: value })}
                      options={OPERATOR_OPTIONS}
                      style={{ width: '100%' }}
                      disabled={!editable}
                    />
                  )
                },
                {
                  title: '阈值',
                  width: 200,
                  render: (_, record) => (
                    <Input
                      value={Array.isArray(record.value) ? record.value.join(',') : String(record.value)}
                      onChange={(e) => {
                        const text = e.target.value;
                        if (record.operator === 'between') {
                          const parts = text.split(',').map((s) => Number(s.trim()));
                          updateRow(record.rowId, { value: parts });
                        } else {
                          const num = Number(text);
                          updateRow(record.rowId, { value: isNaN(num) ? text : num });
                        }
                      }}
                      disabled={!editable}
                    />
                  )
                },
                {
                  title: '操作',
                  width: 80,
                  render: (_, record) => (
                    <Button
                      size="small"
                      type="link"
                      danger
                      disabled={!editable}
                      onClick={() => setRows(rows.filter((r) => r.rowId !== record.rowId))}
                    >
                      删除
                    </Button>
                  )
                }
              ]}
            />
            <Button
              type="dashed"
              style={{ marginTop: 12 }}
              disabled={!editable}
              onClick={() =>
                setRows([
                  ...rows,
                  { rowId: cryptoRandomId(), paramId: '', operator: '>', value: 0 }
                ])
              }
            >
              + 添加参数条件
            </Button>

            <Row gutter={16} style={{ marginTop: 16 }}>
              <Col span={8}>
                <Form.Item label="持续时长 durationSeconds" name="durationSeconds">
                  <InputNumber min={0} style={{ width: '100%' }} addonAfter="秒" />
                </Form.Item>
              </Col>
              <Col span={8}>
                <Form.Item label="前向边界缓冲 bufferBeforeSeconds" name="bufferBeforeSeconds">
                  <InputNumber min={0} style={{ width: '100%' }} addonAfter="秒" />
                </Form.Item>
              </Col>
              <Col span={8}>
                <Form.Item label="后向边界缓冲 bufferAfterSeconds" name="bufferAfterSeconds">
                  <InputNumber min={0} style={{ width: '100%' }} addonAfter="秒" />
                </Form.Item>
              </Col>
            </Row>
          </Card>

          <Card type="inner" title="2. 目标参数提取与质量规则">
            <Text type="secondary" style={{ display: 'block', marginBottom: 12 }}>
              在计算出的有效时间段内提取目标参数；离群规则仅打标，不剔除、不插值（与 hq_param_point.is_outlier 一致）。
            </Text>
            <Table<FilterTargetParam>
              size="small"
              rowKey={(_, idx) => `target_${idx}`}
              dataSource={targets}
              pagination={false}
              columns={[
                {
                  title: '提取目标参数 param_id',
                  render: (_, record, idx) => (
                    <Select
                      style={{ width: '100%' }}
                      value={record.paramId}
                      showSearch
                      placeholder="选择 param_cache 中的参数"
                      options={paramOptions.map((p) => ({
                        value: p.paramId,
                        label: `${p.paramId}（${p.paramName}）`
                      }))}
                      onChange={(value) =>
                        updateTarget(idx, {
                          paramId: value,
                          paramName: paramOptions.find((p) => p.paramId === value)?.paramName
                        })
                      }
                      disabled={!editable}
                    />
                  )
                },
                {
                  title: '离群判定方法',
                  width: 220,
                  render: (_, record, idx) => (
                    <Select<OutlierMethod>
                      style={{ width: '100%' }}
                      value={record.outlier?.method ?? 'SIGMA'}
                      options={OUTLIER_METHODS}
                      onChange={(value) =>
                        updateTarget(idx, {
                          outlier: { ...(record.outlier ?? { method: value }), method: value }
                        })
                      }
                      disabled={!editable}
                    />
                  )
                },
                {
                  title: '离群参数',
                  render: (_, record, idx) => (
                    <Space>
                      {record.outlier?.method === 'THRESHOLD' && (
                        <>
                          <InputNumber
                            placeholder="min"
                            value={record.outlier?.min}
                            onChange={(value) =>
                              updateTarget(idx, {
                                outlier: { ...record.outlier!, min: value ?? undefined }
                              })
                            }
                            disabled={!editable}
                          />
                          <InputNumber
                            placeholder="max"
                            value={record.outlier?.max}
                            onChange={(value) =>
                              updateTarget(idx, {
                                outlier: { ...record.outlier!, max: value ?? undefined }
                              })
                            }
                            disabled={!editable}
                          />
                        </>
                      )}
                      {record.outlier?.method === 'SIGMA' && (
                        <InputNumber
                          placeholder="σ 倍数"
                          value={record.outlier?.sigma ?? 3}
                          onChange={(value) =>
                            updateTarget(idx, {
                              outlier: { ...record.outlier!, sigma: value ?? 3 }
                            })
                          }
                          disabled={!editable}
                        />
                      )}
                      {record.outlier && !['THRESHOLD', 'SIGMA'].includes(record.outlier.method) && (
                        <InputNumber
                          placeholder="窗口"
                          value={record.outlier?.windowSize ?? 30}
                          onChange={(value) =>
                            updateTarget(idx, {
                              outlier: { ...record.outlier!, windowSize: value ?? 30 }
                            })
                          }
                          disabled={!editable}
                        />
                      )}
                    </Space>
                  )
                },
                {
                  title: '前向缓冲 (s)',
                  width: 120,
                  render: (_, record, idx) => (
                    <InputNumber
                      min={0}
                      value={record.boundaryBufferBeforeSec ?? 0}
                      onChange={(v) => updateTarget(idx, { boundaryBufferBeforeSec: v ?? 0 })}
                      disabled={!editable}
                    />
                  )
                },
                {
                  title: '后向缓冲 (s)',
                  width: 120,
                  render: (_, record, idx) => (
                    <InputNumber
                      min={0}
                      value={record.boundaryBufferAfterSec ?? 0}
                      onChange={(v) => updateTarget(idx, { boundaryBufferAfterSec: v ?? 0 })}
                      disabled={!editable}
                    />
                  )
                },
                {
                  title: '操作',
                  width: 80,
                  render: (_, _record, idx) => (
                    <Button
                      size="small"
                      type="link"
                      danger
                      disabled={!editable}
                      onClick={() => setTargets(targets.filter((_, i) => i !== idx))}
                    >
                      删除
                    </Button>
                  )
                }
              ]}
            />
            <Button
              type="dashed"
              style={{ marginTop: 12 }}
              disabled={!editable}
              onClick={() =>
                setTargets([
                  ...targets,
                  {
                    paramId: '',
                    outlier: { method: 'SIGMA', sigma: 3 },
                    boundaryBufferBeforeSec: 0,
                    boundaryBufferAfterSec: 0
                  }
                ])
              }
            >
              + 添加目标参数
            </Button>
          </Card>
        </Form>
      </Card>
    </Spin>
  );

  function updateRow(rowId: string, patch: Partial<FlatRuleRow>) {
    setRows((curr) => curr.map((r) => (r.rowId === rowId ? { ...r, ...patch } : r)));
  }

  function updateTarget(idx: number, patch: Partial<FilterTargetParam>) {
    setTargets((curr) =>
      curr.map((t, i) => (i === idx ? { ...t, ...patch, outlier: patch.outlier ?? t.outlier } : t))
    );
  }
}

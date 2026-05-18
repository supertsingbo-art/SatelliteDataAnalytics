import { useEffect, useMemo, useState } from 'react';
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
  formatParamCacheLabel,
  RuleLeaf,
  RuleOperator,
  SatelliteCache,
  SatelliteGroupMemberDto,
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

export function FilterTemplateEditor() {
  const params = useParams();
  const navigate = useNavigate();
  const isNew = !params.templateId;
  const templateId = params.templateId;
  const version = params.version ? Number(params.version) : undefined;

  const [loading, setLoading] = useState(true);
  const [groups, setGroups] = useState<SatelliteGroupNode[]>([]);
  const [satellites, setSatellites] = useState<SatelliteCache[]>([]);
  const [groupMembers, setGroupMembers] = useState<SatelliteGroupMemberDto[]>([]);
  const [paramOptions, setParamOptions] = useState<ParamCache[]>([]);

  const [form] = Form.useForm<{
    templateName: string;
    description?: string;
    groupId: string;
    referenceSatelliteKey?: string;
    bufferBeforeSeconds: number;
    bufferAfterSeconds: number;
    durationSeconds: number;
  }>();

  const watchedGroupId = Form.useWatch('groupId', form);
  const watchedRefKey = Form.useWatch('referenceSatelliteKey', form);

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
          const refT = detail.configJson.scope.referenceTasookNo;
          const refS = detail.configJson.scope.referenceSatelliteNo;
          const refKey =
            refT && refS ? `${refT}||${refS}` : undefined;
          const mems = await groupsApi.listMembers(detail.view.groupId, true);
          setGroupMembers(mems);
          form.setFieldsValue({
            templateName: detail.view.templateName,
            description: detail.view.description ?? undefined,
            groupId: detail.view.groupId,
            referenceSatelliteKey: refKey,
            bufferBeforeSeconds: detail.configJson.timeWindow.bufferBeforeSeconds ?? 0,
            bufferAfterSeconds: detail.configJson.timeWindow.bufferAfterSeconds ?? 0,
            durationSeconds: detail.configJson.durationSeconds ?? 10
          });
          setRows(flattenRules(detail.configJson.ruleTree));
          setTargets(detail.configJson.targetParams);
          if ('op' in detail.configJson.ruleTree) {
            setLogicOp(detail.configJson.ruleTree.op === 'OR' ? 'OR' : 'AND');
          }
          if (!refKey) {
            message.warning('该版本 config 缺少参考卫星，请补选「适用数据范围」中的具体星后再保存。');
          }
        } else {
          const defaultGid = tree[0]?.groupId;
          let initialRef: string | undefined;
          if (defaultGid) {
            const mems = await groupsApi.listMembers(defaultGid, true);
            setGroupMembers(mems);
            const first = mems[0];
            if (first) {
              initialRef = `${first.tasookNo}||${first.satelliteNo}`;
            }
          }
          form.setFieldsValue({
            templateName: '',
            groupId: defaultGid,
            referenceSatelliteKey: initialRef,
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
    let cancelled = false;
    (async () => {
      if (!watchedGroupId) {
        if (!cancelled) {
          setGroupMembers([]);
        }
        return;
      }
      const m = await groupsApi.listMembers(watchedGroupId, true);
      if (cancelled) {
        return;
      }
      setGroupMembers(m);
      const currentKey = form.getFieldValue('referenceSatelliteKey') as string | undefined;
      if (currentKey && !m.some((x) => `${x.tasookNo}||${x.satelliteNo}` === currentKey)) {
        form.setFieldsValue({ referenceSatelliteKey: undefined });
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [watchedGroupId, form]);

  useEffect(() => {
    (async () => {
      if (!watchedRefKey) {
        setParamOptions([]);
        return;
      }
      const parts = watchedRefKey.split('||');
      const t = parts[0];
      const s = parts[1];
      if (!t || !s) {
        setParamOptions([]);
        return;
      }
      const result = await assetsApi.listParams(t, s, { pageNo: 1, pageSize: 500 });
      setParamOptions(result.items);
    })();
  }, [watchedRefKey]);

  const memberSelectOptions = useMemo(
    () =>
      groupMembers.map((m) => {
        const sat = satellites.find((x) => x.tasookNo === m.tasookNo && x.satelliteNo === m.satelliteNo);
        const label = sat?.satelliteName
          ? `${m.tasookNo} / ${m.satelliteNo} · ${sat.satelliteName}`
          : `${m.tasookNo} / ${m.satelliteNo}`;
        return { value: `${m.tasookNo}||${m.satelliteNo}`, label };
      }),
    [groupMembers, satellites]
  );

  const buildPayload = (): FilterTemplateConfigJson => {
    const groupId = form.getFieldValue('groupId');
    const refKey = form.getFieldValue('referenceSatelliteKey') as string | undefined;
    if (!refKey) {
      throw new Error('MISSING_REF_SAT');
    }
    const [refTasook, refSat] = refKey.split('||');
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
      scope: {
        groupId,
        referenceTasookNo: refTasook,
        referenceSatelliteNo: refSat
      },
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
    if (!values.referenceSatelliteKey) {
      message.error('请选择适用数据范围中的具体参考卫星');
      return;
    }
    if (rows.length === 0) {
      message.error('至少需要 1 条参数条件');
      return;
    }
    if (targets.length === 0) {
      message.error('至少需要 1 个目标参数');
      return;
    }

    let config: FilterTemplateConfigJson;
    try {
      config = buildPayload();
    } catch {
      message.error('请选择适用数据范围中的具体参考卫星');
      return;
    }
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

        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 12 }}
          message="分组与参考星"
          description="卫星分组用于把单星编制的模板提升到组级复用：模板归属某分组后，该分组及子分组下的成员星均可选用。筛选条件与目标参数列表均绑定「参考卫星」在 param_cache 中的元数据。其它成员星在运行前应调用后端 GET /api/v1/templates/filters/{templateId}/versions/{version}/resolved-config?taskNo=…&satNo=…，按参数名称（忽略大小写）及原始 JSON 中的描述类字段做语义匹配，映射到本星参数 ID，避免同名不同码的错配。"
        />

        <Form form={form} layout="vertical" disabled={!editable}>
          <Card type="inner" title="基本信息" style={{ marginBottom: 16 }}>
            <Row gutter={16}>
              <Col span={10}>
                <Form.Item label="模板名称" name="templateName" rules={[{ required: true, max: 256 }]}>
                  <Input placeholder="例如：稳态电压提取模板" />
                </Form.Item>
              </Col>
              <Col span={14}>
                <Row gutter={12}>
                  <Col span={12}>
                    <Form.Item
                      label="归属卫星分组"
                      name="groupId"
                      rules={[{ required: true, message: '请选择归属分组' }]}
                      extra="模板归属该分组；子分组内卫星继承可用模板"
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
                  <Col span={12}>
                    <Form.Item
                      label="参考卫星（适用数据范围）"
                      name="referenceSatelliteKey"
                      rules={[{ required: true, message: '请选择分组内的一颗具体卫星' }]}
                      extra="下列筛选条件与目标参数均来自该星；须为当前分组（含子分组）成员"
                    >
                      <Select
                        showSearch
                        optionFilterProp="label"
                        placeholder={watchedGroupId ? '选择分组内卫星' : '请先选择归属分组'}
                        options={memberSelectOptions}
                        disabled={!watchedGroupId || memberSelectOptions.length === 0}
                      />
                    </Form.Item>
                  </Col>
                </Row>
              </Col>
            </Row>
            <Form.Item label="描述" name="description">
              <Input.TextArea rows={2} />
            </Form.Item>
          </Card>

          <Card type="inner" title="1. 有效时间段提取规则" style={{ marginBottom: 16 }}>
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
                          paramName:
                            paramOptions.find((p) => p.paramId === value)?.paraCode
                            ?? paramOptions.find((p) => p.paramId === value)?.paraDesc
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

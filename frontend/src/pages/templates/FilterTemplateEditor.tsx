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
import { CommandCacheSelect } from '@/components/CommandCacheSelect';
import { ParamCacheSelect } from '@/components/ParamCacheSelect';
import {
  CommandCache,
  FilterTargetParam,
  FilterTemplateConfigJson,
  ParamCache,
  formatParamCacheLabel,
  paramCacheId,
  RuleOperator,
  SatelliteListItem,
  SatelliteGroupMemberDto,
  SatelliteGroupNode
} from '@/api/types';

const { Text } = Typography;

type ConditionOperator = Exclude<RuleOperator, '=='>;

interface ParameterConditionRow {
  rowId: string;
  conditionId: string;
  paramId: string;
  operator: ConditionOperator;
  value: number | string | (number | string)[];
}

interface InstructionConditionRow {
  rowId: string;
  conditionId: string;
  commandId: string;
  channelId: number;
}

const OPERATOR_OPTIONS: { value: ConditionOperator; label: string }[] = [
  { value: '>', label: '>' },
  { value: '>=', label: '≥' },
  { value: '<', label: '<' },
  { value: '<=', label: '≤' },
  { value: '=', label: '=' },
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

function cryptoRandomId(): string {
  return Math.random().toString(36).slice(2, 10);
}

function buildDefaultExpression(conditionIds: string[]): string {
  return conditionIds.join(' && ');
}

function validateExpression(
  expression: string,
  allowedIds: Set<string>
): { valid: boolean; error?: string } {
  const trimmed = expression.trim();
  if (!trimmed) {
    return { valid: true };
  }

  const tokens: string[] = [];
  for (let i = 0; i < trimmed.length; ) {
    const ch = trimmed[i];
    if (!ch) {
      break;
    }
    if (/\s/.test(ch)) {
      i += 1;
      continue;
    }
    if (ch === '(' || ch === ')') {
      tokens.push(ch);
      i += 1;
      continue;
    }
    if (ch === '&' && trimmed[i + 1] === '&') {
      tokens.push('&&');
      i += 2;
      continue;
    }
    if (ch === '|' && trimmed[i + 1] === '|') {
      tokens.push('||');
      i += 2;
      continue;
    }
    if (/[A-Za-z_]/.test(ch)) {
      const start = i;
      i += 1;
      while (i < trimmed.length && /[A-Za-z0-9_]/.test(trimmed[i] || '')) {
        i += 1;
      }
      const id = trimmed.slice(start, i);
      tokens.push(id);
      continue;
    }
    return { valid: false, error: `表达式存在非法字符：${ch}` };
  }

  let expectOperand = true;
  let paren = 0;
  for (const token of tokens) {
    if (token === '(') {
      if (!expectOperand) {
        return { valid: false, error: "表达式中 '(' 前缺少逻辑符" };
      }
      paren += 1;
      continue;
    }
    if (token === ')') {
      if (expectOperand || paren <= 0) {
        return { valid: false, error: "表达式括号不匹配或 ')' 位置不合法" };
      }
      paren -= 1;
      continue;
    }
    if (token === '&&' || token === '||') {
      if (expectOperand) {
        return { valid: false, error: `逻辑符 ${token} 前缺少条件项` };
      }
      expectOperand = true;
      continue;
    }

    if (!expectOperand) {
      return { valid: false, error: `条件项 ${token} 前缺少逻辑符` };
    }
    if (!allowedIds.has(token)) {
      return { valid: false, error: `表达式引用未定义条件ID：${token}` };
    }
    expectOperand = false;
  }

  if (expectOperand) {
    return { valid: false, error: '表达式不能以逻辑符结尾' };
  }
  if (paren !== 0) {
    return { valid: false, error: '表达式括号不匹配' };
  }
  return { valid: true };
}

export function FilterTemplateEditor() {
  const params = useParams();
  const navigate = useNavigate();
  const isNew = !params.templateId;
  const templateId = params.templateId;
  const version = params.version ? Number(params.version) : undefined;

  const [loading, setLoading] = useState(true);
  const [groups, setGroups] = useState<SatelliteGroupNode[]>([]);
  const [satellites, setSatellites] = useState<SatelliteListItem[]>([]);
  const [groupMembers, setGroupMembers] = useState<SatelliteGroupMemberDto[]>([]);
  const [paramOptions, setParamOptions] = useState<ParamCache[]>([]);
  const [commandOptions, setCommandOptions] = useState<CommandCache[]>([]);
  const [optionsLoading, setOptionsLoading] = useState(false);

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

  const [parameterRows, setParameterRows] = useState<ParameterConditionRow[]>([]);
  const [startCommands, setStartCommands] = useState<InstructionConditionRow[]>([]);
  const [endCommands, setEndCommands] = useState<InstructionConditionRow[]>([]);
  const [startRelation, setStartRelation] = useState<'AND' | 'OR'>('OR');
  const [endRelation, setEndRelation] = useState<'AND' | 'OR'>('OR');
  const [startRangeSeconds, setStartRangeSeconds] = useState(0);
  const [endRangeSeconds, setEndRangeSeconds] = useState(0);
  const [expression, setExpression] = useState('');

  const [targets, setTargets] = useState<FilterTargetParam[]>([]);
  const [editable, setEditable] = useState(true);
  const [status, setStatus] = useState<string>('Draft');

  useEffect(() => {
    (async () => {
      setLoading(true);
      try {
        const [tree, satResult] = await Promise.all([
          groupsApi.getTree(),
          assetsApi.listSatellites({ pageNo: 1, pageSize: 500, enabledOnly: true })
        ]);
        setGroups(tree);
        setSatellites(satResult.items);

        if (!isNew && templateId && version) {
          const detail = await filterTemplatesApi.detail(templateId, version);
          setStatus(detail.view.status);
          setEditable(detail.view.status === 'Draft');
          const refT = detail.configJson.scope.referenceTasookNo;
          const refS = detail.configJson.scope.referenceSatelliteNo;
          const refKey = refT && refS ? `${refT}||${refS}` : undefined;
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

          const cc = detail.configJson.conditionConfig;
          if (cc) {
            const paramsRows = (cc.parameters ?? []).map((item) => ({
              rowId: cryptoRandomId(),
              conditionId: item.conditionId,
              paramId: item.paramId,
              operator: item.operator,
              value: item.value
            }));
            setParameterRows(paramsRows);
            setStartRelation(cc.instructions?.startRelation ?? 'OR');
            setEndRelation(cc.instructions?.endRelation ?? 'OR');
            setStartRangeSeconds(cc.instructions?.startRangeSeconds ?? 0);
            setEndRangeSeconds(cc.instructions?.endRangeSeconds ?? 0);
            setStartCommands(
              (cc.instructions?.startCommands ?? []).map((item) => ({
                rowId: cryptoRandomId(),
                conditionId: item.conditionId,
                commandId: item.commandId,
                channelId: item.channelId ?? 0
              }))
            );
            setEndCommands(
              (cc.instructions?.endCommands ?? []).map((item) => ({
                rowId: cryptoRandomId(),
                conditionId: item.conditionId,
                commandId: item.commandId,
                channelId: item.channelId ?? 0
              }))
            );
            setExpression(cc.expression ?? buildDefaultExpression(paramsRows.map((r) => r.conditionId)));
          } else {
            setParameterRows([]);
            setStartCommands([]);
            setEndCommands([]);
            setStartRelation('OR');
            setEndRelation('OR');
            setStartRangeSeconds(0);
            setEndRangeSeconds(0);
            setExpression('');
            message.warning('当前模板缺少 conditionConfig，已按新规则清空条件，请重新配置后保存。');
          }

          setTargets(detail.configJson.targetParams);
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
          setStartRelation('OR');
          setEndRelation('OR');
          setStartRangeSeconds(0);
          setEndRangeSeconds(0);
          setStartCommands([]);
          setEndCommands([]);
          setParameterRows([]);
          setExpression('');
        }
      } finally {
        setLoading(false);
      }
    })();
  }, [templateId, version, isNew, form]);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      if (!watchedGroupId) {
        if (!cancelled) {
          setGroupMembers([]);
        }
        return;
      }
      const members = await groupsApi.listMembers(watchedGroupId, true);
      if (cancelled) {
        return;
      }
      setGroupMembers(members);
      const currentKey = form.getFieldValue('referenceSatelliteKey') as string | undefined;
      if (currentKey && !members.some((m) => `${m.tasookNo}||${m.satelliteNo}` === currentKey)) {
        form.setFieldsValue({ referenceSatelliteKey: undefined });
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [watchedGroupId, form]);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      if (!watchedRefKey) {
        setParamOptions([]);
        setCommandOptions([]);
        return;
      }

      const [taskNo, satNo] = watchedRefKey.split('||');
      if (!taskNo || !satNo) {
        setParamOptions([]);
        setCommandOptions([]);
        return;
      }

      setOptionsLoading(true);
      try {
        const [paramResult, commandResult] = await Promise.all([
          assetsApi.listAllParams(taskNo, satNo),
          assetsApi.listAllCommands(taskNo, satNo)
        ]);
        if (!cancelled) {
          setParamOptions(paramResult.items);
          setCommandOptions(commandResult.items);
        }
      } finally {
        if (!cancelled) {
          setOptionsLoading(false);
        }
      }
    })();
    return () => {
      cancelled = true;
    };
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
    const cleanedParams = parameterRows
      .map((row) => ({
        conditionId: row.conditionId.trim(),
        paramId: row.paramId.trim(),
        operator: row.operator,
        value: row.value
      }))
      .filter((row) => row.conditionId && row.paramId);
    const cleanedStart = startCommands
      .map((row) => ({
        conditionId: row.conditionId.trim(),
        commandId: row.commandId.trim(),
        channelId: row.channelId
      }))
      .filter((row) => row.conditionId && row.commandId);
    const cleanedEnd = endCommands
      .map((row) => ({
        conditionId: row.conditionId.trim(),
        commandId: row.commandId.trim(),
        channelId: row.channelId
      }))
      .filter((row) => row.conditionId && row.commandId);

    const conditionIds = new Set<string>();
    for (const row of cleanedParams) {
      if (conditionIds.has(row.conditionId)) {
        throw new Error(`DUPLICATE_CONDITION_ID:${row.conditionId}`);
      }
      conditionIds.add(row.conditionId);
    }

    const expressionText = expression.trim();
    if (expressionText) {
      const validation = validateExpression(expressionText, conditionIds);
      if (!validation.valid) {
        throw new Error(validation.error || 'INVALID_EXPRESSION');
      }
    }

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
      conditionConfig: {
        instructions: {
          startRelation,
          endRelation,
          startRangeSeconds,
          endRangeSeconds,
          startCommands: cleanedStart,
          endCommands: cleanedEnd
        },
        parameters: cleanedParams,
        expression: expressionText || undefined
      },
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
    if (targets.length === 0) {
      message.error('至少需要 1 个目标参数');
      return;
    }

    let config: FilterTemplateConfigJson;
    try {
      config = buildPayload();
    } catch (error) {
      const text = String((error as Error).message ?? '');
      if (text.startsWith('DUPLICATE_CONDITION_ID:')) {
        message.error(`参数条件ID重复：${text.replace('DUPLICATE_CONDITION_ID:', '')}`);
        return;
      }
      message.error(text || '规则配置不合法，请检查表达式与条件ID');
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
            message="该版本已发布或归档，不可修改。请通过列表「复制为新模板」生成新的 Draft。"
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
                      extra="参数与指令候选均来自该星缓存"
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
            <Text type="secondary" style={{ display: 'block', marginBottom: 12 }}>
              配置阶段：指令从 command_cache 选择、参数从 param_cache 选择。执行阶段：按海量接口历史数据计算条件成立时间段。
            </Text>

            <Spin spinning={optionsLoading} tip="正在加载参考星参数/指令缓存…">
              <Card type="inner" size="small" title="1.1 起始指令条件" style={{ marginBottom: 12 }}>
                <Space style={{ marginBottom: 8 }}>
                  <Text>指令关系：</Text>
                  <Select
                    value={startRelation}
                    onChange={setStartRelation}
                    options={[
                      { value: 'OR', label: 'OR' },
                      { value: 'AND', label: 'AND' }
                    ]}
                    style={{ width: 100 }}
                  />
                  <Text>时间范围(秒)：</Text>
                  <InputNumber
                    min={0}
                    value={startRangeSeconds}
                    onChange={(value) => setStartRangeSeconds(value ?? 0)}
                    style={{ width: 140 }}
                  />
                </Space>
                <Table<InstructionConditionRow>
                  size="small"
                  rowKey="rowId"
                  pagination={false}
                  dataSource={startCommands}
                  columns={[
                    {
                      title: '条件ID',
                      width: 120,
                      render: (_, row) => (
                        <Input
                          value={row.conditionId}
                          onChange={(e) => updateInstructionRow('start', row.rowId, { conditionId: e.target.value })}
                        />
                      )
                    },
                    {
                      title: '指令',
                      render: (_, row) => (
                        <CommandCacheSelect
                          value={row.commandId}
                          commands={commandOptions}
                          loading={optionsLoading}
                          onChange={(value) => updateInstructionRow('start', row.rowId, { commandId: value })}
                        />
                      )
                    },
                    {
                      title: 'channelId',
                      width: 120,
                      render: (_, row) => (
                        <InputNumber
                          min={0}
                          value={row.channelId}
                          onChange={(value) => updateInstructionRow('start', row.rowId, { channelId: value ?? 0 })}
                        />
                      )
                    },
                    {
                      title: '操作',
                      width: 80,
                      render: (_, row) => (
                        <Button danger type="link" onClick={() => removeInstructionRow('start', row.rowId)}>
                          删除
                        </Button>
                      )
                    }
                  ]}
                />
                <Button
                  type="dashed"
                  style={{ marginTop: 10 }}
                  onClick={() =>
                    setStartCommands((curr) => [
                      ...curr,
                      {
                        rowId: cryptoRandomId(),
                        conditionId: `S${curr.length + 1}`,
                        commandId: '',
                        channelId: 0
                      }
                    ])
                  }
                >
                  + 添加起始指令
                </Button>
              </Card>

              <Card type="inner" size="small" title="1.2 结束指令条件" style={{ marginBottom: 12 }}>
                <Space style={{ marginBottom: 8 }}>
                  <Text>指令关系：</Text>
                  <Select
                    value={endRelation}
                    onChange={setEndRelation}
                    options={[
                      { value: 'OR', label: 'OR' },
                      { value: 'AND', label: 'AND' }
                    ]}
                    style={{ width: 100 }}
                  />
                  <Text>时间范围(秒)：</Text>
                  <InputNumber
                    min={0}
                    value={endRangeSeconds}
                    onChange={(value) => setEndRangeSeconds(value ?? 0)}
                    style={{ width: 140 }}
                  />
                </Space>
                <Table<InstructionConditionRow>
                  size="small"
                  rowKey="rowId"
                  pagination={false}
                  dataSource={endCommands}
                  columns={[
                    {
                      title: '条件ID',
                      width: 120,
                      render: (_, row) => (
                        <Input
                          value={row.conditionId}
                          onChange={(e) => updateInstructionRow('end', row.rowId, { conditionId: e.target.value })}
                        />
                      )
                    },
                    {
                      title: '指令',
                      render: (_, row) => (
                        <CommandCacheSelect
                          value={row.commandId}
                          commands={commandOptions}
                          loading={optionsLoading}
                          onChange={(value) => updateInstructionRow('end', row.rowId, { commandId: value })}
                        />
                      )
                    },
                    {
                      title: 'channelId',
                      width: 120,
                      render: (_, row) => (
                        <InputNumber
                          min={0}
                          value={row.channelId}
                          onChange={(value) => updateInstructionRow('end', row.rowId, { channelId: value ?? 0 })}
                        />
                      )
                    },
                    {
                      title: '操作',
                      width: 80,
                      render: (_, row) => (
                        <Button danger type="link" onClick={() => removeInstructionRow('end', row.rowId)}>
                          删除
                        </Button>
                      )
                    }
                  ]}
                />
                <Button
                  type="dashed"
                  style={{ marginTop: 10 }}
                  onClick={() =>
                    setEndCommands((curr) => [
                      ...curr,
                      {
                        rowId: cryptoRandomId(),
                        conditionId: `E${curr.length + 1}`,
                        commandId: '',
                        channelId: 0
                      }
                    ])
                  }
                >
                  + 添加结束指令
                </Button>
              </Card>

              <Card type="inner" size="small" title="1.3 参数条件 + 表达式">
                <Table<ParameterConditionRow>
                  size="small"
                  rowKey="rowId"
                  pagination={false}
                  dataSource={parameterRows}
                  columns={[
                    {
                      title: '条件ID',
                      width: 120,
                      render: (_, row) => (
                        <Input
                          value={row.conditionId}
                          onChange={(e) => updateParameterRow(row.rowId, { conditionId: e.target.value })}
                        />
                      )
                    },
                    {
                      title: '参数',
                      width: 320,
                      render: (_, row) => (
                        <ParamCacheSelect
                          value={row.paramId}
                          parameters={paramOptions}
                          loading={optionsLoading}
                          onChange={(value) => updateParameterRow(row.rowId, { paramId: value })}
                        />
                      )
                    },
                    {
                      title: '比较符',
                      width: 100,
                      render: (_, row) => (
                        <Select
                          value={row.operator}
                          options={OPERATOR_OPTIONS}
                          onChange={(value: ConditionOperator) => updateParameterRow(row.rowId, { operator: value })}
                        />
                      )
                    },
                    {
                      title: '阈值',
                      width: 220,
                      render: (_, row) => (
                        <Input
                          value={Array.isArray(row.value) ? row.value.join(',') : String(row.value)}
                          onChange={(e) => {
                            const text = e.target.value;
                            if (row.operator === 'between') {
                              const parts = text.split(',').map((x) => x.trim()).filter(Boolean);
                              updateParameterRow(row.rowId, { value: parts.map((x) => Number(x)) });
                            } else {
                              const num = Number(text);
                              updateParameterRow(row.rowId, { value: Number.isNaN(num) ? text : num });
                            }
                          }}
                        />
                      )
                    },
                    {
                      title: '操作',
                      width: 80,
                      render: (_, row) => (
                        <Button danger type="link" onClick={() => removeParameterRow(row.rowId)}>
                          删除
                        </Button>
                      )
                    }
                  ]}
                />
                <Space style={{ marginTop: 10 }}>
                  <Button
                    type="dashed"
                    onClick={() =>
                      setParameterRows((curr) => [
                        ...curr,
                        {
                          rowId: cryptoRandomId(),
                          conditionId: `P${curr.length + 1}`,
                          paramId: '',
                          operator: '>',
                          value: 0
                        }
                      ])
                    }
                  >
                    + 添加参数条件
                  </Button>
                  <Button
                    onClick={() => setExpression(buildDefaultExpression(parameterRows.map((r) => r.conditionId.trim()).filter(Boolean)))}
                  >
                    自动生成 AND 表达式
                  </Button>
                </Space>

                <Form.Item label="表达式（支持 &&、||、()，引用参数条件ID）" style={{ marginTop: 12, marginBottom: 8 }}>
                  <Input.TextArea value={expression} rows={3} onChange={(e) => setExpression(e.target.value)} />
                </Form.Item>
                <Space>
                  <Button onClick={() => setExpression((s) => `${s} && `)}>插入 &&</Button>
                  <Button onClick={() => setExpression((s) => `${s} || `)}>插入 ||</Button>
                  <Button onClick={() => setExpression((s) => `${s}(`)}>插入 (</Button>
                  <Button onClick={() => setExpression((s) => `${s})`)}>插入 )</Button>
                  <Button
                    onClick={() => {
                      const ids = new Set(parameterRows.map((x) => x.conditionId.trim()).filter(Boolean));
                      const result = validateExpression(expression, ids);
                      if (result.valid) {
                        message.success('表达式语法校验通过');
                      } else {
                        message.error(result.error || '表达式不合法');
                      }
                    }}
                  >
                    语法检查
                  </Button>
                </Space>
              </Card>
            </Spin>

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
              在有效时间段内提取目标参数（无条件时为任务数据时间窗）；离群规则仅打标，不剔除、不插值。
            </Text>
            <Spin spinning={optionsLoading} tip="正在加载参考星参数缓存…">
              <Table<FilterTargetParam>
                size="small"
                rowKey={(_, idx) => `target_${idx}`}
                dataSource={targets}
                pagination={false}
                columns={[
                  {
                    title: '提取目标参数',
                    render: (_, record, idx) => (
                      <ParamCacheSelect
                        value={record.paramId}
                        parameters={paramOptions}
                        loading={optionsLoading}
                        onChange={(value) => {
                          const picked = paramOptions.find((p) => paramCacheId(p) === value);
                          updateTarget(idx, {
                            paramId: value,
                            paramName: picked ? formatParamCacheLabel(picked) : undefined
                          });
                        }}
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
                            />
                            <InputNumber
                              placeholder="max"
                              value={record.outlier?.max}
                              onChange={(value) =>
                                updateTarget(idx, {
                                  outlier: { ...record.outlier!, max: value ?? undefined }
                                })
                              }
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
                      />
                    )
                  },
                  {
                    title: '操作',
                    width: 80,
                    render: (_, _record, idx) => (
                      <Button size="small" type="link" danger onClick={() => setTargets(targets.filter((_, i) => i !== idx))}>
                        删除
                      </Button>
                    )
                  }
                ]}
              />
            </Spin>
            <Button
              type="dashed"
              style={{ marginTop: 12 }}
              disabled={optionsLoading}
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

  function updateParameterRow(rowId: string, patch: Partial<ParameterConditionRow>) {
    setParameterRows((curr) => curr.map((r) => (r.rowId === rowId ? { ...r, ...patch } : r)));
  }

  function removeParameterRow(rowId: string) {
    setParameterRows((curr) => curr.filter((r) => r.rowId !== rowId));
  }

  function updateInstructionRow(
    side: 'start' | 'end',
    rowId: string,
    patch: Partial<InstructionConditionRow>
  ) {
    if (side === 'start') {
      setStartCommands((curr) => curr.map((r) => (r.rowId === rowId ? { ...r, ...patch } : r)));
      return;
    }

    setEndCommands((curr) => curr.map((r) => (r.rowId === rowId ? { ...r, ...patch } : r)));
  }

  function removeInstructionRow(side: 'start' | 'end', rowId: string) {
    if (side === 'start') {
      setStartCommands((curr) => curr.filter((r) => r.rowId !== rowId));
      return;
    }

    setEndCommands((curr) => curr.filter((r) => r.rowId !== rowId));
  }

  function updateTarget(idx: number, patch: Partial<FilterTargetParam>) {
    setTargets((curr) =>
      curr.map((t, i) => (i === idx ? { ...t, ...patch, outlier: patch.outlier ?? t.outlier } : t))
    );
  }
}

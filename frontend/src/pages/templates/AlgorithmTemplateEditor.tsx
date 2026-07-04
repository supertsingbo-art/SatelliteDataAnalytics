import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  Alert,
  Button,
  Card,
  Col,
  Drawer,
  Form,
  Input,
  InputNumber,
  Popconfirm,
  Row,
  Select,
  Space,
  Spin,
  Switch,
  Tag,
  Typography,
  message
} from 'antd';
import { useNavigate, useParams } from 'react-router-dom';
import ReactFlow, {
  Background,
  Connection,
  Controls,
  Edge,
  EdgeChange,
  MiniMap,
  Node,
  NodeChange,
  ReactFlowProvider,
  addEdge,
  applyEdgeChanges,
  applyNodeChanges,
  useReactFlow
} from 'reactflow';
import { algoRegistryApi, algoTemplatesApi } from '@/api/templates';
import { assetsApi } from '@/api/assets';
import {
  AlgorithmCategory,
  AlgorithmConfigJson,
  AlgorithmReactFlowEdge,
  AlgorithmReactFlowJson,
  AlgorithmReactFlowNode,
  AlgorithmRegistryEntry,
  AlgorithmTemplateValidationIssue,
  ParamCache,
  formatParamCacheLabel,
  paramCacheId,
  SatelliteListItem,
  TestPhase
} from '@/api/types';

const { Text, Title } = Typography;

const CATEGORY_GROUPS: {
  category: AlgorithmCategory | 'source';
  title: string;
  hint?: string;
}[] = [
  { category: 'source', title: '1. 数据输入', hint: '从 ClickHouse / 算法结果取数（执行时才查）' },
  { category: 'Stats', title: '2. 基础统计（第一阶）' },
  { category: 'Spectrum', title: '3. 频域处理（第二阶）' },
  { category: 'Align', title: '4. 时序对齐（第三阶）', hint: '占位，运行端二阶段开放' },
  { category: 'Cluster', title: '5. 聚类分析（第四阶）' },
  { category: 'Compare', title: '6. 输出与比对', hint: '「结果落库」保存计算值；阈值/3σ 判定保存判定标记' },
  { category: 'Output', title: '6. 输出与比对' }
];

const SOURCE_NODE_KEY = '__data_source__';

interface NodeMeta {
  nodeRef: string;
  category: 'source' | 'stats' | 'spectrum' | 'align' | 'cluster' | 'compare' | 'output';
  algorithmCode?: string;
  displayName: string;
  runtime?: 'BUILTIN' | 'PYTHON' | 'JS';
  paramsSchema?: unknown;
  paramsValues: Record<string, unknown>;
  // For source nodes only:
  source?: {
    sourceTable: 'hq_param_point' | 'algo_result';
    paramIds: string[];
    valueField: 'processed_value' | 'raw_value';
    includeOutliers: boolean;
    outputName: string;
  };
}

interface NodeDataPayload {
  meta: NodeMeta;
  // ReactFlow's data field can carry anything that serializes to JSON.
  [key: string]: unknown;
}

function uid(): string {
  return Math.random().toString(36).slice(2, 10);
}

function categoryToReactFlowType(cat: NodeMeta['category']): string {
  return cat;
}

export function AlgorithmTemplateEditor() {
  return (
    <ReactFlowProvider>
      <EditorInner />
    </ReactFlowProvider>
  );
}

function EditorInner() {
  const params = useParams();
  const navigate = useNavigate();
  const isNew = !params.templateId;
  const templateId = params.templateId;
  const version = params.version ? Number(params.version) : undefined;

  const [loading, setLoading] = useState(true);
  const [registry, setRegistry] = useState<AlgorithmRegistryEntry[]>([]);
  const [satellites, setSatellites] = useState<SatelliteListItem[]>([]);

  const [templateName, setTemplateName] = useState('');
  const [description, setDescription] = useState<string | undefined>(undefined);
  const [status, setStatus] = useState<string>('Draft');
  const [editable, setEditable] = useState(true);
  const [versionOptions, setVersionOptions] = useState<{ value: number; label: string }[]>([]);

  const [nodes, setNodes] = useState<Node<NodeDataPayload>[]>([]);
  const [edges, setEdges] = useState<Edge[]>([]);
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);

  const [validationIssues, setValidationIssues] = useState<AlgorithmTemplateValidationIssue[] | null>(null);
  const [trialOpen, setTrialOpen] = useState(false);

  const reactFlowWrapper = useRef<HTMLDivElement | null>(null);
  const { screenToFlowPosition } = useReactFlow();

  // -------------------- Bootstrap --------------------
  useEffect(() => {
    (async () => {
      setLoading(true);
      try {
        const [reg, sat] = await Promise.all([
          algoRegistryApi.registry(),
          assetsApi.listSatellites({ pageNo: 1, pageSize: 500, enabledOnly: true })
        ]);
        setRegistry(reg);
        setSatellites(sat.items);

        if (!isNew && templateId && version) {
          const [detail, versions] = await Promise.all([
            algoTemplatesApi.detail(templateId, version),
            algoTemplatesApi.versions(templateId)
          ]);
          setVersionOptions(
            versions.map((v) => ({
              value: v.version,
              label: `V${v.version} · ${v.status}`
            }))
          );
          setStatus(detail.view.status);
          setEditable(detail.view.status === 'Draft');
          setTemplateName(detail.view.templateName);
          setDescription(detail.view.description ?? undefined);
          hydrate(detail.reactFlowJson, detail.configJson);
        } else {
          setTemplateName('未命名算法模板');
        }
      } finally {
        setLoading(false);
      }
    })();
  }, [templateId, version]);

  function hydrate(rf: AlgorithmReactFlowJson, cfg: AlgorithmConfigJson) {
    const algoByRef = new Map<string, NonNullable<AlgorithmConfigJson['nodes']>[number]>();
    cfg.nodes?.forEach((n) => algoByRef.set(n.nodeRef, n));
    const inputByRef = new Map<string, NonNullable<AlgorithmConfigJson['dataInputs']>[number]>();
    cfg.dataInputs?.forEach((n) => inputByRef.set(n.nodeRef, n));

    const built: Node<NodeDataPayload>[] = rf.nodes.map((n) => {
      const data = (n.data ?? {}) as Record<string, unknown>;
      const nodeRef = (data.nodeRef as string | undefined) ?? n.id;
      const displayName = (data.displayName as string | undefined) ?? n.type;
      const cat = (n.type as NodeMeta['category']) ?? 'source';
      const meta: NodeMeta = {
        nodeRef,
        category: cat,
        algorithmCode: data.algorithmCode as string | undefined,
        runtime: data.runtime as NodeMeta['runtime'],
        displayName,
        paramsSchema: data.paramsSchema,
        paramsValues: ((data.paramsValues as Record<string, unknown> | undefined) ??
          algoByRef.get(nodeRef)?.params ??
          {}) as Record<string, unknown>,
        source:
          cat === 'source'
            ? (() => {
                const cfgInput = inputByRef.get(nodeRef);
                return {
                  sourceTable: cfgInput?.sourceTable ?? 'hq_param_point',
                  paramIds: cfgInput?.paramIds ?? [],
                  valueField: cfgInput?.valueField ?? 'processed_value',
                  includeOutliers: cfgInput?.includeOutliers ?? false,
                  outputName: cfgInput?.outputName ?? `series_${nodeRef}`
                };
              })()
            : undefined
      };
      return {
        id: n.id,
        type: 'default',
        position: n.position,
        data: { meta, label: meta.displayName }
      } as Node<NodeDataPayload>;
    });

    const builtEdges: Edge[] = rf.edges.map((e) => ({
      id: e.id,
      source: e.source,
      target: e.target,
      sourceHandle: e.sourceHandle ?? undefined,
      targetHandle: e.targetHandle ?? undefined
    }));

    setNodes(built);
    setEdges(builtEdges);
  }

  // -------------------- Component library --------------------
  const groupedRegistry = useMemo(() => {
    const map = new Map<AlgorithmCategory | 'source', AlgorithmRegistryEntry[]>();
    for (const item of registry) {
      const list = map.get(item.category) ?? [];
      list.push(item);
      map.set(item.category, list);
    }
    return map;
  }, [registry]);

  // -------------------- React Flow handlers --------------------
  const onNodesChange = useCallback(
    (changes: NodeChange[]) => {
      if (!editable && changes.some((c) => c.type === 'remove' || c.type === 'position')) {
        return;
      }
      const removedIds = changes
        .filter((c): c is NodeChange & { type: 'remove'; id: string } => c.type === 'remove')
        .map((c) => c.id);
      if (removedIds.length > 0) {
        setEdges((curr) =>
          curr.filter((e) => !removedIds.includes(e.source) && !removedIds.includes(e.target))
        );
        setSelectedNodeId((prev) => (prev && removedIds.includes(prev) ? null : prev));
      }
      setNodes((curr) => applyNodeChanges(changes, curr) as Node<NodeDataPayload>[]);
    },
    [editable]
  );

  const removeNodesById = useCallback(
    (nodeIds: string[]) => {
      if (!editable || nodeIds.length === 0) return;
      const idSet = new Set(nodeIds);
      setNodes((curr) => curr.filter((n) => !idSet.has(n.id)));
      setEdges((curr) => curr.filter((e) => !idSet.has(e.source) && !idSet.has(e.target)));
      setSelectedNodeId((prev) => (prev && idSet.has(prev) ? null : prev));
    },
    [editable]
  );

  const onNodesDelete = useCallback(
    (deleted: Node[]) => {
      const ids = new Set(deleted.map((n) => n.id));
      setEdges((curr) => curr.filter((e) => !ids.has(e.source) && !ids.has(e.target)));
      setSelectedNodeId((prev) => (prev && ids.has(prev) ? null : prev));
    },
    []
  );

  const onEdgesChange = useCallback(
    (changes: EdgeChange[]) => {
      if (!editable && changes.some((c) => c.type === 'remove')) return;
      setEdges((curr) => applyEdgeChanges(changes, curr));
    },
    [editable]
  );

  const onConnect = useCallback(
    (connection: Connection) => {
      if (!editable) return;
      setEdges((curr) => addEdge({ ...connection, id: `e_${uid()}` }, curr));
    },
    [editable]
  );

  const handleDrop = useCallback(
    (event: React.DragEvent<HTMLDivElement>) => {
      event.preventDefault();
      if (!editable) return;
      const payload = event.dataTransfer.getData('application/algo-component');
      if (!payload) return;
      const data: { algorithmCode: string; isSource?: boolean } = JSON.parse(payload);
      const position = screenToFlowPosition({ x: event.clientX, y: event.clientY });

      if (data.isSource) {
        const nodeRef = `in_${uid()}`;
        const meta: NodeMeta = {
          nodeRef,
          category: 'source',
          displayName: '数据输入节点',
          paramsValues: {},
          source: {
            sourceTable: 'hq_param_point',
            paramIds: [],
            valueField: 'processed_value',
            includeOutliers: false,
            outputName: nodeRef
          }
        };
        setNodes((curr) => [
          ...curr,
          {
            id: `src_${uid()}`,
            type: 'default',
            position,
            data: { meta, label: meta.displayName }
          }
        ]);
        return;
      }

      const algo = registry.find((e) => e.algorithmCode === data.algorithmCode);
      if (!algo) return;
      const nodeRef = `${algo.algorithmCode}_${uid()}`;
      const meta: NodeMeta = {
        nodeRef,
        category: lowerCategory(algo.category),
        algorithmCode: algo.algorithmCode,
        runtime: runtimeToWire(algo.runtime),
        displayName: algo.displayName,
        paramsSchema: algo.paramsSchemaJson,
        paramsValues: extractDefaults(algo.paramsSchemaJson)
      };
      setNodes((curr) => [
        ...curr,
        {
          id: `${categoryToReactFlowType(meta.category)}_${uid()}`,
          type: 'default',
          position,
          data: { meta, label: meta.displayName }
        }
      ]);
    },
    [editable, registry, screenToFlowPosition]
  );

  const handleDragStart = (
    event: React.DragEvent<HTMLDivElement>,
    payload: { algorithmCode?: string; isSource?: boolean }
  ) => {
    event.dataTransfer.setData('application/algo-component', JSON.stringify(payload));
    event.dataTransfer.effectAllowed = 'move';
  };

  // -------------------- Persist / publish / validate --------------------
  const buildSnapshot = (): { reactFlowJson: AlgorithmReactFlowJson; configJson: AlgorithmConfigJson } => {
    const rfNodes: AlgorithmReactFlowNode[] = nodes.map((n) => {
      const meta = n.data.meta;
      return {
        id: n.id,
        type: meta.category,
        position: n.position,
        data: {
          nodeRef: meta.nodeRef,
          algorithmCode: meta.algorithmCode,
          runtime: meta.runtime,
          displayName: meta.displayName,
          paramsValues: meta.paramsValues,
          params: meta.paramsValues
        }
      };
    });
    const rfEdges: AlgorithmReactFlowEdge[] = edges.map((e) => ({
      id: e.id,
      source: e.source,
      target: e.target,
      sourceHandle: (e.sourceHandle ?? undefined) as string | undefined,
      targetHandle: (e.targetHandle ?? undefined) as string | undefined
    }));

    const dataInputs: AlgorithmConfigJson['dataInputs'] = nodes
      .filter((n) => n.data.meta.category === 'source')
      .map((n) => ({
        nodeRef: n.data.meta.nodeRef,
        sourceTable: n.data.meta.source!.sourceTable,
        paramIds: n.data.meta.source!.paramIds,
        valueField: n.data.meta.source!.valueField,
        includeOutliers: n.data.meta.source!.includeOutliers,
        outputName: n.data.meta.source!.outputName
      }));
    const algoNodes: AlgorithmConfigJson['nodes'] = nodes
      .filter((n) => n.data.meta.category !== 'source')
      .map((n) => ({
        nodeRef: n.data.meta.nodeRef,
        nodeType: (n.data.meta.algorithmCode ?? '').toUpperCase(),
        params: n.data.meta.paramsValues
      }));

    return {
      reactFlowJson: { nodes: rfNodes, edges: rfEdges },
      configJson: { dataInputs, nodes: algoNodes }
    };
  };

  const onSave = async () => {
    if (!templateName.trim()) {
      message.error('请填写模板名称');
      return;
    }
    const snapshot = buildSnapshot();
    if (isNew) {
      const created = await algoTemplatesApi.create({
        templateName,
        description: description ?? null,
        reactFlowJson: snapshot.reactFlowJson,
        configJson: snapshot.configJson
      });
      message.success('已保存为草稿');
      navigate(`/templates/algorithms/${created.view.templateId}/versions/${created.view.version}`, { replace: true });
    } else if (templateId && version) {
      await algoTemplatesApi.update(templateId, version, {
        templateName,
        description: description ?? null,
        reactFlowJson: snapshot.reactFlowJson,
        configJson: snapshot.configJson
      });
      message.success('已更新草稿');
    }
  };

  const onValidate = async () => {
    if (!templateId || !version) {
      message.warning('请先保存草稿后再校验');
      return;
    }
    const result = await algoTemplatesApi.validate(templateId, version);
    setValidationIssues(result.issues);
    if (result.valid) {
      message.success(`DAG 校验通过（${result.nodeCount} 个节点 / ${result.edgeCount} 条边）`);
    } else {
      message.error(`DAG 校验失败：共 ${result.issues.length} 项问题`);
    }
  };

  const onPublish = async () => {
    if (!templateId || !version) return;
    await onSave();
    try {
      await algoTemplatesApi.publish(templateId, version);
      message.success('模板已发布');
      setStatus('Published');
      setEditable(false);
    } catch (err) {
      // 错误已被全局拦截器提示；这里不再重复
      throw err;
    }
  };

  const selectedNode = nodes.find((n) => n.id === selectedNodeId);

  const flowNodes = useMemo(
    () => nodes.map((n) => ({ ...n, deletable: editable })),
    [nodes, editable]
  );

  const updateSelectedMeta = (patch: Partial<NodeMeta>) => {
    if (!selectedNode) return;
    setNodes((curr) =>
      curr.map((n) =>
        n.id === selectedNode.id
          ? {
              ...n,
              data: {
                ...n.data,
                meta: { ...n.data.meta, ...patch },
                label: (patch.displayName as string | undefined) ?? n.data.label
              }
            }
          : n
      )
    );
  };

  return (
    <Spin spinning={loading}>
      <Card
        bodyStyle={{ padding: 0 }}
        title={
          <Space>
            <Input
              value={templateName}
              onChange={(e) => setTemplateName(e.target.value)}
              disabled={!editable}
              style={{ width: 320 }}
              placeholder="算法模板名称"
            />
            <Tag>{status}</Tag>
            {!isNew && version && versionOptions.length > 0 && (
              <Select
                size="small"
                value={version}
                style={{ width: 160 }}
                options={versionOptions}
                onChange={(nextVersion) =>
                  navigate(`/templates/algorithms/${templateId}/versions/${nextVersion}`)
                }
              />
            )}
            {!isNew && version && versionOptions.length === 0 && <Tag color="blue">V{version}</Tag>}
          </Space>
        }
        extra={
          <Space>
            <Button onClick={() => navigate('/templates/algorithms')}>返回列表</Button>
            <Button onClick={onValidate}>仅校验</Button>
            <Button disabled={isNew} onClick={() => setTrialOpen(true)}>
              测试运行
            </Button>
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
            message="该版本已发布或归档，画布、节点参数与连接均为只读。请在列表使用「复制为新模板」生成新的 Draft 后修改。"
          />
        )}

        <Row wrap={false} style={{ height: 'calc(100vh - 56px - 90px)' }}>
          <Col flex="240px" style={{ borderRight: '1px solid #e2e8f0', overflow: 'auto' }}>
            <ComponentLibrary
              groupedRegistry={groupedRegistry}
              onDragStart={handleDragStart}
              disabled={!editable}
            />
          </Col>
          <Col flex="auto" style={{ position: 'relative' }}>
            <div
              ref={reactFlowWrapper}
              style={{ width: '100%', height: '100%' }}
              onDragOver={(e) => {
                e.preventDefault();
                e.dataTransfer.dropEffect = 'move';
              }}
              onDrop={handleDrop}
            >
              <ReactFlow
                nodes={flowNodes}
                edges={edges}
                deleteKeyCode={editable ? ['Delete', 'Backspace'] : null}
                onNodesChange={onNodesChange}
                onNodesDelete={onNodesDelete}
                onEdgesChange={onEdgesChange}
                onConnect={onConnect}
                onNodeClick={(_e, n) => setSelectedNodeId(n.id)}
                onPaneClick={() => setSelectedNodeId(null)}
                fitView
              >
                <MiniMap pannable zoomable />
                <Controls />
                <Background gap={18} />
              </ReactFlow>
            </div>
            <div
              style={{
                position: 'absolute',
                left: 16,
                bottom: 12,
                background: '#ffffff',
                border: '1px solid #e2e8f0',
                borderRadius: 8,
                padding: '8px 12px',
                fontSize: 12,
                color: '#64748b',
                maxWidth: 520,
                boxShadow: '0 1px 2px rgba(15,23,42,0.04)'
              }}
            >
              DAG 校验：必须 ≥1 个数据输入节点、≥1 个输出节点（结果落库或判定）、无环；
              多个算法结果请分别连线到各自的「结果落库」节点。选中节点后按 Delete 键，或在右侧属性面板删除。
              发布时序列化保存 react_flow_json 与 config_json。编辑期不查询 ClickHouse。
            </div>

            {validationIssues && validationIssues.length > 0 && (
              <div
                style={{
                  position: 'absolute',
                  right: 16,
                  bottom: 12,
                  background: '#fff7ed',
                  border: '1px solid #fdba74',
                  borderRadius: 8,
                  padding: '8px 12px',
                  fontSize: 12,
                  color: '#7c2d12',
                  maxWidth: 360,
                  maxHeight: 240,
                  overflow: 'auto'
                }}
              >
                <div style={{ fontWeight: 600, marginBottom: 6 }}>DAG 校验问题</div>
                {validationIssues.map((issue, idx) => (
                  <div key={idx}>
                    <Text code>{issue.code}</Text> {issue.message}
                    {issue.nodeId && <Text type="secondary"> ({issue.nodeId})</Text>}
                  </div>
                ))}
              </div>
            )}
          </Col>
          <Col flex="320px" style={{ borderLeft: '1px solid #e2e8f0', overflow: 'auto' }}>
            <PropertiesPanel
              key={selectedNodeId ?? 'none'}
              node={selectedNode ?? null}
              editable={editable}
              registry={registry}
              satellites={satellites}
              onChange={updateSelectedMeta}
              onDelete={() => selectedNodeId && removeNodesById([selectedNodeId])}
            />
          </Col>
        </Row>
      </Card>

      <TrialRunDialog
        open={trialOpen}
        onClose={() => setTrialOpen(false)}
        templateId={templateId}
        version={version}
        satellites={satellites}
      />
    </Spin>
  );
}

// -------------------- Component library panel --------------------
function ComponentLibrary({
  groupedRegistry,
  onDragStart,
  disabled
}: {
  groupedRegistry: Map<AlgorithmCategory | 'source', AlgorithmRegistryEntry[]>;
  onDragStart: (e: React.DragEvent<HTMLDivElement>, p: { algorithmCode?: string; isSource?: boolean }) => void;
  disabled: boolean;
}) {
  return (
    <div style={{ padding: 12 }}>
      <Title level={5} style={{ marginBottom: 8 }}>
        组件库
      </Title>
      <Text type="secondary" style={{ fontSize: 12, display: 'block', marginBottom: 12 }}>
        从下方拖拽节点到画布；具体节点项由算法仓库注册表动态提供。
      </Text>

      {CATEGORY_GROUPS.map((group) => {
        if (group.category === 'source') {
          return (
            <CategoryBlock key="source" title={group.title} hint={group.hint}>
              <ComponentItem
                key={SOURCE_NODE_KEY}
                label="📥 ClickHouse 时序输入"
                disabled={disabled}
                onDragStart={(e) => onDragStart(e, { isSource: true })}
              />
            </CategoryBlock>
          );
        }
        const list = groupedRegistry.get(group.category) ?? [];
        // Output 与 Compare 合并展示
        const merged =
          group.category === 'Output'
            ? [] // already merged into Compare title block
            : group.category === 'Compare'
            ? [...list, ...(groupedRegistry.get('Output') ?? [])]
            : list;
        if (group.category === 'Output') return null;
        return (
          <CategoryBlock key={group.category} title={group.title} hint={group.hint}>
            {merged.length === 0 && (
              <Text type="secondary" style={{ fontSize: 12, display: 'block', padding: '4px 8px' }}>
                暂无已发布算法
              </Text>
            )}
            {merged.map((entry) => (
              <ComponentItem
                key={`${entry.algorithmCode}_${entry.version}`}
                label={`${categoryEmoji(entry.category)} ${entry.displayName}`}
                disabled={disabled}
                onDragStart={(e) => onDragStart(e, { algorithmCode: entry.algorithmCode })}
                hint={entry.algorithmCode}
              />
            ))}
          </CategoryBlock>
        );
      })}
    </div>
  );
}

function CategoryBlock({ title, hint, children }: { title: string; hint?: string; children: React.ReactNode }) {
  return (
    <div style={{ marginBottom: 12 }}>
      <div
        style={{
          fontSize: 12,
          color: '#475569',
          fontWeight: 600,
          padding: '4px 6px',
          background: '#f1f5f9',
          borderRadius: 4
        }}
      >
        {title}
      </div>
      {hint && (
        <div style={{ fontSize: 11, color: '#94a3b8', padding: '2px 6px' }}>{hint}</div>
      )}
      <div style={{ marginTop: 4 }}>{children}</div>
    </div>
  );
}

function ComponentItem({
  label,
  disabled,
  hint,
  onDragStart
}: {
  label: string;
  disabled: boolean;
  hint?: string;
  onDragStart: (e: React.DragEvent<HTMLDivElement>) => void;
}) {
  return (
    <div
      draggable={!disabled}
      onDragStart={onDragStart}
      style={{
        padding: '6px 10px',
        background: disabled ? '#f8fafc' : '#ffffff',
        border: '1px solid #e2e8f0',
        borderRadius: 6,
        margin: '4px 0',
        fontSize: 13,
        color: '#1e293b',
        cursor: disabled ? 'not-allowed' : 'grab',
        opacity: disabled ? 0.5 : 1
      }}
      title={hint}
    >
      {label}
      {hint && (
        <div style={{ fontSize: 11, color: '#94a3b8', marginTop: 2 }}>
          {hint}
        </div>
      )}
    </div>
  );
}

// -------------------- Properties panel --------------------
function PropertiesPanel({
  node,
  editable,
  registry,
  satellites,
  onChange,
  onDelete
}: {
  node: Node<NodeDataPayload> | null;
  editable: boolean;
  registry: AlgorithmRegistryEntry[];
  satellites: SatelliteListItem[];
  onChange: (patch: Partial<NodeMeta>) => void;
  onDelete: () => void;
}) {
  const [paramOptions, setParamOptions] = useState<ParamCache[]>([]);
  const [referenceSat, setReferenceSat] = useState<{ tasookNo: string; satelliteNo: string } | null>(null);

  useEffect(() => {
    (async () => {
      if (!referenceSat) {
        setParamOptions([]);
        return;
      }
      const result = await assetsApi.listAllParams(referenceSat.tasookNo, referenceSat.satelliteNo);
      setParamOptions(result.items);
    })();
  }, [referenceSat?.tasookNo, referenceSat?.satelliteNo]);

  if (!node) {
    return (
      <div style={{ padding: 16 }}>
        <Title level={5}>节点属性配置</Title>
        <Text type="secondary">请在画布选中一个节点以编辑属性。</Text>
      </div>
    );
  }

  const meta = node.data.meta;
  const isSource = meta.category === 'source';
  const algo = meta.algorithmCode
    ? registry.find((e) => e.algorithmCode === meta.algorithmCode)
    : undefined;
  const paramsSchemaProps = parseSchemaProperties(meta.paramsSchema ?? algo?.paramsSchemaJson);

  return (
    <div style={{ padding: 16 }}>
      <Title level={5} style={{ marginBottom: 8 }}>
        节点属性配置
      </Title>
      <Form layout="vertical" disabled={!editable}>
        <Form.Item label="节点引用 nodeRef">
          <Input value={meta.nodeRef} disabled />
        </Form.Item>
        <Form.Item label="显示名称">
          <Input
            value={meta.displayName}
            onChange={(e) => onChange({ displayName: e.target.value })}
          />
        </Form.Item>
        {!isSource && algo && (
          <>
            <Form.Item label="算法编码 / 版本">
              <Input value={`${algo.algorithmCode} @ ${algo.version}`} disabled />
            </Form.Item>
            <Form.Item label="运行时">
              <Input value={meta.runtime ?? algo.runtime} disabled />
            </Form.Item>
          </>
        )}

        {isSource && (
          <>
            <Form.Item label="数据来源表 sourceTable">
              <Select
                value={meta.source!.sourceTable}
                onChange={(value: 'hq_param_point' | 'algo_result') =>
                  onChange({ source: { ...meta.source!, sourceTable: value } })
                }
                options={[
                  { value: 'hq_param_point', label: 'hq_param_point（高品质参数点）' },
                  { value: 'algo_result', label: 'algo_result（链式：上游算法结果）' }
                ]}
              />
            </Form.Item>
            <Form.Item label="取值字段 valueField">
              <Select
                value={meta.source!.valueField}
                onChange={(value: 'processed_value' | 'raw_value') =>
                  onChange({ source: { ...meta.source!, valueField: value } })
                }
                options={[
                  { value: 'processed_value', label: 'processed_value（默认）' },
                  { value: 'raw_value', label: 'raw_value' }
                ]}
              />
            </Form.Item>
            <Form.Item label="包含离群点 includeOutliers" valuePropName="checked">
              <Switch
                checked={meta.source!.includeOutliers}
                onChange={(checked) =>
                  onChange({ source: { ...meta.source!, includeOutliers: checked } })
                }
              />
            </Form.Item>
            <Form.Item
              label="参考卫星（仅用于参数候选，不存入模板）"
              extra="模板里不存任何卫星号，运行时由任务上下文注入"
            >
              <Select
                allowClear
                placeholder="选择参考卫星"
                onChange={(v) => {
                  if (!v) {
                    setReferenceSat(null);
                    return;
                  }
                  const [t, s] = v.split('||');
                  setReferenceSat({ tasookNo: t, satelliteNo: s });
                }}
                options={satellites.map((sat) => ({
                  value: `${sat.tasookNo}||${sat.satelliteNo}`,
                  label: `${sat.tasookNo} / ${sat.satelliteNo}`
                }))}
              />
            </Form.Item>
            <Form.Item label="参数列表">
              <Select
                mode="multiple"
                value={meta.source!.paramIds}
                onChange={(value) =>
                  onChange({ source: { ...meta.source!, paramIds: value as string[] } })
                }
                showSearch
                virtual
                listHeight={360}
                placeholder="从 param_cache 选择参数，可输入代号/描述过滤"
                filterOption={(input, option) => {
                  const q = input.trim().toLowerCase();
                  if (!q) return true;
                  const label = String(option?.label ?? '').toLowerCase();
                  const value = String(option?.value ?? '').toLowerCase();
                  return label.includes(q) || value.includes(q);
                }}
                options={paramOptions.map((p) => ({
                  value: paramCacheId(p),
                  label: formatParamCacheLabel(p)
                }))}
              />
            </Form.Item>
            <Form.Item label="输出引用变量名 outputName">
              <Input
                value={meta.source!.outputName}
                onChange={(e) =>
                  onChange({ source: { ...meta.source!, outputName: e.target.value } })
                }
              />
            </Form.Item>
          </>
        )}

        {!isSource && meta.algorithmCode === 'save_result' && (
          <>
            <Form.Item
              label="结果名称 metricName"
              extra="写入 algo_result.metric_name；留空则使用上游算法名称"
            >
              <Input
                value={(meta.paramsValues.metricName as string | undefined) ?? ''}
                onChange={(e) =>
                  onChange({ paramsValues: { ...meta.paramsValues, metricName: e.target.value } })
                }
                placeholder="如：电压均值、主频"
              />
            </Form.Item>
            <Form.Item label="写入明细 includeDetail" valuePropName="checked">
              <Switch
                checked={meta.paramsValues.includeDetail !== false}
                onChange={(checked) =>
                  onChange({ paramsValues: { ...meta.paramsValues, includeDetail: checked } })
                }
              />
            </Form.Item>
          </>
        )}

        {!isSource && meta.algorithmCode !== 'save_result' && (
          <>
            <Title level={5} style={{ fontSize: 13, marginTop: 12 }}>
              算法参数（按 paramsSchema 渲染）
            </Title>
            {paramsSchemaProps.length === 0 && (
              <Text type="secondary">该算法无可配置参数。</Text>
            )}
            {paramsSchemaProps.map((p) => (
              <Form.Item key={p.name} label={`${p.name}${p.title ? ` · ${p.title}` : ''}`}>
                {p.kind === 'boolean' ? (
                  <Switch
                    checked={
                      (meta.paramsValues[p.name] as boolean | undefined) ??
                      (typeof p.defaultValue === 'boolean' ? p.defaultValue : false)
                    }
                    onChange={(checked) =>
                      onChange({ paramsValues: { ...meta.paramsValues, [p.name]: checked } })
                    }
                  />
                ) : p.kind === 'enum' ? (
                  <Select
                    value={(meta.paramsValues[p.name] as string | undefined) ?? p.defaultValue ?? p.options?.[0]}
                    onChange={(value) =>
                      onChange({ paramsValues: { ...meta.paramsValues, [p.name]: value } })
                    }
                    options={(p.options ?? []).map((o) => ({ value: o, label: String(o) }))}
                  />
                ) : p.kind === 'integer' || p.kind === 'number' ? (
                  <InputNumber
                    style={{ width: '100%' }}
                    value={
                      (meta.paramsValues[p.name] as number | undefined) ??
                      (typeof p.defaultValue === 'number' ? p.defaultValue : undefined)
                    }
                    onChange={(value) =>
                      onChange({ paramsValues: { ...meta.paramsValues, [p.name]: value } })
                    }
                  />
                ) : (
                  <Input
                    value={(meta.paramsValues[p.name] as string | undefined) ?? ''}
                    onChange={(e) =>
                      onChange({ paramsValues: { ...meta.paramsValues, [p.name]: e.target.value } })
                    }
                  />
                )}
              </Form.Item>
            ))}
          </>
        )}
        {editable && (
          <Form.Item style={{ marginTop: 16 }}>
            <Popconfirm title="确认删除该节点及其连线？" onConfirm={onDelete}>
              <Button danger block>
                删除节点
              </Button>
            </Popconfirm>
          </Form.Item>
        )}
      </Form>
    </div>
  );
}

// -------------------- Trial run dialog --------------------
function TrialRunDialog({
  open,
  onClose,
  templateId,
  version,
  satellites
}: {
  open: boolean;
  onClose: () => void;
  templateId?: string;
  version?: number;
  satellites: SatelliteListItem[];
}) {
  const [tasookNo, setTasookNo] = useState<string | undefined>();
  const [satelliteNo, setSatelliteNo] = useState<string | undefined>();
  const [phases, setPhases] = useState<TestPhase[]>([]);
  const [testBatchId, setTestBatchId] = useState<string | undefined>();
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    (async () => {
      if (!tasookNo || !satelliteNo) {
        setPhases([]);
        return;
      }
      const list = await assetsApi.listTestPhases(tasookNo, satelliteNo);
      setPhases(list);
    })();
  }, [tasookNo, satelliteNo]);

  const onSubmit = async () => {
    if (!templateId || !version) return;
    if (!tasookNo || !satelliteNo) {
      message.error('请选择卫星');
      return;
    }
    setSubmitting(true);
    try {
      const resp = await algoTemplatesApi.trialRun(templateId, version, {
        tasookNo,
        satelliteNo,
        testBatchId: testBatchId ?? null
      });
      message.success(`测试运行已提交：${resp.runId}`);
      onClose();
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Drawer
      title="测试运行（创建临时 ALGORITHM 任务）"
      open={open}
      onClose={onClose}
      width={420}
      destroyOnClose
      footer={
        <Space style={{ float: 'right' }}>
          <Button onClick={onClose}>取消</Button>
          <Button type="primary" loading={submitting} onClick={onSubmit}>
            提交测试运行
          </Button>
        </Space>
      }
    >
      <Alert
        type="info"
        showIcon
        style={{ marginBottom: 12 }}
        message="测试运行的本质是创建一个 trigger_type='TRIAL' 的临时算法任务，走全套执行链路（执行时才查 ClickHouse），模板状态保持原状。"
      />
      <Form layout="vertical">
        <Form.Item label="发射任务 tasook_no" required>
          <Select
            value={tasookNo}
            onChange={(v) => {
              setTasookNo(v);
              setSatelliteNo(undefined);
            }}
            placeholder="选择 tasook_no"
            options={Array.from(new Set(satellites.map((s) => s.tasookNo))).map((v) => ({
              value: v,
              label: v
            }))}
          />
        </Form.Item>
        <Form.Item label="卫星编号 satellite_no" required>
          <Select
            value={satelliteNo}
            onChange={setSatelliteNo}
            placeholder="选择 satellite_no"
            options={satellites
              .filter((s) => s.tasookNo === tasookNo)
              .map((s) => ({ value: s.satelliteNo, label: `${s.satelliteNo}（${s.satelliteName}）` }))}
          />
        </Form.Item>
        <Form.Item label="测试阶段">
          <Select
            value={testBatchId}
            allowClear
            onChange={setTestBatchId}
            placeholder="可选；不填将走任务自身窗口逻辑"
            options={phases.map((p) => ({
              value: p.testBatchName,
              label: p.testBatchName
            }))}
          />
        </Form.Item>
      </Form>
    </Drawer>
  );
}

// -------------------- Helpers --------------------
function lowerCategory(cat: AlgorithmCategory): NodeMeta['category'] {
  switch (cat) {
    case 'Source':
      return 'source';
    case 'Stats':
      return 'stats';
    case 'Spectrum':
      return 'spectrum';
    case 'Align':
      return 'align';
    case 'Cluster':
      return 'cluster';
    case 'Compare':
      return 'compare';
    case 'Output':
      return 'output';
  }
}

function runtimeToWire(runtime: AlgorithmRegistryEntry['runtime']): NodeMeta['runtime'] {
  if (runtime === 'Builtin') return 'BUILTIN';
  if (runtime === 'Python') return 'PYTHON';
  return 'JS';
}

function categoryEmoji(cat: AlgorithmCategory): string {
  switch (cat) {
    case 'Source':
      return '📥';
    case 'Stats':
      return '📈';
    case 'Spectrum':
      return '📊';
    case 'Align':
      return '〰️';
    case 'Cluster':
      return '🔵';
    case 'Compare':
      return '🎯';
    case 'Output':
      return '📝';
  }
}

interface SchemaPropertyEntry {
  name: string;
  title?: string;
  kind: 'string' | 'integer' | 'number' | 'enum' | 'boolean';
  defaultValue?: unknown;
  options?: unknown[];
}

function parseSchemaProperties(schema: unknown): SchemaPropertyEntry[] {
  if (!schema || typeof schema !== 'object') return [];
  const obj = schema as Record<string, unknown>;
  const properties = obj.properties as Record<string, unknown> | undefined;
  if (!properties || typeof properties !== 'object') return [];
  const entries: SchemaPropertyEntry[] = [];
  for (const [name, raw] of Object.entries(properties)) {
    if (!raw || typeof raw !== 'object') continue;
    const propObj = raw as Record<string, unknown>;
    const enumValues = propObj.enum as unknown[] | undefined;
    let kind: SchemaPropertyEntry['kind'] = 'string';
    if (enumValues && Array.isArray(enumValues)) {
      kind = 'enum';
    } else if (propObj.type === 'boolean') {
      kind = 'boolean';
    } else if (propObj.type === 'integer') {
      kind = 'integer';
    } else if (propObj.type === 'number') {
      kind = 'number';
    } else {
      kind = 'string';
    }
    entries.push({
      name,
      title: typeof propObj.title === 'string' ? (propObj.title as string) : undefined,
      kind,
      defaultValue: propObj.default,
      options: enumValues
    });
  }
  return entries;
}

function extractDefaults(schema: unknown): Record<string, unknown> {
  const props = parseSchemaProperties(schema);
  const out: Record<string, unknown> = {};
  for (const p of props) {
    if (p.defaultValue !== undefined) {
      out[p.name] = p.defaultValue;
    } else if (p.kind === 'boolean') {
      out[p.name] = false;
    } else if (p.kind === 'enum' && p.options && p.options.length > 0) {
      out[p.name] = p.options[0];
    }
  }
  return out;
}

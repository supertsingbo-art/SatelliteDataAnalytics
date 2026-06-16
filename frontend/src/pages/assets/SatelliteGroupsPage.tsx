import { useEffect, useMemo, useState } from 'react';
import {
  Button,
  Card,
  Col,
  Empty,
  Form,
  Input,
  InputNumber,
  Modal,
  Popconfirm,
  Row,
  Space,
  Switch,
  Table,
  Tag,
  Tree,
  TreeSelect,
  Typography,
  message
} from 'antd';
import { ReloadOutlined, PlusOutlined, EditOutlined, DeleteOutlined } from '@ant-design/icons';
import dayjs from 'dayjs';
import { groupsApi, CreateSatelliteGroupRequest } from '@/api/groups';
import { assetsApi } from '@/api/assets';
import { SatelliteGroupMemberDto, SatelliteGroupNode, SatelliteListItem } from '@/api/types';

const { Text } = Typography;

interface AntTreeNode {
  key: string;
  title: React.ReactNode;
  children?: AntTreeNode[];
  raw: SatelliteGroupNode;
}

function buildParentTreeOptions(
  nodes: SatelliteGroupNode[],
  disabledId?: string
): { value: string; title: string; disabled?: boolean; children?: ReturnType<typeof buildParentTreeOptions> }[] {
  return nodes.map((node) => ({
    value: node.groupId,
    title: node.groupName,
    disabled: node.groupId === disabledId,
    children: node.children.length > 0 ? buildParentTreeOptions(node.children, disabledId) : undefined
  }));
}

function toTreeNodes(nodes: SatelliteGroupNode[]): AntTreeNode[] {
  return nodes.map((node) => ({
    key: node.groupId,
    title: (
      <Space>
        <span>{node.groupName}</span>
        <Tag>{node.directMemberCount} 直属</Tag>
        <Tag color="blue">{node.descendantMemberCount} 含后代</Tag>
      </Space>
    ),
    raw: node,
    children: node.children.length > 0 ? toTreeNodes(node.children) : undefined
  }));
}

function collectTreeKeys(nodes: AntTreeNode[]): string[] {
  const keys: string[] = [];
  for (const node of nodes) {
    keys.push(node.key);
    if (node.children?.length) {
      keys.push(...collectTreeKeys(node.children));
    }
  }
  return keys;
}

export function SatelliteGroupsPage() {
  const [tree, setTree] = useState<SatelliteGroupNode[]>([]);
  const [expandedKeys, setExpandedKeys] = useState<string[]>([]);
  const [loading, setLoading] = useState(false);
  const [selected, setSelected] = useState<SatelliteGroupNode | null>(null);
  const [members, setMembers] = useState<SatelliteGroupMemberDto[]>([]);
  const [includeDescendants, setIncludeDescendants] = useState(false);

  const [editorOpen, setEditorOpen] = useState(false);
  const [editorMode, setEditorMode] = useState<'create' | 'edit'>('create');
  const [editingGroupId, setEditingGroupId] = useState<string | null>(null);
  const [parentLocked, setParentLocked] = useState(false);
  const [form] = Form.useForm<CreateSatelliteGroupRequest & { sortOrder: number }>();

  const [memberModalOpen, setMemberModalOpen] = useState(false);
  const [allSatellites, setAllSatellites] = useState<SatelliteListItem[]>([]);
  const [pickedSatellites, setPickedSatellites] = useState<string[]>([]);

  const reload = async () => {
    setLoading(true);
    try {
      const list = await groupsApi.getTree();
      setTree(list);
      if (selected) {
        const found = findGroup(list, selected.groupId);
        setSelected(found ?? null);
      }
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    reload();
  }, []);

  useEffect(() => {
    (async () => {
      if (!selected) {
        setMembers([]);
        return;
      }
      const list = await groupsApi.listMembers(selected.groupId, includeDescendants);
      setMembers(list);
    })();
  }, [selected?.groupId, includeDescendants]);

  const treeNodes = useMemo(() => toTreeNodes(tree), [tree]);

  useEffect(() => {
    if (treeNodes.length > 0) {
      setExpandedKeys(collectTreeKeys(treeNodes));
    } else {
      setExpandedKeys([]);
    }
  }, [treeNodes]);

  const parentTreeOptions = useMemo(
    () => buildParentTreeOptions(tree, editingGroupId ?? undefined),
    [tree, editingGroupId]
  );

  const openCreate = (parent?: SatelliteGroupNode) => {
    setEditorMode('create');
    setEditingGroupId(null);
    setParentLocked(Boolean(parent));
    form.resetFields();
    form.setFieldsValue({
      parentGroupId: parent?.groupId ?? null,
      sortOrder: 0,
      groupName: '',
      description: undefined
    });
    setEditorOpen(true);
  };

  const openEdit = (node: SatelliteGroupNode) => {
    setEditorMode('edit');
    setEditingGroupId(node.groupId);
    setParentLocked(node.parentGroupId === null);
    form.setFieldsValue({
      parentGroupId: node.parentGroupId,
      groupName: node.groupName,
      sortOrder: node.sortOrder,
      description: node.description ?? undefined
    });
    setEditorOpen(true);
  };

  const submit = async () => {
    const values = await form.validateFields();
    const rawParent = values.parentGroupId;
    const parentGroupId =
      typeof rawParent === 'string' && rawParent.trim() ? rawParent.trim() : null;
    const payload = { ...values, parentGroupId };
    if (editorMode === 'create') {
      await groupsApi.create(payload);
      message.success('已创建分组');
    } else if (selected) {
      await groupsApi.update(selected.groupId, {
        parentGroupId,
        groupName: values.groupName,
        sortOrder: values.sortOrder,
        description: values.description ?? null
      });
      message.success('已更新分组');
    }
    setEditorOpen(false);
    reload();
  };

  const remove = async (node: SatelliteGroupNode) => {
    await groupsApi.remove(node.groupId);
    message.success(`已删除分组 ${node.groupName}`);
    setSelected(null);
    reload();
  };

  const openAddMembers = async () => {
    if (!selected) return;
    const result = await assetsApi.listSatellites({ pageNo: 1, pageSize: 500, enabledOnly: true });
    setAllSatellites(result.items);
    setPickedSatellites([]);
    setMemberModalOpen(true);
  };

  const submitAddMembers = async () => {
    if (!selected) return;
    const sats = pickedSatellites.map((key) => {
      const [tasookNo, satelliteNo] = key.split('||');
      return { tasookNo, satelliteNo };
    });
    await groupsApi.addMembers(selected.groupId, sats);
    message.success(`已加入 ${sats.length} 颗卫星`);
    setMemberModalOpen(false);
    const list = await groupsApi.listMembers(selected.groupId, includeDescendants);
    setMembers(list);
  };

  const removeMember = async (record: SatelliteGroupMemberDto) => {
    if (!selected) return;
    await groupsApi.removeMember(selected.groupId, record.tasookNo, record.satelliteNo);
    message.success('已移出该卫星');
    const list = await groupsApi.listMembers(selected.groupId, includeDescendants);
    setMembers(list);
  };

  return (
    <Row gutter={16}>
      <Col span={9}>
        <Card
          title="卫星分组树"
          extra={
            <Space>
              <Button icon={<ReloadOutlined />} onClick={reload}>
                刷新
              </Button>
              <Button type="primary" icon={<PlusOutlined />} onClick={() => openCreate()}>
                新建分组
              </Button>
            </Space>
          }
        >
          <Text type="secondary" style={{ display: 'block', marginBottom: 12 }}>
            分组采用物化路径。模板对该分组及其所有后代分组下的卫星可用；一颗卫星只能归属一个分组。
          </Text>
          {treeNodes.length === 0 && !loading && <Empty />}
          <Tree
            treeData={treeNodes}
            blockNode
            expandedKeys={expandedKeys}
            onExpand={(keys) => setExpandedKeys(keys.map(String))}
            onSelect={(_keys, info) => {
              const node = info.node as unknown as AntTreeNode;
              setSelected(node?.raw ?? null);
            }}
            selectedKeys={selected ? [selected.groupId] : []}
          />
        </Card>
      </Col>
      <Col span={15}>
        {selected ? (
          <Card
            title={`分组明细：${selected.groupName}`}
            extra={
              <Space>
                <Button icon={<EditOutlined />} onClick={() => openEdit(selected)}>
                  编辑
                </Button>
                <Popconfirm title="确认删除该分组？" onConfirm={() => remove(selected)}>
                  <Button danger icon={<DeleteOutlined />}>
                    删除
                  </Button>
                </Popconfirm>
              </Space>
            }
          >
            <p>
              <Text type="secondary">groupId：</Text>
              <Text code>{selected.groupId}</Text>
            </p>
            <p>
              <Text type="secondary">物化路径 group_path：</Text>
              <Text code>{selected.groupPath}</Text>
            </p>
            <p>
              <Text type="secondary">排序 sort_order：</Text>
              <Text>{selected.sortOrder}</Text>
              <Text type="secondary" style={{ marginLeft: 16 }}>
                创建时间：{dayjs(selected.createdAt).format('YYYY-MM-DD HH:mm:ss')}
              </Text>
            </p>
            {selected.description && <p>{selected.description}</p>}

            <div style={{ display: 'flex', justifyContent: 'space-between', marginTop: 12, marginBottom: 8 }}>
              <Space>
                <Text strong>卫星成员</Text>
                <Switch
                  checkedChildren="含后代"
                  unCheckedChildren="仅本级"
                  checked={includeDescendants}
                  onChange={setIncludeDescendants}
                />
              </Space>
              <Space>
                <Button icon={<PlusOutlined />} onClick={() => openCreate(selected)}>
                  添加子分组
                </Button>
                <Button type="primary" icon={<PlusOutlined />} onClick={openAddMembers}>
                  加入卫星
                </Button>
              </Space>
            </div>
            <Table<SatelliteGroupMemberDto>
              size="small"
              rowKey={(record) => `${record.tasookNo}_${record.satelliteNo}`}
              dataSource={members}
              pagination={{ pageSize: 10 }}
              columns={[
                { title: '型号代号', dataIndex: 'tasookNo' },
                { title: '卫星代号', dataIndex: 'satelliteNo' },
                { title: '所属分组路径', dataIndex: 'groupPath' },
                {
                  title: '操作',
                  width: 100,
                  render: (_, record) => (
                    <Popconfirm title="移出该卫星？" onConfirm={() => removeMember(record)}>
                      <Button size="small" type="link" danger>
                        移出
                      </Button>
                    </Popconfirm>
                  )
                }
              ]}
            />
          </Card>
        ) : (
          <Card>
            <Empty description="请选择左侧任意分组以查看明细" />
          </Card>
        )}
      </Col>

      <Modal
        open={editorOpen}
        title={editorMode === 'create' ? '新建分组' : '编辑分组'}
        onCancel={() => setEditorOpen(false)}
        onOk={submit}
        okText="保存"
        cancelText="取消"
        destroyOnClose
      >
        <Form form={form} layout="vertical">
          <Form.Item
            label="父分组"
            name="parentGroupId"
            tooltip="留空表示挂在默认根分组下；根分组本身无父级"
          >
            <TreeSelect
              allowClear={!parentLocked}
              disabled={parentLocked}
              placeholder="默认根分组"
              treeData={parentTreeOptions}
              treeDefaultExpandAll
              showSearch
              treeNodeFilterProp="title"
            />
          </Form.Item>
          <Form.Item label="分组名" name="groupName" rules={[{ required: true, max: 256 }]}>
            <Input placeholder="例如：平台型号 A" />
          </Form.Item>
          <Form.Item label="同级排序" name="sortOrder" initialValue={0}>
            <InputNumber style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item label="描述" name="description">
            <Input.TextArea rows={3} />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        open={memberModalOpen}
        title="加入卫星到当前分组"
        onCancel={() => setMemberModalOpen(false)}
        onOk={submitAddMembers}
        okText="加入"
        cancelText="取消"
        width={760}
        destroyOnClose
      >
        <Text type="secondary">每颗卫星只能归属一个分组；选中后会从原分组迁移到当前分组。</Text>
        <Table<SatelliteListItem>
          rowKey={(record) => `${record.tasookNo}||${record.satelliteNo}`}
          dataSource={allSatellites}
          pagination={{ pageSize: 8 }}
          rowSelection={{
            selectedRowKeys: pickedSatellites,
            onChange: (keys) => setPickedSatellites(keys.map(String))
          }}
          columns={[
            { title: '型号', dataIndex: 'tasookNo' },
            { title: '卫星', dataIndex: 'satelliteNo' },
            { title: '名称', dataIndex: 'satelliteName' },
            { title: '版本号', dataIndex: 'dbStage' }
          ]}
        />
      </Modal>
    </Row>
  );
}

function findGroup(nodes: SatelliteGroupNode[], groupId: string): SatelliteGroupNode | null {
  for (const node of nodes) {
    if (node.groupId === groupId) return node;
    const child = findGroup(node.children, groupId);
    if (child) return child;
  }
  return null;
}

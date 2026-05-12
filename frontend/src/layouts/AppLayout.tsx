import { Layout, Menu } from 'antd';
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom';
import { useMemo } from 'react';

const { Sider, Header, Content } = Layout;

const menuItems = [
  {
    key: '/assets',
    label: '资产配置中心',
    children: [
      { key: '/assets/sources', label: '数据源配置' },
      { key: '/assets/satellites', label: '卫星资产缓存' },
      { key: '/assets/groups', label: '卫星分组管理' }
    ]
  },
  {
    key: '/templates',
    label: '模板治理',
    children: [
      { key: '/templates/filters', label: '筛选模板' },
      { key: '/templates/algorithms', label: '算法模板' }
    ]
  },
  {
    key: '/algorithms',
    label: '算法仓库',
    children: [
      { key: '/algorithms/registry', label: '内置算法注册表' },
      { key: '/algorithms/packages', label: '算法包列表' }
    ]
  },
  {
    key: 'group-tasks',
    label: '任务编排',
    children: [
      { key: '/tasks', label: '任务列表' },
      { key: '/tasks/pipeline', label: '新建 PIPELINE' },
      { key: '/tasks/preprocess', label: '仅预处理入仓' }
    ]
  }
];

export function AppLayout() {
  const location = useLocation();
  const navigate = useNavigate();

  const selectedKeys = useMemo(() => {
    return [location.pathname];
  }, [location.pathname]);

  const openKeys = useMemo(() => {
    return menuItems.map((item) => item.key);
  }, []);

  const headerTitle = useMemo(() => {
    const path = location.pathname;
    if (path.startsWith('/assets/sources')) return '资产配置中心 / 数据源配置';
    if (path.startsWith('/assets/satellites')) return '资产配置中心 / 卫星资产缓存';
    if (path.startsWith('/assets/groups')) return '资产配置中心 / 卫星分组管理';
    if (path.startsWith('/templates/filters')) return '模板治理 / 筛选模板';
    if (path.startsWith('/templates/algorithms')) return '模板治理 / 算法模板';
    if (path.startsWith('/algorithms/registry')) return '算法仓库 / 内置算法注册表';
    if (path.startsWith('/algorithms/packages')) return '算法仓库 / 算法包列表';
    if (path === '/tasks') return '任务编排 / 任务列表';
    if (path.startsWith('/tasks/pipeline')) return '任务编排 / 新建 PIPELINE';
    if (path.startsWith('/tasks/preprocess')) return '任务编排 / 仅预处理入仓';
    return '卫星测试数据预处理与数据分析平台';
  }, [location.pathname]);

  return (
    <Layout style={{ minHeight: '100vh' }}>
      <Sider width={240} theme="dark">
        <div className="sidebar-title" style={{ color: '#fff' }}>
          卫星测试数据平台
        </div>
        <Menu
          theme="dark"
          mode="inline"
          selectedKeys={selectedKeys}
          defaultOpenKeys={openKeys}
          items={menuItems.map((item) => ({
            key: item.key,
            label: item.label,
            children: item.children?.map((child) => ({
              key: child.key,
              label: <NavLink to={child.key}>{child.label}</NavLink>
            }))
          }))}
          onClick={({ key }) => {
            // 顶层菜单点击不导航；二级菜单已用 NavLink
            if (!menuItems.some((m) => m.key === key)) {
              navigate(key);
            }
          }}
        />
      </Sider>
      <Layout className="app-main">
        <Header className="app-header" style={{ background: '#fff', padding: '0 20px' }}>
          <span>{headerTitle}</span>
        </Header>
        <Content className="app-content">
          <Outlet />
        </Content>
      </Layout>
    </Layout>
  );
}

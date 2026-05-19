import { Button, Card, Collapse, Form, Space, Typography, message } from 'antd';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { tasksApi } from '@/api/tasks';
import {
  CUSTOM_PHASE,
  CUSTOM_TIME_DISPLAY_NAME,
  PreprocessFormFields,
  parseFilterTemplateKey,
  timeRangeToWindowIso
} from '@/pages/tasks/components/PreprocessFormFields';
import { TaskDetailCard } from '@/pages/tasks/components/TaskDetailCard';
import { useTaskRunDetail } from '@/pages/tasks/hooks/useTaskRunDetail';

const { Paragraph } = Typography;

const runIdPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function PreprocessTasksPage() {
  const [form] = Form.useForm();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const urlRunId = searchParams.get('runId');
  const viewRunId = urlRunId && runIdPattern.test(urlRunId) ? urlRunId : null;
  const { detail, loading, setPolling, refresh } = useTaskRunDetail(viewRunId);

  return (
    <Card
      title={viewRunId ? '预处理入仓任务详情（PREPROCESS）' : '新建预处理入仓（PREPROCESS）'}
      extra={
        <Link to="/tasks">
          <Button type="link">返回任务列表</Button>
        </Link>
      }
    >
      <Paragraph type="secondary">
        调用 <code>POST /api/v1/tasks/preprocess</code>：Mongo 拉取、筛选、离群打标与 ClickHouse 入仓，不执行算法 DAG。
        从任务列表「详情」进入时可查看完整任务信息并自动刷新进度。
      </Paragraph>

      {viewRunId && <TaskDetailCard detail={detail} loading={loading} />}

      <Collapse
        defaultActiveKey={viewRunId ? [] : ['create']}
        items={[
          {
            key: 'create',
            label: viewRunId ? '新建任务（展开填写）' : '新建任务',
            children: (
              <Form
                form={form}
                layout="vertical"
                onFinish={async (v) => {
                  try {
                    const { filterTemplateId, filterTemplateVersion } = parseFilterTemplateKey(
                      v.filterTemplateKey
                    );
                    if (!filterTemplateId || filterTemplateVersion == null) {
                      message.warning('请选择筛选模板');
                      return;
                    }
                    const { windowStart, windowEnd } = timeRangeToWindowIso(v.timeRange);
                    if (!windowStart || !windowEnd) {
                      message.warning('请选择开始与结束日期');
                      return;
                    }
                    const phasePick = v.phasePick as string;
                    const testBatchName =
                      phasePick === CUSTOM_PHASE ? CUSTOM_TIME_DISPLAY_NAME : phasePick;
                    const res = await tasksApi.createPreprocess({
                      tasookNo: v.tasookNo,
                      satelliteNo: v.satelliteNo,
                      testBatchName,
                      windowStart,
                      windowEnd,
                      filterTemplateId,
                      filterTemplateVersion
                    });
                    message.success(`已创建 PREPROCESS 任务 ${res.runId}`);
                    setPolling(true);
                    setSearchParams({ runId: res.runId });
                    navigate(`/tasks/preprocess?runId=${encodeURIComponent(res.runId)}`, {
                      replace: true
                    });
                  } catch {
                    /* axios 已提示 */
                  }
                }}
              >
                <PreprocessFormFields form={form} />
                <Form.Item>
                  <Space>
                    <Button type="primary" htmlType="submit">
                      创建预处理入仓任务
                    </Button>
                    {viewRunId && (
                      <Button onClick={() => void refresh()} loading={loading}>
                        刷新状态
                      </Button>
                    )}
                  </Space>
                </Form.Item>
              </Form>
            )
          }
        ]}
      />
    </Card>
  );
}

import { Button, Card, Collapse, Form, Input, Typography, message } from 'antd';
import { Link, useSearchParams } from 'react-router-dom';
import dayjs from 'dayjs';
import { tasksApi } from '@/api/tasks';
import { TaskWindowFields } from '@/pages/tasks/components/TaskWindowFields';
import { TaskDetailCard } from '@/pages/tasks/components/TaskDetailCard';
import { useTaskRunDetail } from '@/pages/tasks/hooks/useTaskRunDetail';

const { Paragraph } = Typography;

const runIdPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function PipelineTasksPage() {
  const [form] = Form.useForm();
  const [searchParams, setSearchParams] = useSearchParams();
  const urlRunId = searchParams.get('runId');
  const viewRunId = urlRunId && runIdPattern.test(urlRunId) ? urlRunId : null;
  const { detail, loading, setPolling, refresh } = useTaskRunDetail(viewRunId);

  return (
    <Card
      title={viewRunId ? 'PIPELINE 任务详情' : '任务编排（PIPELINE）'}
      extra={
        <Link to="/tasks">
          <Button type="link">返回任务列表</Button>
        </Link>
      }
    >
      <Paragraph type="secondary">
        调用内部 <code>/api/v1/tasks/pipeline</code> 创建任务；默认使用开发种子筛选/算法模板。需后端 Hangfire 与
        ClickHouse 可用。从任务列表「详情」进入可查看完整任务信息并自动刷新进度。
      </Paragraph>

      {viewRunId && (
        <>
          <TaskDetailCard detail={detail} loading={loading} />
          <div style={{ marginTop: 8 }}>
            <Button onClick={() => void refresh()} loading={loading}>
              刷新状态
            </Button>
          </div>
        </>
      )}

      {!viewRunId && (
        <Collapse
          defaultActiveKey={['create']}
          items={[
            {
              key: 'create',
              label: '新建任务',
              children: (
                <Form
                  form={form}
                  layout="vertical"
                  initialValues={{
                    tasookNo: 'TASK-A100',
                    satelliteNo: 'SAT-001'
                  }}
                  onFinish={async (v) => {
                    try {
                      const res = await tasksApi.createPipeline({
                        tasookNo: v.tasookNo,
                        satelliteNo: v.satelliteNo,
                        testBatchName: v.testBatchName || null,
                        windowStart: v.timeRange?.[0] ? dayjs(v.timeRange[0]).toISOString() : null,
                        windowEnd: v.timeRange?.[1] ? dayjs(v.timeRange[1]).toISOString() : null
                      });
                      if (!res.runId) return;
                      message.success(`已创建任务 ${res.runId}`);
                      setPolling(true);
                      setSearchParams({ runId: res.runId });
                    } catch {
                      /* axios 已提示 */
                    }
                  }}
                >
                  <Form.Item name="tasookNo" label="型号代号 tasook_no" rules={[{ required: true }]}>
                    <Input />
                  </Form.Item>
                  <Form.Item name="satelliteNo" label="卫星代号 satellite_no" rules={[{ required: true }]}>
                    <Input />
                  </Form.Item>
                  <TaskWindowFields form={form} />
                  <Form.Item>
                    <Button type="primary" htmlType="submit">
                      创建 PIPELINE
                    </Button>
                  </Form.Item>
                </Form>
              )
            }
          ]}
        />
      )}
    </Card>
  );
}

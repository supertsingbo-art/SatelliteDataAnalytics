import { Button, Card, Form, Input, InputNumber, Typography, message } from 'antd';
import { Link, useNavigate } from 'react-router-dom';
import dayjs from 'dayjs';
import { tasksApi } from '@/api/tasks';
import { TaskWindowFields } from '@/pages/tasks/components/TaskWindowFields';

const { Paragraph } = Typography;

export function PreprocessTasksPage() {
  const [form] = Form.useForm();
  const navigate = useNavigate();

  return (
    <Card
      title="仅预处理入仓（PREPROCESS）"
      extra={
        <Link to="/tasks">
          <Button type="link">返回任务列表</Button>
        </Link>
      }
    >
      <Paragraph type="secondary">
        调用内部 <code>/api/v1/tasks/preprocess</code>：执行 Mongo 拉取、筛选、离群打标与 ClickHouse 入仓，不进入算法
        DAG。默认筛选模板与 PIPELINE 一致；创建后将跳转到「新建 PIPELINE」页复用状态轮询（仅查看进度，任务类型为
        PREPROCESS）。
      </Paragraph>
      <Form
        form={form}
        layout="vertical"
        initialValues={{
          tasookNo: 'TASK-A100',
          satelliteNo: 'SAT-001'
        }}
        onFinish={async (v) => {
          try {
            const res = await tasksApi.createPreprocess({
              tasookNo: v.tasookNo,
              satelliteNo: v.satelliteNo,
              testBatchId: v.testBatchId || null,
              windowStart: v.timeRange?.[0] ? dayjs(v.timeRange[0]).toISOString() : null,
              windowEnd: v.timeRange?.[1] ? dayjs(v.timeRange[1]).toISOString() : null,
              filterTemplateId: v.filterTemplateId || null,
              filterTemplateVersion: v.filterTemplateVersion ?? null
            });
            message.success(`已创建 PREPROCESS 任务 ${res.runId}`);
            navigate(`/tasks/pipeline?runId=${encodeURIComponent(res.runId)}`);
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
        <Form.Item name="filterTemplateId" label="筛选模板 ID（可选）">
          <Input placeholder="留空则使用服务端默认开发模板" />
        </Form.Item>
        <Form.Item name="filterTemplateVersion" label="筛选模板版本（可选）">
          <InputNumber min={1} style={{ width: '100%' }} placeholder="与模板 ID 同时填写时生效" />
        </Form.Item>
        <Form.Item>
          <Button type="primary" htmlType="submit">
            创建 PREPROCESS
          </Button>
        </Form.Item>
      </Form>
    </Card>
  );
}

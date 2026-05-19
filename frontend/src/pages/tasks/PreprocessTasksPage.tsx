import { useEffect, useState } from 'react';
import { Button, Card, Form, Input, InputNumber, Progress, Space, Typography, message } from 'antd';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import dayjs from 'dayjs';
import { tasksApi } from '@/api/tasks';
import { TaskWindowFields } from '@/pages/tasks/components/TaskWindowFields';

const { Paragraph } = Typography;

const runIdPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function PreprocessTasksPage() {
  const [form] = Form.useForm();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const [runId, setRunId] = useState<string | null>(null);
  const [status, setStatus] = useState<Awaited<ReturnType<typeof tasksApi.get>> | null>(null);
  const [polling, setPolling] = useState(false);

  const urlRunId = searchParams.get('runId');
  useEffect(() => {
    if (urlRunId && runIdPattern.test(urlRunId)) {
      setRunId(urlRunId);
      setPolling(true);
      setStatus(null);
    }
  }, [urlRunId]);

  useEffect(() => {
    if (!runId || !polling) {
      return;
    }
    const id = window.setInterval(async () => {
      try {
        const s = await tasksApi.get(runId);
        setStatus(s);
        if (['Succeeded', 'Failed', 'Timeout', 'Cancelled'].includes(s.status)) {
          setPolling(false);
        }
      } catch {
        setPolling(false);
      }
    }, 2000);
    return () => window.clearInterval(id);
  }, [runId, polling]);

  return (
    <Card
      title="新建预处理入仓（PREPROCESS）"
      extra={
        <Link to="/tasks">
          <Button type="link">返回任务列表</Button>
        </Link>
      }
    >
      <Paragraph type="secondary">
        调用 <code>POST /api/v1/tasks/preprocess</code>：Mongo 拉取、筛选、离群打标与 ClickHouse 入仓，不执行算法 DAG。
        从任务列表「详情」进入时可带 <code>?runId=</code> 自动轮询状态。
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
            setRunId(res.runId);
            setPolling(true);
            setStatus(null);
            setSearchParams({ runId: res.runId });
            navigate(`/tasks/preprocess?runId=${encodeURIComponent(res.runId)}`, { replace: true });
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
            创建预处理入仓任务
          </Button>
        </Form.Item>
      </Form>

      {status && (
        <Card size="small" title="任务状态" style={{ marginTop: 16 }}>
          <Space direction="vertical" style={{ width: '100%' }}>
            <div>
              <strong>run_id</strong> {status.run_id}
            </div>
            <div>
              <strong>status</strong> {status.status}
            </div>
            <div>
              <strong>step</strong> {status.current_step ?? '—'}
            </div>
            <Progress
              percent={Number(status.progress_percent)}
              status={status.status === 'Failed' ? 'exception' : 'active'}
            />
            {status.error_code && (
              <Typography.Text type="danger">
                {status.error_code}: {status.error_msg}
              </Typography.Text>
            )}
          </Space>
        </Card>
      )}
    </Card>
  );
}

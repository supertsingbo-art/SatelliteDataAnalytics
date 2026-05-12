import { useEffect, useState } from 'react';
import { Button, Card, Form, Input, Progress, Space, Typography, message } from 'antd';
import { Link, useSearchParams } from 'react-router-dom';
import dayjs from 'dayjs';
import { tasksApi } from '@/api/tasks';
import { TaskWindowFields } from '@/pages/tasks/components/TaskWindowFields';

const { Paragraph } = Typography;

const runIdPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function PipelineTasksPage() {
  const [form] = Form.useForm();
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
    if (!runId || !polling) return;
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
      title="任务编排（PIPELINE）"
      extra={
        <Link to="/tasks">
          <Button type="link">返回任务列表</Button>
        </Link>
      }
    >
      <Paragraph type="secondary">
        调用内部 <code>/api/v1/tasks/pipeline</code> 创建任务；默认使用开发种子筛选/算法模板。需后端 Hangfire 与
        ClickHouse 可用；Mongo 无数据时将按配置写入合成点。从任务列表进入时可带 <code>?runId=</code> 自动轮询状态。
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
            const res = await tasksApi.createPipeline({
              tasookNo: v.tasookNo,
              satelliteNo: v.satelliteNo,
              testBatchId: v.testBatchId || null,
              windowStart: v.timeRange?.[0] ? dayjs(v.timeRange[0]).toISOString() : null,
              windowEnd: v.timeRange?.[1] ? dayjs(v.timeRange[1]).toISOString() : null
            });
            message.success(`已创建任务 ${res.runId}`);
            setRunId(res.runId);
            setPolling(true);
            setStatus(null);
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
            <Progress percent={Number(status.progress_percent)} status={status.status === 'Failed' ? 'exception' : 'active'} />
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

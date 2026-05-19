import { useEffect, useState } from 'react';
import { Button, Card, Form, Progress, Space, Typography, message } from 'antd';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { tasksApi } from '@/api/tasks';
import {
  CUSTOM_PHASE,
  PreprocessFormFields,
  parseFilterTemplateKey,
  timeRangeToWindowIso
} from '@/pages/tasks/components/PreprocessFormFields';

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
        onFinish={async (v) => {
          try {
            const { filterTemplateId, filterTemplateVersion } = parseFilterTemplateKey(v.filterTemplateKey);
            if (!filterTemplateId || filterTemplateVersion == null) {
              message.warning('请选择筛选模板');
              return;
            }
            const { windowStart, windowEnd } = timeRangeToWindowIso(v.timeRange);
            if (!windowStart || !windowEnd) {
              message.warning('请选择开始与结束日期');
              return;
            }
            const phasePick = v.phasePick as string | undefined;
            const testBatchId = phasePick && phasePick !== CUSTOM_PHASE ? phasePick : null;
            const res = await tasksApi.createPreprocess({
              tasookNo: v.tasookNo,
              satelliteNo: v.satelliteNo,
              testBatchId,
              windowStart,
              windowEnd,
              filterTemplateId,
              filterTemplateVersion
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
        <PreprocessFormFields form={form} />
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

import { Button, Card, Collapse, Form, Typography, message } from 'antd';
import { Link, useSearchParams } from 'react-router-dom';
import { tasksApi } from '@/api/tasks';
import { TaskDetailCard } from '@/pages/tasks/components/TaskDetailCard';
import {
  PipelineFormFields,
  parseAlgorithmTemplateKey
} from '@/pages/tasks/components/PipelineFormFields';
import {
  CUSTOM_PHASE,
  CUSTOM_TIME_DISPLAY_NAME
} from '@/pages/tasks/components/PreprocessWindowFields';
import { timeRangeToWindowIso } from '@/pages/tasks/components/PreprocessFormFields';
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
        调用内部 <code>/api/v1/tasks/pipeline</code> 创建任务；须选择已发布算法模板，筛选模板仍使用系统默认。
        需后端 Hangfire 与 ClickHouse 可用。从任务列表「详情」进入可查看完整任务信息并自动刷新进度。
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
                  onFinish={async (v) => {
                    try {
                      const { algorithmTemplateId, algorithmTemplateVersion } = parseAlgorithmTemplateKey(
                        v.algorithmTemplateKey
                      );
                      if (!algorithmTemplateId || algorithmTemplateVersion == null) {
                        message.warning('请选择算法模板');
                        return;
                      }

                      const win = timeRangeToWindowIso(v.timeRange);
                      if (!win.windowStart || !win.windowEnd) {
                        message.warning('请选择开始与结束日期');
                        return;
                      }

                      const phasePick = v.phasePick as string;
                      const testBatchName =
                        phasePick === CUSTOM_PHASE ? CUSTOM_TIME_DISPLAY_NAME : phasePick;

                      const res = await tasksApi.createPipeline({
                        tasookNo: v.tasookNo,
                        satelliteNo: v.satelliteNo,
                        testBatchName,
                        windowStart: win.windowStart,
                        windowEnd: win.windowEnd,
                        algorithmTemplateId,
                        algorithmTemplateVersion
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
                  <PipelineFormFields form={form} />
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

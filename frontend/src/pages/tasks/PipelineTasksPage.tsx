import { Button, Card, Collapse, Form, Space, Typography, message } from 'antd';
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
import {
  parseFilterTemplateKey,
  timeRangeToWindowIso
} from '@/pages/tasks/components/PreprocessFormFields';
import { useTaskRunDetail } from '@/pages/tasks/hooks/useTaskRunDetail';
import { runPreprocessWithPreflight } from '@/pages/tasks/utils/preprocessConflictPreflight';
import { reExecuteWithConflictPolicy } from '@/pages/tasks/utils/preprocessConflictRetry';
import { canReExecuteTaskDetail } from '@/pages/tasks/utils/taskReExecute';

const { Paragraph } = Typography;

const runIdPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function PipelineTasksPage() {
  const [form] = Form.useForm();
  const [searchParams, setSearchParams] = useSearchParams();
  const urlRunId = searchParams.get('runId');
  const viewRunId = urlRunId && runIdPattern.test(urlRunId) ? urlRunId : null;
  const { detail, loading, setPolling, refresh } = useTaskRunDetail(viewRunId);

  const openTemplateEditor = (templateId: string, version: number) => {
    window.location.href = `/templates/filters/${encodeURIComponent(templateId)}/versions/${version}`;
  };

  const openConflictRetry = () => {
    if (!viewRunId || !detail) return;
    void runPreprocessWithPreflight({
      runId: viewRunId,
      onOpenTemplate: openTemplateEditor,
      execute: async (options) => {
        if (options) {
          await reExecuteWithConflictPolicy(
            viewRunId,
            options.onCommittedConflict === 'OVERWRITE' ? 'OVERWRITE' : 'SKIP'
          );
        } else {
          await tasksApi.reExecuteRun(viewRunId);
        }
        setPolling(true);
        await refresh();
      }
    });
  };

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
        创建 PIPELINE 任务：须选择已发布算法模板；可选启用筛选模板执行预处理（数据与元数据落盘后再运行算法），
        或不启用预处理、直接读取 ClickHouse 已有数据运行算法。需后端 Hangfire 与 ClickHouse 可用。
      </Paragraph>

      {viewRunId && (
        <>
          <TaskDetailCard detail={detail} loading={loading} />
          <Space style={{ marginTop: 8 }} wrap>
            <Button onClick={() => void refresh()} loading={loading}>
              刷新状态
            </Button>
            {detail?.error_code === 'PRE_006' && detail && canReExecuteTaskDetail(detail) && (
              <Button type="primary" danger onClick={openConflictRetry}>
                选择策略并重复执行
              </Button>
            )}
          </Space>
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

                      const useFilterTemplate = Boolean(v.useFilterTemplate);
                      let filterTemplateId: string | null = null;
                      let filterTemplateVersion: number | null = null;
                      if (useFilterTemplate) {
                        const parsed = parseFilterTemplateKey(v.filterTemplateKey);
                        filterTemplateId = parsed.filterTemplateId;
                        filterTemplateVersion = parsed.filterTemplateVersion;
                        if (!filterTemplateId || filterTemplateVersion == null) {
                          message.warning('请选择筛选模板');
                          return;
                        }
                      }

                      const res = await tasksApi.createPipeline({
                        tasookNo: v.tasookNo,
                        satelliteNo: v.satelliteNo,
                        testBatchName,
                        windowStart: win.windowStart,
                        windowEnd: win.windowEnd,
                        useFilterTemplate,
                        filterTemplateId,
                        filterTemplateVersion,
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

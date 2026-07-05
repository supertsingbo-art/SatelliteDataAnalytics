import { Button, Card, Collapse, Form, Space, Typography, message } from 'antd';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { tasksApi } from '@/api/tasks';
import {
  PreprocessFormFields,
  parseFilterTemplateKey,
  timeRangeToWindowIso
} from '@/pages/tasks/components/PreprocessFormFields';
import {
  CUSTOM_PHASE,
  CUSTOM_TIME_DISPLAY_NAME
} from '@/pages/tasks/components/PreprocessWindowFields';
import {
  combineScheduledRunAt,
  formatDailyTime,
  formatEffectiveFrom,
  type PreprocessExecutionMode
} from '@/pages/tasks/components/PreprocessSchedulePanel';
import { TaskDetailCard } from '@/pages/tasks/components/TaskDetailCard';
import { useTaskRunDetail } from '@/pages/tasks/hooks/useTaskRunDetail';
import { runPreprocessWithPreflight } from '@/pages/tasks/utils/preprocessConflictPreflight';
import { reExecuteWithConflictPolicy } from '@/pages/tasks/utils/preprocessConflictRetry';
import { canReExecuteTaskDetail } from '@/pages/tasks/utils/taskReExecute';

const { Paragraph } = Typography;

const runIdPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function PreprocessTasksPage() {
  const [form] = Form.useForm();
  const navigate = useNavigate();
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
      title={viewRunId ? '预处理入仓任务详情（PREPROCESS）' : '新建预处理入仓（PREPROCESS）'}
      extra={
        <Link to="/tasks">
          <Button type="link">返回任务列表</Button>
        </Link>
      }
    >
      <Paragraph type="secondary">
        支持三种处理类型：立即执行、指定时间执行一次、每天定时（数据窗为前一日同刻至当日设定时刻）。
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
                  initialValues={{ executionMode: 'IMMEDIATE', intervalDays: 1 }}
                  onFinish={async (v) => {
                    try {
                      const executionMode = (v.executionMode ?? 'IMMEDIATE') as PreprocessExecutionMode;
                      const { filterTemplateId, filterTemplateVersion } = parseFilterTemplateKey(
                        v.filterTemplateKey
                      );
                      if (!filterTemplateId || filterTemplateVersion == null) {
                        message.warning('请选择筛选模板');
                        return;
                      }

                      let windowStart: string | null = null;
                      let windowEnd: string | null = null;
                      let testBatchName: string | null = null;

                      if (executionMode === 'IMMEDIATE' || executionMode === 'ONCE_SCHEDULED') {
                        const win = timeRangeToWindowIso(v.timeRange);
                        windowStart = win.windowStart;
                        windowEnd = win.windowEnd;
                        if (!windowStart || !windowEnd) {
                          message.warning('请选择开始与结束日期');
                          return;
                        }
                        const phasePick = v.phasePick as string;
                        testBatchName =
                          phasePick === CUSTOM_PHASE ? CUSTOM_TIME_DISPLAY_NAME : phasePick;
                      }

                      const res = await tasksApi.createPreprocess({
                        executionMode,
                        tasookNo: v.tasookNo,
                        satelliteNo: v.satelliteNo,
                        testBatchName,
                        windowStart,
                        windowEnd,
                        scheduledAt:
                          executionMode === 'ONCE_SCHEDULED'
                            ? combineScheduledRunAt(v.scheduledRunAt)
                            : null,
                        dailyTime:
                          executionMode === 'DAILY_RECURRING' ? formatDailyTime(v.dailyTime) : null,
                        intervalDays:
                          executionMode === 'DAILY_RECURRING' ? Number(v.intervalDays) : null,
                        effectiveFrom:
                          executionMode === 'DAILY_RECURRING'
                            ? formatEffectiveFrom(v.scheduleEffectiveFrom)
                            : null,
                        filterTemplateId,
                        filterTemplateVersion
                      });

                      if (res.scheduleId) {
                        message.success(`已创建每天定时计划 ${res.scheduleId}`);
                      } else if (res.runId) {
                        if (executionMode === 'IMMEDIATE') {
                          message.success('已创建，请到任务列表点击「执行」');
                        } else {
                          message.success(`已创建 PREPROCESS 任务 ${res.runId}`);
                        }
                        setPolling(executionMode !== 'IMMEDIATE');
                        setSearchParams({ runId: res.runId });
                        navigate(`/tasks/preprocess?runId=${encodeURIComponent(res.runId)}`, {
                          replace: true
                        });
                      }
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
              )
            }
          ]}
        />
      )}
    </Card>
  );
}

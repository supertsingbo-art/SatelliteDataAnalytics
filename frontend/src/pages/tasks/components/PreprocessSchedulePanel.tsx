import { Col, DatePicker, Form, InputNumber, Radio, Row, TimePicker, Typography } from 'antd';
import type { Dayjs } from 'dayjs';
import dayjs from 'dayjs';

const { Text } = Typography;

export type PreprocessExecutionMode = 'IMMEDIATE' | 'ONCE_SCHEDULED' | 'DAILY_RECURRING';

export const EXECUTION_MODE_OPTIONS: { value: PreprocessExecutionMode; label: string }[] = [
  { value: 'IMMEDIATE', label: '一次性立即执行' },
  { value: 'ONCE_SCHEDULED', label: '一次性指定时间执行' },
  { value: 'DAILY_RECURRING', label: '每天定时任务' }
];

type Props = {
  executionMode: PreprocessExecutionMode;
};

/** 左侧模式选择 + 右侧定时配置（参考 Windows 任务计划程序布局）。 */
export function PreprocessSchedulePanel({ executionMode }: Props) {
  return (
    <Row gutter={24} style={{ marginBottom: 16 }}>
      <Col xs={24} md={8}>
        <Form.Item
          name="executionMode"
          label="任务处理类型"
          rules={[{ required: true, message: '请选择任务处理类型' }]}
        >
          <Radio.Group style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
            {EXECUTION_MODE_OPTIONS.map((opt) => (
              <Radio key={opt.value} value={opt.value}>
                {opt.label}
              </Radio>
            ))}
          </Radio.Group>
        </Form.Item>
      </Col>
      <Col xs={24} md={16}>
        {executionMode === 'ONCE_SCHEDULED' && (
          <Form.Item
            name="scheduledRunAt"
            label="开始（计划执行时刻）"
            rules={[{ required: true, message: '请选择计划执行日期与时间' }]}
            extra="任务将在该时刻开始执行；数据时间范围由下方测试阶段/自定义时间窗单独指定。"
          >
            <DatePicker
              showTime={{ format: 'HH:mm:ss' }}
              format="YYYY-MM-DD HH:mm:ss"
              style={{ width: '100%' }}
              disabledDate={(d) => d && d.isBefore(dayjs().startOf('day'))}
            />
          </Form.Item>
        )}
        {executionMode === 'DAILY_RECURRING' && (
          <>
            <Text type="secondary" style={{ display: 'block', marginBottom: 12 }}>
              每次执行时，数据时间范围为：当日设定时刻往前 24 小时（前一日同一时刻 ~ 当日该时刻）。测试阶段与时间范围由系统自动计算，无需填写。
            </Text>
            <Form.Item
              name="scheduleEffectiveFrom"
              label="开始（计划生效日期）"
              rules={[{ required: true, message: '请选择生效日期' }]}
            >
              <DatePicker style={{ width: '100%' }} />
            </Form.Item>
            <Form.Item
              name="dailyTime"
              label="每日执行时刻"
              rules={[{ required: true, message: '请选择每日执行时刻' }]}
            >
              <TimePicker style={{ width: '100%' }} />
            </Form.Item>
            <Form.Item
              name="intervalDays"
              label="每隔"
              initialValue={1}
              rules={[{ required: true, message: '请填写间隔天数' }]}
            >
              <InputNumber min={1} max={365} addonAfter="天发生一次" style={{ width: '100%' }} />
            </Form.Item>
          </>
        )}
        {executionMode === 'IMMEDIATE' && (
          <Text type="secondary">创建后立即执行；请在下方选择测试阶段与数据时间范围。</Text>
        )}
      </Col>
    </Row>
  );
}

export function combineScheduledRunAt(value: Dayjs | null | undefined): string | null {
  return value ? value.toISOString() : null;
}

export function formatDailyTime(value: Dayjs | null | undefined): string | null {
  if (!value) return null;
  return value.format('HH:mm:ss');
}

export function formatEffectiveFrom(value: Dayjs | null | undefined): string | null {
  if (!value) return null;
  return value.format('YYYY-MM-DD');
}

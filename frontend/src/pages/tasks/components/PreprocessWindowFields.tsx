import { useEffect, useMemo, useState } from 'react';
import { Button, DatePicker, Form, FormInstance, message, Select, Space, Typography } from 'antd';
import type { Dayjs } from 'dayjs';
import dayjs from 'dayjs';
import { assetsApi } from '@/api/assets';
import type { TestPhase } from '@/api/types';

const { RangePicker } = DatePicker;

export const CUSTOM_PHASE = '__CUSTOM_TIME__';
export const CUSTOM_TIME_DISPLAY_NAME = '自定义时间段';

type Props = {
  form: FormInstance;
  tasookNo?: string;
  satelliteNo?: string;
};

export function PreprocessWindowFields({ form, tasookNo, satelliteNo }: Props) {
  const phasePick = Form.useWatch('phasePick', form);
  const [phases, setPhases] = useState<TestPhase[]>([]);
  const [phasesLoading, setPhasesLoading] = useState(false);
  const [timeRangeEditable, setTimeRangeEditable] = useState(true);

  const resetPhaseAndTime = () => {
    form.setFieldsValue({ phasePick: undefined, timeRange: undefined });
    setTimeRangeEditable(true);
  };

  useEffect(() => {
    resetPhaseAndTime();
    setPhases([]);
  }, [tasookNo, satelliteNo]);

  const loadPhases = async () => {
    const t = String(tasookNo ?? '').trim();
    const s = String(satelliteNo ?? '').trim();
    if (!t || !s) {
      message.warning('请先选择型号与卫星');
      return;
    }
    setPhasesLoading(true);
    try {
      const list = await assetsApi.listTestPhases(t, s);
      setPhases(list);
      resetPhaseAndTime();
      if (list.length === 0) {
        message.info('暂无测试阶段缓存，请在「卫星资产缓存」中执行同步后再试');
      }
    } catch {
      message.error('加载测试阶段失败');
    } finally {
      setPhasesLoading(false);
    }
  };

  const onPhaseSelect = (value: string | undefined) => {
    form.setFieldsValue({ phasePick: value });
    if (!value) {
      setTimeRangeEditable(true);
      form.setFieldsValue({ timeRange: undefined });
      return;
    }
    if (value === CUSTOM_PHASE) {
      setTimeRangeEditable(true);
      form.setFieldsValue({ timeRange: undefined });
      return;
    }
    const p = phases.find((x) => x.testBatchName === value);
    if (!p) return;
    setTimeRangeEditable(false);
    form.setFieldsValue({
      timeRange: [dayjs(p.startTs).startOf('day'), dayjs(p.endTs).startOf('day')] as [Dayjs, Dayjs]
    });
  };

  const phaseSelectOptions = useMemo(
    () => [
      ...phases.map((p) => ({ value: p.testBatchName, label: p.testBatchName })),
      { value: CUSTOM_PHASE, label: CUSTOM_TIME_DISPLAY_NAME }
    ],
    [phases]
  );

  return (
    <Space direction="vertical" style={{ width: '100%' }} size="middle">
      <Typography.Text type="secondary">
        测试阶段来自 test_batch_cache；选择阶段后自动填入时间范围，或选择「自定义时间段」手动指定。
      </Typography.Text>
      <Space wrap>
        <Button
          type="default"
          onClick={loadPhases}
          loading={phasesLoading}
          disabled={!tasookNo || !satelliteNo}
        >
          加载测试阶段
        </Button>
        <Button onClick={resetPhaseAndTime} disabled={!phasePick && phases.length === 0}>
          清空时间窗
        </Button>
      </Space>
      <Form.Item
        name="phasePick"
        label="测试阶段（快捷选择）"
        rules={[{ required: true, message: '请选择测试阶段或自定义时间段' }]}
      >
        <Select
          allowClear
          placeholder={phases.length ? '选择测试阶段' : '请先点击「加载测试阶段」'}
          style={{ width: '100%' }}
          disabled={phases.length === 0}
          options={phaseSelectOptions}
          onChange={onPhaseSelect}
        />
      </Form.Item>
      <Form.Item
        name="timeRange"
        label="数据时间范围"
        extra={timeRangeEditable ? '请选择开始与结束日期' : '已按所选测试阶段锁定'}
        rules={[
          { required: true, message: '请选择开始与结束日期' },
          {
            validator(_, value: [Dayjs, Dayjs] | null | undefined) {
              if (!value?.[0] || !value[1]) {
                return Promise.reject(new Error('请选择开始与结束日期'));
              }
              if (!value[0].isBefore(value[1]) && !value[0].isSame(value[1], 'day')) {
                return Promise.reject(new Error('开始日期不能晚于结束日期'));
              }
              return Promise.resolve();
            }
          }
        ]}
      >
        <RangePicker style={{ width: '100%' }} disabled={!timeRangeEditable} />
      </Form.Item>
    </Space>
  );
}

import { useState } from 'react';
import { Button, DatePicker, Form, FormInstance, Input, message, Select, Space, Typography } from 'antd';
import type { Dayjs } from 'dayjs';
import dayjs from 'dayjs';
import { assetsApi } from '@/api/assets';
import type { TestPhase } from '@/api/types';

const { RangePicker } = DatePicker;

type Props = {
  form: FormInstance;
};

export function TaskWindowFields({ form }: Props) {
  const [phases, setPhases] = useState<TestPhase[]>([]);
  const [loading, setLoading] = useState(false);

  const loadPhases = async () => {
    const tasookNo = String(form.getFieldValue('tasookNo') ?? '').trim();
    const satelliteNo = String(form.getFieldValue('satelliteNo') ?? '').trim();
    if (!tasookNo || !satelliteNo) {
      message.warning('请先填写型号代号与卫星代号');
      return;
    }
    setLoading(true);
    try {
      const list = await assetsApi.listTestPhases(tasookNo, satelliteNo);
      setPhases(list);
      if (list.length === 0) {
        message.info('暂无测试阶段缓存，请在「卫星资产缓存」中执行同步后再试');
      }
    } catch {
      message.error('加载测试阶段失败');
    } finally {
      setLoading(false);
    }
  };

  const onPhasePick = (testBatchName: string) => {
    const p = phases.find((x) => x.testBatchName === testBatchName);
    if (!p) return;
    form.setFieldsValue({
      testBatchName: p.testBatchName,
      timeRange: [dayjs(p.startTs), dayjs(p.endTs)] as [Dayjs, Dayjs]
    });
  };

  const clearWindow = () => {
    form.setFieldsValue({ timeRange: undefined });
  };

  return (
    <Space direction="vertical" style={{ width: '100%' }} size="middle">
      <Typography.Text type="secondary">
        测试阶段与时间来自资产缓存（由卫星测试流程规划服务 POST /api/testplan/teststages 同步）；可快捷填入后再手动调整时间范围。
      </Typography.Text>
      <Space wrap>
        <Button type="default" onClick={loadPhases} loading={loading}>
          加载测试阶段
        </Button>
        <Button onClick={clearWindow}>清空时间窗</Button>
      </Space>
      <Form.Item label="测试阶段（快捷选择）" help="选择后写入 test_batch_name 与下方起止时间">
        <Select
          allowClear
          placeholder={phases.length ? '选择阶段名称' : '请先点击「加载测试阶段」'}
          style={{ width: '100%' }}
          disabled={phases.length === 0}
          options={phases.map((p) => ({
            value: p.testBatchName,
            label: `${p.testBatchName} — ${dayjs(p.startTs).format('YYYY-MM-DD HH:mm')} ~ ${dayjs(p.endTs).format('YYYY-MM-DD HH:mm')}`
          }))}
          onChange={(v) => {
            if (v) onPhasePick(v as string);
          }}
        />
      </Form.Item>
      <Form.Item name="testBatchName" label="测试阶段名称" help="可手输，或与上方快捷选择同步（写入 task_run.test_batch_name，非外键）">
        <Input placeholder="可选；与筛选模板 TEST_BATCH 模式配合" />
      </Form.Item>
      <Form.Item
        name="timeRange"
        label="数据时间范围（window_start ~ window_end）"
        extra="留空则由筛选模板推断；支持精确到秒"
        rules={[
          {
            validator(_, value: [Dayjs, Dayjs] | null | undefined) {
              if (!value || (!value[0] && !value[1])) return Promise.resolve();
              if (!value[0] || !value[1]) {
                return Promise.reject(new Error('请选择完整的开始与结束时间，或清空时间窗'));
              }
              if (!value[0].isBefore(value[1])) {
                return Promise.reject(new Error('开始时间须早于结束时间'));
              }
              return Promise.resolve();
            }
          }
        ]}
      >
        <RangePicker showTime style={{ width: '100%' }} />
      </Form.Item>
    </Space>
  );
}

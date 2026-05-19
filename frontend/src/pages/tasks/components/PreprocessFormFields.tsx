import { useEffect, useMemo, useState } from 'react';
import { Button, DatePicker, Form, FormInstance, message, Select, Space, Typography } from 'antd';
import type { Dayjs } from 'dayjs';
import dayjs from 'dayjs';
import { assetsApi } from '@/api/assets';
import { filterTemplatesApi } from '@/api/templates';
import type { FilterTemplateView, SatelliteListItem, TestPhase } from '@/api/types';

const { RangePicker } = DatePicker;

export const CUSTOM_PHASE = '__CUSTOM_TIME__';

/** 写入 task_run.test_batch_name，表示用户选择了自定义时间窗（非 test_batch_cache 外键）。 */
export const CUSTOM_TIME_DISPLAY_NAME = '自定义时间段';

type Props = {
  form: FormInstance;
};

export function formatFilterTemplateLabel(t: FilterTemplateView) {
  return `${t.templateName} v${t.version}`;
}

export function parseFilterTemplateKey(key: string | undefined | null) {
  if (!key) {
    return { filterTemplateId: null as string | null, filterTemplateVersion: null as number | null };
  }
  const sep = key.indexOf(':');
  if (sep <= 0) {
    return { filterTemplateId: null, filterTemplateVersion: null };
  }
  const templateId = key.slice(0, sep);
  const version = Number(key.slice(sep + 1));
  if (!templateId || !Number.isFinite(version)) {
    return { filterTemplateId: null, filterTemplateVersion: null };
  }
  return { filterTemplateId: templateId, filterTemplateVersion: version };
}

export function timeRangeToWindowIso(range: [Dayjs, Dayjs] | null | undefined) {
  if (!range?.[0] || !range[1]) {
    return { windowStart: null as string | null, windowEnd: null as string | null };
  }
  return {
    windowStart: range[0].startOf('day').toISOString(),
    windowEnd: range[1].endOf('day').toISOString()
  };
}

function uniqueTasooks(items: SatelliteListItem[]) {
  const map = new Map<string, { tasookNo: string; tasookName: string | null }>();
  for (const item of items) {
    if (!map.has(item.tasookNo)) {
      map.set(item.tasookNo, { tasookNo: item.tasookNo, tasookName: item.tasookName });
    }
  }
  return [...map.values()].sort((a, b) => a.tasookNo.localeCompare(b.tasookNo));
}

function satellitesForTasook(items: SatelliteListItem[], tasookNo: string) {
  return items
    .filter((s) => s.tasookNo === tasookNo && s.isEnabled)
    .sort((a, b) => a.satelliteNo.localeCompare(b.satelliteNo));
}

function formatTasookLabel(t: { tasookNo: string; tasookName: string | null }) {
  return t.tasookName ? `${t.tasookNo} ${t.tasookName}` : t.tasookNo;
}

function formatSatelliteLabel(s: SatelliteListItem) {
  return `${s.satelliteNo} ${s.satelliteName}`;
}

export function PreprocessFormFields({ form }: Props) {
  const tasookNo = Form.useWatch('tasookNo', form);
  const satelliteNo = Form.useWatch('satelliteNo', form);
  const phasePick = Form.useWatch('phasePick', form);

  const [satellites, setSatellites] = useState<SatelliteListItem[]>([]);
  const [satellitesLoading, setSatellitesLoading] = useState(false);
  const [phases, setPhases] = useState<TestPhase[]>([]);
  const [phasesLoading, setPhasesLoading] = useState(false);
  const [filterTemplates, setFilterTemplates] = useState<FilterTemplateView[]>([]);
  const [filterTemplatesLoading, setFilterTemplatesLoading] = useState(false);
  const [timeRangeEditable, setTimeRangeEditable] = useState(true);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      setSatellitesLoading(true);
      try {
        const result = await assetsApi.listSatellites({ enabledOnly: true, pageSize: 500 });
        if (!cancelled) {
          setSatellites(result.items);
        }
      } catch {
        if (!cancelled) {
          message.error('加载卫星缓存列表失败');
        }
      } finally {
        if (!cancelled) {
          setSatellitesLoading(false);
        }
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const tasookOptions = useMemo(() => uniqueTasooks(satellites), [satellites]);
  const satelliteOptions = useMemo(
    () => (tasookNo ? satellitesForTasook(satellites, tasookNo) : []),
    [satellites, tasookNo]
  );

  useEffect(() => {
    if (!tasookNo || !satelliteNo) {
      setFilterTemplates([]);
      return;
    }
    let cancelled = false;
    (async () => {
      setFilterTemplatesLoading(true);
      try {
        const list = await filterTemplatesApi.applicable(tasookNo, satelliteNo);
        if (!cancelled) {
          setFilterTemplates(list);
        }
      } catch {
        if (!cancelled) {
          message.error('加载可用筛选模板失败');
          setFilterTemplates([]);
        }
      } finally {
        if (!cancelled) {
          setFilterTemplatesLoading(false);
        }
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [tasookNo, satelliteNo]);

  useEffect(() => {
    if (!phasePick || phasePick === CUSTOM_PHASE) {
      setTimeRangeEditable(true);
    } else {
      setTimeRangeEditable(false);
    }
  }, [phasePick]);

  const resetPhaseAndTime = () => {
    setPhases([]);
    setTimeRangeEditable(true);
    form.setFieldsValue({ timeRange: undefined, phasePick: undefined });
  };

  const onTasookChange = () => {
    form.setFieldsValue({
      satelliteNo: undefined,
      filterTemplateKey: undefined,
      timeRange: undefined,
      phasePick: undefined
    });
    resetPhaseAndTime();
  };

  const onSatelliteChange = () => {
    form.setFieldsValue({
      filterTemplateKey: undefined,
      timeRange: undefined,
      phasePick: undefined
    });
    resetPhaseAndTime();
  };

  const loadPhases = async () => {
    const t = String(form.getFieldValue('tasookNo') ?? '').trim();
    const s = String(form.getFieldValue('satelliteNo') ?? '').trim();
    if (!t || !s) {
      message.warning('请先选择型号与卫星');
      return;
    }
    setPhasesLoading(true);
    try {
      const list = await assetsApi.listTestPhases(t, s);
      setPhases(list);
      form.setFieldsValue({ phasePick: undefined, timeRange: undefined });
      setTimeRangeEditable(true);
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
    if (!p) {
      return;
    }
    setTimeRangeEditable(false);
    form.setFieldsValue({
      timeRange: [dayjs(p.startTs).startOf('day'), dayjs(p.endTs).startOf('day')] as [Dayjs, Dayjs]
    });
  };

  const phaseSelectOptions = [
    ...phases.map((p) => ({
      value: p.testBatchName,
      label: p.testBatchName
    })),
    { value: CUSTOM_PHASE, label: CUSTOM_TIME_DISPLAY_NAME }
  ];

  return (
    <Space direction="vertical" style={{ width: '100%' }} size="middle">
      <Form.Item name="tasookNo" label="型号" rules={[{ required: true, message: '请选择型号' }]}>
        <Select
          showSearch
          allowClear
          placeholder="从已启用卫星缓存中选择型号"
          loading={satellitesLoading}
          optionFilterProp="label"
          options={tasookOptions.map((t) => ({
            value: t.tasookNo,
            label: formatTasookLabel(t)
          }))}
          onChange={onTasookChange}
        />
      </Form.Item>

      <Form.Item name="satelliteNo" label="卫星" rules={[{ required: true, message: '请选择卫星' }]}>
        <Select
          showSearch
          allowClear
          disabled={!tasookNo}
          placeholder={tasookNo ? '选择该型号下的卫星' : '请先选择型号'}
          optionFilterProp="label"
          options={satelliteOptions.map((s) => ({
            value: s.satelliteNo,
            label: formatSatelliteLabel(s)
          }))}
          onChange={onSatelliteChange}
        />
      </Form.Item>

      <Typography.Text type="secondary">
        测试阶段来自 test_batch_cache（卫星测试流程规划同步）；选择阶段后自动填入时间范围，或选择「自定义时间」手动指定。
      </Typography.Text>

      <Space wrap>
        <Button type="default" onClick={loadPhases} loading={phasesLoading} disabled={!tasookNo || !satelliteNo}>
          加载测试阶段
        </Button>
        <Button
          onClick={() => {
            resetPhaseAndTime();
          }}
          disabled={!phasePick && phases.length === 0}
        >
          清空时间窗
        </Button>
      </Space>

      <Form.Item
        name="phasePick"
        label="测试阶段（快捷选择）"
        rules={[{ required: true, message: '请选择测试阶段或自定义时间段' }]}
        help="选缓存中的阶段名称，或「自定义时间段」后手动填写下方日期"
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

      <Form.Item
        name="filterTemplateKey"
        label="筛选模板"
        rules={[{ required: true, message: '请选择筛选模板' }]}
        extra="列表为当前卫星可用的已发布模板"
      >
        <Select
          showSearch
          placeholder={satelliteNo ? '请选择筛选模板' : '请先选择型号与卫星'}
          disabled={!tasookNo || !satelliteNo}
          loading={filterTemplatesLoading}
          optionFilterProp="label"
          options={filterTemplates.map((t) => ({
            value: `${t.templateId}:${t.version}`,
            label: formatFilterTemplateLabel(t)
          }))}
        />
      </Form.Item>
    </Space>
  );
}

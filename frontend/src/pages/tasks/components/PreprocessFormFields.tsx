import { useEffect, useMemo, useState } from 'react';
import { Divider, Form, FormInstance, message, Select } from 'antd';
import { assetsApi } from '@/api/assets';
import { filterTemplatesApi } from '@/api/templates';
import type { FilterTemplateView, SatelliteListItem } from '@/api/types';
import type { Dayjs } from 'dayjs';
import {
  PreprocessSchedulePanel,
  type PreprocessExecutionMode
} from '@/pages/tasks/components/PreprocessSchedulePanel';
import { PreprocessWindowFields } from '@/pages/tasks/components/PreprocessWindowFields';

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
  const executionMode = (Form.useWatch('executionMode', form) ?? 'IMMEDIATE') as PreprocessExecutionMode;

  const [satellites, setSatellites] = useState<SatelliteListItem[]>([]);
  const [satellitesLoading, setSatellitesLoading] = useState(false);
  const [filterTemplates, setFilterTemplates] = useState<FilterTemplateView[]>([]);
  const [filterTemplatesLoading, setFilterTemplatesLoading] = useState(false);

  const showWindowFields =
    executionMode === 'IMMEDIATE' || executionMode === 'ONCE_SCHEDULED';

  useEffect(() => {
    let cancelled = false;
    (async () => {
      setSatellitesLoading(true);
      try {
        const result = await assetsApi.listSatellites({ enabledOnly: true, pageSize: 500 });
        if (!cancelled) setSatellites(result.items);
      } catch {
        if (!cancelled) message.error('加载卫星缓存列表失败');
      } finally {
        if (!cancelled) setSatellitesLoading(false);
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
        if (!cancelled) setFilterTemplates(list);
      } catch {
        if (!cancelled) {
          message.error('加载适用筛选模板失败');
          setFilterTemplates([]);
        }
      } finally {
        if (!cancelled) setFilterTemplatesLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [tasookNo, satelliteNo]);

  useEffect(() => {
    if (!showWindowFields) {
      form.setFieldsValue({ phasePick: undefined, timeRange: undefined });
    }
  }, [showWindowFields, form]);

  const onTasookChange = () => {
    form.setFieldsValue({
      satelliteNo: undefined,
      filterTemplateKey: undefined,
      phasePick: undefined,
      timeRange: undefined
    });
  };

  const onSatelliteChange = () => {
    form.setFieldsValue({
      filterTemplateKey: undefined,
      phasePick: undefined,
      timeRange: undefined
    });
  };

  return (
    <>
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

      <Divider />

      <PreprocessSchedulePanel executionMode={executionMode} />

      {showWindowFields && (
        <>
          <Divider />
          <PreprocessWindowFields form={form} tasookNo={tasookNo} satelliteNo={satelliteNo} />
        </>
      )}
    </>
  );
}

// Re-export for submit page
export {
  CUSTOM_PHASE,
  CUSTOM_TIME_DISPLAY_NAME
} from '@/pages/tasks/components/PreprocessWindowFields';

import { useEffect, useMemo, useState } from 'react';
import { Divider, Form, FormInstance, message, Select, Switch } from 'antd';
import { assetsApi } from '@/api/assets';
import { algoTemplatesApi, filterTemplatesApi } from '@/api/templates';
import type { AlgorithmTemplateView, FilterTemplateView, SatelliteListItem } from '@/api/types';
import {
  formatFilterTemplateLabel
} from '@/pages/tasks/components/PreprocessFormFields';
import { PreprocessWindowFields } from '@/pages/tasks/components/PreprocessWindowFields';

type Props = {
  form: FormInstance;
};

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

export function formatAlgorithmTemplateLabel(t: AlgorithmTemplateView) {
  return `${t.templateName} v${t.version}`;
}

export function parseAlgorithmTemplateKey(key: string | undefined | null) {
  if (!key) {
    return { algorithmTemplateId: null as string | null, algorithmTemplateVersion: null as number | null };
  }
  const sep = key.indexOf(':');
  if (sep <= 0) {
    return { algorithmTemplateId: null, algorithmTemplateVersion: null };
  }
  const templateId = key.slice(0, sep);
  const version = Number(key.slice(sep + 1));
  if (!templateId || !Number.isFinite(version)) {
    return { algorithmTemplateId: null, algorithmTemplateVersion: null };
  }
  return { algorithmTemplateId: templateId, algorithmTemplateVersion: version };
}

export function PipelineFormFields({ form }: Props) {
  const tasookNo = Form.useWatch('tasookNo', form);
  const satelliteNo = Form.useWatch('satelliteNo', form);
  const useFilterTemplate = Form.useWatch('useFilterTemplate', form);

  const [satellites, setSatellites] = useState<SatelliteListItem[]>([]);
  const [satellitesLoading, setSatellitesLoading] = useState(false);
  const [algorithmTemplates, setAlgorithmTemplates] = useState<AlgorithmTemplateView[]>([]);
  const [algorithmTemplatesLoading, setAlgorithmTemplatesLoading] = useState(false);
  const [filterTemplates, setFilterTemplates] = useState<FilterTemplateView[]>([]);
  const [filterTemplatesLoading, setFilterTemplatesLoading] = useState(false);

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

  useEffect(() => {
    let cancelled = false;
    (async () => {
      setAlgorithmTemplatesLoading(true);
      try {
        const result = await algoTemplatesApi.list({ status: 'Published', pageSize: 500 });
        if (!cancelled) setAlgorithmTemplates(result.items);
      } catch {
        if (!cancelled) {
          message.error('加载已发布算法模板失败');
          setAlgorithmTemplates([]);
        }
      } finally {
        if (!cancelled) setAlgorithmTemplatesLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

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

  const tasookOptions = useMemo(() => uniqueTasooks(satellites), [satellites]);
  const satelliteOptions = useMemo(
    () => (tasookNo ? satellitesForTasook(satellites, tasookNo) : []),
    [satellites, tasookNo]
  );

  const onTasookChange = () => {
    form.setFieldsValue({
      satelliteNo: undefined,
      algorithmTemplateKey: undefined,
      filterTemplateKey: undefined,
      phasePick: undefined,
      timeRange: undefined
    });
  };

  const onSatelliteChange = () => {
    form.setFieldsValue({
      algorithmTemplateKey: undefined,
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
        name="algorithmTemplateKey"
        label="算法模板"
        rules={[{ required: true, message: '请选择算法模板' }]}
        extra="须选择已发布算法模板"
      >
        <Select
          showSearch
          placeholder="请选择算法模板"
          loading={algorithmTemplatesLoading}
          optionFilterProp="label"
          options={algorithmTemplates.map((t) => ({
            value: `${t.templateId}:${t.version}`,
            label: formatAlgorithmTemplateLabel(t)
          }))}
        />
      </Form.Item>

      <Form.Item
        name="useFilterTemplate"
        label="启用数据预处理"
        valuePropName="checked"
        initialValue={false}
        extra={
          useFilterTemplate
            ? '将按所选筛选模板执行预处理，筛选时序与元数据落盘后自动运行算法'
            : '直接使用 ClickHouse 已有预处理数据，请确保算法输入参数在时间窗内有数据'
        }
      >
        <Switch
          onChange={(checked) => {
            if (!checked) {
              form.setFieldsValue({ filterTemplateKey: undefined });
            }
          }}
        />
      </Form.Item>

      {useFilterTemplate && (
        <Form.Item
          name="filterTemplateKey"
          label="筛选模板"
          rules={[{ required: true, message: '请选择筛选模板' }]}
        >
          <Select
            showSearch
            disabled={!tasookNo || !satelliteNo}
            placeholder={tasookNo && satelliteNo ? '请选择适用筛选模板' : '请先选择型号与卫星'}
            loading={filterTemplatesLoading}
            optionFilterProp="label"
            options={filterTemplates.map((t) => ({
              value: `${t.templateId}:${t.version}`,
              label: formatFilterTemplateLabel(t)
            }))}
          />
        </Form.Item>
      )}

      <Divider />

      <PreprocessWindowFields form={form} tasookNo={tasookNo} satelliteNo={satelliteNo} />
    </>
  );
}

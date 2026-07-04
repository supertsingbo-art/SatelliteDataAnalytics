import { useCallback, useMemo, useRef } from 'react';
import { Alert, Checkbox, Space, Typography } from 'antd';
import ReactECharts from 'echarts-for-react';
import type { EChartsOption } from 'echarts';
import type {
  OutlierMarkOption,
  TaskProcessedDataColumn,
  TaskProcessedSeries
} from '@/api/types';

const { Paragraph, Text } = Typography;

const CHART_HEIGHT = 'calc(100vh - 380px)';
const LINE_COLORS = ['#1677ff', '#52c41a', '#722ed1', '#fa8c16', '#13c2c2', '#eb2f96', '#a0d911', '#2f54eb'];
const MAX_PARAMS = 8;

function normalizeStatus(status?: string | null): string {
  return (status ?? '').trim().toUpperCase();
}

function outlierScatterColor(
  point: { review_status?: string | null; is_confirmed_outlier?: boolean; is_outlier?: boolean },
  reviewOptionByCode: Record<string, OutlierMarkOption>
): string {
  const status = normalizeStatus(point.review_status);
  const mark = reviewOptionByCode[status];
  if (mark) {
    return mark.is_outlier ? '#cf1322' : '#389e0d';
  }
  if (status === 'PENDING' || point.is_confirmed_outlier || point.is_outlier) {
    return '#d46b08';
  }
  return '#d46b08';
}

function parseTs(value: string): number {
  return new Date(value).getTime();
}

function formatWindowLabel(value: string): string {
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return value;
  const pad = (n: number) => n.toString().padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
}

export interface ProcessedDataChartPanelProps {
  columns: TaskProcessedDataColumn[];
  seriesData: TaskProcessedSeries | null;
  loading: boolean;
  selectedParamIds: string[];
  onSelectedParamIdsChange: (ids: string[]) => void;
  onWindowChange: (windowStart: string, windowEnd: string) => void;
  reviewOptions?: OutlierMarkOption[];
}

export function ProcessedDataChartPanel({
  columns,
  seriesData,
  loading,
  selectedParamIds,
  onSelectedParamIdsChange,
  onWindowChange,
  reviewOptions = []
}: ProcessedDataChartPanelProps) {
  const zoomTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const chartRef = useRef<ReactECharts | null>(null);

  const reviewOptionByCode = useMemo(() => {
    const map: Record<string, OutlierMarkOption> = {};
    for (const item of reviewOptions) {
      map[normalizeStatus(item.mark_code)] = item;
    }
    return map;
  }, [reviewOptions]);

  const checkboxOptions = useMemo(
    () =>
      columns.map((col) => ({
        label: col.label,
        value: col.param_id,
        disabled: selectedParamIds.length >= MAX_PARAMS && !selectedParamIds.includes(col.param_id)
      })),
    [columns, selectedParamIds]
  );

  const handleParamChange = (checked: string[]) => {
    if (checked.length > MAX_PARAMS) return;
    onSelectedParamIdsChange(checked);
  };

  const scheduleWindowRefetch = useCallback(
    (startPercent: number, endPercent: number) => {
      if (!seriesData) return;
      const fullStart = parseTs(seriesData.window_start);
      const fullEnd = parseTs(seriesData.window_end);
      if (Number.isNaN(fullStart) || Number.isNaN(fullEnd) || fullEnd <= fullStart) return;

      const span = fullEnd - fullStart;
      const nextStart = new Date(fullStart + (span * startPercent) / 100);
      const nextEnd = new Date(fullStart + (span * endPercent) / 100);
      if (nextEnd.getTime() - nextStart.getTime() < 1000) return;

      if (zoomTimerRef.current) {
        clearTimeout(zoomTimerRef.current);
      }
      zoomTimerRef.current = setTimeout(() => {
        onWindowChange(nextStart.toISOString(), nextEnd.toISOString());
      }, 300);
    },
    [onWindowChange, seriesData]
  );

  const chartOption = useMemo((): EChartsOption => {
    if (!seriesData) {
      return {
        title: { text: '请选择参数', left: 'center', top: 'middle', textStyle: { color: '#999', fontSize: 14 } }
      };
    }

    const legendNames = seriesData.series.map((s) => s.label);
    const lineSeries = seriesData.series.map((s, index) => ({
      name: s.label,
      type: 'line' as const,
      showSymbol: false,
      smooth: false,
      lineStyle: { width: 1.5, color: LINE_COLORS[index % LINE_COLORS.length] },
      itemStyle: { color: LINE_COLORS[index % LINE_COLORS.length] },
      // value tuple: [ts, avg, min, max, count]
      data: s.points.map((p) => [parseTs(p.ts), p.value, p.min_value, p.max_value, p.point_count])
    }));

    const outlierSeries = {
      name: '离群点',
      type: 'scatter' as const,
      symbolSize: 7,
      data: seriesData.outliers.map((o) => ({
        value: [parseTs(o.ts), o.value] as [number, number],
        itemStyle: { color: outlierScatterColor(o, reviewOptionByCode) },
        paramLabel: o.param_label,
        reviewStatus: o.review_status ?? 'PENDING'
      }))
    };

    const rawTotal = seriesData.series.reduce((acc, s) => acc + s.raw_point_count, 0);
    const bucketCount = seriesData.series.reduce((acc, s) => acc + s.points.length, 0);

    return {
      title: {
        text: `共 ${rawTotal} 个原始点，当前显示 ${bucketCount} 个时间桶（桶宽 ${seriesData.bucket_seconds}s）`,
        left: 0,
        top: 0,
        textStyle: { fontSize: 12, fontWeight: 'normal', color: 'rgba(0,0,0,0.45)' }
      },
      grid: { left: 56, right: 24, top: 48, bottom: 72 },
      tooltip: {
        trigger: 'axis',
        axisPointer: { type: 'cross' },
        formatter: (params: unknown) => {
          const items = Array.isArray(params) ? params : [params];
          const axis = items[0] as { axisValue?: number; seriesName?: string; data?: unknown; value?: unknown };
          if (axis?.axisValue == null) return '';
          const lines = [`时间：${formatWindowLabel(new Date(axis.axisValue).toISOString())}`];
          for (const item of items) {
            const row = item as {
              seriesName?: string;
              data?:
                | [number, number, number, number, number]
                | { value?: [number, number]; paramLabel?: string; reviewStatus?: string };
              value?: [number, number, number, number, number];
            };
            if (row.seriesName === '离群点') {
              const payload = row.data as { value?: [number, number]; paramLabel?: string; reviewStatus?: string } | undefined;
              const val = payload?.value?.[1] ?? row.value?.[1];
              if (val == null) continue;
              lines.push(
                `${payload?.paramLabel ?? '离群点'}：${Number(val).toFixed(4)}（复核 ${payload?.reviewStatus ?? 'PENDING'}）`
              );
              continue;
            }
            const tuple = Array.isArray(row.data)
              ? row.data
              : Array.isArray(row.value)
                ? row.value
                : undefined;
            const avg = tuple?.[1];
            if (avg == null) continue;
            const min = tuple?.[2];
            const max = tuple?.[3];
            const count = tuple?.[4];
            lines.push(
              `${row.seriesName}：avg ${Number(avg).toFixed(4)}，min ${Number(min ?? avg).toFixed(4)}，max ${Number(max ?? avg).toFixed(4)}，count ${Number(count ?? 0)}`
            );
          }
          return lines.join('<br/>');
        }
      },
      legend: { data: [...legendNames, '离群点'], top: 24, type: 'scroll' },
      xAxis: {
        type: 'time',
        min: parseTs(seriesData.window_start),
        max: parseTs(seriesData.window_end)
      },
      yAxis: { type: 'value', scale: true },
      dataZoom: [
        { type: 'inside', xAxisIndex: 0, filterMode: 'none' },
        { type: 'slider', xAxisIndex: 0, height: 24, bottom: 8, filterMode: 'none' }
      ],
      series: [...lineSeries, outlierSeries]
    };
  }, [reviewOptionByCode, seriesData]);

  const onEvents = useMemo(
    () => ({
      datazoom: () => {
        const chart = chartRef.current?.getEchartsInstance();
        if (!chart || !seriesData) return;
        const option = chart.getOption() as { dataZoom?: Array<{ start?: number; end?: number }> };
        const zoom = option.dataZoom?.[0];
        if (zoom?.start == null || zoom?.end == null) return;
        scheduleWindowRefetch(zoom.start, zoom.end);
      }
    }),
    [scheduleWindowRefetch, seriesData]
  );

  const summaryText = seriesData
    ? `视窗 ${formatWindowLabel(seriesData.window_start)} ~ ${formatWindowLabel(seriesData.window_end)}；离群点 ${seriesData.outliers.length}/${seriesData.outliers_total}（橙=待复核，红=已确认离群，绿=已确认非离群）`
    : '勾选同量纲参数后加载曲线；缩放视窗将重新查询更细分辨率。';

  return (
    <Space direction="vertical" style={{ width: '100%' }} size="small">
      <Checkbox.Group
        options={checkboxOptions}
        value={selectedParamIds}
        onChange={(values) => handleParamChange(values as string[])}
      />
      <Paragraph type="secondary" style={{ marginBottom: 0 }}>
        {summaryText}
      </Paragraph>
      {seriesData?.outliers_truncated && (
        <Alert
          type="warning"
          showIcon
          message={`视窗内离群点共 ${seriesData.outliers_total} 个，已截断显示前 ${seriesData.outliers.length} 个。请缩小时间范围后重试。`}
        />
      )}
      {selectedParamIds.length === 0 && (
        <Text type="secondary">请至少选择一个参数（最多 {MAX_PARAMS} 个）。</Text>
      )}
      <ReactECharts
        ref={chartRef}
        option={chartOption}
        notMerge
        lazyUpdate
        showLoading={loading}
        style={{ height: CHART_HEIGHT, width: '100%' }}
        onEvents={onEvents}
      />
    </Space>
  );
}

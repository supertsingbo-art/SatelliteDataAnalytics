import { useMemo } from 'react';
import { Select } from 'antd';
import type { DefaultOptionType } from 'antd/es/select';
import { ParamCache, formatParamCacheLabel, paramCacheId } from '@/api/types';

export function filterParamCacheOption(input: string, option?: DefaultOptionType): boolean {
  const q = input.trim().toLowerCase();
  if (!q) {
    return true;
  }

  const label = String(option?.label ?? '').toLowerCase();
  const value = String(option?.value ?? '').toLowerCase();
  return label.includes(q) || value.includes(q);
}

export function buildParamCacheSelectOptions(parameters: ParamCache[]) {
  return parameters.map((p) => ({
    value: paramCacheId(p),
    label: formatParamCacheLabel(p)
  }));
}

interface ParamCacheSelectProps {
  value?: string;
  onChange: (paramId: string) => void;
  parameters: ParamCache[];
  loading?: boolean;
  disabled?: boolean;
  placeholder?: string;
}

/** 参数下拉：展示参考星 param_cache 全量选项，支持按代号/描述/ID 本地过滤。 */
export function ParamCacheSelect({
  value,
  onChange,
  parameters,
  loading,
  disabled,
  placeholder = '选择参数（代号 描述），可输入过滤'
}: ParamCacheSelectProps) {
  const options = useMemo(() => buildParamCacheSelectOptions(parameters), [parameters]);

  return (
    <Select
      style={{ width: '100%' }}
      value={value || undefined}
      showSearch
      allowClear
      loading={loading}
      disabled={disabled}
      placeholder={placeholder}
      options={options}
      virtual
      listHeight={360}
      filterOption={filterParamCacheOption}
      onChange={(next) => onChange(String(next))}
    />
  );
}

import { useMemo } from 'react';
import { Select } from 'antd';
import type { DefaultOptionType } from 'antd/es/select';
import { CommandCache, formatCommandCacheLabel } from '@/api/types';

function filterCommandCacheOption(input: string, option?: DefaultOptionType): boolean {
  const q = input.trim().toLowerCase();
  if (!q) {
    return true;
  }

  const label = String(option?.label ?? '').toLowerCase();
  const value = String(option?.value ?? '').toLowerCase();
  return label.includes(q) || value.includes(q);
}

function buildCommandCacheSelectOptions(commands: CommandCache[]) {
  return commands.map((c) => ({
    value: String(c.cmdId),
    label: formatCommandCacheLabel(c)
  }));
}

interface CommandCacheSelectProps {
  value?: string;
  onChange: (commandId: string) => void;
  commands: CommandCache[];
  loading?: boolean;
  disabled?: boolean;
  placeholder?: string;
}

/** 指令下拉：展示参考星 command_cache 全量选项，支持按代号/名称/描述/ID 本地过滤。 */
export function CommandCacheSelect({
  value,
  onChange,
  commands,
  loading,
  disabled,
  placeholder = '选择指令（代号 名称(描述)），可输入过滤'
}: CommandCacheSelectProps) {
  const options = useMemo(() => buildCommandCacheSelectOptions(commands), [commands]);

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
      filterOption={filterCommandCacheOption}
      onChange={(next) => onChange(next == null ? '' : String(next))}
    />
  );
}

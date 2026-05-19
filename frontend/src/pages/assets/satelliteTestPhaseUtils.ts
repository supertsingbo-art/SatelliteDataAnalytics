import type { SatelliteListItem, TestPhase } from '@/api/types';

/** 从 test_batch_cache 列表项提取阶段名称（去重、保持顺序）。 */
export function testPhaseNamesFromCache(phases: TestPhase[]): string[] {
  const seen = new Set<string>();
  const names: string[] = [];
  for (const p of phases) {
    const name = p.testBatchName?.trim();
    if (!name || seen.has(name)) {
      continue;
    }
    seen.add(name);
    names.push(name);
  }
  return names;
}

/** 兼容 API 可能返回的多种字段名。 */
export function normalizeSatelliteListItem(
  item: SatelliteListItem & {
    development_phases?: string[];
    DevelopmentPhases?: string[];
  }
): SatelliteListItem {
  const phases =
    item.developmentPhases ??
    item.development_phases ??
    item.DevelopmentPhases ??
    [];
  return {
    ...item,
    developmentPhases: Array.isArray(phases) ? phases.filter((n) => String(n).trim()) : []
  };
}

import { request } from './client';
import type { OutlierMarkOption } from './types';

const SETTINGS_BASE = '/api/v1/system-configs';

export const settingsApi = {
  getOutlierMarks: () =>
    request<OutlierMarkOption[]>('get', `${SETTINGS_BASE}/outlier-marks`),
  saveOutlierMarks: (items: OutlierMarkOption[]) =>
    request<OutlierMarkOption[]>('put', `${SETTINGS_BASE}/outlier-marks`, { items })
};

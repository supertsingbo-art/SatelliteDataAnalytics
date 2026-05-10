import { request } from './client';
import { SatelliteGroupMemberDto, SatelliteGroupNode } from './types';

const BASE = '/api/v1/asset/groups';

export interface CreateSatelliteGroupRequest {
  parentGroupId?: string | null;
  groupName: string;
  sortOrder?: number;
  description?: string | null;
}

export interface UpdateSatelliteGroupRequest {
  parentGroupId?: string | null;
  groupName: string;
  sortOrder: number;
  description?: string | null;
}

export const groupsApi = {
  getTree: () => request<SatelliteGroupNode[]>('get', BASE),
  getOne: (groupId: string) => request<SatelliteGroupNode>('get', `${BASE}/${groupId}`),
  create: (body: CreateSatelliteGroupRequest) => request<SatelliteGroupNode>('post', BASE, body),
  update: (groupId: string, body: UpdateSatelliteGroupRequest) =>
    request<SatelliteGroupNode>('put', `${BASE}/${groupId}`, body),
  remove: (groupId: string) => request<{ deleted: boolean }>('delete', `${BASE}/${groupId}`),
  listMembers: (groupId: string, includeDescendants = false) =>
    request<SatelliteGroupMemberDto[]>('get', `${BASE}/${groupId}/members`, undefined, {
      includeDescendants
    }),
  addMembers: (groupId: string, satellites: { tasookNo: string; satelliteNo: string }[]) =>
    request<{ added: number }>('post', `${BASE}/${groupId}/members`, { satellites }),
  removeMember: (groupId: string, tasookNo: string, satelliteNo: string) =>
    request<{ removed: boolean }>(
      'delete',
      `${BASE}/${groupId}/members/${encodeURIComponent(tasookNo)}/${encodeURIComponent(satelliteNo)}`
    )
};

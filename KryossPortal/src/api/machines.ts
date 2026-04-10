import { useQuery } from '@tanstack/react-query';
import { apiFetch } from './client';
import type { Machine, RunDetail } from '../types';
import type { AssessmentRunSummary } from '../types';

interface MachineListResponse {
  total: number;
  page: number;
  pageSize: number;
  items: Machine[];
}

export interface MachineDetail extends Machine {
  osBuild: string | null;
  tpmPresent: boolean | null;
  tpmVersion: string | null;
  secureBoot: boolean | null;
  bitlocker: boolean | null;
  ipAddress: string | null;
  macAddress: string | null;
  manufacturer: string | null;
  model: string | null;
  serialNumber: string | null;
  assessmentHistory: AssessmentRunSummary[];
}

export function useMachines(params: {
  organizationId?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}) {
  const qs = new URLSearchParams();
  if (params.organizationId) qs.set('organizationId', params.organizationId);
  if (params.search) qs.set('search', params.search);
  if (params.page) qs.set('page', String(params.page));
  if (params.pageSize) qs.set('pageSize', String(params.pageSize));
  return useQuery({
    queryKey: ['machines', params],
    queryFn: () => apiFetch<MachineListResponse>(`/v2/machines?${qs}`),
    enabled: !!params.organizationId,
  });
}

export function useMachine(id: string | undefined) {
  return useQuery({
    queryKey: ['machine', id],
    queryFn: () => apiFetch<MachineDetail>(`/v2/machines/${id}`),
    enabled: !!id,
  });
}

export function useRunDetail(
  machineId: string | undefined,
  runId: string | undefined,
) {
  return useQuery({
    queryKey: ['run', machineId, runId],
    queryFn: () =>
      apiFetch<RunDetail>(`/v2/machines/${machineId}/runs/${runId}`),
    enabled: !!machineId && !!runId,
  });
}

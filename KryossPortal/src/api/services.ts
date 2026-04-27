import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiFetch } from './client';

export interface ServiceItem {
  name: string;
  displayName: string | null;
  status: string;
  startupType: string;
  updatedAt: string;
  isProtected: boolean;
  isPriority: boolean;
}

interface ServicesResponse {
  total: number;
  items: ServiceItem[];
}

export function useMachineServices(machineId: string | undefined) {
  return useQuery({
    queryKey: ['machine-services', machineId],
    queryFn: () => apiFetch<ServicesResponse>(`/v2/machines/${machineId}/services`),
    enabled: !!machineId,
    refetchInterval: 30_000,
  });
}

export function useServiceAction(machineId: string | undefined) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ serviceName, action }: { serviceName: string; action: 'start' | 'stop' | 'restart' }) =>
      apiFetch<{ taskId: number }>(`/v2/machines/${machineId}/services/${encodeURIComponent(serviceName)}/action`, {
        method: 'POST',
        body: JSON.stringify({ action }),
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['machine-services', machineId] });
      qc.invalidateQueries({ queryKey: ['machine-activity', machineId] });
    },
  });
}

export function useTogglePriority(machineId: string | undefined) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ serviceName, enable }: { serviceName: string; enable: boolean }) =>
      apiFetch<{ priorityServices: string[] }>(`/v2/machines/${machineId}/priority-services`, {
        method: 'PATCH',
        body: JSON.stringify({ serviceName, enable }),
      }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['machine-services', machineId] }),
  });
}

export interface ActivityItem {
  timestamp: string;
  type: string;
  severity: string;
  action: string;
  actorEmail: string | null;
  source: string;
  serviceName: string | null;
  errorMessage: string | null;
}

interface ActivityResponse {
  total: number;
  page: number;
  pageSize: number;
  items: ActivityItem[];
}

export function useMachineActivity(machineId: string | undefined, page = 1) {
  return useQuery({
    queryKey: ['machine-activity', machineId, page],
    queryFn: () => apiFetch<ActivityResponse>(`/v2/machines/${machineId}/activity?page=${page}&pageSize=50`),
    enabled: !!machineId,
    refetchInterval: 30_000,
  });
}

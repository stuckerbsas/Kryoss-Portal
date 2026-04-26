import { useQuery } from '@tanstack/react-query';
import { apiFetch, qs } from './client';
import type { FleetDashboard } from '../types';

export function useFleetDashboard(organizationId?: string) {
  return useQuery({
    queryKey: ['dashboard', 'fleet', organizationId],
    queryFn: () => apiFetch<FleetDashboard>(`/v2/dashboard/fleet${qs({ organizationId })}`),
    enabled: !!organizationId,
    refetchInterval: 60_000,
  });
}

export function useTrend(params: {
  machineId?: string;
  organizationId?: string;
  months?: number;
}) {
  const qs = new URLSearchParams();
  if (params.machineId) qs.set('machineId', params.machineId);
  if (params.organizationId) qs.set('organizationId', params.organizationId);
  if (params.months) qs.set('months', String(params.months));
  return useQuery({
    queryKey: ['dashboard', 'trend', params],
    queryFn: () =>
      apiFetch<{ months: number; dataPoints: unknown[] }>(
        `/v2/dashboard/trend?${qs}`,
      ),
    enabled: !!(params.machineId || params.organizationId),
  });
}

import { useQuery } from '@tanstack/react-query';
import { apiFetch } from './client';

// -- Machine-level threats --

export interface ThreatResult {
  threatName: string;
  category: string;
  severity: string;
  vector: string;
  detail: string | null;
  detectedAt: string;
}

export interface MachineThreatsResponse {
  total: number;
  critical: number;
  high: number;
  medium: number;
  low: number;
  threats: ThreatResult[];
}

export function useMachineThreats(machineId: string | undefined) {
  return useQuery({
    queryKey: ['machine-threats', machineId],
    queryFn: () =>
      apiFetch<MachineThreatsResponse>(`/v2/threats?machineId=${machineId}`),
    enabled: !!machineId,
  });
}

// -- Org-level threats summary --

export interface TopThreat {
  threatName: string;
  severity: string;
  category: string;
  machineCount: number;
  machines: string[];
}

export interface OrgThreatsSummary {
  totalMachines: number;
  machinesWithThreats: number;
  criticalThreats: number;
  highThreats: number;
  topThreats: TopThreat[];
}

export function useOrgThreats(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['org-threats', organizationId],
    queryFn: () =>
      apiFetch<OrgThreatsSummary>(
        `/v2/threats/org?organizationId=${organizationId}`,
      ),
    enabled: !!organizationId,
  });
}

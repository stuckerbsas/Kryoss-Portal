import { useQuery } from '@tanstack/react-query';
import { apiFetch } from './client';

// ── Machine-level ports ──

export interface PortResult {
  port: number;
  protocol: string;
  status: string;
  service: string | null;
  risk: string | null;
  scannedAt: string;
}

export interface MachinePortsResponse {
  totalOpen: number;
  critical: number;
  high: number;
  medium: number;
  ports: PortResult[];
}

export function useMachinePorts(machineId: string | undefined) {
  return useQuery({
    queryKey: ['machine-ports', machineId],
    queryFn: () =>
      apiFetch<MachinePortsResponse>(`/v2/ports?machineId=${machineId}`),
    enabled: !!machineId,
  });
}

// ── Org-level ports summary ──

export interface TopRiskyPort {
  port: number;
  service: string | null;
  risk: string;
  machineCount: number;
  machines: string[];
}

export interface OrgPortsSummary {
  totalMachines: number;
  machinesWithRiskyPorts: number;
  criticalPorts: number;
  highRiskPorts: number;
  topRiskyPorts: TopRiskyPort[];
}

export function useOrgPorts(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['org-ports', organizationId],
    queryFn: () =>
      apiFetch<OrgPortsSummary>(
        `/v2/ports/org?organizationId=${organizationId}`,
      ),
    enabled: !!organizationId,
  });
}

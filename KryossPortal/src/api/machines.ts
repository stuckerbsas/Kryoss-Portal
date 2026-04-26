import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiFetch } from './client';
import type { Machine, RunDetail } from '../types';
import type { AssessmentRunSummary } from '../types';
import { isGuid } from '@/lib/slugify';

interface MachineListResponse {
  total: number;
  page: number;
  pageSize: number;
  items: Machine[];
}

export interface MachineDetail extends Machine {
  agentId: string;
  osBuild: string | null;
  // Hardware
  manufacturer: string | null;
  model: string | null;
  serialNumber: string | null;
  cpuCores: number | null;
  diskSizeGb: number | null;
  diskFreeGb: number | null;
  // Security
  tpmPresent: boolean | null;
  tpmVersion: string | null;
  secureBoot: boolean | null;
  bitlocker: boolean | null;
  // Network
  macAddress: string | null;
  // Identity
  domainName: string | null;
  // Lifecycle
  systemAgeDays: number | null;
  lastBootAt: string | null;
  // Disks
  disks: { driveLetter: string; label: string | null; diskType: string | null; totalGb: number | null; freeGb: number | null; fileSystem: string | null; }[];
  // Local administrators (per-machine, from agent)
  localAdmins: { name: string; type: string; source: string; }[] | null;
  // Agent config (portal-controlled)
  agentConfig: AgentConfig;
  // History
  assessmentHistory: AssessmentRunSummary[];
}

export interface AgentConfig {
  complianceIntervalHours: number;
  snmpIntervalMinutes: number;
  enableNetworkScan: boolean;
  networkScanIntervalHours: number;
  enablePassiveDiscovery: boolean;
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
    refetchInterval: 30_000,
  });
}

/**
 * Accepts either a GUID or a hostname. If hostname, resolves via the machine list.
 */
export function useMachine(idOrHostname: string | undefined, organizationId?: string) {
  const isMachineId = idOrHostname ? isGuid(idOrHostname) : false;

  // If hostname, resolve from machine list
  const { data: machineList } = useMachines({
    organizationId,
    pageSize: 100,
  });
  const resolvedId = isMachineId
    ? idOrHostname
    : machineList?.items.find(
        (m) => m.hostname.toLowerCase() === idOrHostname?.toLowerCase(),
      )?.id;

  return useQuery({
    queryKey: ['machine', resolvedId],
    queryFn: () => apiFetch<MachineDetail>(`/v2/machines/${resolvedId}`),
    enabled: !!resolvedId,
    refetchInterval: 30_000,
  });
}

/** Get the resolved GUID for a machine hostname/id. */
export function useResolvedMachineId(
  idOrHostname: string | undefined,
  organizationId?: string,
): string | undefined {
  const isMachineId = idOrHostname ? isGuid(idOrHostname) : false;
  const { data: machineList } = useMachines({
    organizationId,
    pageSize: 100,
  });

  if (isMachineId) return idOrHostname;
  return machineList?.items.find(
    (m) => m.hostname.toLowerCase() === idOrHostname?.toLowerCase(),
  )?.id;
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

export interface SoftwareItem {
  name: string;
  version: string | null;
  publisher: string | null;
}

interface SoftwareListResponse {
  total: number;
  items: SoftwareItem[];
}

export function useMachineSoftware(machineId: string | undefined) {
  return useQuery({
    queryKey: ['machine-software', machineId],
    queryFn: () =>
      apiFetch<SoftwareListResponse>(`/v2/machines/${machineId}/software`),
    enabled: !!machineId,
  });
}

export function useUpdateAgentConfig(machineId: string | undefined) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (config: Partial<AgentConfig>) =>
      apiFetch<AgentConfig>(`/v2/machines/${machineId}/agent-config`, {
        method: 'PATCH',
        body: JSON.stringify(config),
      }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['machine', machineId] }),
  });
}

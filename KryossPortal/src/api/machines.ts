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
  aadTenantId: string | null;
  // Lifecycle
  systemAgeDays: number | null;
  lastBootAt: string | null;
  // Disks
  disks: { driveLetter: string; label: string | null; diskType: string | null; totalGb: number | null; freeGb: number | null; fileSystem: string | null; }[];
  // Local administrators (per-machine, from agent)
  localAdmins: { name: string; type: string; source: string; }[] | null;
  // Agent config (portal-controlled)
  agentConfig: AgentConfig;
  // Scan trigger
  scanPending: boolean;
  scanRequestedAt: string | null;
  // Service mode
  agentMode: string | null;
  agentUptimeSeconds: number | null;
  lastHeartbeatAt: string | null;
  // Loop status (v2.8.0)
  loopStatus: Record<string, LoopStatus> | null;
  lastErrorAt: string | null;
  lastErrorPhase: string | null;
  lastErrorMsg: string | null;
  // History
  assessmentHistory: AssessmentRunSummary[];
}

export interface LoopStatus {
  lastRunAt: string | null;
  lastDurationMs: number | null;
  lastError: string | null;
  state: 'idle' | 'running' | 'error';
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

export function useMachine(idOrHostname: string | undefined) {
  const isId = idOrHostname ? isGuid(idOrHostname) : false;
  const url = isId
    ? `/v2/machines/${idOrHostname}`
    : `/v2/machines/by-hostname/${encodeURIComponent(idOrHostname!)}`;

  return useQuery({
    queryKey: ['machine', idOrHostname],
    queryFn: () => apiFetch<MachineDetail>(url),
    enabled: !!idOrHostname,
    refetchInterval: 30_000,
  });
}

export function useResolvedMachineId(
  idOrHostname: string | undefined,
): string | undefined {
  if (idOrHostname && isGuid(idOrHostname)) return idOrHostname;
  const { data } = useMachine(idOrHostname);
  return data?.id;
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
  category: string;
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

export function useTriggerScan(machineId: string | undefined) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () =>
      apiFetch<{ queued: boolean; message: string }>(`/v2/machines/${machineId}/trigger-scan`, {
        method: 'POST',
      }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['machine', machineId] }),
  });
}

export interface LocalAdminEntry {
  name: string;
  type: string;
  source: string;
  machineCount: number;
  machines: { machineId: string; hostname: string }[];
}

interface LocalAdminsResponse {
  totalAccounts: number;
  totalEntries: number;
  admins: LocalAdminEntry[];
}

export function useOrgLocalAdmins(orgId: string | undefined) {
  return useQuery({
    queryKey: ['local-admins', orgId],
    queryFn: () => apiFetch<LocalAdminsResponse>(`/v2/local-admins?organizationId=${orgId}`),
    enabled: !!orgId,
  });
}

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiFetch } from './client';

export interface InfraAssessmentSite {
  id: string;
  siteName: string;
  location: string | null;
  siteType: string;
  deviceCount: number;
  userCount: number;
  connectivityType: string | null;
}

export interface InfraAssessmentDevice {
  id: string;
  siteId: string | null;
  hostname: string | null;
  deviceType: string;
  vendor: string | null;
  model: string | null;
  role: string | null;
  ipAddress: string | null;
  os: string | null;
  firmware: string | null;
  serialNumber: string | null;
}

export interface InfraAssessmentConnectivity {
  id: string;
  siteAId: string;
  siteBId: string;
  linkType: string;
  bandwidthMbps: number | null;
  latencyMs: number | null;
  uptimePct: number | null;
  costMonthlyUsd: number | null;
}

export interface InfraAssessmentCapacity {
  id: string;
  deviceId: string | null;
  metricKey: string;
  currentValue: number | null;
  peakValue: number | null;
  threshold: number | null;
  trendDirection: string;
}

export interface InfraAssessmentFinding {
  id: string;
  area: string;
  service: string | null;
  feature: string | null;
  status: string;
  priority: string;
  observation: string | null;
  recommendation: string | null;
  linkText: string | null;
  linkUrl: string | null;
}

export interface InfraAssessmentScan {
  id: string;
  organizationId: string;
  status: string;
  scope: string | null;
  overallHealth: number | null;
  siteCount: number;
  deviceCount: number;
  findingCount: number;
  startedAt: string | null;
  completedAt: string | null;
  createdAt: string;
  sites: InfraAssessmentSite[];
  devices: InfraAssessmentDevice[];
  connectivity: InfraAssessmentConnectivity[];
  capacity: InfraAssessmentCapacity[];
  findings: InfraAssessmentFinding[];
}

export interface InfraAssessmentHistoryItem {
  id: string;
  status: string;
  overallHealth: number | null;
  siteCount: number;
  deviceCount: number;
  findingCount: number;
  startedAt: string | null;
  completedAt: string | null;
  createdAt: string;
}

export function useInfraAssessment(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['infra-assessment', organizationId],
    queryFn: () =>
      apiFetch<{ scan: InfraAssessmentScan | null }>(
        `/v2/infra-assessment?organizationId=${organizationId}`,
      ).then((r) => r.scan),
    enabled: !!organizationId,
  });
}

export function useInfraAssessmentHistory(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['infra-assessment-history', organizationId],
    queryFn: () =>
      apiFetch<InfraAssessmentHistoryItem[]>(
        `/v2/infra-assessment/history?organizationId=${organizationId}`,
      ),
    enabled: !!organizationId,
  });
}

export function useStartInfraAssessmentScan(organizationId: string | undefined) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (scope?: string) =>
      apiFetch<{ scanId: string; status: string }>(
        '/v2/infra-assessment/scan',
        {
          method: 'POST',
          body: JSON.stringify({ organizationId, scope }),
        },
      ),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['infra-assessment', organizationId] });
      qc.invalidateQueries({ queryKey: ['infra-assessment-history', organizationId] });
      qc.invalidateQueries({ queryKey: ['hypervisor-scan', organizationId] });
    },
  });
}

// ── IA-1: Hypervisor Inventory ──

export interface HypervisorConfig {
  id: string;
  platform: string;
  displayName: string | null;
  hostUrl: string;
  username: string | null;
  verifySsl: boolean;
  isActive: boolean;
  lastTestedAt: string | null;
  lastTestOk: boolean | null;
  lastError: string | null;
  createdAt: string;
}

export interface HypervisorHost {
  id: string;
  platform: string;
  hostFqdn: string;
  version: string | null;
  clusterName: string | null;
  cpuCoresTotal: number | null;
  ramGbTotal: number | null;
  storageGbTotal: number | null;
  cpuUsagePct: number | null;
  ramUsagePct: number | null;
  vmCount: number;
  vmRunning: number;
  haEnabled: boolean | null;
  powerState: string;
}

export interface HypervisorVm {
  id: string;
  hypervisorId: string;
  vmName: string;
  os: string | null;
  powerState: string;
  cpuCores: number | null;
  ramGb: number | null;
  diskGb: number | null;
  cpuAvgPct: number | null;
  ramAvgPct: number | null;
  diskUsedPct: number | null;
  snapshotCount: number;
  oldestSnapshotDays: number | null;
  lastBackup: string | null;
  ipAddress: string | null;
  toolsStatus: string | null;
  isTemplate: boolean;
  isIdle: boolean;
  notes: string | null;
}

export interface HypervisorFinding {
  area: string;
  feature: string;
  status: string;
  priority: string;
  observation: string;
  recommendation: string;
}

export interface HypervisorScanResult {
  scanId: string;
  scannedAt: string;
  hosts: HypervisorHost[];
  vms: HypervisorVm[];
  findings: HypervisorFinding[];
}

export function useHypervisorConfigs(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['hypervisor-configs', organizationId],
    queryFn: () =>
      apiFetch<HypervisorConfig[]>(
        `/v2/infra-assessment/hypervisor-configs?organizationId=${organizationId}`,
      ),
    enabled: !!organizationId,
  });
}

export function useCreateHypervisorConfig() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: {
      organizationId: string;
      platform: string;
      displayName?: string;
      hostUrl: string;
      username?: string;
      password?: string;
      apiToken?: string;
      verifySsl?: boolean;
    }) =>
      apiFetch<{ id: string }>('/v2/infra-assessment/hypervisor-configs', {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    onSuccess: (_data, variables) => {
      qc.invalidateQueries({ queryKey: ['hypervisor-configs', variables.organizationId] });
    },
  });
}

export function useDeleteHypervisorConfig() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ configId }: { configId: string; organizationId: string | undefined }) =>
      apiFetch(`/v2/infra-assessment/hypervisor-configs/${configId}`, { method: 'DELETE' }),
    onSuccess: (_data, variables) => {
      qc.invalidateQueries({ queryKey: ['hypervisor-configs', variables.organizationId] });
    },
  });
}

export function useTestHypervisorConfig() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ configId }: { configId: string; organizationId: string | undefined }) =>
      apiFetch<{ success: boolean; error?: string }>(
        `/v2/infra-assessment/hypervisor-configs/${configId}/test`,
        { method: 'POST' },
      ),
    onSuccess: (_data, variables) => {
      qc.invalidateQueries({ queryKey: ['hypervisor-configs', variables.organizationId] });
    },
  });
}

export function useHypervisorScanResults(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['hypervisor-scan', organizationId],
    queryFn: () =>
      apiFetch<HypervisorScanResult>(
        `/v2/infra-assessment/hypervisors?organizationId=${organizationId}`,
      ),
    enabled: !!organizationId,
  });
}

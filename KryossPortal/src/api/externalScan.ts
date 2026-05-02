import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiFetch } from './client';

// ── Types ──

export interface ExternalScanResultItem {
  ipAddress: string;
  port: number;
  protocol: string;
  status: string;
  service: string | null;
  risk: string | null;
  banner: string | null;
  serviceName: string | null;
  serviceVersion: string | null;
}

export interface ExternalScanFindingItem {
  severity: string;
  title: string;
  description: string | null;
  remediation: string | null;
  port: number | null;
  category: string | null;
}

export interface DiscoveredTarget {
  value: string;
  source: string;
  label: string | null;
}

export interface AutoScanResult {
  scanned: number;
  scanIds: Array<{
    scanId: string | null;
    target: string;
    source: string;
    error?: string;
  }>;
}

export interface ExternalScanDetail {
  id: string;
  target: string;
  status: string;
  startedAt: string | null;
  completedAt: string | null;
  overallGrade: string | null;
  categoryScores: string | null;
  results: ExternalScanResultItem[];
  findings: ExternalScanFindingItem[];
}

export interface ScanHistoryItem {
  id: string;
  target: string;
  status: string;
  startedAt: string | null;
  completedAt: string | null;
  overallGrade: string | null;
  openPorts: number;
  criticalFindings: number;
  highFindings: number;
  totalFindings: number;
}

// ── Hooks ──

export function useExternalScanDetail(scanId: string | undefined) {
  return useQuery({
    queryKey: ['external-scan', scanId],
    queryFn: () => apiFetch<ExternalScanDetail>(`/v2/external-scan/${scanId}`),
    enabled: !!scanId,
  });
}

export function useExternalScanHistory(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['external-scan-history', organizationId],
    queryFn: async () => {
      const data = await apiFetch<{ scans: ScanHistoryItem[] }>(
        `/v2/external-scan/history?organizationId=${organizationId}`,
      );
      return data.scans;
    },
    enabled: !!organizationId,
  });
}

export function useStartExternalScan() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (params: { organizationId: string; target: string }) =>
      apiFetch<{ scanId: string }>('/v2/external-scan', {
        method: 'POST',
        body: JSON.stringify(params),
      }),
    onSuccess: (_data, variables) => {
      qc.invalidateQueries({ queryKey: ['external-scan-history', variables.organizationId] });
    },
  });
}

export function useExternalScanTargets(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['external-scan-targets', organizationId],
    queryFn: () =>
      apiFetch<{ targets: DiscoveredTarget[] }>(
        `/v2/external-scan/targets?organizationId=${organizationId}`,
      ),
    enabled: !!organizationId,
  });
}

export function useAutoExternalScan() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (params: { organizationId: string }) =>
      apiFetch<AutoScanResult>('/v2/external-scan/auto', {
        method: 'POST',
        body: JSON.stringify(params),
      }),
    onSuccess: (_data, variables) => {
      qc.invalidateQueries({ queryKey: ['external-scan-history', variables.organizationId] });
    },
  });
}

// ── Consent ──

export function useEnableExternalScanConsent() {
  return useMutation({
    mutationFn: (params: { organizationId: string }) =>
      apiFetch(`/v2/organizations/${params.organizationId}/external-scan`, {
        method: 'PATCH',
        body: JSON.stringify({ enabled: true }),
      }),
  });
}

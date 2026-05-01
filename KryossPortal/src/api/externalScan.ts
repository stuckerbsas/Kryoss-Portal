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
  detail: string | null;
}

export interface ExternalScanSummary {
  totalIps: number;
  totalOpen: number;
  criticalPorts: number;
  highPorts: number;
  mediumPorts: number;
  infoPorts: number;
}

export interface ExternalScanFindingItem {
  severity: string;
  title: string;
  description: string | null;
  remediation: string | null;
  port: number | null;
}

export interface ExternalScanDetail {
  id: string;
  target: string;
  status: string;
  startedAt: string | null;
  completedAt: string | null;
  createdAt: string;
  summary: ExternalScanSummary;
  results: ExternalScanResultItem[];
  findings?: ExternalScanFindingItem[];
}

export interface StartScanResponse {
  scanId: string;
  status: string;
  target: string;
  ipsFound: number;
  openPorts: number;
  criticalPorts: number;
  highPorts: number;
  startedAt: string | null;
  completedAt: string | null;
}

export interface ScanHistoryItem {
  id: string;
  target: string;
  status: string;
  startedAt: string | null;
  completedAt: string | null;
  createdAt: string;
  openPorts: number;
  criticalPorts: number;
}

// ── Hooks ──

export function useLatestExternalScan(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['external-scan-latest', organizationId],
    queryFn: () =>
      apiFetch<ExternalScanDetail>(
        `/v2/external-scan?organizationId=${organizationId}`,
      ),
    enabled: !!organizationId,
  });
}

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
    queryFn: () =>
      apiFetch<ScanHistoryItem[]>(
        `/v2/external-scan/history?organizationId=${organizationId}`,
      ),
    enabled: !!organizationId,
  });
}

export function useStartExternalScan() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (params: { organizationId: string; target: string }) =>
      apiFetch<StartScanResponse>('/v2/external-scan', {
        method: 'POST',
        body: JSON.stringify(params),
      }),
    onSuccess: (_data, variables) => {
      qc.invalidateQueries({
        queryKey: ['external-scan-latest', variables.organizationId],
      });
      qc.invalidateQueries({
        queryKey: ['external-scan-history', variables.organizationId],
      });
    },
  });
}

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiFetch } from './client';

// ── Types ──

export interface M365Finding {
  checkId: string;
  name: string;
  category: string;
  severity: string;
  status: string; // pass | fail | warn | info
  finding: string | null;
  actualValue: string | null;
  scannedAt: string;
}

export interface M365Summary {
  totalChecks: number;
  passed: number;
  failed: number;
  warned: number;
  info: number;
}

export interface M365ScanResponse {
  connected: boolean;
  tenantId?: string;
  tenantName?: string;
  status?: string;
  lastScanAt?: string;
  createdAt?: string;
  summary?: M365Summary;
  findings?: M365Finding[];
}

export interface M365ConnectPayload {
  organizationId: string;
  tenantId: string;
  tenantName?: string;
  clientId: string;
  clientSecret: string;
}

export interface M365ConnectResponse {
  tenantId: string;
  totalChecks: number;
  checksPassed: number;
  checksFailed: number;
  checksWarned: number;
}

export interface M365ScanResult {
  totalChecks: number;
  checksPassed: number;
  checksFailed: number;
  checksWarned: number;
  scannedAt: string;
}

// ── Hooks ──

export function useM365(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['m365', organizationId],
    queryFn: () =>
      apiFetch<M365ScanResponse>(`/v2/m365?organizationId=${organizationId}`),
    enabled: !!organizationId,
  });
}

export function useM365Connect() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: M365ConnectPayload) =>
      apiFetch<M365ConnectResponse>('/v2/m365/connect', {
        method: 'POST',
        body: JSON.stringify(payload),
      }),
    onSuccess: (_data, variables) => {
      qc.invalidateQueries({ queryKey: ['m365', variables.organizationId] });
    },
  });
}

export function useM365Scan() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (organizationId: string) =>
      apiFetch<M365ScanResult>('/v2/m365/scan', {
        method: 'POST',
        body: JSON.stringify({ organizationId }),
      }),
    onSuccess: (_data, organizationId) => {
      qc.invalidateQueries({ queryKey: ['m365', organizationId] });
    },
  });
}

export function useM365Disconnect() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (organizationId: string) =>
      apiFetch<{ message: string }>('/v2/m365/disconnect', {
        method: 'DELETE',
        body: JSON.stringify({ organizationId }),
      }),
    onSuccess: (_data, organizationId) => {
      qc.invalidateQueries({ queryKey: ['m365', organizationId] });
    },
  });
}

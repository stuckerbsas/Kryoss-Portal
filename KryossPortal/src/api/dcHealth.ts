import { useQuery } from '@tanstack/react-query';
import { apiFetch } from './client';

export interface DcReplPartner {
  partnerHostname: string | null;
  direction: string | null;
  namingContext: string | null;
  lastSuccess: string | null;
  lastAttempt: string | null;
  failureCount: number;
  lastError: string | null;
  transport: string | null;
}

export interface DcHealthSnapshot {
  id: string;
  machineId: string;
  machineHostname: string | null;
  schemaVersion: number | null;
  schemaVersionLabel: string | null;
  forestLevel: string | null;
  domainLevel: string | null;
  forestName: string | null;
  domainName: string | null;
  schemaMaster: string | null;
  domainNamingMaster: string | null;
  pdcEmulator: string | null;
  ridMaster: string | null;
  infrastructureMaster: string | null;
  fsmoSinglePoint: boolean;
  replPartnerCount: number;
  replFailureCount: number;
  lastSuccessfulRepl: string | null;
  siteCount: number;
  subnetCount: number;
  dcCount: number;
  gcCount: number;
  scannedAt: string;
  scannedBy: string | null;
  replicationPartners: DcReplPartner[];
}

export interface DcHealthHistoryEntry {
  id: string;
  scannedAt: string;
  scannedBy: string | null;
  replFailureCount: number;
  dcCount: number;
}

export interface DcHealthResponse {
  latest: DcHealthSnapshot | null;
  history: DcHealthHistoryEntry[];
}

export function useDcHealth(orgId: string | undefined) {
  return useQuery({
    queryKey: ['dc-health', orgId],
    queryFn: () =>
      apiFetch<DcHealthResponse>(`/v2/dc-health?organizationId=${orgId}`),
    enabled: !!orgId,
  });
}

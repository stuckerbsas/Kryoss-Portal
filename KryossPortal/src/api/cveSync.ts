import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiFetch } from './client';

export interface CveSyncStatus {
  lastSyncAt: string | null;
  lastSyncStatus: string | null;
  lastSyncError: string | null;
  totalCves: number;
  knownExploited: number;
  productMappings: number;
  totalFindings: number;
  softwareWithCpe: number;
  totalSoftware: number;
  recentSyncs: {
    syncedAt: string;
    status: string;
    entriesAdded: number;
    entriesUpdated: number;
    source: string;
    errorMessage: string | null;
  }[];
}

export interface CveProduct {
  vendor: string;
  product: string | null;
  softwareCount: number;
  cveCount: number;
  openFindings: number;
}

export interface CveProductsResponse {
  products: CveProduct[];
  unmappedCount: number;
}

export function useCveSyncStatus() {
  return useQuery({
    queryKey: ['cve-sync-status'],
    queryFn: () => apiFetch<CveSyncStatus>('/v2/cve-sync/status'),
  });
}

export function useCveSyncProducts() {
  return useQuery({
    queryKey: ['cve-sync-products'],
    queryFn: () => apiFetch<CveProductsResponse>('/v2/cve-sync/products'),
  });
}

export function useCveSyncManual() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (full: boolean) =>
      apiFetch('/v2/cve-sync', {
        method: 'POST',
        body: full ? undefined : undefined,
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['cve-sync-status'] });
      qc.invalidateQueries({ queryKey: ['cve-sync-products'] });
    },
  });
}

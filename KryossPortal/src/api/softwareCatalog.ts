import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiFetch } from './client';

export interface SoftwareCatalogItem {
  id: number;
  name: string;
  publisher: string | null;
  machineCount: number;
  licenseType: string;
  confidence: number;
}

interface SoftwareCatalogResponse {
  stats: { total: number; commercial: number; free: number; unknown: number };
  items: SoftwareCatalogItem[];
  page: number;
  pageSize: number;
  totalPages: number;
}

export function useSoftwareCatalog(search: string, licenseType: string, page: number) {
  return useQuery({
    queryKey: ['software-catalog', search, licenseType, page],
    queryFn: () => {
      const params = new URLSearchParams();
      if (search) params.set('search', search);
      if (licenseType) params.set('licenseType', licenseType);
      params.set('page', String(page));
      params.set('pageSize', '50');
      return apiFetch<SoftwareCatalogResponse>(`/v2/software-catalog?${params}`);
    },
  });
}

export function useReclassify() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () =>
      apiFetch<{ status: string }>(
        '/v2/software-catalog/reclassify',
        { method: 'POST' },
      ),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['software-catalog'] });
    },
  });
}

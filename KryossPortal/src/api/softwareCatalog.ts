import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiFetch } from './client';

export interface SoftwareCatalogItem {
  id: number;
  name: string;
  publisher: string | null;
  category: string | null;
  isCommercial: boolean;
  cpeVendor: string | null;
  cpeProduct: string | null;
  machineCount: number;
}

interface SoftwareCatalogResponse {
  stats: { total: number; licensed: number; uncategorized: number };
  items: SoftwareCatalogItem[];
  page: number;
  pageSize: number;
  totalPages: number;
}

export function useSoftwareCatalog(search: string, category: string, page: number) {
  return useQuery({
    queryKey: ['software-catalog', search, category, page],
    queryFn: () => {
      const params = new URLSearchParams();
      if (search) params.set('search', search);
      if (category) params.set('category', category);
      params.set('page', String(page));
      params.set('pageSize', '50');
      return apiFetch<SoftwareCatalogResponse>(`/v2/software-catalog?${params}`);
    },
  });
}

export function useUpdateCategory() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, category }: { id: number; category: string | null }) =>
      apiFetch(`/v2/software-catalog/${id}`, {
        method: 'PATCH',
        body: JSON.stringify({ category }),
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['software-catalog'] });
    },
  });
}

export function useAutoDetect() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () =>
      apiFetch<{ processed: number; categorized: number; markedLicensed: number }>(
        '/v2/software-catalog/auto-detect',
        { method: 'POST' },
      ),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['software-catalog'] });
    },
  });
}

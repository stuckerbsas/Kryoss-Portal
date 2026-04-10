import { useQuery } from '@tanstack/react-query';
import { apiFetch } from './client';

export interface CatalogControl {
  id: number;
  controlId: string;
  frameworks: { code: string; frameworkRef: string }[];
}

interface CatalogResponse {
  total: number;
  items: CatalogControl[];
}

export function useCatalogControls() {
  return useQuery({
    queryKey: ['catalog-controls'],
    queryFn: () => apiFetch<CatalogResponse>('/v2/catalog/controls'),
    staleTime: 5 * 60 * 1000,
  });
}

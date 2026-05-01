import { useQuery } from '@tanstack/react-query';
import { apiFetch, qs } from './client';

export interface AdObjectItem {
  id: number;
  samAccountName: string;
  distinguishedName: string | null;
  displayName: string | null;
  objectType: 'user' | 'computer';
  enabled: boolean;
  lastLogon: string | null;
  whenCreated: string | null;
  memberOf: string | null;
  organizationalUnit: string | null;
  updatedAt: string;
}

export interface AdObjectsResponse {
  items: AdObjectItem[];
  total: number;
  page: number;
  pageSize: number;
}

export function useAdObjects(
  organizationId: string | undefined,
  params: { type?: string; search?: string; page?: number; pageSize?: number },
) {
  return useQuery({
    queryKey: ['ad-objects', organizationId, params],
    queryFn: () =>
      apiFetch<AdObjectsResponse>(
        `/v2/ad-objects${qs({
          organizationId,
          type: params.type,
          search: params.search || undefined,
          page: params.page,
          pageSize: params.pageSize,
        })}`,
      ),
    enabled: !!organizationId,
  });
}

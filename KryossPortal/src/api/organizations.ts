import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiFetch } from './client';
import type { Organization } from '../types';

interface OrgListResponse {
  total: number;
  page: number;
  pageSize: number;
  items: Organization[];
}

export function useOrganizations(params?: { status?: string; search?: string }) {
  const qs = new URLSearchParams();
  if (params?.status) qs.set('status', params.status);
  if (params?.search) qs.set('search', params.search);
  const query = qs.toString() ? `?${qs}` : '';

  return useQuery({
    queryKey: ['organizations', params],
    queryFn: () => apiFetch<OrgListResponse>(`/v2/organizations${query}`),
  });
}

export function useOrganization(id: string | undefined) {
  return useQuery({
    queryKey: ['organization', id],
    queryFn: () => apiFetch<Organization>(`/v2/organizations/${id}`),
    enabled: !!id,
  });
}

export function useCreateOrganization() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: {
      name: string;
      legalName?: string;
      taxId?: string;
      status?: string;
      brandId?: number;
      entraTenantId?: string;
    }) =>
      apiFetch<Organization>('/v2/organizations', {
        method: 'POST',
        body: JSON.stringify(data),
      }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['organizations'] }),
  });
}

export function useUpdateOrganization() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      id,
      ...data
    }: {
      id: string;
      name?: string;
      legalName?: string;
      taxId?: string;
      status?: string;
      brandId?: number;
    }) =>
      apiFetch<Organization>(`/v2/organizations/${id}`, {
        method: 'PATCH',
        body: JSON.stringify(data),
      }),
    onSuccess: (_, vars) => {
      qc.invalidateQueries({ queryKey: ['organizations'] });
      qc.invalidateQueries({ queryKey: ['organization', vars.id] });
    },
  });
}

export function useDeleteOrganization() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiFetch(`/v2/organizations/${id}`, { method: 'DELETE' }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['organizations'] }),
  });
}

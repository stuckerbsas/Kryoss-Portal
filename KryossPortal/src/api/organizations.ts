import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiFetch } from './client';
import type { Organization } from '../types';
import { isGuid, slugify } from '@/lib/slugify';

interface OrgListResponse {
  total: number;
  page: number;
  pageSize: number;
  items: Organization[];
}

export function useOrganizations(
  params?: { status?: string; search?: string },
  options?: { enabled?: boolean },
) {
  const qs = new URLSearchParams();
  if (params?.status) qs.set('status', params.status);
  if (params?.search) qs.set('search', params.search);
  const query = qs.toString() ? `?${qs}` : '';

  return useQuery({
    queryKey: ['organizations', params],
    queryFn: () => apiFetch<OrgListResponse>(`/v2/organizations${query}`),
    enabled: options?.enabled ?? true,
  });
}

/**
 * Accepts either a GUID or a slug. If slug, resolves via the org list cache.
 * If already a GUID, skips the org list fetch entirely (no waterfall).
 */
export function useOrganization(idOrSlug: string | undefined) {
  const isId = idOrSlug ? isGuid(idOrSlug) : false;

  const { data: orgList } = useOrganizations(undefined, { enabled: !isId });
  const resolvedId = isId
    ? idOrSlug
    : orgList?.items.find((o) => slugify(o.name) === idOrSlug)?.id;

  return useQuery({
    queryKey: ['organization', resolvedId],
    queryFn: () => apiFetch<Organization>(`/v2/organizations/${resolvedId}`),
    enabled: !!resolvedId,
  });
}

/** Get the resolved GUID for an org slug/id. */
export function useResolvedOrgId(idOrSlug: string | undefined): string | undefined {
  const isId = idOrSlug ? isGuid(idOrSlug) : false;
  const { data: orgList } = useOrganizations(undefined, { enabled: !isId });

  if (isId) return idOrSlug;
  return orgList?.items.find((o) => slugify(o.name) === idOrSlug)?.id;
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

// ── v1.5.1: Protocol Audit toggle ──────────────────────────────────────────

export interface ProtocolAuditStatus {
  id: string;
  protocolAuditEnabled: boolean;
  protocolAuditEnabledAt?: string | null;
  protocolAuditEnabledBy?: string | null;
}

export function useToggleProtocolAudit() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, enabled }: { id: string; enabled: boolean }) =>
      apiFetch<ProtocolAuditStatus>(`/v2/organizations/${id}/protocol-audit`, {
        method: 'PATCH',
        body: JSON.stringify({ enabled }),
      }),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ['organization', vars.id] });
      qc.invalidateQueries({ queryKey: ['organizations'] });
    },
  });
}

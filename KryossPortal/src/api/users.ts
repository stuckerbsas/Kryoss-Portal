import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiFetch } from './client';

export interface UserListItem {
  id: string;
  email: string;
  displayName: string;
  authSource: string;
  lastLoginAt: string | null;
  phone: string | null;
  jobTitle: string | null;
  role: { id: number; code: string; name: string };
  franchise: { id: string; name: string } | null;
  organization: { id: string; name: string } | null;
  createdAt: string;
}

export interface RoleItem {
  id: number;
  code: string;
  name: string;
  isSystem: boolean;
  permissionCount: number;
}

interface UserListResponse {
  total: number;
  page: number;
  pageSize: number;
  items: UserListItem[];
}

export function useUsers(params?: { search?: string; role?: string; page?: number }) {
  const qs = new URLSearchParams();
  if (params?.search) qs.set('search', params.search);
  if (params?.role) qs.set('role', params.role);
  if (params?.page) qs.set('page', String(params.page));
  qs.set('pageSize', '50');
  return useQuery({
    queryKey: ['users', params],
    queryFn: () => apiFetch<UserListResponse>(`/v2/users?${qs}`),
  });
}

export function useRoles() {
  return useQuery({
    queryKey: ['roles'],
    queryFn: () => apiFetch<RoleItem[]>('/v2/roles'),
    staleTime: Infinity,
  });
}

export function useUpdateUser() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...body }: { id: string; roleCode?: string; organizationId?: string; franchiseId?: string; displayName?: string }) =>
      apiFetch(`/v2/users/${id}`, { method: 'PATCH', body: JSON.stringify(body) }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['users'] }),
  });
}

export function useDeleteUser() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiFetch(`/v2/users/${id}`, { method: 'DELETE' }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['users'] }),
  });
}

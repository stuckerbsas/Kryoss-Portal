import { useQuery } from '@tanstack/react-query';
import { apiFetch } from './client';
import type { MeResponse } from '../types';

// DEV_MOCK: Return mock super_admin when backend is unavailable
const DEV_MOCK: MeResponse | null = import.meta.env.DEV ? {
  id: 'dev-mock-user',
  email: 'dev@kryoss.local',
  displayName: 'Dev User',
  authSource: 'mock',
  lastLoginAt: new Date().toISOString(),
  role: { id: 1, code: 'super_admin', name: 'Super Administrator', isSystem: true },
  franchise: { id: 'dev-franchise', name: 'TeamLogic IT' },
  organization: null,
  permissions: [
    'organizations:read', 'organizations:create', 'organizations:edit', 'organizations:delete', 'organizations:export',
    'machines:read', 'machines:create', 'machines:edit', 'machines:delete', 'machines:export',
    'assessment:read', 'assessment:create', 'assessment:edit', 'assessment:delete', 'assessment:export',
    'enrollment:read', 'enrollment:create', 'enrollment:edit', 'enrollment:delete', 'enrollment:export',
    'reports:read', 'reports:create', 'reports:edit', 'reports:delete', 'reports:export',
    'controls:read', 'controls:create', 'controls:edit', 'controls:delete', 'controls:export',
    'recycle_bin:read', 'recycle_bin:restore',
    'admin:read', 'admin:edit',
  ],
} : null;

export function useMe() {
  return useQuery({
    queryKey: ['me'],
    queryFn: async () => {
      try {
        const controller = new AbortController();
        const timeout = setTimeout(() => controller.abort(), 3000);
        const result = await apiFetch<MeResponse>('/v2/me', { signal: controller.signal });
        clearTimeout(timeout);
        return result;
      } catch {
        if (DEV_MOCK) return DEV_MOCK;
        throw new Error('Authentication required');
      }
    },
    staleTime: Infinity,
    retry: false,
  });
}

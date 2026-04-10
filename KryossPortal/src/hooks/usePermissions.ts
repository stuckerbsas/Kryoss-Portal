import { useMe } from '../api/me';

// DEV_MOCK: In dev mode without backend, grant all permissions
const DEV_MOCK = import.meta.env.DEV;

export function usePermissions() {
  const { data: me } = useMe();
  const permissions = me?.permissions;

  if (DEV_MOCK && !permissions) {
    // Mock: super_admin with all permissions when backend is unavailable
    return {
      has: (_slug: string) => true,
      hasAny: (_slugs: string[]) => true,
      hasAll: (_slugs: string[]) => true,
      role: 'super_admin',
      isSuperAdmin: true,
    };
  }

  return {
    has: (slug: string) => permissions?.includes(slug) ?? false,
    hasAny: (slugs: string[]) => slugs.some((s) => permissions?.includes(s)),
    hasAll: (slugs: string[]) => slugs.every((s) => permissions?.includes(s)),
    role: me?.role.code,
    isSuperAdmin: me?.role.code === 'super_admin',
  };
}

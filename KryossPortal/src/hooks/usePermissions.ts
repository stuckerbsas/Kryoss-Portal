import { useMe } from '../api/me';

export function usePermissions() {
  const { data: me } = useMe();
  return {
    has: (slug: string) => me?.permissions.includes(slug) ?? false,
    hasAny: (slugs: string[]) => slugs.some((s) => me?.permissions.includes(s)),
    hasAll: (slugs: string[]) => slugs.every((s) => me?.permissions.includes(s)),
    role: me?.role.code,
    isSuperAdmin: me?.role.code === 'super_admin',
  };
}

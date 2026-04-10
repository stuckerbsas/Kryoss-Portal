import type { ReactNode } from 'react';
import { Navigate } from 'react-router-dom';
import { usePermissions } from '@/hooks/usePermissions';

export function RequirePermission({ slug, children }: { slug: string; children: ReactNode }) {
  const { has } = usePermissions();
  if (!has(slug)) return <Navigate to="/forbidden" replace />;
  return <>{children}</>;
}

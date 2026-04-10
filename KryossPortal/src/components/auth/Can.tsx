import type { ReactNode } from 'react';
import { usePermissions } from '@/hooks/usePermissions';

interface CanProps {
  permission?: string;
  anyOf?: string[];
  allOf?: string[];
  fallback?: ReactNode;
  children: ReactNode;
}

export function Can({ permission, anyOf, allOf, fallback = null, children }: CanProps) {
  const { has, hasAny, hasAll } = usePermissions();
  const allowed =
    (permission && has(permission)) ||
    (anyOf && hasAny(anyOf)) ||
    (allOf && hasAll(allOf));
  return allowed ? <>{children}</> : <>{fallback}</>;
}

import { Navigate } from 'react-router-dom';
import { useMe } from '@/api/me';

export function SmartRedirect() {
  const { data: me } = useMe();
  const isClient = me?.role.code === 'client_admin' || me?.role.code === 'client_viewer';

  if (isClient && me?.organization) {
    return <Navigate to={`/organizations/${me.organization.id}`} replace />;
  }

  return <Navigate to="/organizations" replace />;
}

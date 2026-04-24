import { Outlet } from 'react-router-dom';
import { useMe } from '@/api/me';
import { Topbar } from './Topbar';
import { Breadcrumbs } from './Breadcrumbs';

export function ClientLayout() {
  const { data: me } = useMe();

  if (!me?.organization) {
    return (
      <div className="h-screen flex flex-col">
        <Topbar />
        <div className="flex-1 flex items-center justify-center">
          <div className="text-center space-y-2">
            <h2 className="text-lg font-semibold">No Organization Assigned</h2>
            <p className="text-sm text-muted-foreground">
              Contact your administrator to get access to an organization.
            </p>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="h-screen flex flex-col">
      <Topbar />
      <main className="flex-1 overflow-y-auto p-4 sm:p-6">
        <Breadcrumbs />
        <Outlet />
      </main>
    </div>
  );
}

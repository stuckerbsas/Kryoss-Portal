import { createBrowserRouter, Navigate } from 'react-router-dom';
import { AppShell } from './components/layout/AppShell';
import { RequirePermission } from './components/auth/RequirePermission';
import { ForbiddenPage } from './pages/ForbiddenPage';
import { OrganizationsPage } from './pages/OrganizationsPage';

function PlaceholderPage({ title }: { title: string }) {
  return (
    <div className="flex items-center justify-center h-64 text-muted-foreground">
      Coming soon: {title}
    </div>
  );
}

export const router = createBrowserRouter([
  {
    element: <AppShell />,
    children: [
      { index: true, element: <Navigate to="/organizations" replace /> },
      {
        path: 'organizations',
        handle: { crumb: () => 'Organizations' },
        children: [
          {
            index: true,
            element: (
              <RequirePermission slug="organizations:read">
                <OrganizationsPage />
              </RequirePermission>
            ),
          },
          {
            path: ':orgId',
            handle: { crumb: (_: unknown, p: Record<string, string>) => p.orgId?.slice(0, 8) + '...' },
            children: [
              { index: true, element: <PlaceholderPage title="Org Detail" /> },
              { path: 'fleet', element: <PlaceholderPage title="Fleet" /> },
              { path: 'enrollment', element: <PlaceholderPage title="Enrollment" /> },
              { path: 'reports', element: <PlaceholderPage title="Reports" /> },
              {
                path: 'machines/:machineId',
                handle: { crumb: (_: unknown, p: Record<string, string>) => p.machineId?.slice(0, 8) + '...' },
                children: [
                  { index: true, element: <PlaceholderPage title="Machine Detail" /> },
                  { path: 'runs/:runId', element: <PlaceholderPage title="Run Detail" /> },
                ],
              },
            ],
          },
        ],
      },
      {
        path: 'recycle-bin',
        handle: { crumb: () => 'Recycle Bin' },
        element: (
          <RequirePermission slug="recycle_bin:read">
            <PlaceholderPage title="Recycle Bin" />
          </RequirePermission>
        ),
      },
      { path: 'forbidden', element: <ForbiddenPage /> },
      { path: '*', element: <Navigate to="/organizations" replace /> },
    ],
  },
]);

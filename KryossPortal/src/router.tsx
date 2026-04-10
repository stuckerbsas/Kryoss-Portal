import { createBrowserRouter, Navigate } from 'react-router-dom';
import { AppShell } from './components/layout/AppShell';
import { RequirePermission } from './components/auth/RequirePermission';
import { ForbiddenPage } from './pages/ForbiddenPage';
import { OrganizationsPage } from './pages/OrganizationsPage';
import { OrgDetailPage } from './pages/OrgDetailPage';
import { OverviewTab } from './components/org-detail/OverviewTab';
import { FleetTab } from './components/org-detail/FleetTab';
import { EnrollmentTab } from './components/org-detail/EnrollmentTab';
import { ReportsTab } from './components/org-detail/ReportsTab';
import { MachineDetailPage } from './pages/MachineDetailPage';
import { RunDetailPage } from './pages/RunDetailPage';
import { RecycleBinPage } from './pages/RecycleBinPage';

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
            element: <OrgDetailPage />,
            handle: { crumb: (_: unknown, p: Record<string, string>) => p.orgId?.slice(0, 8) + '...' },
            children: [
              { index: true, element: <OverviewTab /> },
              { path: 'fleet', element: <FleetTab /> },
              { path: 'enrollment', element: <EnrollmentTab /> },
              { path: 'reports', element: <ReportsTab /> },
              {
                path: 'machines/:machineId',
                handle: { crumb: (_: unknown, p: Record<string, string>) => p.machineId?.slice(0, 8) + '...' },
                children: [
                  { index: true, element: <MachineDetailPage /> },
                  {
                    path: 'runs/:runId',
                    handle: { crumb: (_: unknown, p: Record<string, string>) => 'Run ' + (p.runId?.slice(0, 8) ?? '') },
                    element: <RunDetailPage />,
                  },
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
            <RecycleBinPage />
          </RequirePermission>
        ),
      },
      { path: 'forbidden', element: <ForbiddenPage /> },
      { path: '*', element: <Navigate to="/organizations" replace /> },
    ],
  },
]);

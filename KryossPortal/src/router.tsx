import { createBrowserRouter, Navigate } from 'react-router-dom';
import { AppShell } from './components/layout/AppShell';
import { RequirePermission } from './components/auth/RequirePermission';
import { SmartRedirect } from './components/auth/SmartRedirect';
import { ForbiddenPage } from './pages/ForbiddenPage';
import { OrganizationsPage } from './pages/OrganizationsPage';
import { OrgDetailPage } from './pages/OrgDetailPage';
import { OverviewTab } from './components/org-detail/OverviewTab';
import { DevicesTab } from './components/org-detail/DevicesTab';
import { ReportsTab } from './components/org-detail/ReportsTab';
import { SecurityTab } from './components/org-detail/SecurityTab';
import { NetworkTab } from './components/org-detail/NetworkTab';
import { CloudAssessmentTab } from './components/org-detail/CloudAssessmentTab';
import { MachineDetailPage } from './pages/MachineDetailPage';
import { RunDetailPage } from './pages/RunDetailPage';
import { RecycleBinPage } from './pages/RecycleBinPage';
import { ActivityLogPage } from './pages/ActivityLogPage';
import { UsersPage } from './pages/UsersPage';
import { ProfilePage } from './pages/ProfilePage';
import { CveDatabasePage } from './pages/CveDatabasePage';
import { SoftwareCatalogPage } from './pages/SoftwareCatalogPage';

export const router = createBrowserRouter([
  {
    element: <AppShell />,
    children: [
      { index: true, element: <SmartRedirect /> },
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
            handle: { crumb: (_: unknown, p: Record<string, string>) => p.orgId },
            children: [
              { index: true, element: <OverviewTab /> },
              { path: 'devices', element: <DevicesTab /> },
              { path: 'enrollment', element: <Navigate to=".." replace /> },
              { path: 'reports', element: <ReportsTab /> },
              { path: 'security', element: <SecurityTab /> },
              { path: 'network', element: <NetworkTab /> },
              { path: 'cloud-assessment', element: <CloudAssessmentTab /> },
              // Legacy redirects
              { path: 'fleet', element: <Navigate to="../devices" replace /> },
              { path: 'hardware', element: <Navigate to="../devices?section=hardware" replace /> },
              { path: 'software-inventory', element: <Navigate to="../devices?section=software" replace /> },
              { path: 'hygiene', element: <Navigate to="../security?section=active-directory" replace /> },
              { path: 'active-directory', element: <Navigate to="../security?section=active-directory" replace /> },
              { path: 'dc-health', element: <Navigate to="../security?section=active-directory" replace /> },
              { path: 'threats', element: <Navigate to="../security?section=threats" replace /> },
              { path: 'cve', element: <Navigate to="../security?section=cve" replace /> },
              { path: 'patches', element: <Navigate to="../security?section=patches" replace /> },
              { path: 'm365', element: <Navigate to="../cloud-assessment" replace /> },
              { path: 'ports', element: <Navigate to="../network?section=ports" replace /> },
              { path: 'external-scan', element: <Navigate to="../network?section=external-scan" replace /> },
              { path: 'network-diagnostics', element: <Navigate to="../network?section=diagnostics" replace /> },
              { path: 'snmp', element: <Navigate to="../network?section=snmp" replace /> },
              { path: 'network-sites', element: <Navigate to="../network?section=sites" replace /> },
              { path: 'protocol-usage', element: <Navigate to="../network?section=protocol-usage" replace /> },
              {
                path: 'machines/:machineId',
                handle: { crumb: (_: unknown, p: Record<string, string>) => p.machineId },
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
        path: 'users',
        handle: { crumb: () => 'Users' },
        element: (
          <RequirePermission slug="admin:read">
            <UsersPage />
          </RequirePermission>
        ),
      },
      {
        path: 'activity-log',
        handle: { crumb: () => 'Activity Log' },
        element: (
          <RequirePermission slug="admin:read">
            <ActivityLogPage />
          </RequirePermission>
        ),
      },
      {
        path: 'cve-database',
        handle: { crumb: () => 'CVE Database' },
        element: (
          <RequirePermission slug="admin:read">
            <CveDatabasePage />
          </RequirePermission>
        ),
      },
      {
        path: 'software-catalog',
        handle: { crumb: () => 'Software Catalog' },
        element: (
          <RequirePermission slug="admin:read">
            <SoftwareCatalogPage />
          </RequirePermission>
        ),
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
      {
        path: 'profile',
        handle: { crumb: () => 'Profile' },
        element: <ProfilePage />,
      },
      { path: 'forbidden', element: <ForbiddenPage /> },
      { path: '*', element: <SmartRedirect /> },
    ],
  },
]);

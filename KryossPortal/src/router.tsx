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
import { HygieneTab } from './components/org-detail/HygieneTab';
import { PortsTab } from './components/org-detail/PortsTab';
import { ThreatsTab } from './components/org-detail/ThreatsTab';
import { ExternalScanTab } from './components/org-detail/ExternalScanTab';
import { CloudAssessmentTab } from './components/org-detail/CloudAssessmentTab';
import { ProtocolUsageTab } from './components/org-detail/ProtocolUsageTab';
import { NetworkDiagnosticsTab } from './components/org-detail/NetworkDiagnosticsTab';
import { SnmpTab } from './components/org-detail/SnmpTab';
import { InfraAssessmentTab } from './components/org-detail/InfraAssessmentTab';
import { NetworkSitesTab } from './components/org-detail/NetworkSitesTab';
import { HardwareInventoryTab } from './components/org-detail/HardwareInventoryTab';
import { SoftwareInventoryTab } from './components/org-detail/SoftwareInventoryTab';
import { MachineDetailPage } from './pages/MachineDetailPage';
import { RunDetailPage } from './pages/RunDetailPage';
import { RecycleBinPage } from './pages/RecycleBinPage';
import { ActivityLogPage } from './pages/ActivityLogPage';

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
            handle: { crumb: (_: unknown, p: Record<string, string>) => p.orgId },
            children: [
              { index: true, element: <OverviewTab /> },
              { path: 'fleet', element: <FleetTab /> },
              { path: 'enrollment', element: <EnrollmentTab /> },
              { path: 'hardware', element: <HardwareInventoryTab /> },
              { path: 'software-inventory', element: <SoftwareInventoryTab /> },
              { path: 'reports', element: <ReportsTab /> },
              { path: 'hygiene', element: <HygieneTab /> },
              { path: 'ports', element: <PortsTab /> },
              { path: 'threats', element: <ThreatsTab /> },
              { path: 'external-scan', element: <ExternalScanTab /> },
              { path: 'm365', element: <Navigate to="../cloud-assessment" replace /> },
              { path: 'cloud-assessment', element: <CloudAssessmentTab /> },
              { path: 'network-diagnostics', element: <NetworkDiagnosticsTab /> },
              { path: 'snmp', element: <SnmpTab /> },
              { path: 'network-sites', element: <NetworkSitesTab /> },
              { path: 'infra-assessment', element: <InfraAssessmentTab /> },
              { path: 'protocol-usage', element: <ProtocolUsageTab /> },
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
        path: 'activity-log',
        handle: { crumb: () => 'Activity Log' },
        element: (
          <RequirePermission slug="admin:read">
            <ActivityLogPage />
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
      { path: 'forbidden', element: <ForbiddenPage /> },
      { path: '*', element: <Navigate to="/organizations" replace /> },
    ],
  },
]);

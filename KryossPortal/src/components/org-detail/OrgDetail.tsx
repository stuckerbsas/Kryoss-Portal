import { useState } from 'react';
import { useParams, NavLink, Outlet } from 'react-router-dom';
import { Building2, Download, Loader2, Pencil, Trash2 } from 'lucide-react';
import { toast } from 'sonner';
import { useOrganization } from '@/api/organizations';
import { API_BASE, loginRequest } from '@/auth/msalConfig';
import { msalInstance } from '@/auth/msalInstance';
import { usePermissions } from '@/hooks/usePermissions';
import { Can } from '@/components/auth/Can';
import { StatusBadge } from '@/components/shared/StatusBadge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { slugify } from '@/lib/slugify';

const tabs = [
  { label: 'Overview', to: '', end: true, permission: 'organizations:read' },
  { label: 'Fleet', to: 'fleet', end: false, permission: 'machines:read' },
  {
    label: 'Enrollment',
    to: 'enrollment',
    end: false,
    permission: 'enrollment:create',
  },
  { label: 'Reports', to: 'reports', end: false, permission: 'reports:read' },
  { label: 'AD Hygiene', to: 'hygiene', end: false, permission: 'assessment:read' },
] as const;

export function OrgDetail() {
  const { orgId: orgSlug } = useParams<{ orgId: string }>();
  const { data: org, isLoading } = useOrganization(orgSlug);
  const { has } = usePermissions();
  const [downloading, setDownloading] = useState(false);

  const handleDownloadAgent = async () => {
    if (!org) return;
    setDownloading(true);
    try {
      const accounts = msalInstance.getAllAccounts();
      const tokenRes = await msalInstance.acquireTokenSilent({
        ...loginRequest,
        account: accounts[0],
      });
      const res = await fetch(
        `${API_BASE}/v2/agent/download?orgId=${org.id}`,
        { headers: { Authorization: `Bearer ${tokenRes.accessToken}` } }
      );
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const blob = await res.blob();
      const a = document.createElement('a');
      a.href = URL.createObjectURL(blob);
      a.download = `KryossAgent-${slugify(org.name)}.zip`;
      a.click();
      URL.revokeObjectURL(a.href);
      toast.success('Agent downloaded');
    } catch (err: any) {
      toast.error(`Download failed: ${err.message}`);
    } finally {
      setDownloading(false);
    }
  };

  if (isLoading) {
    return (
      <div className="space-y-6">
        <div className="flex items-center gap-4">
          <Skeleton className="h-8 w-64" />
          <Skeleton className="h-6 w-16" />
        </div>
        <Skeleton className="h-10 w-full max-w-md" />
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }

  if (!org) {
    return (
      <div className="text-muted-foreground text-center py-16">
        Organization not found
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <Building2 className="h-6 w-6 text-muted-foreground" />
          <h1 className="text-2xl font-bold tracking-tight">{org.name}</h1>
          <StatusBadge status={org.status} />
        </div>
        <div className="flex items-center gap-2">
          <Can permission="assessment:export">
            <Button
              variant="outline"
              size="sm"
              disabled={downloading}
              onClick={handleDownloadAgent}
            >
              {downloading ? (
                <Loader2 className="mr-1.5 h-4 w-4 animate-spin" />
              ) : (
                <Download className="mr-1.5 h-4 w-4" />
              )}
              Download Agent
            </Button>
          </Can>
          <Can permission="organizations:update">
            <Button variant="outline" size="sm">
              <Pencil className="mr-1.5 h-4 w-4" />
              Edit
            </Button>
          </Can>
          <Can permission="organizations:delete">
            <Button variant="outline" size="sm" className="text-destructive">
              <Trash2 className="mr-1.5 h-4 w-4" />
              Delete
            </Button>
          </Can>
        </div>
      </div>

      {/* Tab navigation */}
      <nav className="flex border-b">
        {tabs.map(
          (tab) =>
            has(tab.permission) && (
              <NavLink
                key={tab.to}
                to={tab.to}
                end={tab.end}
                className={({ isActive }) =>
                  [
                    'px-4 py-2 text-sm font-medium border-b-2 -mb-px transition-colors',
                    isActive
                      ? 'border-primary text-primary'
                      : 'border-transparent text-muted-foreground hover:text-foreground hover:border-muted-foreground/40',
                  ].join(' ')
                }
              >
                {tab.label}
              </NavLink>
            ),
        )}
      </nav>

      {/* Tab content */}
      <Outlet />
    </div>
  );
}

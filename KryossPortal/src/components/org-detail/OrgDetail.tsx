import { useState } from 'react';
import { NavLink, Outlet } from 'react-router-dom';
import { Building2, Check, Copy, Download, Key, Loader2, Pencil, RefreshCw, Trash2 } from 'lucide-react';
import { toast } from 'sonner';
import { useOrganization } from '@/api/organizations';
import { useEnrollmentCodes, useCreateEnrollmentCode } from '@/api/enrollment';
import { useOrgParam } from '@/hooks/useOrgParam';
import { API_BASE, loginRequest } from '@/auth/msalConfig';
import { msalInstance } from '@/auth/msalInstance';
import { usePermissions } from '@/hooks/usePermissions';
import { useMe } from '@/api/me';
import { Can } from '@/components/auth/Can';
import { StatusBadge } from '@/components/shared/StatusBadge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { slugify } from '@/lib/slugify';

const CLIENT_ROLES = ['client_admin', 'client_viewer'];

const tabs = [
  { label: 'Overview', to: '', end: true, permission: 'assessment:read' },
  { label: 'Devices', to: 'devices', end: false, permission: 'machines:read' },
  { label: 'Reports', to: 'reports', end: false, permission: 'reports:read' },
  { label: 'Security', to: 'security', end: false, permission: 'assessment:read' },
  { label: 'Network', to: 'network', end: false, permission: 'machines:read' },
  { label: 'Cloud', to: 'cloud-assessment', end: false, permission: 'assessment:read' },
] as const;

export function OrgDetail() {
  const { orgId, orgSlug } = useOrgParam();
  const { data: org, isLoading } = useOrganization(orgSlug);
  const { has } = usePermissions();
  const { data: me } = useMe();
  const isClient = CLIENT_ROLES.includes(me?.role?.code ?? '');
  const [downloading, setDownloading] = useState(false);
  const [copied, setCopied] = useState(false);
  const { data: codes } = useEnrollmentCodes(orgId);
  const createCode = useCreateEnrollmentCode();
  const [refreshing, setRefreshing] = useState(false);

  const activeCode = codes?.find((c) => !c.isExpired && !c.isUsed);

  const handleCopyCode = async () => {
    if (!activeCode) return;
    await navigator.clipboard.writeText(activeCode.code);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  const handleRefreshCode = async () => {
    if (!orgId) return;
    setRefreshing(true);
    try {
      await createCode.mutateAsync({
        organizationId: orgId,
        label: 'Agent enrollment',
        expiryDays: 30,
        maxUses: 999,
      });
    } finally {
      setRefreshing(false);
    }
  };

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
        {!isClient && (
          <div className="flex items-center gap-3">
            <Can permission="enrollment:create">
              <div className="flex items-center gap-2 rounded-md border px-3 py-1.5 bg-muted/50">
                <Key className="h-4 w-4 text-muted-foreground" />
                {activeCode ? (
                  <>
                    <code className="font-mono text-sm font-semibold tracking-wider select-all">
                      {activeCode.code}
                    </code>
                    <Button variant="ghost" size="icon" className="h-7 w-7" onClick={handleCopyCode}>
                      {copied ? <Check className="h-3.5 w-3.5 text-green-600" /> : <Copy className="h-3.5 w-3.5" />}
                    </Button>
                  </>
                ) : (
                  <span className="text-sm text-muted-foreground">No code</span>
                )}
                <Button
                  variant="ghost"
                  size="icon"
                  className="h-7 w-7"
                  onClick={handleRefreshCode}
                  disabled={refreshing}
                >
                  <RefreshCw className={`h-3.5 w-3.5 ${refreshing ? 'animate-spin' : ''}`} />
                </Button>
              </div>
            </Can>
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
        )}
      </div>

      {/* Tab navigation — hidden for client roles (they use sidebar) */}
      {!isClient && (
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
      )}

      {/* Tab content */}
      <Outlet />
    </div>
  );
}

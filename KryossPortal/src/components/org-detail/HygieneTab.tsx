import { Monitor, UserX, KeyRound, AlertTriangle, Clock, ShieldAlert, Shield, Server, Settings, Info } from 'lucide-react';
import { useHygiene, type HygieneFinding } from '@/api/hygiene';
import { useOrgParam } from '@/hooks/useOrgParam';
import { EmptyState } from '@/components/shared/EmptyState';
import { Badge } from '@/components/ui/badge';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';

function formatDate(dateStr: string): string {
  return new Date(dateStr).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

function statusBadge(status: string) {
  const config: Record<string, { label: string; className: string }> = {
    Dormant: { label: 'Dormant', className: 'bg-red-100 text-red-800' },
    Stale: { label: 'Stale', className: 'bg-amber-100 text-amber-800' },
    Disabled: { label: 'Disabled', className: 'bg-gray-100 text-gray-600' },
    PwdNeverExpires: { label: 'Pwd Never Expires', className: 'bg-red-100 text-red-800' },
    OldPassword: { label: 'Old Password', className: 'bg-amber-100 text-amber-800' },
    PrivilegedAccount: { label: 'AD Privileged', className: 'bg-purple-100 text-purple-800' },
    LocalAdmin: { label: 'Local Admin', className: 'bg-orange-100 text-orange-800' },
    Kerberoastable: { label: 'Kerberoastable', className: 'bg-red-100 text-red-800' },
    UnconstrainedDelegation: { label: 'Unconstrained Deleg.', className: 'bg-red-100 text-red-800' },
    AdminCountResidue: { label: 'Admin Residue', className: 'bg-amber-100 text-amber-800' },
    NoLAPS: { label: 'No LAPS', className: 'bg-amber-100 text-amber-800' },
    OutdatedDomainLevel: { label: 'Outdated', className: 'bg-red-100 text-red-800' },
    DomainLevel: { label: 'Info', className: 'bg-blue-100 text-blue-800' },
    LowLAPSCoverage: { label: 'Low Coverage', className: 'bg-amber-100 text-amber-800' },
    LAPSCoverage: { label: 'Good', className: 'bg-green-100 text-green-800' },
  };
  const c = config[status] ?? { label: status, className: 'bg-gray-100 text-gray-500' };
  return <Badge variant="secondary" className={c.className}>{c.label}</Badge>;
}

function FindingsTable({ findings, title, icon, tooltip }: { findings: HygieneFinding[]; title: string; icon: React.ReactNode; tooltip?: string }) {
  if (findings.length === 0) return null;
  return (
    <Card>
      <CardHeader className="pb-3">
        <CardTitle className="flex items-center gap-2 text-base">
          {icon}
          {title} ({findings.length})
          {tooltip && (
            <span title={tooltip} className="cursor-help">
              <Info className="h-3.5 w-3.5 text-muted-foreground" />
            </span>
          )}
        </CardTitle>
      </CardHeader>
      <CardContent>
        <div className="max-h-80 overflow-y-auto">
          {/* Mobile cards */}
          <div className="space-y-3 sm:hidden">
            {findings.map((f, i) => (
              <div key={i} className="rounded-lg border p-4 space-y-1">
                <div className="flex items-center justify-between gap-2">
                  <span className="font-medium font-mono text-sm truncate">{f.name}</span>
                  {statusBadge(f.status)}
                </div>
                <div className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted-foreground">
                  {f.daysInactive > 0 && <span>{f.daysInactive}d inactive</span>}
                  <span className="truncate">{f.detail ?? '—'}</span>
                </div>
              </div>
            ))}
          </div>
          {/* Desktop table */}
          <div className="hidden sm:block">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Name</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Days</TableHead>
                  <TableHead className="hidden lg:table-cell">Detail</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {findings.map((f, i) => (
                  <TableRow key={i}>
                    <TableCell className="font-medium font-mono text-sm">{f.name}</TableCell>
                    <TableCell>{statusBadge(f.status)}</TableCell>
                    <TableCell className="tabular-nums">{f.daysInactive > 0 ? `${f.daysInactive}d` : '—'}</TableCell>
                    <TableCell className="text-sm text-muted-foreground max-w-xs truncate hidden lg:table-cell">{f.detail ?? '—'}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

export function HygieneTab() {
  const { orgId } = useOrgParam();
  const { data: scan, isLoading } = useHygiene(orgId);

  if (isLoading) {
    return (
      <div className="space-y-4">
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {Array.from({ length: 4 }).map((_, i) => (
            <Skeleton key={i} className="h-24" />
          ))}
        </div>
        <Skeleton className="h-48" />
      </div>
    );
  }

  if (!scan || !scan.id) {
    return (
      <EmptyState
        icon={<ShieldAlert className="h-12 w-12" />}
        title="No AD hygiene data"
        description="Run a network scan with the agent to generate AD hygiene findings."
      />
    );
  }

  const machines = scan.findings?.filter((f) => f.objectType === 'Computer') ?? [];
  const users = scan.findings?.filter((f) => f.objectType === 'User') ?? [];
  const security = scan.findings?.filter((f) => f.objectType === 'Security') ?? [];
  const config = scan.findings?.filter((f) => f.objectType === 'Config') ?? [];

  const dormantMachines = machines.filter((f) => f.status === 'Dormant');
  const staleMachines = machines.filter((f) => f.status === 'Stale');
  const dormantUsers = users.filter((f) => f.status === 'Dormant');
  const staleUsers = users.filter((f) => f.status === 'Stale' || f.status === 'OldPassword');
  const disabledUsers = users.filter((f) => f.status === 'Disabled');
  const pwdNeverExpire = users.filter((f) => f.status === 'PwdNeverExpires');
  const privileged = security.filter((f) => f.status === 'PrivilegedAccount');
  const localAdmins = security.filter((f) => f.status === 'LocalAdmin');
  const kerberoastable = security.filter((f) => f.status === 'Kerberoastable');
  const unconstrainedDeleg = security.filter((f) => f.status === 'UnconstrainedDelegation');
  const adminResidue = security.filter((f) => f.status === 'AdminCountResidue');
  const noLaps = security.filter((f) => f.status === 'NoLAPS');

  const totalFindings = scan.findings?.length ?? 0;

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h3 className="text-lg font-semibold">Active Directory Hygiene</h3>
          <p className="text-sm text-muted-foreground">
            Last scan: {formatDate(scan.scannedAt)} by {scan.scannedBy}
          </p>
        </div>
        <Badge variant={totalFindings > 10 ? 'destructive' : totalFindings > 0 ? 'secondary' : 'outline'}>
          {totalFindings} findings
        </Badge>
      </div>

      {/* Domain config */}
      {config.length > 0 && (
        <div className="flex flex-wrap gap-3">
          {config.map((c, i) => (
            <div key={i} className="flex items-center gap-2 rounded-lg border px-3 py-2 text-sm">
              <Settings className="h-4 w-4 text-muted-foreground" />
              <span>{c.detail}</span>
              {statusBadge(c.status)}
            </div>
          ))}
        </div>
      )}

      {/* KPI cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-5 gap-3">
        <Card>
          <CardHeader className="flex flex-row items-start justify-between pb-1 pt-0 h-12">
            <CardTitle className="text-sm font-medium text-muted-foreground">Dormant Machines</CardTitle>
            <Monitor className="h-4 w-4 text-red-500" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold" style={{ color: scan.dormantMachines > 0 ? '#C0392B' : '#006536' }}>
              {scan.dormantMachines}
            </div>
            <p className="text-xs text-muted-foreground">&gt;60 days without logon</p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex flex-row items-start justify-between pb-1 pt-0 h-12">
            <CardTitle className="text-sm font-medium text-muted-foreground">Dormant Users</CardTitle>
            <UserX className="h-4 w-4 text-red-500" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold" style={{ color: scan.dormantUsers > 0 ? '#C0392B' : '#006536' }}>
              {scan.dormantUsers}
            </div>
            <p className="text-xs text-muted-foreground">&gt;60 days without logon</p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex flex-row items-start justify-between pb-1 pt-0 h-12">
            <CardTitle className="text-sm font-medium text-muted-foreground">Disabled Users</CardTitle>
            <Clock className="h-4 w-4 text-amber-500" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold" style={{ color: scan.disabledUsers > 0 ? '#D97706' : '#006536' }}>
              {scan.disabledUsers}
            </div>
            <p className="text-xs text-muted-foreground">Remove from AD</p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex flex-row items-start justify-between pb-1 pt-0 h-12">
            <CardTitle className="text-sm font-medium text-muted-foreground">Pwd Never Expires</CardTitle>
            <KeyRound className="h-4 w-4 text-red-500" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold" style={{ color: scan.pwdNeverExpire > 0 ? '#C0392B' : '#006536' }}>
              {scan.pwdNeverExpire}
            </div>
            <p className="text-xs text-muted-foreground">Security risk</p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-start justify-between pb-1 pt-0 h-12">
            <CardTitle className="text-sm font-medium text-muted-foreground">AD Privileged</CardTitle>
            <Shield className="h-4 w-4 text-purple-500" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold" style={{ color: privileged.length > 5 ? '#C0392B' : '#006536' }}>
              {privileged.length}
            </div>
            <p className="text-xs text-muted-foreground">Domain/Enterprise/Schema</p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex flex-row items-start justify-between pb-1 pt-0 h-12">
            <CardTitle className="text-sm font-medium text-muted-foreground">Local Admins</CardTitle>
            <Shield className="h-4 w-4 text-orange-500" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold" style={{ color: localAdmins.length > 10 ? '#D97706' : '#006536' }}>
              {localAdmins.length}
            </div>
            <p className="text-xs text-muted-foreground">Builtin Administrators</p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex flex-row items-start justify-between pb-1 pt-0 h-12">
            <CardTitle className="text-sm font-medium text-muted-foreground">Kerberoastable</CardTitle>
            <AlertTriangle className="h-4 w-4 text-red-500" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold" style={{ color: kerberoastable.length > 0 ? '#C0392B' : '#006536' }}>
              {kerberoastable.length}
            </div>
            <p className="text-xs text-muted-foreground">Accounts with SPN</p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex flex-row items-start justify-between pb-1 pt-0 h-12">
            <CardTitle className="text-sm font-medium text-muted-foreground">No LAPS</CardTitle>
            <Server className="h-4 w-4 text-amber-500" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold" style={{ color: noLaps.length > 0 ? '#D97706' : '#006536' }}>
              {noLaps.length}
            </div>
            <p className="text-xs text-muted-foreground">Machines unmanaged</p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex flex-row items-start justify-between pb-1 pt-0 h-12">
            <CardTitle className="text-sm font-medium text-muted-foreground">Delegation</CardTitle>
            <ShieldAlert className="h-4 w-4 text-red-500" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold" style={{ color: unconstrainedDeleg.length > 0 ? '#C0392B' : '#006536' }}>
              {unconstrainedDeleg.length}
            </div>
            <p className="text-xs text-muted-foreground">Unconstrained</p>
          </CardContent>
        </Card>
      </div>

      {/* Finding tables */}
      <FindingsTable
        findings={privileged}
        title="AD Privileged Accounts (Domain Admins / Enterprise Admins / Schema Admins)"
        icon={<Shield className="h-4 w-4 text-purple-500" />}
      />
      <FindingsTable
        findings={localAdmins}
        title="Local Administrators (Builtin Administrators Group)"
        icon={<Shield className="h-4 w-4 text-orange-500" />}
      />
      <FindingsTable
        findings={kerberoastable}
        title="Kerberoastable Accounts (vulnerable to offline cracking)"
        icon={<AlertTriangle className="h-4 w-4 text-red-500" />}
      />
      <FindingsTable
        findings={unconstrainedDeleg}
        title="Unconstrained Delegation"
        icon={<ShieldAlert className="h-4 w-4 text-red-500" />}
        tooltip="These computers can impersonate any user to any service in the domain. If an attacker compromises one, they can access anything. Restrict to Constrained Delegation or Resource-Based Constrained Delegation."
      />
      <FindingsTable
        findings={adminResidue}
        title="AdminCount Residual"
        icon={<Shield className="h-4 w-4 text-amber-500" />}
        tooltip="These accounts were once in a privileged group (Domain/Enterprise/Schema Admins). AD set the adminCount flag but never cleared it after removal, leaving stale elevated ACL permissions on the object. Clean up with 'AdminSDHolder' reset."
      />
      <FindingsTable
        findings={noLaps}
        title="No LAPS (shared local admin password)"
        icon={<Server className="h-4 w-4 text-amber-500" />}
      />
      <FindingsTable
        findings={dormantMachines}
        title="Dormant Machines (>60 days)"
        icon={<AlertTriangle className="h-4 w-4 text-red-500" />}
      />
      <FindingsTable
        findings={staleMachines}
        title="Stale Machines (30-60 days)"
        icon={<Clock className="h-4 w-4 text-amber-500" />}
      />
      <FindingsTable
        findings={dormantUsers}
        title="Dormant Users (>60 days)"
        icon={<UserX className="h-4 w-4 text-red-500" />}
      />
      <FindingsTable
        findings={staleUsers}
        title="Stale Users / Old Passwords"
        icon={<Clock className="h-4 w-4 text-amber-500" />}
      />
      <FindingsTable
        findings={disabledUsers}
        title="Disabled Users"
        icon={<UserX className="h-4 w-4 text-gray-500" />}
      />
      <FindingsTable
        findings={pwdNeverExpire}
        title="Password Never Expires"
        icon={<KeyRound className="h-4 w-4 text-red-500" />}
      />
    </div>
  );
}

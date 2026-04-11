import { Monitor, UserX, KeyRound, AlertTriangle, Clock, ShieldAlert, Shield, Server, Settings } from 'lucide-react';
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
    PrivilegedAccount: { label: 'Privileged', className: 'bg-purple-100 text-purple-800' },
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

function FindingsTable({ findings, title, icon }: { findings: HygieneFinding[]; title: string; icon: React.ReactNode }) {
  if (findings.length === 0) return null;
  return (
    <Card>
      <CardHeader className="pb-3">
        <CardTitle className="flex items-center gap-2 text-base">
          {icon}
          {title} ({findings.length})
        </CardTitle>
      </CardHeader>
      <CardContent>
        <div className="max-h-80 overflow-y-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Days</TableHead>
                <TableHead>Detail</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {findings.map((f, i) => (
                <TableRow key={i}>
                  <TableCell className="font-medium font-mono text-sm">{f.name}</TableCell>
                  <TableCell>{statusBadge(f.status)}</TableCell>
                  <TableCell className="tabular-nums">{f.daysInactive > 0 ? `${f.daysInactive}d` : '—'}</TableCell>
                  <TableCell className="text-sm text-muted-foreground max-w-xs truncate">{f.detail ?? '—'}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
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
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4 xl:grid-cols-8">
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
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
          <CardHeader className="flex flex-row items-center justify-between pb-2">
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
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Disabled Users</CardTitle>
            <Clock className="h-4 w-4 text-amber-500" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold" style={{ color: scan.disabledUsers > 0 ? '#D97706' : '#006536' }}>
              {scan.disabledUsers}
            </div>
            <p className="text-xs text-muted-foreground">Still in AD, consider removing</p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
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
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Privileged</CardTitle>
            <Shield className="h-4 w-4 text-purple-500" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold">{privileged.length}</div>
            <p className="text-xs text-muted-foreground">Admin accounts</p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
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
          <CardHeader className="flex flex-row items-center justify-between pb-2">
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
          <CardHeader className="flex flex-row items-center justify-between pb-2">
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
        title="Privileged Accounts"
        icon={<Shield className="h-4 w-4 text-purple-500" />}
      />
      <FindingsTable
        findings={kerberoastable}
        title="Kerberoastable Accounts (vulnerable to offline cracking)"
        icon={<AlertTriangle className="h-4 w-4 text-red-500" />}
      />
      <FindingsTable
        findings={unconstrainedDeleg}
        title="Unconstrained Delegation (high risk)"
        icon={<ShieldAlert className="h-4 w-4 text-red-500" />}
      />
      <FindingsTable
        findings={adminResidue}
        title="AdminCount Residual (orphaned admin permissions)"
        icon={<Shield className="h-4 w-4 text-amber-500" />}
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
        title="Disabled Users (still in AD)"
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

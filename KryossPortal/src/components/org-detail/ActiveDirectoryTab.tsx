import {
  AlertTriangle,
  CheckCircle,
  Clock,
  Crown,
  Globe,
  KeyRound,
  Monitor,
  Network,
  Server,
  Settings,
  Shield,
  ShieldAlert,
  UserX,
} from 'lucide-react';
import { useHygiene, type HygieneFinding } from '@/api/hygiene';
import { useDcHealth } from '@/api/dcHealth';
import type { DcReplPartner } from '@/api/dcHealth';
import { useOrgParam } from '@/hooks/useOrgParam';
import { EmptyState } from '@/components/shared/EmptyState';
import { Badge } from '@/components/ui/badge';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';

// ── Shared helpers ──

const SCHEMA_MAP: Record<number, string> = {
  30: 'Server 2003', 31: 'Server 2003 R2', 44: 'Server 2008', 47: 'Server 2008 R2',
  56: 'Server 2012', 69: 'Server 2012 R2', 87: 'Server 2016', 88: 'Server 2019',
  89: 'Server 2022', 90: 'Server 2025',
};
function schemaFriendly(v: number | null): string {
  if (v === null) return 'Unknown';
  return SCHEMA_MAP[v] ?? `v${v}`;
}

function formatDate(dateStr: string): string {
  return new Date(dateStr).toLocaleDateString(undefined, {
    year: 'numeric', month: 'short', day: 'numeric',
    hour: '2-digit', minute: '2-digit',
  });
}

function timeAgo(iso: string | null) {
  if (!iso) return 'Never';
  const diff = Date.now() - new Date(iso).getTime();
  const mins = Math.floor(diff / 60000);
  if (mins < 60) return `${mins}m ago`;
  const hours = Math.floor(mins / 60);
  if (hours < 24) return `${hours}h ago`;
  return `${Math.floor(hours / 24)}d ago`;
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

function FindingsTable({ findings, title, icon, description }: { findings: HygieneFinding[]; title: string; icon: React.ReactNode; description?: string }) {
  if (findings.length === 0) return null;
  return (
    <Card>
      <CardHeader className="pb-3">
        <CardTitle className="flex items-center gap-2 text-base">{icon}{title} ({findings.length})</CardTitle>
        {description && <p className="text-xs text-muted-foreground mt-1">{description}</p>}
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

function replStatusBadge(partner: DcReplPartner) {
  if (partner.failureCount > 0)
    return <Badge className="bg-red-100 text-red-800">Failing ({partner.failureCount})</Badge>;
  if (partner.lastSuccess)
    return <Badge className="bg-green-100 text-green-800">Healthy</Badge>;
  return <Badge className="bg-gray-100 text-gray-500">Unknown</Badge>;
}

// ── Sub-tab: Hygiene ──

function HygieneSection({ scan }: { scan: NonNullable<ReturnType<typeof useHygiene>['data']> }) {
  const machines = scan.findings?.filter((f) => f.objectType === 'Computer') ?? [];
  const users = scan.findings?.filter((f) => f.objectType === 'User') ?? [];
  const config = scan.findings?.filter((f) => f.objectType === 'Config') ?? [];

  const dormantMachines = machines.filter((f) => f.status === 'Dormant');
  const staleMachines = machines.filter((f) => f.status === 'Stale');
  const dormantUsers = users.filter((f) => f.status === 'Dormant');
  const staleUsers = users.filter((f) => f.status === 'Stale' || f.status === 'OldPassword');
  const disabledUsers = users.filter((f) => f.status === 'Disabled');
  const pwdNeverExpire = users.filter((f) => f.status === 'PwdNeverExpires');

  return (
    <div className="space-y-6">
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

      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <Card>
          <CardHeader className="flex flex-row items-start justify-between pb-1 pt-0 h-12">
            <CardTitle className="text-sm font-medium text-muted-foreground">Dormant Machines</CardTitle>
            <Monitor className="h-4 w-4 text-red-500" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold" style={{ color: scan.dormantMachines > 0 ? '#C0392B' : '#006536' }}>{scan.dormantMachines}</div>
            <p className="text-xs text-muted-foreground">&gt;60 days without logon</p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-start justify-between pb-1 pt-0 h-12">
            <CardTitle className="text-sm font-medium text-muted-foreground">Dormant Users</CardTitle>
            <UserX className="h-4 w-4 text-red-500" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold" style={{ color: scan.dormantUsers > 0 ? '#C0392B' : '#006536' }}>{scan.dormantUsers}</div>
            <p className="text-xs text-muted-foreground">&gt;60 days without logon</p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-start justify-between pb-1 pt-0 h-12">
            <CardTitle className="text-sm font-medium text-muted-foreground">Disabled Users</CardTitle>
            <Clock className="h-4 w-4 text-amber-500" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold" style={{ color: scan.disabledUsers > 0 ? '#D97706' : '#006536' }}>{scan.disabledUsers}</div>
            <p className="text-xs text-muted-foreground">Remove from AD</p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-start justify-between pb-1 pt-0 h-12">
            <CardTitle className="text-sm font-medium text-muted-foreground">Pwd Never Expires</CardTitle>
            <KeyRound className="h-4 w-4 text-red-500" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold" style={{ color: scan.pwdNeverExpire > 0 ? '#C0392B' : '#006536' }}>{scan.pwdNeverExpire}</div>
            <p className="text-xs text-muted-foreground">Security risk</p>
          </CardContent>
        </Card>
      </div>

      <FindingsTable findings={dormantMachines} title="Dormant Machines (>60 days)" icon={<AlertTriangle className="h-4 w-4 text-red-500" />} />
      <FindingsTable findings={staleMachines} title="Stale Machines (30-60 days)" icon={<Clock className="h-4 w-4 text-amber-500" />} />
      <FindingsTable findings={dormantUsers} title="Dormant Users (>60 days)" icon={<UserX className="h-4 w-4 text-red-500" />} />
      <FindingsTable findings={staleUsers} title="Stale Users / Old Passwords" icon={<Clock className="h-4 w-4 text-amber-500" />} />
      <FindingsTable findings={disabledUsers} title="Disabled Users" icon={<UserX className="h-4 w-4 text-gray-500" />} />
      <FindingsTable findings={pwdNeverExpire} title="Password Never Expires" icon={<KeyRound className="h-4 w-4 text-red-500" />} />
    </div>
  );
}

// ── Sub-tab: Security ──

function SecuritySection({ scan }: { scan: NonNullable<ReturnType<typeof useHygiene>['data']> }) {
  const security = scan.findings?.filter((f) => f.objectType === 'Security') ?? [];

  const privileged = security.filter((f) => f.status === 'PrivilegedAccount');
  const domainAdmins = privileged.filter((f) => f.detail?.includes('Domain Admins'));
  const enterpriseAdmins = privileged.filter((f) => f.detail?.includes('Enterprise Admins'));
  const schemaAdmins = privileged.filter((f) => f.detail?.includes('Schema Admins'));
  const localAdmins = security.filter((f) => f.status === 'LocalAdmin');
  const kerberoastable = security.filter((f) => f.status === 'Kerberoastable');
  const unconstrainedDeleg = security.filter((f) => f.status === 'UnconstrainedDelegation');
  const adminResidue = security.filter((f) => f.status === 'AdminCountResidue');
  const noLaps = security.filter((f) => f.status === 'NoLAPS');

  if (security.length === 0) {
    return <EmptyState icon={<Shield className="h-12 w-12" />} title="No Security Findings" description="No AD security issues detected." />;
  }

  return (
    <div className="space-y-6">
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-6">
        <Card>
          <CardHeader className="flex flex-row items-start justify-between pb-1 pt-0 h-12">
            <CardTitle className="text-sm font-medium text-muted-foreground">Domain Admins</CardTitle>
            <Shield className="h-4 w-4 text-purple-500" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold" style={{ color: domainAdmins.length > 5 ? '#C0392B' : '#006536' }}>{domainAdmins.length}</div>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-start justify-between pb-1 pt-0 h-12">
            <CardTitle className="text-sm font-medium text-muted-foreground">Enterprise Admins</CardTitle>
            <Crown className="h-4 w-4 text-purple-500" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold" style={{ color: enterpriseAdmins.length > 1 ? '#C0392B' : '#006536' }}>{enterpriseAdmins.length}</div>
            {enterpriseAdmins.length > 1 && <p className="text-xs text-amber-600 font-medium">Best practice: max 1</p>}
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-start justify-between pb-1 pt-0 h-12">
            <CardTitle className="text-sm font-medium text-muted-foreground">Schema Admins</CardTitle>
            <ShieldAlert className="h-4 w-4 text-purple-500" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold" style={{ color: schemaAdmins.length > 1 ? '#C0392B' : '#006536' }}>{schemaAdmins.length}</div>
            {schemaAdmins.length > 1 && <p className="text-xs text-amber-600 font-medium">Best practice: max 1</p>}
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-start justify-between pb-1 pt-0 h-12">
            <CardTitle className="text-sm font-medium text-muted-foreground">Local Admins</CardTitle>
            <Shield className="h-4 w-4 text-orange-500" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold" style={{ color: localAdmins.length > 10 ? '#D97706' : '#006536' }}>{localAdmins.length}</div>
            <p className="text-xs text-muted-foreground">Builtin Administrators</p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-start justify-between pb-1 pt-0 h-12">
            <CardTitle className="text-sm font-medium text-muted-foreground">Kerberoastable</CardTitle>
            <AlertTriangle className="h-4 w-4 text-red-500" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold" style={{ color: kerberoastable.length > 0 ? '#C0392B' : '#006536' }}>{kerberoastable.length}</div>
            <p className="text-xs text-muted-foreground">Accounts with SPN</p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-start justify-between pb-1 pt-0 h-12">
            <CardTitle className="text-sm font-medium text-muted-foreground">No LAPS</CardTitle>
            <Server className="h-4 w-4 text-amber-500" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold" style={{ color: noLaps.length > 0 ? '#D97706' : '#006536' }}>{noLaps.length}</div>
            <p className="text-xs text-muted-foreground">Machines unmanaged</p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-start justify-between pb-1 pt-0 h-12">
            <CardTitle className="text-sm font-medium text-muted-foreground">Delegation</CardTitle>
            <ShieldAlert className="h-4 w-4 text-red-500" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold" style={{ color: unconstrainedDeleg.length > 0 ? '#C0392B' : '#006536' }}>{unconstrainedDeleg.length}</div>
            <p className="text-xs text-muted-foreground">Unconstrained</p>
          </CardContent>
        </Card>
      </div>

      <FindingsTable findings={domainAdmins} title="Domain Admins" icon={<Shield className="h-4 w-4 text-purple-500" />} />
      <FindingsTable findings={enterpriseAdmins} title={`Enterprise Admins${enterpriseAdmins.length > 1 ? ' — Best practice: max 1' : ''}`} icon={<Crown className="h-4 w-4 text-purple-500" />} />
      <FindingsTable findings={schemaAdmins} title={`Schema Admins${schemaAdmins.length > 1 ? ' — Best practice: max 1' : ''}`} icon={<ShieldAlert className="h-4 w-4 text-purple-500" />} />
      <FindingsTable findings={localAdmins} title="Local Administrators (Builtin Administrators Group)" icon={<Shield className="h-4 w-4 text-orange-500" />} />
      <FindingsTable findings={kerberoastable} title="Kerberoastable Accounts (vulnerable to offline cracking)" icon={<AlertTriangle className="h-4 w-4 text-red-500" />} />
      <FindingsTable findings={unconstrainedDeleg} title="Unconstrained Delegation" icon={<ShieldAlert className="h-4 w-4 text-red-500" />} description="This computer can impersonate any user to any service. Attackers who compromise this machine can access anything in the domain." />
      <FindingsTable findings={adminResidue} title="AdminCount Residual" icon={<Shield className="h-4 w-4 text-amber-500" />} description="User was once in a privileged group. AD set the adminCount flag but never cleared it when removed, leaving stale elevated permissions on the object's ACL." />
      <FindingsTable findings={noLaps} title="No LAPS (shared local admin password)" icon={<Server className="h-4 w-4 text-amber-500" />} />
    </div>
  );
}

// ── Sub-tab: Health (DC schema/replication/FSMO) ──

function HealthSection({ orgId }: { orgId: string }) {
  const { data, isLoading } = useDcHealth(orgId);

  if (isLoading) return <div className="space-y-4">{Array.from({ length: 3 }, (_, i) => <Skeleton key={i} className="h-24 w-full" />)}</div>;
  if (!data?.latest)
    return <EmptyState icon={<Server className="h-12 w-12" />} title="No DC Health Data" description="DC health data will appear after a Domain Controller runs a compliance scan." />;

  const s = data.latest;

  return (
    <div className="space-y-6">
      <div className="grid grid-cols-2 gap-4 md:grid-cols-3 lg:grid-cols-6">
        <Card>
          <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
            <CardTitle className="text-sm font-medium">Schema</CardTitle>
            <Globe className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold">{schemaFriendly(s.schemaVersion)}</div>
            <p className="text-xs text-muted-foreground">Schema v{s.schemaVersion ?? '?'}</p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
            <CardTitle className="text-sm font-medium">Domain Controllers</CardTitle>
            <Server className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold">{s.dcCount}</div>
            <p className="text-xs text-muted-foreground">{s.gcCount} Global Catalogs</p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
            <CardTitle className="text-sm font-medium">Sites</CardTitle>
            <Network className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold">{s.siteCount}</div>
            <p className="text-xs text-muted-foreground">{s.subnetCount} subnets</p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
            <CardTitle className="text-sm font-medium">Replication</CardTitle>
            {s.replFailureCount > 0 ? <AlertTriangle className="h-4 w-4 text-red-500" /> : <CheckCircle className="h-4 w-4 text-green-500" />}
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold">{s.replPartnerCount} partners</div>
            <p className="text-xs text-muted-foreground">
              {s.replFailureCount > 0 ? <span className="text-red-600">{s.replFailureCount} failing</span> : 'All healthy'}
            </p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
            <CardTitle className="text-sm font-medium">Forest Level</CardTitle>
            <Shield className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <div className="text-lg font-bold truncate">{s.forestLevel ?? '?'}</div>
            <p className="text-xs text-muted-foreground">Domain: {s.domainLevel ?? '?'}</p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
            <CardTitle className="text-sm font-medium">FSMO</CardTitle>
            <Crown className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <div className="text-lg font-bold">
              {s.fsmoSinglePoint ? <span className="text-amber-600">Single Point</span> : <span className="text-green-600">Distributed</span>}
            </div>
            <p className="text-xs text-muted-foreground">5 roles</p>
          </CardContent>
        </Card>
      </div>

      <Card>
        <CardHeader><CardTitle>Domain Information</CardTitle></CardHeader>
        <CardContent>
          <div className="grid grid-cols-2 gap-4 md:grid-cols-3">
            <div><span className="text-sm text-muted-foreground">Domain</span><p className="font-medium">{s.domainName ?? '—'}</p></div>
            <div><span className="text-sm text-muted-foreground">Forest</span><p className="font-medium">{s.forestName ?? '—'}</p></div>
            <div><span className="text-sm text-muted-foreground">Scanned By</span><p className="font-medium">{s.scannedBy ?? '—'}</p></div>
            <div><span className="text-sm text-muted-foreground">Last Scan</span><p className="font-medium">{timeAgo(s.scannedAt)}</p></div>
            <div><span className="text-sm text-muted-foreground">Last Successful Repl</span><p className="font-medium">{timeAgo(s.lastSuccessfulRepl)}</p></div>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader><CardTitle>FSMO Role Holders</CardTitle></CardHeader>
        <CardContent>
          <Table>
            <TableHeader>
              <TableRow><TableHead>Role</TableHead><TableHead>Holder</TableHead></TableRow>
            </TableHeader>
            <TableBody>
              {([
                ['Schema Master', s.schemaMaster],
                ['Domain Naming Master', s.domainNamingMaster],
                ['PDC Emulator', s.pdcEmulator],
                ['RID Master', s.ridMaster],
                ['Infrastructure Master', s.infrastructureMaster],
              ] as const).map(([role, holder]) => (
                <TableRow key={role}>
                  <TableCell className="font-medium">{role}</TableCell>
                  <TableCell>{holder ?? <span className="text-muted-foreground">Unknown</span>}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          {s.fsmoSinglePoint && (
            <div className="mt-3 flex items-center gap-2 rounded-md bg-amber-50 p-3 text-sm text-amber-800">
              <AlertTriangle className="h-4 w-4" />
              All FSMO roles held by single DC — consider distributing for resilience.
            </div>
          )}
        </CardContent>
      </Card>

      {s.replicationPartners.length > 0 && (
        <Card>
          <CardHeader><CardTitle>Replication Partners ({s.replicationPartners.length})</CardTitle></CardHeader>
          <CardContent>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Partner</TableHead>
                  <TableHead>Direction</TableHead>
                  <TableHead>Naming Context</TableHead>
                  <TableHead>Last Success</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Transport</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {s.replicationPartners.map((rp, i) => (
                  <TableRow key={i}>
                    <TableCell className="font-medium">{rp.partnerHostname ?? '—'}</TableCell>
                    <TableCell>{rp.direction ?? '—'}</TableCell>
                    <TableCell className="max-w-[200px] truncate text-xs">{rp.namingContext ?? '—'}</TableCell>
                    <TableCell>{timeAgo(rp.lastSuccess)}</TableCell>
                    <TableCell>{replStatusBadge(rp)}</TableCell>
                    <TableCell><Badge variant="outline">{rp.transport ?? 'IP'}</Badge></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      )}
    </div>
  );
}

// ── Main tab ──

export function ActiveDirectoryTab() {
  const { orgId } = useOrgParam();
  const { data: scan, isLoading: hygieneLoading } = useHygiene(orgId);

  const hasHygiene = !!scan?.id;

  if (hygieneLoading) {
    return (
      <div className="space-y-4 p-4">
        {Array.from({ length: 4 }, (_, i) => <Skeleton key={i} className="h-24" />)}
      </div>
    );
  }

  if (!hasHygiene) {
    return (
      <EmptyState
        icon={<ShieldAlert className="h-12 w-12" />}
        title="No Active Directory Data"
        description="Run a network scan from a Domain Controller to generate AD hygiene, security, and health data."
      />
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h3 className="text-lg font-semibold">Active Directory</h3>
          <p className="text-sm text-muted-foreground">
            Last scan: {formatDate(scan!.scannedAt)} by {scan!.scannedBy} — {scan!.findings?.length ?? 0} findings
          </p>
        </div>
      </div>

      <Tabs defaultValue="hygiene">
        <TabsList>
          <TabsTrigger value="hygiene">Hygiene</TabsTrigger>
          <TabsTrigger value="security">Security</TabsTrigger>
          <TabsTrigger value="health">Health</TabsTrigger>
        </TabsList>

        <TabsContent value="hygiene" className="mt-4">
          <HygieneSection scan={scan!} />
        </TabsContent>

        <TabsContent value="security" className="mt-4">
          <SecuritySection scan={scan!} />
        </TabsContent>

        <TabsContent value="health" className="mt-4">
          {orgId ? <HealthSection orgId={orgId} /> : null}
        </TabsContent>
      </Tabs>
    </div>
  );
}

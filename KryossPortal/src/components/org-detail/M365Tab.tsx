import { useState } from 'react';
import {
  Cloud,
  RefreshCw,
  Shield,
  ShieldAlert,
  ShieldCheck,
  ShieldX,
  Info,
  Loader2,
  Unlink,
  CheckCircle2,
  XCircle,
  AlertTriangle,
} from 'lucide-react';
import { toast } from 'sonner';
import {
  useM365,
  useM365Connect,
  useM365Scan,
  useM365Disconnect,
  type M365Finding,
  type M365ConnectPayload,
} from '@/api/m365';
import { useOrgParam } from '@/hooks/useOrgParam';
import { EmptyState } from '@/components/shared/EmptyState';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';

// ── Helpers ──

function formatDate(dateStr: string): string {
  return new Date(dateStr).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

function statusIcon(status: string) {
  switch (status) {
    case 'pass':
      return <CheckCircle2 className="h-4 w-4 text-green-600" />;
    case 'fail':
      return <XCircle className="h-4 w-4 text-red-600" />;
    case 'warn':
      return <AlertTriangle className="h-4 w-4 text-amber-600" />;
    default:
      return <Info className="h-4 w-4 text-blue-500" />;
  }
}

function statusBadge(status: string) {
  const config: Record<string, { label: string; className: string }> = {
    pass: { label: 'Pass', className: 'bg-green-100 text-green-800' },
    fail: { label: 'Fail', className: 'bg-red-100 text-red-800' },
    warn: { label: 'Warn', className: 'bg-amber-100 text-amber-800' },
    info: { label: 'Info', className: 'bg-blue-100 text-blue-800' },
  };
  const c = config[status] ?? {
    label: status,
    className: 'bg-gray-100 text-gray-500',
  };
  return (
    <Badge variant="secondary" className={c.className}>
      {c.label}
    </Badge>
  );
}

function severityBadge(severity: string) {
  const config: Record<string, string> = {
    critical: 'bg-red-100 text-red-800',
    high: 'bg-orange-100 text-orange-800',
    medium: 'bg-amber-100 text-amber-800',
    low: 'bg-blue-100 text-blue-800',
  };
  return (
    <Badge variant="secondary" className={config[severity] ?? 'bg-gray-100 text-gray-500'}>
      {severity}
    </Badge>
  );
}

const categoryLabels: Record<string, string> = {
  conditional_access: 'Conditional Access',
  mfa: 'MFA',
  security_defaults: 'Security Defaults',
  admin_roles: 'Admin Roles',
  guest_access: 'Guest Access',
  mail_security: 'Mail Security',
};

// ── Connect Form ──

function ConnectForm({ orgId }: { orgId: string }) {
  const [tenantId, setTenantId] = useState('');
  const [tenantName, setTenantName] = useState('');
  const [clientId, setClientId] = useState('');
  const [clientSecret, setClientSecret] = useState('');

  const connect = useM365Connect();

  const handleConnect = () => {
    if (!tenantId || !clientId || !clientSecret) {
      toast.error('Tenant ID, Client ID, and Client Secret are required');
      return;
    }

    const payload: M365ConnectPayload = {
      organizationId: orgId,
      tenantId,
      tenantName: tenantName || undefined,
      clientId,
      clientSecret,
    };

    connect.mutate(payload, {
      onSuccess: (data) => {
        toast.success(
          `M365 tenant connected. ${data.checksPassed} passed, ${data.checksFailed} failed.`,
        );
      },
      onError: (err: any) => {
        toast.error(`Connection failed: ${err.message}`);
      },
    });
  };

  return (
    <div className="space-y-6">
      <EmptyState
        icon={<Cloud className="h-12 w-12" />}
        title="No M365 tenant connected"
        description="Connect a Microsoft 365 / Entra ID tenant to audit cloud security configuration."
      />

      <Card className="max-w-2xl mx-auto">
        <CardHeader>
          <CardTitle className="text-base">Connect M365 Tenant</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="rounded-lg border border-blue-200 bg-blue-50 p-4 text-sm text-blue-800 space-y-2">
            <p className="font-medium">Setup instructions:</p>
            <ol className="list-decimal list-inside space-y-1 text-xs">
              <li>Go to Azure Portal &gt; Entra ID &gt; App registrations &gt; New registration</li>
              <li>Name it "Kryoss Security Audit" (single tenant)</li>
              <li>Go to API permissions and add Microsoft Graph (Application):
                <span className="font-mono text-xs"> Directory.Read.All, Policy.Read.All, User.Read.All, UserAuthenticationMethod.Read.All, MailboxSettings.Read, Mail.Read</span>
              </li>
              <li>Grant admin consent for all permissions</li>
              <li>Go to Certificates &amp; secrets &gt; New client secret</li>
              <li>Copy the Tenant ID, Application (client) ID, and secret value below</li>
            </ol>
          </div>

          <div>
            <label className="text-sm font-medium">Tenant ID (Directory ID)</label>
            <Input
              placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
              value={tenantId}
              onChange={(e) => setTenantId(e.target.value)}
            />
          </div>

          <div>
            <label className="text-sm font-medium">Tenant Name (optional)</label>
            <Input
              placeholder="Contoso Inc."
              value={tenantName}
              onChange={(e) => setTenantName(e.target.value)}
            />
          </div>

          <div>
            <label className="text-sm font-medium">Application (Client) ID</label>
            <Input
              placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
              value={clientId}
              onChange={(e) => setClientId(e.target.value)}
            />
          </div>

          <div>
            <label className="text-sm font-medium">Client Secret</label>
            <Input
              type="password"
              placeholder="Client secret value"
              value={clientSecret}
              onChange={(e) => setClientSecret(e.target.value)}
            />
          </div>

          <Button onClick={handleConnect} disabled={connect.isPending} className="w-full">
            {connect.isPending ? (
              <>
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                Connecting and scanning...
              </>
            ) : (
              <>
                <Cloud className="mr-2 h-4 w-4" />
                Connect Tenant
              </>
            )}
          </Button>
        </CardContent>
      </Card>
    </div>
  );
}

// ── Category Bar ──

function CategoryBar({
  category,
  findings,
}: {
  category: string;
  findings: M365Finding[];
}) {
  const passed = findings.filter((f) => f.status === 'pass').length;
  const failed = findings.filter((f) => f.status === 'fail').length;
  const warned = findings.filter((f) => f.status === 'warn').length;
  const info = findings.filter((f) => f.status === 'info').length;
  const total = findings.length;

  return (
    <div className="space-y-1">
      <div className="flex items-center justify-between text-sm">
        <span className="font-medium">{categoryLabels[category] ?? category}</span>
        <span className="text-muted-foreground text-xs">
          {passed}/{total - info} checks passed
        </span>
      </div>
      <div className="flex h-2 rounded-full overflow-hidden bg-gray-100">
        {passed > 0 && (
          <div
            className="bg-green-500"
            style={{ width: `${(passed / total) * 100}%` }}
          />
        )}
        {warned > 0 && (
          <div
            className="bg-amber-400"
            style={{ width: `${(warned / total) * 100}%` }}
          />
        )}
        {failed > 0 && (
          <div
            className="bg-red-500"
            style={{ width: `${(failed / total) * 100}%` }}
          />
        )}
        {info > 0 && (
          <div
            className="bg-blue-300"
            style={{ width: `${(info / total) * 100}%` }}
          />
        )}
      </div>
    </div>
  );
}

// ── Main Component ──

export function M365Tab() {
  const { orgId } = useOrgParam();
  const { data, isLoading } = useM365(orgId);
  const rescan = useM365Scan();
  const disconnect = useM365Disconnect();
  const [filterCategory, setFilterCategory] = useState<string>('all');
  const [filterStatus, setFilterStatus] = useState<string>('all');

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

  if (!data?.connected || !orgId) {
    return <ConnectForm orgId={orgId ?? ''} />;
  }

  const findings = data.findings ?? [];
  const summary = data.summary ?? {
    totalChecks: 0,
    passed: 0,
    failed: 0,
    warned: 0,
    info: 0,
  };

  // Group by category
  const categories = [...new Set(findings.map((f) => f.category))];

  // Filtering
  const filteredFindings = findings.filter((f) => {
    if (filterCategory !== 'all' && f.category !== filterCategory) return false;
    if (filterStatus !== 'all' && f.status !== filterStatus) return false;
    return true;
  });

  const handleRescan = () => {
    rescan.mutate(orgId, {
      onSuccess: (result) => {
        toast.success(
          `Scan complete. ${result.checksPassed} passed, ${result.checksFailed} failed.`,
        );
      },
      onError: (err: any) => {
        toast.error(`Scan failed: ${err.message}`);
      },
    });
  };

  const handleDisconnect = () => {
    if (!confirm('Are you sure you want to disconnect this M365 tenant? All findings will be deleted.'))
      return;

    disconnect.mutate(orgId, {
      onSuccess: () => {
        toast.success('M365 tenant disconnected');
      },
      onError: (err: any) => {
        toast.error(`Disconnect failed: ${err.message}`);
      },
    });
  };

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h3 className="text-lg font-semibold">
            Microsoft 365 / Entra ID Security
          </h3>
          <p className="text-sm text-muted-foreground">
            Tenant: {data.tenantName ?? data.tenantId}
            {data.lastScanAt && <> &middot; Last scan: {formatDate(data.lastScanAt)}</>}
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={handleRescan}
            disabled={rescan.isPending}
          >
            {rescan.isPending ? (
              <Loader2 className="mr-1.5 h-4 w-4 animate-spin" />
            ) : (
              <RefreshCw className="mr-1.5 h-4 w-4" />
            )}
            Re-scan
          </Button>
          <Button
            variant="ghost"
            size="sm"
            className="text-destructive"
            onClick={handleDisconnect}
            disabled={disconnect.isPending}
          >
            <Unlink className="mr-1.5 h-4 w-4" />
            Disconnect
          </Button>
        </div>
      </div>

      {/* Status badge */}
      {data.status === 'expired' && (
        <div className="rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800">
          Tenant credentials may be expired. Re-scan or reconnect to verify.
        </div>
      )}

      {/* KPI cards */}
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <Card>
          <CardHeader className="flex flex-row items-start justify-between pb-1 pt-0 h-12">
            <CardTitle className="text-sm font-medium text-muted-foreground">
              Total Checks
            </CardTitle>
            <Shield className="h-4 w-4 text-blue-500" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold">{summary.totalChecks}</div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex flex-row items-start justify-between pb-1 pt-0 h-12">
            <CardTitle className="text-sm font-medium text-muted-foreground">
              Passed
            </CardTitle>
            <ShieldCheck className="h-4 w-4 text-green-500" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold text-green-600">
              {summary.passed}
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex flex-row items-start justify-between pb-1 pt-0 h-12">
            <CardTitle className="text-sm font-medium text-muted-foreground">
              Failed
            </CardTitle>
            <ShieldX className="h-4 w-4 text-red-500" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold text-red-600">
              {summary.failed}
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex flex-row items-start justify-between pb-1 pt-0 h-12">
            <CardTitle className="text-sm font-medium text-muted-foreground">
              Warnings
            </CardTitle>
            <ShieldAlert className="h-4 w-4 text-amber-500" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold text-amber-600">
              {summary.warned}
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Category breakdown */}
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-base">Category Breakdown</CardTitle>
        </CardHeader>
        <CardContent className="space-y-3">
          {categories.map((cat) => (
            <CategoryBar
              key={cat}
              category={cat}
              findings={findings.filter((f) => f.category === cat)}
            />
          ))}
        </CardContent>
      </Card>

      {/* Filters */}
      <div className="flex items-center gap-3">
        <select
          className="rounded-md border px-3 py-1.5 text-sm"
          value={filterCategory}
          onChange={(e) => setFilterCategory(e.target.value)}
        >
          <option value="all">All Categories</option>
          {categories.map((cat) => (
            <option key={cat} value={cat}>
              {categoryLabels[cat] ?? cat}
            </option>
          ))}
        </select>

        <select
          className="rounded-md border px-3 py-1.5 text-sm"
          value={filterStatus}
          onChange={(e) => setFilterStatus(e.target.value)}
        >
          <option value="all">All Statuses</option>
          <option value="pass">Pass</option>
          <option value="fail">Fail</option>
          <option value="warn">Warn</option>
          <option value="info">Info</option>
        </select>

        <span className="text-sm text-muted-foreground">
          {filteredFindings.length} of {findings.length} findings
        </span>
      </div>

      {/* Findings table */}
      <Card>
        <CardContent className="p-0">
          <div className="max-h-[600px] overflow-y-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="w-24">Check ID</TableHead>
                  <TableHead>Name</TableHead>
                  <TableHead className="w-36">Category</TableHead>
                  <TableHead className="w-24">Severity</TableHead>
                  <TableHead className="w-20">Status</TableHead>
                  <TableHead>Finding</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {filteredFindings.map((f, i) => (
                  <TableRow key={i}>
                    <TableCell className="font-mono text-xs">
                      {f.checkId}
                    </TableCell>
                    <TableCell className="text-sm font-medium">
                      <div className="flex items-center gap-2">
                        {statusIcon(f.status)}
                        {f.name}
                      </div>
                    </TableCell>
                    <TableCell className="text-sm text-muted-foreground">
                      {categoryLabels[f.category] ?? f.category}
                    </TableCell>
                    <TableCell>{severityBadge(f.severity)}</TableCell>
                    <TableCell>{statusBadge(f.status)}</TableCell>
                    <TableCell className="text-sm text-muted-foreground max-w-sm">
                      <div className="truncate" title={f.finding ?? undefined}>
                        {f.finding ?? '--'}
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
                {filteredFindings.length === 0 && (
                  <TableRow>
                    <TableCell
                      colSpan={6}
                      className="text-center text-muted-foreground py-8"
                    >
                      No findings match the selected filters
                    </TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}

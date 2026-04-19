import { useState } from 'react';
import {
  usePowerBiConnection,
  usePowerBiConnect,
  usePowerBiVerify,
  usePowerBiDisconnect,
  useCloudAssessmentDetail,
  type PowerBiConnectInstructions,
  type CloudAssessmentFinding,
} from '@/api/cloudAssessment';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { BarChart3, CheckCircle2, XCircle, AlertTriangle, Loader2, Unplug } from 'lucide-react';

function ConnectPowerBiCard({ orgId }: { orgId: string }) {
  const connect = usePowerBiConnect();
  const verify = usePowerBiVerify();
  const [instructions, setInstructions] = useState<PowerBiConnectInstructions | null>(null);
  const [verifyResult, setVerifyResult] = useState<{ connected: boolean; error?: string } | null>(null);

  const handleConnect = () => {
    connect.mutate({ organizationId: orgId }, {
      onSuccess: (data) => setInstructions(data),
    });
  };

  const handleVerify = () => {
    setVerifyResult(null);
    verify.mutate({ organizationId: orgId }, {
      onSuccess: (data) => setVerifyResult(data),
    });
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-lg">
          <BarChart3 className="h-5 w-5" />
          Connect Power BI Governance
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        <p className="text-sm text-muted-foreground">
          Enable Power BI governance scanning to audit workspaces, datasets, gateways, capacities, and user activity.
        </p>

        {!instructions ? (
          <Button onClick={handleConnect} disabled={connect.isPending}>
            {connect.isPending && <Loader2 className="h-4 w-4 mr-2 animate-spin" />}
            Get Setup Instructions
          </Button>
        ) : (
          <div className="space-y-3">
            <div className="rounded-lg border p-4 space-y-2 text-sm">
              {instructions.instructions.map((step, i) => (
                <p key={i}>{step}</p>
              ))}
            </div>

            <div className="flex items-center gap-3">
              <Button onClick={handleVerify} disabled={verify.isPending}>
                {verify.isPending && <Loader2 className="h-4 w-4 mr-2 animate-spin" />}
                Verify Connection
              </Button>
              {verifyResult && (
                verifyResult.connected ? (
                  <span className="text-sm text-green-600 flex items-center gap-1">
                    <CheckCircle2 className="h-4 w-4" /> Connected
                  </span>
                ) : (
                  <span className="text-sm text-red-600 flex items-center gap-1">
                    <XCircle className="h-4 w-4" /> {verifyResult.error ?? 'Connection failed'}
                  </span>
                )
              )}
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

function PowerBiDashboard({
  orgId,
  scanId,
}: {
  orgId: string;
  scanId: string | undefined;
}) {
  const disconnect = usePowerBiDisconnect();
  const { data: detail } = useCloudAssessmentDetail(scanId);

  const findings: CloudAssessmentFinding[] = (detail?.findings ?? []).filter(
    (f) => f.area === 'powerbi',
  );

  const metrics = detail?.metrics?.filter((m) => m.area === 'powerbi') ?? [];
  const getMetric = (key: string) => metrics.find((m) => m.metricKey === key)?.metricValue;

  const workspacesTotal = parseInt(getMetric('workspaces_total') ?? '0');
  const workspacesOrphaned = parseInt(getMetric('workspaces_orphaned') ?? '0');
  const workspacesPersonal = parseInt(getMetric('workspaces_personal') ?? '0');
  const datasetsTotal = parseInt(getMetric('datasets_total') ?? '0');
  const datasetsStale = parseInt(getMetric('datasets_stale_30d') ?? '0');
  const gatewaysTotal = parseInt(getMetric('gateways_total') ?? '0');
  const gatewaysOffline = parseInt(getMetric('gateways_offline') ?? '0');
  const capacitiesTotal = parseInt(getMetric('capacities_total') ?? '0');
  const activities30d = parseInt(getMetric('activities_30d') ?? '0');
  const uniqueUsers = parseInt(getMetric('unique_users_30d') ?? '0');
  const externalShares = parseInt(getMetric('external_shares_30d') ?? '0');
  const reportsTotal = parseInt(getMetric('reports_total') ?? '0');

  const hasScanData = metrics.length > 0;

  return (
    <div className="space-y-4">
      {!hasScanData && (
        <div className="rounded-lg border border-blue-200 bg-blue-50 p-4 text-sm text-blue-800 flex items-start gap-3">
          <BarChart3 className="h-5 w-5 shrink-0 mt-0.5 text-blue-600" />
          <div>
            <p className="font-medium">No Power BI scan data yet</p>
            <p className="text-xs mt-1 text-blue-700">
              Run a scan from the Overview tab to see Power BI governance data.
            </p>
          </div>
        </div>
      )}

      {hasScanData && (
        <>
          {/* KPI Cards */}
          <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-3">
            <KpiCard label="Workspaces" value={workspacesTotal} />
            <KpiCard label="Reports" value={reportsTotal} />
            <KpiCard label="Datasets" value={datasetsTotal} warn={datasetsStale > 0 ? `${datasetsStale} stale` : undefined} />
            <KpiCard label="Gateways" value={gatewaysTotal} warn={gatewaysOffline > 0 ? `${gatewaysOffline} offline` : undefined} />
            <KpiCard label="Capacities" value={capacitiesTotal} />
            <KpiCard label="Active Users (30d)" value={uniqueUsers} />
          </div>

          {/* Warning banners */}
          {workspacesOrphaned > 0 && (
            <div className="rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800 flex items-center gap-2">
              <AlertTriangle className="h-4 w-4" />
              {workspacesOrphaned} orphaned workspace(s) with no admin
            </div>
          )}

          {workspacesPersonal > 0 && (
            <div className="rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800 flex items-center gap-2">
              <AlertTriangle className="h-4 w-4" />
              {workspacesPersonal} personal workspace(s) detected
            </div>
          )}

          {/* Activity summary */}
          <Card>
            <CardHeader className="pb-2">
              <CardTitle className="text-sm">Activity Summary (30 days)</CardTitle>
            </CardHeader>
            <CardContent>
              <div className="grid grid-cols-2 sm:grid-cols-4 gap-4 text-sm">
                <div>
                  <span className="text-muted-foreground">Total Events</span>
                  <p className="text-lg font-semibold">{activities30d.toLocaleString()}</p>
                </div>
                <div>
                  <span className="text-muted-foreground">Unique Users</span>
                  <p className="text-lg font-semibold">{uniqueUsers}</p>
                </div>
                <div>
                  <span className="text-muted-foreground">External Shares</span>
                  <p className={`text-lg font-semibold ${externalShares > 10 ? 'text-amber-600' : ''}`}>{externalShares}</p>
                </div>
                <div>
                  <span className="text-muted-foreground">Exports</span>
                  <p className="text-lg font-semibold">{parseInt(getMetric('external_shares_30d') ?? '0')}</p>
                </div>
              </div>
            </CardContent>
          </Card>

          {/* Findings table */}
          {findings.length > 0 && (
            <Card>
              <CardHeader className="pb-2">
                <CardTitle className="text-sm">Power BI Findings</CardTitle>
              </CardHeader>
              <CardContent className="p-0 overflow-x-auto">
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Feature</TableHead>
                      <TableHead>Status</TableHead>
                      <TableHead>Observation</TableHead>
                      <TableHead>Recommendation</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {findings.map((f, i) => (
                      <TableRow key={`${f.feature}-${i}`}>
                        <TableCell className="text-sm font-medium whitespace-nowrap">{f.feature}</TableCell>
                        <TableCell>
                          <StatusBadge status={f.status} />
                        </TableCell>
                        <TableCell className="text-sm text-muted-foreground max-w-xs">
                          <div className="truncate" title={f.observation ?? undefined}>{f.observation ?? '—'}</div>
                        </TableCell>
                        <TableCell className="text-sm text-muted-foreground max-w-xs">
                          <div className="truncate" title={f.recommendation ?? undefined}>{f.recommendation ?? '—'}</div>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </CardContent>
            </Card>
          )}
        </>
      )}

      {/* Disconnect */}
      <div className="text-right">
        <Button
          variant="ghost"
          size="sm"
          className="text-red-600 hover:text-red-700"
          onClick={() => disconnect.mutate({ organizationId: orgId })}
          disabled={disconnect.isPending}
        >
          <Unplug className="h-4 w-4 mr-1" />
          Disconnect Power BI
        </Button>
      </div>
    </div>
  );
}

function KpiCard({ label, value, warn }: { label: string; value: number; warn?: string }) {
  return (
    <Card>
      <CardContent className="py-3 px-4">
        <p className="text-xs text-muted-foreground">{label}</p>
        <p className="text-xl font-semibold">{value}</p>
        {warn && <p className="text-xs text-amber-600 mt-0.5">{warn}</p>}
      </CardContent>
    </Card>
  );
}

function StatusBadge({ status }: { status: string }) {
  const map: Record<string, { color: string; label: string }> = {
    success:         { color: 'bg-green-100 text-green-800', label: 'Success' },
    action_required: { color: 'bg-red-100 text-red-800',    label: 'Action Required' },
    warning:         { color: 'bg-amber-100 text-amber-800', label: 'Warning' },
    insight:         { color: 'bg-blue-100 text-blue-800',  label: 'Insight' },
    informational:   { color: 'bg-blue-100 text-blue-800',  label: 'Informational' },
    disabled:        { color: 'bg-gray-100 text-gray-600',  label: 'Disabled' },
    not_licensed:    { color: 'bg-gray-100 text-gray-600',  label: 'Not Licensed' },
  };
  const entry = map[status.toLowerCase()];
  return <Badge variant="secondary" className={entry?.color ?? 'bg-gray-100 text-gray-500'}>{entry?.label ?? status}</Badge>;
}

export function PowerBiTab({
  orgId,
  scanId,
}: {
  orgId: string;
  scanId: string | undefined;
}) {
  const { data: connection, isLoading } = usePowerBiConnection(orgId);

  if (isLoading) {
    return (
      <Card>
        <CardContent className="py-10 text-center text-sm text-muted-foreground">
          Loading...
        </CardContent>
      </Card>
    );
  }

  const connState = connection && 'connectionState' in connection ? connection.connectionState : 'none';
  const errorMsg = connection && 'errorMessage' in connection ? (connection as any).errorMessage : null;

  if (connState === 'connected') {
    return <PowerBiDashboard orgId={orgId} scanId={scanId} />;
  }

  if (connState === 'unavailable') {
    return (
      <Card>
        <CardContent className="py-8 text-center space-y-3">
          <BarChart3 className="h-8 w-8 text-gray-400 mx-auto" />
          <p className="text-sm font-medium text-muted-foreground">Power BI Governance — Not Available</p>
          <p className="text-xs text-muted-foreground max-w-md mx-auto">
            {errorMsg ?? 'The Power BI Admin API is not accessible for this tenant. Enable "Service principals can use read-only admin APIs" in Power BI Admin Portal.'}
          </p>
          <a
            href="https://app.powerbi.com/admin-portal/tenantSettings"
            target="_blank"
            rel="noopener noreferrer"
            className="inline-flex items-center gap-1 text-xs text-blue-600 hover:underline"
          >
            Open Power BI Admin Portal
          </a>
        </CardContent>
      </Card>
    );
  }

  // Default: not connected yet
  return <ConnectPowerBiCard orgId={orgId} />;
}

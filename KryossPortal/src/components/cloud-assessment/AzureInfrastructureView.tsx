import { toast } from 'sonner';
import {
  AlertTriangle,
  CheckCircle,
  Cloud,
  Database,
  Loader2,
  RefreshCw,
  Server,
  Shield,
} from 'lucide-react';
import {
  PieChart,
  Pie,
  Cell,
  ResponsiveContainer,
  Tooltip,
  Legend,
} from 'recharts';
import {
  useCloudAssessment,
  useCloudAssessmentDetail,
  useAzureVerify,
  type AzureSubscription,
  type CloudAssessmentFinding,
} from '@/api/cloudAssessment';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { ExternalLink } from 'lucide-react';

// ── Helpers ──

function parseMetric(v: string | undefined): number {
  if (!v) return 0;
  const n = Number(v);
  return Number.isFinite(n) ? n : 0;
}

function parseFloatMetric(v: string | undefined): number | null {
  if (!v) return null;
  const n = Number(v);
  return Number.isFinite(n) ? n : null;
}

function scoreColor(score: number | null): string {
  if (score === null) return 'text-gray-400';
  if (score >= 4) return 'text-green-600';
  if (score >= 3) return 'text-amber-600';
  return 'text-red-600';
}

function formatDate(s: string | null): string {
  if (!s) return '—';
  return new Date(s).toLocaleString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

function statusBadge(status: string) {
  const key = status.toLowerCase();
  const map: Record<string, { color: string; label: string }> = {
    success:         { color: 'bg-green-100 text-green-800', label: 'Success' },
    action_required: { color: 'bg-red-100 text-red-800',    label: 'Action Required' },
    warning:         { color: 'bg-amber-100 text-amber-800', label: 'Warning' },
    critical:        { color: 'bg-red-200 text-red-900',    label: 'Critical' },
    disabled:        { color: 'bg-gray-100 text-gray-600',  label: 'Disabled' },
    not_licensed:    { color: 'bg-gray-100 text-gray-600',  label: 'Not Licensed' },
    insight:         { color: 'bg-blue-100 text-blue-800',  label: 'Insight' },
    informational:   { color: 'bg-blue-100 text-blue-800',  label: 'Informational' },
  };
  const entry = map[key];
  return <Badge variant="secondary" className={entry?.color ?? 'bg-gray-100 text-gray-500'}>{entry?.label ?? status}</Badge>;
}

const SERVICE_LABELS: Record<string, string> = {
  arm: 'Azure Resource Manager',
  'defender-cloud': 'Defender for Cloud',
  storage: 'Azure Storage',
  keyvault: 'Key Vault',
  network: 'Network Security',
  compute: 'Compute',
  policy: 'Azure Policy',
};

function serviceLabel(service: string): string {
  return SERVICE_LABELS[service.toLowerCase()] ?? service.charAt(0).toUpperCase() + service.slice(1);
}

function priorityBadge(priority: string) {
  const key = priority.toLowerCase();
  const map: Record<string, { color: string; label: string }> = {
    critical:      { color: 'bg-red-100 text-red-800',    label: 'Critical' },
    high:          { color: 'bg-orange-100 text-orange-800', label: 'High' },
    medium:        { color: 'bg-amber-100 text-amber-800', label: 'Medium' },
    low:           { color: 'bg-blue-100 text-blue-800',  label: 'Low' },
    informational: { color: 'bg-gray-100 text-gray-600',  label: 'Informational' },
  };
  const entry = map[key];
  return <Badge variant="secondary" className={entry?.color ?? 'bg-gray-100 text-gray-500'}>{entry?.label ?? priority}</Badge>;
}

// Donut palette — keeps Kryoss primary green first, then the category greys/accents
// used across the Cloud Assessment module.
const DONUT_COLORS = ['#008852', '#3b82f6', '#a855f7', '#f59e0b', '#ef4444', '#6b7280'];

// ── Sub-cards ──

interface HeaderKpiCardProps {
  subscriptionsScanned: number;
  resourcesTotal: number;
  areaScore: number | null;
  completedAt: string | null;
}

function HeaderKpiCard({
  subscriptionsScanned,
  resourcesTotal,
  areaScore,
  completedAt,
}: HeaderKpiCardProps) {
  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-sm flex items-center gap-2">
          <Cloud className="h-4 w-4" /> Azure Infrastructure
        </CardTitle>
      </CardHeader>
      <CardContent>
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <div>
            <p className="text-xs uppercase tracking-wide text-muted-foreground">
              Subscriptions scanned
            </p>
            <p className="text-2xl font-bold mt-1">{subscriptionsScanned}</p>
          </div>
          <div>
            <p className="text-xs uppercase tracking-wide text-muted-foreground">
              Resources total
            </p>
            <p className="text-2xl font-bold mt-1">{resourcesTotal.toLocaleString()}</p>
          </div>
          <div>
            <p className="text-xs uppercase tracking-wide text-muted-foreground">
              Azure area score
            </p>
            <p className={`text-2xl font-bold mt-1 ${scoreColor(areaScore)}`}>
              {areaScore !== null ? `${areaScore.toFixed(2)} / 5.00` : '—'}
            </p>
          </div>
          <div>
            <p className="text-xs uppercase tracking-wide text-muted-foreground">
              Last scan
            </p>
            <p className="text-sm font-medium mt-1">{formatDate(completedAt)}</p>
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

interface PublicExposureCardProps {
  storagePublicBlob: number;
  nsgAnyAnyAllow: number;
  keyvaultsNoSoftDelete: number;
}

function PublicExposureCard({
  storagePublicBlob,
  nsgAnyAnyAllow,
  keyvaultsNoSoftDelete,
}: PublicExposureCardProps) {
  const alerts: { count: number; message: string }[] = [];
  if (storagePublicBlob > 0) {
    alerts.push({
      count: storagePublicBlob,
      message: `${storagePublicBlob} storage account${
        storagePublicBlob === 1 ? '' : 's'
      } allow public blob access`,
    });
  }
  if (nsgAnyAnyAllow > 0) {
    alerts.push({
      count: nsgAnyAnyAllow,
      message: `${nsgAnyAnyAllow} NSG rule${
        nsgAnyAnyAllow === 1 ? '' : 's'
      } allow any-any inbound traffic`,
    });
  }
  if (keyvaultsNoSoftDelete > 0) {
    alerts.push({
      count: keyvaultsNoSoftDelete,
      message: `${keyvaultsNoSoftDelete} Key Vault${
        keyvaultsNoSoftDelete === 1 ? '' : 's'
      } have soft-delete disabled`,
    });
  }

  if (alerts.length === 0) {
    return (
      <Card className="border-green-200">
        <CardContent className="py-6 flex items-center gap-3">
          <CheckCircle className="h-6 w-6 text-green-600 shrink-0" />
          <div>
            <p className="font-medium text-green-800">
              No public exposure findings detected.
            </p>
            <p className="text-xs text-muted-foreground mt-0.5">
              Storage, NSG rules, and Key Vault soft-delete posture look healthy.
            </p>
          </div>
        </CardContent>
      </Card>
    );
  }

  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-sm flex items-center gap-2">
          <AlertTriangle className="h-4 w-4 text-red-500" />
          Public exposure alerts
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-2">
        {alerts.map((a, i) => (
          <div
            key={i}
            className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-800 flex items-start gap-3"
          >
            <AlertTriangle className="h-4 w-4 shrink-0 mt-0.5 text-red-600" />
            <p>{a.message}</p>
          </div>
        ))}
      </CardContent>
    </Card>
  );
}

interface ResourceBreakdownCardProps {
  vm: number;
  storage: number;
  keyvault: number;
  nsg: number;
  publicIp: number;
  total: number;
}

function ResourceBreakdownCard({
  vm,
  storage,
  keyvault,
  nsg,
  publicIp,
  total,
}: ResourceBreakdownCardProps) {
  const categorised = vm + storage + keyvault + nsg + publicIp;
  const other = Math.max(0, total - categorised);

  const raw = [
    { name: 'VMs', value: vm },
    { name: 'Storage', value: storage },
    { name: 'Key Vaults', value: keyvault },
    { name: 'NSGs', value: nsg },
    { name: 'Public IPs', value: publicIp },
    { name: 'Other', value: other },
  ];
  const data = raw.filter((d) => d.value > 0);

  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-sm flex items-center gap-2">
          <Server className="h-4 w-4" /> Resource breakdown
        </CardTitle>
      </CardHeader>
      <CardContent>
        {data.length === 0 ? (
          <p className="text-sm text-muted-foreground py-8 text-center">
            No resources inventoried.
          </p>
        ) : (
          <ResponsiveContainer width="100%" height={260}>
            <PieChart>
              <Pie
                data={data}
                dataKey="value"
                nameKey="name"
                cx="50%"
                cy="50%"
                innerRadius={60}
                outerRadius={90}
                paddingAngle={2}
              >
                {data.map((_, i) => (
                  <Cell key={i} fill={DONUT_COLORS[i % DONUT_COLORS.length]} />
                ))}
              </Pie>
              <Tooltip />
              <Legend verticalAlign="bottom" height={24} iconSize={10} />
            </PieChart>
          </ResponsiveContainer>
        )}
      </CardContent>
    </Card>
  );
}

interface DefenderCardProps {
  healthy: number;
  unhealthy: number;
  notApplicable: number;
  secureScorePct: number | null;
}

function DefenderCard({
  healthy,
  unhealthy,
  notApplicable,
  secureScorePct,
}: DefenderCardProps) {
  const total = healthy + unhealthy + notApplicable;
  const pct = (n: number) => (total > 0 ? (n / total) * 100 : 0);

  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-sm flex items-center gap-2">
          <Shield className="h-4 w-4" /> Defender for Cloud
        </CardTitle>
      </CardHeader>
      <CardContent>
        <div className="grid gap-6 md:grid-cols-[1fr_auto] items-center">
          <div>
            {total === 0 ? (
              <p className="text-sm text-muted-foreground py-6">
                No Defender for Cloud assessments were returned for this scan.
              </p>
            ) : (
              <>
                <div className="h-5 w-full overflow-hidden rounded-full border bg-gray-100 flex">
                  {healthy > 0 && (
                    <div
                      className="bg-green-500 h-full"
                      style={{ width: `${pct(healthy)}%` }}
                      title={`Healthy: ${healthy}`}
                    />
                  )}
                  {unhealthy > 0 && (
                    <div
                      className="bg-red-500 h-full"
                      style={{ width: `${pct(unhealthy)}%` }}
                      title={`Unhealthy: ${unhealthy}`}
                    />
                  )}
                  {notApplicable > 0 && (
                    <div
                      className="bg-gray-400 h-full"
                      style={{ width: `${pct(notApplicable)}%` }}
                      title={`Not applicable: ${notApplicable}`}
                    />
                  )}
                </div>
                <div className="mt-3 flex flex-wrap gap-4 text-sm">
                  <div className="flex items-center gap-1.5">
                    <span className="h-3 w-3 rounded-sm bg-green-500" />
                    <span className="text-muted-foreground">Healthy</span>
                    <span className="font-semibold">{healthy}</span>
                  </div>
                  <div className="flex items-center gap-1.5">
                    <span className="h-3 w-3 rounded-sm bg-red-500" />
                    <span className="text-muted-foreground">Unhealthy</span>
                    <span className="font-semibold">{unhealthy}</span>
                  </div>
                  <div className="flex items-center gap-1.5">
                    <span className="h-3 w-3 rounded-sm bg-gray-400" />
                    <span className="text-muted-foreground">N/A</span>
                    <span className="font-semibold">{notApplicable}</span>
                  </div>
                </div>
              </>
            )}
          </div>
          {secureScorePct !== null && (
            <div className="text-center rounded-lg border px-5 py-3 min-w-[120px]">
              <p className="text-xs uppercase tracking-wide text-muted-foreground">
                Secure Score
              </p>
              <p className="text-3xl font-bold mt-1 text-[#008852]">
                {secureScorePct.toFixed(1)}%
              </p>
            </div>
          )}
        </div>
      </CardContent>
    </Card>
  );
}

interface FindingsTableProps {
  findings: CloudAssessmentFinding[];
}

function FindingsTable({ findings }: FindingsTableProps) {
  if (findings.length === 0) {
    return (
      <Card>
        <CardContent className="py-10 text-center text-sm text-muted-foreground">
          No Azure findings for this scan.
        </CardContent>
      </Card>
    );
  }

  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-sm flex items-center gap-2">
          <Database className="h-4 w-4" /> Azure findings ({findings.length})
        </CardTitle>
      </CardHeader>
      <CardContent className="p-0 overflow-x-auto">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Service</TableHead>
              <TableHead>Feature</TableHead>
              <TableHead>Status</TableHead>
              <TableHead>Priority</TableHead>
              <TableHead>Observation</TableHead>
              <TableHead>Recommendation</TableHead>
              <TableHead className="w-20">Link</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {findings.map((f, i) => (
              <TableRow key={`${f.service}-${f.feature}-${i}`}>
                <TableCell className="text-sm font-medium whitespace-nowrap">
                  {serviceLabel(f.service)}
                </TableCell>
                <TableCell className="text-sm">{f.feature}</TableCell>
                <TableCell>{statusBadge(f.status)}</TableCell>
                <TableCell>{priorityBadge(f.priority)}</TableCell>
                <TableCell className="text-sm text-muted-foreground max-w-sm">
                  <div className="truncate" title={f.observation ?? undefined}>
                    {f.observation ?? '—'}
                  </div>
                </TableCell>
                <TableCell className="text-sm text-muted-foreground max-w-sm">
                  <div className="truncate" title={f.recommendation ?? undefined}>
                    {f.recommendation ?? '—'}
                  </div>
                </TableCell>
                <TableCell>
                  {f.linkUrl ? (
                    <a
                      href={f.linkUrl}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="text-blue-600 hover:underline text-xs flex items-center gap-1"
                    >
                      <ExternalLink className="h-3 w-3" />
                      {f.linkText ?? 'Docs'}
                    </a>
                  ) : (
                    '—'
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  );
}

interface SubscriptionsQuickRefProps {
  orgId: string;
  subs: AzureSubscription[];
  onManage: () => void;
}

function SubscriptionsQuickRef({ orgId, subs, onManage }: SubscriptionsQuickRefProps) {
  const verify = useAzureVerify();

  const handleReVerify = (tenantId: string | null, label: string) => {
    if (!tenantId) {
      toast.error('No tenant ID available for this subscription.');
      return;
    }
    verify.mutate(
      { organizationId: orgId, tenantId },
      {
        onSuccess: (data) => {
          if (data.connected && data.subscriptions && data.subscriptions.length > 0) {
            toast.success(`Re-verified ${label}. ${data.subscriptions.length} sub(s) visible.`);
          } else if (data.connected) {
            toast.warning(data.message ?? 'Consent granted, but no subscriptions visible yet.');
          } else if (data.error) {
            toast.error(`Re-verify error: ${data.error}`);
          } else {
            toast.error(data.message ?? 'Re-verify failed.');
          }
        },
        onError: (err: Error) => {
          toast.error(`Re-verify failed: ${err.message}`);
        },
      },
    );
  };

  return (
    <Card>
      <CardContent className="py-3">
        <details className="text-sm">
          <summary className="cursor-pointer font-medium flex items-center justify-between gap-2">
            <span>
              Connected subscriptions ({subs.length}) — click to expand
            </span>
            <Button
              variant="outline"
              size="sm"
              onClick={(e) => {
                e.preventDefault();
                onManage();
              }}
            >
              Manage subscriptions
            </Button>
          </summary>
          <div className="mt-3 space-y-2">
            {subs.map((s) => {
              const label = s.displayName ?? s.subscriptionId;
              return (
                <div
                  key={s.id}
                  className="flex flex-wrap items-center justify-between gap-2 rounded-md border px-3 py-2"
                >
                  <div className="min-w-0 flex-1">
                    <p className="text-sm font-medium truncate">{label}</p>
                    <p className="text-xs text-muted-foreground font-mono truncate">
                      {s.subscriptionId}
                    </p>
                  </div>
                  <div className="flex items-center gap-2">
                    <Badge
                      variant="secondary"
                      className={
                        s.state === 'Enabled'
                          ? 'bg-green-100 text-green-800'
                          : 'bg-gray-100 text-gray-600'
                      }
                    >
                      {s.state ?? '—'}
                    </Badge>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => handleReVerify(s.tenantId, label)}
                      disabled={verify.isPending}
                    >
                      {verify.isPending ? (
                        <Loader2 className="mr-1 h-3.5 w-3.5 animate-spin" />
                      ) : (
                        <RefreshCw className="mr-1 h-3.5 w-3.5" />
                      )}
                      Re-verify
                    </Button>
                  </div>
                </div>
              );
            })}
          </div>
        </details>
      </CardContent>
    </Card>
  );
}

// ── Main view ──

interface AzureInfrastructureViewProps {
  orgId: string;
  scanId: string;
  subs: AzureSubscription[];
  onManageSubscriptions: () => void;
}

export function AzureInfrastructureView({
  orgId,
  scanId,
  subs,
  onManageSubscriptions,
}: AzureInfrastructureViewProps) {
  const { data: detail, isLoading } = useCloudAssessmentDetail(scanId);
  const { data: summary } = useCloudAssessment(orgId);

  if (isLoading || !detail) {
    return (
      <Card>
        <CardContent className="py-16 flex flex-col items-center justify-center gap-3">
          <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
          <span className="text-sm text-muted-foreground">Loading Azure infrastructure posture…</span>
        </CardContent>
      </Card>
    );
  }

  const findings = (detail.findings ?? []).filter((f) => f.area === 'azure');
  const metrics: Record<string, string> = Object.fromEntries(
    (detail.metrics ?? [])
      .filter((m) => m.area === 'azure')
      .map((m) => [m.metricKey, m.metricValue]),
  );

  const areaScore =
    summary && 'areaScores' in summary && summary.areaScores
      ? summary.areaScores['azure'] ?? null
      : null;

  const subscriptionsScanned = parseMetric(metrics.subscriptions_scanned);
  const resourcesTotal = parseMetric(metrics.resources_total);
  const vm = parseMetric(metrics.resources_vm);
  const storage = parseMetric(metrics.resources_storage);
  const keyvault = parseMetric(metrics.resources_keyvault);
  const nsg = parseMetric(metrics.resources_nsg);
  const publicIp = parseMetric(metrics.resources_public_ip);

  const storagePublicBlob = parseMetric(metrics.storage_public_blob);
  const nsgAnyAnyAllow = parseMetric(metrics.nsg_any_any_allow);
  const keyvaultsNoSoftDelete = parseMetric(metrics.keyvaults_no_soft_delete);

  const healthy = parseMetric(metrics.assessments_healthy);
  const unhealthy = parseMetric(metrics.assessments_unhealthy);
  const notApplicable = parseMetric(metrics.assessments_not_applicable);
  const secureScorePct = parseFloatMetric(metrics.secure_score_pct);

  return (
    <div className="space-y-4">
      <HeaderKpiCard
        subscriptionsScanned={subscriptionsScanned}
        resourcesTotal={resourcesTotal}
        areaScore={areaScore}
        completedAt={detail.completedAt}
      />
      <PublicExposureCard
        storagePublicBlob={storagePublicBlob}
        nsgAnyAnyAllow={nsgAnyAnyAllow}
        keyvaultsNoSoftDelete={keyvaultsNoSoftDelete}
      />
      <div className="grid gap-4 lg:grid-cols-2">
        <ResourceBreakdownCard
          vm={vm}
          storage={storage}
          keyvault={keyvault}
          nsg={nsg}
          publicIp={publicIp}
          total={resourcesTotal}
        />
        <DefenderCard
          healthy={healthy}
          unhealthy={unhealthy}
          notApplicable={notApplicable}
          secureScorePct={secureScorePct}
        />
      </div>
      <FindingsTable findings={findings} />
      <SubscriptionsQuickRef orgId={orgId} subs={subs} onManage={onManageSubscriptions} />
    </div>
  );
}

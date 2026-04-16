import { useState } from 'react';
import {
  Shield,
  Share2,
  UserX,
  Lock,
  ShieldCheck,
  FileCheck,
  CheckCircle,
  AlertTriangle,
  XCircle,
  Loader2,
  RefreshCw,
  ExternalLink,
  ChevronDown,
  ChevronRight,
} from 'lucide-react';
import { toast } from 'sonner';
import {
  useCopilotReadiness,
  useCopilotReadinessDetail,
  useCopilotReadinessScan,
  type CopilotReadinessFinding,
} from '@/api/copilotReadiness';
import { useOrgParam } from '@/hooks/useOrgParam';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
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
import { API_BASE } from '@/auth/msalConfig';

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

function scoreColorClass(score: number | null): string {
  if (score === null) return 'bg-gray-100 text-gray-500';
  if (score <= 2) return 'bg-red-100 text-red-800';
  if (score === 3) return 'bg-amber-100 text-amber-800';
  return 'bg-green-100 text-green-800';
}

function scoreBorderClass(score: number | null): string {
  if (score === null) return 'border-gray-200';
  if (score <= 2) return 'border-red-200';
  if (score === 3) return 'border-amber-200';
  return 'border-green-200';
}

function findingStatusBadge(status: string) {
  const colors: Record<string, string> = {
    'Success': 'bg-green-100 text-green-800',
    'Action Required': 'bg-red-100 text-red-800',
    'Warning': 'bg-amber-100 text-amber-800',
    'Critical': 'bg-red-200 text-red-900',
    'Disabled': 'bg-gray-100 text-gray-800',
    'Not Licensed': 'bg-gray-100 text-gray-600',
    'Insight': 'bg-blue-100 text-blue-800',
  };
  return (
    <Badge variant="secondary" className={colors[status] ?? 'bg-gray-100 text-gray-500'}>
      {status}
    </Badge>
  );
}

function priorityPill(priority: string) {
  const colors: Record<string, string> = {
    'Critical': 'bg-red-100 text-red-800',
    'High': 'bg-orange-100 text-orange-800',
    'Medium': 'bg-amber-100 text-amber-800',
    'Low': 'bg-blue-100 text-blue-800',
    'Informational': 'bg-gray-100 text-gray-600',
  };
  return (
    <Badge variant="secondary" className={colors[priority] ?? 'bg-gray-100 text-gray-500'}>
      {priority}
    </Badge>
  );
}

function pipelineIcon(status: string) {
  if (status === 'ok' || status === 'completed') {
    return <CheckCircle className="h-4 w-4 text-green-500" />;
  }
  if (status === 'partial') {
    return <AlertTriangle className="h-4 w-4 text-amber-500" />;
  }
  return <XCircle className="h-4 w-4 text-red-500" />;
}

const serviceLabels: Record<string, string> = {
  entra: 'Entra ID',
  defender: 'Defender',
  m365: 'Microsoft 365',
  purview: 'Purview',
  power_platform: 'Power Platform',
  copilot_studio: 'Copilot Studio',
};

const dimensions = [
  { key: 'd1Score' as const, name: 'Information Protection', icon: Shield },
  { key: 'd2Score' as const, name: 'Data Sharing', icon: Share2 },
  { key: 'd3Score' as const, name: 'External Users', icon: UserX },
  { key: 'd4Score' as const, name: 'Conditional Access', icon: Lock },
  { key: 'd5Score' as const, name: 'Zero Trust', icon: ShieldCheck },
  { key: 'd6Score' as const, name: 'Compliance', icon: FileCheck },
];

// ── Service Group (accordion row) ──

function ServiceGroup({
  service,
  findings,
}: {
  service: string;
  findings: CopilotReadinessFinding[];
}) {
  const [open, setOpen] = useState(false);

  const successCount = findings.filter((f) => f.status === 'Success').length;
  const actionCount = findings.filter((f) => f.status === 'Action Required' || f.status === 'Critical').length;
  const warnCount = findings.filter((f) => f.status === 'Warning').length;

  return (
    <div className="border rounded-lg overflow-hidden">
      <button
        className="w-full flex items-center justify-between px-4 py-3 bg-gray-50 hover:bg-gray-100 transition-colors text-left"
        onClick={() => setOpen(!open)}
      >
        <div className="flex items-center gap-3">
          {open ? (
            <ChevronDown className="h-4 w-4 text-muted-foreground" />
          ) : (
            <ChevronRight className="h-4 w-4 text-muted-foreground" />
          )}
          <span className="font-medium text-sm">
            {serviceLabels[service] ?? service}
          </span>
          <span className="text-xs text-muted-foreground">
            {findings.length} checks
          </span>
        </div>
        <div className="flex items-center gap-2">
          {actionCount > 0 && (
            <Badge variant="secondary" className="bg-red-100 text-red-800 text-xs">
              {actionCount} action required
            </Badge>
          )}
          {warnCount > 0 && (
            <Badge variant="secondary" className="bg-amber-100 text-amber-800 text-xs">
              {warnCount} warning
            </Badge>
          )}
          {successCount > 0 && (
            <Badge variant="secondary" className="bg-green-100 text-green-800 text-xs">
              {successCount} ok
            </Badge>
          )}
        </div>
      </button>

      {open && (
        <div className="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead className="w-48">Feature</TableHead>
                <TableHead className="w-36">Status</TableHead>
                <TableHead className="w-28">Priority</TableHead>
                <TableHead>Observation</TableHead>
                <TableHead>Recommendation</TableHead>
                <TableHead className="w-20">Link</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {findings.map((f) => (
                <TableRow key={f.id}>
                  <TableCell className="text-sm font-medium">{f.feature}</TableCell>
                  <TableCell>{findingStatusBadge(f.status)}</TableCell>
                  <TableCell>{priorityPill(f.priority)}</TableCell>
                  <TableCell className="text-sm text-muted-foreground max-w-xs">
                    <div className="truncate" title={f.observation ?? undefined}>
                      {f.observation ?? '--'}
                    </div>
                  </TableCell>
                  <TableCell className="text-sm text-muted-foreground max-w-xs">
                    <div className="truncate" title={f.recommendation ?? undefined}>
                      {f.recommendation ?? '--'}
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
                        {f.linkText ?? 'Learn more'}
                      </a>
                    ) : (
                      '--'
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </div>
  );
}

// ── Main Component ──

export function CopilotReadinessTab() {
  const { orgId } = useOrgParam();
  const { data: summary, isLoading } = useCopilotReadiness(orgId);
  const scanMutation = useCopilotReadinessScan();

  const scan = 'id' in (summary ?? {}) ? (summary as any) : null;
  const scanId = scan?.id as string | undefined;

  const { data: detail } = useCopilotReadinessDetail(
    scan?.status === 'completed' || scan?.status === 'partial' ? scanId : undefined,
  );

  const isRunning = scan?.status === 'running';
  const notScanned = !summary || ('scanned' in summary && summary.scanned === false);

  const handleRunAssessment = () => {
    if (!orgId) return;
    scanMutation.mutate(orgId, {
      onSuccess: () => {
        toast.success('Assessment started. Results will appear in about 60 seconds.');
      },
      onError: (err: any) => {
        toast.error(`Failed to start assessment: ${err.message}`);
      },
    });
  };

  const handleExportReport = () => {
    if (!orgId) return;
    const url = `${API_BASE}/v2/reports/org/${orgId}?type=m365&lang=es`;
    window.open(url, '_blank');
  };

  // ── Loading state ──
  if (isLoading) {
    return (
      <div className="space-y-4">
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {Array.from({ length: 6 }).map((_, i) => (
            <Skeleton key={i} className="h-28" />
          ))}
        </div>
        <Skeleton className="h-48" />
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h3 className="text-lg font-semibold">Copilot Readiness Assessment</h3>
          <p className="text-sm text-muted-foreground">
            {scan?.completedAt
              ? <>Last assessment: {formatDate(scan.completedAt)}</>
              : scan?.startedAt
              ? <>Started: {formatDate(scan.startedAt)}</>
              : 'No assessment has been run yet'}
          </p>
        </div>
        <div className="flex items-center gap-2">
          {!notScanned && !isRunning && (
            <Button variant="outline" size="sm" onClick={handleExportReport}>
              <ExternalLink className="mr-1.5 h-4 w-4" />
              Export Report
            </Button>
          )}
          <Button
            size="sm"
            onClick={handleRunAssessment}
            disabled={scanMutation.isPending || isRunning}
          >
            {scanMutation.isPending || isRunning ? (
              <>
                <Loader2 className="mr-1.5 h-4 w-4 animate-spin" />
                {isRunning ? 'Assessment running...' : 'Starting...'}
              </>
            ) : (
              <>
                <RefreshCw className="mr-1.5 h-4 w-4" />
                {notScanned ? 'Run Assessment' : 'Re-run Assessment'}
              </>
            )}
          </Button>
        </div>
      </div>

      {/* Running state */}
      {isRunning && (
        <Card>
          <CardContent className="flex items-center gap-4 py-8">
            <Loader2 className="h-8 w-8 animate-spin text-blue-500 shrink-0" />
            <div>
              <p className="font-medium">Assessment in progress</p>
              <p className="text-sm text-muted-foreground">
                Evaluating your M365 tenant across 6 readiness dimensions. This typically takes 60-90 seconds.
              </p>
            </div>
          </CardContent>
        </Card>
      )}

      {/* Not scanned empty state */}
      {notScanned && !isRunning && !scanMutation.isPending && (
        <Card>
          <CardContent className="flex flex-col items-center justify-center py-16 text-center gap-4">
            <ShieldCheck className="h-12 w-12 text-muted-foreground" />
            <div>
              <p className="font-semibold text-lg">Run your first Copilot Readiness Assessment</p>
              <p className="text-sm text-muted-foreground mt-1 max-w-md">
                Evaluate whether your Microsoft 365 tenant is ready for Copilot deployment
                across 6 security and compliance dimensions.
              </p>
            </div>
            <Button onClick={handleRunAssessment} disabled={scanMutation.isPending}>
              <RefreshCw className="mr-1.5 h-4 w-4" />
              Run Assessment
            </Button>
          </CardContent>
        </Card>
      )}

      {/* Results — shown when scan exists and not in running/not-scanned state */}
      {scan && !isRunning && (
        <>
          {/* Overall score */}
          <div className="flex items-center gap-4">
            <div
              className={`rounded-xl border-2 px-6 py-4 text-center min-w-[120px] ${scoreColorClass(scan.overallScore)} ${scoreBorderClass(scan.overallScore)}`}
            >
              <div className="text-4xl font-bold">{scan.overallScore ?? '–'}</div>
              <div className="text-xs font-medium mt-1 opacity-75">out of 5</div>
            </div>
            <div>
              <p className="text-xl font-semibold">{scan.verdict ?? 'Assessment complete'}</p>
              <p className="text-sm text-muted-foreground mt-0.5">
                Overall Copilot Readiness Score
              </p>
              {scan.status === 'partial' && (
                <Badge variant="secondary" className="bg-amber-100 text-amber-800 mt-2">
                  Partial results — some pipelines failed
                </Badge>
              )}
            </div>
          </div>

          {/* Copilot license banner */}
          {detail?.findings && detail.findings.some(
            (f) => f.status === 'Not Licensed' && f.service === 'm365',
          ) && (
            <div className="rounded-lg border border-amber-200 bg-amber-50 p-4 text-sm text-amber-800 flex items-start gap-3">
              <AlertTriangle className="h-5 w-5 shrink-0 mt-0.5 text-amber-600" />
              <div>
                <p className="font-medium">No Microsoft 365 Copilot licenses detected</p>
                <p className="text-xs mt-1 text-amber-700">
                  Copilot licenses are required before deployment. Contact your Microsoft partner
                  or visit the Microsoft 365 admin center to assign licenses.
                </p>
              </div>
            </div>
          )}

          {/* 6 dimension cards */}
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {dimensions.map(({ key, name, icon: Icon }) => {
              const score = scan[key] as number | null;
              return (
                <Card key={key} className={`border-2 ${scoreBorderClass(score)}`}>
                  <CardHeader className="flex flex-row items-center justify-between pb-2 pt-4 px-4">
                    <CardTitle className="text-sm font-medium text-muted-foreground">
                      {name}
                    </CardTitle>
                    <Icon className="h-4 w-4 text-muted-foreground shrink-0" />
                  </CardHeader>
                  <CardContent className="px-4 pb-4">
                    <div className="flex items-end gap-2">
                      <span className={`text-3xl font-bold ${scoreColorClass(score).split(' ')[1]}`}>
                        {score ?? '–'}
                      </span>
                      <span className="text-sm text-muted-foreground mb-1">/ 5</span>
                    </div>
                    {/* Score bar */}
                    <div className="mt-2 h-1.5 rounded-full bg-gray-100 overflow-hidden">
                      <div
                        className={`h-full rounded-full transition-all ${
                          score === null ? '' :
                          score <= 2 ? 'bg-red-400' :
                          score === 3 ? 'bg-amber-400' :
                          'bg-green-500'
                        }`}
                        style={{ width: score !== null ? `${(score / 5) * 100}%` : '0%' }}
                      />
                    </div>
                  </CardContent>
                </Card>
              );
            })}
          </div>

          {/* Pipeline status row */}
          {scan.pipelineStatus && Object.keys(scan.pipelineStatus).length > 0 && (
            <Card>
              <CardHeader className="pb-2">
                <CardTitle className="text-sm font-medium text-muted-foreground">
                  Pipeline Status
                </CardTitle>
              </CardHeader>
              <CardContent>
                <div className="flex flex-wrap gap-4">
                  {Object.entries(scan.pipelineStatus as Record<string, string>).map(
                    ([pipeline, status]) => (
                      <div key={pipeline} className="flex items-center gap-1.5">
                        {pipelineIcon(status)}
                        <span className="text-sm">
                          {serviceLabels[pipeline] ?? pipeline}
                        </span>
                      </div>
                    ),
                  )}
                </div>
              </CardContent>
            </Card>
          )}

          {/* Findings by service */}
          {detail?.findings && detail.findings.length > 0 && (
            <div className="space-y-2">
              <h4 className="text-base font-semibold">Findings by Service</h4>
              {Object.entries(
                detail.findings.reduce<Record<string, CopilotReadinessFinding[]>>(
                  (acc, f) => {
                    if (!acc[f.service]) acc[f.service] = [];
                    acc[f.service].push(f);
                    return acc;
                  },
                  {},
                ),
              ).map(([service, findings]) => (
                <ServiceGroup key={service} service={service} findings={findings} />
              ))}
            </div>
          )}
        </>
      )}
    </div>
  );
}

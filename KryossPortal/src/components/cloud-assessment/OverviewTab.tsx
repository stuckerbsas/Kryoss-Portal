import { useState } from 'react';
import {
  Cloud,
  Shield,
  Database,
  Users,
  Activity,
  TrendingUp,
  TrendingDown,
  CheckCircle,
  AlertTriangle,
  XCircle,
  MinusCircle,
  Loader2,
  RefreshCw,
  GitCompare,
} from 'lucide-react';
import { toast } from 'sonner';
import {
  useCloudAssessment,
  useCloudAssessmentDetail,
  useCloudAssessmentHistory,
  useCloudAssessmentScan,
  type CloudAssessmentFinding,
} from '@/api/cloudAssessment';
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
import {
  RadarChart,
  Radar,
  PolarGrid,
  PolarAngleAxis,
  PolarRadiusAxis,
  ResponsiveContainer,
} from 'recharts';
import { TimelineCard } from './TimelineCard';
import { CompareModal } from './CompareModal';

const AREAS = [
  { key: 'identity', label: 'Identity', icon: Users },
  { key: 'endpoint', label: 'Endpoint', icon: Shield },
  { key: 'data', label: 'Data', icon: Database },
  { key: 'productivity', label: 'Productivity', icon: Activity },
  { key: 'azure', label: 'Azure', icon: Cloud },
] as const;

function scoreColor(score: number | null): string {
  if (score === null) return 'text-gray-400';
  if (score >= 4) return 'text-green-600';
  if (score >= 3) return 'text-amber-600';
  return 'text-red-600';
}

function scoreBorderClass(score: number | null): string {
  if (score === null) return 'border-gray-200';
  if (score >= 4) return 'border-green-200';
  if (score >= 3) return 'border-amber-200';
  return 'border-red-200';
}

function scoreBarBg(score: number | null): string {
  if (score === null) return 'bg-gray-200';
  if (score >= 4) return 'bg-green-500';
  if (score >= 3) return 'bg-amber-400';
  return 'bg-red-500';
}

function verdictLabel(verdict: string | null, score: number | null): string {
  if (verdict) return verdict;
  if (score === null) return 'Not scored';
  if (score >= 4) return 'Ready';
  if (score >= 3) return 'Nearly Ready';
  return 'Not Ready';
}

function pipelineIcon(status: string) {
  if (status === 'ok' || status === 'completed') return <CheckCircle className="h-4 w-4 text-green-500" />;
  if (status === 'partial') return <AlertTriangle className="h-4 w-4 text-amber-500" />;
  if (status === 'skipped' || status === 'disabled') return <MinusCircle className="h-4 w-4 text-gray-400" />;
  return <XCircle className="h-4 w-4 text-red-500" />;
}

function formatDate(s: string | null): string {
  if (!s) return '—';
  return new Date(s).toLocaleString(undefined, { year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
}

function priorityBadge(priority: string) {
  const colors: Record<string, string> = {
    'Critical': 'bg-red-100 text-red-800',
    'High': 'bg-orange-100 text-orange-800',
    'Medium': 'bg-amber-100 text-amber-800',
    'Low': 'bg-blue-100 text-blue-800',
    'Informational': 'bg-gray-100 text-gray-600',
  };
  return <Badge variant="secondary" className={colors[priority] ?? 'bg-gray-100 text-gray-500'}>{priority}</Badge>;
}

const PIPELINE_LABELS: Record<string, string> = {
  identity: 'Identity',
  endpoint: 'Endpoint',
  data: 'Data',
  productivity: 'Productivity',
  azure: 'Azure',
};

interface OverviewTabProps {
  orgId: string;
}

export function OverviewTab({ orgId }: OverviewTabProps) {
  const { data: summary, isLoading } = useCloudAssessment(orgId);
  const scanMutation = useCloudAssessmentScan();
  const [compareOpen, setCompareOpen] = useState(false);

  const scan = summary && 'id' in summary ? summary : null;
  const scanId = scan?.id;
  const isRunning = scan?.status === 'running';
  const notScanned = !summary || ('scanned' in summary && summary.scanned === false);

  const { data: detail } = useCloudAssessmentDetail(
    scan && (scan.status === 'completed' || scan.status === 'partial') ? scanId : undefined,
  );
  const { data: history } = useCloudAssessmentHistory(orgId, 20);

  const completedHistory = (history ?? []).filter(h => h.status === 'completed' || h.status === 'partial');
  const canCompare = completedHistory.length >= 2;

  // Delta vs previous completed scan.
  const previousScore = completedHistory.length >= 2 ? completedHistory[1]?.overallScore ?? null : null;
  const currentScore = scan?.overallScore ?? null;
  const scoreDelta =
    previousScore !== null && currentScore !== null ? currentScore - previousScore : null;

  const handleRunScan = () => {
    if (!orgId) return;
    scanMutation.mutate(
      { organizationId: orgId },
      {
        onSuccess: () => {
          toast.success('Scan started. Results will appear shortly.');
        },
        onError: (err: any) => {
          toast.error(`Failed to start scan: ${err.message}`);
        },
      },
    );
  };

  if (isLoading) {
    return (
      <div className="space-y-4">
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {Array.from({ length: 4 }).map((_, i) => (
            <Skeleton key={i} className="h-28" />
          ))}
        </div>
        <Skeleton className="h-64" />
      </div>
    );
  }

  // Radar data.
  const radarData = AREAS.map(a => ({
    area: a.label,
    score: scan?.areaScores?.[a.key] ?? 0,
    fullMark: 5,
  }));

  // Top findings (Critical + High), Critical first.
  const topFindings: CloudAssessmentFinding[] = ((detail?.findings ?? []) as CloudAssessmentFinding[])
    .filter(f => f.priority === 'Critical' || f.priority === 'High')
    .sort((a, b) => {
      if (a.priority === b.priority) return 0;
      return a.priority === 'Critical' ? -1 : 1;
    })
    .slice(0, 10);

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between gap-4 flex-wrap">
        <div>
          <h3 className="text-lg font-semibold flex items-center gap-2">
            <Cloud className="h-5 w-5" /> Cloud Assessment
          </h3>
          <p className="text-sm text-muted-foreground">
            {scan?.completedAt
              ? <>Last scan: {formatDate(scan.completedAt)}</>
              : scan?.startedAt
              ? <>Started: {formatDate(scan.startedAt)}</>
              : 'No scan has been run yet'}
          </p>
        </div>
        <div className="flex items-center gap-2">
          {canCompare && (
            <Button variant="outline" size="sm" onClick={() => setCompareOpen(true)}>
              <GitCompare className="mr-1.5 h-4 w-4" />
              Compare
            </Button>
          )}
          {!notScanned && (
            <Button
              size="sm"
              onClick={handleRunScan}
              disabled={scanMutation.isPending || isRunning}
            >
              {scanMutation.isPending || isRunning ? (
                <>
                  <Loader2 className="mr-1.5 h-4 w-4 animate-spin" />
                  {isRunning ? 'Scan running...' : 'Starting...'}
                </>
              ) : (
                <>
                  <RefreshCw className="mr-1.5 h-4 w-4" />
                  Re-run Scan
                </>
              )}
            </Button>
          )}
        </div>
      </div>

      {/* Status banners */}
      {isRunning && (
        <Card>
          <CardContent className="flex items-center gap-4 py-8">
            <Loader2 className="h-8 w-8 animate-spin text-blue-500 shrink-0" />
            <div>
              <p className="font-medium">Scan in progress</p>
              <p className="text-sm text-muted-foreground">
                Evaluating your cloud environment across Identity, Endpoint, Data, and Productivity pipelines.
                This typically takes a minute or two.
              </p>
            </div>
          </CardContent>
        </Card>
      )}

      {scan?.status === 'failed' && (
        <div className="rounded-lg border border-red-200 bg-red-50 p-4 text-sm text-red-800 flex items-start gap-3">
          <XCircle className="h-5 w-5 shrink-0 mt-0.5 text-red-600" />
          <div>
            <p className="font-medium">Scan failed</p>
            <p className="text-xs mt-1 text-red-700">
              The scan completed with errors. Try running again, or check pipeline status below.
            </p>
          </div>
        </div>
      )}

      {scan?.status === 'partial' && (
        <div className="rounded-lg border border-amber-200 bg-amber-50 p-4 text-sm text-amber-800 flex items-start gap-3">
          <AlertTriangle className="h-5 w-5 shrink-0 mt-0.5 text-amber-600" />
          <div>
            <p className="font-medium">Partial results</p>
            <p className="text-xs mt-1 text-amber-700">
              Some pipelines failed. Scores and findings shown reflect only the pipelines that ran successfully.
            </p>
          </div>
        </div>
      )}

      {/* Empty state */}
      {notScanned && !isRunning && !scanMutation.isPending && (
        <Card>
          <CardContent className="flex flex-col items-center justify-center py-16 text-center gap-4">
            <Cloud className="h-12 w-12 text-muted-foreground" />
            <div>
              <p className="font-semibold text-lg">Run your first Cloud Assessment</p>
              <p className="text-sm text-muted-foreground mt-1 max-w-md">
                Evaluate your cloud security posture across Identity, Endpoint, Data, and Productivity.
              </p>
            </div>
            <Button onClick={handleRunScan} disabled={scanMutation.isPending}>
              <RefreshCw className="mr-1.5 h-4 w-4" />
              Run First Scan
            </Button>
          </CardContent>
        </Card>
      )}

      {/* Results */}
      {scan && !isRunning && (scan.status === 'completed' || scan.status === 'partial') && (
        <>
          {/* Overall score hero */}
          <Card>
            <CardContent className="py-6">
              <div className="flex items-center gap-6 flex-wrap">
                <div
                  className={`rounded-xl border-2 px-6 py-4 text-center min-w-[120px] ${scoreBorderClass(currentScore)}`}
                >
                  <div className={`text-4xl font-bold ${scoreColor(currentScore)}`}>
                    {currentScore !== null ? currentScore.toFixed(1) : '–'}
                  </div>
                  <div className="text-xs font-medium mt-1 text-muted-foreground">out of 5</div>
                </div>
                <div className="flex-1 min-w-0">
                  <p className="text-xl font-semibold">{verdictLabel(scan.verdict, currentScore)}</p>
                  <p className="text-sm text-muted-foreground mt-0.5">Overall Cloud Posture Score</p>
                  {scoreDelta !== null && (
                    <div className="mt-2 flex items-center gap-1.5 text-sm">
                      {scoreDelta > 0 ? (
                        <>
                          <TrendingUp className="h-4 w-4 text-green-600" />
                          <span className="text-green-600 font-medium">+{scoreDelta.toFixed(2)}</span>
                        </>
                      ) : scoreDelta < 0 ? (
                        <>
                          <TrendingDown className="h-4 w-4 text-red-600" />
                          <span className="text-red-600 font-medium">{scoreDelta.toFixed(2)}</span>
                        </>
                      ) : (
                        <>
                          <MinusCircle className="h-4 w-4 text-muted-foreground" />
                          <span className="text-muted-foreground">No change</span>
                        </>
                      )}
                      <span className="text-muted-foreground text-xs">vs previous scan</span>
                    </div>
                  )}
                </div>
              </div>
            </CardContent>
          </Card>

          {/* Radar chart */}
          <Card>
            <CardHeader>
              <CardTitle className="text-sm">Area Breakdown</CardTitle>
            </CardHeader>
            <CardContent>
              <ResponsiveContainer width="100%" height={300}>
                <RadarChart data={radarData}>
                  <PolarGrid />
                  <PolarAngleAxis dataKey="area" tick={{ fontSize: 12 }} />
                  <PolarRadiusAxis angle={90} domain={[0, 5]} tick={{ fontSize: 10 }} />
                  <Radar
                    name="Current"
                    dataKey="score"
                    stroke="#008852"
                    fill="#008852"
                    fillOpacity={0.35}
                  />
                </RadarChart>
              </ResponsiveContainer>
            </CardContent>
          </Card>

          {/* Area cards */}
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
            {AREAS.map(({ key, label, icon: Icon }) => {
              const score = scan.areaScores?.[key] ?? null;
              return (
                <Card key={key} className={`border-2 ${scoreBorderClass(score)}`}>
                  <CardHeader className="flex flex-row items-center justify-between pb-2 pt-4 px-4">
                    <CardTitle className="text-sm font-medium text-muted-foreground">
                      {label}
                    </CardTitle>
                    <Icon className="h-4 w-4 text-muted-foreground shrink-0" />
                  </CardHeader>
                  <CardContent className="px-4 pb-4">
                    <div className="flex items-end gap-2">
                      <span className={`text-3xl font-bold ${scoreColor(score)}`}>
                        {score !== null ? score.toFixed(1) : '–'}
                      </span>
                      <span className="text-sm text-muted-foreground mb-1">/ 5</span>
                    </div>
                    <div className="mt-2 h-1.5 rounded-full bg-gray-100 overflow-hidden">
                      <div
                        className={`h-full rounded-full transition-all ${scoreBarBg(score)}`}
                        style={{ width: score !== null ? `${(score / 5) * 100}%` : '0%' }}
                      />
                    </div>
                  </CardContent>
                </Card>
              );
            })}
          </div>

          {/* Timeline */}
          <TimelineCard organizationId={orgId} />

          {/* Pipeline status */}
          {scan.pipelineStatus && Object.keys(scan.pipelineStatus).length > 0 && (
            <Card>
              <CardHeader className="pb-2">
                <CardTitle className="text-sm font-medium text-muted-foreground">
                  Pipeline Status
                </CardTitle>
              </CardHeader>
              <CardContent>
                <div className="flex flex-wrap gap-4">
                  {Object.entries(scan.pipelineStatus).map(([pipeline, status]) => (
                    <div key={pipeline} className="flex items-center gap-1.5">
                      {pipelineIcon(status)}
                      <span className="text-sm">{PIPELINE_LABELS[pipeline] ?? pipeline}</span>
                      <span className="text-xs text-muted-foreground">({status})</span>
                    </div>
                  ))}
                </div>
              </CardContent>
            </Card>
          )}

          {/* Top findings */}
          {topFindings.length > 0 && (
            <Card>
              <CardHeader>
                <CardTitle className="text-sm flex items-center gap-2">
                  <AlertTriangle className="h-4 w-4 text-amber-500" />
                  Top Findings
                </CardTitle>
              </CardHeader>
              <CardContent className="p-0 overflow-x-auto">
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Area</TableHead>
                      <TableHead>Service</TableHead>
                      <TableHead>Feature</TableHead>
                      <TableHead>Priority</TableHead>
                      <TableHead>Observation</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {topFindings.map((f, i) => (
                      <TableRow key={`${f.area}-${f.service}-${f.feature}-${i}`}>
                        <TableCell className="text-sm capitalize">{f.area}</TableCell>
                        <TableCell className="text-sm font-medium whitespace-nowrap">{f.service}</TableCell>
                        <TableCell className="text-sm">{f.feature}</TableCell>
                        <TableCell>{priorityBadge(f.priority)}</TableCell>
                        <TableCell className="text-sm text-muted-foreground max-w-md">
                          <div className="truncate" title={f.observation ?? undefined}>
                            {f.observation ?? '—'}
                          </div>
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

      {orgId && (
        <CompareModal
          organizationId={orgId}
          open={compareOpen}
          onOpenChange={setCompareOpen}
        />
      )}
    </div>
  );
}

import { useState } from 'react';
import {
  Activity,
  AlertTriangle,
  CheckCircle,
  ChevronDown,
  ChevronRight,
  Router,
  Signal,
  XCircle,
} from 'lucide-react';
import { useWanHealth } from '@/api/networkSites';
import type { WanSiteSummary, WanFinding } from '@/api/networkSites';
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

function scoreColor(score: number | null) {
  if (score == null) return 'text-muted-foreground';
  if (score >= 80) return 'text-green-600';
  if (score >= 60) return 'text-amber-600';
  return 'text-red-600';
}

function scoreBg(score: number | null) {
  if (score == null) return 'bg-gray-100';
  if (score >= 80) return 'bg-green-100';
  if (score >= 60) return 'bg-amber-100';
  return 'bg-red-100';
}

function scoreLabel(score: number | null) {
  if (score == null) return 'N/A';
  if (score >= 90) return 'Excellent';
  if (score >= 80) return 'Good';
  if (score >= 60) return 'Fair';
  if (score >= 40) return 'Poor';
  return 'Critical';
}

function severityBadge(severity: string) {
  const styles: Record<string, string> = {
    critical: 'bg-red-100 text-red-800',
    warning: 'bg-amber-100 text-amber-800',
    info: 'bg-blue-100 text-blue-800',
  };
  return (
    <Badge className={styles[severity] ?? 'bg-gray-100 text-gray-800'}>
      {severity}
    </Badge>
  );
}

function metricCell(value: number | null, unit: string, warnAbove?: number, critAbove?: number) {
  if (value == null) return <span className="text-muted-foreground">--</span>;
  let color = 'text-green-600';
  if (critAbove != null && value > critAbove) color = 'text-red-600';
  else if (warnAbove != null && value > warnAbove) color = 'text-amber-600';
  return <span className={color}>{value.toFixed(1)} {unit}</span>;
}

function ScoreGauge({ score }: { score: number | null }) {
  const display = score != null ? Math.round(score) : '--';
  return (
    <div className={`flex flex-col items-center gap-1 rounded-xl p-4 ${scoreBg(score)}`}>
      <span className={`text-4xl font-bold ${scoreColor(score)}`}>{display}</span>
      <span className={`text-sm font-medium ${scoreColor(score)}`}>{scoreLabel(score)}</span>
      <span className="text-xs text-muted-foreground">WAN Score</span>
    </div>
  );
}

function FindingsRow({ findings }: { findings: WanFinding[] }) {
  if (findings.length === 0) return null;
  return (
    <div className="mt-3 space-y-1">
      {findings.map((f, i) => (
        <div key={i} className="flex items-center gap-2 text-sm">
          {severityBadge(f.severity)}
          <span className="font-medium">{f.title}</span>
          {f.detail && <span className="text-muted-foreground">{f.detail}</span>}
        </div>
      ))}
    </div>
  );
}

function SiteCard({ site }: { site: WanSiteSummary }) {
  const [expanded, setExpanded] = useState(false);
  const criticals = site.findings.filter((f) => f.severity === 'critical').length;
  const warnings = site.findings.filter((f) => f.severity === 'warning').length;

  return (
    <Card className={criticals > 0 ? 'border-red-200' : warnings > 0 ? 'border-amber-200' : ''}>
      <CardHeader className="pb-2 cursor-pointer" onClick={() => setExpanded(!expanded)}>
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-3">
            {expanded ? <ChevronDown className="size-4" /> : <ChevronRight className="size-4" />}
            <div>
              <CardTitle className="text-base">{site.siteName}</CardTitle>
              <p className="text-xs text-muted-foreground">
                {site.publicIp} {site.geoCity ? `- ${site.geoCity}` : ''} {site.isp ? `(${site.isp})` : ''}
              </p>
            </div>
          </div>
          <div className="flex items-center gap-4">
            {criticals > 0 && (
              <Badge className="bg-red-100 text-red-800">{criticals} critical</Badge>
            )}
            {warnings > 0 && (
              <Badge className="bg-amber-100 text-amber-800">{warnings} warning</Badge>
            )}
            <div className={`text-2xl font-bold ${scoreColor(site.wanScore)}`}>
              {site.wanScore != null ? Math.round(site.wanScore) : '--'}
            </div>
          </div>
        </div>
      </CardHeader>
      {expanded && (
        <CardContent>
          <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-4 lg:grid-cols-6 gap-4 text-sm">
            <div>
              <p className="text-muted-foreground">Latency</p>
              <p className="font-medium">{metricCell(site.avgLatencyMs, 'ms', 80, 150)}</p>
            </div>
            <div>
              <p className="text-muted-foreground">Jitter</p>
              <p className="font-medium">{metricCell(site.avgJitterMs, 'ms', 10, 30)}</p>
            </div>
            <div>
              <p className="text-muted-foreground">Packet Loss</p>
              <p className="font-medium">{metricCell(site.avgPacketLossPct, '%', 1, 3)}</p>
            </div>
            <div>
              <p className="text-muted-foreground">Download</p>
              <p className="font-medium">
                {site.avgDownMbps != null ? `${site.avgDownMbps.toFixed(1)} Mbps` : '--'}
              </p>
            </div>
            <div>
              <p className="text-muted-foreground">Upload</p>
              <p className="font-medium">
                {site.avgUpMbps != null ? `${site.avgUpMbps.toFixed(1)} Mbps` : '--'}
              </p>
            </div>
            <div>
              <p className="text-muted-foreground">Hop Count</p>
              <p className="font-medium">{site.hopCount ?? '--'}</p>
            </div>
          </div>
          <div className="grid grid-cols-2 md:grid-cols-4 gap-4 text-sm mt-3">
            <div>
              <p className="text-muted-foreground">Link Type</p>
              <p className="font-medium">{site.linkType ?? '--'}</p>
            </div>
            <div>
              <p className="text-muted-foreground">Redundant</p>
              <p className="font-medium">{site.isRedundant ? 'Yes' : 'No'}</p>
            </div>
            <div>
              <p className="text-muted-foreground">Agents</p>
              <p className="font-medium">{site.agentCount}</p>
            </div>
            <div>
              <p className="text-muted-foreground">Monthly Cost</p>
              <p className="font-medium">
                {site.monthlyCost != null ? `$${site.monthlyCost.toFixed(0)}` : '--'}
              </p>
            </div>
          </div>
          <FindingsRow findings={site.findings} />
        </CardContent>
      )}
    </Card>
  );
}

export function WanHealthTab() {
  const { orgId } = useOrgParam();
  const { data, isLoading } = useWanHealth(orgId);

  if (isLoading) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-8 w-64" />
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
          {[1, 2, 3, 4].map((i) => <Skeleton key={i} className="h-24" />)}
        </div>
      </div>
    );
  }

  if (!data || data.sites.length === 0) {
    return (
      <EmptyState
        icon={<Activity className="size-10" />}
        title="No WAN Health Data"
        description="WAN health metrics are collected from agent network diagnostics. Run an assessment and rebuild sites first."
      />
    );
  }

  const criticals = data.summary.find((s) => s.severity === 'critical')?.count ?? 0;
  const warnings = data.summary.find((s) => s.severity === 'warning')?.count ?? 0;
  const healthySites = data.sites.filter((s) => s.wanScore != null && s.wanScore >= 80).length;

  return (
    <div className="space-y-6">
      <h2 className="text-lg font-semibold">WAN Health</h2>

      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-5 gap-4">
        <ScoreGauge score={data.orgScore} />
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Sites</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="flex items-center gap-2">
              <Router className="size-5 text-primary" />
              <span className="text-2xl font-bold">{data.sites.length}</span>
            </div>
            <p className="text-xs text-muted-foreground mt-1">{healthySites} healthy</p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Critical Issues</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="flex items-center gap-2">
              {criticals > 0 ? <XCircle className="size-5 text-red-600" /> : <CheckCircle className="size-5 text-green-600" />}
              <span className={`text-2xl font-bold ${criticals > 0 ? 'text-red-600' : 'text-green-600'}`}>
                {criticals}
              </span>
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Warnings</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="flex items-center gap-2">
              {warnings > 0 ? <AlertTriangle className="size-5 text-amber-600" /> : <CheckCircle className="size-5 text-green-600" />}
              <span className={`text-2xl font-bold ${warnings > 0 ? 'text-amber-600' : 'text-green-600'}`}>
                {warnings}
              </span>
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Avg Latency</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="flex items-center gap-2">
              <Signal className="size-5 text-primary" />
              <span className="text-2xl font-bold">
                {data.sites.filter(s => s.avgLatencyMs != null).length > 0
                  ? `${(data.sites.reduce((a, s) => a + (s.avgLatencyMs ?? 0), 0) / data.sites.filter(s => s.avgLatencyMs != null).length).toFixed(0)} ms`
                  : '--'}
              </span>
            </div>
          </CardContent>
        </Card>
      </div>

      {/* All findings summary table */}
      {(criticals > 0 || warnings > 0) && (
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium">All Findings</CardTitle>
          </CardHeader>
          <CardContent>
            {/* Mobile cards */}
            <div className="space-y-3 sm:hidden">
              {data.sites.flatMap((s) =>
                s.findings.map((f, i) => (
                  <div key={`${s.id}-${i}`} className="rounded-lg border p-4 space-y-1">
                    <div className="flex items-center justify-between gap-2">
                      <span className="font-medium text-sm truncate">{s.siteName}</span>
                      {severityBadge(f.severity)}
                    </div>
                    <p className="text-xs text-muted-foreground">{f.title}</p>
                    {f.detail && <p className="text-xs text-muted-foreground truncate">{f.detail}</p>}
                  </div>
                )),
              )}
            </div>
            {/* Desktop table */}
            <div className="hidden sm:block overflow-x-auto">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Severity</TableHead>
                    <TableHead>Site</TableHead>
                    <TableHead>Finding</TableHead>
                    <TableHead className="hidden lg:table-cell">Detail</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {data.sites.flatMap((s) =>
                    s.findings.map((f, i) => (
                      <TableRow key={`${s.id}-${i}`}>
                        <TableCell>{severityBadge(f.severity)}</TableCell>
                        <TableCell className="font-medium">{s.siteName}</TableCell>
                        <TableCell>{f.title}</TableCell>
                        <TableCell className="text-sm text-muted-foreground hidden lg:table-cell">{f.detail ?? '--'}</TableCell>
                      </TableRow>
                    )),
                  )}
                </TableBody>
              </Table>
            </div>
          </CardContent>
        </Card>
      )}

      {/* Per-site cards */}
      <div className="space-y-3">
        {data.sites.map((site) => (
          <SiteCard key={site.id} site={site} />
        ))}
      </div>
    </div>
  );
}

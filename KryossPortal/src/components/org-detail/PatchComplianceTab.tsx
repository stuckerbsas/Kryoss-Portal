import {
  AlertTriangle,
  CheckCircle,
  Monitor,
  RefreshCw,
  Shield,
  XCircle,
} from 'lucide-react';
import { usePatchCompliance } from '@/api/patchCompliance';
import type { PatchMachineSummary } from '@/api/patchCompliance';
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

function scoreColor(score: number) {
  if (score >= 80) return 'text-green-600';
  if (score >= 60) return 'text-amber-600';
  return 'text-red-600';
}

function scoreBadge(score: number) {
  const style =
    score >= 80
      ? 'bg-green-100 text-green-800'
      : score >= 60
        ? 'bg-amber-100 text-amber-800'
        : 'bg-red-100 text-red-800';
  return <Badge className={style}>{score}%</Badge>;
}

function sourceBadge(source: string | null) {
  const styles: Record<string, string> = {
    wsus: 'bg-blue-100 text-blue-800',
    wufb: 'bg-purple-100 text-purple-800',
    ninja: 'bg-indigo-100 text-indigo-800',
    standalone: 'bg-gray-100 text-gray-800',
    unknown: 'bg-gray-100 text-gray-500',
  };
  return (
    <Badge className={styles[source ?? 'unknown'] ?? 'bg-gray-100 text-gray-800'}>
      {source === 'wsus'
        ? 'WSUS'
        : source === 'wufb'
          ? 'WUfB'
          : source === 'ninja'
            ? 'NinjaOne'
            : source ?? 'Unknown'}
    </Badge>
  );
}

function daysAgo(dateStr: string | null) {
  if (!dateStr) return 'Never';
  const days = Math.floor(
    (Date.now() - new Date(dateStr).getTime()) / 86400000,
  );
  if (days === 0) return 'Today';
  if (days === 1) return '1 day ago';
  return `${days}d ago`;
}

export function PatchComplianceTab() {
  const { orgId } = useOrgParam();
  const { data, isLoading } = usePatchCompliance(orgId);

  if (isLoading) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-8 w-64" />
        <div className="grid grid-cols-4 gap-4">
          {[1, 2, 3, 4].map((i) => (
            <Skeleton key={i} className="h-24" />
          ))}
        </div>
      </div>
    );
  }

  if (!data || data.reportingMachines === 0) {
    return (
      <EmptyState
        icon={<Shield className="size-10" />}
        title="No Patch Data"
        description="Patch compliance data will appear after agents report their Windows Update status. Requires agent v2.6.0+."
      />
    );
  }

  return (
    <div className="space-y-6">
      <h2 className="text-lg font-semibold">Patch Compliance</h2>

      {/* KPI Cards */}
      <div className="grid grid-cols-2 lg:grid-cols-5 gap-4">
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">
              Avg Compliance
            </CardTitle>
          </CardHeader>
          <CardContent>
            <span className={`text-2xl font-bold ${scoreColor(data.avgComplianceScore)}`}>
              {data.avgComplianceScore}%
            </span>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">
              Reporting
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="flex items-center gap-2">
              <Monitor className="size-5 text-primary" />
              <span className="text-2xl font-bold">
                {data.reportingMachines} / {data.totalMachines}
              </span>
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">
              Reboot Pending
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="flex items-center gap-2">
              {data.rebootPending > 0 ? (
                <RefreshCw className="size-5 text-amber-600" />
              ) : (
                <CheckCircle className="size-5 text-green-600" />
              )}
              <span
                className={`text-2xl font-bold ${data.rebootPending > 0 ? 'text-amber-600' : 'text-green-600'}`}
              >
                {data.rebootPending}
              </span>
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">
              Unmanaged
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="flex items-center gap-2">
              {data.unmanaged > 0 ? (
                <AlertTriangle className="size-5 text-orange-600" />
              ) : (
                <CheckCircle className="size-5 text-green-600" />
              )}
              <span
                className={`text-2xl font-bold ${data.unmanaged > 0 ? 'text-orange-600' : 'text-green-600'}`}
              >
                {data.unmanaged}
              </span>
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">
              WU Stopped
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="flex items-center gap-2">
              {data.wuStopped > 0 ? (
                <XCircle className="size-5 text-red-600" />
              ) : (
                <CheckCircle className="size-5 text-green-600" />
              )}
              <span
                className={`text-2xl font-bold ${data.wuStopped > 0 ? 'text-red-600' : 'text-green-600'}`}
              >
                {data.wuStopped}
              </span>
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Source Distribution */}
      {data.sourceDistribution.length > 0 && (
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium">
              Update Source Distribution
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="flex gap-6">
              {data.sourceDistribution.map((s) => (
                <div key={s.source} className="flex items-center gap-2">
                  {sourceBadge(s.source)}
                  <span className="text-lg font-semibold">{s.count}</span>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>
      )}

      {/* Machine Table */}
      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-sm font-medium">
            Per-Machine Patch Status
          </CardTitle>
        </CardHeader>
        <CardContent>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Machine</TableHead>
                <TableHead>OS</TableHead>
                <TableHead>Source</TableHead>
                <TableHead>Last Check</TableHead>
                <TableHead>Last Install</TableHead>
                <TableHead>30d Patches</TableHead>
                <TableHead>Reboot</TableHead>
                <TableHead>Score</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.machines.map((m: PatchMachineSummary) => (
                <TableRow key={m.id}>
                  <TableCell className="font-medium">{m.hostname}</TableCell>
                  <TableCell className="text-sm text-muted-foreground max-w-40 truncate">
                    {m.osName ?? '--'}
                  </TableCell>
                  <TableCell>{sourceBadge(m.updateSource)}</TableCell>
                  <TableCell className="text-sm">{daysAgo(m.lastCheckUtc)}</TableCell>
                  <TableCell className="text-sm">{daysAgo(m.lastInstallUtc)}</TableCell>
                  <TableCell className="text-sm">{m.installedCount30d}</TableCell>
                  <TableCell>
                    {m.rebootPending ? (
                      <Badge className="bg-amber-100 text-amber-800">Yes</Badge>
                    ) : (
                      <span className="text-muted-foreground text-sm">No</span>
                    )}
                  </TableCell>
                  <TableCell>{scoreBadge(m.complianceScore)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
    </div>
  );
}

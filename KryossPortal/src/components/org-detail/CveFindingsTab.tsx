import { useState } from 'react';
import {
  AlertTriangle,
  Bug,
  CheckCircle,
  Loader2,
  Monitor,
  RefreshCw,
  Shield,
  XCircle,
} from 'lucide-react';
import { toast } from 'sonner';
import { useCveFindings, useCveStats, useCveRescan, useDismissCve } from '@/api/cveFindings';
import type { CveFinding } from '@/api/cveFindings';
import { useOrgParam } from '@/hooks/useOrgParam';
import { EmptyState } from '@/components/shared/EmptyState';
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
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';

function severityBadge(severity: string) {
  const styles: Record<string, string> = {
    critical: 'bg-red-100 text-red-800',
    high: 'bg-orange-100 text-orange-800',
    medium: 'bg-amber-100 text-amber-800',
    low: 'bg-blue-100 text-blue-800',
  };
  return (
    <Badge className={styles[severity] ?? 'bg-gray-100 text-gray-800'}>
      {severity}
    </Badge>
  );
}

function cvssColor(score: number | null) {
  if (score == null) return 'text-muted-foreground';
  if (score >= 9.0) return 'text-red-600 font-bold';
  if (score >= 7.0) return 'text-orange-600 font-semibold';
  if (score >= 4.0) return 'text-amber-600';
  return 'text-blue-600';
}

export function CveFindingsTab() {
  const { orgId } = useOrgParam();
  const [severityFilter, setSeverityFilter] = useState<string | undefined>(undefined);
  const { data, isLoading } = useCveFindings(orgId, severityFilter);
  const { data: stats } = useCveStats(orgId);
  const rescan = useCveRescan(orgId);
  const dismiss = useDismissCve(orgId);

  const handleRescan = () => {
    rescan.mutate(undefined, {
      onSuccess: (r) => toast.success(`CVE scan complete: ${r.findingsCount} findings`),
      onError: (e: Error) => toast.error(e.message),
    });
  };

  const handleDismiss = (finding: CveFinding) => {
    dismiss.mutate(finding.id, {
      onSuccess: () => toast.success(`${finding.cveId} dismissed`),
    });
  };

  if (isLoading) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-8 w-64" />
        <div className="grid grid-cols-4 gap-4">
          {[1, 2, 3, 4].map((i) => <Skeleton key={i} className="h-24" />)}
        </div>
      </div>
    );
  }

  if (!data || data.totalFindings === 0) {
    return (
      <EmptyState
        icon={<Shield className="size-10" />}
        title="No CVE Findings"
        description="No known vulnerabilities detected in installed software. Run a rescan to check against the latest CVE database."
        action={
          <Button onClick={handleRescan} disabled={rescan.isPending}>
            {rescan.isPending && <Loader2 className="size-4 mr-1 animate-spin" />}
            Scan Now
          </Button>
        }
      />
    );
  }

  const criticals = data.summary.find((s) => s.severity === 'critical')?.count ?? 0;
  const highs = data.summary.find((s) => s.severity === 'high')?.count ?? 0;

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h2 className="text-lg font-semibold">CVE Findings</h2>
        <Button
          variant="outline"
          size="sm"
          onClick={handleRescan}
          disabled={rescan.isPending}
        >
          {rescan.isPending ? (
            <Loader2 className="size-4 mr-1 animate-spin" />
          ) : (
            <RefreshCw className="size-4 mr-1" />
          )}
          Rescan
        </Button>
      </div>

      <div className="grid grid-cols-2 lg:grid-cols-5 gap-4">
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Total Findings</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="flex items-center gap-2">
              <Bug className="size-5 text-primary" />
              <span className="text-2xl font-bold">{data.totalFindings}</span>
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Unique CVEs</CardTitle>
          </CardHeader>
          <CardContent>
            <span className="text-2xl font-bold">{data.uniqueCves}</span>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Affected Machines</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="flex items-center gap-2">
              <Monitor className="size-5 text-primary" />
              <span className="text-2xl font-bold">
                {data.affectedMachines} / {data.totalMachines}
              </span>
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Critical</CardTitle>
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
            <CardTitle className="text-sm font-medium text-muted-foreground">High</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="flex items-center gap-2">
              {highs > 0 ? <AlertTriangle className="size-5 text-orange-600" /> : <CheckCircle className="size-5 text-green-600" />}
              <span className={`text-2xl font-bold ${highs > 0 ? 'text-orange-600' : 'text-green-600'}`}>
                {highs}
              </span>
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Top vulnerable software */}
      {stats && stats.topSoftware.length > 0 && (
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium">Most Vulnerable Software</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
              {stats.topSoftware.slice(0, 8).map((sw) => (
                <div key={sw.softwareName} className="border rounded-lg p-3">
                  <p className="text-sm font-medium truncate">{sw.softwareName}</p>
                  <div className="flex items-center gap-2 mt-1">
                    <span className="text-xs text-muted-foreground">{sw.cveCount} CVEs</span>
                    <span className="text-xs text-muted-foreground">{sw.machineCount} machines</span>
                    {sw.maxCvss != null && (
                      <span className={`text-xs ${cvssColor(sw.maxCvss)}`}>
                        CVSS {sw.maxCvss.toFixed(1)}
                      </span>
                    )}
                  </div>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>
      )}

      {/* Findings table */}
      <Card>
        <CardHeader className="pb-2">
          <div className="flex items-center justify-between">
            <CardTitle className="text-sm font-medium">Findings</CardTitle>
            <Select
              value={severityFilter ?? 'all'}
              onValueChange={(v) => setSeverityFilter(v === 'all' ? undefined : v)}
            >
              <SelectTrigger className="w-32">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All</SelectItem>
                <SelectItem value="critical">Critical</SelectItem>
                <SelectItem value="high">High</SelectItem>
                <SelectItem value="medium">Medium</SelectItem>
                <SelectItem value="low">Low</SelectItem>
              </SelectContent>
            </Select>
          </div>
        </CardHeader>
        <CardContent>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Severity</TableHead>
                <TableHead>CVE</TableHead>
                <TableHead>CVSS</TableHead>
                <TableHead>Software</TableHead>
                <TableHead>Installed</TableHead>
                <TableHead>Fix</TableHead>
                <TableHead>Machine</TableHead>
                <TableHead>Found</TableHead>
                <TableHead></TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.findings.map((f) => (
                <TableRow key={f.id}>
                  <TableCell>{severityBadge(f.severity)}</TableCell>
                  <TableCell className="font-mono text-sm">{f.cveId}</TableCell>
                  <TableCell className={cvssColor(f.cvssScore)}>
                    {f.cvssScore?.toFixed(1) ?? '--'}
                  </TableCell>
                  <TableCell className="font-medium max-w-48 truncate">{f.softwareName}</TableCell>
                  <TableCell className="text-sm">{f.installedVersion ?? '--'}</TableCell>
                  <TableCell className="text-sm text-green-700">{f.fixedVersion ?? '--'}</TableCell>
                  <TableCell className="text-sm">{f.machineName}</TableCell>
                  <TableCell className="text-sm">
                    {new Date(f.foundAt).toLocaleDateString()}
                  </TableCell>
                  <TableCell>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => handleDismiss(f)}
                      disabled={dismiss.isPending}
                    >
                      Dismiss
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
    </div>
  );
}

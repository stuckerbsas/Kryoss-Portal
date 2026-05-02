import { useState, useMemo } from 'react';
import {
  AlertTriangle,
  Bug,
  CheckCircle,
  ChevronDown,
  ChevronRight,
  Loader2,
  Monitor,
  Package,
  RefreshCw,
  Shield,
  XCircle,
} from 'lucide-react';
import { toast } from 'sonner';
import { useCveFindings, useCveRescan, useDismissCve } from '@/api/cveFindings';
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

const SEV_ORDER: Record<string, number> = { critical: 4, high: 3, medium: 2, low: 1 };

interface SoftwareGroup {
  softwareName: string;
  version: string;
  maxCvss: number | null;
  maxSeverity: string;
  machines: { id: string; name: string; foundAt: string }[];
  cves: { cveId: string; severity: string; cvssScore: number | null; fixedVersion: string | null; description: string | null; findingIds: number[] }[];
}

function buildGroups(findings: CveFinding[]): SoftwareGroup[] {
  const map = new Map<string, SoftwareGroup>();

  for (const f of findings) {
    const key = `${f.softwareName}\0${f.installedVersion ?? ''}`;
    let g = map.get(key);
    if (!g) {
      g = { softwareName: f.softwareName, version: f.installedVersion ?? '--', maxCvss: null, maxSeverity: 'low', machines: [], cves: [] };
      map.set(key, g);
    }

    if (!g.machines.some((m) => m.id === f.machineId))
      g.machines.push({ id: f.machineId, name: f.machineName, foundAt: f.foundAt });

    const existing = g.cves.find((c) => c.cveId === f.cveId);
    if (existing) {
      existing.findingIds.push(f.id);
    } else {
      g.cves.push({ cveId: f.cveId, severity: f.severity, cvssScore: f.cvssScore, fixedVersion: f.fixedVersion, description: f.description, findingIds: [f.id] });
    }

    if (f.cvssScore != null && (g.maxCvss == null || f.cvssScore > g.maxCvss))
      g.maxCvss = f.cvssScore;
    if ((SEV_ORDER[f.severity] ?? 0) > (SEV_ORDER[g.maxSeverity] ?? 0))
      g.maxSeverity = f.severity;
  }

  for (const g of map.values())
    g.cves.sort((a, b) => (b.cvssScore ?? 0) - (a.cvssScore ?? 0));

  return [...map.values()].sort((a, b) => (b.maxCvss ?? 0) - (a.maxCvss ?? 0));
}

function severityBadge(severity: string) {
  const styles: Record<string, string> = {
    critical: 'bg-red-100 text-red-800',
    high: 'bg-orange-100 text-orange-800',
    medium: 'bg-amber-100 text-amber-800',
    low: 'bg-blue-100 text-blue-800',
  };
  return <Badge className={styles[severity] ?? 'bg-gray-100 text-gray-800'}>{severity}</Badge>;
}

function cvssColor(score: number | null) {
  if (score == null) return 'text-muted-foreground';
  if (score >= 9.0) return 'text-red-600 font-bold';
  if (score >= 7.0) return 'text-orange-600 font-semibold';
  if (score >= 4.0) return 'text-amber-600';
  return 'text-blue-600';
}

function sevBorderColor(severity: string) {
  const colors: Record<string, string> = {
    critical: 'border-l-red-500',
    high: 'border-l-orange-500',
    medium: 'border-l-amber-400',
    low: 'border-l-blue-400',
  };
  return colors[severity] ?? 'border-l-gray-300';
}

function SoftwareGroupCard({ group, onDismiss, dismissPending }: {
  group: SoftwareGroup;
  onDismiss: (findingId: number, cveId: string) => void;
  dismissPending: boolean;
}) {
  const [expanded, setExpanded] = useState(false);

  return (
    <Card className={`border-l-4 ${sevBorderColor(group.maxSeverity)}`}>
      <CardHeader className="pb-2">
        <div
          className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between cursor-pointer select-none"
          onClick={() => setExpanded(!expanded)}
        >
          <div className="flex items-center gap-3 min-w-0">
            <Package className="size-5 text-muted-foreground shrink-0" />
            <div className="min-w-0">
              <span className="font-semibold break-words">{group.softwareName}</span>
              <span className="text-sm text-muted-foreground block">v{group.version}</span>
            </div>
          </div>
          <div className="flex items-center gap-3 shrink-0">
            {severityBadge(group.maxSeverity)}
            <span className={`text-sm font-mono ${cvssColor(group.maxCvss)}`}>
              {group.maxCvss?.toFixed(1) ?? '--'}
            </span>
            <span className="text-xs text-muted-foreground">{group.cves.length} CVEs</span>
            <span className="hidden sm:inline text-xs text-muted-foreground">{group.machines.length} {group.machines.length === 1 ? 'machine' : 'machines'}</span>
            {expanded ? <ChevronDown className="size-4" /> : <ChevronRight className="size-4" />}
          </div>
        </div>
      </CardHeader>

      <CardContent className="pt-0">
        {/* Machines — hidden on mobile */}
        <div className="hidden sm:flex flex-wrap gap-1.5 mb-2">
          {group.machines.map((m) => (
            <Badge key={m.id} variant="outline" className="text-xs font-normal">
              {m.name}
            </Badge>
          ))}
        </div>

        {/* CVE list — collapsible */}
        {expanded && (
          <>
            {/* Mobile card view */}
            <div className="space-y-3 sm:hidden">
              {group.cves.map((c) => (
                <div key={c.cveId} className="rounded-lg border p-4">
                  <div className="flex items-center justify-between">
                    <span className="font-mono font-medium text-sm">{c.cveId}</span>
                    {severityBadge(c.severity)}
                  </div>
                  <div className="mt-1 flex items-center gap-3 text-xs text-muted-foreground">
                    <span className={cvssColor(c.cvssScore)}>CVSS {c.cvssScore?.toFixed(1) ?? '--'}</span>
                    {c.fixedVersion && <span className="text-green-700">Fix: {c.fixedVersion}</span>}
                  </div>
                </div>
              ))}
            </div>

            {/* Desktop table view */}
            <div className="hidden sm:block">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead className="w-20">Severity</TableHead>
                    <TableHead>CVE</TableHead>
                    <TableHead className="w-16">CVSS</TableHead>
                    <TableHead className="w-24">Fix</TableHead>
                    <TableHead className="hidden lg:table-cell">Description</TableHead>
                    <TableHead className="w-16"></TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {group.cves.map((c) => (
                    <TableRow key={c.cveId}>
                      <TableCell>{severityBadge(c.severity)}</TableCell>
                      <TableCell className="font-mono text-sm">{c.cveId}</TableCell>
                      <TableCell className={cvssColor(c.cvssScore)}>
                        {c.cvssScore?.toFixed(1) ?? '--'}
                      </TableCell>
                      <TableCell className="text-sm text-green-700">{c.fixedVersion ?? '--'}</TableCell>
                      <TableCell className="hidden lg:table-cell text-xs text-muted-foreground max-w-sm truncate">
                        {c.description ?? ''}
                      </TableCell>
                      <TableCell>
                        <Button
                          variant="ghost"
                          size="sm"
                          className="text-xs"
                          onClick={() => onDismiss(c.findingIds[0], c.cveId)}
                          disabled={dismissPending}
                        >
                          Dismiss
                        </Button>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          </>
        )}
      </CardContent>
    </Card>
  );
}

export function CveFindingsTab() {
  const { orgId } = useOrgParam();
  const [severityFilter, setSeverityFilter] = useState<string | undefined>(undefined);
  const { data, isLoading } = useCveFindings(orgId, severityFilter);
  const rescan = useCveRescan(orgId);
  const dismiss = useDismissCve(orgId);

  const groups = useMemo(() => (data ? buildGroups(data.findings) : []), [data]);

  const handleRescan = () => {
    rescan.mutate(undefined, {
      onSuccess: (r) => toast.success(`CVE scan complete: ${r.findingsCount} findings`),
      onError: (e: Error) => toast.error(e.message),
    });
  };

  const handleDismiss = (findingId: number, cveId: string) => {
    dismiss.mutate(findingId, {
      onSuccess: () => toast.success(`${cveId} dismissed`),
    });
  };

  if (isLoading) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-8 w-64" />
        <div className="grid grid-cols-1 sm:grid-cols-4 gap-4">
          {[1, 2, 3, 4].map((i) => <Skeleton key={i} className="h-24" />)}
        </div>
      </div>
    );
  }

  if (!data) {
    return (
      <EmptyState
        icon={<Shield className="size-10" />}
        title="Failed to load CVE data"
        description="Could not retrieve vulnerability findings. Try again later."
      />
    );
  }

  if (data.totalFindings === 0) {
    const noMachines = data.totalMachines === 0;
    return (
      <EmptyState
        icon={noMachines ? <Monitor className="size-10" /> : <CheckCircle className="size-10 text-green-600" />}
        title={noMachines ? 'No machines enrolled' : 'No known vulnerabilities'}
        description={
          noMachines
            ? 'Deploy agents to start CVE monitoring. Scans run automatically after each assessment.'
            : `${data.totalMachines} machines scanned — no known vulnerabilities detected in installed software.`
        }
        action={
          noMachines ? undefined : (
            <Button variant="outline" onClick={handleRescan} disabled={rescan.isPending}>
              {rescan.isPending && <Loader2 className="size-4 mr-1 animate-spin" />}
              Rescan
            </Button>
          )
        }
      />
    );
  }

  const criticals = data.summary.find((s) => s.severity === 'critical')?.count ?? 0;
  const highs = data.summary.find((s) => s.severity === 'high')?.count ?? 0;

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <h2 className="text-lg font-semibold">CVE Findings</h2>
        <div className="flex items-center gap-2">
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
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-5 gap-4">
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

      {/* Software groups */}
      <div className="space-y-3">
        <h3 className="text-sm font-medium text-muted-foreground">
          {groups.length} vulnerable {groups.length === 1 ? 'software' : 'software packages'}
        </h3>
        {groups.map((g) => (
          <SoftwareGroupCard
            key={`${g.softwareName}\0${g.version}`}
            group={g}
            onDismiss={handleDismiss}
            dismissPending={dismiss.isPending}
          />
        ))}
      </div>
    </div>
  );
}

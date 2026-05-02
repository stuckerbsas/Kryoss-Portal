import { useState } from 'react';
import {
  Globe,
  Search,
  ShieldAlert,
  AlertTriangle,
  Wifi,
  Loader2,
  Info,
  Lock,
  Mail,
  ShieldCheck,
} from 'lucide-react';
import { toast } from 'sonner';
import {
  useLatestExternalScan,
  useStartExternalScan,
} from '@/api/externalScan';
import type { ExternalScanResultItem, ExternalScanFindingItem } from '@/api/externalScan';
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

function riskBadge(risk: string | null) {
  if (!risk) return <Badge variant="secondary">--</Badge>;
  const config: Record<string, string> = {
    critical: 'bg-red-200 text-red-900',
    high: 'bg-red-100 text-red-800',
    medium: 'bg-amber-100 text-amber-800',
    info: 'bg-blue-100 text-blue-800',
  };
  return (
    <Badge
      variant="secondary"
      className={config[risk] ?? 'bg-gray-100 text-gray-500'}
    >
      {risk}
    </Badge>
  );
}

function formatDate(iso: string | null) {
  if (!iso) return '--';
  return new Date(iso).toLocaleString();
}

export function ExternalScanTab() {
  const { orgId } = useOrgParam();
  const { data: scan, isLoading } = useLatestExternalScan(orgId);
  const startScan = useStartExternalScan();
  const [target, setTarget] = useState('');

  const handleScan = async () => {
    if (!orgId || !target.trim()) return;
    try {
      const result = await startScan.mutateAsync({
        organizationId: orgId,
        target: target.trim(),
      });
      toast.success(
        `Scan complete: ${result.ipsFound} IPs, ${result.openPorts} open ports`,
      );
    } catch (err: any) {
      toast.error(`Scan failed: ${err.message}`);
    }
  };

  if (isLoading) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-10 w-full max-w-lg" />
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {Array.from({ length: 4 }).map((_, i) => (
            <Card key={i}>
              <CardHeader className="pb-2">
                <Skeleton className="h-4 w-24" />
              </CardHeader>
              <CardContent>
                <Skeleton className="h-8 w-16" />
              </CardContent>
            </Card>
          ))}
        </div>
        <Skeleton className="h-48" />
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div>
        <h3 className="text-lg font-semibold">External Port Scan</h3>
        <p className="text-sm text-muted-foreground">
          Scan public-facing IPs of a domain or IP address to detect exposed
          services from the cloud.
        </p>
      </div>

      {/* Scan input */}
      <div className="flex items-center gap-3 max-w-xl">
        <Input
          placeholder="Enter domain (e.g. example.com) or IP address"
          value={target}
          onChange={(e) => setTarget(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && handleScan()}
          disabled={startScan.isPending}
        />
        <Button
          onClick={handleScan}
          disabled={!target.trim() || startScan.isPending}
        >
          {startScan.isPending ? (
            <Loader2 className="mr-1.5 h-4 w-4 animate-spin" />
          ) : (
            <Search className="mr-1.5 h-4 w-4" />
          )}
          Scan
        </Button>
      </div>

      {/* No data yet */}
      {!scan && !startScan.isPending && (
        <EmptyState
          icon={<Globe className="size-10" />}
          title="No external scans yet"
          description="Enter a domain or IP address above to run your first external port scan."
        />
      )}

      {/* Results */}
      {scan && (
        <>
          {/* Scan metadata */}
          <div className="text-sm text-muted-foreground flex flex-wrap gap-4">
            <span>
              Target: <strong>{scan.target}</strong>
            </span>
            <span>Status: <strong>{scan.status}</strong></span>
            <span>Started: {formatDate(scan.startedAt)}</span>
            <span>Completed: {formatDate(scan.completedAt)}</span>
          </div>

          {/* KPI cards */}
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <Card>
              <CardHeader className="flex flex-row items-center justify-between pb-2">
                <CardTitle className="text-sm font-medium text-muted-foreground">
                  IPs Found
                </CardTitle>
                <Wifi className="h-4 w-4 text-muted-foreground" />
              </CardHeader>
              <CardContent>
                <p className="text-2xl font-bold">{scan.summary.totalIps}</p>
              </CardContent>
            </Card>
            <Card>
              <CardHeader className="flex flex-row items-center justify-between pb-2">
                <CardTitle className="text-sm font-medium text-muted-foreground">
                  Open Ports
                </CardTitle>
                <Globe className="h-4 w-4 text-muted-foreground" />
              </CardHeader>
              <CardContent>
                <p className="text-2xl font-bold">{scan.summary.totalOpen}</p>
              </CardContent>
            </Card>
            <Card>
              <CardHeader className="flex flex-row items-center justify-between pb-2">
                <CardTitle className="text-sm font-medium text-muted-foreground">
                  Critical
                </CardTitle>
                <ShieldAlert className="h-4 w-4 text-red-500" />
              </CardHeader>
              <CardContent>
                <p
                  className="text-2xl font-bold"
                  style={{
                    color:
                      scan.summary.criticalPorts > 0 ? '#C0392B' : '#006536',
                  }}
                >
                  {scan.summary.criticalPorts}
                </p>
              </CardContent>
            </Card>
            <Card>
              <CardHeader className="flex flex-row items-center justify-between pb-2">
                <CardTitle className="text-sm font-medium text-muted-foreground">
                  High Risk
                </CardTitle>
                <AlertTriangle className="h-4 w-4 text-red-500" />
              </CardHeader>
              <CardContent>
                <p
                  className="text-2xl font-bold"
                  style={{
                    color: scan.summary.highPorts > 0 ? '#D97706' : '#006536',
                  }}
                >
                  {scan.summary.highPorts}
                </p>
              </CardContent>
            </Card>
          </div>

          {/* Results table */}
          {scan.results.length > 0 ? (
            <Card>
              <CardHeader className="pb-3">
                <CardTitle className="flex items-center gap-2 text-base">
                  <Globe className="h-4 w-4 text-muted-foreground" />
                  Open Ports
                </CardTitle>
              </CardHeader>
              <CardContent>
                {/* Mobile cards */}
                <div className="space-y-3 sm:hidden">
                  {scan.results.map((r: ExternalScanResultItem, i: number) => (
                    <div key={i} className="rounded-lg border p-4 space-y-1">
                      <div className="flex items-center justify-between gap-2">
                        <span className="font-mono font-medium text-sm">{r.ipAddress}:{r.port}</span>
                        {riskBadge(r.risk)}
                      </div>
                      <div className="text-xs text-muted-foreground">
                        <span>Service: {r.service ?? '--'}</span>
                        {r.banner && <p className="truncate mt-0.5">{r.banner}</p>}
                      </div>
                    </div>
                  ))}
                </div>
                {/* Desktop table */}
                <div className="hidden sm:block overflow-x-auto">
                  <Table>
                    <TableHeader>
                      <TableRow>
                        <TableHead>IP Address</TableHead>
                        <TableHead>Port</TableHead>
                        <TableHead>Service</TableHead>
                        <TableHead>Risk</TableHead>
                        <TableHead className="hidden lg:table-cell">Banner</TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {scan.results.map(
                        (r: ExternalScanResultItem, i: number) => (
                          <TableRow key={i}>
                            <TableCell className="font-mono text-sm">
                              {r.ipAddress}
                            </TableCell>
                            <TableCell className="font-mono font-medium">
                              {r.port}
                            </TableCell>
                            <TableCell className="text-sm">
                              {r.service ?? '--'}
                            </TableCell>
                            <TableCell>{riskBadge(r.risk)}</TableCell>
                            <TableCell className="text-xs text-muted-foreground max-w-xs truncate hidden lg:table-cell">
                              {r.banner ?? '--'}
                            </TableCell>
                          </TableRow>
                        ),
                      )}
                    </TableBody>
                  </Table>
                </div>
              </CardContent>
            </Card>
          ) : (
            scan.status === 'completed' && (
              <Card>
                <CardContent className="py-8">
                  <div className="flex flex-col items-center text-center">
                    <Info className="h-8 w-8 text-muted-foreground mb-2" />
                    <p className="text-sm font-medium">No open ports found</p>
                    <p className="text-xs text-muted-foreground mt-1">
                      All 25 scanned ports are closed or filtered on the
                      resolved IPs.
                    </p>
                  </div>
                </CardContent>
              </Card>
            )
          )}

          {/* Domain findings (TLS, headers, mail) */}
          {scan.findings && scan.findings.length > 0 && (() => {
            const tlsFindings = scan.findings!.filter((f: ExternalScanFindingItem) => f.title.includes('TLS') || f.title.includes('Certificate'));
            const headerFindings = scan.findings!.filter((f: ExternalScanFindingItem) => f.title.includes('Missing') && (f.title.includes('HSTS') || f.title.includes('CSP') || f.title.includes('Frame') || f.title.includes('Content-Type') || f.title.includes('Referrer')));
            const mailFindings = scan.findings!.filter((f: ExternalScanFindingItem) => f.title.includes('SPF') || f.title.includes('DMARC'));
            const otherFindings = scan.findings!.filter((f: ExternalScanFindingItem) => !tlsFindings.includes(f) && !headerFindings.includes(f) && !mailFindings.includes(f));

            const renderFindingGroup = (title: string, icon: React.ReactNode, items: ExternalScanFindingItem[]) => {
              if (items.length === 0) return null;
              return (
                <Card key={title}>
                  <CardHeader className="pb-3">
                    <CardTitle className="flex items-center gap-2 text-base">
                      {icon}
                      {title}
                    </CardTitle>
                  </CardHeader>
                  <CardContent className="space-y-3">
                    {items.map((f: ExternalScanFindingItem, i: number) => (
                      <div key={i} className="border rounded-md p-3">
                        <div className="flex items-center gap-2 mb-1">
                          {riskBadge(f.severity)}
                          <span className="font-medium text-sm">{f.title}</span>
                        </div>
                        {f.description && <p className="text-xs text-muted-foreground">{f.description}</p>}
                        {f.remediation && <p className="text-xs text-blue-600 mt-1">{f.remediation}</p>}
                      </div>
                    ))}
                  </CardContent>
                </Card>
              );
            };

            return (
              <>
                {renderFindingGroup('TLS / Certificate', <Lock className="h-4 w-4 text-muted-foreground" />, tlsFindings)}
                {renderFindingGroup('HTTP Security Headers', <ShieldCheck className="h-4 w-4 text-muted-foreground" />, headerFindings)}
                {renderFindingGroup('Email Authentication', <Mail className="h-4 w-4 text-muted-foreground" />, mailFindings)}
                {renderFindingGroup('Port Findings', <AlertTriangle className="h-4 w-4 text-muted-foreground" />, otherFindings)}
              </>
            );
          })()}
        </>
      )}
    </div>
  );
}

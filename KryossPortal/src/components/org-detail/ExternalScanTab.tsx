import { useState, useMemo } from 'react';
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
  Radar,
  Network,
  Cookie,
} from 'lucide-react';
import { toast } from 'sonner';
import {
  useExternalScanHistory,
  useExternalScanDetail,
  useStartExternalScan,
  useExternalScanTargets,
  useAutoExternalScan,
} from '@/api/externalScan';
import type {
  ExternalScanDetail,
  ExternalScanResultItem,
  ExternalScanFindingItem,
  ScanHistoryItem,
} from '@/api/externalScan';
import { useOrgParam } from '@/hooks/useOrgParam';
import { EmptyState } from '@/components/shared/EmptyState';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';
import { Tabs, TabsList, TabsTrigger, TabsContent } from '@/components/ui/tabs';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';

function portState(status: string) {
  const isOpen = status === 'open';
  return (
    <span className={`inline-flex items-center gap-1 text-xs ${isOpen ? 'text-green-700' : 'text-red-700'}`}>
      <span className={`h-2 w-2 rounded-full ${isOpen ? 'bg-green-500' : 'bg-red-500'}`} />
      {status}
    </span>
  );
}

function riskBadge(risk: string | null) {
  if (!risk) return <Badge variant="secondary">--</Badge>;
  const config: Record<string, string> = {
    critical: 'bg-red-200 text-red-900',
    high: 'bg-red-100 text-red-800',
    medium: 'bg-amber-100 text-amber-800',
    low: 'bg-blue-100 text-blue-800',
    info: 'bg-blue-100 text-blue-800',
  };
  return (
    <Badge variant="secondary" className={config[risk] ?? 'bg-gray-100 text-gray-500'}>
      {risk}
    </Badge>
  );
}

function formatDate(iso: string | null) {
  if (!iso) return '--';
  return new Date(iso).toLocaleString();
}

const categoryConfig: Record<string, { title: string; icon: React.ReactNode }> = {
  port: { title: 'Port Exposure', icon: <AlertTriangle className="h-4 w-4 text-muted-foreground" /> },
  tls: { title: 'TLS / Certificate', icon: <Lock className="h-4 w-4 text-muted-foreground" /> },
  header: { title: 'HTTP Security Headers', icon: <ShieldCheck className="h-4 w-4 text-muted-foreground" /> },
  mail: { title: 'Email Authentication', icon: <Mail className="h-4 w-4 text-muted-foreground" /> },
  dns: { title: 'DNS Health', icon: <Network className="h-4 w-4 text-muted-foreground" /> },
  web: { title: 'Web Security', icon: <Cookie className="h-4 w-4 text-muted-foreground" /> },
};

function ScanDetail({ scan }: { scan: ExternalScanDetail }) {
  return (
    <div className="space-y-6">
      <div className="text-sm text-muted-foreground flex flex-wrap gap-4">
        <span>Target: <strong>{scan.target}</strong></span>
        <span>Status: <strong>{scan.status}</strong></span>
        <span>Started: {formatDate(scan.startedAt)}</span>
        <span>Completed: {formatDate(scan.completedAt)}</span>
      </div>

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">IPs Found</CardTitle>
            <Wifi className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <p className="text-2xl font-bold">{scan.summary?.totalIps ?? 0}</p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Open Ports</CardTitle>
            <Globe className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <p className="text-2xl font-bold">{scan.summary?.totalOpen ?? 0}</p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Critical</CardTitle>
            <ShieldAlert className="h-4 w-4 text-red-500" />
          </CardHeader>
          <CardContent>
            <p className="text-2xl font-bold" style={{ color: (scan.summary?.criticalPorts ?? 0) > 0 ? '#C0392B' : '#006536' }}>
              {scan.summary?.criticalPorts ?? 0}
            </p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Findings</CardTitle>
            <AlertTriangle className="h-4 w-4 text-amber-500" />
          </CardHeader>
          <CardContent>
            <p className="text-2xl font-bold">{scan.findings?.length ?? 0}</p>
          </CardContent>
        </Card>
      </div>

      {scan.results && scan.results.length > 0 ? (
        <Card>
          <CardHeader className="pb-3">
            <CardTitle className="flex items-center gap-2 text-base">
              <Globe className="h-4 w-4 text-muted-foreground" />
              Open Ports
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="space-y-3 sm:hidden">
              {scan.results.map((r: ExternalScanResultItem, i: number) => (
                <div key={i} className="rounded-lg border p-4 space-y-1">
                  <div className="flex items-center justify-between gap-2">
                    <span className="font-mono font-medium text-sm flex items-center gap-1.5">
                      {portState(r.status)}
                      {r.ipAddress}:{r.port}
                    </span>
                    {riskBadge(r.risk)}
                  </div>
                  <div className="text-xs text-muted-foreground">
                    <span>Service: {r.service ?? '--'}</span>
                    {r.banner && <p className="truncate mt-0.5">{r.banner}</p>}
                  </div>
                </div>
              ))}
            </div>
            <div className="hidden sm:block overflow-x-auto">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>State</TableHead>
                    <TableHead>IP Address</TableHead>
                    <TableHead>Port</TableHead>
                    <TableHead>Service</TableHead>
                    <TableHead>Risk</TableHead>
                    <TableHead className="hidden lg:table-cell">Banner</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {scan.results.map((r: ExternalScanResultItem, i: number) => (
                    <TableRow key={i}>
                      <TableCell>{portState(r.status)}</TableCell>
                      <TableCell className="font-mono text-sm">{r.ipAddress}</TableCell>
                      <TableCell className="font-mono font-medium">{r.port}</TableCell>
                      <TableCell className="text-sm">{r.service ?? '--'}</TableCell>
                      <TableCell>{riskBadge(r.risk)}</TableCell>
                      <TableCell className="text-xs text-muted-foreground max-w-xs truncate hidden lg:table-cell">
                        {r.banner ?? '--'}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          </CardContent>
        </Card>
      ) : (
        scan.status === 'completed' && scan.results && (
          <Card>
            <CardContent className="py-8">
              <div className="flex flex-col items-center text-center">
                <Info className="h-8 w-8 text-muted-foreground mb-2" />
                <p className="text-sm font-medium">No open ports found</p>
                <p className="text-xs text-muted-foreground mt-1">All scanned ports are closed or filtered.</p>
              </div>
            </CardContent>
          </Card>
        )
      )}

      {scan.findings && scan.findings.length > 0 && (() => {
        const grouped = new Map<string, ExternalScanFindingItem[]>();
        const order = ['port', 'tls', 'dns', 'header', 'web', 'mail'];
        for (const f of scan.findings!) {
          const cat = f.category ?? 'port';
          if (!grouped.has(cat)) grouped.set(cat, []);
          grouped.get(cat)!.push(f);
        }
        return (
          <>
            {order.map(cat => {
              const items = grouped.get(cat);
              if (!items || items.length === 0) return null;
              const cfg = categoryConfig[cat] ?? { title: cat, icon: <Info className="h-4 w-4" /> };
              return (
                <Card key={cat}>
                  <CardHeader className="pb-3">
                    <CardTitle className="flex items-center gap-2 text-base">
                      {cfg.icon}
                      {cfg.title}
                      <Badge variant="secondary" className="ml-auto text-xs">{items.length}</Badge>
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
            })}
          </>
        );
      })()}
    </div>
  );
}

export function ExternalScanTab() {
  const { orgId } = useOrgParam();
  const { data: historyData, isLoading } = useExternalScanHistory(orgId);
  const { data: targetsData } = useExternalScanTargets(orgId);
  const startScan = useStartExternalScan();
  const autoScan = useAutoExternalScan();
  const [target, setTarget] = useState('');
  const [selectedScanId, setSelectedScanId] = useState<string>();

  const targets = targetsData?.targets ?? [];
  const history = historyData ?? [];

  const latestByTarget = useMemo(() => {
    const map = new Map<string, ScanHistoryItem>();
    for (const item of history) {
      if (!map.has(item.target)) map.set(item.target, item);
    }
    return Array.from(map.values());
  }, [history]);

  const activeScanId = selectedScanId ?? latestByTarget[0]?.id;
  const { data: selectedScan, isLoading: detailLoading } = useExternalScanDetail(activeScanId);

  const handleScan = async () => {
    if (!orgId || !target.trim()) return;
    try {
      const result = await startScan.mutateAsync({
        organizationId: orgId,
        target: target.trim(),
      });
      setSelectedScanId(result.scanId);
      toast.success('Scan complete');
    } catch (err: any) {
      toast.error(`Scan failed: ${err.message}`);
    }
  };

  const handleAutoScan = async () => {
    if (!orgId) return;
    try {
      const result = await autoScan.mutateAsync({ organizationId: orgId });
      const ok = result.scanIds.filter(s => s.scanId).length;
      const fail = result.scanIds.filter(s => !s.scanId).length;
      const first = result.scanIds.find(s => s.scanId);
      if (first?.scanId) setSelectedScanId(first.scanId);
      toast.success(`Auto-scan complete: ${ok} targets scanned${fail > 0 ? `, ${fail} failed` : ''}`);
    } catch (err: any) {
      toast.error(`Auto-scan failed: ${err.message}`);
    }
  };

  if (isLoading) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-10 w-full max-w-lg" />
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {Array.from({ length: 4 }).map((_, i) => (
            <Card key={i}>
              <CardHeader className="pb-2"><Skeleton className="h-4 w-24" /></CardHeader>
              <CardContent><Skeleton className="h-8 w-16" /></CardContent>
            </Card>
          ))}
        </div>
        <Skeleton className="h-48" />
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div>
        <h3 className="text-lg font-semibold">External Exposure Scan</h3>
        <p className="text-sm text-muted-foreground">
          Scan public IPs and domains to detect exposed services, DNS issues, TLS misconfigurations, and email authentication gaps.
        </p>
      </div>

      <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
        <div className="flex items-center gap-3 flex-1 max-w-xl">
          <Input
            placeholder="Enter domain (e.g. example.com) or IP address"
            value={target}
            onChange={(e) => setTarget(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && handleScan()}
            disabled={startScan.isPending || autoScan.isPending}
          />
          <Button
            onClick={handleScan}
            disabled={!target.trim() || startScan.isPending || autoScan.isPending}
          >
            {startScan.isPending ? (
              <Loader2 className="mr-1.5 h-4 w-4 animate-spin" />
            ) : (
              <Search className="mr-1.5 h-4 w-4" />
            )}
            Scan
          </Button>
        </div>
        {targets.length > 0 && (
          <Button
            variant="outline"
            onClick={handleAutoScan}
            disabled={autoScan.isPending || startScan.isPending}
          >
            {autoScan.isPending ? (
              <Loader2 className="mr-1.5 h-4 w-4 animate-spin" />
            ) : (
              <Radar className="mr-1.5 h-4 w-4" />
            )}
            Auto-Scan ({targets.length} targets)
          </Button>
        )}
      </div>

      {targets.length > 0 && (
        <div className="flex flex-wrap gap-2">
          {targets.map((t, i) => (
            <Badge key={i} variant="outline" className="text-xs font-mono">
              {t.source === 'network_site' ? '🌐' : '📧'} {t.value}
              {t.label ? ` (${t.label})` : ''}
            </Badge>
          ))}
        </div>
      )}

      {latestByTarget.length === 0 && !startScan.isPending && !autoScan.isPending && (
        <EmptyState
          icon={<Globe className="size-10" />}
          title="No external scans yet"
          description={targets.length > 0
            ? `${targets.length} targets discovered. Click "Auto-Scan" to scan all known IPs and domains, or enter a target manually.`
            : 'Enter a domain or IP address above to run your first external scan.'}
        />
      )}

      {latestByTarget.length === 1 && selectedScan && (
        <ScanDetail scan={selectedScan} />
      )}

      {latestByTarget.length > 1 && (
        <Tabs
          value={activeScanId}
          onValueChange={setSelectedScanId}
        >
          <TabsList className="w-full overflow-x-auto flex-wrap h-auto">
            {latestByTarget.map((s) => (
              <TabsTrigger key={s.id} value={s.id} className="text-xs font-mono gap-1.5">
                {s.target}
                {s.criticalPorts > 0 ? (
                  <Badge variant="secondary" className="bg-red-100 text-red-800 text-[10px] px-1 py-0">{s.criticalPorts} crit</Badge>
                ) : (
                  <Badge variant="secondary" className="text-[10px] px-1 py-0">{s.openPorts} open</Badge>
                )}
              </TabsTrigger>
            ))}
          </TabsList>
          {latestByTarget.map((s) => (
            <TabsContent key={s.id} value={s.id}>
              {activeScanId === s.id && detailLoading && (
                <div className="space-y-4 py-4">
                  <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
                    {Array.from({ length: 4 }).map((_, i) => (
                      <Card key={i}>
                        <CardHeader className="pb-2"><Skeleton className="h-4 w-24" /></CardHeader>
                        <CardContent><Skeleton className="h-8 w-16" /></CardContent>
                      </Card>
                    ))}
                  </div>
                  <Skeleton className="h-48" />
                </div>
              )}
              {activeScanId === s.id && selectedScan && <ScanDetail scan={selectedScan} />}
            </TabsContent>
          ))}
        </Tabs>
      )}
    </div>
  );
}

import { useState } from 'react';
import {
  Wifi,
  ArrowDownToLine,
  ArrowUpFromLine,
  Globe,
  Monitor,
  Route,
  Shield,
  Network,
  ChevronDown,
  ChevronRight,
} from 'lucide-react';
import { useNetworkDiagnostics, type NetworkDiag } from '@/api/networkDiagnostics';
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

function speedColor(mbps: number): string {
  if (mbps >= 100) return '#008852';
  if (mbps >= 25) return '#D97706';
  return '#C0392B';
}

function latencyColor(ms: number): string {
  if (ms <= 20) return '#008852';
  if (ms <= 80) return '#D97706';
  return '#C0392B';
}

function lossColor(pct: number): string {
  if (pct === 0) return '#008852';
  if (pct <= 2) return '#D97706';
  return '#C0392B';
}

function fmt(n: number | null | undefined, decimals = 1): string {
  if (n == null) return '--';
  return n.toFixed(decimals);
}

function MachineRow({ diag }: { diag: NetworkDiag }) {
  const [expanded, setExpanded] = useState(false);
  const hasLatency = diag.latencyPeers.length > 0;
  const hasRoutes = diag.routes.length > 0;

  return (
    <>
      <TableRow
        className="cursor-pointer hover:bg-muted/50"
        onClick={() => setExpanded(!expanded)}
      >
        <TableCell className="font-medium">
          <div className="flex items-center gap-2">
            {expanded ? (
              <ChevronDown className="h-4 w-4 text-muted-foreground" />
            ) : (
              <ChevronRight className="h-4 w-4 text-muted-foreground" />
            )}
            {diag.machineName}
          </div>
        </TableCell>
        <TableCell>
          <span className="font-mono tabular-nums" style={{ color: speedColor(diag.downloadMbps) }}>
            {fmt(diag.downloadMbps)}
          </span>
        </TableCell>
        <TableCell>
          <span className="font-mono tabular-nums" style={{ color: speedColor(diag.uploadMbps) }}>
            {fmt(diag.uploadMbps)}
          </span>
        </TableCell>
        <TableCell>
          <span className="font-mono tabular-nums" style={{ color: latencyColor(diag.internetLatencyMs) }}>
            {fmt(diag.internetLatencyMs, 0)}
          </span>
          <span className="text-muted-foreground text-xs"> ms</span>
        </TableCell>
        <TableCell className="text-center">
          {diag.vpnDetected ? (
            <Badge variant="secondary" className="bg-blue-100 text-blue-800">VPN</Badge>
          ) : (
            <span className="text-muted-foreground text-xs">—</span>
          )}
        </TableCell>
        <TableCell>
          <span className="font-mono tabular-nums" style={{ color: diag.dnsResolutionMs != null ? latencyColor(diag.dnsResolutionMs) : undefined }}>
            {diag.dnsResolutionMs != null ? fmt(diag.dnsResolutionMs, 0) : '--'}
          </span>
        </TableCell>
        <TableCell>
          <span className="font-mono tabular-nums" style={{ color: diag.cloudEndpointAvgMs != null ? latencyColor(diag.cloudEndpointAvgMs) : undefined }}>
            {diag.cloudEndpointAvgMs != null ? fmt(diag.cloudEndpointAvgMs, 0) : '--'}
          </span>
          {diag.cloudEndpointCount != null && (
            <span className="text-muted-foreground text-xs"> ({diag.cloudEndpointCount})</span>
          )}
        </TableCell>
        <TableCell className="text-center font-mono tabular-nums">{diag.adapterCount}</TableCell>
        <TableCell className="text-center font-mono tabular-nums">{diag.routeCount}</TableCell>
        <TableCell className="text-muted-foreground text-xs">
          {new Date(diag.scannedAt).toLocaleString()}
        </TableCell>
      </TableRow>

      {expanded && hasLatency && (
        <TableRow>
          <TableCell colSpan={10} className="bg-muted/30 p-4">
            <div className="space-y-4">
              <h4 className="text-sm font-semibold flex items-center gap-2">
                <Network className="h-4 w-4" /> Internal Latency ({diag.latencyPeers.length} peers)
              </h4>
              <div className="overflow-x-auto">
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Host</TableHead>
                      <TableHead>Subnet</TableHead>
                      <TableHead>Reachable</TableHead>
                      <TableHead>Avg (ms)</TableHead>
                      <TableHead>Min (ms)</TableHead>
                      <TableHead>Max (ms)</TableHead>
                      <TableHead>Jitter (ms)</TableHead>
                      <TableHead>Packet Loss</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {diag.latencyPeers.map((p, i) => (
                      <TableRow key={i}>
                        <TableCell className="font-mono text-xs">{p.host}</TableCell>
                        <TableCell className="text-xs">{p.subnet}</TableCell>
                        <TableCell>
                          {p.reachable ? (
                            <Badge variant="secondary" className="bg-green-100 text-green-800">✓</Badge>
                          ) : (
                            <Badge variant="secondary" className="bg-red-100 text-red-800">✗</Badge>
                          )}
                        </TableCell>
                        <TableCell className="font-mono tabular-nums" style={{ color: latencyColor(p.avgMs) }}>
                          {fmt(p.avgMs)}
                        </TableCell>
                        <TableCell className="font-mono tabular-nums">{fmt(p.minMs)}</TableCell>
                        <TableCell className="font-mono tabular-nums">{fmt(p.maxMs)}</TableCell>
                        <TableCell className="font-mono tabular-nums">{fmt(p.jitterMs)}</TableCell>
                        <TableCell>
                          <span className="font-mono tabular-nums" style={{ color: lossColor(p.packetLoss) }}>
                            {fmt(p.packetLoss)}%
                          </span>
                          <span className="text-muted-foreground text-xs"> ({p.totalSent} sent)</span>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </div>

              {hasRoutes && (
                <>
                  <h4 className="text-sm font-semibold flex items-center gap-2 mt-4">
                    <Route className="h-4 w-4" /> Route Table ({diag.routes.length} entries)
                  </h4>
                  <div className="overflow-x-auto max-h-64 overflow-y-auto">
                    <Table>
                      <TableHeader>
                        <TableRow>
                          <TableHead>Destination</TableHead>
                          <TableHead>Mask</TableHead>
                          <TableHead>Next Hop</TableHead>
                          <TableHead>Interface</TableHead>
                          <TableHead>Metric</TableHead>
                          <TableHead>Type</TableHead>
                          <TableHead>Protocol</TableHead>
                        </TableRow>
                      </TableHeader>
                      <TableBody>
                        {diag.routes.map((r, i) => (
                          <TableRow key={i}>
                            <TableCell className="font-mono text-xs">{r.destination}</TableCell>
                            <TableCell className="font-mono text-xs">{r.mask}</TableCell>
                            <TableCell className="font-mono text-xs">{r.nextHop}</TableCell>
                            <TableCell className="font-mono text-xs">{r.interfaceIndex}</TableCell>
                            <TableCell className="font-mono tabular-nums">{r.metric}</TableCell>
                            <TableCell className="text-xs">{r.routeType}</TableCell>
                            <TableCell className="text-xs">{r.protocol}</TableCell>
                          </TableRow>
                        ))}
                      </TableBody>
                    </Table>
                  </div>
                </>
              )}
            </div>
          </TableCell>
        </TableRow>
      )}
    </>
  );
}

export function NetworkDiagnosticsTab() {
  const { orgId } = useOrgParam();
  const { data, isLoading } = useNetworkDiagnostics(orgId);

  if (isLoading) {
    return (
      <div className="space-y-4">
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

  if (!data || data.length === 0) {
    return (
      <EmptyState
        icon={<Wifi className="size-10" />}
        title="No network diagnostics data"
        description="Run the agent to collect speed test, latency, and routing data across your fleet."
      />
    );
  }

  const avgDown = data.reduce((s, d) => s + d.downloadMbps, 0) / data.length;
  const avgUp = data.reduce((s, d) => s + d.uploadMbps, 0) / data.length;
  const avgLatency = data.reduce((s, d) => s + d.internetLatencyMs, 0) / data.length;
  const vpnCount = data.filter((d) => d.vpnDetected).length;
  const dnsValues = data.filter((d) => d.dnsResolutionMs != null).map((d) => d.dnsResolutionMs!);
  const avgDns = dnsValues.length > 0 ? dnsValues.reduce((s, v) => s + v, 0) / dnsValues.length : null;
  const cloudValues = data.filter((d) => d.cloudEndpointAvgMs != null).map((d) => d.cloudEndpointAvgMs!);
  const avgCloud = cloudValues.length > 0 ? cloudValues.reduce((s, v) => s + v, 0) / cloudValues.length : null;

  return (
    <div className="space-y-6">
      <div>
        <h3 className="text-lg font-semibold">Network Diagnostics</h3>
        <p className="text-sm text-muted-foreground">
          Speed test, internal latency, routing, and VPN detection across {data.length} machines.
        </p>
      </div>

      {/* KPI cards */}
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-6">
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Avg Download</CardTitle>
            <ArrowDownToLine className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <p className="text-2xl font-bold" style={{ color: speedColor(avgDown) }}>
              {fmt(avgDown)} <span className="text-sm font-normal text-muted-foreground">Mbps</span>
            </p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Avg Upload</CardTitle>
            <ArrowUpFromLine className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <p className="text-2xl font-bold" style={{ color: speedColor(avgUp) }}>
              {fmt(avgUp)} <span className="text-sm font-normal text-muted-foreground">Mbps</span>
            </p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Avg Internet Latency</CardTitle>
            <Globe className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <p className="text-2xl font-bold" style={{ color: latencyColor(avgLatency) }}>
              {fmt(avgLatency, 0)} <span className="text-sm font-normal text-muted-foreground">ms</span>
            </p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">VPN Detected</CardTitle>
            <Shield className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <p className="text-2xl font-bold">
              {vpnCount} <span className="text-sm font-normal text-muted-foreground">/ {data.length}</span>
            </p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Avg DNS</CardTitle>
            <Globe className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <p className="text-2xl font-bold" style={{ color: avgDns != null ? latencyColor(avgDns) : undefined }}>
              {avgDns != null ? fmt(avgDns, 0) : '--'} <span className="text-sm font-normal text-muted-foreground">ms</span>
            </p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Avg Cloud Latency</CardTitle>
            <Wifi className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <p className="text-2xl font-bold" style={{ color: avgCloud != null ? latencyColor(avgCloud) : undefined }}>
              {avgCloud != null ? fmt(avgCloud, 0) : '--'} <span className="text-sm font-normal text-muted-foreground">ms</span>
            </p>
          </CardContent>
        </Card>
      </div>

      {/* Per-machine table */}
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="flex items-center gap-2 text-base">
            <Monitor className="h-4 w-4 text-muted-foreground" />
            Per-Machine Results
          </CardTitle>
        </CardHeader>
        <CardContent>
          <div className="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Machine</TableHead>
                  <TableHead>Down (Mbps)</TableHead>
                  <TableHead>Up (Mbps)</TableHead>
                  <TableHead>Latency</TableHead>
                  <TableHead>DNS</TableHead>
                  <TableHead>Cloud</TableHead>
                  <TableHead className="text-center">VPN</TableHead>
                  <TableHead className="text-center">Adapters</TableHead>
                  <TableHead className="text-center">Routes</TableHead>
                  <TableHead>Scanned</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data.map((d) => (
                  <MachineRow key={d.id} diag={d} />
                ))}
              </TableBody>
            </Table>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}

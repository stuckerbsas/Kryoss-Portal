import { lazy, Suspense, useState } from 'react';
import {
  Globe,
  MapPin,
  Wifi,
  ArrowDown,
  Clock,
  RefreshCw,
  Loader2,
  AlertTriangle,
  CheckCircle,
  XCircle,
  Star,
} from 'lucide-react';
import { toast } from 'sonner';
import {
  useNetworkSites,
  useIpHistory,
  useRebuildSites,
} from '@/api/networkSites';
import type { NetworkSite, IpHistoryEntry } from '@/api/networkSites';
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
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from '@/components/ui/tabs';
import { SiteDetailDrawer } from '@/components/network/SiteDetailDrawer';

const SiteMap = lazy(() =>
  import('@/components/network/SiteMap').then((m) => ({ default: m.SiteMap })),
);

function speedColor(mbps: number | null) {
  if (mbps == null) return 'text-muted-foreground';
  if (mbps >= 100) return 'text-green-600';
  if (mbps >= 25) return 'text-amber-600';
  return 'text-red-600';
}

function latencyColor(ms: number | null) {
  if (ms == null) return 'text-muted-foreground';
  if (ms <= 20) return 'text-green-600';
  if (ms <= 50) return 'text-amber-600';
  return 'text-red-600';
}

function connBadge(type: string | null) {
  const colors: Record<string, string> = {
    business: 'bg-blue-100 text-blue-800',
    residential: 'bg-gray-100 text-gray-800',
    cellular: 'bg-amber-100 text-amber-800',
    satellite: 'bg-red-100 text-red-800',
    hosting: 'bg-purple-100 text-purple-800',
  };
  if (!type) return null;
  return (
    <Badge className={colors[type] ?? 'bg-gray-100 text-gray-800'}>
      {type}
    </Badge>
  );
}

function slaBadge(site: NetworkSite) {
  if (site.contractedDownMbps == null || site.avgDownMbps == null) return null;
  const ratio = site.avgDownMbps / site.contractedDownMbps;
  if (ratio >= 0.8)
    return (
      <span className="text-green-600 flex items-center gap-1 text-xs">
        <CheckCircle className="size-3" />
        {Math.round(ratio * 100)}%
      </span>
    );
  return (
    <span className="text-red-600 flex items-center gap-1 text-xs font-medium">
      <XCircle className="size-3" />
      {Math.round(ratio * 100)}%
    </span>
  );
}

function KpiCard({
  icon: Icon,
  label,
  value,
  sub,
}: {
  icon: React.ElementType;
  label: string;
  value: string | number;
  sub?: string;
}) {
  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-sm font-medium text-muted-foreground">
          {label}
        </CardTitle>
      </CardHeader>
      <CardContent>
        <div className="flex items-center gap-2">
          <Icon className="size-5 text-primary" />
          <span className="text-2xl font-bold">{value}</span>
        </div>
        {sub && <p className="text-xs text-muted-foreground mt-1">{sub}</p>}
      </CardContent>
    </Card>
  );
}

function SitesTable({
  sites,
  onSiteClick,
}: {
  sites: NetworkSite[];
  onSiteClick: (site: NetworkSite) => void;
}) {
  return (
    <>
      {/* Mobile cards */}
      <div className="space-y-3 sm:hidden">
        {sites.map((s) => (
          <div
            key={s.id}
            className="rounded-lg border p-4 cursor-pointer active:bg-muted/50"
            onClick={() => onSiteClick(s)}
          >
            <div className="flex items-center justify-between">
              <span className="font-medium text-sm truncate flex items-center gap-1">
                {s.isPrimary && <Star className="size-3 text-amber-500 fill-amber-500" />}
                {s.siteName}
              </span>
              <div className="flex items-center gap-1.5">
                {connBadge(s.connType)}
                {slaBadge(s)}
              </div>
            </div>
            <div className="mt-2 grid grid-cols-2 gap-x-4 gap-y-1 text-xs text-muted-foreground">
              <span>IP: <span className="font-mono">{s.publicIp ?? '—'}</span></span>
              <span>Agents: {s.agentCount}</span>
              <span>Down: <span className={speedColor(s.avgDownMbps)}>{s.avgDownMbps != null ? `${s.avgDownMbps.toFixed(1)} Mbps` : '—'}</span></span>
              <span>Latency: <span className={latencyColor(s.avgLatencyMs)}>{s.avgLatencyMs != null ? `${s.avgLatencyMs.toFixed(1)} ms` : '—'}</span></span>
            </div>
          </div>
        ))}
      </div>

      {/* Desktop table */}
      <div className="hidden sm:block overflow-x-auto">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Site</TableHead>
              <TableHead>Public IP</TableHead>
              <TableHead className="hidden lg:table-cell">Location</TableHead>
              <TableHead className="hidden lg:table-cell">ISP</TableHead>
              <TableHead>Type</TableHead>
              <TableHead className="text-right">Agents</TableHead>
              <TableHead className="text-right">Down</TableHead>
              <TableHead className="text-right hidden lg:table-cell">Up</TableHead>
              <TableHead className="text-right">Latency</TableHead>
              <TableHead className="text-right hidden lg:table-cell">SLA</TableHead>
              <TableHead className="text-right hidden lg:table-cell">IP Changes (90d)</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {sites.map((s) => (
              <TableRow
                key={s.id}
                className="cursor-pointer hover:bg-muted/50"
                onClick={() => onSiteClick(s)}
              >
                <TableCell className="font-medium">
                  <span className="flex items-center gap-1.5">
                    {s.isPrimary && <Star className="size-3.5 text-amber-500 fill-amber-500" />}
                    {s.siteName}
                  </span>
                </TableCell>
                <TableCell className="font-mono text-sm">{s.publicIp ?? '—'}</TableCell>
                <TableCell className="hidden lg:table-cell">
                  {s.geoCity && s.geoCountry
                    ? `${s.geoCity}, ${s.geoCountry}`
                    : s.geoCountry ?? '—'}
                </TableCell>
                <TableCell className="hidden lg:table-cell">{s.isp ?? '—'}</TableCell>
                <TableCell>{connBadge(s.connType)}</TableCell>
                <TableCell className="text-right">{s.agentCount}</TableCell>
                <TableCell className={`text-right ${speedColor(s.avgDownMbps)}`}>
                  {s.avgDownMbps != null ? `${s.avgDownMbps.toFixed(1)} Mbps` : '—'}
                </TableCell>
                <TableCell className={`text-right hidden lg:table-cell ${speedColor(s.avgUpMbps)}`}>
                  {s.avgUpMbps != null ? `${s.avgUpMbps.toFixed(1)} Mbps` : '—'}
                </TableCell>
                <TableCell className={`text-right ${latencyColor(s.avgLatencyMs)}`}>
                  {s.avgLatencyMs != null ? `${s.avgLatencyMs.toFixed(1)} ms` : '—'}
                </TableCell>
                <TableCell className="text-right hidden lg:table-cell">{slaBadge(s) ?? '—'}</TableCell>
                <TableCell className="text-right hidden lg:table-cell">
                  {s.ipChanges90d > 3 ? (
                    <span className="text-amber-600 font-medium flex items-center justify-end gap-1">
                      <AlertTriangle className="size-3" />
                      {s.ipChanges90d}
                    </span>
                  ) : (
                    s.ipChanges90d
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
    </>
  );
}

function IpHistoryTable({ history }: { history: IpHistoryEntry[] }) {
  return (
    <>
      {/* Mobile cards */}
      <div className="space-y-3 sm:hidden">
        {history.map((h) => (
          <div key={h.id} className="rounded-lg border p-4">
            <div className="flex items-center justify-between">
              <span className="font-medium text-sm truncate">{h.machineName}</span>
              {connBadge(h.connType)}
            </div>
            <div className="mt-2 grid grid-cols-2 gap-x-4 gap-y-1 text-xs text-muted-foreground">
              <span>IP: <span className="font-mono">{h.publicIp}</span></span>
              <span>ISP: {h.isp ?? '—'}</span>
              <span>First: {new Date(h.firstSeen).toLocaleDateString()}</span>
              <span>Last: {new Date(h.lastSeen).toLocaleDateString()}</span>
            </div>
          </div>
        ))}
      </div>

      {/* Desktop table */}
      <div className="hidden sm:block overflow-x-auto">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Machine</TableHead>
              <TableHead>Public IP</TableHead>
              <TableHead className="hidden lg:table-cell">ISP</TableHead>
              <TableHead className="hidden lg:table-cell">Location</TableHead>
              <TableHead>Type</TableHead>
              <TableHead>First Seen</TableHead>
              <TableHead>Last Seen</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {history.map((h) => (
              <TableRow key={h.id}>
                <TableCell className="font-medium">{h.machineName}</TableCell>
                <TableCell className="font-mono text-sm">{h.publicIp}</TableCell>
                <TableCell className="hidden lg:table-cell">{h.isp ?? '—'}</TableCell>
                <TableCell className="hidden lg:table-cell">
                  {h.geoCity && h.geoCountry
                    ? `${h.geoCity}, ${h.geoCountry}`
                    : h.geoCountry ?? '—'}
                </TableCell>
                <TableCell>{connBadge(h.connType)}</TableCell>
                <TableCell className="text-sm">{new Date(h.firstSeen).toLocaleDateString()}</TableCell>
                <TableCell className="text-sm">{new Date(h.lastSeen).toLocaleDateString()}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
    </>
  );
}

export function NetworkSitesTab() {
  const { orgId } = useOrgParam();
  const { data: sites, isLoading } = useNetworkSites(orgId);
  const { data: history, isLoading: historyLoading } = useIpHistory(orgId);
  const rebuild = useRebuildSites(orgId);
  const [selectedSite, setSelectedSite] = useState<NetworkSite | null>(null);

  const handleRebuild = () => {
    rebuild.mutate(undefined, {
      onSuccess: () => toast.success('Sites rebuilt from current data'),
      onError: (err: Error) => toast.error(err.message),
    });
  };

  const handleSiteClick = (siteOrId: NetworkSite | string) => {
    if (typeof siteOrId === 'string') {
      const found = sites?.find((s) => s.id === siteOrId);
      if (found) setSelectedSite(found);
    } else {
      setSelectedSite(siteOrId);
    }
  };

  if (isLoading) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-8 w-64" />
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
          {[1, 2, 3, 4].map((i) => (
            <Skeleton key={i} className="h-24" />
          ))}
        </div>
      </div>
    );
  }

  const activeSites = sites?.filter((s) => s.agentCount > 0 || s.deviceCount > 0) ?? [];

  if (!sites || activeSites.length === 0) {
    const hasIpData = history && history.length > 0;
    return (
      <EmptyState
        icon={<Globe className="size-10" />}
        title="No Network Sites Detected"
        description={
          hasIpData
            ? `${history.length} IP records collected from agents. Click Rebuild to create sites.`
            : 'Sites are auto-derived from agent public IPs. Agents report IPs on each heartbeat — data will appear within minutes of enrollment.'
        }
        action={
          <Button onClick={handleRebuild} disabled={rebuild.isPending || !hasIpData}>
            {rebuild.isPending && <Loader2 className="size-4 mr-1 animate-spin" />}
            Rebuild Sites
          </Button>
        }
      />
    );
  }

  const totalAgents = activeSites.reduce((a, s) => a + s.agentCount, 0);
  const avgDown =
    activeSites.filter((s) => s.avgDownMbps != null).length > 0
      ? activeSites.reduce((a, s) => a + (s.avgDownMbps ?? 0), 0) /
        activeSites.filter((s) => s.avgDownMbps != null).length
      : null;
  const unstableSites = activeSites.filter((s) => s.ipChanges90d > 3).length;
  const satelliteSites = activeSites.filter(
    (s) => s.connType === 'satellite' || s.connType === 'cellular',
  ).length;
  const slaBreaches = activeSites.filter(
    (s) => s.contractedDownMbps != null && s.avgDownMbps != null && s.avgDownMbps / s.contractedDownMbps < 0.8,
  ).length;

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h2 className="text-lg font-semibold">Network Sites</h2>
        <Button
          variant="outline"
          size="sm"
          onClick={handleRebuild}
          disabled={rebuild.isPending}
        >
          {rebuild.isPending ? (
            <Loader2 className="size-4 mr-1 animate-spin" />
          ) : (
            <RefreshCw className="size-4 mr-1" />
          )}
          Rebuild Sites
        </Button>
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-5 gap-4">
        <KpiCard icon={MapPin} label="Sites" value={activeSites.length} />
        <KpiCard icon={Wifi} label="Agents" value={totalAgents} />
        <KpiCard
          icon={ArrowDown}
          label="Avg Download"
          value={avgDown != null ? `${avgDown.toFixed(1)} Mbps` : '—'}
        />
        {slaBreaches > 0 ? (
          <KpiCard icon={XCircle} label="SLA Breaches" value={slaBreaches} sub="Below 80% contracted" />
        ) : unstableSites > 0 ? (
          <KpiCard icon={AlertTriangle} label="Unstable Sites" value={unstableSites} sub=">3 IP changes in 90d" />
        ) : (
          <KpiCard icon={Clock} label="Sites Stable" value="All" />
        )}
        {satelliteSites > 0 ? (
          <KpiCard icon={Globe} label="Cellular/Satellite" value={satelliteSites} />
        ) : (
          <KpiCard icon={CheckCircle} label="All Wired" value="Yes" />
        )}
      </div>

      <Card>
        <CardHeader className="pb-2">
          <CardTitle className="text-sm font-medium">Site Map</CardTitle>
        </CardHeader>
        <CardContent>
          <Suspense fallback={<Skeleton className="h-[400px]" />}>
            <SiteMap sites={activeSites} onSiteClick={handleSiteClick} />
          </Suspense>
        </CardContent>
      </Card>

      <Tabs defaultValue="sites">
        <TabsList>
          <TabsTrigger value="sites">Sites</TabsTrigger>
          <TabsTrigger value="ip-history">IP History</TabsTrigger>
        </TabsList>
        <TabsContent value="sites">
          <Card>
            <CardContent className="pt-4">
              <SitesTable sites={activeSites} onSiteClick={handleSiteClick} />
            </CardContent>
          </Card>
        </TabsContent>
        <TabsContent value="ip-history">
          <Card>
            <CardContent className="pt-4">
              {historyLoading ? (
                <Skeleton className="h-40" />
              ) : history && history.length > 0 ? (
                <IpHistoryTable history={history} />
              ) : (
                <p className="text-sm text-muted-foreground text-center py-8">
                  No IP history recorded yet
                </p>
              )}
            </CardContent>
          </Card>
        </TabsContent>
      </Tabs>

      {selectedSite && (
        <SiteDetailDrawer
          site={selectedSite}
          onClose={() => setSelectedSite(null)}
        />
      )}
    </div>
  );
}

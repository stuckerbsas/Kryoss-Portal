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
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Site</TableHead>
          <TableHead>Public IP</TableHead>
          <TableHead>Location</TableHead>
          <TableHead>ISP</TableHead>
          <TableHead>Type</TableHead>
          <TableHead className="text-right">Agents</TableHead>
          <TableHead className="text-right">Down</TableHead>
          <TableHead className="text-right">Up</TableHead>
          <TableHead className="text-right">Latency</TableHead>
          <TableHead className="text-right">SLA</TableHead>
          <TableHead className="text-right">IP Changes (90d)</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {sites.map((s) => (
          <TableRow
            key={s.id}
            className="cursor-pointer hover:bg-muted/50"
            onClick={() => onSiteClick(s)}
          >
            <TableCell className="font-medium">{s.siteName}</TableCell>
            <TableCell className="font-mono text-sm">{s.publicIp ?? '—'}</TableCell>
            <TableCell>
              {s.geoCity && s.geoCountry
                ? `${s.geoCity}, ${s.geoCountry}`
                : s.geoCountry ?? '—'}
            </TableCell>
            <TableCell>{s.isp ?? '—'}</TableCell>
            <TableCell>{connBadge(s.connType)}</TableCell>
            <TableCell className="text-right">{s.agentCount}</TableCell>
            <TableCell className={`text-right ${speedColor(s.avgDownMbps)}`}>
              {s.avgDownMbps != null ? `${s.avgDownMbps.toFixed(1)} Mbps` : '—'}
            </TableCell>
            <TableCell className={`text-right ${speedColor(s.avgUpMbps)}`}>
              {s.avgUpMbps != null ? `${s.avgUpMbps.toFixed(1)} Mbps` : '—'}
            </TableCell>
            <TableCell className={`text-right ${latencyColor(s.avgLatencyMs)}`}>
              {s.avgLatencyMs != null ? `${s.avgLatencyMs.toFixed(1)} ms` : '—'}
            </TableCell>
            <TableCell className="text-right">{slaBadge(s) ?? '—'}</TableCell>
            <TableCell className="text-right">
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
  );
}

function IpHistoryTable({ history }: { history: IpHistoryEntry[] }) {
  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Machine</TableHead>
          <TableHead>Public IP</TableHead>
          <TableHead>ISP</TableHead>
          <TableHead>Location</TableHead>
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
            <TableCell>{h.isp ?? '—'}</TableCell>
            <TableCell>
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
        <div className="grid grid-cols-4 gap-4">
          {[1, 2, 3, 4].map((i) => (
            <Skeleton key={i} className="h-24" />
          ))}
        </div>
      </div>
    );
  }

  if (!sites || sites.length === 0) {
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

  const totalAgents = sites.reduce((a, s) => a + s.agentCount, 0);
  const avgDown =
    sites.filter((s) => s.avgDownMbps != null).length > 0
      ? sites.reduce((a, s) => a + (s.avgDownMbps ?? 0), 0) /
        sites.filter((s) => s.avgDownMbps != null).length
      : null;
  const unstableSites = sites.filter((s) => s.ipChanges90d > 3).length;
  const satelliteSites = sites.filter(
    (s) => s.connType === 'satellite' || s.connType === 'cellular',
  ).length;
  const slaBreaches = sites.filter(
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

      <div className="grid grid-cols-2 lg:grid-cols-5 gap-4">
        <KpiCard icon={MapPin} label="Sites" value={sites.length} />
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
            <SiteMap sites={sites} onSiteClick={handleSiteClick} />
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
              <SitesTable sites={sites} onSiteClick={handleSiteClick} />
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

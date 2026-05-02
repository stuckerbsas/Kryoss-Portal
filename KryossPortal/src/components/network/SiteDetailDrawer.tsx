import { X, Monitor, AlertTriangle, Star, Gauge, MapPin } from 'lucide-react';
import { toast } from 'sonner';
import { useSpeedHistory, useSiteMachines, useUpdateSite, useSiteLocationHistory, type NetworkSite } from '@/api/networkSites';
import { useOrgParam } from '@/hooks/useOrgParam';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { SpeedHistoryChart } from './SpeedHistoryChart';

interface SiteDetailDrawerProps {
  site: NetworkSite;
  onClose: () => void;
}

function SlaStatus({ site }: { site: NetworkSite }) {
  if (site.contractedDownMbps == null || site.avgDownMbps == null) return null;

  const ratio = site.avgDownMbps / site.contractedDownMbps;
  const pct = Math.round(ratio * 100);
  const ok = ratio >= 0.8;

  return (
    <div className={`flex items-center gap-2 px-3 py-2 rounded-lg text-sm font-medium ${ok ? 'bg-green-50 text-green-800' : 'bg-red-50 text-red-800'}`}>
      {!ok && <AlertTriangle className="size-4" />}
      SLA: {pct}% of contracted {site.contractedDownMbps} Mbps
      {ok ? ' — Compliant' : ' — Below threshold'}
    </div>
  );
}

export function SiteDetailDrawer({ site, onClose }: SiteDetailDrawerProps) {
  const { orgId } = useOrgParam();
  const { data: speedData, isLoading: speedLoading } = useSpeedHistory(site.id);
  const { data: machines, isLoading: machinesLoading } = useSiteMachines(site.id);
  const { data: locationHistory } = useSiteLocationHistory(site.id);
  const updateSite = useUpdateSite(orgId);

  const handleTogglePrimary = () => {
    updateSite.mutate(
      { siteId: site.id, isPrimary: !site.isPrimary },
      {
        onSuccess: () => toast.success(site.isPrimary ? 'Primary cleared' : 'Set as primary site'),
        onError: (err: Error) => toast.error(err.message),
      },
    );
  };

  const handleSpeedTestMachine = (machineId: string) => {
    updateSite.mutate(
      { siteId: site.id, speedTestMachineId: machineId === '__none__' ? '00000000-0000-0000-0000-000000000000' : machineId },
      {
        onSuccess: () => toast.success(machineId === '__none__' ? 'Speed test device cleared' : 'Speed test device set'),
        onError: (err: Error) => toast.error(err.message),
      },
    );
  };

  return (
    <div className="fixed inset-y-0 right-0 w-full max-w-2xl bg-background border-l shadow-xl z-[1000] overflow-y-auto">
      <div className="sticky top-0 bg-background border-b px-4 py-3 flex items-center justify-between z-10">
        <div className="flex items-center gap-2">
          <div>
            <div className="flex items-center gap-2">
              <h3 className="font-semibold">{site.siteName}</h3>
              {site.isPrimary && (
                <Badge className="bg-amber-100 text-amber-800 text-xs">Primary</Badge>
              )}
            </div>
            <p className="text-xs text-muted-foreground">
              {site.publicIp} {site.geoCity && `— ${site.geoCity}, ${site.geoCountry}`}
            </p>
          </div>
        </div>
        <Button variant="ghost" size="icon" onClick={onClose}>
          <X className="size-4" />
        </Button>
      </div>

      <div className="p-4 space-y-6">
        <SlaStatus site={site} />

        <div className="flex flex-wrap items-center gap-3">
          <Button
            variant={site.isPrimary ? 'default' : 'outline'}
            size="sm"
            onClick={handleTogglePrimary}
            disabled={updateSite.isPending}
          >
            <Star className={`size-4 mr-1 ${site.isPrimary ? 'fill-current' : ''}`} />
            {site.isPrimary ? 'Primary Site' : 'Set as Primary'}
          </Button>

          <div className="flex items-center gap-2">
            <Gauge className="size-4 text-muted-foreground" />
            <Select
              value={site.speedTestMachineId ?? '__none__'}
              onValueChange={handleSpeedTestMachine}
              disabled={updateSite.isPending || machinesLoading}
            >
              <SelectTrigger className="w-[200px] h-8 text-xs">
                <SelectValue placeholder="Speed test device" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="__none__">Auto (any machine)</SelectItem>
                {machines?.map((m) => (
                  <SelectItem key={m.id} value={m.id}>{m.hostname}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        </div>

        <div className="grid grid-cols-3 gap-3 text-sm">
          <div className="bg-muted/50 rounded-lg p-3">
            <p className="text-xs text-muted-foreground">Download</p>
            <p className="font-bold text-lg">{site.avgDownMbps?.toFixed(1) ?? '--'}</p>
            <p className="text-xs text-muted-foreground">Mbps</p>
          </div>
          <div className="bg-muted/50 rounded-lg p-3">
            <p className="text-xs text-muted-foreground">Upload</p>
            <p className="font-bold text-lg">{site.avgUpMbps?.toFixed(1) ?? '--'}</p>
            <p className="text-xs text-muted-foreground">Mbps</p>
          </div>
          <div className="bg-muted/50 rounded-lg p-3">
            <p className="text-xs text-muted-foreground">Latency</p>
            <p className="font-bold text-lg">{site.avgLatencyMs?.toFixed(0) ?? '--'}</p>
            <p className="text-xs text-muted-foreground">ms</p>
          </div>
        </div>

        <div>
          <h4 className="text-sm font-semibold mb-2">Speed History (90d)</h4>
          {speedLoading ? (
            <Skeleton className="h-[280px]" />
          ) : speedData ? (
            <SpeedHistoryChart data={speedData} />
          ) : null}
        </div>

        <div>
          <h4 className="text-sm font-semibold mb-2 flex items-center gap-2">
            <Monitor className="size-4" />
            Machines at this site ({machines?.length ?? 0})
          </h4>
          {machinesLoading ? (
            <Skeleton className="h-32" />
          ) : machines && machines.length > 0 ? (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Hostname</TableHead>
                  <TableHead>OS</TableHead>
                  <TableHead className="text-right">Down</TableHead>
                  <TableHead className="text-right">Latency</TableHead>
                  <TableHead>Last Seen</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {machines.map((m) => (
                  <TableRow key={m.id}>
                    <TableCell className="font-medium">{m.hostname}</TableCell>
                    <TableCell className="text-xs">{m.osName ?? '—'}</TableCell>
                    <TableCell className="text-right font-mono text-sm">
                      {m.latestDiag?.downloadMbps?.toFixed(1) ?? '—'}
                    </TableCell>
                    <TableCell className="text-right font-mono text-sm">
                      {m.latestDiag?.internetLatencyMs?.toFixed(0) ?? '—'}
                    </TableCell>
                    <TableCell className="text-xs text-muted-foreground">
                      {m.lastSeenAt ? new Date(m.lastSeenAt).toLocaleDateString() : '—'}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          ) : (
            <p className="text-sm text-muted-foreground">No machines found</p>
          )}
        </div>

        {locationHistory && locationHistory.length > 1 && (
          <div>
            <h4 className="text-sm font-semibold mb-2 flex items-center gap-2">
              <MapPin className="size-4" />
              Location History (last {locationHistory.length} IPs)
            </h4>
            <div className="space-y-2">
              {locationHistory.map((loc, i) => (
                <div key={loc.publicIp} className={`flex items-center justify-between text-xs p-2 rounded-lg ${i === 0 ? 'bg-green-50 border border-green-200' : 'bg-muted/50'}`}>
                  <div>
                    <span className="font-mono font-medium">{loc.publicIp}</span>
                    {loc.geoCity && <span className="text-muted-foreground ml-2">— {loc.geoCity}, {loc.geoCountry}</span>}
                  </div>
                  <div className="text-muted-foreground text-right">
                    <div>{loc.isp ?? '—'}</div>
                    <div>{new Date(loc.lastSeen).toLocaleDateString()}</div>
                  </div>
                </div>
              ))}
            </div>
          </div>
        )}

        <div className="text-xs text-muted-foreground space-y-1">
          <p>ISP: {site.isp ?? '—'} | ASN: {site.asn ?? '—'}</p>
          <p>Type: {site.connType ?? '—'} | IP changes (90d): {site.ipChanges90d}</p>
          {site.connType === 'cellular' && (
            <Badge className="bg-amber-100 text-amber-800">Cellular connection detected</Badge>
          )}
          {site.connType === 'satellite' && (
            <Badge className="bg-red-100 text-red-800">Satellite connection detected</Badge>
          )}
        </div>
      </div>
    </div>
  );
}

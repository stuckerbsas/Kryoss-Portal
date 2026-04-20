import { X, Monitor, AlertTriangle } from 'lucide-react';
import { useSpeedHistory, useSiteMachines, type NetworkSite } from '@/api/networkSites';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
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
  const { data: speedData, isLoading: speedLoading } = useSpeedHistory(site.id);
  const { data: machines, isLoading: machinesLoading } = useSiteMachines(site.id);

  return (
    <div className="fixed inset-y-0 right-0 w-full max-w-lg bg-background border-l shadow-xl z-50 overflow-y-auto">
      <div className="sticky top-0 bg-background border-b px-4 py-3 flex items-center justify-between">
        <div>
          <h3 className="font-semibold">{site.siteName}</h3>
          <p className="text-xs text-muted-foreground">
            {site.publicIp} {site.geoCity && `— ${site.geoCity}, ${site.geoCountry}`}
          </p>
        </div>
        <Button variant="ghost" size="icon" onClick={onClose}>
          <X className="size-4" />
        </Button>
      </div>

      <div className="p-4 space-y-6">
        <SlaStatus site={site} />

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

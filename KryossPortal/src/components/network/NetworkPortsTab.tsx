import { useState } from 'react';
import { Plug, AlertTriangle, ChevronDown, ChevronRight, Monitor } from 'lucide-react';
import { useNetworkPorts, type NetworkPort } from '@/api/networkPorts';
import { useOrgParam } from '@/hooks/useOrgParam';
import { EmptyState } from '@/components/shared/EmptyState';
import { Badge } from '@/components/ui/badge';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
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

function PortRow({ port }: { port: NetworkPort }) {
  const [expanded, setExpanded] = useState(false);

  return (
    <>
      <TableRow
        className="cursor-pointer hover:bg-muted/50"
        onClick={() => setExpanded(!expanded)}
      >
        <TableCell className="w-8">
          {expanded ? (
            <ChevronDown className="h-4 w-4 text-muted-foreground" />
          ) : (
            <ChevronRight className="h-4 w-4 text-muted-foreground" />
          )}
        </TableCell>
        <TableCell className="font-mono font-medium">{port.port}</TableCell>
        <TableCell className="uppercase text-xs">{port.protocol}</TableCell>
        <TableCell className="text-sm">{port.service ?? '--'}</TableCell>
        <TableCell>
          <span className="font-bold tabular-nums">{port.machineCount}</span>
        </TableCell>
        <TableCell>
          {port.isRisky && (
            <Badge variant="destructive" className="text-xs">
              <AlertTriangle className="h-3 w-3 mr-1" />
              Risky
            </Badge>
          )}
        </TableCell>
      </TableRow>
      {expanded && (
        <TableRow>
          <TableCell />
          <TableCell colSpan={5} className="bg-muted/30 py-2">
            <div className="flex flex-wrap gap-1">
              {port.machines.map((m) => (
                <Badge key={m.id} variant="outline" className="text-xs font-mono">
                  {m.hostname}
                  <span className="ml-1 text-muted-foreground">({m.state})</span>
                </Badge>
              ))}
            </div>
          </TableCell>
        </TableRow>
      )}
    </>
  );
}

export function NetworkPortsTab() {
  const { orgId } = useOrgParam();
  const [stateFilter, setStateFilter] = useState<string>('all');
  const { data, isLoading } = useNetworkPorts(orgId, stateFilter === 'all' ? undefined : stateFilter);

  if (isLoading) {
    return (
      <div className="space-y-4">
        <div className="grid gap-4 grid-cols-1 sm:grid-cols-3">
          {Array.from({ length: 3 }).map((_, i) => (
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

  if (!data || data.ports.length === 0) {
    return (
      <EmptyState
        icon={<Plug className="size-10" />}
        title="No port data"
        description="Run a port scan with the agent to see consolidated port results."
      />
    );
  }

  const riskyCount = data.ports.filter((p) => p.isRisky).length;

  return (
    <div className="space-y-6">
      <div>
        <h3 className="text-lg font-semibold">Network Ports (Consolidated)</h3>
        <p className="text-sm text-muted-foreground">
          All ports across the fleet grouped by port number.
        </p>
      </div>

      <div className="grid gap-4 grid-cols-1 sm:grid-cols-3">
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Total Open Ports</CardTitle>
            <Plug className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <p className="text-2xl font-bold">{data.totalOpenPorts}</p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Unique Ports</CardTitle>
            <Monitor className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <p className="text-2xl font-bold">{data.total}</p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Risky Ports</CardTitle>
            <AlertTriangle className="h-4 w-4 text-red-500" />
          </CardHeader>
          <CardContent>
            <p
              className="text-2xl font-bold"
              style={{ color: riskyCount > 0 ? '#C0392B' : '#006536' }}
            >
              {riskyCount}
            </p>
          </CardContent>
        </Card>
      </div>

      <div className="flex items-center gap-3">
        <span className="text-sm text-muted-foreground">Filter by state:</span>
        <Select value={stateFilter} onValueChange={setStateFilter}>
          <SelectTrigger className="w-40">
            <SelectValue placeholder="All" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All</SelectItem>
            <SelectItem value="open">Open</SelectItem>
            <SelectItem value="filtered">Filtered</SelectItem>
            <SelectItem value="closed">Closed</SelectItem>
          </SelectContent>
        </Select>
      </div>

      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="flex items-center gap-2 text-base">
            <Plug className="h-4 w-4 text-muted-foreground" />
            Ports by Number
          </CardTitle>
        </CardHeader>
        <CardContent>
          {/* Mobile cards */}
          <div className="space-y-3 sm:hidden">
            {data.ports.map((p) => (
              <div key={`${p.port}-${p.protocol}`} className="rounded-lg border p-4">
                <div className="flex items-center justify-between">
                  <span className="font-mono font-medium text-sm">{p.port}/{p.protocol}</span>
                  <div className="flex items-center gap-2">
                    {p.isRisky && (
                      <Badge variant="destructive" className="text-xs">
                        <AlertTriangle className="h-3 w-3 mr-1" />
                        Risky
                      </Badge>
                    )}
                  </div>
                </div>
                <div className="mt-2 flex items-center gap-4 text-xs text-muted-foreground">
                  <span>Service: {p.service ?? '--'}</span>
                  <span>Machines: <span className="font-bold tabular-nums">{p.machineCount}</span></span>
                </div>
              </div>
            ))}
          </div>

          {/* Desktop table */}
          <div className="hidden sm:block overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="w-8" />
                  <TableHead>Port</TableHead>
                  <TableHead>Protocol</TableHead>
                  <TableHead>Service</TableHead>
                  <TableHead>Machines</TableHead>
                  <TableHead>Risk</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data.ports.map((p) => (
                  <PortRow key={`${p.port}-${p.protocol}`} port={p} />
                ))}
              </TableBody>
            </Table>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}

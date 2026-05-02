import { Plug, AlertTriangle, ShieldAlert, Monitor } from 'lucide-react';
import { useOrgPorts } from '@/api/ports';
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

function riskBadge(risk: string) {
  const config: Record<string, string> = {
    critical: 'bg-red-200 text-red-900',
    high: 'bg-red-100 text-red-800',
    medium: 'bg-amber-100 text-amber-800',
    low: 'bg-blue-100 text-blue-800',
  };
  return (
    <Badge variant="secondary" className={config[risk] ?? 'bg-gray-100 text-gray-500'}>
      {risk}
    </Badge>
  );
}

export function PortsTab() {
  const { orgId } = useOrgParam();
  const { data, isLoading } = useOrgPorts(orgId);

  if (isLoading) {
    return (
      <div className="space-y-4">
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

  if (!data || data.topRiskyPorts.length === 0) {
    return (
      <EmptyState
        icon={<Plug className="size-10" />}
        title="No port scan data"
        description="Run a port scan with the agent to see network port results across your fleet."
      />
    );
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div>
        <h3 className="text-lg font-semibold">Network Ports</h3>
        <p className="text-sm text-muted-foreground">
          Port scan results aggregated across all machines in this organization.
        </p>
      </div>

      {/* KPI cards */}
      <div className="grid gap-4 grid-cols-1 sm:grid-cols-2 lg:grid-cols-4">
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">
              Total Machines
            </CardTitle>
            <Monitor className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <p className="text-2xl font-bold">{data.totalMachines}</p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">
              Machines with Risky Ports
            </CardTitle>
            <AlertTriangle className="h-4 w-4 text-amber-500" />
          </CardHeader>
          <CardContent>
            <p className="text-2xl font-bold" style={{ color: data.machinesWithRiskyPorts > 0 ? '#D97706' : '#006536' }}>
              {data.machinesWithRiskyPorts}
            </p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">
              Critical Ports
            </CardTitle>
            <ShieldAlert className="h-4 w-4 text-red-500" />
          </CardHeader>
          <CardContent>
            <p className="text-2xl font-bold" style={{ color: data.criticalPorts > 0 ? '#C0392B' : '#006536' }}>
              {data.criticalPorts}
            </p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">
              High Risk Ports
            </CardTitle>
            <AlertTriangle className="h-4 w-4 text-red-500" />
          </CardHeader>
          <CardContent>
            <p className="text-2xl font-bold" style={{ color: data.highRiskPorts > 0 ? '#C0392B' : '#006536' }}>
              {data.highRiskPorts}
            </p>
          </CardContent>
        </Card>
      </div>

      {/* Top risky ports table */}
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="flex items-center gap-2 text-base">
            <Plug className="h-4 w-4 text-muted-foreground" />
            Top Risky Ports
          </CardTitle>
        </CardHeader>
        <CardContent>
          {/* Mobile card view */}
          <div className="space-y-3 sm:hidden">
            {data.topRiskyPorts.map((p, i) => (
              <div key={i} className="rounded-lg border p-4">
                <div className="flex items-start justify-between gap-2">
                  <span className="font-mono font-medium text-sm">{p.port}</span>
                  {riskBadge(p.risk)}
                </div>
                <div className="mt-2 flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
                  <span>{p.service ?? '--'}</span>
                  <span>{p.machineCount} / {data.totalMachines} machines</span>
                </div>
              </div>
            ))}
          </div>
          {/* Desktop table */}
          <div className="hidden sm:block overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Port</TableHead>
                  <TableHead>Service</TableHead>
                  <TableHead>Risk</TableHead>
                  <TableHead>Machines</TableHead>
                  <TableHead className="hidden lg:table-cell">Affected Hosts</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data.topRiskyPorts.map((p, i) => (
                  <TableRow key={i}>
                    <TableCell className="font-mono font-medium">{p.port}</TableCell>
                    <TableCell className="text-sm">{p.service ?? '--'}</TableCell>
                    <TableCell>{riskBadge(p.risk)}</TableCell>
                    <TableCell>
                      <span className="font-bold tabular-nums">{p.machineCount}</span>
                      <span className="text-muted-foreground text-sm"> / {data.totalMachines}</span>
                    </TableCell>
                    <TableCell className="hidden lg:table-cell text-sm text-muted-foreground max-w-md">
                      <div className="flex flex-wrap gap-1">
                        {p.machines.slice(0, 8).map((h) => (
                          <Badge key={h} variant="outline" className="text-xs font-mono">
                            {h}
                          </Badge>
                        ))}
                        {p.machines.length > 8 && (
                          <Badge variant="outline" className="text-xs">
                            +{p.machines.length - 8} more
                          </Badge>
                        )}
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}

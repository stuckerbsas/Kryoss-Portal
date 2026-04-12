import { ShieldAlert, AlertTriangle, Monitor } from 'lucide-react';
import { useOrgThreats } from '@/api/threats';
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

function severityBadge(severity: string) {
  const config: Record<string, string> = {
    critical: 'bg-red-200 text-red-900',
    high: 'bg-red-100 text-red-800',
    medium: 'bg-amber-100 text-amber-800',
    low: 'bg-blue-100 text-blue-800',
    info: 'bg-gray-100 text-gray-500',
  };
  return (
    <Badge variant="secondary" className={config[severity] ?? 'bg-gray-100 text-gray-500'}>
      {severity}
    </Badge>
  );
}

function categoryBadge(category: string) {
  const config: Record<string, string> = {
    browser_hijacker: 'bg-amber-100 text-amber-800',
    adware: 'bg-yellow-100 text-yellow-800',
    stalkerware: 'bg-red-200 text-red-900',
    keylogger: 'bg-red-200 text-red-900',
    rat: 'bg-red-200 text-red-900',
    c2_tool: 'bg-red-200 text-red-900',
    cryptominer: 'bg-purple-100 text-purple-800',
    ransomware: 'bg-red-200 text-red-900',
    fake_av: 'bg-amber-100 text-amber-800',
    loader_stealer: 'bg-red-100 text-red-800',
    employee_monitor: 'bg-blue-100 text-blue-800',
    pup: 'bg-gray-100 text-gray-600',
  };
  return (
    <Badge variant="secondary" className={config[category] ?? 'bg-gray-100 text-gray-500'}>
      {category.replace(/_/g, ' ')}
    </Badge>
  );
}

export function ThreatsTab() {
  const { orgId } = useOrgParam();
  const { data, isLoading } = useOrgThreats(orgId);

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

  if (!data || data.topThreats.length === 0) {
    return (
      <EmptyState
        icon={<ShieldAlert className="size-10" />}
        title="No threat data"
        description="Run an assessment with the agent to detect threat indicators across your fleet."
      />
    );
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div>
        <h3 className="text-lg font-semibold">Threat Detection</h3>
        <p className="text-sm text-muted-foreground">
          Threat indicators detected across all machines in this organization.
        </p>
      </div>

      {/* KPI cards */}
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
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
              Machines with Threats
            </CardTitle>
            <AlertTriangle className="h-4 w-4 text-amber-500" />
          </CardHeader>
          <CardContent>
            <p className="text-2xl font-bold" style={{ color: data.machinesWithThreats > 0 ? '#D97706' : '#006536' }}>
              {data.machinesWithThreats}
            </p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">
              Critical Threats
            </CardTitle>
            <ShieldAlert className="h-4 w-4 text-red-500" />
          </CardHeader>
          <CardContent>
            <p className="text-2xl font-bold" style={{ color: data.criticalThreats > 0 ? '#C0392B' : '#006536' }}>
              {data.criticalThreats}
            </p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">
              High Threats
            </CardTitle>
            <AlertTriangle className="h-4 w-4 text-red-500" />
          </CardHeader>
          <CardContent>
            <p className="text-2xl font-bold" style={{ color: data.highThreats > 0 ? '#C0392B' : '#006536' }}>
              {data.highThreats}
            </p>
          </CardContent>
        </Card>
      </div>

      {/* Top threats table */}
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="flex items-center gap-2 text-base">
            <ShieldAlert className="h-4 w-4 text-muted-foreground" />
            Top Threats
          </CardTitle>
        </CardHeader>
        <CardContent>
          <div className="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Threat Name</TableHead>
                  <TableHead>Category</TableHead>
                  <TableHead>Severity</TableHead>
                  <TableHead>Machines</TableHead>
                  <TableHead>Affected Hosts</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data.topThreats.map((t, i) => (
                  <TableRow key={i}>
                    <TableCell className="font-medium text-sm">{t.threatName}</TableCell>
                    <TableCell>{categoryBadge(t.category)}</TableCell>
                    <TableCell>{severityBadge(t.severity)}</TableCell>
                    <TableCell>
                      <span className="font-bold tabular-nums">{t.machineCount}</span>
                      <span className="text-muted-foreground text-sm"> / {data.totalMachines}</span>
                    </TableCell>
                    <TableCell className="text-sm text-muted-foreground max-w-md">
                      <div className="flex flex-wrap gap-1">
                        {t.machines.slice(0, 8).map((h) => (
                          <Badge key={h} variant="outline" className="text-xs font-mono">
                            {h}
                          </Badge>
                        ))}
                        {t.machines.length > 8 && (
                          <Badge variant="outline" className="text-xs">
                            +{t.machines.length - 8} more
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

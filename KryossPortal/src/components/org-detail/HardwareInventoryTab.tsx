import { useState, useMemo } from 'react';
import {
  Search,
  Monitor,
  CheckCircle2,
  XCircle,
  Cpu,
} from 'lucide-react';
import { useHardwareInventory } from '@/api/inventory';
import { useOrgParam } from '@/hooks/useOrgParam';
import { EmptyState } from '@/components/shared/EmptyState';
import { Input } from '@/components/ui/input';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Table,
  TableHeader,
  TableRow,
  TableHead,
  TableBody,
  TableCell,
} from '@/components/ui/table';

function formatRelativeTime(dateStr: string | null): string {
  if (!dateStr) return 'Never';
  const now = Date.now();
  const then = new Date(dateStr).getTime();
  const diffMs = now - then;
  const diffMin = Math.floor(diffMs / 60000);
  if (diffMin < 1) return 'Just now';
  if (diffMin < 60) return `${diffMin}m ago`;
  const diffHrs = Math.floor(diffMin / 60);
  if (diffHrs < 24) return `${diffHrs}h ago`;
  const diffDays = Math.floor(diffHrs / 24);
  if (diffDays < 30) return `${diffDays}d ago`;
  return new Date(dateStr).toLocaleDateString();
}

export function HardwareInventoryTab() {
  const { orgId } = useOrgParam();
  const { data, isLoading } = useHardwareInventory(orgId);
  const [search, setSearch] = useState('');

  const filtered = useMemo(() => {
    if (!data) return [];
    if (!search) return data.items;
    const lower = search.toLowerCase();
    return data.items.filter(
      (m) =>
        m.hostname.toLowerCase().includes(lower) ||
        m.manufacturer?.toLowerCase().includes(lower) ||
        m.model?.toLowerCase().includes(lower) ||
        m.osName?.toLowerCase().includes(lower),
    );
  }, [data, search]);

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
        <div className="space-y-2">
          {Array.from({ length: 5 }).map((_, i) => (
            <Skeleton key={i} className="h-12 w-full" />
          ))}
        </div>
      </div>
    );
  }

  if (!data || data.items.length === 0) {
    return (
      <EmptyState
        icon={<Monitor className="size-10" />}
        title="No hardware data"
        description="Enroll machines and run an assessment to see hardware inventory."
      />
    );
  }

  const readyPct =
    data.total > 0 ? Math.round((data.win11Ready / data.total) * 100) : 0;

  return (
    <div className="space-y-4">
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
            <p className="text-2xl font-bold">{data.total}</p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">
              Win11 Ready
            </CardTitle>
            <CheckCircle2 className="h-4 w-4 text-green-600" />
          </CardHeader>
          <CardContent>
            <p className="text-2xl font-bold text-green-600">
              {data.win11Ready}
            </p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">
              Not Ready
            </CardTitle>
            <XCircle className="h-4 w-4 text-red-600" />
          </CardHeader>
          <CardContent>
            <p className="text-2xl font-bold text-red-600">
              {data.win11NotReady}
            </p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">
              % Ready
            </CardTitle>
            <Cpu className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <p className="text-2xl font-bold">{readyPct}%</p>
          </CardContent>
        </Card>
      </div>

      {/* Search */}
      <div className="relative max-w-sm">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 size-4 text-muted-foreground" />
        <Input
          placeholder="Search by hostname, manufacturer, model..."
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          className="pl-9"
        />
      </div>

      {/* Table */}
      <div className="overflow-x-auto">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Hostname</TableHead>
              <TableHead>OS</TableHead>
              <TableHead>CPU</TableHead>
              <TableHead>RAM</TableHead>
              <TableHead>Disk</TableHead>
              <TableHead>Manufacturer / Model</TableHead>
              <TableHead>TPM</TableHead>
              <TableHead>Win11</TableHead>
              <TableHead>Last Seen</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {filtered.map((m) => {
              const lowDisk =
                m.diskFreeGb !== null && m.diskFreeGb < 20;
              const noTpm = m.tpmPresent !== true;
              let rowClass = '';
              if (lowDisk) rowClass = 'bg-red-50';
              else if (noTpm) rowClass = 'bg-yellow-50';

              return (
                <TableRow key={m.id} className={rowClass}>
                  <TableCell className="font-medium">{m.hostname}</TableCell>
                  <TableCell className="text-muted-foreground text-sm">
                    {m.osName ?? 'Unknown'}
                    {m.osVersion ? ` (${m.osVersion})` : ''}
                  </TableCell>
                  <TableCell className="text-muted-foreground text-sm">
                    {m.cpuName ?? 'N/A'}
                    {m.cpuCores != null ? ` (${m.cpuCores}c)` : ''}
                  </TableCell>
                  <TableCell className="text-muted-foreground">
                    {m.ramGb != null ? `${m.ramGb} GB` : 'N/A'}
                  </TableCell>
                  <TableCell className="text-muted-foreground text-sm">
                    {m.diskType ?? ''}
                    {m.diskSizeGb != null ? ` ${m.diskSizeGb} GB` : ''}
                    {m.diskFreeGb != null ? (
                      <span
                        className={
                          lowDisk ? 'text-red-600 font-medium' : ''
                        }
                      >
                        {' '}
                        ({m.diskFreeGb.toFixed(1)} free)
                      </span>
                    ) : null}
                  </TableCell>
                  <TableCell className="text-muted-foreground text-sm">
                    {[m.manufacturer, m.model].filter(Boolean).join(' / ') ||
                      'N/A'}
                  </TableCell>
                  <TableCell>
                    {m.tpmPresent ? (
                      <span className="text-green-600 text-sm">
                        {m.tpmVersion ?? 'Yes'}
                      </span>
                    ) : (
                      <span className="text-red-600 text-sm">No</span>
                    )}
                  </TableCell>
                  <TableCell>
                    {m.win11Ready ? (
                      <CheckCircle2 className="h-4 w-4 text-green-600" />
                    ) : (
                      <span
                        title={m.win11Blockers.join(', ')}
                        className="cursor-help"
                      >
                        <XCircle className="h-4 w-4 text-red-500" />
                      </span>
                    )}
                  </TableCell>
                  <TableCell className="text-muted-foreground text-sm">
                    {formatRelativeTime(m.lastSeenAt)}
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </div>
      <p className="text-sm text-muted-foreground">
        Showing {filtered.length} of {data.total} machines
      </p>
    </div>
  );
}

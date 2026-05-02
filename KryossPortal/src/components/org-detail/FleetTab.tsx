import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Search, Monitor } from 'lucide-react';
import { useMachines } from '@/api/machines';
import { useOrgParam } from '@/hooks/useOrgParam';
import { GradeBadge } from '@/components/shared/GradeBadge';
import { EmptyState } from '@/components/shared/EmptyState';
import { Input } from '@/components/ui/input';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Table,
  TableHeader,
  TableRow,
  TableHead,
  TableBody,
  TableCell,
} from '@/components/ui/table';
import { Badge } from '@/components/ui/badge';

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

function isOnline(lastHeartbeat: string | null): boolean {
  if (!lastHeartbeat) return false;
  return Date.now() - new Date(lastHeartbeat).getTime() < 30 * 60000;
}

const PAGE_SIZE = 25;

export function FleetTab() {
  const { orgId, orgSlug } = useOrgParam();
  const navigate = useNavigate();
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);

  const { data, isLoading } = useMachines({
    organizationId: orgId,
    search: search || undefined,
    page,
    pageSize: PAGE_SIZE,
  });

  const totalPages = data ? Math.max(1, Math.ceil(data.total / PAGE_SIZE)) : 1;

  const goToMachine = (hostname: string) =>
    navigate(`/organizations/${orgSlug}/machines/${hostname}`);

  return (
    <div className="space-y-4">
      <div className="relative max-w-sm">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 size-4 text-muted-foreground" />
        <Input
          placeholder="Search machines..."
          value={search}
          onChange={(e) => {
            setSearch(e.target.value);
            setPage(1);
          }}
          className="pl-9"
        />
      </div>

      {isLoading ? (
        <div className="space-y-2">
          {Array.from({ length: 5 }).map((_, i) => (
            <Skeleton key={i} className="h-12 w-full" />
          ))}
        </div>
      ) : !data || data.items.length === 0 ? (
        <EmptyState
          icon={<Monitor className="size-10" />}
          title="No machines enrolled yet"
          description="Generate an enrollment code to get started."
        />
      ) : (
        <>
          {/* Mobile: cards */}
          <div className="space-y-3 sm:hidden">
            {data.items.map((m) => (
              <div
                key={m.id}
                className="rounded-lg border p-4 cursor-pointer hover:bg-muted/50 transition-colors"
                onClick={() => goToMachine(m.hostname)}
              >
                <div className="flex items-center justify-between mb-2">
                  <div className="flex items-center gap-2 min-w-0">
                    <span
                      className={`inline-block size-2.5 rounded-full shrink-0 ${
                        isOnline(m.lastHeartbeatAt) ? 'bg-green-500 animate-pulse' : 'bg-gray-300'
                      }`}
                    />
                    <span className="font-medium text-sm truncate">{m.hostname}</span>
                  </div>
                  <GradeBadge grade={m.latestScore?.grade} score={m.latestScore?.globalScore} />
                </div>
                <div className="flex items-center justify-between text-xs text-muted-foreground">
                  <span>{m.osName ?? 'Unknown'}</span>
                  <span>{formatRelativeTime(m.lastSeenAt)}</span>
                </div>
              </div>
            ))}
          </div>

          {/* Desktop: table */}
          <div className="hidden sm:block">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Hostname</TableHead>
                  <TableHead>OS</TableHead>
                  <TableHead className="hidden lg:table-cell">IP</TableHead>
                  <TableHead className="hidden lg:table-cell">CPU / RAM</TableHead>
                  <TableHead>Score</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead className="hidden lg:table-cell">Agent</TableHead>
                  <TableHead>Last Seen</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data.items.map((m) => (
                  <TableRow
                    key={m.id}
                    className="cursor-pointer"
                    onClick={() => goToMachine(m.hostname)}
                  >
                    <TableCell className="font-medium flex items-center gap-2">
                      <span
                        className={`inline-block size-2.5 rounded-full ${
                          isOnline(m.lastHeartbeatAt) ? 'bg-green-500 animate-pulse' : 'bg-gray-300'
                        }`}
                        title={isOnline(m.lastHeartbeatAt) ? 'Online' : 'Offline'}
                      />
                      {m.hostname}
                    </TableCell>
                    <TableCell className="text-muted-foreground">
                      {m.osName ?? 'Unknown'}
                    </TableCell>
                    <TableCell className="text-muted-foreground text-xs font-mono hidden lg:table-cell">
                      {m.ipAddress ?? '—'}
                    </TableCell>
                    <TableCell className="text-muted-foreground hidden lg:table-cell">
                      {m.cpuName ?? 'N/A'}
                      {m.ramGb != null ? ` / ${m.ramGb} GB` : ''}
                    </TableCell>
                    <TableCell>
                      <GradeBadge
                        grade={m.latestScore?.grade}
                        score={m.latestScore?.globalScore}
                      />
                    </TableCell>
                    <TableCell>
                      <Badge
                        variant="secondary"
                        className={
                          m.isActive
                            ? 'bg-green-100 text-green-800 hover:bg-green-100'
                            : 'bg-gray-100 text-gray-500 hover:bg-gray-100'
                        }
                      >
                        {m.isActive ? 'Active' : 'Inactive'}
                      </Badge>
                    </TableCell>
                    <TableCell className="text-muted-foreground text-xs font-mono hidden lg:table-cell">
                      {m.agentVersion ? `v${m.agentVersion}` : '—'}
                    </TableCell>
                    <TableCell className="text-muted-foreground">
                      {formatRelativeTime(m.lastSeenAt)}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>

          <div className="flex items-center justify-between pt-2">
            <p className="text-sm text-muted-foreground">
              Page {page} of {totalPages} ({data.total} machines)
            </p>
            <div className="flex gap-2">
              <Button
                variant="outline"
                size="sm"
                disabled={page <= 1}
                onClick={() => setPage((p) => p - 1)}
              >
                Previous
              </Button>
              <Button
                variant="outline"
                size="sm"
                disabled={page >= totalPages}
                onClick={() => setPage((p) => p + 1)}
              >
                Next
              </Button>
            </div>
          </div>
        </>
      )}
    </div>
  );
}

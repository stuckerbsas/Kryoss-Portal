import { useState, useMemo } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { ArrowLeft, Search } from 'lucide-react';
import { useRunDetail } from '@/api/machines';
import { useCatalogControls } from '@/api/catalog';
import { GradeBadge } from '@/components/shared/GradeBadge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
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
  TableHeader,
  TableRow,
  TableHead,
  TableBody,
  TableCell,
} from '@/components/ui/table';
import { EmptyState } from '@/components/shared/EmptyState';

const PAGE_SIZE = 50;

const FRAMEWORKS = ['All', 'NIST', 'CIS', 'HIPAA', 'ISO27001', 'PCI-DSS'];
const SEVERITIES = ['All', 'critical', 'high', 'medium', 'low'];
const STATUSES = ['All', 'pass', 'warn', 'fail'];

function statusColor(status: string): string {
  switch (status.toLowerCase()) {
    case 'pass':
      return 'bg-green-100 text-green-800 hover:bg-green-100';
    case 'warn':
      return 'bg-amber-100 text-amber-800 hover:bg-amber-100';
    case 'fail':
      return 'bg-red-100 text-red-800 hover:bg-red-100';
    default:
      return 'bg-gray-100 text-gray-500 hover:bg-gray-100';
  }
}

function severityColor(severity: string): string {
  switch (severity.toLowerCase()) {
    case 'critical':
      return 'bg-red-200 text-red-900 hover:bg-red-200';
    case 'high':
      return 'bg-red-100 text-red-800 hover:bg-red-100';
    case 'medium':
      return 'bg-amber-100 text-amber-800 hover:bg-amber-100';
    case 'low':
      return 'bg-blue-100 text-blue-800 hover:bg-blue-100';
    default:
      return 'bg-gray-100 text-gray-500 hover:bg-gray-100';
  }
}

function formatDate(dateStr: string): string {
  return new Date(dateStr).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

function formatDuration(ms: number | null): string {
  if (ms == null) return 'N/A';
  if (ms < 1000) return `${ms}ms`;
  const secs = Math.round(ms / 1000);
  if (secs < 60) return `${secs}s`;
  const mins = Math.floor(secs / 60);
  const remSecs = secs % 60;
  return `${mins}m ${remSecs}s`;
}

export function RunDetail() {
  const { orgId, machineId, runId } = useParams<{
    orgId: string;
    machineId: string;
    runId: string;
  }>();
  const navigate = useNavigate();

  const { data: run, isLoading } = useRunDetail(machineId, runId);
  const { data: catalog } = useCatalogControls();

  const [framework, setFramework] = useState('All');
  const [severity, setSeverity] = useState('All');
  const [status, setStatus] = useState('All');
  const [searchText, setSearchText] = useState('');
  const [page, setPage] = useState(1);

  // Build framework -> controlId set lookup
  const frameworkControlIds = useMemo(() => {
    if (!catalog) return null;
    const map = new Map<string, Set<string>>();
    for (const ctrl of catalog.items) {
      for (const fw of ctrl.frameworks) {
        let set = map.get(fw.code);
        if (!set) {
          set = new Set<string>();
          map.set(fw.code, set);
        }
        set.add(ctrl.controlId);
      }
    }
    return map;
  }, [catalog]);

  // Filter results
  const filtered = useMemo(() => {
    if (!run) return [];
    let items = run.results;

    if (framework !== 'All' && frameworkControlIds) {
      const allowed = frameworkControlIds.get(framework);
      if (allowed) {
        items = items.filter((r) => allowed.has(r.controlId));
      } else {
        items = [];
      }
    }

    if (severity !== 'All') {
      items = items.filter(
        (r) => r.severity.toLowerCase() === severity.toLowerCase(),
      );
    }

    if (status !== 'All') {
      items = items.filter(
        (r) => r.status.toLowerCase() === status.toLowerCase(),
      );
    }

    if (searchText.trim()) {
      const q = searchText.toLowerCase();
      items = items.filter(
        (r) =>
          r.controlId.toLowerCase().includes(q) ||
          r.name.toLowerCase().includes(q),
      );
    }

    return items;
  }, [run, framework, severity, status, searchText, frameworkControlIds]);

  const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  const pageItems = filtered.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE);

  if (isLoading) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-8 w-48" />
        <Skeleton className="h-16 w-full" />
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }

  if (!run) {
    return (
      <EmptyState
        title="Run not found"
        description="This assessment run does not exist or you don't have access."
      />
    );
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center gap-4">
        <Button
          variant="ghost"
          size="sm"
          onClick={() =>
            navigate(`/organizations/${orgId}/machines/${machineId}`)
          }
        >
          <ArrowLeft className="size-4 mr-1" />
          Machine
        </Button>
        <h2 className="text-xl font-semibold">
          Run {formatDate(run.startedAt)}
        </h2>
      </div>

      {/* Stats Bar */}
      <div className="flex flex-wrap items-center gap-4">
        <div className="flex items-center gap-2">
          <span className="text-3xl font-bold tabular-nums">
            {run.globalScore ?? '--'}
          </span>
          <GradeBadge grade={run.grade} />
        </div>
        <div className="flex items-center gap-2">
          <Badge
            variant="secondary"
            className="bg-green-100 text-green-800 hover:bg-green-100"
          >
            {run.passCount ?? 0} Pass
          </Badge>
          <Badge
            variant="secondary"
            className="bg-amber-100 text-amber-800 hover:bg-amber-100"
          >
            {run.warnCount ?? 0} Warn
          </Badge>
          <Badge
            variant="secondary"
            className="bg-red-100 text-red-800 hover:bg-red-100"
          >
            {run.failCount ?? 0} Fail
          </Badge>
        </div>
        <span className="text-sm text-muted-foreground">
          Duration: {formatDuration(run.durationMs)}
        </span>
        {run.agentVersion && (
          <span className="text-sm text-muted-foreground">
            Agent: v{run.agentVersion}
          </span>
        )}
      </div>

      {/* Filter Bar */}
      <div className="flex flex-wrap items-center gap-3">
        <Select
          value={framework}
          onValueChange={(v) => {
            setFramework(v);
            setPage(1);
          }}
        >
          <SelectTrigger className="w-[140px]">
            <SelectValue placeholder="Framework" />
          </SelectTrigger>
          <SelectContent>
            {FRAMEWORKS.map((fw) => (
              <SelectItem key={fw} value={fw}>
                {fw}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>

        <Select
          value={severity}
          onValueChange={(v) => {
            setSeverity(v);
            setPage(1);
          }}
        >
          <SelectTrigger className="w-[130px]">
            <SelectValue placeholder="Severity" />
          </SelectTrigger>
          <SelectContent>
            {SEVERITIES.map((s) => (
              <SelectItem key={s} value={s}>
                {s === 'All' ? 'All Severities' : s}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>

        <Select
          value={status}
          onValueChange={(v) => {
            setStatus(v);
            setPage(1);
          }}
        >
          <SelectTrigger className="w-[120px]">
            <SelectValue placeholder="Status" />
          </SelectTrigger>
          <SelectContent>
            {STATUSES.map((s) => (
              <SelectItem key={s} value={s}>
                {s === 'All' ? 'All Statuses' : s}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>

        <div className="relative max-w-xs flex-1">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 size-4 text-muted-foreground" />
          <Input
            placeholder="Search by ID or name..."
            value={searchText}
            onChange={(e) => {
              setSearchText(e.target.value);
              setPage(1);
            }}
            className="pl-9"
          />
        </div>

        <span className="text-sm text-muted-foreground ml-auto">
          {filtered.length} results
        </span>
      </div>

      {/* Results Table */}
      {pageItems.length === 0 ? (
        <EmptyState
          title="No matching controls"
          description="Adjust your filters to see results."
        />
      ) : (
        <>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Control ID</TableHead>
                <TableHead>Name</TableHead>
                <TableHead>Category</TableHead>
                <TableHead>Severity</TableHead>
                <TableHead>Status</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {pageItems.map((r) => (
                <TableRow key={r.controlId}>
                  <TableCell className="font-mono text-xs">
                    {r.controlId}
                  </TableCell>
                  <TableCell className="max-w-md truncate">{r.name}</TableCell>
                  <TableCell className="text-muted-foreground">
                    {r.categoryName}
                  </TableCell>
                  <TableCell>
                    <Badge
                      variant="secondary"
                      className={severityColor(r.severity)}
                    >
                      {r.severity}
                    </Badge>
                  </TableCell>
                  <TableCell>
                    <Badge
                      variant="secondary"
                      className={statusColor(r.status)}
                    >
                      {r.status}
                    </Badge>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>

          {/* Pagination */}
          <div className="flex items-center justify-between pt-2">
            <p className="text-sm text-muted-foreground">
              Page {page} of {totalPages} ({filtered.length} controls)
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

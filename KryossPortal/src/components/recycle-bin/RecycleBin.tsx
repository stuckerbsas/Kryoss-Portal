import { useState } from 'react';
import { Building2, Monitor, KeyRound, Trash2, RotateCcw } from 'lucide-react';
import { toast } from 'sonner';
import { useRecycleBin, useRestoreItem } from '@/api/recycle-bin';
import { Can } from '@/components/auth/Can';
import { EmptyState } from '@/components/shared/EmptyState';
import { LoadingSkeleton } from '@/components/shared/LoadingSkeleton';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
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
import type { RecycleBinItem } from '@/types';

const ENTITY_FILTERS = [
  { value: 'all', label: 'All' },
  { value: 'Organization', label: 'Organizations' },
  { value: 'Machine', label: 'Machines' },
  { value: 'EnrollmentCode', label: 'Enrollment Codes' },
] as const;

const ENTITY_ICONS: Record<string, typeof Building2> = {
  Organization: Building2,
  Machine: Monitor,
  EnrollmentCode: KeyRound,
};

function formatRelativeTime(dateStr: string): string {
  const now = Date.now();
  const then = new Date(dateStr).getTime();
  const diffMs = now - then;
  const diffMin = Math.floor(diffMs / 60_000);
  if (diffMin < 1) return 'just now';
  if (diffMin < 60) return `${diffMin}m ago`;
  const diffHr = Math.floor(diffMin / 60);
  if (diffHr < 24) return `${diffHr}h ago`;
  const diffDay = Math.floor(diffHr / 24);
  if (diffDay < 30) return `${diffDay}d ago`;
  return new Date(dateStr).toLocaleDateString();
}

function RestoreConfirmMessage({ item }: { item: RecycleBinItem }) {
  if (item.entityType === 'Organization') {
    return (
      <>
        This will restore <strong>{item.name}</strong> and all its data
        (machines, enrollment codes). Continue?
      </>
    );
  }
  return (
    <>
      Restore <strong>{item.name}</strong>?
    </>
  );
}

export function RecycleBin() {
  const [filter, setFilter] = useState('all');
  const [restoreTarget, setRestoreTarget] = useState<RecycleBinItem | null>(null);

  const queryType = filter === 'all' ? undefined : filter;
  const { data, isLoading, isError } = useRecycleBin(queryType);
  const restoreMutation = useRestoreItem();

  const items = data?.items ?? [];

  function handleRestore() {
    if (!restoreTarget) return;
    restoreMutation.mutate(
      { entityType: restoreTarget.entityType, id: restoreTarget.id },
      {
        onSuccess: () => {
          toast.success('Restored successfully');
          setRestoreTarget(null);
        },
        onError: (err) => {
          toast.error(err instanceof Error ? err.message : 'Restore failed');
        },
      },
    );
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <h1 className="text-2xl font-bold tracking-tight">Recycle Bin</h1>
        <Select value={filter} onValueChange={setFilter}>
          <SelectTrigger className="w-48">
            <SelectValue placeholder="Filter by type" />
          </SelectTrigger>
          <SelectContent>
            {ENTITY_FILTERS.map((f) => (
              <SelectItem key={f.value} value={f.value}>
                {f.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      {isLoading ? (
        <LoadingSkeleton rows={5} columns={6} />
      ) : isError ? (
        <EmptyState
          title="Failed to load recycle bin"
          description="Something went wrong. Try refreshing the page."
        />
      ) : items.length === 0 ? (
        <EmptyState
          icon={<Trash2 className="h-12 w-12" />}
          title="Recycle Bin is empty"
          description="Deleted items will appear here."
        />
      ) : (
        <>
        {/* Mobile cards */}
        <div className="space-y-3 sm:hidden">
          {items.map((item) => {
            const Icon = ENTITY_ICONS[item.entityType] ?? Trash2;
            return (
              <div key={`${item.entityType}-${item.id}`} className="rounded-lg border p-4 space-y-2">
                <div className="flex items-center justify-between gap-2">
                  <div className="flex items-center gap-2 min-w-0">
                    <Icon className="h-4 w-4 text-muted-foreground shrink-0" />
                    <span className="font-medium text-sm truncate">{item.name}</span>
                  </div>
                  <Can permission="recycle_bin:restore">
                    <Button
                      size="sm"
                      variant="outline"
                      disabled={!item.canRestore}
                      onClick={() => setRestoreTarget(item)}
                    >
                      <RotateCcw className="h-4 w-4 mr-1" />
                      Restore
                    </Button>
                  </Can>
                </div>
                <div className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted-foreground">
                  <span>{item.entityType}</span>
                  <span>{formatRelativeTime(item.deletedAt)}</span>
                  {item.deletedByEmail && <span>{item.deletedByEmail}</span>}
                </div>
              </div>
            );
          })}
        </div>

        {/* Desktop table */}
        <div className="hidden sm:block overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Type</TableHead>
                <TableHead>Name</TableHead>
                <TableHead className="hidden md:table-cell">Details</TableHead>
                <TableHead>Deleted</TableHead>
                <TableHead className="hidden lg:table-cell">Deleted By</TableHead>
                <TableHead className="text-right">Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {items.map((item) => {
                const Icon = ENTITY_ICONS[item.entityType] ?? Trash2;
                return (
                  <TableRow key={`${item.entityType}-${item.id}`}>
                    <TableCell>
                      <div className="flex items-center gap-2">
                        <Icon className="h-4 w-4 text-muted-foreground" />
                        <span>{item.entityType}</span>
                      </div>
                    </TableCell>
                    <TableCell className="font-medium">{item.name}</TableCell>
                    <TableCell className="hidden md:table-cell text-muted-foreground">
                      {item.description}
                    </TableCell>
                    <TableCell>{formatRelativeTime(item.deletedAt)}</TableCell>
                    <TableCell className="hidden lg:table-cell text-muted-foreground">
                      {item.deletedByEmail ?? '-'}
                    </TableCell>
                    <TableCell className="text-right">
                      <Can permission="recycle_bin:restore">
                        <Button
                          size="sm"
                          variant="outline"
                          disabled={!item.canRestore}
                          title={
                            item.canRestore
                              ? 'Restore this item'
                              : 'Restore parent organization first'
                          }
                          onClick={() => setRestoreTarget(item)}
                        >
                          <RotateCcw className="h-4 w-4 mr-1" />
                          Restore
                        </Button>
                      </Can>
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </div>
        </>
      )}

      {/* Restore confirmation dialog */}
      <Dialog
        open={restoreTarget !== null}
        onOpenChange={(open) => {
          if (!open) setRestoreTarget(null);
        }}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Confirm Restore</DialogTitle>
            <DialogDescription>
              {restoreTarget && <RestoreConfirmMessage item={restoreTarget} />}
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setRestoreTarget(null)}>
              Cancel
            </Button>
            <Button
              onClick={handleRestore}
              disabled={restoreMutation.isPending}
            >
              {restoreMutation.isPending ? 'Restoring...' : 'Restore'}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

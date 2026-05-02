import { useState } from 'react';
import { toast } from 'sonner';
import { Monitor, Search, User, KeyRound, Ban, Unlock, Trash2 } from 'lucide-react';
import { useAdObjects, type AdObjectItem } from '@/api/adObjects';
import { useAdUserAction } from '@/api/machines';
import { useOrgParam } from '@/hooks/useOrgParam';
import { EmptyState } from '@/components/shared/EmptyState';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';
import { ConfirmActionDialog } from '@/components/ui/confirm-action-dialog';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';

function formatDate(iso: string | null) {
  if (!iso) return '—';
  return new Date(iso).toLocaleDateString(undefined, {
    year: 'numeric', month: 'short', day: 'numeric',
  });
}

function timeAgo(iso: string | null) {
  if (!iso) return 'Never';
  const diff = Date.now() - new Date(iso).getTime();
  const days = Math.floor(diff / 86400000);
  if (days === 0) return 'Today';
  if (days === 1) return '1d ago';
  return `${days}d ago`;
}

function memberOfCount(json: string | null): number {
  if (!json) return 0;
  try { return JSON.parse(json).length; }
  catch { return 0; }
}

export function AdObjectsTab() {
  const { orgId } = useOrgParam();
  const [type, setType] = useState<'user' | 'computer'>('user');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const pageSize = 50;
  const [actionTarget, setActionTarget] = useState<{ obj: AdObjectItem; action: string } | null>(null);
  const adAction = useAdUserAction();

  const { data, isLoading } = useAdObjects(orgId, { type, search, page, pageSize });

  const totalPages = data ? Math.ceil(data.total / pageSize) : 0;

  return (
    <div className="space-y-4">
      <div className="flex flex-col sm:flex-row items-stretch sm:items-center justify-between gap-3 sm:gap-4">
        <div className="flex gap-2">
          <Button
            variant={type === 'user' ? 'default' : 'outline'}
            size="sm"
            onClick={() => { setType('user'); setPage(1); }}
          >
            <User className="mr-1 h-4 w-4" />
            Users
            {type === 'user' && data ? ` (${data.total})` : ''}
          </Button>
          <Button
            variant={type === 'computer' ? 'default' : 'outline'}
            size="sm"
            onClick={() => { setType('computer'); setPage(1); }}
          >
            <Monitor className="mr-1 h-4 w-4" />
            Computers
            {type === 'computer' && data ? ` (${data.total})` : ''}
          </Button>
        </div>

        <div className="relative w-full sm:w-72">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            placeholder="Search name, DN..."
            value={search}
            onChange={(e) => { setSearch(e.target.value); setPage(1); }}
            className="pl-9"
          />
        </div>
      </div>

      {isLoading ? (
        <div className="space-y-2">
          {Array.from({ length: 10 }, (_, i) => <Skeleton key={i} className="h-10 w-full" />)}
        </div>
      ) : !data || data.items.length === 0 ? (
        <EmptyState
          icon={type === 'user' ? <User className="h-12 w-12" /> : <Monitor className="h-12 w-12" />}
          title={`No ${type === 'user' ? 'Users' : 'Computers'} Found`}
          description={search ? 'Try a different search term.' : 'AD object data will appear after a Domain Controller runs a compliance scan.'}
        />
      ) : (
        <>
          {/* Mobile cards */}
          <div className="space-y-3 sm:hidden">
            {data.items.map((obj: AdObjectItem) => (
              <div key={obj.id} className="rounded-lg border p-4">
                <div className="flex items-center justify-between">
                  <span className="font-medium text-sm font-mono truncate">{obj.samAccountName}</span>
                  {obj.enabled
                    ? <Badge className="bg-green-100 text-green-800">Enabled</Badge>
                    : <Badge className="bg-red-100 text-red-800">Disabled</Badge>
                  }
                </div>
                <div className="flex items-center gap-3 mt-2 text-xs text-muted-foreground">
                  {obj.displayName && <span className="truncate">{obj.displayName}</span>}
                  <span>{timeAgo(obj.lastLogon)}</span>
                  <span>{memberOfCount(obj.memberOf)} groups</span>
                </div>
                {type === 'user' && (
                  <div className="mt-2 flex justify-end">
                    <DropdownMenu>
                      <DropdownMenuTrigger asChild>
                        <Button variant="ghost" size="sm" className="h-7 px-2 text-xs">Actions</Button>
                      </DropdownMenuTrigger>
                      <DropdownMenuContent align="end">
                        <DropdownMenuItem onClick={() => setActionTarget({ obj, action: 'reset_password' })}>
                          <KeyRound className="mr-2 h-3.5 w-3.5" /> Reset Password
                        </DropdownMenuItem>
                        {obj.enabled && (
                          <DropdownMenuItem onClick={() => setActionTarget({ obj, action: 'disable' })}>
                            <Ban className="mr-2 h-3.5 w-3.5" /> Disable
                          </DropdownMenuItem>
                        )}
                        <DropdownMenuItem onClick={() => setActionTarget({ obj, action: 'unlock' })}>
                          <Unlock className="mr-2 h-3.5 w-3.5" /> Unlock
                        </DropdownMenuItem>
                        <DropdownMenuItem className="text-destructive" onClick={() => setActionTarget({ obj, action: 'delete' })}>
                          <Trash2 className="mr-2 h-3.5 w-3.5" /> Delete
                        </DropdownMenuItem>
                      </DropdownMenuContent>
                    </DropdownMenu>
                  </div>
                )}
              </div>
            ))}
          </div>

          {/* Desktop table */}
          <div className="hidden sm:block rounded-md border">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Name</TableHead>
                  <TableHead className="hidden lg:table-cell">Display Name</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Last Logon</TableHead>
                  <TableHead className="hidden lg:table-cell">Created</TableHead>
                  <TableHead className="hidden lg:table-cell">OU</TableHead>
                  <TableHead>Groups</TableHead>
                  {type === 'user' && <TableHead className="w-20" />}
                </TableRow>
              </TableHeader>
              <TableBody>
                {data.items.map((obj: AdObjectItem) => (
                  <TableRow key={obj.id}>
                    <TableCell className="font-mono text-sm font-medium">{obj.samAccountName}</TableCell>
                    <TableCell className="hidden lg:table-cell text-sm">{obj.displayName ?? '—'}</TableCell>
                    <TableCell>
                      {obj.enabled
                        ? <Badge className="bg-green-100 text-green-800">Enabled</Badge>
                        : <Badge className="bg-red-100 text-red-800">Disabled</Badge>
                      }
                    </TableCell>
                    <TableCell className="text-sm text-muted-foreground" title={obj.lastLogon ?? undefined}>
                      {timeAgo(obj.lastLogon)}
                    </TableCell>
                    <TableCell className="hidden lg:table-cell text-sm text-muted-foreground">{formatDate(obj.whenCreated)}</TableCell>
                    <TableCell className="hidden lg:table-cell max-w-[200px] truncate text-xs text-muted-foreground" title={obj.organizationalUnit ?? undefined}>
                      {obj.organizationalUnit ?? '—'}
                    </TableCell>
                    <TableCell className="tabular-nums text-sm">{memberOfCount(obj.memberOf)}</TableCell>
                    {type === 'user' && (
                      <TableCell>
                        <DropdownMenu>
                          <DropdownMenuTrigger asChild>
                            <Button variant="ghost" size="sm" className="h-7 px-2 text-xs">Actions</Button>
                          </DropdownMenuTrigger>
                          <DropdownMenuContent align="end">
                            <DropdownMenuItem onClick={() => setActionTarget({ obj, action: 'reset_password' })}>
                              <KeyRound className="mr-2 h-3.5 w-3.5" /> Reset Password
                            </DropdownMenuItem>
                            {obj.enabled && (
                              <DropdownMenuItem onClick={() => setActionTarget({ obj, action: 'disable' })}>
                                <Ban className="mr-2 h-3.5 w-3.5" /> Disable
                              </DropdownMenuItem>
                            )}
                            <DropdownMenuItem onClick={() => setActionTarget({ obj, action: 'unlock' })}>
                              <Unlock className="mr-2 h-3.5 w-3.5" /> Unlock
                            </DropdownMenuItem>
                            <DropdownMenuItem className="text-destructive" onClick={() => setActionTarget({ obj, action: 'delete' })}>
                              <Trash2 className="mr-2 h-3.5 w-3.5" /> Delete
                            </DropdownMenuItem>
                          </DropdownMenuContent>
                        </DropdownMenu>
                      </TableCell>
                    )}
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>

          <div className="flex items-center justify-between">
            <span className="text-sm text-muted-foreground">
              Showing {(page - 1) * pageSize + 1}–{Math.min(page * pageSize, data.total)} of {data.total}
            </span>
            <div className="flex gap-2">
              <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
                Previous
              </Button>
              <Button variant="outline" size="sm" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)}>
                Next
              </Button>
            </div>
          </div>
        </>
      )}

      <ConfirmActionDialog
        open={!!actionTarget}
        onClose={() => setActionTarget(null)}
        onConfirm={() => {
          if (!actionTarget || !orgId) return;
          adAction.mutate(
            {
              organizationId: orgId,
              machineId: '00000000-0000-0000-0000-000000000000',
              accountName: actionTarget.obj.samAccountName,
              distinguishedName: actionTarget.obj.distinguishedName ?? '',
              action: actionTarget.action,
            },
            {
              onSuccess: () => {
                toast.success(`${actionTarget.action.replace('_', ' ')} task queued for ${actionTarget.obj.samAccountName}`);
                setActionTarget(null);
              },
              onError: () => toast.error('Failed to queue AD action'),
            },
          );
        }}
        title={actionTarget ? `${actionTarget.action === 'reset_password' ? 'Reset password for' : actionTarget.action === 'disable' ? 'Disable' : actionTarget.action === 'unlock' ? 'Unlock' : 'Delete'} ${actionTarget.obj.samAccountName}?` : ''}
        description={actionTarget?.action === 'delete'
          ? 'This will permanently delete the AD user account. This action cannot be undone.'
          : `This will ${actionTarget?.action?.replace('_', ' ')} the AD user account on the Domain Controller.`}
        destructive={actionTarget?.action === 'delete'}
        confirmLabel={actionTarget?.action === 'delete' ? 'Delete User' : 'Confirm'}
        requireReason={actionTarget?.action === 'delete'}
      />
    </div>
  );
}

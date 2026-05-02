import { useState } from 'react';
import { toast } from 'sonner';
import { ShieldAlert, Monitor, Search, ChevronDown, ChevronRight, Users, UserMinus, Ban, AlertTriangle } from 'lucide-react';
import { useOrgLocalAdmins, useLocalAdminAction } from '@/api/machines';
import type { LocalAdminEntry } from '@/api/machines';
import { useOrgParam } from '@/hooks/useOrgParam';
import { EmptyState } from '@/components/shared/EmptyState';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';
import { ConfirmActionDialog } from '@/components/ui/confirm-action-dialog';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { useNavigate } from 'react-router-dom';

export function LocalAdminsTab() {
  const { orgId } = useOrgParam();
  const { data, isLoading } = useOrgLocalAdmins(orgId);
  const [search, setSearch] = useState('');
  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const navigate = useNavigate();
  const [actionTarget, setActionTarget] = useState<{ machineId: string; accountName: string; action: 'remove' | 'disable' } | null>(null);
  const adminAction = useLocalAdminAction();

  if (isLoading) return <Skeleton className="h-64 w-full" />;
  if (!data || data.admins.length === 0) {
    return <EmptyState icon={<Users className="h-10 w-10" />} title="No local admin data" description="Waiting for agent compliance scans to report local administrator group members." />;
  }

  const filtered = data.admins.filter(a =>
    a.name.toLowerCase().includes(search.toLowerCase())
  );

  const domainAdmins = filtered.filter(a => a.source === 'Domain');
  const localAccounts = filtered.filter(a => a.source !== 'Domain');

  const toggle = (name: string) => {
    setExpanded(prev => {
      const next = new Set(prev);
      if (next.has(name)) next.delete(name);
      else next.add(name);
      return next;
    });
  };

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
        <Card>
          <CardHeader className="flex flex-row items-start justify-between pb-1 pt-0 h-12">
            <CardTitle className="text-sm font-medium text-muted-foreground">Unique Accounts</CardTitle>
            <Users className="h-4 w-4 text-blue-500" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold">{data.totalAccounts}</div>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-start justify-between pb-1 pt-0 h-12">
            <CardTitle className="text-sm font-medium text-muted-foreground">Total Memberships</CardTitle>
            <ShieldAlert className="h-4 w-4 text-orange-500" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold">{data.totalEntries}</div>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-start justify-between pb-1 pt-0 h-12">
            <CardTitle className="text-sm font-medium text-muted-foreground">Domain Accounts</CardTitle>
            <ShieldAlert className="h-4 w-4 text-purple-500" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold" style={{ color: domainAdmins.length > 5 ? '#D97706' : '#006536' }}>
              {data.admins.filter(a => a.source === 'Domain').length}
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-start justify-between pb-1 pt-0 h-12">
            <CardTitle className="text-sm font-medium text-muted-foreground">Local Accounts</CardTitle>
            <Monitor className="h-4 w-4 text-blue-500" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold">
              {data.admins.filter(a => a.source !== 'Domain').length}
            </div>
          </CardContent>
        </Card>
      </div>

      <div className="relative">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
        <Input
          placeholder="Search account name..."
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          className="pl-9"
        />
      </div>

      <AdminTable
        title="Domain Accounts in Local Administrators"
        admins={domainAdmins}
        expanded={expanded}
        onToggle={toggle}
        onNavigate={(id) => navigate(`/machines/${id}`)}
        onAction={(machineId, accountName, action) => setActionTarget({ machineId, accountName, action })}
        badgeClass="bg-purple-100 text-purple-800 dark:bg-purple-900/30 dark:text-purple-300"
        showActions
      />

      <AdminTable
        title="Local Accounts in Administrators Group"
        admins={localAccounts}
        expanded={expanded}
        onToggle={toggle}
        onNavigate={(id) => navigate(`/machines/${id}`)}
        onAction={(machineId, accountName, action) => setActionTarget({ machineId, accountName, action })}
        badgeClass="bg-blue-100 text-blue-800 dark:bg-blue-900/30 dark:text-blue-300"
        showActions
      />

      <ConfirmActionDialog
        open={!!actionTarget}
        onClose={() => setActionTarget(null)}
        onConfirm={() => {
          if (!actionTarget) return;
          adminAction.mutate(
            { machineId: actionTarget.machineId, accountName: actionTarget.accountName, action: actionTarget.action },
            {
              onSuccess: () => { toast.success(`${actionTarget.action === 'remove' ? 'Remove admin' : 'Disable account'} task queued for ${actionTarget.accountName}`); setActionTarget(null); },
              onError: () => toast.error('Failed to queue action'),
            },
          );
        }}
        title={actionTarget?.action === 'remove' ? `Remove ${actionTarget?.accountName} from Administrators?` : `Disable account ${actionTarget?.accountName}?`}
        description={actionTarget?.action === 'remove'
          ? 'This will remove the account from the local Administrators group on the target machine.'
          : 'This will disable the local account on the target machine. The account will not be deleted.'}
        destructive
        confirmLabel={actionTarget?.action === 'remove' ? 'Remove from Admins' : 'Disable Account'}
      />
    </div>
  );
}

function AdminTable({
  title,
  admins,
  expanded,
  onToggle,
  onNavigate,
  onAction,
  badgeClass,
  showActions,
}: {
  title: string;
  admins: LocalAdminEntry[];
  expanded: Set<string>
  onToggle: (name: string) => void;
  onNavigate: (machineId: string) => void;
  onAction: (machineId: string, accountName: string, action: 'remove' | 'disable') => void;
  badgeClass: string;
  showActions: boolean;
}) {
  if (admins.length === 0) return null;

  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-sm font-medium">{title}</CardTitle>
      </CardHeader>
      <CardContent className="p-0">
        {/* Mobile cards */}
        <div className="space-y-3 sm:hidden p-4">
          {admins.map((admin) => (
            <div key={admin.name}>
              <div
                className="rounded-lg border p-4 cursor-pointer"
                onClick={() => onToggle(admin.name)}
              >
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-2 min-w-0">
                    {expanded.has(admin.name)
                      ? <ChevronDown className="h-4 w-4 text-muted-foreground shrink-0" />
                      : <ChevronRight className="h-4 w-4 text-muted-foreground shrink-0" />}
                    <span className="font-medium text-sm truncate">{admin.name}</span>
                  </div>
                  <Badge variant={admin.machineCount > 5 ? 'destructive' : 'secondary'}>
                    {admin.machineCount}
                  </Badge>
                </div>
                <div className="flex items-center gap-2 mt-2 text-xs text-muted-foreground ml-6">
                  <Badge className={badgeClass}>{admin.type}</Badge>
                  {admin.machines.some(m => m.passwordNeverExpires) && (
                    <Badge variant="outline" className="text-xs text-amber-600 border-amber-300">
                      <AlertTriangle className="h-3 w-3 mr-1" />Pwd Never Expires
                    </Badge>
                  )}
                </div>
              </div>
              {expanded.has(admin.name) && admin.machines.map((m) => (
                <div key={`${admin.name}-${m.machineId}`} className="rounded-lg border p-3 ml-4 mt-2 bg-muted/30">
                  <div className="flex items-center justify-between">
                    <div className="flex items-center gap-2 cursor-pointer min-w-0" onClick={() => onNavigate(m.machineId)}>
                      <Monitor className="h-3.5 w-3.5 shrink-0 text-muted-foreground" />
                      <span className="text-sm truncate">{m.hostname}</span>
                    </div>
                    {m.isEnabled === false
                      ? <Badge variant="outline" className="text-xs text-red-600">Disabled</Badge>
                      : m.isEnabled === true
                      ? <Badge variant="outline" className="text-xs text-green-600">Active</Badge>
                      : null}
                  </div>
                  <div className="flex items-center justify-between mt-2">
                    <span className="text-xs text-muted-foreground">{m.lastLogon ? new Date(m.lastLogon).toLocaleDateString() : '—'}</span>
                    {showActions && (
                      <div className="flex items-center gap-1">
                        <Button variant="ghost" size="sm" className="h-6 px-1.5 text-xs" onClick={(e) => { e.stopPropagation(); onAction(m.machineId, admin.name, 'remove'); }}>
                          <UserMinus className="h-3 w-3 mr-1" /> Remove
                        </Button>
                        <Button variant="ghost" size="sm" className="h-6 px-1.5 text-xs text-destructive" onClick={(e) => { e.stopPropagation(); onAction(m.machineId, admin.name, 'disable'); }}>
                          <Ban className="h-3 w-3 mr-1" /> Disable
                        </Button>
                      </div>
                    )}
                  </div>
                </div>
              ))}
            </div>
          ))}
        </div>

        {/* Desktop table */}
        <div className="hidden sm:block">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead className="w-8" />
                <TableHead>Account</TableHead>
                <TableHead>Type</TableHead>
                <TableHead className="hidden lg:table-cell">Status</TableHead>
                <TableHead className="text-right">Machines</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {admins.map((admin) => (
                <>
                  <TableRow
                    key={admin.name}
                    className="cursor-pointer hover:bg-muted/50"
                    onClick={() => onToggle(admin.name)}
                  >
                    <TableCell className="w-8 px-2">
                      {expanded.has(admin.name)
                        ? <ChevronDown className="h-4 w-4 text-muted-foreground" />
                        : <ChevronRight className="h-4 w-4 text-muted-foreground" />}
                    </TableCell>
                    <TableCell className="font-medium">{admin.name}</TableCell>
                    <TableCell>
                      <Badge className={badgeClass}>{admin.type}</Badge>
                    </TableCell>
                    <TableCell className="hidden lg:table-cell">
                      {admin.machines.some(m => m.passwordNeverExpires) && (
                        <Badge variant="outline" className="text-xs text-amber-600 border-amber-300">
                          <AlertTriangle className="h-3 w-3 mr-1" />Pwd Never Expires
                        </Badge>
                      )}
                    </TableCell>
                    <TableCell className="text-right">
                      <Badge variant={admin.machineCount > 5 ? 'destructive' : 'secondary'}>
                        {admin.machineCount}
                      </Badge>
                    </TableCell>
                  </TableRow>
                  {expanded.has(admin.name) && admin.machines.map((m) => (
                    <TableRow
                      key={`${admin.name}-${m.machineId}`}
                      className="bg-muted/30 hover:bg-muted/60"
                    >
                      <TableCell />
                      <TableCell className="text-sm text-muted-foreground pl-8 cursor-pointer" onClick={() => onNavigate(m.machineId)}>
                        <div className="flex items-center gap-2">
                          <Monitor className="h-3.5 w-3.5" />
                          {m.hostname}
                        </div>
                      </TableCell>
                      <TableCell>
                        {m.isEnabled === false
                          ? <Badge variant="outline" className="text-xs text-red-600">Disabled</Badge>
                          : m.isEnabled === true
                          ? <Badge variant="outline" className="text-xs text-green-600">Active</Badge>
                          : <span className="text-xs text-muted-foreground">—</span>}
                      </TableCell>
                      <TableCell className="hidden lg:table-cell text-sm text-muted-foreground">
                        {m.lastLogon ? new Date(m.lastLogon).toLocaleDateString() : '—'}
                      </TableCell>
                      <TableCell className="text-right">
                        {showActions && (
                          <div className="flex items-center justify-end gap-1">
                            <Button variant="ghost" size="sm" className="h-6 px-1.5 text-xs" onClick={(e) => { e.stopPropagation(); onAction(m.machineId, admin.name, 'remove'); }}>
                              <UserMinus className="h-3 w-3 mr-1" /> Remove Admin
                            </Button>
                            <Button variant="ghost" size="sm" className="h-6 px-1.5 text-xs text-destructive" onClick={(e) => { e.stopPropagation(); onAction(m.machineId, admin.name, 'disable'); }}>
                              <Ban className="h-3 w-3 mr-1" /> Disable
                            </Button>
                          </div>
                        )}
                      </TableCell>
                    </TableRow>
                  ))}
                </>
              ))}
            </TableBody>
          </Table>
        </div>
      </CardContent>
    </Card>
  );
}

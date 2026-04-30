import { useState } from 'react';
import { ShieldAlert, Monitor, Search, ChevronDown, ChevronRight, Users } from 'lucide-react';
import { useOrgLocalAdmins } from '@/api/machines';
import type { LocalAdminEntry } from '@/api/machines';
import { useOrgParam } from '@/hooks/useOrgParam';
import { EmptyState } from '@/components/shared/EmptyState';
import { Badge } from '@/components/ui/badge';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';
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
        badgeClass="bg-purple-100 text-purple-800 dark:bg-purple-900/30 dark:text-purple-300"
      />

      <AdminTable
        title="Local Accounts in Administrators Group"
        admins={localAccounts}
        expanded={expanded}
        onToggle={toggle}
        onNavigate={(id) => navigate(`/machines/${id}`)}
        badgeClass="bg-blue-100 text-blue-800 dark:bg-blue-900/30 dark:text-blue-300"
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
  badgeClass,
}: {
  title: string;
  admins: LocalAdminEntry[];
  expanded: Set<string>;
  onToggle: (name: string) => void;
  onNavigate: (machineId: string) => void;
  badgeClass: string;
}) {
  if (admins.length === 0) return null;

  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-sm font-medium">{title}</CardTitle>
      </CardHeader>
      <CardContent className="p-0">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="w-8" />
              <TableHead>Account</TableHead>
              <TableHead>Type</TableHead>
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
                  <TableCell className="text-right">
                    <Badge variant={admin.machineCount > 5 ? 'destructive' : 'secondary'}>
                      {admin.machineCount}
                    </Badge>
                  </TableCell>
                </TableRow>
                {expanded.has(admin.name) && admin.machines.map((m) => (
                  <TableRow
                    key={`${admin.name}-${m.machineId}`}
                    className="bg-muted/30 cursor-pointer hover:bg-muted/60"
                    onClick={() => onNavigate(m.machineId)}
                  >
                    <TableCell />
                    <TableCell colSpan={2} className="text-sm text-muted-foreground pl-8">
                      <div className="flex items-center gap-2">
                        <Monitor className="h-3.5 w-3.5" />
                        {m.hostname}
                      </div>
                    </TableCell>
                    <TableCell />
                  </TableRow>
                ))}
              </>
            ))}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  );
}

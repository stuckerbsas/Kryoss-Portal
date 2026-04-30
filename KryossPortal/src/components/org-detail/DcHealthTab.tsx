import {
  AlertTriangle,
  CheckCircle,
  Crown,
  Globe,
  Network,
  Server,
  Shield,
} from 'lucide-react';
import { useDcHealth } from '@/api/dcHealth';
import type { DcReplPartner } from '@/api/dcHealth';
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

function timeAgo(iso: string | null) {
  if (!iso) return 'Never';
  const diff = Date.now() - new Date(iso).getTime();
  const mins = Math.floor(diff / 60000);
  if (mins < 60) return `${mins}m ago`;
  const hours = Math.floor(mins / 60);
  if (hours < 24) return `${hours}h ago`;
  return `${Math.floor(hours / 24)}d ago`;
}

function replStatusBadge(partner: DcReplPartner) {
  if (partner.failureCount > 0)
    return <Badge className="bg-red-100 text-red-800">Failing ({partner.failureCount})</Badge>;
  if (partner.lastSuccess)
    return <Badge className="bg-green-100 text-green-800">Healthy</Badge>;
  return <Badge className="bg-gray-100 text-gray-500">Unknown</Badge>;
}

const SCHEMA_VERSION_MAP: Record<number, string> = {
  30: 'Windows Server 2003',
  31: 'Windows Server 2003 R2',
  44: 'Windows Server 2008',
  47: 'Windows Server 2008 R2',
  56: 'Windows Server 2012',
  69: 'Windows Server 2012 R2',
  87: 'Windows Server 2016',
  88: 'Windows Server 2019',
  89: 'Windows Server 2022',
  90: 'Windows Server 2025',
};

function schemaLabel(version: number | null): string {
  if (version === null) return 'Unknown';
  return SCHEMA_VERSION_MAP[version] ?? `Schema ${version}`;
}

export function DcHealthTab() {
  const { orgId } = useOrgParam();
  const { data, isLoading } = useDcHealth(orgId);

  if (isLoading) return <div className="space-y-4 p-4">{Array.from({ length: 4 }, (_, i) => <Skeleton key={i} className="h-24 w-full" />)}</div>;
  if (!data?.latest)
    return <EmptyState icon={<Server className="h-12 w-12" />} title="No DC Health Data" description="DC health data will appear after a Domain Controller runs a compliance scan." />;

  const s = data.latest;

  return (
    <div className="space-y-6 p-4">
      {/* KPI row */}
      <div className="grid grid-cols-2 gap-4 md:grid-cols-4 lg:grid-cols-6">
        <Card>
          <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
            <CardTitle className="text-sm font-medium">Schema</CardTitle>
            <Globe className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold">{schemaLabel(s.schemaVersion)}</div>
            <p className="text-xs text-muted-foreground">Schema v{s.schemaVersion ?? '?'}</p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
            <CardTitle className="text-sm font-medium">Domain Controllers</CardTitle>
            <Server className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold">{s.dcCount}</div>
            <p className="text-xs text-muted-foreground">{s.gcCount} Global Catalogs</p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
            <CardTitle className="text-sm font-medium">Sites</CardTitle>
            <Network className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold">{s.siteCount}</div>
            <p className="text-xs text-muted-foreground">{s.subnetCount} subnets</p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
            <CardTitle className="text-sm font-medium">Replication</CardTitle>
            {s.replFailureCount > 0
              ? <AlertTriangle className="h-4 w-4 text-red-500" />
              : <CheckCircle className="h-4 w-4 text-green-500" />}
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold">{s.replPartnerCount} partners</div>
            <p className="text-xs text-muted-foreground">
              {s.replFailureCount > 0
                ? <span className="text-red-600">{s.replFailureCount} failing</span>
                : 'All healthy'}
            </p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
            <CardTitle className="text-sm font-medium">Forest Level</CardTitle>
            <Shield className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <div className="text-lg font-bold truncate">{s.forestLevel ?? '?'}</div>
            <p className="text-xs text-muted-foreground">Domain: {s.domainLevel ?? '?'}</p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
            <CardTitle className="text-sm font-medium">FSMO</CardTitle>
            <Crown className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <div className="text-lg font-bold">
              {s.fsmoSinglePoint
                ? <span className="text-amber-600">Single Point</span>
                : <span className="text-green-600">Distributed</span>}
            </div>
            <p className="text-xs text-muted-foreground">5 roles</p>
          </CardContent>
        </Card>
      </div>

      {/* Domain info */}
      <Card>
        <CardHeader>
          <CardTitle>Domain Information</CardTitle>
        </CardHeader>
        <CardContent>
          <div className="grid grid-cols-2 gap-4 md:grid-cols-3">
            <div><span className="text-sm text-muted-foreground">Domain</span><p className="font-medium">{s.domainName ?? '—'}</p></div>
            <div><span className="text-sm text-muted-foreground">Forest</span><p className="font-medium">{s.forestName ?? '—'}</p></div>
            <div><span className="text-sm text-muted-foreground">Scanned By</span><p className="font-medium">{s.scannedBy ?? '—'}</p></div>
            <div><span className="text-sm text-muted-foreground">Last Scan</span><p className="font-medium">{timeAgo(s.scannedAt)}</p></div>
            <div><span className="text-sm text-muted-foreground">Last Successful Repl</span><p className="font-medium">{timeAgo(s.lastSuccessfulRepl)}</p></div>
          </div>
        </CardContent>
      </Card>

      {/* FSMO Roles */}
      <Card>
        <CardHeader>
          <CardTitle>FSMO Role Holders</CardTitle>
        </CardHeader>
        <CardContent>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Role</TableHead>
                <TableHead>Holder</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {[
                ['Schema Master', s.schemaMaster],
                ['Domain Naming Master', s.domainNamingMaster],
                ['PDC Emulator', s.pdcEmulator],
                ['RID Master', s.ridMaster],
                ['Infrastructure Master', s.infrastructureMaster],
              ].map(([role, holder]) => (
                <TableRow key={role as string}>
                  <TableCell className="font-medium">{role}</TableCell>
                  <TableCell>{(holder as string) ?? <span className="text-muted-foreground">Unknown</span>}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          {s.fsmoSinglePoint && (
            <div className="mt-3 flex items-center gap-2 rounded-md bg-amber-50 p-3 text-sm text-amber-800">
              <AlertTriangle className="h-4 w-4" />
              All FSMO roles are held by a single domain controller — consider distributing for resilience.
            </div>
          )}
        </CardContent>
      </Card>

      {/* Replication Partners */}
      {s.replicationPartners.length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle>Replication Partners ({s.replicationPartners.length})</CardTitle>
          </CardHeader>
          <CardContent>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Partner</TableHead>
                  <TableHead>Direction</TableHead>
                  <TableHead>Naming Context</TableHead>
                  <TableHead>Last Success</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Transport</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {s.replicationPartners.map((rp, i) => (
                  <TableRow key={i}>
                    <TableCell className="font-medium">{rp.partnerHostname ?? '—'}</TableCell>
                    <TableCell>{rp.direction ?? '—'}</TableCell>
                    <TableCell className="max-w-[200px] truncate text-xs">{rp.namingContext ?? '—'}</TableCell>
                    <TableCell>{timeAgo(rp.lastSuccess)}</TableCell>
                    <TableCell>{replStatusBadge(rp)}</TableCell>
                    <TableCell><Badge variant="outline">{rp.transport ?? 'IP'}</Badge></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      )}
    </div>
  );
}

import { useState } from 'react';
import {
  Router,
  Server,
  Settings,
  ChevronDown,
  ChevronRight,
  Wifi,
  Clock,
  MapPin,
} from 'lucide-react';
import { toast } from 'sonner';
import {
  useSnmpConfig,
  useSnmpDevices,
  useSaveSnmpConfig,
  type SnmpDevice,
  type SnmpConfigPayload,
} from '@/api/snmp';
import { useOrgParam } from '@/hooks/useOrgParam';
import { EmptyState } from '@/components/shared/EmptyState';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Skeleton } from '@/components/ui/skeleton';
import { Switch } from '@/components/ui/switch';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';

function ifStatusBadge(status: number | null) {
  if (status === 1) return <Badge variant="secondary" className="bg-green-100 text-green-800">Up</Badge>;
  if (status === 2) return <Badge variant="secondary" className="bg-red-100 text-red-800">Down</Badge>;
  return <Badge variant="secondary" className="bg-gray-100 text-gray-500">Unknown</Badge>;
}

function DeviceRow({ device }: { device: SnmpDevice }) {
  const [expanded, setExpanded] = useState(false);

  return (
    <>
      <TableRow
        className="cursor-pointer hover:bg-muted/50"
        onClick={() => setExpanded(!expanded)}
      >
        <TableCell>
          <div className="flex items-center gap-2">
            {expanded ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
            <span className="font-mono">{device.ipAddress}</span>
          </div>
        </TableCell>
        <TableCell className="font-medium">{device.sysName ?? '—'}</TableCell>
        <TableCell className="text-sm max-w-xs truncate">{device.sysDescr ?? '—'}</TableCell>
        <TableCell>
          {device.entityMfg && device.entityModel
            ? `${device.entityMfg} ${device.entityModel}`
            : device.entityModel ?? '—'}
        </TableCell>
        <TableCell className="font-mono text-xs">{device.entityFirmware ?? '—'}</TableCell>
        <TableCell className="text-center">{device.interfaceCount}</TableCell>
        <TableCell>
          {device.uptimeDays != null ? (
            <span className="font-mono tabular-nums">{Math.floor(device.uptimeDays)}d</span>
          ) : '—'}
        </TableCell>
        <TableCell className="text-xs text-muted-foreground">
          {new Date(device.scannedAt).toLocaleString()}
        </TableCell>
      </TableRow>

      {expanded && device.interfaces.length > 0 && (
        <TableRow>
          <TableCell colSpan={8} className="bg-muted/30 p-4">
            <h4 className="text-sm font-semibold mb-2">Interfaces ({device.interfaces.length})</h4>
            <div className="overflow-x-auto max-h-64 overflow-y-auto">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>#</TableHead>
                    <TableHead>Name</TableHead>
                    <TableHead>Description</TableHead>
                    <TableHead>Speed</TableHead>
                    <TableHead>MAC</TableHead>
                    <TableHead>Admin</TableHead>
                    <TableHead>Oper</TableHead>
                    <TableHead>In Errors</TableHead>
                    <TableHead>Out Errors</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {device.interfaces.map((iface) => (
                    <TableRow key={iface.ifIndex}>
                      <TableCell className="font-mono text-xs">{iface.ifIndex}</TableCell>
                      <TableCell className="text-xs">{iface.name ?? '—'}</TableCell>
                      <TableCell className="text-xs max-w-xs truncate">{iface.description ?? '—'}</TableCell>
                      <TableCell className="font-mono text-xs">
                        {iface.speedMbps != null ? `${iface.speedMbps} Mbps` : '—'}
                      </TableCell>
                      <TableCell className="font-mono text-xs">{iface.macAddress ?? '—'}</TableCell>
                      <TableCell>{ifStatusBadge(iface.adminStatus)}</TableCell>
                      <TableCell>{ifStatusBadge(iface.operStatus)}</TableCell>
                      <TableCell className="font-mono tabular-nums" style={{ color: iface.inErrors > 0 ? '#C0392B' : undefined }}>
                        {iface.inErrors}
                      </TableCell>
                      <TableCell className="font-mono tabular-nums" style={{ color: iface.outErrors > 0 ? '#C0392B' : undefined }}>
                        {iface.outErrors}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          </TableCell>
        </TableRow>
      )}
    </>
  );
}

function SnmpConfigCard({ orgId }: { orgId: string }) {
  const { data: config, isLoading } = useSnmpConfig(orgId);
  const save = useSaveSnmpConfig();
  const [editing, setEditing] = useState(false);
  const [form, setForm] = useState<Partial<SnmpConfigPayload>>({});

  if (isLoading) return <Skeleton className="h-32" />;

  const startEdit = () => {
    setForm({
      organizationId: orgId,
      version: config?.version ?? 2,
      community: config?.community === '***' ? '' : (config?.community ?? ''),
      username: config?.username ?? '',
      authProtocol: config?.authProtocol ?? '',
      privProtocol: config?.privProtocol ?? '',
      targets: config?.targets ?? [],
      enabled: config?.enabled ?? true,
    });
    setEditing(true);
  };

  const handleSave = async () => {
    try {
      await save.mutateAsync({
        organizationId: orgId,
        version: form.version ?? 2,
        community: form.community || null,
        username: form.username || null,
        authProtocol: form.authProtocol || null,
        authPassword: (form as any).authPassword || null,
        privProtocol: form.privProtocol || null,
        privPassword: (form as any).privPassword || null,
        targets: form.targets?.length ? form.targets : null,
        enabled: form.enabled ?? true,
      });
      toast.success('SNMP configuration saved');
      setEditing(false);
    } catch (err: any) {
      toast.error(`Save failed: ${err.message}`);
    }
  };

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between pb-3">
        <CardTitle className="flex items-center gap-2 text-base">
          <Settings className="h-4 w-4 text-muted-foreground" />
          SNMP Configuration
        </CardTitle>
        {!editing && (
          <Button variant="outline" size="sm" onClick={startEdit}>
            {config?.configured ? 'Edit' : 'Configure'}
          </Button>
        )}
      </CardHeader>
      <CardContent>
        {!editing ? (
          config?.configured ? (
            <div className="grid grid-cols-2 md:grid-cols-4 gap-4 text-sm">
              <div>
                <span className="text-muted-foreground">Status</span>
                <p>{config.enabled ? '✓ Enabled' : '✗ Disabled'}</p>
              </div>
              <div>
                <span className="text-muted-foreground">Version</span>
                <p>SNMPv{config.version}</p>
              </div>
              <div>
                <span className="text-muted-foreground">Auth</span>
                <p>{config.version === 3 ? (config.username ?? '—') : (config.community ? 'Community set' : '—')}</p>
              </div>
              <div>
                <span className="text-muted-foreground">Targets</span>
                <p>{config.targets?.length ?? 0} configured</p>
              </div>
            </div>
          ) : (
            <p className="text-sm text-muted-foreground">
              SNMP not configured. Click Configure to set up network device discovery.
            </p>
          )
        ) : (
          <div className="space-y-4">
            <div className="flex items-center gap-4">
              <div className="flex items-center gap-2">
                <Label>Enabled</Label>
                <Switch
                  checked={form.enabled}
                  onCheckedChange={(v) => setForm({ ...form, enabled: v })}
                />
              </div>
              <div>
                <Label>Version</Label>
                <select
                  className="ml-2 border rounded px-2 py-1 text-sm"
                  value={form.version}
                  onChange={(e) => setForm({ ...form, version: Number(e.target.value) })}
                >
                  <option value={2}>v2c</option>
                  <option value={3}>v3</option>
                </select>
              </div>
            </div>
            {form.version === 2 ? (
              <div>
                <Label>Community String</Label>
                <Input
                  type="password"
                  placeholder="public"
                  value={(form.community as string) ?? ''}
                  onChange={(e) => setForm({ ...form, community: e.target.value })}
                />
              </div>
            ) : (
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <Label>Username</Label>
                  <Input value={form.username ?? ''} onChange={(e) => setForm({ ...form, username: e.target.value })} />
                </div>
                <div>
                  <Label>Auth Protocol</Label>
                  <Input placeholder="SHA" value={form.authProtocol ?? ''} onChange={(e) => setForm({ ...form, authProtocol: e.target.value })} />
                </div>
                <div>
                  <Label>Auth Password</Label>
                  <Input type="password" onChange={(e) => setForm({ ...form, authPassword: e.target.value } as any)} />
                </div>
                <div>
                  <Label>Priv Protocol</Label>
                  <Input placeholder="AES" value={form.privProtocol ?? ''} onChange={(e) => setForm({ ...form, privProtocol: e.target.value })} />
                </div>
                <div>
                  <Label>Priv Password</Label>
                  <Input type="password" onChange={(e) => setForm({ ...form, privPassword: e.target.value } as any)} />
                </div>
              </div>
            )}
            <div>
              <Label>Targets (comma-separated IPs or CIDRs)</Label>
              <Input
                placeholder="10.0.0.1, 192.168.1.0/24"
                value={form.targets?.join(', ') ?? ''}
                onChange={(e) =>
                  setForm({
                    ...form,
                    targets: e.target.value
                      .split(',')
                      .map((s) => s.trim())
                      .filter(Boolean),
                  })
                }
              />
            </div>
            <div className="flex gap-2">
              <Button size="sm" onClick={handleSave} disabled={save.isPending}>
                {save.isPending ? 'Saving...' : 'Save'}
              </Button>
              <Button size="sm" variant="outline" onClick={() => setEditing(false)}>Cancel</Button>
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

export function SnmpTab() {
  const { orgId } = useOrgParam();
  const { data: devices, isLoading } = useSnmpDevices(orgId);

  return (
    <div className="space-y-6">
      <div>
        <h3 className="text-lg font-semibold">SNMP Network Devices</h3>
        <p className="text-sm text-muted-foreground">
          Discover and monitor switches, routers, firewalls, and access points via SNMP.
        </p>
      </div>

      <SnmpConfigCard orgId={orgId!} />

      {isLoading ? (
        <Skeleton className="h-48" />
      ) : !devices || devices.length === 0 ? (
        <EmptyState
          icon={<Router className="size-10" />}
          title="No SNMP devices discovered"
          description="Configure SNMP targets above, then run the agent to discover network devices."
        />
      ) : (
        <>
          {/* KPI cards */}
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <Card>
              <CardHeader className="flex flex-row items-center justify-between pb-2">
                <CardTitle className="text-sm font-medium text-muted-foreground">Devices</CardTitle>
                <Server className="h-4 w-4 text-muted-foreground" />
              </CardHeader>
              <CardContent>
                <p className="text-2xl font-bold">{devices.length}</p>
              </CardContent>
            </Card>
            <Card>
              <CardHeader className="flex flex-row items-center justify-between pb-2">
                <CardTitle className="text-sm font-medium text-muted-foreground">Total Interfaces</CardTitle>
                <Wifi className="h-4 w-4 text-muted-foreground" />
              </CardHeader>
              <CardContent>
                <p className="text-2xl font-bold">
                  {devices.reduce((s, d) => s + d.interfaceCount, 0)}
                </p>
              </CardContent>
            </Card>
            <Card>
              <CardHeader className="flex flex-row items-center justify-between pb-2">
                <CardTitle className="text-sm font-medium text-muted-foreground">Avg Uptime</CardTitle>
                <Clock className="h-4 w-4 text-muted-foreground" />
              </CardHeader>
              <CardContent>
                <p className="text-2xl font-bold">
                  {(() => {
                    const withUptime = devices.filter((d) => d.uptimeDays != null);
                    if (withUptime.length === 0) return '—';
                    const avg = withUptime.reduce((s, d) => s + d.uptimeDays!, 0) / withUptime.length;
                    return `${Math.floor(avg)}d`;
                  })()}
                </p>
              </CardContent>
            </Card>
            <Card>
              <CardHeader className="flex flex-row items-center justify-between pb-2">
                <CardTitle className="text-sm font-medium text-muted-foreground">Locations</CardTitle>
                <MapPin className="h-4 w-4 text-muted-foreground" />
              </CardHeader>
              <CardContent>
                <p className="text-2xl font-bold">
                  {new Set(devices.map((d) => d.sysLocation).filter(Boolean)).size || '—'}
                </p>
              </CardContent>
            </Card>
          </div>

          {/* Device table */}
          <Card>
            <CardHeader className="pb-3">
              <CardTitle className="flex items-center gap-2 text-base">
                <Router className="h-4 w-4 text-muted-foreground" />
                Discovered Devices
              </CardTitle>
            </CardHeader>
            <CardContent>
              <div className="overflow-x-auto">
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>IP Address</TableHead>
                      <TableHead>Name</TableHead>
                      <TableHead>Description</TableHead>
                      <TableHead>Model</TableHead>
                      <TableHead>Firmware</TableHead>
                      <TableHead className="text-center">IFs</TableHead>
                      <TableHead>Uptime</TableHead>
                      <TableHead>Last Scan</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {devices.map((d) => (
                      <DeviceRow key={d.id} device={d} />
                    ))}
                  </TableBody>
                </Table>
              </div>
            </CardContent>
          </Card>
        </>
      )}
    </div>
  );
}

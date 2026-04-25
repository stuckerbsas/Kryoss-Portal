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
  Network,
  Cable,
  ArrowRight,
  Cpu,
  HardDrive,
  MemoryStick,
  AlertTriangle,
  Monitor,
  Printer,
  Shield,
} from 'lucide-react';
import { toast } from 'sonner';
import {
  useSnmpConfig,
  useSnmpDevices,
  useSaveSnmpConfig,
  type SnmpDevice,
  type SnmpConfigPayload,
  type LldpNeighbor,
  type CdpNeighbor,
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

const DEVICE_TYPE_CONFIG: Record<string, { color: string; label: string }> = {
  switch: { color: 'bg-blue-100 text-blue-700', label: 'Switch' },
  router: { color: 'bg-green-100 text-green-700', label: 'Router' },
  access_point: { color: 'bg-purple-100 text-purple-700', label: 'AP' },
  server: { color: 'bg-amber-100 text-amber-700', label: 'Server' },
  printer: { color: 'bg-pink-100 text-pink-700', label: 'Printer' },
  firewall: { color: 'bg-red-100 text-red-700', label: 'Firewall' },
  workstation: { color: 'bg-indigo-100 text-indigo-700', label: 'Workstation' },
  unknown: { color: 'bg-gray-100 text-gray-500', label: 'Unknown' },
};

function deviceTypeBadge(type: string | null) {
  const cfg = DEVICE_TYPE_CONFIG[type ?? 'unknown'] ?? DEVICE_TYPE_CONFIG.unknown;
  return <Badge variant="secondary" className={cfg.color}>{cfg.label}</Badge>;
}

function ifStatusBadge(status: number | null) {
  if (status === 1) return <Badge variant="secondary" className="bg-green-100 text-green-800">Up</Badge>;
  if (status === 2) return <Badge variant="secondary" className="bg-red-100 text-red-800">Down</Badge>;
  return <Badge variant="secondary" className="bg-gray-100 text-gray-500">Unknown</Badge>;
}

function UsageBar({ used, total, unit, warn = 80, crit = 90 }: { used: number; total: number; unit: string; warn?: number; crit?: number }) {
  const pct = total > 0 ? Math.round((used / total) * 100) : 0;
  const color = pct >= crit ? 'bg-red-500' : pct >= warn ? 'bg-amber-500' : 'bg-green-500';
  return (
    <div className="space-y-0.5">
      <div className="flex justify-between text-[10px] text-muted-foreground">
        <span>{pct}%</span>
        <span>{used}/{total} {unit}</span>
      </div>
      <div className="h-1.5 bg-gray-200 rounded-full overflow-hidden">
        <div className={`h-full rounded-full ${color}`} style={{ width: `${Math.min(pct, 100)}%` }} />
      </div>
    </div>
  );
}

function NeighborTable({ lldp, cdp }: { lldp: LldpNeighbor[] | null; cdp: CdpNeighbor[] | null }) {
  const hasLldp = lldp && lldp.length > 0;
  const hasCdp = cdp && cdp.length > 0;
  if (!hasLldp && !hasCdp) return null;

  return (
    <div className="mt-3">
      <h5 className="text-sm font-semibold mb-2 flex items-center gap-2">
        <Cable className="h-4 w-4 text-blue-500" />
        Port Mapping ({(lldp?.length ?? 0) + (cdp?.length ?? 0)} neighbors)
      </h5>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Local Port</TableHead>
            <TableHead></TableHead>
            <TableHead>Remote Device</TableHead>
            <TableHead>Remote Port</TableHead>
            <TableHead>Protocol</TableHead>
            <TableHead>Details</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {lldp?.map((n, i) => (
            <TableRow key={`lldp-${i}`}>
              <TableCell className="font-mono text-xs font-medium">{n.localPort ?? '—'}</TableCell>
              <TableCell><ArrowRight className="h-3 w-3 text-muted-foreground" /></TableCell>
              <TableCell className="font-medium text-sm">
                {n.remoteSysName ?? n.remoteChassisId ?? '—'}
              </TableCell>
              <TableCell className="font-mono text-xs">{n.remotePortId ?? n.remotePortDesc ?? '—'}</TableCell>
              <TableCell><Badge variant="secondary" className="bg-blue-100 text-blue-700">LLDP</Badge></TableCell>
              <TableCell className="text-xs text-muted-foreground max-w-xs truncate">
                {n.remoteSysDesc ?? ''}
              </TableCell>
            </TableRow>
          ))}
          {cdp?.map((n, i) => (
            <TableRow key={`cdp-${i}`}>
              <TableCell className="font-mono text-xs font-medium">{n.localPort ?? '—'}</TableCell>
              <TableCell><ArrowRight className="h-3 w-3 text-muted-foreground" /></TableCell>
              <TableCell className="font-medium text-sm">
                {n.remoteDeviceId ?? '—'}
                {n.remoteIp && <span className="text-muted-foreground ml-1">({n.remoteIp})</span>}
              </TableCell>
              <TableCell className="font-mono text-xs">{n.remotePortId ?? '—'}</TableCell>
              <TableCell><Badge variant="secondary" className="bg-orange-100 text-orange-700">CDP</Badge></TableCell>
              <TableCell className="text-xs text-muted-foreground max-w-xs truncate">
                {n.remotePlatform ?? ''}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}

function DeviceRow({ device }: { device: SnmpDevice }) {
  const [expanded, setExpanded] = useState(false);
  const neighborCount = (device.lldpNeighborCount ?? 0) + (device.cdpNeighborCount ?? 0);

  return (
    <>
      <TableRow
        className={`cursor-pointer hover:bg-muted/50 ${device.isStale ? 'opacity-50' : ''}`}
        onClick={() => setExpanded(!expanded)}
      >
        <TableCell>
          <div className="flex items-center gap-2">
            {expanded ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
            <span className="font-mono text-xs">{device.ipAddress}</span>
            {device.isStale && <AlertTriangle className="h-3 w-3 text-amber-500" />}
            {device.machineId && <Monitor className="h-3 w-3 text-green-600" title="Kryoss Agent" />}
          </div>
        </TableCell>
        <TableCell className="font-medium text-sm">{device.sysName ?? '—'}</TableCell>
        <TableCell>{deviceTypeBadge(device.deviceType)}</TableCell>
        <TableCell className="text-xs text-muted-foreground">{device.vendor ?? '—'}</TableCell>
        <TableCell>
          {device.cpuLoadPct != null ? (
            <div className="w-16">
              <UsageBar used={device.cpuLoadPct} total={100} unit="%" />
            </div>
          ) : '—'}
        </TableCell>
        <TableCell>
          {device.memoryTotalMb != null && device.memoryUsedMb != null ? (
            <div className="w-20">
              <UsageBar used={Math.round(device.memoryUsedMb / 1024)} total={Math.round(device.memoryTotalMb / 1024)} unit="GB" />
            </div>
          ) : '—'}
        </TableCell>
        <TableCell>
          {device.diskTotalGb != null && device.diskUsedGb != null ? (
            <div className="w-20">
              <UsageBar used={device.diskUsedGb} total={device.diskTotalGb} unit="GB" />
            </div>
          ) : '—'}
        </TableCell>
        <TableCell className="text-center text-xs">{device.interfaceCount}</TableCell>
        <TableCell className="text-center">
          {neighborCount > 0 ? (
            <Badge variant="secondary" className="bg-blue-100 text-blue-700">{neighborCount}</Badge>
          ) : '—'}
        </TableCell>
        <TableCell>
          {device.uptimeDays != null ? (
            <span className="font-mono text-xs tabular-nums">{Math.floor(device.uptimeDays)}d</span>
          ) : '—'}
        </TableCell>
      </TableRow>

      {expanded && (
        <TableRow>
          <TableCell colSpan={10} className="bg-muted/30 p-4">
            {/* Device detail grid */}
            <div className="grid grid-cols-2 md:grid-cols-4 gap-3 mb-4 text-xs">
              {device.entityModel && <div><span className="text-muted-foreground">Model:</span> {device.entityMfg ? `${device.entityMfg} ${device.entityModel}` : device.entityModel}</div>}
              {device.entitySerial && <div><span className="text-muted-foreground">Serial:</span> {device.entitySerial}</div>}
              {device.entityFirmware && <div><span className="text-muted-foreground">Firmware:</span> {device.entityFirmware}</div>}
              {device.sysLocation && <div><span className="text-muted-foreground">Location:</span> {device.sysLocation}</div>}
              {device.macAddress && <div><span className="text-muted-foreground">MAC:</span> <span className="font-mono">{device.macAddress}</span></div>}
              {device.processCount != null && <div><span className="text-muted-foreground">Processes:</span> {device.processCount}</div>}
              {device.pageCount != null && <div><span className="text-muted-foreground">Page Count:</span> {device.pageCount.toLocaleString()}</div>}
              {device.scanSource && <div><span className="text-muted-foreground">Scanned by:</span> {device.scanSource}</div>}
              {device.firstSeenAt && <div><span className="text-muted-foreground">First seen:</span> {new Date(device.firstSeenAt).toLocaleDateString()}</div>}
              <div><span className="text-muted-foreground">Last scan:</span> {new Date(device.scannedAt).toLocaleString()}</div>
            </div>

            {/* Printer supplies */}
            {device.supplies?.length > 0 && (
              <div className="mb-4">
                <h5 className="text-sm font-semibold mb-2 flex items-center gap-2">
                  <Printer className="h-4 w-4 text-pink-500" />
                  Supplies ({device.supplies.length})
                </h5>
                <div className="grid grid-cols-2 md:grid-cols-4 gap-2">
                  {device.supplies.map((s, i) => (
                    <div key={i} className="border rounded p-2 text-xs">
                      <div className="font-medium truncate">{s.description}</div>
                      {s.levelPercent != null && (
                        <div className="mt-1">
                          <div className="h-2 bg-gray-200 rounded-full overflow-hidden">
                            <div
                              className="h-full rounded-full"
                              style={{
                                width: `${Math.max(0, Math.min(100, s.levelPercent))}%`,
                                backgroundColor: s.color ?? (s.levelPercent < 10 ? '#EF4444' : s.levelPercent < 25 ? '#F59E0B' : '#22C55E'),
                              }}
                            />
                          </div>
                          <span className="text-muted-foreground">{s.levelPercent}%</span>
                        </div>
                      )}
                    </div>
                  ))}
                </div>
              </div>
            )}

            {/* Vendor data */}
            {device.vendorData && Object.keys(device.vendorData).length > 0 && (
              <div className="mb-4">
                <h5 className="text-sm font-semibold mb-2">Vendor-Specific Data</h5>
                <div className="grid grid-cols-2 md:grid-cols-3 gap-2 text-xs">
                  {Object.entries(device.vendorData).map(([k, v]) => (
                    <div key={k}><span className="text-muted-foreground">{k}:</span> {v}</div>
                  ))}
                </div>
              </div>
            )}

            {/* Interfaces */}
            {device.interfaces.length > 0 && (
              <>
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
                            {iface.speedMbps != null ? (iface.speedMbps >= 1000 ? `${iface.speedMbps / 1000} Gbps` : `${iface.speedMbps} Mbps`) : '—'}
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
              </>
            )}

            {/* Port Mapping (LLDP/CDP) */}
            <NeighborTable lldp={device.lldpNeighbors} cdp={device.cdpNeighbors} />
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
              <div><span className="text-muted-foreground">Status</span><p>{config.enabled ? 'Enabled' : 'Disabled'}</p></div>
              <div><span className="text-muted-foreground">Version</span><p>SNMPv{config.version}</p></div>
              <div><span className="text-muted-foreground">Auth</span><p>{config.version === 3 ? (config.username ?? '—') : (config.community ? 'Community set' : '—')}</p></div>
              <div><span className="text-muted-foreground">Targets</span><p>{config.targets?.length ?? 0} configured</p></div>
            </div>
          ) : (
            <p className="text-sm text-muted-foreground">
              SNMP not configured. Agent auto-scans with SNMPv2c &quot;public&quot; by default. Configure custom credentials here.
            </p>
          )
        ) : (
          <div className="space-y-4">
            <div className="flex items-center gap-4">
              <div className="flex items-center gap-2">
                <Label>Enabled</Label>
                <Switch checked={form.enabled} onCheckedChange={(v: boolean) => setForm({ ...form, enabled: v })} />
              </div>
              <div>
                <Label>Version</Label>
                <select className="ml-2 border rounded px-2 py-1 text-sm" value={form.version} onChange={(e) => setForm({ ...form, version: Number(e.target.value) })}>
                  <option value={2}>v2c</option>
                  <option value={3}>v3</option>
                </select>
              </div>
            </div>
            {form.version === 2 ? (
              <div><Label>Community String</Label><Input type="password" placeholder="public" value={(form.community as string) ?? ''} onChange={(e) => setForm({ ...form, community: e.target.value })} /></div>
            ) : (
              <div className="grid grid-cols-2 gap-4">
                <div><Label>Username</Label><Input value={form.username ?? ''} onChange={(e) => setForm({ ...form, username: e.target.value })} /></div>
                <div><Label>Auth Protocol</Label><Input placeholder="SHA" value={form.authProtocol ?? ''} onChange={(e) => setForm({ ...form, authProtocol: e.target.value })} /></div>
                <div><Label>Auth Password</Label><Input type="password" onChange={(e) => setForm({ ...form, authPassword: e.target.value } as any)} /></div>
                <div><Label>Priv Protocol</Label><Input placeholder="AES" value={form.privProtocol ?? ''} onChange={(e) => setForm({ ...form, privProtocol: e.target.value })} /></div>
                <div><Label>Priv Password</Label><Input type="password" onChange={(e) => setForm({ ...form, privPassword: e.target.value } as any)} /></div>
              </div>
            )}
            <div><Label>Targets (comma-separated IPs or CIDRs)</Label><Input placeholder="10.0.0.1, 192.168.1.0/24" value={form.targets?.join(', ') ?? ''} onChange={(e) => setForm({ ...form, targets: e.target.value.split(',').map(s => s.trim()).filter(Boolean) })} /></div>
            <div className="flex gap-2">
              <Button size="sm" onClick={handleSave} disabled={save.isPending}>{save.isPending ? 'Saving...' : 'Save'}</Button>
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

  const active = devices?.filter(d => !d.isStale) ?? [];
  const stale = devices?.filter(d => d.isStale) ?? [];
  const totalNeighbors = devices?.reduce((s, d) => s + (d.lldpNeighborCount ?? 0) + (d.cdpNeighborCount ?? 0), 0) ?? 0;
  const withCpu = active.filter(d => d.cpuLoadPct != null);
  const avgCpu = withCpu.length > 0 ? Math.round(withCpu.reduce((s, d) => s + d.cpuLoadPct!, 0) / withCpu.length) : null;
  const withMem = active.filter(d => d.memoryTotalMb != null && d.memoryUsedMb != null);
  const avgMem = withMem.length > 0 ? Math.round(withMem.reduce((s, d) => s + (d.memoryUsedMb! / d.memoryTotalMb!) * 100, 0) / withMem.length) : null;
  const typeCounts: Record<string, number> = {};
  active.forEach(d => { typeCounts[d.deviceType ?? 'unknown'] = (typeCounts[d.deviceType ?? 'unknown'] ?? 0) + 1; });

  return (
    <div className="space-y-6">
      <div>
        <h3 className="text-lg font-semibold">SNMP Network Devices</h3>
        <p className="text-sm text-muted-foreground">
          Switches, routers, firewalls, printers, and servers discovered via SNMP. Port mapping via LLDP/CDP.
        </p>
      </div>

      <SnmpConfigCard orgId={orgId!} />

      {isLoading ? (
        <Skeleton className="h-48" />
      ) : !devices || devices.length === 0 ? (
        <EmptyState
          icon={<Router className="size-10" />}
          title="No SNMP devices discovered"
          description="Run the agent to auto-discover network devices via SNMPv2c. Default community: public."
        />
      ) : (
        <>
          {/* KPI cards */}
          <div className="grid gap-3 grid-cols-2 md:grid-cols-4 lg:grid-cols-7">
            <Card>
              <CardHeader className="flex flex-row items-center justify-between pb-1"><CardTitle className="text-xs text-muted-foreground">Devices</CardTitle><Server className="h-4 w-4 text-muted-foreground" /></CardHeader>
              <CardContent><p className="text-2xl font-bold">{active.length}</p>{stale.length > 0 && <p className="text-xs text-amber-500">{stale.length} stale</p>}</CardContent>
            </Card>
            <Card>
              <CardHeader className="flex flex-row items-center justify-between pb-1"><CardTitle className="text-xs text-muted-foreground">Interfaces</CardTitle><Wifi className="h-4 w-4 text-muted-foreground" /></CardHeader>
              <CardContent><p className="text-2xl font-bold">{active.reduce((s, d) => s + d.interfaceCount, 0)}</p></CardContent>
            </Card>
            <Card>
              <CardHeader className="flex flex-row items-center justify-between pb-1"><CardTitle className="text-xs text-muted-foreground">Neighbors</CardTitle><Network className="h-4 w-4 text-blue-500" /></CardHeader>
              <CardContent><p className="text-2xl font-bold">{totalNeighbors || '—'}</p></CardContent>
            </Card>
            <Card>
              <CardHeader className="flex flex-row items-center justify-between pb-1"><CardTitle className="text-xs text-muted-foreground">Avg CPU</CardTitle><Cpu className="h-4 w-4 text-muted-foreground" /></CardHeader>
              <CardContent>
                {avgCpu != null ? (
                  <p className={`text-2xl font-bold ${avgCpu > 80 ? 'text-red-500' : avgCpu > 60 ? 'text-amber-500' : ''}`}>{avgCpu}%</p>
                ) : <p className="text-2xl font-bold text-muted-foreground">—</p>}
              </CardContent>
            </Card>
            <Card>
              <CardHeader className="flex flex-row items-center justify-between pb-1"><CardTitle className="text-xs text-muted-foreground">Avg Memory</CardTitle><MemoryStick className="h-4 w-4 text-muted-foreground" /></CardHeader>
              <CardContent>
                {avgMem != null ? (
                  <p className={`text-2xl font-bold ${avgMem > 85 ? 'text-red-500' : avgMem > 70 ? 'text-amber-500' : ''}`}>{avgMem}%</p>
                ) : <p className="text-2xl font-bold text-muted-foreground">—</p>}
              </CardContent>
            </Card>
            <Card>
              <CardHeader className="flex flex-row items-center justify-between pb-1"><CardTitle className="text-xs text-muted-foreground">Avg Uptime</CardTitle><Clock className="h-4 w-4 text-muted-foreground" /></CardHeader>
              <CardContent>
                <p className="text-2xl font-bold">{(() => { const w = active.filter(d => d.uptimeDays != null); return w.length ? `${Math.floor(w.reduce((s, d) => s + d.uptimeDays!, 0) / w.length)}d` : '—'; })()}</p>
              </CardContent>
            </Card>
            <Card>
              <CardHeader className="flex flex-row items-center justify-between pb-1"><CardTitle className="text-xs text-muted-foreground">Locations</CardTitle><MapPin className="h-4 w-4 text-muted-foreground" /></CardHeader>
              <CardContent><p className="text-2xl font-bold">{new Set(active.map(d => d.sysLocation).filter(Boolean)).size || '—'}</p></CardContent>
            </Card>
          </div>

          {/* Type breakdown */}
          <div className="flex flex-wrap gap-2">
            {Object.entries(typeCounts).sort((a, b) => b[1] - a[1]).map(([type, count]) => {
              const cfg = DEVICE_TYPE_CONFIG[type] ?? DEVICE_TYPE_CONFIG.unknown;
              return <Badge key={type} variant="secondary" className={cfg.color}>{cfg.label}: {count}</Badge>;
            })}
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
                      <TableHead>Type</TableHead>
                      <TableHead>Vendor</TableHead>
                      <TableHead>CPU</TableHead>
                      <TableHead>Memory</TableHead>
                      <TableHead>Disk</TableHead>
                      <TableHead className="text-center">IFs</TableHead>
                      <TableHead className="text-center">Neighbors</TableHead>
                      <TableHead>Uptime</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {active.map((d) => <DeviceRow key={d.id} device={d} />)}
                    {stale.map((d) => <DeviceRow key={d.id} device={d} />)}
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

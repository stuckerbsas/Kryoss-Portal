import { useState } from 'react';
import {
  Building2,
  Server,
  AlertTriangle,
  Loader2,
  Plus,
  Trash2,
  CheckCircle,
  XCircle,
  PlayCircle,
  Monitor,
  Cpu,
  HardDrive,
} from 'lucide-react';
import {
  useInfraAssessment,
  useStartInfraAssessmentScan,
  useHypervisorConfigs,
  useCreateHypervisorConfig,
  useDeleteHypervisorConfig,
  useTestHypervisorConfig,
  useHypervisorScanResults,
  type HypervisorConfig,
  type HypervisorVm,
} from '@/api/infraAssessment';
import { useOrgParam } from '@/hooks/useOrgParam';
import { EmptyState } from '@/components/shared/EmptyState';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from '@/components/ui/tabs';
import {
  Dialog,
  DialogContent,
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
import { toast } from 'sonner';

function usageBadge(pct: number | null) {
  if (pct == null) return <span className="text-muted-foreground">—</span>;
  const color = pct > 85 ? 'text-red-600' : pct > 60 ? 'text-amber-600' : 'text-green-600';
  return <span className={`font-medium ${color}`}>{pct.toFixed(1)}%</span>;
}

function powerBadge(state: string) {
  const map: Record<string, string> = {
    on: 'bg-green-100 text-green-800',
    running: 'bg-green-100 text-green-800',
    off: 'bg-gray-100 text-gray-600',
    suspended: 'bg-amber-100 text-amber-800',
    paused: 'bg-amber-100 text-amber-800',
    maintenance: 'bg-blue-100 text-blue-800',
  };
  return <Badge className={map[state] ?? 'bg-gray-100 text-gray-600'}>{state}</Badge>;
}

function platformIcon(platform: string) {
  const colors: Record<string, string> = {
    vmware: 'text-blue-600',
    proxmox: 'text-orange-600',
    hyperv: 'text-purple-600',
  };
  return <Server className={`size-4 ${colors[platform] ?? 'text-gray-500'}`} />;
}

function AddConfigDialog({
  orgId,
  open,
  onOpenChange,
}: {
  orgId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const create = useCreateHypervisorConfig();
  const [platform, setPlatform] = useState('vmware');
  const [hostUrl, setHostUrl] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [apiToken, setApiToken] = useState('');

  const handleSubmit = () => {
    create.mutate(
      {
        organizationId: orgId,
        platform,
        displayName: displayName || undefined,
        hostUrl,
        username: username || undefined,
        password: password || undefined,
        apiToken: apiToken || undefined,
      },
      {
        onSuccess: () => {
          toast.success('Hypervisor config added');
          onOpenChange(false);
          setHostUrl('');
          setDisplayName('');
          setUsername('');
          setPassword('');
          setApiToken('');
        },
        onError: (err: Error) => toast.error(err.message),
      },
    );
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Add Hypervisor Connection</DialogTitle>
        </DialogHeader>
        <div className="space-y-3">
          <div>
            <Label>Platform</Label>
            <Select value={platform} onValueChange={setPlatform}>
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="vmware">VMware vCenter</SelectItem>
                <SelectItem value="proxmox">Proxmox VE</SelectItem>
                <SelectItem value="hyperv">Hyper-V</SelectItem>
              </SelectContent>
            </Select>
          </div>
          <div>
            <Label>Display Name</Label>
            <Input placeholder="Production vCenter" value={displayName} onChange={(e) => setDisplayName(e.target.value)} />
          </div>
          <div>
            <Label>Host URL</Label>
            <Input placeholder="https://vcenter.local" value={hostUrl} onChange={(e) => setHostUrl(e.target.value)} />
          </div>
          {platform !== 'proxmox' || !apiToken ? (
            <>
              <div>
                <Label>Username</Label>
                <Input placeholder="administrator@vsphere.local" value={username} onChange={(e) => setUsername(e.target.value)} />
              </div>
              <div>
                <Label>Password</Label>
                <Input type="password" value={password} onChange={(e) => setPassword(e.target.value)} />
              </div>
            </>
          ) : null}
          {platform === 'proxmox' && (
            <div>
              <Label>API Token (alternative to user/pass)</Label>
              <Input placeholder="user@realm!tokenid=secret" value={apiToken} onChange={(e) => setApiToken(e.target.value)} />
            </div>
          )}
          <Button onClick={handleSubmit} disabled={!hostUrl || create.isPending} className="w-full">
            {create.isPending && <Loader2 className="size-4 mr-1 animate-spin" />}
            Add Connection
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}

function ConfigsList({ orgId }: { orgId: string }) {
  const { data: configs, isLoading } = useHypervisorConfigs(orgId);
  const deleteConfig = useDeleteHypervisorConfig();
  const testConfig = useTestHypervisorConfig();
  const [showAdd, setShowAdd] = useState(false);

  if (isLoading) return <Skeleton className="h-32" />;

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-medium">Hypervisor Connections</h3>
        <Button size="sm" variant="outline" onClick={() => setShowAdd(true)}>
          <Plus className="size-4 mr-1" /> Add
        </Button>
      </div>

      {(!configs || configs.length === 0) ? (
        <p className="text-sm text-muted-foreground py-4 text-center">
          No hypervisor connections configured. Add a vCenter or Proxmox to scan.
        </p>
      ) : (
        <div className="space-y-2">
          {configs.map((c: HypervisorConfig) => (
            <div key={c.id} className="flex items-center gap-3 p-3 rounded border">
              {platformIcon(c.platform)}
              <div className="flex-1 min-w-0">
                <p className="text-sm font-medium">{c.displayName ?? c.hostUrl}</p>
                <p className="text-xs text-muted-foreground">{c.platform} — {c.hostUrl}</p>
              </div>
              {c.lastTestOk === true && <CheckCircle className="size-4 text-green-600" />}
              {c.lastTestOk === false && (
                <span className="text-xs text-red-600 max-w-40 truncate" title={c.lastError ?? undefined}>
                  <XCircle className="size-4 inline mr-1" />{c.lastError ?? 'Failed'}
                </span>
              )}
              <Button
                size="sm"
                variant="ghost"
                onClick={() => testConfig.mutate(
                  { configId: c.id, organizationId: orgId },
                  {
                    onSuccess: (r) => r.success ? toast.success('Connection OK') : toast.error(r.error ?? 'Test failed'),
                  },
                )}
                disabled={testConfig.isPending}
              >
                <PlayCircle className="size-4" />
              </Button>
              <Button
                size="sm"
                variant="ghost"
                className="text-red-500 hover:text-red-700"
                onClick={() => deleteConfig.mutate(
                  { configId: c.id, organizationId: orgId },
                  { onSuccess: () => toast.success('Removed') },
                )}
              >
                <Trash2 className="size-4" />
              </Button>
            </div>
          ))}
        </div>
      )}

      <AddConfigDialog orgId={orgId} open={showAdd} onOpenChange={setShowAdd} />
    </div>
  );
}

function VmTable({ vms }: { vms: HypervisorVm[] }) {
  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>VM Name</TableHead>
          <TableHead>OS</TableHead>
          <TableHead>Power</TableHead>
          <TableHead className="text-right">vCPU</TableHead>
          <TableHead className="text-right">RAM</TableHead>
          <TableHead className="text-right">Disk</TableHead>
          <TableHead className="text-right">CPU Avg</TableHead>
          <TableHead className="text-right">RAM Avg</TableHead>
          <TableHead className="text-right">Snapshots</TableHead>
          <TableHead>IP</TableHead>
          <TableHead>Flags</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {vms.map((v) => (
          <TableRow key={v.id} className={v.isIdle ? 'bg-amber-50/50' : ''}>
            <TableCell className="font-medium">{v.vmName}</TableCell>
            <TableCell className="text-sm">{v.os ?? '—'}</TableCell>
            <TableCell>{powerBadge(v.powerState)}</TableCell>
            <TableCell className="text-right">{v.cpuCores ?? '—'}</TableCell>
            <TableCell className="text-right">{v.ramGb != null ? `${v.ramGb} GB` : '—'}</TableCell>
            <TableCell className="text-right">{v.diskGb != null ? `${v.diskGb} GB` : '—'}</TableCell>
            <TableCell className="text-right">{usageBadge(v.cpuAvgPct)}</TableCell>
            <TableCell className="text-right">{usageBadge(v.ramAvgPct)}</TableCell>
            <TableCell className="text-right">
              {v.snapshotCount > 0 ? (
                <span className={v.oldestSnapshotDays && v.oldestSnapshotDays > 7 ? 'text-amber-600 font-medium' : ''}>
                  {v.snapshotCount}{v.oldestSnapshotDays ? ` (${v.oldestSnapshotDays}d)` : ''}
                </span>
              ) : '—'}
            </TableCell>
            <TableCell className="font-mono text-xs">{v.ipAddress ?? '—'}</TableCell>
            <TableCell>
              <div className="flex gap-1">
                {v.isIdle && <Badge className="bg-amber-100 text-amber-800 text-xs">idle</Badge>}
                {v.isTemplate && <Badge className="bg-blue-100 text-blue-800 text-xs">template</Badge>}
                {v.notes && <Badge className="bg-gray-100 text-gray-600 text-xs">{v.notes}</Badge>}
              </div>
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

function ServersVmsTab({ orgId }: { orgId: string | undefined }) {
  const { data: scanResult, isLoading } = useHypervisorScanResults(orgId);

  return (
    <div className="space-y-6">
      <ConfigsList orgId={orgId} />

      {isLoading && <Skeleton className="h-64" />}

      {scanResult && scanResult.hosts.length > 0 && (
        <>
          <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
            <Card>
              <CardHeader className="pb-2">
                <CardTitle className="text-sm font-medium text-muted-foreground">Hosts</CardTitle>
              </CardHeader>
              <CardContent>
                <div className="flex items-center gap-2">
                  <Cpu className="size-5 text-primary" />
                  <span className="text-2xl font-bold">{scanResult.hosts.length}</span>
                </div>
              </CardContent>
            </Card>
            <Card>
              <CardHeader className="pb-2">
                <CardTitle className="text-sm font-medium text-muted-foreground">VMs</CardTitle>
              </CardHeader>
              <CardContent>
                <div className="flex items-center gap-2">
                  <Monitor className="size-5 text-primary" />
                  <span className="text-2xl font-bold">{scanResult.vms.length}</span>
                </div>
                <p className="text-xs text-muted-foreground mt-1">
                  {scanResult.vms.filter((v) => v.powerState === 'on' || v.powerState === 'running').length} running
                </p>
              </CardContent>
            </Card>
            <Card>
              <CardHeader className="pb-2">
                <CardTitle className="text-sm font-medium text-muted-foreground">Idle VMs</CardTitle>
              </CardHeader>
              <CardContent>
                <div className="flex items-center gap-2">
                  <AlertTriangle className="size-5 text-amber-500" />
                  <span className="text-2xl font-bold">{scanResult.vms.filter((v) => v.isIdle).length}</span>
                </div>
              </CardContent>
            </Card>
            <Card>
              <CardHeader className="pb-2">
                <CardTitle className="text-sm font-medium text-muted-foreground">Total Storage</CardTitle>
              </CardHeader>
              <CardContent>
                <div className="flex items-center gap-2">
                  <HardDrive className="size-5 text-primary" />
                  <span className="text-2xl font-bold">
                    {Math.round(scanResult.hosts.reduce((a, h) => a + (h.storageGbTotal ?? 0), 0))} GB
                  </span>
                </div>
              </CardContent>
            </Card>
          </div>

          <Card>
            <CardHeader>
              <CardTitle className="text-base">Hypervisor Hosts</CardTitle>
            </CardHeader>
            <CardContent className="p-0 overflow-x-auto">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Host</TableHead>
                    <TableHead>Platform</TableHead>
                    <TableHead>Cluster</TableHead>
                    <TableHead className="text-right">CPU Cores</TableHead>
                    <TableHead className="text-right">RAM</TableHead>
                    <TableHead className="text-right">CPU Usage</TableHead>
                    <TableHead className="text-right">RAM Usage</TableHead>
                    <TableHead className="text-right">VMs</TableHead>
                    <TableHead>HA</TableHead>
                    <TableHead>State</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {scanResult.hosts.map((h) => (
                    <TableRow key={h.id}>
                      <TableCell className="font-medium">{h.hostFqdn}</TableCell>
                      <TableCell>
                        <div className="flex items-center gap-1">
                          {platformIcon(h.platform)}
                          <span className="text-sm">{h.platform}</span>
                        </div>
                      </TableCell>
                      <TableCell>{h.clusterName ?? '—'}</TableCell>
                      <TableCell className="text-right">{h.cpuCoresTotal ?? '—'}</TableCell>
                      <TableCell className="text-right">{h.ramGbTotal != null ? `${h.ramGbTotal} GB` : '—'}</TableCell>
                      <TableCell className="text-right">{usageBadge(h.cpuUsagePct)}</TableCell>
                      <TableCell className="text-right">{usageBadge(h.ramUsagePct)}</TableCell>
                      <TableCell className="text-right">{h.vmCount} ({h.vmRunning} on)</TableCell>
                      <TableCell>
                        {h.haEnabled === true && <CheckCircle className="size-4 text-green-600" />}
                        {h.haEnabled === false && <XCircle className="size-4 text-red-500" />}
                        {h.haEnabled == null && <span className="text-muted-foreground">—</span>}
                      </TableCell>
                      <TableCell>{powerBadge(h.powerState)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle className="text-base">Virtual Machines ({scanResult.vms.length})</CardTitle>
            </CardHeader>
            <CardContent className="p-0 overflow-x-auto">
              <VmTable vms={scanResult.vms.filter((v) => !v.isTemplate)} />
            </CardContent>
          </Card>

          {scanResult.findings.length > 0 && (
            <Card>
              <CardHeader>
                <CardTitle className="text-base">Findings ({scanResult.findings.length})</CardTitle>
              </CardHeader>
              <CardContent>
                <div className="space-y-2">
                  {scanResult.findings.map((f, i) => (
                    <div key={i} className="flex items-start gap-3 p-2 rounded border">
                      <Badge className={
                        f.priority === 'high' ? 'bg-red-100 text-red-800' :
                        f.priority === 'medium' ? 'bg-amber-100 text-amber-800' :
                        'bg-blue-100 text-blue-800'
                      }>
                        {f.priority}
                      </Badge>
                      <div className="flex-1 min-w-0">
                        <p className="text-sm font-medium">{f.observation}</p>
                        <p className="text-xs text-muted-foreground mt-0.5">{f.recommendation}</p>
                      </div>
                      <Badge variant="outline" className="text-xs">{f.area}</Badge>
                    </div>
                  ))}
                </div>
              </CardContent>
            </Card>
          )}
        </>
      )}

      {scanResult && scanResult.hosts.length === 0 && (
        <p className="text-sm text-muted-foreground text-center py-8">
          No hypervisor data yet. Add a connection above, then run a scan from the Overview tab.
        </p>
      )}
    </div>
  );
}

export function InfraAssessmentTab() {
  const { orgId } = useOrgParam();
  const { data: scan, isLoading } = useInfraAssessment(orgId);
  const startScan = useStartInfraAssessmentScan(orgId);

  const handleStartScan = () => {
    startScan.mutate(undefined, {
      onSuccess: () => toast.success('Infrastructure scan started'),
      onError: (err: Error) => toast.error(err.message),
    });
  };

  if (isLoading) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-8 w-64" />
        <div className="grid grid-cols-4 gap-4">
          {[1, 2, 3, 4].map((i) => (
            <Skeleton key={i} className="h-24" />
          ))}
        </div>
      </div>
    );
  }

  if (!scan) {
    return (
      <EmptyState
        icon={<Building2 className="size-10" />}
        title="No Infrastructure Assessment"
        description="Start a scan to assess your on-prem and hybrid infrastructure across sites, devices, connectivity, and capacity."
        action={
          <Button onClick={handleStartScan} disabled={startScan.isPending}>
            {startScan.isPending && <Loader2 className="size-4 mr-1 animate-spin" />}
            Start Scan
          </Button>
        }
      />
    );
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-lg font-semibold">Infrastructure Assessment</h2>
          <p className="text-sm text-muted-foreground">
            Status: <Badge variant="outline">{scan.status}</Badge>
            {scan.overallHealth != null && (
              <span className="ml-2">Health: {scan.overallHealth}%</span>
            )}
          </p>
        </div>
        <Button
          variant="outline"
          size="sm"
          onClick={handleStartScan}
          disabled={startScan.isPending || scan.status === 'running'}
        >
          {startScan.isPending && <Loader2 className="size-4 mr-1 animate-spin" />}
          Re-scan
        </Button>
      </div>

      <Tabs defaultValue="overview">
        <TabsList>
          <TabsTrigger value="overview">Overview</TabsTrigger>
          <TabsTrigger value="servers-vms">Servers & VMs</TabsTrigger>
        </TabsList>

        <TabsContent value="overview">
          <div className="space-y-6">
            <div className="grid grid-cols-4 gap-4">
              <Card>
                <CardHeader className="pb-2">
                  <CardTitle className="text-sm font-medium text-muted-foreground">Sites</CardTitle>
                </CardHeader>
                <CardContent>
                  <div className="flex items-center gap-2">
                    <Building2 className="size-5 text-primary" />
                    <span className="text-2xl font-bold">{scan.siteCount}</span>
                  </div>
                </CardContent>
              </Card>
              <Card>
                <CardHeader className="pb-2">
                  <CardTitle className="text-sm font-medium text-muted-foreground">Devices</CardTitle>
                </CardHeader>
                <CardContent>
                  <div className="flex items-center gap-2">
                    <Server className="size-5 text-primary" />
                    <span className="text-2xl font-bold">{scan.deviceCount}</span>
                  </div>
                </CardContent>
              </Card>
              <Card>
                <CardHeader className="pb-2">
                  <CardTitle className="text-sm font-medium text-muted-foreground">Findings</CardTitle>
                </CardHeader>
                <CardContent>
                  <div className="flex items-center gap-2">
                    <AlertTriangle className="size-5 text-amber-500" />
                    <span className="text-2xl font-bold">{scan.findingCount}</span>
                  </div>
                </CardContent>
              </Card>
              <Card>
                <CardHeader className="pb-2">
                  <CardTitle className="text-sm font-medium text-muted-foreground">Links</CardTitle>
                </CardHeader>
                <CardContent>
                  <span className="text-2xl font-bold">{scan.connectivity.length}</span>
                </CardContent>
              </Card>
            </div>

            {scan.findings.length > 0 && (
              <Card>
                <CardHeader>
                  <CardTitle className="text-base">Findings</CardTitle>
                </CardHeader>
                <CardContent>
                  <div className="space-y-2">
                    {scan.findings.map((f) => (
                      <div key={f.id} className="flex items-start gap-3 p-2 rounded border">
                        <Badge className={
                          f.status === 'action_required' ? 'bg-red-100 text-red-800' :
                          f.status === 'warning' ? 'bg-amber-100 text-amber-800' :
                          'bg-blue-100 text-blue-800'
                        }>
                          {f.status}
                        </Badge>
                        <div className="flex-1 min-w-0">
                          <p className="text-sm font-medium">{f.area}{f.service ? ` — ${f.service}` : ''}</p>
                          {f.observation && <p className="text-sm text-muted-foreground">{f.observation}</p>}
                          {f.recommendation && <p className="text-xs text-muted-foreground mt-1">{f.recommendation}</p>}
                        </div>
                        <Badge variant="outline" className="text-xs">{f.priority}</Badge>
                      </div>
                    ))}
                  </div>
                </CardContent>
              </Card>
            )}
          </div>
        </TabsContent>

        <TabsContent value="servers-vms">
          {orgId && <ServersVmsTab orgId={orgId} />}
        </TabsContent>
      </Tabs>
    </div>
  );
}

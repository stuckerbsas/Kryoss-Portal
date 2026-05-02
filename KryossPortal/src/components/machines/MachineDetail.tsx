import { useState, useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
import { toast } from 'sonner';
import { useMachineParam } from '@/hooks/useMachineParam';
import {
  LineChart,
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
} from 'recharts';
import {
  Monitor,
  Cpu,
  HardDrive,
  ShieldCheck,
  Network,
  Building2,
  Clock,
  ArrowLeft,
  Server,
  Check,
  X,
  Package,
  Search,
  ListChecks,
  KeyRound,
  History,
  Plug,
  ShieldAlert,
  Users,
  Settings,
  Play,
  ClipboardList,
  XCircle,
  Activity,
  Cog,
  ScrollText,
  Pencil,
  CalendarClock,
  RefreshCw,
  HeartPulse,
  ScanSearch,
  Radio,
  Globe,
  Wrench,
  Loader2,
  CircleAlert,
  CircleCheck,
  Trash2,
} from 'lucide-react';
import { useMachine, useMachineSoftware, useRunDetail, useUpdateAgentConfig, useTriggerScan, useUninstallSoftware } from '@/api/machines';
import { useMachineTasks, useCancelTask, useRescheduleTask } from '@/api/remediation';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from '@/components/ui/dialog';
import { ConfirmActionDialog } from '@/components/ui/confirm-action-dialog';
import { ServicesTab } from './ServicesTab';
import { ActivityTab } from './ActivityTab';
import type { AgentConfig, LoopStatus } from '@/api/machines';
import { useMachinePorts } from '@/api/ports';
import { useMachineThreats } from '@/api/threats';
import { useTrend } from '@/api/dashboard';
import { GradeBadge } from '@/components/shared/GradeBadge';
import { EmptyState } from '@/components/shared/EmptyState';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';
import { Switch } from '@/components/ui/switch';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import {
  Table,
  TableHeader,
  TableRow,
  TableHead,
  TableBody,
  TableCell,
} from '@/components/ui/table';
// ── Security categories for the Security tab ──
const SECURITY_CATEGORIES = new Set([
  'Account Policies',
  'Account Lockout',
  'Authentication',
  'Credential Protection',
  'User Rights Assignment',
  'Local Users And Account Management',
  'LAPS',
  'Credential Guard',
  'Credential UI',
  'Network Security',
  'Network Access',
  'Security Options',
]);

// ── Loading skeleton for tabs ──

function TabSkeleton({ rows = 6, cards = 0 }: { rows?: number; cards?: number }) {
  return (
    <div className="space-y-4 animate-in fade-in duration-300">
      {cards > 0 && (
        <div className={`grid grid-cols-2 md:grid-cols-${Math.min(cards, 4)} gap-3`}>
          {Array.from({ length: cards }).map((_, i) => (
            <Skeleton key={i} className="h-24" />
          ))}
        </div>
      )}
      <div className="space-y-2">
        {Array.from({ length: rows }).map((_, i) => (
          <Skeleton key={i} className="h-10 w-full" style={{ opacity: 1 - i * 0.12 }} />
        ))}
      </div>
    </div>
  );
}

// ── Helpers ──

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

function formatTimeAgo(dateStr: string | null): string {
  if (!dateStr) return 'Never';
  const diff = Date.now() - new Date(dateStr).getTime();
  const mins = Math.floor(diff / 60000);
  if (mins < 1) return 'Just now';
  if (mins < 60) return `${mins}m ago`;
  const hours = Math.floor(mins / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  return `${days}d ago`;
}

function statusColor(status: string): string {
  switch (status.toLowerCase()) {
    case 'pass': return 'bg-green-100 text-green-800 hover:bg-green-100';
    case 'warn': return 'bg-amber-100 text-amber-800 hover:bg-amber-100';
    case 'fail': return 'bg-red-100 text-red-800 hover:bg-red-100';
    default: return 'bg-gray-100 text-gray-500 hover:bg-gray-100';
  }
}

function severityColor(severity: string): string {
  switch (severity.toLowerCase()) {
    case 'critical': return 'bg-red-200 text-red-900 hover:bg-red-200';
    case 'high': return 'bg-red-100 text-red-800 hover:bg-red-100';
    case 'medium': return 'bg-amber-100 text-amber-800 hover:bg-amber-100';
    case 'low': return 'bg-blue-100 text-blue-800 hover:bg-blue-100';
    default: return 'bg-gray-100 text-gray-500 hover:bg-gray-100';
  }
}

// ── Shared sub-components ──

function BoolIndicator({ value, label }: { value: boolean | null; label: string }) {
  if (value == null) return <span className="text-muted-foreground text-sm">{label}: N/A</span>;
  return (
    <span className="inline-flex items-center gap-1 text-sm">
      {value ? <Check className="size-3.5 text-green-600" /> : <X className="size-3.5 text-red-500" />}
      <span className={value ? 'text-green-700' : 'text-red-600'}>{label}</span>
    </span>
  );
}

function InfoRow({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="flex justify-between py-1.5 border-b border-border/50 last:border-0">
      <span className="text-sm text-muted-foreground">{label}</span>
      <span className="text-sm font-medium text-right">{value ?? <span className="text-muted-foreground">N/A</span>}</span>
    </div>
  );
}

function SectionCard({ icon, title, children }: { icon: React.ReactNode; title: string; children: React.ReactNode }) {
  return (
    <Card className="p-4">
      <div className="flex items-center gap-2 mb-3">
        <div className="text-muted-foreground">{icon}</div>
        <h3 className="text-sm font-semibold">{title}</h3>
      </div>
      {children}
    </Card>
  );
}

// ── Tab: Overview ──

function OverviewTabContent({ machine, chartData, machineId }: { machine: any; chartData: any[] | undefined; machineId?: string }) {
  return (
    <div className="space-y-6">
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
        <SectionCard icon={<Monitor className="size-4" />} title="Operating System">
          <InfoRow label="OS" value={machine.osName} />
          <InfoRow label="Version" value={machine.osVersion} />
          <InfoRow label="Build" value={machine.osBuild} />
        </SectionCard>

        <SectionCard icon={<Server className="size-4" />} title="Hardware">
          <InfoRow label="Manufacturer" value={machine.manufacturer} />
          <InfoRow label="Model" value={machine.model} />
          <InfoRow label="Serial" value={machine.serialNumber} />
        </SectionCard>

        <SectionCard icon={<Cpu className="size-4" />} title="CPU & Memory">
          <InfoRow label="CPU" value={machine.cpuName} />
          <InfoRow label="Cores" value={machine.cpuCores} />
          <InfoRow label="RAM" value={machine.ramGb != null ? `${machine.ramGb} GB` : null} />
        </SectionCard>

        <SectionCard icon={<HardDrive className="size-4" />} title="Storage">
          {machine.disks && machine.disks.length > 0 ? (
            <div className="space-y-3">
              {machine.disks.map((d: any) => {
                const pctFree = d.totalGb && d.freeGb != null ? (d.freeGb / d.totalGb) * 100 : null;
                const pctUsed = pctFree != null ? 100 - pctFree : 0;
                const barColor = pctFree != null && pctFree < 20 ? '#C0392B' : pctFree != null && pctFree < 40 ? '#D97706' : '#008852';
                return (
                  <div key={d.driveLetter} className="space-y-1.5">
                    <div className="flex items-center justify-between text-sm">
                      <div className="flex items-center gap-2">
                        <span className="font-mono font-semibold">{d.driveLetter}:</span>
                        {d.label && <span className="text-muted-foreground text-xs">{d.label}</span>}
                      </div>
                      <span className="text-xs text-muted-foreground">
                        {d.diskType ?? ''} {d.fileSystem ? `· ${d.fileSystem}` : ''}
                      </span>
                    </div>
                    <div className="h-2 bg-gray-100 rounded-full overflow-hidden">
                      <div
                        className="h-full rounded-full transition-all"
                        style={{ width: `${pctUsed}%`, backgroundColor: barColor }}
                      />
                    </div>
                    <div className="flex justify-between text-xs text-muted-foreground">
                      <span>{d.freeGb != null ? `${d.freeGb} GB free` : '--'}</span>
                      <span>{d.totalGb != null ? `${d.totalGb} GB total` : '--'}</span>
                    </div>
                  </div>
                );
              })}
            </div>
          ) : (
            <>
              <InfoRow label="Type" value={machine.diskType} />
              <InfoRow label="Capacity" value={machine.diskSizeGb != null ? `${machine.diskSizeGb} GB` : null} />
              <InfoRow label="Free" value={
                machine.diskFreeGb != null ? (
                  <span className={machine.diskFreeGb < 20 ? 'text-red-600 font-semibold' : ''}>
                    {machine.diskFreeGb} GB
                    {machine.diskSizeGb ? ` (${Math.round((machine.diskFreeGb / machine.diskSizeGb) * 100)}%)` : ''}
                  </span>
                ) : null
              } />
            </>
          )}
        </SectionCard>

        <SectionCard icon={<ShieldCheck className="size-4" />} title="Security">
          <div className="flex justify-between items-center py-1.5 border-b border-border/50">
            <span className="text-sm text-muted-foreground">TPM</span>
            <span className="text-sm font-medium">
              {machine.tpmPresent == null ? 'N/A' : machine.tpmPresent ? `v${machine.tpmVersion ?? '?'}` : 'Not present'}
            </span>
          </div>
          <div className="flex gap-4 py-1.5">
            <BoolIndicator value={machine.secureBoot} label="Secure Boot" />
            <BoolIndicator value={machine.bitlocker} label="BitLocker" />
          </div>
        </SectionCard>

        <SectionCard icon={<Network className="size-4" />} title="Network">
          <InfoRow label="IP Address" value={machine.ipAddress} />
          <InfoRow label="MAC" value={machine.macAddress} />
        </SectionCard>

        <SectionCard icon={<Building2 className="size-4" />} title="Domain">
          <InfoRow label="Status" value={
            machine.domainStatus ? <Badge variant="outline" className="text-xs">{
              ({ DomainJoined: 'Domain Joined', AzureADJoined: 'Azure AD Joined', HybridJoined: 'Hybrid Joined', Workgroup: 'Workgroup' } as Record<string, string>)[machine.domainStatus] ?? machine.domainStatus
            }</Badge> : null
          } />
          <InfoRow label="Domain" value={machine.domainName} />
          {machine.aadTenantId && <InfoRow label="Tenant ID" value={machine.aadTenantId} />}
        </SectionCard>

        {machine.localAdmins && machine.localAdmins.length > 0 && (
          <SectionCard icon={<Users className="size-4" />} title="Local Administrators">
            {machine.localAdmins.map((a: { name: string; type: string; source: string; isEnabled: boolean | null; passwordNeverExpires: boolean | null; lastLogon: string | null }, i: number) => (
              <div key={i} className="flex justify-between items-center py-1.5 border-b border-border/50 last:border-0">
                <div className="flex items-center gap-2">
                  <span className="text-sm">{a.name}</span>
                  {a.isEnabled === false && <Badge variant="outline" className="text-xs text-red-600 border-red-300">Disabled</Badge>}
                  {a.passwordNeverExpires && <Badge variant="outline" className="text-xs text-amber-600 border-amber-300">Pwd Never Expires</Badge>}
                </div>
                <Badge variant="secondary" className={
                  a.source === 'Domain' ? 'bg-purple-100 text-purple-800 dark:bg-purple-900/30 dark:text-purple-300' : 'bg-blue-100 text-blue-800 dark:bg-blue-900/30 dark:text-blue-300'
                }>
                  {a.source === 'Domain' ? 'Domain' : 'Local'} {a.type}
                </Badge>
              </div>
            ))}
          </SectionCard>
        )}

        <SectionCard icon={<Clock className="size-4" />} title="Lifecycle">
          <InfoRow label="Age" value={
            machine.systemAgeDays != null
              ? machine.systemAgeDays > 365
                ? `${Math.round(machine.systemAgeDays / 365 * 10) / 10} years`
                : `${machine.systemAgeDays} days`
              : null
          } />
          <InfoRow label="Last Boot" value={machine.lastBootAt ? formatDate(machine.lastBootAt) : null} />
          <InfoRow label="Last Seen" value={formatTimeAgo(machine.lastSeenAt)} />
        </SectionCard>

        {machine.agentConfig && (
          <AgentConfigCard config={machine.agentConfig} machineId={machineId} />
        )}

        {machine.loopStatus && (
          <LoopStatusCard
            loopStatus={machine.loopStatus}
            lastHeartbeatAt={machine.lastHeartbeatAt}
            agentMode={machine.agentMode}
            uptimeSeconds={machine.agentUptimeSeconds}
            lastErrorAt={machine.lastErrorAt}
            lastErrorPhase={machine.lastErrorPhase}
            lastErrorMsg={machine.lastErrorMsg}
          />
        )}
      </div>

      {chartData && chartData.length > 0 && (
        <Card className="p-4">
          <h3 className="text-sm font-semibold mb-4">Score Trend</h3>
          <ResponsiveContainer width="100%" height={220}>
            <LineChart data={chartData}>
              <CartesianGrid strokeDasharray="3 3" />
              <XAxis dataKey="date" fontSize={12} />
              <YAxis domain={[0, 100]} fontSize={12} />
              <Tooltip />
              <Line type="monotone" dataKey="score" stroke="#008852" strokeWidth={2} dot={{ r: 3 }} />
            </LineChart>
          </ResponsiveContainer>
        </Card>
      )}
    </div>
  );
}

// ── Tab: Software ──

const licenseTypeBadge: Record<string, { label: string; className: string }> = {
  Commercial: { label: 'Commercial', className: 'bg-red-100 text-red-800' },
  'Likely Commercial': { label: 'Likely Commercial', className: 'bg-orange-100 text-orange-800' },
  Free: { label: 'Free', className: 'bg-green-100 text-green-800' },
  OpenSource: { label: 'Open Source', className: 'bg-emerald-100 text-emerald-800' },
  Freemium: { label: 'Freemium', className: 'bg-amber-100 text-amber-800' },
  Bundled: { label: 'Bundled', className: 'bg-blue-100 text-blue-800' },
  Unknown: { label: 'Unknown', className: 'bg-gray-100 text-gray-600' },
};

function SoftwareTabContent({ machineId }: { machineId: string | undefined }) {
  const [search, setSearch] = useState('');
  const { data: softwareData, isLoading } = useMachineSoftware(machineId);
  const uninstallMutation = useUninstallSoftware(machineId);
  const [uninstallTarget, setUninstallTarget] = useState<{ name: string; uninstallString: string } | null>(null);

  if (isLoading) return <TabSkeleton rows={10} />;

  if (!softwareData || softwareData.items.length === 0)
    return <EmptyState icon={<Package className="size-10" />} title="No software data" description="Run an assessment to collect software inventory." />;

  const filtered = softwareData.items.filter(
    (s) => !search || s.name.toLowerCase().includes(search.toLowerCase()) || (s.publisher ?? '').toLowerCase().includes(search.toLowerCase()),
  );

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between gap-2">
        <span className="text-sm text-muted-foreground whitespace-nowrap">{softwareData.total} packages</span>
        <div className="relative w-full sm:w-64">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 size-3.5 text-muted-foreground" />
          <Input placeholder="Search software..." value={search} onChange={(e) => setSearch(e.target.value)} className="pl-9 h-8 text-sm" />
        </div>
      </div>
      {/* Mobile cards */}
      <div className="space-y-3 sm:hidden">
        {filtered.map((s, i) => {
          const badge = licenseTypeBadge[s.licenseType] ?? licenseTypeBadge.Unknown;
          return (
            <div key={i} className="rounded-lg border p-4">
              <div className="flex items-start justify-between gap-2">
                <span className="font-medium text-sm truncate">{s.name}</span>
                <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium shrink-0 ${badge.className}`}>{badge.label}</span>
              </div>
              <div className="flex items-center gap-3 mt-1 text-xs text-muted-foreground">
                {s.version && <span className="font-mono">{s.version}</span>}
                {s.publisher && <span>{s.publisher}</span>}
              </div>
              {s.uninstallString && (
                <Button
                  variant="ghost"
                  size="sm"
                  className="h-7 px-2 text-destructive hover:text-destructive mt-2"
                  onClick={() => setUninstallTarget({ name: s.name, uninstallString: s.uninstallString! })}
                >
                  <Trash2 className="size-3.5 mr-1" /> Uninstall
                </Button>
              )}
            </div>
          );
        })}
      </div>
      {/* Desktop table */}
      <div className="hidden sm:block max-h-[600px] overflow-y-auto rounded-md border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Name</TableHead>
              <TableHead>Version</TableHead>
              <TableHead>Publisher</TableHead>
              <TableHead>License Type</TableHead>
              <TableHead className="w-20" />
            </TableRow>
          </TableHeader>
          <TableBody>
            {filtered.map((s, i) => {
              const badge = licenseTypeBadge[s.licenseType] ?? licenseTypeBadge.Unknown;
              return (
                <TableRow key={i}>
                  <TableCell className="font-medium text-sm">{s.name}</TableCell>
                  <TableCell className="text-sm text-muted-foreground font-mono">{s.version ?? '—'}</TableCell>
                  <TableCell className="text-sm text-muted-foreground">{s.publisher ?? '—'}</TableCell>
                  <TableCell><span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${badge.className}`}>{badge.label}</span></TableCell>
                  <TableCell>
                    {s.uninstallString && (
                      <Button
                        variant="ghost"
                        size="sm"
                        className="h-7 px-2 text-destructive hover:text-destructive"
                        onClick={() => setUninstallTarget({ name: s.name, uninstallString: s.uninstallString! })}
                      >
                        <Trash2 className="size-3.5 mr-1" /> Uninstall
                      </Button>
                    )}
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </div>
      <ConfirmActionDialog
        open={!!uninstallTarget}
        onClose={() => setUninstallTarget(null)}
        onConfirm={() => {
          if (!uninstallTarget) return;
          uninstallMutation.mutate(
            { displayName: uninstallTarget.name, uninstallString: uninstallTarget.uninstallString },
            {
              onSuccess: () => { toast.success(`Uninstall task queued for ${uninstallTarget.name}`); setUninstallTarget(null); },
              onError: () => toast.error('Failed to queue uninstall task'),
            },
          );
        }}
        title={`Uninstall ${uninstallTarget?.name ?? ''}?`}
        description="This will queue a silent uninstall on the remote machine. The software will be removed on the next agent check-in."
        destructive
        confirmLabel="Uninstall"
      />
    </div>
  );
}

// ── Tab: Controls ──

function ControlsTabContent({ machineId, latestRunId }: { machineId: string | undefined; latestRunId: string | undefined }) {
  const { data: run, isLoading } = useRunDetail(machineId, latestRunId);
  const [severity, setSeverity] = useState('All');
  const [status, setStatus] = useState('All');
  const [search, setSearch] = useState('');

  if (isLoading) return <TabSkeleton rows={8} cards={4} />;

  if (!run) return <EmptyState icon={<ListChecks className="size-10" />} title="No assessment data" description="Run an assessment to see control results." />;

  return <ControlResultsView run={run} severity={severity} setSeverity={setSeverity} status={status} setStatus={setStatus} search={search} setSearch={setSearch} />;
}

// ── Tab: Security ──

function SecurityTabContent({ machineId, latestRunId }: { machineId: string | undefined; latestRunId: string | undefined }) {
  const { data: run, isLoading } = useRunDetail(machineId, latestRunId);

  if (isLoading) return <TabSkeleton rows={6} cards={0} />;

  if (!run) return <EmptyState icon={<KeyRound className="size-10" />} title="No assessment data" description="Run an assessment to see security posture." />;

  const securityResults = run.results.filter((r) => SECURITY_CATEGORIES.has(r.categoryName));
  const passCount = securityResults.filter((r) => r.status === 'pass').length;
  const warnCount = securityResults.filter((r) => r.status === 'warn').length;
  const failCount = securityResults.filter((r) => r.status === 'fail').length;
  const total = securityResults.length;
  const score = total > 0 ? Math.round((passCount / total) * 100) : 0;

  // Group by category
  const byCategory = new Map<string, typeof securityResults>();
  for (const r of securityResults) {
    const list = byCategory.get(r.categoryName) ?? [];
    list.push(r);
    byCategory.set(r.categoryName, list);
  }

  return (
    <div className="space-y-4">
      {/* Summary */}
      <div className="flex flex-col sm:flex-row sm:items-center gap-2 sm:gap-4">
        <div className="flex items-center gap-2">
          <span className="text-2xl font-bold tabular-nums">{score}%</span>
          <span className="text-sm text-muted-foreground">security compliance</span>
        </div>
        <div className="flex items-center gap-2 flex-wrap">
          <Badge variant="secondary" className="bg-green-100 text-green-800">{passCount} Pass</Badge>
          <Badge variant="secondary" className="bg-amber-100 text-amber-800">{warnCount} Warn</Badge>
          <Badge variant="secondary" className="bg-red-100 text-red-800">{failCount} Fail</Badge>
          <span className="text-sm text-muted-foreground sm:ml-auto">{total} controls</span>
        </div>
      </div>

      {/* By category */}
      {Array.from(byCategory.entries())
        .sort(([a], [b]) => a.localeCompare(b))
        .map(([category, items]) => {
          const catPass = items.filter((r) => r.status === 'pass').length;
          const catFail = items.filter((r) => r.status === 'fail').length;
          const catScore = items.length > 0 ? Math.round((catPass / items.length) * 100) : 0;
          return (
            <Card key={category} className="p-4">
              <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-1 mb-3">
                <h4 className="text-sm font-semibold">{category}</h4>
                <div className="flex items-center gap-2">
                  <span className="text-sm font-bold tabular-nums" style={{
                    color: catScore >= 90 ? '#008852' : catScore >= 70 ? '#A2C564' : catScore >= 50 ? '#D97706' : '#C0392B'
                  }}>{catScore}%</span>
                  <span className="text-xs text-muted-foreground">{items.length} controls</span>
                </div>
              </div>
              <div className="h-1.5 bg-gray-100 rounded-full overflow-hidden mb-3">
                <div className="h-full rounded-full" style={{
                  width: `${catScore}%`,
                  backgroundColor: catScore >= 90 ? '#008852' : catScore >= 70 ? '#A2C564' : catScore >= 50 ? '#D97706' : '#C0392B',
                }} />
              </div>
              {catFail > 0 && (
                <div className="space-y-1">
                  {items.filter((r) => r.status === 'fail').map((r) => (
                    <div key={r.controlId} className="py-1">
                      {/* Mobile */}
                      <div className="sm:hidden rounded-lg border p-3 space-y-1">
                        <div className="flex items-center justify-between gap-2">
                          <span className="font-mono text-xs text-muted-foreground">{r.controlId}</span>
                          <Badge variant="secondary" className={`text-xs ${severityColor(r.severity)}`}>{r.severity}</Badge>
                        </div>
                        <p className="text-sm">{r.name}</p>
                      </div>
                      {/* Desktop */}
                      <div className="hidden sm:flex items-center gap-2 text-sm">
                        <X className="size-3.5 text-red-500 shrink-0" />
                        <span className="font-mono text-xs text-muted-foreground">{r.controlId}</span>
                        <span className="truncate">{r.name}</span>
                        <Badge variant="secondary" className={`ml-auto text-xs ${severityColor(r.severity)}`}>{r.severity}</Badge>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </Card>
          );
        })}
    </div>
  );
}

// ── Tab: History ──

function HistoryTabContent({ machine, orgSlug, machineSlug }: { machine: any; orgSlug: string | undefined; machineSlug: string | undefined }) {
  const navigate = useNavigate();

  if (machine.assessmentHistory.length === 0)
    return <EmptyState icon={<History className="size-10" />} title="No assessments yet" description="Run the agent on this machine to generate assessment data." />;

  return (
    <>
      {/* Mobile cards */}
      <div className="space-y-3 sm:hidden">
        {machine.assessmentHistory.map((run: any) => (
          <div key={run.id} className="rounded-lg border p-4 cursor-pointer" onClick={() => navigate(`/organizations/${orgSlug}/machines/${machineSlug}/runs/${run.id}`)}>
            <div className="flex items-center justify-between gap-2">
              <span className="font-medium text-sm">{formatDate(run.startedAt)}</span>
              <GradeBadge grade={run.grade} />
            </div>
            <div className="flex items-center gap-3 mt-1 text-xs text-muted-foreground">
              <span>Score: {run.globalScore ?? 'N/A'}</span>
              <span className="text-green-700">{run.passCount ?? 0}P</span>
              <span className="text-amber-600">{run.warnCount ?? 0}W</span>
              <span className="text-red-600">{run.failCount ?? 0}F</span>
            </div>
          </div>
        ))}
      </div>
      {/* Desktop table */}
      <div className="hidden sm:block rounded-md border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Date</TableHead>
              <TableHead>Score</TableHead>
              <TableHead>Grade</TableHead>
              <TableHead>Pass</TableHead>
              <TableHead>Warn</TableHead>
              <TableHead>Fail</TableHead>
              <TableHead>Duration</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {machine.assessmentHistory.map((run: any) => (
              <TableRow key={run.id} className="cursor-pointer" onClick={() => navigate(`/organizations/${orgSlug}/machines/${machineSlug}/runs/${run.id}`)}>
                <TableCell>{formatDate(run.startedAt)}</TableCell>
                <TableCell>{run.globalScore != null ? run.globalScore : 'N/A'}</TableCell>
                <TableCell><GradeBadge grade={run.grade} /></TableCell>
                <TableCell className="text-green-700">{run.passCount ?? 0}</TableCell>
                <TableCell className="text-amber-600">{run.warnCount ?? 0}</TableCell>
                <TableCell className="text-red-600">{run.failCount ?? 0}</TableCell>
                <TableCell className="text-muted-foreground">{formatDuration(run.durationMs)}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
    </>
  );
}

// ── Tab: Ports ──

function PortsTabContent({ machineId }: { machineId: string | undefined }) {
  const { data, isLoading } = useMachinePorts(machineId);

  if (isLoading) return <TabSkeleton rows={8} cards={4} />;

  if (!data || data.ports.length === 0)
    return <EmptyState icon={<Plug className="size-10" />} title="No port scan data" description="Run a port scan to see open ports on this machine." />;

  function riskColor(risk: string | null): string {
    switch (risk) {
      case 'critical': return 'bg-red-200 text-red-900 hover:bg-red-200';
      case 'high': return 'bg-red-100 text-red-800 hover:bg-red-100';
      case 'medium': return 'bg-amber-100 text-amber-800 hover:bg-amber-100';
      case 'low': return 'bg-blue-100 text-blue-800 hover:bg-blue-100';
      default: return 'bg-gray-100 text-gray-500 hover:bg-gray-100';
    }
  }

  return (
    <div className="space-y-4">
      {/* KPI row */}
      <div className="grid grid-cols-2 sm:grid-cols-2 lg:grid-cols-4 gap-3">
        <Card className="p-3">
          <div className="text-sm text-muted-foreground">Total Open</div>
          <div className="text-2xl font-bold tabular-nums">{data.totalOpen}</div>
        </Card>
        <Card className="p-3">
          <div className="text-sm text-muted-foreground">Critical</div>
          <div className="text-2xl font-bold tabular-nums" style={{ color: data.critical > 0 ? '#C0392B' : '#006536' }}>{data.critical}</div>
        </Card>
        <Card className="p-3">
          <div className="text-sm text-muted-foreground">High</div>
          <div className="text-2xl font-bold tabular-nums" style={{ color: data.high > 0 ? '#D97706' : '#006536' }}>{data.high}</div>
        </Card>
        <Card className="p-3">
          <div className="text-sm text-muted-foreground">Medium</div>
          <div className="text-2xl font-bold tabular-nums" style={{ color: data.medium > 0 ? '#D97706' : '#006536' }}>{data.medium}</div>
        </Card>
      </div>

      {/* Mobile cards */}
      <div className="space-y-3 sm:hidden">
        {data.ports.map((p, i) => (
          <div key={i} className="rounded-lg border p-4">
            <div className="flex items-center justify-between gap-2">
              <span className="font-mono font-medium text-sm">:{p.port}</span>
              <div className="flex items-center gap-2">
                <Badge variant="secondary" className={p.status === 'open' ? 'bg-green-100 text-green-800 hover:bg-green-100' : 'bg-gray-100 text-gray-500 hover:bg-gray-100'}>
                  {p.status}
                </Badge>
                {p.risk && <Badge variant="secondary" className={riskColor(p.risk)}>{p.risk}</Badge>}
              </div>
            </div>
            <div className="flex items-center gap-3 mt-1 text-xs text-muted-foreground">
              <span>{p.protocol}</span>
              {p.service && <span>{p.service}</span>}
            </div>
          </div>
        ))}
      </div>
      {/* Desktop table */}
      <div className="hidden sm:block max-h-[600px] overflow-y-auto rounded-md border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Port</TableHead>
              <TableHead>Protocol</TableHead>
              <TableHead>Service</TableHead>
              <TableHead>Status</TableHead>
              <TableHead>Risk</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {data.ports.map((p, i) => (
              <TableRow key={i}>
                <TableCell className="font-mono font-medium">{p.port}</TableCell>
                <TableCell className="text-sm text-muted-foreground">{p.protocol}</TableCell>
                <TableCell className="text-sm">{p.service ?? '--'}</TableCell>
                <TableCell>
                  <Badge variant="secondary" className={p.status === 'open' ? 'bg-green-100 text-green-800 hover:bg-green-100' : 'bg-gray-100 text-gray-500 hover:bg-gray-100'}>
                    {p.status}
                  </Badge>
                </TableCell>
                <TableCell>
                  {p.risk ? (
                    <Badge variant="secondary" className={riskColor(p.risk)}>{p.risk}</Badge>
                  ) : (
                    <span className="text-sm text-muted-foreground">--</span>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
    </div>
  );
}

// ── Tab: Threats ──

function ThreatsTabContent({ machineId }: { machineId: string | undefined }) {
  const { data, isLoading } = useMachineThreats(machineId);

  if (isLoading) return <TabSkeleton rows={8} cards={4} />;

  if (!data || data.threats.length === 0)
    return <EmptyState icon={<ShieldAlert className="size-10" />} title="No threats detected" description="No threat indicators found on this machine." />;

  function categoryBadge(category: string) {
    const config: Record<string, string> = {
      browser_hijacker: 'bg-amber-100 text-amber-800',
      adware: 'bg-yellow-100 text-yellow-800',
      stalkerware: 'bg-red-200 text-red-900',
      keylogger: 'bg-red-200 text-red-900',
      rat: 'bg-red-200 text-red-900',
      c2_tool: 'bg-red-200 text-red-900',
      cryptominer: 'bg-purple-100 text-purple-800',
      ransomware: 'bg-red-200 text-red-900',
      fake_av: 'bg-amber-100 text-amber-800',
      loader_stealer: 'bg-red-100 text-red-800',
      employee_monitor: 'bg-blue-100 text-blue-800',
      pup: 'bg-gray-100 text-gray-600',
    };
    return (
      <Badge variant="secondary" className={config[category] ?? 'bg-gray-100 text-gray-500'}>
        {category.replace(/_/g, ' ')}
      </Badge>
    );
  }

  function threatSeverityColor(sev: string): string {
    switch (sev) {
      case 'critical': return 'bg-red-200 text-red-900 hover:bg-red-200';
      case 'high': return 'bg-red-100 text-red-800 hover:bg-red-100';
      case 'medium': return 'bg-amber-100 text-amber-800 hover:bg-amber-100';
      case 'low': return 'bg-blue-100 text-blue-800 hover:bg-blue-100';
      default: return 'bg-gray-100 text-gray-500 hover:bg-gray-100';
    }
  }

  return (
    <div className="space-y-4">
      {/* KPI row */}
      <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-5 gap-3">
        <Card className="p-3">
          <div className="text-sm text-muted-foreground">Total</div>
          <div className="text-2xl font-bold tabular-nums">{data.total}</div>
        </Card>
        <Card className="p-3">
          <div className="text-sm text-muted-foreground">Critical</div>
          <div className="text-2xl font-bold tabular-nums" style={{ color: data.critical > 0 ? '#C0392B' : '#006536' }}>{data.critical}</div>
        </Card>
        <Card className="p-3">
          <div className="text-sm text-muted-foreground">High</div>
          <div className="text-2xl font-bold tabular-nums" style={{ color: data.high > 0 ? '#D97706' : '#006536' }}>{data.high}</div>
        </Card>
        <Card className="p-3">
          <div className="text-sm text-muted-foreground">Medium</div>
          <div className="text-2xl font-bold tabular-nums" style={{ color: data.medium > 0 ? '#D97706' : '#006536' }}>{data.medium}</div>
        </Card>
        <Card className="p-3">
          <div className="text-sm text-muted-foreground">Low</div>
          <div className="text-2xl font-bold tabular-nums">{data.low}</div>
        </Card>
      </div>

      {/* Mobile cards */}
      <div className="space-y-3 sm:hidden">
        {data.threats.map((t, i) => (
          <div key={i} className="rounded-lg border p-4">
            <div className="flex items-start justify-between gap-2">
              <span className="font-medium text-sm truncate">{t.threatName}</span>
              <Badge variant="secondary" className={threatSeverityColor(t.severity)}>{t.severity}</Badge>
            </div>
            <div className="flex items-center gap-2 mt-1 flex-wrap">
              {categoryBadge(t.category)}
              <Badge variant="outline" className="text-xs">{t.vector}</Badge>
            </div>
            {t.detail && <p className="text-xs text-muted-foreground mt-1 line-clamp-2">{t.detail}</p>}
          </div>
        ))}
      </div>
      {/* Desktop table */}
      <div className="hidden sm:block max-h-[600px] overflow-y-auto rounded-md border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Threat Name</TableHead>
              <TableHead>Category</TableHead>
              <TableHead>Severity</TableHead>
              <TableHead>Vector</TableHead>
              <TableHead>Detail</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {data.threats.map((t, i) => (
              <TableRow key={i}>
                <TableCell className="font-medium text-sm">{t.threatName}</TableCell>
                <TableCell>{categoryBadge(t.category)}</TableCell>
                <TableCell>
                  <Badge variant="secondary" className={threatSeverityColor(t.severity)}>{t.severity}</Badge>
                </TableCell>
                <TableCell>
                  <Badge variant="outline" className="text-xs">{t.vector}</Badge>
                </TableCell>
                <TableCell className="text-sm text-muted-foreground max-w-xs truncate">{t.detail ?? '--'}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
    </div>
  );
}

// ── Tab: Tasks ──

function TasksTabContent({ machineId, scanPending, scanRequestedAt }: { machineId: string | undefined; scanPending?: boolean; scanRequestedAt?: string | null }) {
  const { data, isLoading } = useMachineTasks(machineId);
  const cancel = useCancelTask(machineId);
  const reschedule = useRescheduleTask(machineId);
  const [editTaskId, setEditTaskId] = useState<number | null>(null);
  const [editDate, setEditDate] = useState('');

  if (isLoading) return <TabSkeleton rows={4} />;

  const hasTasks = data && data.tasks.length > 0;
  if (!hasTasks && !scanPending)
    return <EmptyState icon={<ClipboardList className="size-10" />} title="No tasks" description="No remediation or scan tasks for this machine." />;

  const pending = data?.tasks.filter(t => t.status === 'approved' || t.status === 'pending') ?? [];
  const completed = data?.tasks.filter(t => t.status !== 'approved' && t.status !== 'pending') ?? [];

  function taskStatusBadge(status: string) {
    const cls: Record<string, string> = {
      approved: 'bg-blue-100 text-blue-800',
      pending: 'bg-yellow-100 text-yellow-800',
      executing: 'bg-purple-100 text-purple-800',
      completed: 'bg-green-100 text-green-800',
      failed: 'bg-red-100 text-red-800',
      cancelled: 'bg-gray-100 text-gray-500',
      rolled_back: 'bg-orange-100 text-orange-800',
    };
    return <Badge variant="secondary" className={cls[status] ?? 'bg-gray-100 text-gray-500'}>{status}</Badge>;
  }

  function openEditDialog(taskId: number, currentScheduled: string | null) {
    setEditTaskId(taskId);
    setEditDate(currentScheduled ? currentScheduled.slice(0, 16) : '');
  }

  function handleSaveSchedule() {
    if (editTaskId === null) return;
    reschedule.mutate(
      { taskId: editTaskId, scheduledFor: editDate ? new Date(editDate).toISOString() : null },
      {
        onSuccess: () => { setEditTaskId(null); toast.success('Schedule updated'); },
        onError: () => toast.error('Failed to reschedule'),
      }
    );
  }

  const pendingCount = pending.length + (scanPending ? 1 : 0);

  return (
    <div className="space-y-4">
      {pendingCount > 0 && (
        <>
          <h4 className="text-sm font-semibold">Pending ({pendingCount})</h4>
          {/* Mobile cards */}
          <div className="space-y-3 sm:hidden">
            {scanPending && (
              <div className="rounded-lg border p-4">
                <div className="flex items-center justify-between gap-2">
                  <span className="font-medium text-sm">Compliance scan requested</span>
                  <Badge variant="secondary" className="bg-blue-100 text-blue-800">waiting</Badge>
                </div>
                <div className="text-xs text-muted-foreground mt-1">
                  <span className="font-mono mr-2">SCAN</span>
                  {scanRequestedAt && <span>{formatDate(scanRequestedAt)}</span>}
                </div>
              </div>
            )}
            {pending.map(t => (
              <div key={t.id} className="rounded-lg border p-4">
                <div className="flex items-start justify-between gap-2">
                  <span className="font-medium text-sm truncate">{t.controlName}</span>
                  {taskStatusBadge(t.status)}
                </div>
                <div className="flex items-center gap-2 mt-1 text-xs text-muted-foreground">
                  <span className="font-mono">{t.controlId}</span>
                  <span>{t.actionType}</span>
                  {t.scheduledFor ? <span>{formatDate(t.scheduledFor)}</span> : <span>Immediate</span>}
                </div>
                <div className="flex items-center gap-1 mt-2">
                  <Button size="sm" variant="ghost" className="text-muted-foreground hover:text-foreground" onClick={() => openEditDialog(t.id, t.scheduledFor)}>
                    <Pencil className="size-3.5 mr-1" /> Edit
                  </Button>
                  <Button size="sm" variant="ghost" className="text-red-600 hover:text-red-700 hover:bg-red-50" disabled={cancel.isPending} onClick={() => cancel.mutate(t.id)}>
                    <XCircle className="size-3.5 mr-1" /> Cancel
                  </Button>
                </div>
              </div>
            ))}
          </div>
          {/* Desktop table */}
          <div className="hidden sm:block rounded-md border">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Control</TableHead>
                  <TableHead>Action</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Scheduled</TableHead>
                  <TableHead className="hidden lg:table-cell">Created</TableHead>
                  <TableHead className="w-32"></TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {scanPending && (
                  <TableRow>
                    <TableCell>
                      <span className="font-mono text-xs text-muted-foreground mr-2">SCAN</span>
                      <span className="text-sm">Compliance scan requested</span>
                    </TableCell>
                    <TableCell className="text-sm">scan</TableCell>
                    <TableCell><Badge variant="secondary" className="bg-blue-100 text-blue-800">waiting</Badge></TableCell>
                    <TableCell className="text-muted-foreground text-xs">--</TableCell>
                    <TableCell className="text-sm text-muted-foreground hidden lg:table-cell">{scanRequestedAt ? formatDate(scanRequestedAt) : '--'}</TableCell>
                    <TableCell></TableCell>
                  </TableRow>
                )}
                {pending.map(t => (
                  <TableRow key={t.id}>
                    <TableCell>
                      <span className="font-mono text-xs text-muted-foreground mr-2">{t.controlId}</span>
                      <span className="text-sm">{t.controlName}</span>
                    </TableCell>
                    <TableCell className="text-sm">{t.actionType}</TableCell>
                    <TableCell>{taskStatusBadge(t.status)}</TableCell>
                    <TableCell>
                      <div className="flex items-center gap-1">
                        {t.scheduledFor ? (
                          <span className="inline-flex items-center gap-1 text-sm">
                            <CalendarClock className="size-3.5 text-muted-foreground" />
                            {formatDate(t.scheduledFor)}
                          </span>
                        ) : (
                          <span className="text-muted-foreground text-xs">Immediate</span>
                        )}
                      </div>
                    </TableCell>
                    <TableCell className="text-sm text-muted-foreground hidden lg:table-cell">{formatDate(t.createdAt)}</TableCell>
                    <TableCell>
                      <div className="flex items-center gap-1">
                        <Button
                          size="sm"
                          variant="ghost"
                          className="text-muted-foreground hover:text-foreground"
                          onClick={() => openEditDialog(t.id, t.scheduledFor)}
                        >
                          <Pencil className="size-3.5 mr-1" />
                          Edit
                        </Button>
                        <Button
                          size="sm"
                          variant="ghost"
                          className="text-red-600 hover:text-red-700 hover:bg-red-50"
                          disabled={cancel.isPending}
                          onClick={() => cancel.mutate(t.id)}
                        >
                          <XCircle className="size-3.5 mr-1" />
                          Cancel
                        </Button>
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        </>
      )}

      {completed.length > 0 && (
        <>
          <h4 className="text-sm font-semibold text-muted-foreground">History ({completed.length})</h4>
          {/* Mobile cards */}
          <div className="space-y-3 sm:hidden">
            {completed.map(t => (
              <div key={t.id} className="rounded-lg border p-4">
                <div className="flex items-start justify-between gap-2">
                  <span className="font-medium text-sm truncate">{t.controlName}</span>
                  {taskStatusBadge(t.status)}
                </div>
                <div className="flex items-center gap-2 mt-1 text-xs text-muted-foreground">
                  <span className="font-mono">{t.controlId}</span>
                  <span>{t.actionType}</span>
                  {t.completedAt && <span>{formatDate(t.completedAt)}</span>}
                </div>
                {t.errorMessage && <p className="text-xs text-red-600 mt-1 line-clamp-2">{t.errorMessage}</p>}
              </div>
            ))}
          </div>
          {/* Desktop table */}
          <div className="hidden sm:block rounded-md border">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Control</TableHead>
                  <TableHead>Action</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Completed</TableHead>
                  <TableHead>Error</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {completed.map(t => (
                  <TableRow key={t.id}>
                    <TableCell>
                      <span className="font-mono text-xs text-muted-foreground mr-2">{t.controlId}</span>
                      <span className="text-sm">{t.controlName}</span>
                    </TableCell>
                    <TableCell className="text-sm">{t.actionType}</TableCell>
                    <TableCell>{taskStatusBadge(t.status)}</TableCell>
                    <TableCell className="text-sm text-muted-foreground">{t.completedAt ? formatDate(t.completedAt) : '--'}</TableCell>
                    <TableCell className="text-sm text-red-600 max-w-xs truncate">{t.errorMessage ?? '--'}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        </>
      )}

      <Dialog open={editTaskId !== null} onOpenChange={(open) => { if (!open) setEditTaskId(null); }}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Reschedule Task</DialogTitle>
          </DialogHeader>
          <div className="space-y-3 py-2">
            <label className="text-sm font-medium">Apply at</label>
            <input
              type="datetime-local"
              className="flex h-9 w-full rounded-md border border-input bg-transparent px-3 py-1 text-sm shadow-sm focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
              value={editDate}
              onChange={(e) => setEditDate(e.target.value)}
            />
            <p className="text-xs text-muted-foreground">Leave empty for immediate execution on next heartbeat.</p>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setEditTaskId(null)}>Cancel</Button>
            <Button onClick={handleSaveSchedule} disabled={reschedule.isPending}>Save</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

// ── Shared Controls View (reused by Controls and could be reused elsewhere) ──

function ControlResultsView({ run, severity, setSeverity, status, setStatus, search, setSearch }: any) {
  const filtered = useMemo(() => {
    let items = run.results;
    if (severity !== 'All') items = items.filter((r: any) => r.severity.toLowerCase() === severity.toLowerCase());
    if (status !== 'All') items = items.filter((r: any) => r.status.toLowerCase() === status.toLowerCase());
    if (search.trim()) {
      const q = search.toLowerCase();
      items = items.filter((r: any) => r.controlId.toLowerCase().includes(q) || r.name.toLowerCase().includes(q));
    }
    return items;
  }, [run, severity, status, search]);

  return (
    <div className="space-y-4">
      {/* Framework scores */}
      {run.frameworkScores && run.frameworkScores.length > 0 && (
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
          {run.frameworkScores.map((fs: any) => (
            <Card key={fs.code} className="p-3">
              <div className="flex items-center justify-between mb-2">
                <span className="text-sm font-semibold">{fs.code}</span>
                <span className="text-lg font-bold tabular-nums">{fs.score}%</span>
              </div>
              <div className="h-1.5 bg-gray-100 rounded-full overflow-hidden mb-2">
                <div className="h-full rounded-full" style={{
                  width: `${fs.score}%`,
                  backgroundColor: fs.score >= 90 ? '#008852' : fs.score >= 70 ? '#A2C564' : fs.score >= 50 ? '#D97706' : '#C0392B',
                }} />
              </div>
              <div className="flex gap-2 text-xs text-muted-foreground">
                <span className="text-green-700">{fs.passCount}P</span>
                <span className="text-amber-600">{fs.warnCount}W</span>
                <span className="text-red-600">{fs.failCount}F</span>
              </div>
            </Card>
          ))}
        </div>
      )}

      {/* Filters */}
      <div className="flex flex-col sm:flex-row sm:flex-wrap sm:items-center gap-3">
        <div className="relative flex-1 sm:max-w-xs">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 size-4 text-muted-foreground" />
          <Input placeholder="Search controls..." value={search} onChange={(e) => setSearch(e.target.value)} className="pl-9" />
        </div>
        <div className="flex gap-2">
          <Select value={severity} onValueChange={(v) => setSeverity(v)}>
            <SelectTrigger className="flex-1 sm:w-[130px]"><SelectValue placeholder="Severity" /></SelectTrigger>
            <SelectContent>
              {['All', 'critical', 'high', 'medium', 'low'].map((s) => (
                <SelectItem key={s} value={s}>{s === 'All' ? 'All Severities' : s}</SelectItem>
              ))}
            </SelectContent>
          </Select>
          <Select value={status} onValueChange={(v) => setStatus(v)}>
            <SelectTrigger className="flex-1 sm:w-[120px]"><SelectValue placeholder="Status" /></SelectTrigger>
            <SelectContent>
              {['All', 'pass', 'warn', 'fail'].map((s) => (
                <SelectItem key={s} value={s}>{s === 'All' ? 'All Statuses' : s}</SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <span className="text-sm text-muted-foreground sm:ml-auto">{filtered.length} results</span>
      </div>

      {/* Results grouped by category */}
      <div className="max-h-[600px] overflow-y-auto space-y-3">
        {(() => {
          const byCategory = new Map<string, any[]>();
          for (const r of filtered) {
            const cat = r.categoryName || 'Uncategorized';
            const list = byCategory.get(cat) ?? [];
            list.push(r);
            byCategory.set(cat, list);
          }
          return Array.from(byCategory.entries()).sort(([a], [b]) => a.localeCompare(b)).map(([cat, items]) => {
            const catPass = items.filter((r: any) => r.status === 'pass').length;
            const catFail = items.filter((r: any) => r.status === 'fail').length;
            const catWarn = items.filter((r: any) => r.status === 'warn').length;
            const catScore = items.length > 0 ? Math.round((catPass / items.length) * 100) : 0;
            return (
              <Card key={cat} className="p-4">
                <div className="flex items-center justify-between mb-2">
                  <h4 className="text-sm font-semibold">{cat}</h4>
                  <div className="flex items-center gap-2">
                    <span className="text-sm font-bold tabular-nums" style={{
                      color: catScore >= 90 ? '#008852' : catScore >= 70 ? '#A2C564' : catScore >= 50 ? '#D97706' : '#C0392B'
                    }}>{catScore}%</span>
                    <span className="text-xs text-muted-foreground">{catPass}P {catWarn}W {catFail}F</span>
                  </div>
                </div>
                <div className="h-1.5 bg-gray-100 rounded-full overflow-hidden mb-3">
                  <div className="h-full rounded-full" style={{
                    width: `${catScore}%`,
                    backgroundColor: catScore >= 90 ? '#008852' : catScore >= 70 ? '#A2C564' : catScore >= 50 ? '#D97706' : '#C0392B',
                  }} />
                </div>
                {/* Mobile cards */}
                <div className="space-y-2 sm:hidden">
                  {items.map((r: any) => (
                    <div key={r.controlId} className="rounded-lg border p-3 space-y-1.5">
                      <div className="flex items-center justify-between gap-2">
                        <span className="font-mono text-xs font-medium text-muted-foreground">{r.controlId}</span>
                        <div className="flex gap-1.5">
                          <Badge variant="secondary" className={severityColor(r.severity)}>{r.severity}</Badge>
                          <Badge variant="secondary" className={statusColor(r.status)}>{r.status}</Badge>
                        </div>
                      </div>
                      <p className="text-sm">{r.name}</p>
                    </div>
                  ))}
                </div>
                {/* Desktop table */}
                <div className="hidden sm:block">
                <Table>
                  <TableBody>
                    {items.map((r: any) => (
                      <TableRow key={r.controlId}>
                        <TableCell className="font-mono text-xs w-24">{r.controlId}</TableCell>
                        <TableCell className="text-sm max-w-md truncate" title={r.name}>{r.name}</TableCell>
                        <TableCell className="w-20"><Badge variant="secondary" className={severityColor(r.severity)}>{r.severity}</Badge></TableCell>
                        <TableCell className="w-16"><Badge variant="secondary" className={statusColor(r.status)}>{r.status}</Badge></TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
                </div>
              </Card>
            );
          });
        })()}
      </div>
    </div>
  );
}

// ── Main Component ──

const machineTabSections = [
  { value: 'overview', label: 'Overview' },
  { value: 'software', label: 'Software' },
  { value: 'controls', label: 'Controls' },
  { value: 'security', label: 'Security' },
  { value: 'threats', label: 'Threats' },
  { value: 'ports', label: 'Ports' },
  { value: 'tasks', label: 'Tasks' },
  { value: 'history', label: 'History' },
  { value: 'services', label: 'Services' },
  { value: 'activity', label: 'Activity' },
];

export function MachineDetail() {
  const { orgSlug, machineId, machineSlug } = useMachineParam();
  const navigate = useNavigate();

  const [activeTab, setActiveTab] = useState('overview');
  const { data: machine, isLoading } = useMachine(machineSlug);
  const { data: trendData } = useTrend({ machineId, months: 6 });
  const triggerScan = useTriggerScan(machineId);

  if (isLoading) {
    return (
      <div className="space-y-4 animate-in fade-in duration-500">
        {/* Header skeleton */}
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-4">
            <Skeleton className="h-8 w-16" />
            <Skeleton className="h-7 w-48" />
            <Skeleton className="h-5 w-16 rounded-full" />
          </div>
          <Skeleton className="h-6 w-24 rounded-full" />
        </div>
        {/* Tabs skeleton */}
        <div className="flex gap-1 bg-muted p-1 rounded-lg w-fit">
          {Array.from({ length: 6 }).map((_, i) => (
            <Skeleton key={i} className="h-8 w-24 rounded-md" />
          ))}
        </div>
        {/* Cards skeleton */}
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
          {Array.from({ length: 8 }).map((_, i) => (
            <Skeleton key={i} className="h-28 rounded-lg" />
          ))}
        </div>
        {/* Chart skeleton */}
        <Skeleton className="h-56 rounded-lg" />
      </div>
    );
  }

  if (!machine) {
    return <EmptyState title="Machine not found" description="This machine does not exist or you don't have access." />;
  }

  const chartData = (trendData?.dataPoints as { globalScore: number; startedAt: string }[] | undefined)?.map((dp) => ({
    date: new Date(dp.startedAt).toLocaleDateString(undefined, { month: 'short', day: 'numeric' }),
    score: dp.globalScore,
  }));

  const latestRun = machine.assessmentHistory?.[0];

  return (
    <div className="space-y-4">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
        <div className="flex items-center gap-3 flex-wrap">
          <Button variant="ghost" size="sm" onClick={() => navigate(`/organizations/${orgSlug}/devices`)}>
            <ArrowLeft className="size-4 mr-1" />
            Fleet
          </Button>
          <h2 className="text-xl font-semibold">{machine.hostname}</h2>
          <Badge variant="secondary" className={machine.isActive ? 'bg-green-100 text-green-800 hover:bg-green-100' : 'bg-gray-100 text-gray-500 hover:bg-gray-100'}>
            {machine.isActive ? 'Active' : 'Inactive'}
          </Badge>
          {machine.osName && <span className="text-sm text-muted-foreground hidden sm:inline">{machine.osName}</span>}
        </div>
        <div className="flex items-center gap-3">
          <Button
            size="sm"
            variant="outline"
            disabled={triggerScan.isPending || machine.scanPending}
            onClick={() => triggerScan.mutate()}
          >
            <Play className="size-3.5 mr-1" />
            {triggerScan.isPending ? 'Queuing...' : machine.scanPending ? 'Scan Pending...' : 'Run Scan'}
          </Button>
          {latestRun && (
            <>
              <span className="text-sm text-muted-foreground hidden sm:inline">Latest:</span>
              <GradeBadge grade={latestRun.grade} score={latestRun.globalScore} />
            </>
          )}
        </div>
      </div>

      {/* Tabs */}
      <Tabs value={activeTab} onValueChange={setActiveTab} className="space-y-4">
        {/* Mobile: select */}
        <div className="sm:hidden">
          <Select value={activeTab} onValueChange={setActiveTab}>
            <SelectTrigger className="w-full">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {machineTabSections.map((s) => (
                <SelectItem key={s.value} value={s.value}>
                  {s.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        {/* Desktop: tabs */}
        <TabsList className="hidden sm:inline-flex">
          <TabsTrigger value="overview" className="gap-1.5">
            <Monitor className="size-3.5" /> Overview
          </TabsTrigger>
          <TabsTrigger value="software" className="gap-1.5">
            <Package className="size-3.5" /> Software
          </TabsTrigger>
          <TabsTrigger value="controls" className="gap-1.5">
            <ListChecks className="size-3.5" /> Controls
          </TabsTrigger>
          <TabsTrigger value="security" className="gap-1.5">
            <KeyRound className="size-3.5" /> Security
          </TabsTrigger>
          <TabsTrigger value="threats" className="gap-1.5">
            <ShieldAlert className="size-3.5" /> Threats
          </TabsTrigger>
          <TabsTrigger value="ports" className="gap-1.5">
            <Plug className="size-3.5" /> Ports
          </TabsTrigger>
          <TabsTrigger value="tasks" className="gap-1.5">
            <ClipboardList className="size-3.5" /> Tasks
          </TabsTrigger>
          <TabsTrigger value="history" className="gap-1.5">
            <History className="size-3.5" /> History
          </TabsTrigger>
          <TabsTrigger value="services" className="gap-1.5">
            <Cog className="size-3.5" /> Services
          </TabsTrigger>
          <TabsTrigger value="activity" className="gap-1.5">
            <ScrollText className="size-3.5" /> Activity
          </TabsTrigger>
        </TabsList>

        <TabsContent value="overview">
          <OverviewTabContent machine={machine} chartData={chartData} machineId={machine?.id} />
        </TabsContent>

        <TabsContent value="software">
          <SoftwareTabContent machineId={machineId} />
        </TabsContent>

        <TabsContent value="controls">
          <ControlsTabContent machineId={machineId} latestRunId={latestRun?.id} />
        </TabsContent>

        <TabsContent value="security">
          <SecurityTabContent machineId={machineId} latestRunId={latestRun?.id} />
        </TabsContent>

        <TabsContent value="threats">
          <ThreatsTabContent machineId={machineId} />
        </TabsContent>

        <TabsContent value="ports">
          <PortsTabContent machineId={machineId} />
        </TabsContent>

        <TabsContent value="tasks">
          <TasksTabContent machineId={machineId} scanPending={machine.scanPending} scanRequestedAt={machine.scanRequestedAt} />
        </TabsContent>

        <TabsContent value="history">
          <HistoryTabContent machine={machine} orgSlug={orgSlug} machineSlug={machineSlug} />
        </TabsContent>

        <TabsContent value="services">
          <ServicesTab machineId={machine?.id} hostname={machine?.hostname ?? ''} />
        </TabsContent>
        <TabsContent value="activity">
          <ActivityTab machineId={machine?.id} />
        </TabsContent>
      </Tabs>
    </div>
  );
}

function AgentConfigCard({ config, machineId }: { config: AgentConfig; machineId?: string }) {
  const [local, setLocal] = useState(config);
  const mutation = useUpdateAgentConfig(machineId);

  // Sync when server data changes
  useMemo(() => setLocal(config), [config]);

  const update = (patch: Partial<AgentConfig>) => {
    setLocal((prev) => ({ ...prev, ...patch }));
    mutation.mutate(patch, {
      onError: (err: any) => {
        setLocal(config);
        toast.error(`Config update failed: ${err.message}`);
      },
    });
  };

  return (
    <SectionCard icon={<Settings className="size-4" />} title="Agent Configuration">
      <div className="space-y-3">
        <div className="flex justify-between items-center py-1">
          <span className="text-sm text-muted-foreground">Compliance scan interval</span>
          <Select value={String(local.complianceIntervalHours)} onValueChange={(v) => update({ complianceIntervalHours: Number(v) })}>
            <SelectTrigger className="w-24 h-8 text-xs"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="6">6h</SelectItem>
              <SelectItem value="12">12h</SelectItem>
              <SelectItem value="24">24h</SelectItem>
              <SelectItem value="48">48h</SelectItem>
            </SelectContent>
          </Select>
        </div>
        <div className="flex justify-between items-center py-1">
          <span className="text-sm text-muted-foreground">SNMP scan interval</span>
          <Select value={String(local.snmpIntervalMinutes)} onValueChange={(v) => update({ snmpIntervalMinutes: Number(v) })}>
            <SelectTrigger className="w-24 h-8 text-xs"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="60">1h</SelectItem>
              <SelectItem value="120">2h</SelectItem>
              <SelectItem value="240">4h</SelectItem>
              <SelectItem value="480">8h</SelectItem>
            </SelectContent>
          </Select>
        </div>
        <div className="flex justify-between items-center py-1">
          <span className="text-sm text-muted-foreground">Network scan</span>
          <div className="flex items-center gap-2">
            <Switch checked={local.enableNetworkScan} onCheckedChange={(v) => update({ enableNetworkScan: v })} />
            {local.enableNetworkScan && (
              <Select value={String(local.networkScanIntervalHours)} onValueChange={(v) => update({ networkScanIntervalHours: Number(v) })}>
                <SelectTrigger className="w-20 h-8 text-xs"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="6">6h</SelectItem>
                  <SelectItem value="12">12h</SelectItem>
                  <SelectItem value="24">24h</SelectItem>
                </SelectContent>
              </Select>
            )}
          </div>
        </div>
        <div className="flex justify-between items-center py-1">
          <span className="text-sm text-muted-foreground">Passive discovery</span>
          <Switch checked={local.enablePassiveDiscovery} onCheckedChange={(v) => update({ enablePassiveDiscovery: v })} />
        </div>
      </div>
      <p className="text-xs text-muted-foreground mt-2">Changes apply on next heartbeat (~15 min)</p>
    </SectionCard>
  );
}

const LOOP_META: Record<string, { label: string; icon: React.ReactNode }> = {
  update: { label: 'Self-Update', icon: <RefreshCw className="size-3.5" /> },
  heartbeat: { label: 'Heartbeat', icon: <HeartPulse className="size-3.5" /> },
  compliance: { label: 'Compliance Scan', icon: <ScanSearch className="size-3.5" /> },
  snmp: { label: 'SNMP Discovery', icon: <Radio className="size-3.5" /> },
  network: { label: 'Network Scan', icon: <Globe className="size-3.5" /> },
  remediation: { label: 'Remediation', icon: <Wrench className="size-3.5" /> },
};

function LoopStatusCard({
  loopStatus,
  lastHeartbeatAt,
  agentMode,
  uptimeSeconds,
  lastErrorAt,
  lastErrorPhase,
  lastErrorMsg,
}: {
  loopStatus: Record<string, LoopStatus>;
  lastHeartbeatAt: string | null;
  agentMode: string | null;
  uptimeSeconds: number | null;
  lastErrorAt: string | null;
  lastErrorPhase: string | null;
  lastErrorMsg: string | null;
}) {
  const formatDuration = (ms: number) => {
    if (ms < 1000) return `${ms}ms`;
    if (ms < 60000) return `${(ms / 1000).toFixed(1)}s`;
    return `${(ms / 60000).toFixed(1)}m`;
  };

  const formatUptime = (s: number) => {
    const d = Math.floor(s / 86400);
    const h = Math.floor((s % 86400) / 3600);
    if (d > 0) return `${d}d ${h}h`;
    const m = Math.floor((s % 3600) / 60);
    return h > 0 ? `${h}h ${m}m` : `${m}m`;
  };

  const stateIcon = (state: string) => {
    if (state === 'running') return <Loader2 className="size-3.5 text-blue-500 animate-spin" />;
    if (state === 'error') return <CircleAlert className="size-3.5 text-red-500" />;
    return <CircleCheck className="size-3.5 text-green-500" />;
  };

  return (
    <SectionCard icon={<Activity className="size-4" />} title="Agent Loop Status">
      <div className="space-y-1 mb-3">
        {agentMode && (
          <div className="flex justify-between text-xs">
            <span className="text-muted-foreground">Mode</span>
            <Badge variant="outline" className="text-xs">{agentMode}</Badge>
          </div>
        )}
        {uptimeSeconds != null && (
          <div className="flex justify-between text-xs">
            <span className="text-muted-foreground">Uptime</span>
            <span>{formatUptime(uptimeSeconds)}</span>
          </div>
        )}
        {lastHeartbeatAt && (
          <div className="flex justify-between text-xs">
            <span className="text-muted-foreground">Last Heartbeat</span>
            <span>{formatTimeAgo(lastHeartbeatAt)}</span>
          </div>
        )}
      </div>

      <div className="space-y-2">
        {Object.entries(loopStatus).map(([key, loop]) => {
          const meta = LOOP_META[key];
          return (
          <div key={key} className="flex items-center justify-between text-xs">
            <div className="flex items-center gap-2">
              <span className="text-muted-foreground">{meta?.icon ?? <Activity className="size-3.5" />}</span>
              <span className="font-medium">{meta?.label ?? key}</span>
              {stateIcon(loop.state)}
            </div>
            <div className="flex items-center gap-2 text-muted-foreground">
              {loop.lastDurationMs != null && (
                <span>{formatDuration(loop.lastDurationMs)}</span>
              )}
              {loop.lastRunAt && (
                <span>{formatTimeAgo(loop.lastRunAt)}</span>
              )}
              {loop.lastError && (
                <Badge variant="destructive" className="text-[10px] px-1">{loop.lastError.slice(0, 30)}</Badge>
              )}
            </div>
          </div>
          );
        })}
      </div>

      {lastErrorAt && lastErrorMsg && (
        <div className="mt-3 p-2 bg-destructive/10 rounded text-xs">
          <span className="font-medium text-destructive">[{lastErrorPhase}]</span>{' '}
          <span className="text-muted-foreground">{lastErrorMsg.slice(0, 100)}</span>
          <div className="text-[10px] text-muted-foreground mt-1">{formatTimeAgo(lastErrorAt)}</div>
        </div>
      )}
    </SectionCard>
  );
}

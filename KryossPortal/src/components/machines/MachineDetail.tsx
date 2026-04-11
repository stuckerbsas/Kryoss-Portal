import { useState, useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
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
} from 'lucide-react';
import { useMachine, useMachineSoftware, useRunDetail } from '@/api/machines';
import { useTrend } from '@/api/dashboard';
import { GradeBadge } from '@/components/shared/GradeBadge';
import { EmptyState } from '@/components/shared/EmptyState';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import {
  Table,
  TableHeader,
  TableRow,
  TableHead,
  TableBody,
  TableCell,
} from '@/components/ui/table';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';

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

function OverviewTabContent({ machine, chartData }: { machine: any; chartData: any[] | undefined }) {
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
            machine.domainStatus ? <Badge variant="outline" className="text-xs">{machine.domainStatus}</Badge> : null
          } />
          <InfoRow label="Domain" value={machine.domainName} />
        </SectionCard>

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

function SoftwareTabContent({ machineId }: { machineId: string | undefined }) {
  const [search, setSearch] = useState('');
  const { data: softwareData, isLoading } = useMachineSoftware(machineId);

  if (isLoading) return <TabSkeleton rows={10} />;

  if (!softwareData || softwareData.items.length === 0)
    return <EmptyState icon={<Package className="size-10" />} title="No software data" description="Run an assessment to collect software inventory." />;

  const filtered = softwareData.items.filter(
    (s) => !search || s.name.toLowerCase().includes(search.toLowerCase()) || (s.publisher ?? '').toLowerCase().includes(search.toLowerCase()),
  );

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <span className="text-sm text-muted-foreground">{softwareData.total} packages</span>
        <div className="relative w-64">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 size-3.5 text-muted-foreground" />
          <Input placeholder="Search software..." value={search} onChange={(e) => setSearch(e.target.value)} className="pl-9 h-8 text-sm" />
        </div>
      </div>
      <div className="max-h-[600px] overflow-y-auto rounded-md border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Name</TableHead>
              <TableHead>Version</TableHead>
              <TableHead>Publisher</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {filtered.map((s, i) => (
              <TableRow key={i}>
                <TableCell className="font-medium text-sm">{s.name}</TableCell>
                <TableCell className="text-sm text-muted-foreground font-mono">{s.version ?? '—'}</TableCell>
                <TableCell className="text-sm text-muted-foreground">{s.publisher ?? '—'}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
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
      <div className="flex items-center gap-4">
        <div className="flex items-center gap-2">
          <span className="text-2xl font-bold tabular-nums">{score}%</span>
          <span className="text-sm text-muted-foreground">security compliance</span>
        </div>
        <Badge variant="secondary" className="bg-green-100 text-green-800">{passCount} Pass</Badge>
        <Badge variant="secondary" className="bg-amber-100 text-amber-800">{warnCount} Warn</Badge>
        <Badge variant="secondary" className="bg-red-100 text-red-800">{failCount} Fail</Badge>
        <span className="text-sm text-muted-foreground ml-auto">{total} controls</span>
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
              <div className="flex items-center justify-between mb-3">
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
                    <div key={r.controlId} className="flex items-center gap-2 text-sm py-1">
                      <X className="size-3.5 text-red-500 shrink-0" />
                      <span className="font-mono text-xs text-muted-foreground">{r.controlId}</span>
                      <span className="truncate">{r.name}</span>
                      <Badge variant="secondary" className={`ml-auto text-xs ${severityColor(r.severity)}`}>{r.severity}</Badge>
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
    <div className="rounded-md border">
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
        <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
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
      <div className="flex flex-wrap items-center gap-3">
        <Select value={severity} onValueChange={(v) => setSeverity(v)}>
          <SelectTrigger className="w-[130px]"><SelectValue placeholder="Severity" /></SelectTrigger>
          <SelectContent>
            {['All', 'critical', 'high', 'medium', 'low'].map((s) => (
              <SelectItem key={s} value={s}>{s === 'All' ? 'All Severities' : s}</SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Select value={status} onValueChange={(v) => setStatus(v)}>
          <SelectTrigger className="w-[120px]"><SelectValue placeholder="Status" /></SelectTrigger>
          <SelectContent>
            {['All', 'pass', 'warn', 'fail'].map((s) => (
              <SelectItem key={s} value={s}>{s === 'All' ? 'All Statuses' : s}</SelectItem>
            ))}
          </SelectContent>
        </Select>
        <div className="relative max-w-xs flex-1">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 size-4 text-muted-foreground" />
          <Input placeholder="Search controls..." value={search} onChange={(e) => setSearch(e.target.value)} className="pl-9" />
        </div>
        <span className="text-sm text-muted-foreground ml-auto">{filtered.length} results</span>
      </div>

      {/* Results table */}
      <div className="max-h-[600px] overflow-y-auto rounded-md border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>ID</TableHead>
              <TableHead>Name</TableHead>
              <TableHead>Category</TableHead>
              <TableHead>Severity</TableHead>
              <TableHead>Status</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {filtered.map((r: any) => (
              <TableRow key={r.controlId}>
                <TableCell className="font-mono text-xs">{r.controlId}</TableCell>
                <TableCell className="max-w-md truncate text-sm">{r.name}</TableCell>
                <TableCell className="text-muted-foreground text-sm">{r.categoryName}</TableCell>
                <TableCell><Badge variant="secondary" className={severityColor(r.severity)}>{r.severity}</Badge></TableCell>
                <TableCell><Badge variant="secondary" className={statusColor(r.status)}>{r.status}</Badge></TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
    </div>
  );
}

// ── Main Component ──

export function MachineDetail() {
  const { orgId, orgSlug, machineId, machineSlug } = useMachineParam();
  const navigate = useNavigate();

  const { data: machine, isLoading } = useMachine(machineSlug, orgId);
  const { data: trendData } = useTrend({ machineId, months: 6 });

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
          {Array.from({ length: 5 }).map((_, i) => (
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
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-4">
          <Button variant="ghost" size="sm" onClick={() => navigate(`/organizations/${orgSlug}/fleet`)}>
            <ArrowLeft className="size-4 mr-1" />
            Fleet
          </Button>
          <div className="flex items-center gap-3">
            <h2 className="text-xl font-semibold">{machine.hostname}</h2>
            <Badge variant="secondary" className={machine.isActive ? 'bg-green-100 text-green-800 hover:bg-green-100' : 'bg-gray-100 text-gray-500 hover:bg-gray-100'}>
              {machine.isActive ? 'Active' : 'Inactive'}
            </Badge>
            {machine.osName && <span className="text-sm text-muted-foreground">{machine.osName}</span>}
          </div>
        </div>
        {latestRun && (
          <div className="flex items-center gap-3">
            <span className="text-sm text-muted-foreground">Latest:</span>
            <GradeBadge grade={latestRun.grade} score={latestRun.globalScore} />
          </div>
        )}
      </div>

      {/* Tabs */}
      <Tabs defaultValue="overview" className="space-y-4">
        <TabsList>
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
          <TabsTrigger value="history" className="gap-1.5">
            <History className="size-3.5" /> History
          </TabsTrigger>
        </TabsList>

        <TabsContent value="overview">
          <OverviewTabContent machine={machine} chartData={chartData} />
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

        <TabsContent value="history">
          <HistoryTabContent machine={machine} orgSlug={orgSlug} machineSlug={machineSlug} />
        </TabsContent>
      </Tabs>
    </div>
  );
}

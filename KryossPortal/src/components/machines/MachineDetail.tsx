import { useParams, useNavigate } from 'react-router-dom';
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
  ArrowLeft,
} from 'lucide-react';
import { useMachine } from '@/api/machines';
import { useTrend } from '@/api/dashboard';
import { GradeBadge } from '@/components/shared/GradeBadge';
import { EmptyState } from '@/components/shared/EmptyState';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Table,
  TableHeader,
  TableRow,
  TableHead,
  TableBody,
  TableCell,
} from '@/components/ui/table';

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

function InfoCard({
  icon,
  label,
  value,
}: {
  icon: React.ReactNode;
  label: string;
  value: string;
}) {
  return (
    <Card className="p-4">
      <div className="flex items-start gap-3">
        <div className="text-muted-foreground mt-0.5">{icon}</div>
        <div className="min-w-0">
          <p className="text-xs text-muted-foreground">{label}</p>
          <p className="text-sm font-medium truncate">{value}</p>
        </div>
      </div>
    </Card>
  );
}

export function MachineDetail() {
  const { orgId, machineId } = useParams<{
    orgId: string;
    machineId: string;
  }>();
  const navigate = useNavigate();

  const { data: machine, isLoading } = useMachine(machineId);
  const { data: trendData } = useTrend({ machineId, months: 6 });

  if (isLoading) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-8 w-64" />
        <div className="grid grid-cols-2 md:grid-cols-3 gap-4">
          {Array.from({ length: 6 }).map((_, i) => (
            <Skeleton key={i} className="h-20" />
          ))}
        </div>
        <Skeleton className="h-64" />
      </div>
    );
  }

  if (!machine) {
    return (
      <EmptyState title="Machine not found" description="This machine does not exist or you don't have access." />
    );
  }

  const chartData = (
    trendData?.dataPoints as
      | { globalScore: number; startedAt: string }[]
      | undefined
  )?.map((dp) => ({
    date: new Date(dp.startedAt).toLocaleDateString(undefined, {
      month: 'short',
      day: 'numeric',
    }),
    score: dp.globalScore,
  }));

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center gap-4">
        <Button
          variant="ghost"
          size="sm"
          onClick={() => navigate(`/organizations/${orgId}/fleet`)}
        >
          <ArrowLeft className="size-4 mr-1" />
          Fleet
        </Button>
        <div className="flex items-center gap-3">
          <h2 className="text-xl font-semibold">{machine.hostname}</h2>
          <Badge
            variant="secondary"
            className={
              machine.isActive
                ? 'bg-green-100 text-green-800 hover:bg-green-100'
                : 'bg-gray-100 text-gray-500 hover:bg-gray-100'
            }
          >
            {machine.isActive ? 'Active' : 'Inactive'}
          </Badge>
          {machine.osName && (
            <span className="text-sm text-muted-foreground">
              {machine.osName}
            </span>
          )}
        </div>
      </div>

      {/* Hardware Info Grid */}
      <div className="grid grid-cols-2 md:grid-cols-3 gap-4">
        <InfoCard
          icon={<Monitor className="size-4" />}
          label="Operating System"
          value={
            [machine.osName, machine.osVersion, machine.osBuild]
              .filter(Boolean)
              .join(' ') || 'N/A'
          }
        />
        <InfoCard
          icon={<Cpu className="size-4" />}
          label="CPU"
          value={machine.cpuName ?? 'N/A'}
        />
        <InfoCard
          icon={<HardDrive className="size-4" />}
          label="RAM"
          value={machine.ramGb != null ? `${machine.ramGb} GB` : 'N/A'}
        />
        <InfoCard
          icon={<HardDrive className="size-4" />}
          label="Disk Type"
          value={machine.diskType ?? 'N/A'}
        />
        <InfoCard
          icon={<ShieldCheck className="size-4" />}
          label="TPM"
          value={
            machine.tpmPresent == null
              ? 'N/A'
              : machine.tpmPresent
                ? `Present${machine.tpmVersion ? ` (v${machine.tpmVersion})` : ''}`
                : 'Not present'
          }
        />
        <InfoCard
          icon={<ShieldCheck className="size-4" />}
          label="Security"
          value={
            [
              machine.secureBoot != null
                ? `SecureBoot: ${machine.secureBoot ? 'On' : 'Off'}`
                : null,
              machine.bitlocker != null
                ? `BitLocker: ${machine.bitlocker ? 'On' : 'Off'}`
                : null,
            ]
              .filter(Boolean)
              .join(', ') || 'N/A'
          }
        />
      </div>

      {/* Score Trend Chart */}
      {chartData && chartData.length > 0 && (
        <Card className="p-4">
          <h3 className="text-sm font-semibold mb-4">Score Trend (6 months)</h3>
          <ResponsiveContainer width="100%" height={250}>
            <LineChart data={chartData}>
              <CartesianGrid strokeDasharray="3 3" />
              <XAxis dataKey="date" fontSize={12} />
              <YAxis domain={[0, 100]} fontSize={12} />
              <Tooltip />
              <Line
                type="monotone"
                dataKey="score"
                stroke="#008852"
                strokeWidth={2}
                dot={{ r: 3 }}
              />
            </LineChart>
          </ResponsiveContainer>
        </Card>
      )}

      {/* Assessment History */}
      <div>
        <h3 className="text-sm font-semibold mb-3">Assessment History</h3>
        {machine.assessmentHistory.length === 0 ? (
          <EmptyState title="No assessments yet" />
        ) : (
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
              {machine.assessmentHistory.map((run) => (
                <TableRow
                  key={run.id}
                  className="cursor-pointer"
                  onClick={() =>
                    navigate(
                      `/organizations/${orgId}/machines/${machineId}/runs/${run.id}`,
                    )
                  }
                >
                  <TableCell>{formatDate(run.startedAt)}</TableCell>
                  <TableCell>
                    {run.globalScore != null ? run.globalScore : 'N/A'}
                  </TableCell>
                  <TableCell>
                    <GradeBadge grade={run.grade} />
                  </TableCell>
                  <TableCell className="text-green-700">
                    {run.passCount ?? 0}
                  </TableCell>
                  <TableCell className="text-amber-600">
                    {run.warnCount ?? 0}
                  </TableCell>
                  <TableCell className="text-red-600">
                    {run.failCount ?? 0}
                  </TableCell>
                  <TableCell className="text-muted-foreground">
                    {formatDuration(run.durationMs)}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </div>
    </div>
  );
}

import { Monitor, ScanSearch, BarChart3, Award, AlertTriangle, ShieldCheck } from 'lucide-react';
import { useFleetDashboard } from '@/api/dashboard';
import { useOrgParam } from '@/hooks/useOrgParam';
import { EmptyState } from '@/components/shared/EmptyState';
import { GradeBadge } from '@/components/shared/GradeBadge';
import { Badge } from '@/components/ui/badge';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';

const severityColors: Record<string, string> = {
  critical: 'bg-red-100 text-red-800',
  high: 'bg-orange-100 text-orange-800',
  medium: 'bg-amber-100 text-amber-800',
  low: 'bg-blue-100 text-blue-800',
  info: 'bg-gray-100 text-gray-600',
};

export function OverviewTab() {
  const { orgId } = useOrgParam();
  const { data: dashboard, isLoading } = useFleetDashboard(orgId);

  if (isLoading) {
    return (
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {Array.from({ length: 4 }).map((_, i) => (
          <Card key={i}>
            <CardHeader className="pb-2">
              <Skeleton className="h-4 w-24" />
            </CardHeader>
            <CardContent>
              <Skeleton className="h-8 w-16" />
            </CardContent>
          </Card>
        ))}
      </div>
    );
  }

  if (!dashboard || dashboard.totalMachines === 0) {
    return (
      <EmptyState
        icon={<Monitor className="h-12 w-12" />}
        title="No machines enrolled yet"
        description="Go to the Enrollment tab to generate an enrollment code and add your first machine."
      />
    );
  }

  const topGrade = Object.entries(dashboard.gradeDistribution).sort(
    ([, a], [, b]) => b - a,
  )[0]?.[0];

  return (
    <div className="space-y-6">
      {/* KPI cards */}
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard
          icon={<Monitor className="h-4 w-4" />}
          label="Total Machines"
          value={dashboard.totalMachines}
        />
        <StatCard
          icon={<ScanSearch className="h-4 w-4" />}
          label="Assessed"
          value={dashboard.assessedMachines}
        />
        <StatCard
          icon={<BarChart3 className="h-4 w-4" />}
          label="Avg Score"
          value={
            dashboard.avgScore != null
              ? `${Math.round(dashboard.avgScore)}%`
              : 'N/A'
          }
        />
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">
              Grade
            </CardTitle>
            <Award className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent className="flex items-center h-8">
            <span className="text-2xl">
              <GradeBadge grade={topGrade} score={dashboard.avgScore} />
            </span>
          </CardContent>
        </Card>
      </div>

      {/* Framework Compliance KPIs */}
      {dashboard.frameworkScores && dashboard.frameworkScores.length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <ShieldCheck className="h-4 w-4 text-muted-foreground" />
              Framework Compliance
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
              {dashboard.frameworkScores.map((fs) => {
                const scoreColor =
                  fs.avgScore >= 90
                    ? '#008852'
                    : fs.avgScore >= 70
                      ? '#A2C564'
                      : fs.avgScore >= 50
                        ? '#D97706'
                        : '#C0392B';
                return (
                  <div
                    key={fs.code}
                    className="rounded-lg border p-4 space-y-3"
                  >
                    <div className="flex items-center justify-between">
                      <span className="text-sm font-semibold">{fs.code}</span>
                      <span
                        className="text-xl font-bold tabular-nums"
                        style={{ color: scoreColor }}
                      >
                        {fs.avgScore}%
                      </span>
                    </div>
                    {/* Progress bar */}
                    <div className="h-2 bg-gray-100 rounded-full overflow-hidden">
                      <div
                        className="h-full rounded-full transition-all"
                        style={{
                          width: `${fs.avgScore}%`,
                          backgroundColor: scoreColor,
                        }}
                      />
                    </div>
                    <div className="flex items-center justify-between text-xs text-muted-foreground">
                      <div className="flex gap-2">
                        <span className="text-green-700">{fs.totalPass}P</span>
                        <span className="text-amber-600">{fs.totalWarn}W</span>
                        <span className="text-red-600">{fs.totalFail}F</span>
                      </div>
                      <span>{fs.machineCount} machines</span>
                    </div>
                  </div>
                );
              })}
            </div>
          </CardContent>
        </Card>
      )}

      {/* Top failing controls */}
      {dashboard.topFailingControls.length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <AlertTriangle className="h-4 w-4 text-destructive" />
              Top Failing Controls
            </CardTitle>
          </CardHeader>
          <CardContent>
            <ul className="divide-y">
              {dashboard.topFailingControls.map((ctrl) => (
                <li
                  key={ctrl.controlId}
                  className="flex items-center justify-between py-2"
                >
                  <div className="flex items-center gap-3 min-w-0">
                    <span className="font-mono text-xs text-muted-foreground shrink-0">
                      {ctrl.controlId}
                    </span>
                    <span className="text-sm truncate">{ctrl.name}</span>
                  </div>
                  <div className="flex items-center gap-3 shrink-0">
                    <Badge
                      variant="secondary"
                      className={
                        severityColors[ctrl.severity] ??
                        'bg-gray-100 text-gray-600'
                      }
                    >
                      {ctrl.severity}
                    </Badge>
                    <span className="text-sm font-medium text-destructive tabular-nums">
                      {ctrl.failCount} fail{ctrl.failCount !== 1 ? 's' : ''}
                    </span>
                  </div>
                </li>
              ))}
            </ul>
          </CardContent>
        </Card>
      )}
    </div>
  );
}

function StatCard({
  icon,
  label,
  value,
}: {
  icon: React.ReactNode;
  label: string;
  value: string | number;
}) {
  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between pb-2">
        <CardTitle className="text-sm font-medium text-muted-foreground">
          {label}
        </CardTitle>
        {icon}
      </CardHeader>
      <CardContent>
        <div className="text-2xl font-bold">{value}</div>
      </CardContent>
    </Card>
  );
}

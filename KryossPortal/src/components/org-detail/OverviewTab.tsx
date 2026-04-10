import { useParams } from 'react-router-dom';
import { Monitor, ScanSearch, BarChart3, Award, AlertTriangle } from 'lucide-react';
import { useFleetDashboard } from '@/api/dashboard';
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
  const { orgId } = useParams<{ orgId: string }>();
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
          <CardContent>
            <GradeBadge grade={topGrade} score={dashboard.avgScore} />
          </CardContent>
        </Card>
      </div>

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

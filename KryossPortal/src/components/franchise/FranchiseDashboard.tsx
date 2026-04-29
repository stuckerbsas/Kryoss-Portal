import { useNavigate } from 'react-router-dom';
import {
  Building2,
  Monitor,
  ScanSearch,
  BarChart3,
  TrendingUp,
  TrendingDown,
  AlertTriangle,
} from 'lucide-react';
import { useOrgComparison } from '@/api/dashboard';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { GradeBadge } from '@/components/shared/GradeBadge';
import { scoreToGrade } from '@/lib/grading';
import { timeAgo } from '@/lib/dates';
import { slugify } from '@/lib/slugify';
import { cn } from '@/lib/utils';

export function FranchiseDashboard() {
  const { data: orgs, isLoading } = useOrgComparison();
  const navigate = useNavigate();

  if (isLoading) {
    return (
      <div className="space-y-4">
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {Array.from({ length: 4 }).map((_, i) => (
            <Card key={i}>
              <CardHeader className="pb-2"><Skeleton className="h-4 w-24" /></CardHeader>
              <CardContent><Skeleton className="h-8 w-16" /></CardContent>
            </Card>
          ))}
        </div>
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }

  if (!orgs || orgs.length === 0) return null;

  const totalOrgs = orgs.length;
  const totalMachines = orgs.reduce((s, o) => s + o.machineCount, 0);
  const totalAssessed = orgs.reduce((s, o) => s + o.assessedMachines, 0);
  const withScores = orgs.filter((o) => o.avgScore > 0);
  const avgScore = withScores.length > 0
    ? Math.round(withScores.reduce((s, o) => s + o.avgScore, 0) / withScores.length * 10) / 10
    : 0;
  const atRisk = orgs.filter((o) => o.avgScore > 0 && o.avgScore < 70);

  return (
    <div className="space-y-6">
      {/* KPI cards */}
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard icon={<Building2 className="h-4 w-4" />} label="Organizations" value={totalOrgs} />
        <StatCard icon={<Monitor className="h-4 w-4" />} label="Total Machines" value={totalMachines} />
        <StatCard icon={<ScanSearch className="h-4 w-4" />} label="Assessed" value={totalAssessed} />
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Avg Score</CardTitle>
            <BarChart3 className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <div className="flex items-center gap-2">
              <span className="text-2xl font-bold">{avgScore}%</span>
              <GradeBadge grade={scoreToGrade(avgScore)} />
            </div>
          </CardContent>
        </Card>
      </div>

      {/* At-risk alert */}
      {atRisk.length > 0 && (
        <Card className="border-amber-200 bg-amber-50/50">
          <CardHeader className="pb-2">
            <CardTitle className="flex items-center gap-2 text-base text-amber-800">
              <AlertTriangle className="h-4 w-4" />
              {atRisk.length} organization{atRisk.length > 1 ? 's' : ''} below 70% score
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="flex flex-wrap gap-2">
              {atRisk.map((o) => (
                <button
                  key={o.id}
                  onClick={() => navigate(`/organizations/${slugify(o.name)}`)}
                  className="inline-flex items-center gap-1.5 rounded-md border border-amber-200 bg-white px-2.5 py-1 text-sm hover:bg-amber-50 transition-colors"
                >
                  <span className="font-medium">{o.name}</span>
                  <span className="text-amber-700 font-mono text-xs">{o.avgScore}%</span>
                </button>
              ))}
            </div>
          </CardContent>
        </Card>
      )}

      {/* Org comparison table */}
      <Card>
        <CardHeader>
          <CardTitle className="text-base">Organization Comparison</CardTitle>
        </CardHeader>
        <CardContent className="p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b bg-muted/30">
                  <th className="text-left px-4 py-2.5 font-medium text-muted-foreground">Organization</th>
                  <th className="text-right px-4 py-2.5 font-medium text-muted-foreground">Machines</th>
                  <th className="text-right px-4 py-2.5 font-medium text-muted-foreground">Assessed</th>
                  <th className="text-center px-4 py-2.5 font-medium text-muted-foreground">Score</th>
                  <th className="text-center px-4 py-2.5 font-medium text-muted-foreground">Grade</th>
                  <th className="text-right px-4 py-2.5 font-medium text-muted-foreground">Last Scan</th>
                </tr>
              </thead>
              <tbody>
                {orgs.map((o) => {
                  const scoreColor =
                    o.avgScore >= 90 ? 'text-green-700'
                    : o.avgScore >= 70 ? 'text-lime-700'
                    : o.avgScore >= 50 ? 'text-amber-700'
                    : o.avgScore > 0 ? 'text-red-700'
                    : 'text-muted-foreground';
                  return (
                    <tr
                      key={o.id}
                      className="border-b last:border-0 hover:bg-muted/20 cursor-pointer transition-colors"
                      onClick={() => navigate(`/organizations/${slugify(o.name)}`)}
                    >
                      <td className="px-4 py-3 font-medium">{o.name}</td>
                      <td className="px-4 py-3 text-right tabular-nums">{o.machineCount}</td>
                      <td className="px-4 py-3 text-right tabular-nums">{o.assessedMachines}</td>
                      <td className={cn('px-4 py-3 text-center tabular-nums font-semibold', scoreColor)}>
                        {o.avgScore > 0 ? `${o.avgScore}%` : '—'}
                      </td>
                      <td className="px-4 py-3 text-center">
                        <GradeBadge grade={scoreToGrade(o.avgScore)} />
                      </td>
                      <td className="px-4 py-3 text-right text-muted-foreground">
                        {timeAgo(o.lastAssessment)}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}

function StatCard({ icon, label, value }: { icon: React.ReactNode; label: string; value: number }) {
  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between pb-2">
        <CardTitle className="text-sm font-medium text-muted-foreground">{label}</CardTitle>
        {icon}
      </CardHeader>
      <CardContent>
        <div className="text-2xl font-bold">{value}</div>
      </CardContent>
    </Card>
  );
}

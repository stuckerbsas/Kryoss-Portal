import { Monitor, ShieldAlert, ShieldCheck, TrendingUp, Bug, Wrench } from 'lucide-react';
import {
  ResponsiveContainer,
  AreaChart,
  Area,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
} from 'recharts';
import { useFleetDashboard, useTrend } from '@/api/dashboard';
import { useCveFindings, useCveStats } from '@/api/cveFindings';
import { useOrgParam } from '@/hooks/useOrgParam';
import { EmptyState } from '@/components/shared/EmptyState';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { scoreToGrade } from '@/lib/grading';

function scoreColor(score: number | null): string {
  if (score == null) return '#94a3b8';
  if (score >= 90) return '#008852';
  if (score >= 70) return '#A2C564';
  if (score >= 50) return '#D97706';
  return '#C0392B';
}

export function OverviewTab() {
  const { orgId } = useOrgParam();
  const { data: dashboard, isLoading } = useFleetDashboard(orgId);
  const { data: trendData } = useTrend({ organizationId: orgId, months: 6 });
  const { data: cveData } = useCveFindings(orgId);
  const { data: cveStats } = useCveStats(orgId);

  if (isLoading) {
    return (
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {Array.from({ length: 4 }).map((_, i) => (
          <Card key={i}>
            <CardHeader className="pb-2"><Skeleton className="h-4 w-24" /></CardHeader>
            <CardContent><Skeleton className="h-8 w-16" /></CardContent>
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

  const avgScore = dashboard.avgScore ?? 0;
  const grade = scoreToGrade(avgScore);
  const pending = dashboard.totalMachines - dashboard.assessedMachines;
  const totalChecks = dashboard.totalPass + dashboard.totalWarn + dashboard.totalFail;
  const cveCount = cveData?.totalFindings ?? 0;

  const chartData = (trendData?.dataPoints as { globalScore: number; startedAt: string }[] | undefined)?.map((dp) => ({
    date: new Date(dp.startedAt).toLocaleDateString(undefined, { month: 'short', day: 'numeric' }),
    score: dp.globalScore,
  }));

  const topSoftware = cveStats?.topSoftware?.slice(0, 5) ?? [];

  return (
    <div className="space-y-6">
      {/* ── KPIs ── */}
      <div className="grid gap-4 grid-cols-1 sm:grid-cols-2 lg:grid-cols-4">
        <KpiCard
          icon={<Monitor className="h-5 w-5" />}
          label="Machines"
          value={dashboard.totalMachines}
          sub={
            <span className="text-xs text-muted-foreground">
              {dashboard.assessedMachines} assessed
              {pending > 0 && <> · {pending} pending</>}
            </span>
          }
        />
        <KpiCard
          icon={<ShieldAlert className="h-5 w-5" />}
          label="Health Score"
          value={dashboard.avgScore != null ? `${Math.round(avgScore)}%` : 'N/A'}
          sub={
            grade ? (
              <span className="text-xs font-semibold" style={{ color: scoreColor(avgScore) }}>
                Grade {grade}
              </span>
            ) : undefined
          }
        />
        <KpiCard
          icon={<Bug className="h-5 w-5" />}
          label="Vulnerabilities"
          value={cveCount}
          sub={
            cveData && cveData.uniqueCves > 0 ? (
              <span className="text-xs text-muted-foreground">
                {cveData.uniqueCves} unique · {cveData.affectedMachines} machines
              </span>
            ) : undefined
          }
        />
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground uppercase">
              Checks
            </CardTitle>
            <ShieldCheck className="h-5 w-5" />
          </CardHeader>
          <CardContent>
            {totalChecks > 0 ? (
              <div className="flex items-center justify-center gap-5">
                <DonutChart
                  pass={dashboard.totalPass}
                  warn={dashboard.totalWarn}
                  fail={dashboard.totalFail}
                />
                <div className="flex flex-col gap-1 text-xs">
                  <span className="flex items-center gap-1.5">
                    <span className="h-2 w-2 rounded-full bg-[#008852]" />
                    {Math.round((dashboard.totalPass / totalChecks) * 100)}% Pass
                  </span>
                  <span className="flex items-center gap-1.5">
                    <span className="h-2 w-2 rounded-full bg-[#D97706]" />
                    {Math.round((dashboard.totalWarn / totalChecks) * 100)}% Warn
                  </span>
                  <span className="flex items-center gap-1.5">
                    <span className="h-2 w-2 rounded-full bg-[#C0392B]" />
                    {Math.round((dashboard.totalFail / totalChecks) * 100)}% Fail
                  </span>
                </div>
              </div>
            ) : (
              <div className="text-2xl font-bold">0</div>
            )}
          </CardContent>
        </Card>
      </div>

      {/* ── Framework Compliance ── */}
      {dashboard.frameworkScores && dashboard.frameworkScores.length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <ShieldCheck className="h-4 w-4 text-muted-foreground" />
              Framework Compliance
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="grid gap-4 grid-cols-1 sm:grid-cols-3 lg:grid-cols-[repeat(auto-fit,minmax(180px,1fr))]">
              {dashboard.frameworkScores.map((fs) => {
                const color = scoreColor(fs.avgScore);
                return (
                  <div key={fs.code} className="rounded-lg border p-4 space-y-3">
                    <div className="flex items-center justify-between">
                      <span className="text-sm font-semibold">{fs.code}</span>
                      <span className="text-xl font-bold tabular-nums" style={{ color }}>
                        {fs.avgScore}%
                      </span>
                    </div>
                    <div className="h-2 bg-gray-100 rounded-full overflow-hidden">
                      <div
                        className="h-full rounded-full transition-all"
                        style={{ width: `${fs.avgScore}%`, backgroundColor: color }}
                      />
                    </div>
                    <div className="flex items-center justify-between text-xs text-muted-foreground">
                      <div className="flex gap-2">
                        {(() => {
                          const t = fs.totalPass + fs.totalWarn + fs.totalFail;
                          if (t === 0) return null;
                          return (
                            <>
                              <span className="text-[#008852]">{Math.round((fs.totalPass / t) * 100)}% P</span>
                              <span className="text-[#D97706]">{Math.round((fs.totalWarn / t) * 100)}% W</span>
                              <span className="text-[#C0392B]">{Math.round((fs.totalFail / t) * 100)}% F</span>
                            </>
                          );
                        })()}
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

      {/* ── Trend + Grade Distribution ── */}
      <div className="grid gap-4 grid-cols-1 lg:grid-cols-[2fr_1fr]">
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <TrendingUp className="h-4 w-4 text-muted-foreground" />
              Score Trend
            </CardTitle>
          </CardHeader>
          <CardContent>
            {chartData && chartData.length >= 2 ? (
              <div className="h-52">
                <ResponsiveContainer width="100%" height="100%">
                  <AreaChart data={chartData} margin={{ top: 5, right: 10, bottom: 0, left: -20 }}>
                    <defs>
                      <linearGradient id="scoreFill" x1="0" y1="0" x2="0" y2="1">
                        <stop offset="0%" stopColor="#008852" stopOpacity={0.2} />
                        <stop offset="100%" stopColor="#008852" stopOpacity={0} />
                      </linearGradient>
                    </defs>
                    <CartesianGrid strokeDasharray="3 3" vertical={false} />
                    <XAxis dataKey="date" tick={{ fontSize: 11 }} />
                    <YAxis domain={[0, 100]} tick={{ fontSize: 11 }} />
                    <Tooltip
                      formatter={(v: number) => [`${v}%`, 'Score']}
                      contentStyle={{ fontSize: 12 }}
                    />
                    <Area
                      type="monotone"
                      dataKey="score"
                      stroke="#008852"
                      strokeWidth={2}
                      fill="url(#scoreFill)"
                    />
                  </AreaChart>
                </ResponsiveContainer>
              </div>
            ) : (
              <p className="text-sm text-muted-foreground py-8 text-center">
                Not enough data yet — at least 2 assessments needed.
              </p>
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <Wrench className="h-4 w-4 text-muted-foreground" />
              Remediation Priorities
            </CardTitle>
          </CardHeader>
          <CardContent>
            {topSoftware.length > 0 ? (
              <ul className="space-y-3">
                {topSoftware.map((sw, idx) => (
                  <li key={sw.softwareName} className="flex items-start gap-3">
                    <span className="text-sm font-bold text-muted-foreground shrink-0 w-4 text-right">
                      {idx + 1}
                    </span>
                    <div className="min-w-0">
                      <p className="text-sm font-medium truncate">{sw.softwareName}</p>
                      <p className="text-xs text-muted-foreground">
                        {sw.machineCount} machine{sw.machineCount !== 1 ? 's' : ''}
                        {sw.maxCvss != null && <> · CVSS {sw.maxCvss.toFixed(1)}</>}
                      </p>
                    </div>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="text-sm text-muted-foreground py-8 text-center">
                No vulnerabilities detected.
              </p>
            )}
          </CardContent>
        </Card>
      </div>
    </div>
  );
}

function DonutChart({ pass, warn, fail }: { pass: number; warn: number; fail: number }) {
  const total = pass + warn + fail;
  if (total === 0) return null;
  const r = 38;
  const c = 2 * Math.PI * r;
  const pPass = (pass / total) * c;
  const pWarn = (warn / total) * c;
  const pFail = (fail / total) * c;
  return (
    <svg width="100" height="100" viewBox="0 0 100 100" className="shrink-0">
      <circle cx="50" cy="50" r={r} fill="none" stroke="#008852" strokeWidth="18"
        strokeDasharray={`${pPass} ${c - pPass}`}
        strokeDashoffset={0}
        transform="rotate(-90 50 50)" />
      <circle cx="50" cy="50" r={r} fill="none" stroke="#D97706" strokeWidth="18"
        strokeDasharray={`${pWarn} ${c - pWarn}`}
        strokeDashoffset={-pPass}
        transform="rotate(-90 50 50)" />
      <circle cx="50" cy="50" r={r} fill="none" stroke="#C0392B" strokeWidth="18"
        strokeDasharray={`${pFail} ${c - pFail}`}
        strokeDashoffset={-(pPass + pWarn)}
        transform="rotate(-90 50 50)" />
    </svg>
  );
}

function KpiCard({
  icon,
  label,
  value,
  sub,
}: {
  icon: React.ReactNode;
  label: string;
  value: string | number;
  sub?: React.ReactNode;
}) {
  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between pb-2">
        <CardTitle className="text-sm font-medium text-muted-foreground uppercase">
          {label}
        </CardTitle>
        {icon}
      </CardHeader>
      <CardContent>
        <div className="text-2xl font-bold">{value}</div>
        {sub && <div className="mt-1">{sub}</div>}
      </CardContent>
    </Card>
  );
}

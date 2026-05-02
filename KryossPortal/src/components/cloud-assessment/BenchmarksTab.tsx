import { useState } from 'react';
import {
  BarChart3,
  Users,
  Building2,
  Globe,
  Info,
  Trophy,
  Medal,
  ChevronDown,
  ChevronUp,
} from 'lucide-react';
import {
  useBenchmarkReport,
  useFranchiseLeaderboard,
  type MetricComparison,
} from '@/api/cloudAssessment';
import { useMe } from '@/api/me';
import { Badge } from '@/components/ui/badge';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { Button } from '@/components/ui/button';
import {
  RadarChart,
  Radar,
  PolarGrid,
  PolarAngleAxis,
  PolarRadiusAxis,
  ResponsiveContainer,
  Legend,
  Tooltip,
} from 'recharts';
import { BenchmarkMetricRow } from './BenchmarkMetricRow';
import { IndustryPicker } from './IndustryPicker';

const CATEGORY_ORDER: Array<{
  key: 'overall' | 'area' | 'framework' | 'metric';
  label: string;
}> = [
  { key: 'overall', label: 'Overall' },
  { key: 'area', label: 'Areas' },
  { key: 'framework', label: 'Compliance frameworks' },
  { key: 'metric', label: 'Operational metrics' },
];

const AREA_ORDER = ['identity', 'endpoint', 'data', 'productivity', 'azure', 'compliance', 'powerbi'];

function areaLabel(key: string): string {
  return (
    {
      identity: 'Identity',
      endpoint: 'Endpoint',
      data: 'Data',
      productivity: 'Productivity',
      azure: 'Azure',
      compliance: 'Compliance',
      powerbi: 'Power BI',
    } as Record<string, string>
  )[key] ?? key;
}

function radarDataFromReport(metrics: MetricComparison[]) {
  const areaRows = metrics.filter(m => m.category === 'area');
  const byKey = new Map<string, MetricComparison>();
  for (const m of areaRows) byKey.set(m.metricKey, m);

  return AREA_ORDER.map(code => {
    const key = `area.${code}`;
    const m = byKey.get(key);
    return {
      area: areaLabel(code),
      org: m?.orgValue ?? 0,
      franchise: m?.franchiseAvg ?? null,
      industry: m?.industryP50 ?? null,
    };
  }).filter(r => r.org > 0 || r.franchise != null || r.industry != null);
}

interface BenchmarksTabProps {
  orgId: string;
  scanId: string | undefined;
}

export function BenchmarksTab({ orgId, scanId }: BenchmarksTabProps) {
  const { data: me } = useMe();
  const { data: report, isLoading } = useBenchmarkReport(scanId);
  const [expandedFrameworks, setExpandedFrameworks] = useState(false);
  const [expandedMetrics, setExpandedMetrics] = useState(false);

  if (!scanId) {
    return (
      <Card>
        <CardContent className="py-10 text-center text-sm text-muted-foreground">
          Run a Cloud Assessment scan to see benchmarks.
        </CardContent>
      </Card>
    );
  }

  if (isLoading) {
    return <Skeleton className="h-96 w-full" />;
  }

  if (!report) {
    return (
      <Card>
        <CardContent className="py-10 text-center text-sm text-muted-foreground">
          No benchmark data available for this scan.
        </CardContent>
      </Card>
    );
  }

  const { metrics, availability } = report;
  const anyAvailable =
    availability.franchiseBenchmarkAvailable ||
    availability.industryBenchmarkAvailable ||
    availability.globalBenchmarkAvailable;

  const radarRows = radarDataFromReport(metrics);
  const franchiseId = me?.franchise?.id;

  return (
    <div className="space-y-6">
      {/* Header */}
      <div>
        <h3 className="text-lg font-semibold flex items-center gap-2">
          <BarChart3 className="h-5 w-5" />
          Benchmarks
        </h3>
        <p className="text-sm text-muted-foreground mt-0.5">
          Compare this organization against franchise peers, industry baselines, and the full Kryoss dataset.
        </p>
      </div>

      {/* Availability banners */}
      <div className="grid gap-3 sm:grid-cols-3">
        <AvailabilityCard
          icon={<Building2 className="h-4 w-4" />}
          label="Franchise peers"
          available={availability.franchiseBenchmarkAvailable}
          detail={
            availability.franchiseBenchmarkAvailable
              ? `${availability.franchiseOrgCount} orgs`
              : `${availability.franchiseOrgCount} / ${availability.franchiseThreshold} orgs`
          }
          subline={
            availability.franchiseBenchmarkAvailable
              ? 'Ready'
              : `Need ${Math.max(0, availability.franchiseThreshold - availability.franchiseOrgCount)} more org${availability.franchiseThreshold - availability.franchiseOrgCount === 1 ? '' : 's'}`
          }
        />
        <AvailabilityCard
          icon={<Users className="h-4 w-4" />}
          label="Industry"
          available={availability.industryBenchmarkAvailable}
          detail={availability.industryCode ?? 'Not set'}
          subline={
            availability.industryBenchmarkAvailable
              ? 'Baseline loaded'
              : 'Set industry to enable'
          }
        />
        <AvailabilityCard
          icon={<Globe className="h-4 w-4" />}
          label="Global Kryoss"
          available={availability.globalBenchmarkAvailable}
          detail={
            availability.globalBenchmarkAvailable
              ? `${availability.globalOrgCount} orgs`
              : `${availability.globalOrgCount} / ${availability.globalThreshold}`
          }
          subline={availability.globalBenchmarkAvailable ? 'Active' : 'Dataset growing'}
        />
      </div>

      {/* Industry picker if not set */}
      {!availability.industryCode && (
        <div className="rounded-lg border border-amber-200 bg-amber-50 p-4">
          <div className="flex items-start gap-3 mb-3">
            <Info className="h-5 w-5 text-amber-600 shrink-0 mt-0.5" />
            <div>
              <p className="text-sm font-medium text-amber-900">Industry not set</p>
              <p className="text-xs text-amber-800 mt-0.5">
                Pick an industry and size to unlock the industry benchmark on the next scan.
              </p>
            </div>
          </div>
          <IndustryPicker orgId={orgId} compact />
        </div>
      )}

      {/* Radar */}
      {radarRows.length > 0 && anyAvailable && (
        <Card>
          <CardHeader>
            <CardTitle className="text-sm">Area comparison (0–5 scale)</CardTitle>
          </CardHeader>
          <CardContent>
            <ResponsiveContainer width="100%" height={340}>
              <RadarChart data={radarRows}>
                <PolarGrid />
                <PolarAngleAxis dataKey="area" tick={{ fontSize: 12 }} />
                <PolarRadiusAxis angle={90} domain={[0, 5]} tick={{ fontSize: 10 }} />
                <Radar
                  name="Your org"
                  dataKey="org"
                  stroke="#008852"
                  fill="#008852"
                  fillOpacity={0.4}
                />
                {availability.franchiseBenchmarkAvailable && (
                  <Radar
                    name="Franchise avg"
                    dataKey="franchise"
                    stroke="#2563eb"
                    fill="#2563eb"
                    fillOpacity={0.15}
                  />
                )}
                {availability.industryBenchmarkAvailable && (
                  <Radar
                    name="Industry median"
                    dataKey="industry"
                    stroke="#d97706"
                    fill="#d97706"
                    fillOpacity={0.1}
                  />
                )}
                <Tooltip />
                <Legend />
              </RadarChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>
      )}

      {/* Metric tables, grouped by category */}
      {CATEGORY_ORDER.map(cat => {
        const rows = metrics.filter(m => m.category === cat.key);
        if (rows.length === 0) return null;

        const collapsible = cat.key === 'framework' || cat.key === 'metric';
        const expanded =
          cat.key === 'framework' ? expandedFrameworks :
          cat.key === 'metric' ? expandedMetrics :
          true;
        const visible = collapsible && !expanded ? rows.slice(0, 4) : rows;

        return (
          <Card key={cat.key}>
            <CardHeader className="flex flex-row items-center justify-between pb-2">
              <CardTitle className="text-sm">{cat.label}</CardTitle>
              {collapsible && rows.length > 4 && (
                <Button
                  variant="ghost"
                  size="sm"
                  className="h-7 text-xs"
                  onClick={() => {
                    if (cat.key === 'framework') setExpandedFrameworks(e => !e);
                    else setExpandedMetrics(e => !e);
                  }}
                >
                  {expanded ? (
                    <>
                      <ChevronUp className="h-3.5 w-3.5 mr-1" />
                      Collapse
                    </>
                  ) : (
                    <>
                      <ChevronDown className="h-3.5 w-3.5 mr-1" />
                      Show all ({rows.length})
                    </>
                  )}
                </Button>
              )}
            </CardHeader>
            <CardContent className="p-0">
              {/* Mobile cards */}
              <div className="space-y-3 p-4 sm:hidden">
                {visible.map(m => (
                  <div key={m.metricKey} className="rounded-lg border p-4 space-y-1">
                    <div className="flex items-center justify-between gap-2">
                      <span className="font-medium text-sm truncate">{m.displayName ?? m.metricKey}</span>
                      {m.verdict && <Badge variant="secondary" className="text-xs">{m.verdict}</Badge>}
                    </div>
                    <div className="flex flex-wrap gap-3 text-xs text-muted-foreground">
                      <span>You: <span className="font-semibold text-foreground">{m.orgValue?.toFixed(1) ?? '—'}</span></span>
                      {availability.franchiseBenchmarkAvailable && <span>Franchise: {m.franchiseAvg?.toFixed(1) ?? '—'}</span>}
                      {availability.industryBenchmarkAvailable && <span>Industry: {m.industryP50?.toFixed(1) ?? '—'}</span>}
                    </div>
                  </div>
                ))}
              </div>
              {/* Desktop table */}
              <div className="hidden sm:block overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b text-xs text-muted-foreground">
                    <th className="text-left font-medium pl-3 pr-2 py-2">Metric</th>
                    <th className="text-right font-medium px-2 py-2">You</th>
                    <th className="text-right font-medium px-2 py-2">Franchise</th>
                    <th className="text-right font-medium px-2 py-2">Industry</th>
                    <th className="text-right font-medium px-2 py-2">Global</th>
                    <th className="text-left font-medium pl-2 pr-3 py-2">Verdict</th>
                  </tr>
                </thead>
                <tbody>
                  {visible.map(m => (
                    <BenchmarkMetricRow
                      key={m.metricKey}
                      metric={m}
                      franchiseAvailable={availability.franchiseBenchmarkAvailable}
                      industryAvailable={availability.industryBenchmarkAvailable}
                      globalAvailable={availability.globalBenchmarkAvailable}
                    />
                  ))}
                </tbody>
              </table>
              </div>
            </CardContent>
          </Card>
        );
      })}

      {/* Industry editor (always visible, for drift) */}
      {availability.industryCode && (
        <IndustryPicker
          orgId={orgId}
          currentIndustryCode={availability.industryCode}
        />
      )}

      {/* Franchise leaderboard */}
      {franchiseId && <FranchiseLeaderboard franchiseId={franchiseId} />}

      {/* Privacy note */}
      <p className="text-xs text-muted-foreground flex items-start gap-1.5">
        <Info className="h-3.5 w-3.5 shrink-0 mt-0.5" />
        Franchise benchmarks activate with {availability.franchiseThreshold}+ orgs; global benchmarks with {availability.globalThreshold}+.
        Per-org numbers are never revealed outside the franchise — only aggregates.
      </p>
    </div>
  );
}

function AvailabilityCard({
  icon,
  label,
  available,
  detail,
  subline,
}: {
  icon: React.ReactNode;
  label: string;
  available: boolean;
  detail: string;
  subline: string;
}) {
  return (
    <Card className={available ? 'border-green-200' : 'border-gray-200'}>
      <CardContent className="py-4 px-4">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
            {icon}
            <span>{label}</span>
          </div>
          <span
            className={`text-xs px-1.5 py-0.5 rounded ${
              available ? 'bg-green-100 text-green-800' : 'bg-gray-100 text-gray-600'
            }`}
          >
            {available ? 'On' : 'Off'}
          </span>
        </div>
        <div className="mt-1 text-lg font-semibold">{detail}</div>
        <div className="text-xs text-muted-foreground">{subline}</div>
      </CardContent>
    </Card>
  );
}

function FranchiseLeaderboard({ franchiseId }: { franchiseId: string }) {
  const { data, isLoading } = useFranchiseLeaderboard(franchiseId);

  if (isLoading) return <Skeleton className="h-48" />;
  if (!data || !data.available || data.rows.length === 0) return null;

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-sm flex items-center gap-2">
          <Trophy className="h-4 w-4 text-amber-500" />
          Franchise leaderboard
          <span className="text-xs text-muted-foreground font-normal">
            ({data.orgCount} orgs)
          </span>
        </CardTitle>
      </CardHeader>
      <CardContent className="p-0">
        {/* Mobile cards */}
        <div className="space-y-3 p-4 sm:hidden">
          {data.rows.map((r, i) => (
            <div key={r.organizationId} className="rounded-lg border p-4 space-y-1">
              <div className="flex items-center justify-between gap-2">
                <span className="font-medium text-sm truncate flex items-center gap-1.5">
                  {i === 0 ? <Medal className="h-4 w-4 text-amber-500" /> : <span className="text-muted-foreground">#{i + 1}</span>}
                  {r.organizationName}
                </span>
                <span className="font-semibold text-sm tabular-nums">{r.overallScore !== null ? r.overallScore.toFixed(1) : '—'}</span>
              </div>
              <div className="flex gap-3 text-xs text-muted-foreground">
                {r.topArea && <span>Top: {areaLabel(r.topArea.replace('area.', ''))}</span>}
                {r.weakestArea && <span>Weak: {areaLabel(r.weakestArea.replace('area.', ''))}</span>}
              </div>
            </div>
          ))}
        </div>
        {/* Desktop table */}
        <div className="hidden sm:block overflow-x-auto">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b text-xs text-muted-foreground">
              <th className="text-left font-medium pl-3 pr-2 py-2 w-10">#</th>
              <th className="text-left font-medium px-2 py-2">Organization</th>
              <th className="text-right font-medium px-2 py-2">Score</th>
              <th className="text-left font-medium px-2 py-2">Top area</th>
              <th className="text-left font-medium pl-2 pr-3 py-2">Weakest</th>
            </tr>
          </thead>
          <tbody>
            {data.rows.map((r, i) => (
              <tr key={r.organizationId} className="border-b text-sm hover:bg-gray-50">
                <td className="pl-3 pr-2 py-2 text-muted-foreground">
                  {i === 0 ? (
                    <Medal className="h-4 w-4 text-amber-500 inline" />
                  ) : (
                    i + 1
                  )}
                </td>
                <td className="px-2 py-2 font-medium">{r.organizationName}</td>
                <td className="px-2 py-2 text-right tabular-nums">
                  {r.overallScore !== null ? r.overallScore.toFixed(1) : '—'}
                </td>
                <td className="px-2 py-2 text-muted-foreground">
                  {r.topArea ? (
                    <span>
                      {areaLabel(r.topArea.replace('area.', ''))}{' '}
                      <span className="text-xs">
                        ({r.topAreaScore?.toFixed(1) ?? '—'})
                      </span>
                    </span>
                  ) : (
                    '—'
                  )}
                </td>
                <td className="pl-2 pr-3 py-2 text-muted-foreground">
                  {r.weakestArea ? (
                    <span>
                      {areaLabel(r.weakestArea.replace('area.', ''))}{' '}
                      <span className="text-xs">
                        ({r.weakestAreaScore?.toFixed(1) ?? '—'})
                      </span>
                    </span>
                  ) : (
                    '—'
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        </div>
      </CardContent>
    </Card>
  );
}

import type { ReactNode } from 'react';
import { TrendingUp, TrendingDown, Minus, HelpCircle } from 'lucide-react';
import type { MetricComparison } from '@/api/cloudAssessment';

function fmtNumber(n: number | null, isCount = false): string {
  if (n === null || n === undefined) return '—';
  if (isCount) return Math.round(n).toString();
  return n.toFixed(1);
}

function fmtPercentile(p: number | null): string {
  if (p === null || p === undefined) return '—';
  return `P${Math.round(p)}`;
}

function verdictPill(verdict: string) {
  const map: Record<string, { cls: string; label: string; icon: ReactNode }> = {
    above_peer: {
      cls: 'bg-green-100 text-green-800',
      label: 'Above peer',
      icon: <TrendingUp className="h-3 w-3" />,
    },
    at_peer: {
      cls: 'bg-blue-100 text-blue-800',
      label: 'At peer',
      icon: <Minus className="h-3 w-3" />,
    },
    below_peer: {
      cls: 'bg-red-100 text-red-800',
      label: 'Below peer',
      icon: <TrendingDown className="h-3 w-3" />,
    },
    insufficient_data: {
      cls: 'bg-gray-100 text-gray-600',
      label: 'Insufficient data',
      icon: <HelpCircle className="h-3 w-3" />,
    },
  };
  const v = map[verdict] ?? map.insufficient_data;
  return (
    <span className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium ${v.cls}`}>
      {v.icon}
      {v.label}
    </span>
  );
}

export function BenchmarkMetricRow({
  metric,
  industryAvailable,
  globalAvailable,
  franchiseAvailable,
}: {
  metric: MetricComparison;
  industryAvailable: boolean;
  globalAvailable: boolean;
  franchiseAvailable: boolean;
}) {
  const isCount = metric.metricKey.endsWith('_count');
  const isArea = metric.category === 'area' || metric.category === 'overall';

  return (
    <tr className="border-b text-sm hover:bg-gray-50">
      <td className="py-2.5 pl-3 pr-2 font-medium">{metric.displayName}</td>
      <td className="py-2.5 px-2 text-right tabular-nums">
        <span className="font-semibold">{fmtNumber(metric.orgValue, isCount)}</span>
        <span className="text-xs text-muted-foreground ml-1">
          {isArea ? '/5' : isCount ? '' : '%'}
        </span>
      </td>
      <td className="py-2.5 px-2 text-right tabular-nums text-muted-foreground">
        {!franchiseAvailable ? (
          <span className="text-xs italic">—</span>
        ) : (
          <>
            <span>{fmtNumber(metric.franchiseAvg, isCount)}</span>
            <span className="text-xs ml-1 opacity-70">{fmtPercentile(metric.franchisePercentile)}</span>
          </>
        )}
      </td>
      <td className="py-2.5 px-2 text-right tabular-nums text-muted-foreground">
        {!industryAvailable ? (
          <span className="text-xs italic">—</span>
        ) : (
          <>
            <span>{fmtNumber(metric.industryP50, isCount)}</span>
            <span className="text-xs ml-1 opacity-70">{fmtPercentile(metric.industryPercentile)}</span>
          </>
        )}
      </td>
      <td className="py-2.5 px-2 text-right tabular-nums text-muted-foreground">
        {!globalAvailable ? (
          <span className="text-xs italic">—</span>
        ) : (
          <>
            <span>{fmtNumber(metric.globalAvg, isCount)}</span>
            <span className="text-xs ml-1 opacity-70">{fmtPercentile(metric.globalPercentile)}</span>
          </>
        )}
      </td>
      <td className="py-2.5 pl-2 pr-3">{verdictPill(metric.verdict)}</td>
    </tr>
  );
}

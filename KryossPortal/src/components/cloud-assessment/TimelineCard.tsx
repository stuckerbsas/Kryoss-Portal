import { useCloudAssessmentHistory } from '@/api/cloudAssessment';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import {
  LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, Legend,
} from 'recharts';
import { TrendingUp } from 'lucide-react';

interface TimelineCardProps {
  organizationId: string;
  height?: number;
  dimensions?: Array<'overall' | 'identity' | 'endpoint' | 'data' | 'productivity'>;
}

const LINE_COLORS: Record<string, string> = {
  overall: '#008852',
  identity: '#3b82f6',
  endpoint: '#8b5cf6',
  data: '#f59e0b',
  productivity: '#ec4899',
};

const LINE_LABELS: Record<string, string> = {
  overall: 'Overall',
  identity: 'Identity',
  endpoint: 'Endpoint',
  data: 'Data',
  productivity: 'Productivity',
};

export function TimelineCard({
  organizationId,
  height = 260,
  dimensions = ['overall', 'identity', 'endpoint', 'data', 'productivity'],
}: TimelineCardProps) {
  const { data: history, isLoading } = useCloudAssessmentHistory(organizationId, 20);

  if (isLoading) {
    return (
      <Card>
        <CardHeader><CardTitle className="text-sm">Cloud Posture Trend</CardTitle></CardHeader>
        <CardContent><div className="h-64 bg-muted/40 rounded animate-pulse" /></CardContent>
      </Card>
    );
  }

  if (!history || history.length < 2) {
    return (
      <Card>
        <CardHeader>
          <CardTitle className="text-sm flex items-center gap-2">
            <TrendingUp className="h-4 w-4" /> Cloud Posture Trend
          </CardTitle>
        </CardHeader>
        <CardContent>
          <p className="text-sm text-muted-foreground py-6 text-center">
            Run at least two scans to see the trend.
          </p>
        </CardContent>
      </Card>
    );
  }

  // Oldest-first for chart readability.
  const chartData = [...history].reverse().map((h) => ({
    date: new Date(h.createdAt).toLocaleDateString(undefined, { month: 'short', day: 'numeric' }),
    overall: h.overallScore ?? 0,
    identity: h.areaScores?.identity ?? 0,
    endpoint: h.areaScores?.endpoint ?? 0,
    data: h.areaScores?.data ?? 0,
    productivity: h.areaScores?.productivity ?? 0,
    verdict: h.verdict,
  }));

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-sm flex items-center gap-2">
          <TrendingUp className="h-4 w-4" /> Cloud Posture Trend
        </CardTitle>
      </CardHeader>
      <CardContent>
        <ResponsiveContainer width="100%" height={height}>
          <LineChart data={chartData} margin={{ top: 5, right: 8, left: -20, bottom: 0 }}>
            <CartesianGrid strokeDasharray="3 3" stroke="#e5e7eb" />
            <XAxis dataKey="date" tick={{ fontSize: 11 }} />
            <YAxis domain={[0, 5]} tick={{ fontSize: 11 }} />
            <Tooltip />
            <Legend wrapperStyle={{ fontSize: 12 }} />
            {dimensions.map((dim) => (
              <Line
                key={dim}
                type="monotone"
                dataKey={dim}
                name={LINE_LABELS[dim]}
                stroke={LINE_COLORS[dim]}
                strokeWidth={dim === 'overall' ? 2.5 : 1.5}
                dot={{ r: dim === 'overall' ? 3 : 2 }}
              />
            ))}
          </LineChart>
        </ResponsiveContainer>
      </CardContent>
    </Card>
  );
}

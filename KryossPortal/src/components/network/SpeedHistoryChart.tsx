import {
  ResponsiveContainer,
  LineChart,
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ReferenceLine,
  Legend,
} from 'recharts';
import type { SpeedHistoryResponse } from '@/api/networkSites';

interface SpeedHistoryChartProps {
  data: SpeedHistoryResponse;
}

export function SpeedHistoryChart({ data }: SpeedHistoryChartProps) {
  const points = data.history.map((p) => ({
    date: new Date(p.scannedAt).toLocaleDateString(),
    down: p.downloadMbps,
    up: p.uploadMbps,
    latency: p.internetLatencyMs,
  }));

  if (points.length === 0) {
    return (
      <div className="flex items-center justify-center h-48 text-sm text-muted-foreground">
        No speed history data yet
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <div className="h-[280px]">
        <ResponsiveContainer width="100%" height="100%">
          <LineChart data={points} margin={{ top: 5, right: 20, bottom: 5, left: 0 }}>
            <CartesianGrid strokeDasharray="3 3" className="stroke-muted" />
            <XAxis dataKey="date" tick={{ fontSize: 11 }} />
            <YAxis tick={{ fontSize: 11 }} unit=" Mbps" />
            <Tooltip />
            <Legend />
            <Line
              type="monotone"
              dataKey="down"
              name="Download"
              stroke="#008852"
              strokeWidth={2}
              dot={false}
            />
            <Line
              type="monotone"
              dataKey="up"
              name="Upload"
              stroke="#2563eb"
              strokeWidth={2}
              dot={false}
            />
            {data.contractedDownMbps != null && (
              <ReferenceLine
                y={data.contractedDownMbps}
                stroke="#D97706"
                strokeDasharray="5 5"
                label={{ value: `SLA ${data.contractedDownMbps} Mbps`, position: 'right', fontSize: 10 }}
              />
            )}
          </LineChart>
        </ResponsiveContainer>
      </div>
    </div>
  );
}

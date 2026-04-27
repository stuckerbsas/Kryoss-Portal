import { useState } from 'react';
import { Activity, ShieldCheck, Wrench, Settings, AlertTriangle, ChevronLeft, ChevronRight } from 'lucide-react';
import { useMachineActivity } from '@/api/services';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';

interface Props {
  machineId: string | undefined;
}

const TYPE_ICONS: Record<string, typeof Activity> = {
  'post.heartbeat': Activity,
  heartbeat: Activity,
  'post.results': ShieldCheck,
  compliance: ShieldCheck,
  service_heal: Settings,
  service_action: Settings,
  created: Wrench,
  dispatched: Wrench,
  executed: Wrench,
  completed: Wrench,
  failed: AlertTriangle,
  rejected: AlertTriangle,
  rolled_back: Wrench,
};

const SEVERITY_COLORS: Record<string, string> = {
  INFO: 'bg-gray-100 text-gray-700',
  WARN: 'bg-amber-100 text-amber-800',
  ERR: 'bg-red-100 text-red-800',
  ERROR: 'bg-red-100 text-red-800',
  CRIT: 'bg-red-200 text-red-900',
};

function formatTimestamp(ts: string): string {
  return new Date(ts).toLocaleString(undefined, {
    month: 'short', day: 'numeric',
    hour: '2-digit', minute: '2-digit', second: '2-digit',
  });
}

export function ActivityTab({ machineId }: Props) {
  const [page, setPage] = useState(1);
  const [severityFilter, setSeverityFilter] = useState<string>('all');
  const { data, isLoading } = useMachineActivity(machineId, page);

  const items = data?.items ?? [];
  const total = data?.total ?? 0;
  const totalPages = Math.ceil(total / 50);

  if (isLoading) return <div className="text-muted-foreground text-sm p-4">Loading activity...</div>;

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2">
        <Select value={severityFilter} onValueChange={v => { setSeverityFilter(v); setPage(1); }}>
          <SelectTrigger className="w-[140px]">
            <SelectValue placeholder="Severity" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All</SelectItem>
            <SelectItem value="INFO">Info</SelectItem>
            <SelectItem value="WARN">Warning</SelectItem>
            <SelectItem value="ERR">Error</SelectItem>
          </SelectContent>
        </Select>
        <Badge variant="outline">{total} events</Badge>
      </div>

      <div className="space-y-1">
        {items.length === 0 && (
          <div className="text-center text-muted-foreground py-8">No activity found</div>
        )}
        {items.map((item, i) => {
          const Icon = TYPE_ICONS[item.type] ?? Settings;
          const sevClass = SEVERITY_COLORS[item.severity] ?? SEVERITY_COLORS.INFO;
          return (
            <div key={`${item.timestamp}-${i}`} className="flex items-start gap-3 py-2 px-3 rounded-md hover:bg-muted/50 border-b last:border-0">
              <div className="mt-0.5">
                <Icon className="h-4 w-4 text-muted-foreground" />
              </div>
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2">
                  <Badge variant="outline" className={`text-xs ${sevClass}`}>{item.severity}</Badge>
                  <span className="text-sm font-medium truncate">{item.action}</span>
                </div>
                {item.errorMessage && (
                  <div className="text-xs text-red-600 mt-0.5 truncate">{item.errorMessage}</div>
                )}
                {item.actorEmail && (
                  <div className="text-xs text-muted-foreground mt-0.5">{item.actorEmail}</div>
                )}
              </div>
              <div className="text-xs text-muted-foreground whitespace-nowrap">
                {formatTimestamp(item.timestamp)}
              </div>
            </div>
          );
        })}
      </div>

      {totalPages > 1 && (
        <div className="flex items-center justify-center gap-2">
          <Button size="sm" variant="outline" disabled={page <= 1} onClick={() => setPage(p => p - 1)}>
            <ChevronLeft className="h-4 w-4" />
          </Button>
          <span className="text-sm text-muted-foreground">Page {page} of {totalPages}</span>
          <Button size="sm" variant="outline" disabled={page >= totalPages} onClick={() => setPage(p => p + 1)}>
            <ChevronRight className="h-4 w-4" />
          </Button>
        </div>
      )}
    </div>
  );
}

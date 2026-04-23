import { useEffect, useState } from 'react';
import { apiFetch } from '@/api/client';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Button } from '@/components/ui/button';
import { RefreshCw, Loader2 } from 'lucide-react';

interface ActlogEntry {
  id: number;
  timestamp: string;
  actorEmail: string | null;
  actorIp: string | null;
  severity: string;
  module: string;
  action: string;
  entityType: string | null;
  entityId: string | null;
  responseCode: number | null;
  durationMs: number | null;
  message: string | null;
}

const SEVERITY_COLORS: Record<string, string> = {
  INFO: 'bg-blue-100 text-blue-800',
  WARN: 'bg-yellow-100 text-yellow-800',
  ERR: 'bg-red-100 text-red-800',
  CRIT: 'bg-red-200 text-red-900',
  SEC: 'bg-purple-100 text-purple-800',
};

const STATUS_COLORS = (code: number | null) => {
  if (!code) return 'text-muted-foreground';
  if (code >= 500) return 'text-red-600 font-semibold';
  if (code >= 400) return 'text-yellow-600';
  return 'text-green-600';
};

export function ActivityLogPage() {
  const [entries, setEntries] = useState<ActlogEntry[]>([]);
  const [loading, setLoading] = useState(true);
  const [severity, setSeverity] = useState('all');
  const [module, setModule] = useState('all');

  const load = async () => {
    setLoading(true);
    try {
      const params = new URLSearchParams();
      params.set('limit', '100');
      if (severity !== 'all') params.set('severity', severity);
      if (module !== 'all') params.set('module', module);
      const data = await apiFetch<ActlogEntry[]>(`/v2/actlog?${params}`);
      setEntries(data);
    } catch {
      setEntries([]);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { load(); }, [severity, module]);

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">Activity Log</h1>
        <div className="flex items-center gap-2">
          <Select value={severity} onValueChange={setSeverity}>
            <SelectTrigger className="w-[120px]">
              <SelectValue placeholder="Severity" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All</SelectItem>
              <SelectItem value="INFO">INFO</SelectItem>
              <SelectItem value="WARN">WARN</SelectItem>
              <SelectItem value="ERR">ERR</SelectItem>
              <SelectItem value="SEC">SEC</SelectItem>
            </SelectContent>
          </Select>

          <Select value={module} onValueChange={setModule}>
            <SelectTrigger className="w-[140px]">
              <SelectValue placeholder="Module" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All Modules</SelectItem>
              <SelectItem value="agent">Agent</SelectItem>
              <SelectItem value="auth">Auth</SelectItem>
              <SelectItem value="reports">Reports</SelectItem>
              <SelectItem value="machines">Machines</SelectItem>
              <SelectItem value="organizations">Organizations</SelectItem>
              <SelectItem value="enrollment">Enrollment</SelectItem>
              <SelectItem value="middleware">Middleware</SelectItem>
              <SelectItem value="assessment">Assessment</SelectItem>
              <SelectItem value="controls">Controls</SelectItem>
              <SelectItem value="admin">Admin</SelectItem>
              <SelectItem value="api">API</SelectItem>
            </SelectContent>
          </Select>

          <Button variant="outline" size="sm" onClick={load} disabled={loading}>
            {loading ? <Loader2 className="size-4 animate-spin" /> : <RefreshCw className="size-4" />}
          </Button>
        </div>
      </div>

      <Card className="overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b bg-muted/50">
                <th className="px-3 py-2 text-left font-medium">Time</th>
                <th className="px-3 py-2 text-left font-medium">Sev</th>
                <th className="px-3 py-2 text-left font-medium">Module</th>
                <th className="px-3 py-2 text-left font-medium">Action</th>
                <th className="px-3 py-2 text-left font-medium">Actor</th>
                <th className="px-3 py-2 text-left font-medium">Status</th>
                <th className="px-3 py-2 text-left font-medium">Ms</th>
                <th className="px-3 py-2 text-left font-medium">Message</th>
              </tr>
            </thead>
            <tbody>
              {entries.map((e) => (
                <tr key={e.id} className="border-b hover:bg-muted/30 transition-colors">
                  <td className="px-3 py-1.5 whitespace-nowrap text-xs text-muted-foreground">
                    {new Date(e.timestamp).toLocaleString()}
                  </td>
                  <td className="px-3 py-1.5">
                    <Badge variant="secondary" className={`text-[10px] px-1.5 py-0 ${SEVERITY_COLORS[e.severity] || ''}`}>
                      {e.severity}
                    </Badge>
                  </td>
                  <td className="px-3 py-1.5 font-mono text-xs">{e.module}</td>
                  <td className="px-3 py-1.5 font-mono text-xs">{e.action}</td>
                  <td className="px-3 py-1.5 text-xs truncate max-w-[180px]" title={e.actorEmail || ''}>
                    {e.actorEmail || <span className="text-muted-foreground">—</span>}
                  </td>
                  <td className={`px-3 py-1.5 text-xs font-mono ${STATUS_COLORS(e.responseCode)}`}>
                    {e.responseCode || '—'}
                  </td>
                  <td className="px-3 py-1.5 text-xs text-right font-mono text-muted-foreground">
                    {e.durationMs != null ? `${e.durationMs}` : '—'}
                  </td>
                  <td className="px-3 py-1.5 text-xs truncate max-w-[400px]" title={e.message || ''}>
                    {e.message || '—'}
                  </td>
                </tr>
              ))}
              {!loading && entries.length === 0 && (
                <tr>
                  <td colSpan={8} className="px-3 py-8 text-center text-muted-foreground">
                    No entries found
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </Card>
    </div>
  );
}

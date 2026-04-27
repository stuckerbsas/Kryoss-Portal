import { useState } from 'react';
import { toast } from 'sonner';
import { Search, Lock, Play, Square, RotateCw } from 'lucide-react';
import { useMachineServices, useServiceAction, useTogglePriority } from '@/api/services';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import { Switch } from '@/components/ui/switch';
import {
  Table, TableHeader, TableRow, TableHead, TableBody, TableCell,
} from '@/components/ui/table';
import {
  Dialog, DialogContent, DialogDescription, DialogFooter,
  DialogHeader, DialogTitle,
} from '@/components/ui/dialog';

interface Props {
  machineId: string | undefined;
  hostname: string;
}

export function ServicesTab({ machineId, hostname }: Props) {
  const { data, isLoading } = useMachineServices(machineId);
  const serviceAction = useServiceAction(machineId);
  const togglePriority = useTogglePriority(machineId);
  const [search, setSearch] = useState('');
  const [confirm, setConfirm] = useState<{ name: string; displayName: string | null; action: 'start' | 'stop' | 'restart' } | null>(null);

  const items = data?.items ?? [];
  const filtered = items.filter(s =>
    s.name.toLowerCase().includes(search.toLowerCase()) ||
    (s.displayName?.toLowerCase().includes(search.toLowerCase()) ?? false)
  );

  const sorted = [...filtered].sort((a, b) => {
    if (a.status === 'Stopped' && b.status !== 'Stopped') return -1;
    if (a.status !== 'Stopped' && b.status === 'Stopped') return 1;
    if (a.isPriority && !b.isPriority) return -1;
    if (!a.isPriority && b.isPriority) return 1;
    return a.name.localeCompare(b.name);
  });

  function executeAction() {
    if (!confirm) return;
    serviceAction.mutate(
      { serviceName: confirm.name, action: confirm.action },
      {
        onSuccess: () => toast.success(`${confirm.action} queued for ${confirm.displayName ?? confirm.name}`),
        onError: (e: any) => toast.error(e?.message ?? 'Failed'),
      }
    );
    setConfirm(null);
  }

  if (isLoading) return <div className="text-muted-foreground text-sm p-4">Loading services...</div>;

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2">
        <div className="relative flex-1">
          <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
          <Input placeholder="Search services..." value={search} onChange={e => setSearch(e.target.value)} className="pl-8" />
        </div>
        <Badge variant="outline">{items.length} services</Badge>
      </div>

      <div className="rounded-md border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Name</TableHead>
              <TableHead>Status</TableHead>
              <TableHead>Startup</TableHead>
              <TableHead>Priority</TableHead>
              <TableHead className="text-right">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {sorted.map(s => (
              <TableRow key={s.name}>
                <TableCell>
                  <div className="flex items-center gap-2">
                    {s.isProtected && <Lock className="h-3.5 w-3.5 text-muted-foreground" />}
                    <div>
                      <div className="font-medium text-sm">{s.displayName ?? s.name}</div>
                      <div className="text-xs text-muted-foreground">{s.name}</div>
                    </div>
                  </div>
                </TableCell>
                <TableCell>
                  <Badge variant="outline" className={
                    s.status === 'Running' ? 'bg-green-100 text-green-800' :
                    s.status === 'Stopped' ? 'bg-red-100 text-red-800' :
                    'bg-amber-100 text-amber-800'
                  }>
                    <span className={`mr-1.5 inline-block h-2 w-2 rounded-full ${
                      s.status === 'Running' ? 'bg-green-500' :
                      s.status === 'Stopped' ? 'bg-red-500' :
                      'bg-amber-500'
                    }`} />
                    {s.status}
                  </Badge>
                </TableCell>
                <TableCell className="text-sm">{s.startupType}</TableCell>
                <TableCell>
                  {s.isProtected ? (
                    <Badge variant="secondary"><Lock className="h-3 w-3 mr-1" />Protected</Badge>
                  ) : (
                    <Switch
                      checked={s.isPriority}
                      onCheckedChange={(checked) =>
                        togglePriority.mutate(
                          { serviceName: s.name, enable: checked },
                          { onError: (e: any) => toast.error(e?.message ?? 'Failed') }
                        )
                      }
                    />
                  )}
                </TableCell>
                <TableCell className="text-right">
                  {s.isProtected ? (
                    <span className="text-xs text-muted-foreground">Protected</span>
                  ) : (
                    <div className="flex gap-1 justify-end">
                      {s.status === 'Stopped' && (
                        <Button size="sm" variant="outline" onClick={() => setConfirm({ name: s.name, displayName: s.displayName, action: 'start' })}>
                          <Play className="h-3.5 w-3.5 mr-1" />Start
                        </Button>
                      )}
                      {s.status === 'Running' && (
                        <>
                          <Button size="sm" variant="outline" onClick={() => setConfirm({ name: s.name, displayName: s.displayName, action: 'restart' })}>
                            <RotateCw className="h-3.5 w-3.5 mr-1" />Restart
                          </Button>
                          <Button size="sm" variant="outline" onClick={() => setConfirm({ name: s.name, displayName: s.displayName, action: 'stop' })}>
                            <Square className="h-3.5 w-3.5 mr-1" />Stop
                          </Button>
                        </>
                      )}
                    </div>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>

      <Dialog open={!!confirm} onOpenChange={() => setConfirm(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Confirm Service Action</DialogTitle>
            <DialogDescription>
              {confirm?.action} service <strong>{confirm?.displayName ?? confirm?.name}</strong> on <strong>{hostname}</strong>?
              This action will be queued and executed on the next agent heartbeat.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setConfirm(null)}>Cancel</Button>
            <Button onClick={executeAction}>Confirm</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

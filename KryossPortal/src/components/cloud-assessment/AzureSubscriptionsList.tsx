import { toast } from 'sonner';
import { Loader2, Plus, RefreshCw, Trash2 } from 'lucide-react';
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import {
  useAzureDisconnect,
  useAzureVerify,
  type AzureSubscription,
} from '@/api/cloudAssessment';

function formatDate(dateStr: string | null): string {
  if (!dateStr) return '—';
  try {
    return new Date(dateStr).toLocaleString(undefined, {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  } catch {
    return dateStr;
  }
}

function StateBadge({ value }: { value: string | null }) {
  const v = value ?? '—';
  const colors: Record<string, string> = {
    Enabled: 'bg-green-100 text-green-800',
    Disabled: 'bg-gray-100 text-gray-600',
  };
  return (
    <Badge variant="secondary" className={colors[v] ?? 'bg-amber-100 text-amber-800'}>
      {v}
    </Badge>
  );
}

function ConsentBadge({
  value,
  errorMessage,
}: {
  value: string | null;
  errorMessage: string | null;
}) {
  const v = value ?? 'unknown';
  const colors: Record<string, string> = {
    connected: 'bg-green-100 text-green-800',
    pending: 'bg-amber-100 text-amber-800',
    failed: 'bg-red-100 text-red-800',
  };
  return (
    <Badge
      variant="secondary"
      className={colors[v] ?? 'bg-gray-100 text-gray-500'}
      title={errorMessage ?? undefined}
    >
      {v}
    </Badge>
  );
}

export function AzureSubscriptionsList({
  orgId,
  subscriptions,
  onConnectAnother,
}: {
  orgId: string;
  subscriptions: AzureSubscription[];
  onConnectAnother: () => void;
}) {
  const disconnect = useAzureDisconnect();
  const verify = useAzureVerify();

  // Per server schema (CA-6 A1), all subscriptions for an org share a tenantId
  // because consent is granted at tenant scope. Picking any non-null tenant re-verifies
  // all subs. If multi-tenant connections are ever added, this needs per-tenant iteration.
  const firstTenantId = subscriptions.find((s) => !!s.tenantId)?.tenantId ?? null;

  const handleReVerifyAll = () => {
    if (!firstTenantId) {
      toast.error('No tenant ID available on the connected subscriptions.');
      return;
    }
    verify.mutate(
      { organizationId: orgId, tenantId: firstTenantId },
      {
        onSuccess: (data) => {
          if (data.connected && data.subscriptions && data.subscriptions.length > 0) {
            toast.success(`Re-verified. Found ${data.subscriptions.length} subscription(s).`);
          } else if (data.connected) {
            toast.warning(data.message ?? 'Consent granted, but no subscriptions visible yet. Assign Reader role first.');
          } else if (data.error) {
            toast.error(`Re-verify error: ${data.error}`);
          } else {
            toast.error(data.message ?? 'Re-verify failed for an unknown reason.');
          }
        },
        onError: (err: Error) => {
          toast.error(`Re-verify failed: ${err.message}`);
        },
      },
    );
  };

  const handleRemove = (sub: AzureSubscription) => {
    const label = sub.displayName ?? sub.subscriptionId;
    if (!confirm(`Remove subscription "${label}"? Kryoss will stop scanning it.`)) {
      return;
    }
    disconnect.mutate(
      { organizationId: orgId, subscriptionId: sub.subscriptionId },
      {
        onSuccess: () => {
          toast.success(`Removed ${label}.`);
        },
        onError: (err: Error) => {
          toast.error(`Remove failed: ${err.message}`);
        },
      },
    );
  };

  return (
    <Card>
      <CardHeader className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 pb-3">
        <div>
          <CardTitle className="text-base">
            {subscriptions.length} subscription{subscriptions.length === 1 ? '' : 's'} connected
          </CardTitle>
        </div>
        <div className="flex items-center gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={handleReVerifyAll}
            disabled={verify.isPending || !firstTenantId}
          >
            {verify.isPending ? (
              <Loader2 className="mr-1.5 h-4 w-4 animate-spin" />
            ) : (
              <RefreshCw className="mr-1.5 h-4 w-4" />
            )}
            Re-verify all
          </Button>
          <Button size="sm" onClick={onConnectAnother}>
            <Plus className="mr-1.5 h-4 w-4" />
            Connect another subscription
          </Button>
        </div>
      </CardHeader>
      <CardContent className="p-0">
        {/* Mobile cards */}
        <div className="space-y-3 p-4 sm:hidden">
          {subscriptions.map((sub) => (
            <div key={sub.id} className="rounded-lg border p-4 space-y-2">
              <div className="flex items-center justify-between gap-2">
                <span className="font-medium text-sm truncate">{sub.displayName ?? '—'}</span>
                <StateBadge value={sub.state} />
              </div>
              <p className="text-xs text-muted-foreground font-mono truncate">{sub.subscriptionId}</p>
              <div className="flex items-center justify-between gap-2">
                <ConsentBadge value={sub.consentState} errorMessage={sub.errorMessage} />
                <Button
                  variant="ghost"
                  size="sm"
                  className="text-destructive hover:text-destructive"
                  onClick={() => handleRemove(sub)}
                  disabled={disconnect.isPending}
                >
                  <Trash2 className="mr-1 h-4 w-4" />
                  Remove
                </Button>
              </div>
            </div>
          ))}
          {subscriptions.length === 0 && (
            <p className="text-center text-muted-foreground py-8 text-sm">No subscriptions connected.</p>
          )}
        </div>
        {/* Desktop table */}
        <div className="hidden sm:block overflow-x-auto">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Display Name</TableHead>
              <TableHead>Subscription ID</TableHead>
              <TableHead className="w-28">State</TableHead>
              <TableHead className="w-32 hidden lg:table-cell">Consent State</TableHead>
              <TableHead className="w-48 hidden lg:table-cell">Last Verified</TableHead>
              <TableHead className="w-24 text-right">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {subscriptions.map((sub) => (
              <TableRow key={sub.id}>
                <TableCell className="text-sm font-medium">
                  {sub.displayName ?? <span className="text-muted-foreground">—</span>}
                </TableCell>
                <TableCell className="font-mono text-xs">{sub.subscriptionId}</TableCell>
                <TableCell>
                  <StateBadge value={sub.state} />
                </TableCell>
                <TableCell className="hidden lg:table-cell">
                  <ConsentBadge value={sub.consentState} errorMessage={sub.errorMessage} />
                </TableCell>
                <TableCell className="text-sm text-muted-foreground hidden lg:table-cell">
                  {formatDate(sub.lastVerifiedAt)}
                </TableCell>
                <TableCell className="text-right">
                  <Button
                    variant="ghost"
                    size="sm"
                    className="text-destructive hover:text-destructive"
                    onClick={() => handleRemove(sub)}
                    disabled={disconnect.isPending}
                  >
                    <Trash2 className="mr-1 h-4 w-4" />
                    Remove
                  </Button>
                </TableCell>
              </TableRow>
            ))}
            {subscriptions.length === 0 && (
              <TableRow>
                <TableCell colSpan={6} className="text-center text-muted-foreground py-8">
                  No subscriptions connected.
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
        </div>
      </CardContent>
    </Card>
  );
}

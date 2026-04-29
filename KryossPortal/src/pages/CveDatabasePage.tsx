import { useCveSyncStatus, useCveSyncProducts, useCveSyncManual } from '@/api/cveSync';
import { Button } from '@/components/ui/button';
import { RefreshCw, CheckCircle2, XCircle, AlertTriangle, Database, Shield, Bug, Package } from 'lucide-react';
import { toast } from 'sonner';

function formatDate(iso: string | null) {
  if (!iso) return 'Never';
  return new Date(iso).toLocaleString();
}

function StatusBadge({ status }: { status: string | null }) {
  if (status === 'success')
    return <span className="inline-flex items-center gap-1 text-xs font-medium text-green-700 bg-green-50 px-2 py-0.5 rounded-full"><CheckCircle2 className="h-3 w-3" /> Success</span>;
  if (status === 'error')
    return <span className="inline-flex items-center gap-1 text-xs font-medium text-red-700 bg-red-50 px-2 py-0.5 rounded-full"><XCircle className="h-3 w-3" /> Error</span>;
  return <span className="text-xs text-muted-foreground">—</span>;
}

export function CveDatabasePage() {
  const { data: status, isLoading: statusLoading } = useCveSyncStatus();
  const { data: products, isLoading: productsLoading } = useCveSyncProducts();
  const syncMutation = useCveSyncManual();

  const handleSync = (full: boolean) => {
    syncMutation.mutate(full, {
      onSuccess: () => toast.success(full ? 'Full rebuild started' : 'Incremental sync started'),
      onError: () => toast.error('Sync failed'),
    });
  };

  if (statusLoading) return <div className="p-6 text-muted-foreground">Loading…</div>;

  const neverSynced = !status?.lastSyncAt;
  const coveragePercent = status && status.totalSoftware > 0
    ? Math.round((status.softwareWithCpe / status.totalSoftware) * 100)
    : 0;

  return (
    <div className="p-6 space-y-6 max-w-6xl">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold">CVE Database</h1>
          <p className="text-sm text-muted-foreground">NVD + CISA KEV sync status and monitored products</p>
        </div>
        <div className="flex gap-2">
          <Button
            size="sm"
            variant="outline"
            disabled={syncMutation.isPending}
            onClick={() => handleSync(false)}
          >
            <RefreshCw className={`h-4 w-4 mr-1 ${syncMutation.isPending ? 'animate-spin' : ''}`} />
            Sync Now
          </Button>
          <Button
            size="sm"
            variant="outline"
            disabled={syncMutation.isPending}
            onClick={() => handleSync(true)}
          >
            Full Rebuild
          </Button>
        </div>
      </div>

      {neverSynced && (
        <div className="bg-amber-50 border border-amber-200 rounded-lg p-4 flex items-start gap-3">
          <AlertTriangle className="h-5 w-5 text-amber-600 mt-0.5 shrink-0" />
          <div>
            <p className="text-sm font-medium text-amber-800">CVE database never synced</p>
            <p className="text-xs text-amber-600 mt-1">
              Run "Full Rebuild" to pull CVEs from NVD and CISA KEV. Requires <code className="bg-amber-100 px-1 rounded">NvdApiKey</code> env var for faster rate limits.
            </p>
          </div>
        </div>
      )}

      {/* KPIs */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        <Kpi icon={Database} label="Total CVEs" value={status?.totalCves ?? 0} />
        <Kpi icon={Shield} label="Known Exploited (KEV)" value={status?.knownExploited ?? 0} color="text-red-600" />
        <Kpi icon={Bug} label="Open Findings" value={status?.totalFindings ?? 0} color="text-amber-600" />
        <Kpi icon={Package} label="Product Mappings" value={status?.productMappings ?? 0} />
      </div>

      {/* Sync info */}
      <div className="grid md:grid-cols-2 gap-6">
        <div className="border rounded-lg p-4 space-y-3">
          <h2 className="text-sm font-semibold">Sync Status</h2>
          <div className="space-y-2 text-sm">
            <Row label="Last Sync" value={formatDate(status?.lastSyncAt ?? null)} />
            <Row label="Status"><StatusBadge status={status?.lastSyncStatus ?? null} /></Row>
            {status?.lastSyncError && (
              <div className="text-xs text-red-600 bg-red-50 p-2 rounded">{status.lastSyncError}</div>
            )}
            <Row label="Software in Catalog" value={String(status?.totalSoftware ?? 0)} />
            <Row label="With CPE Mapping" value={`${status?.softwareWithCpe ?? 0} (${coveragePercent}%)`} />
            <Row label="Unmapped Software" value={String(products?.unmappedCount ?? 0)} />
          </div>
        </div>

        <div className="border rounded-lg p-4 space-y-3">
          <h2 className="text-sm font-semibold">Recent Syncs</h2>
          {status?.recentSyncs && status.recentSyncs.length > 0 ? (
            <div className="space-y-1">
              {status.recentSyncs.map((s, i) => (
                <div key={i} className="flex items-center justify-between text-xs py-1 border-b last:border-0">
                  <span className="text-muted-foreground">{formatDate(s.syncedAt)}</span>
                  <span className="flex items-center gap-2">
                    <StatusBadge status={s.status} />
                    {s.entriesAdded > 0 && <span className="text-green-600">+{s.entriesAdded}</span>}
                    {s.entriesUpdated > 0 && <span className="text-blue-600">~{s.entriesUpdated}</span>}
                  </span>
                </div>
              ))}
            </div>
          ) : (
            <p className="text-xs text-muted-foreground">No sync history</p>
          )}
        </div>
      </div>

      {/* Product checklist */}
      <div className="border rounded-lg p-4 space-y-3">
        <h2 className="text-sm font-semibold">Monitored Products ({products?.products.length ?? 0})</h2>
        <p className="text-xs text-muted-foreground">
          Products with CPE vendor mapping — CVEs are tracked for these. Add more via CpeMappingService.
        </p>
        {productsLoading ? (
          <div className="text-sm text-muted-foreground">Loading…</div>
        ) : (
          <div className="overflow-auto max-h-[500px]">
            <table className="w-full text-sm">
              <thead className="sticky top-0 bg-white border-b">
                <tr className="text-left text-xs text-muted-foreground">
                  <th className="py-2 px-2">Vendor</th>
                  <th className="py-2 px-2">Product</th>
                  <th className="py-2 px-2 text-right">Software in DB</th>
                  <th className="py-2 px-2 text-right">CVEs</th>
                  <th className="py-2 px-2 text-right">Open Findings</th>
                  <th className="py-2 px-2">Status</th>
                </tr>
              </thead>
              <tbody>
                {products?.products.map((p, i) => (
                  <tr key={i} className="border-b last:border-0 hover:bg-muted/50">
                    <td className="py-1.5 px-2 font-medium">{p.vendor}</td>
                    <td className="py-1.5 px-2 text-muted-foreground">{p.product ?? '—'}</td>
                    <td className="py-1.5 px-2 text-right">{p.softwareCount}</td>
                    <td className="py-1.5 px-2 text-right">{p.cveCount}</td>
                    <td className="py-1.5 px-2 text-right">
                      {p.openFindings > 0
                        ? <span className="text-amber-600 font-medium">{p.openFindings}</span>
                        : <span className="text-muted-foreground">0</span>}
                    </td>
                    <td className="py-1.5 px-2">
                      {p.cveCount > 0
                        ? <span className="text-xs text-green-700 bg-green-50 px-1.5 py-0.5 rounded">Synced</span>
                        : <span className="text-xs text-muted-foreground bg-muted px-1.5 py-0.5 rounded">Pending</span>}
                    </td>
                  </tr>
                ))}
                {products?.products.length === 0 && (
                  <tr>
                    <td colSpan={6} className="py-8 text-center text-muted-foreground">
                      No products mapped yet. Run a Full Rebuild to populate.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}

function Kpi({ icon: Icon, label, value, color }: {
  icon: typeof Database;
  label: string;
  value: number;
  color?: string;
}) {
  return (
    <div className="border rounded-lg p-4">
      <div className="flex items-center gap-2 text-muted-foreground mb-1">
        <Icon className="h-4 w-4" />
        <span className="text-xs">{label}</span>
      </div>
      <div className={`text-2xl font-bold ${color ?? ''}`}>
        {value.toLocaleString()}
      </div>
    </div>
  );
}

function Row({ label, value, children }: { label: string; value?: string; children?: React.ReactNode }) {
  return (
    <div className="flex justify-between">
      <span className="text-muted-foreground">{label}</span>
      {children ?? <span className="font-medium">{value}</span>}
    </div>
  );
}

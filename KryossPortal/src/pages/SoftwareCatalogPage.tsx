import { useState } from 'react';
import { Search, Package, RefreshCw, ChevronLeft, ChevronRight } from 'lucide-react';
import { useSoftwareCatalog, useReclassify } from '@/api/softwareCatalog';
import { Input } from '@/components/ui/input';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { toast } from 'sonner';

const licenseTypeOptions = [
  { value: '', label: 'All' },
  { value: 'Commercial', label: 'Commercial' },
  { value: 'Free', label: 'Free' },
  { value: 'OpenSource', label: 'Open Source' },
  { value: 'Freemium', label: 'Freemium' },
  { value: 'Bundled', label: 'Bundled' },
  { value: 'Likely Commercial', label: 'Likely Commercial' },
  { value: 'Unknown', label: 'Unknown' },
];

const TYPE_STYLES: Record<string, string> = {
  Commercial: 'bg-red-100 text-red-800',
  Free: 'bg-green-100 text-green-800',
  OpenSource: 'bg-emerald-100 text-emerald-800',
  Freemium: 'bg-amber-100 text-amber-800',
  Bundled: 'bg-blue-100 text-blue-800',
  'Likely Commercial': 'bg-orange-100 text-orange-800',
  Unknown: 'bg-gray-100 text-gray-600',
};

export function SoftwareCatalogPage() {
  const [search, setSearch] = useState('');
  const [licenseType, setLicenseType] = useState('');
  const [page, setPage] = useState(1);

  const { data, isLoading } = useSoftwareCatalog(search, licenseType, page);
  const reclassify = useReclassify();

  const handleReclassify = () => {
    reclassify.mutate(undefined, {
      onSuccess: () => toast.success('Reclassification completed'),
      onError: () => toast.error('Reclassification failed'),
    });
  };

  return (
    <div className="p-6 space-y-6 max-w-6xl">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold">Software Catalog</h1>
          <p className="text-sm text-muted-foreground">Software license classification across all organizations</p>
        </div>
        <Button size="sm" variant="outline" disabled={reclassify.isPending} onClick={handleReclassify}>
          <RefreshCw className={`h-4 w-4 mr-1 ${reclassify.isPending ? 'animate-spin' : ''}`} />
          Reclassify All
        </Button>
      </div>

      {data && (
        <div className="grid grid-cols-4 gap-4">
          <div className="border rounded-lg p-4">
            <div className="flex items-center gap-2 text-muted-foreground mb-1">
              <Package className="h-4 w-4" />
              <span className="text-xs">Total Software</span>
            </div>
            <div className="text-2xl font-bold">{data.stats.total.toLocaleString()}</div>
          </div>
          <div className="border rounded-lg p-4">
            <div className="flex items-center gap-2 text-red-600 mb-1">
              <Package className="h-4 w-4" />
              <span className="text-xs">Commercial</span>
            </div>
            <div className="text-2xl font-bold text-red-600">{data.stats.commercial.toLocaleString()}</div>
          </div>
          <div className="border rounded-lg p-4">
            <div className="flex items-center gap-2 text-green-600 mb-1">
              <Package className="h-4 w-4" />
              <span className="text-xs">Free / OSS</span>
            </div>
            <div className="text-2xl font-bold text-green-600">{data.stats.free.toLocaleString()}</div>
          </div>
          <div className="border rounded-lg p-4">
            <div className="flex items-center gap-2 text-muted-foreground mb-1">
              <Package className="h-4 w-4" />
              <span className="text-xs">Unknown</span>
            </div>
            <div className="text-2xl font-bold text-amber-600">{data.stats.unknown.toLocaleString()}</div>
          </div>
        </div>
      )}

      <div className="flex flex-wrap items-center gap-3">
        <div className="relative max-w-sm flex-1">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 size-4 text-muted-foreground" />
          <Input
            placeholder="Search by name or publisher..."
            value={search}
            onChange={(e) => { setSearch(e.target.value); setPage(1); }}
            className="pl-9"
          />
        </div>
        <div className="flex gap-1">
          {licenseTypeOptions.map((opt) => (
            <button
              key={opt.value}
              onClick={() => { setLicenseType(opt.value); setPage(1); }}
              className={[
                'px-3 py-1.5 text-xs font-medium rounded-md border transition-colors',
                licenseType === opt.value
                  ? 'bg-primary text-primary-foreground border-primary'
                  : 'bg-background text-muted-foreground border-border hover:bg-accent',
              ].join(' ')}
            >
              {opt.label}
            </button>
          ))}
        </div>
      </div>

      {isLoading ? (
        <div className="text-sm text-muted-foreground">Loading…</div>
      ) : (
        <>
          <div className="border rounded-lg overflow-hidden">
            <table className="w-full text-sm">
              <thead className="bg-muted/50 border-b">
                <tr className="text-left text-xs text-muted-foreground">
                  <th className="py-2.5 px-3">Name</th>
                  <th className="py-2.5 px-3">Publisher</th>
                  <th className="py-2.5 px-3 text-center">Machines</th>
                  <th className="py-2.5 px-3">License Type</th>
                  <th className="py-2.5 px-3">Confidence</th>
                </tr>
              </thead>
              <tbody>
                {data?.items.map((sw) => (
                  <tr key={sw.id} className="border-b last:border-0 hover:bg-muted/30">
                    <td className="py-2 px-3 font-medium">{sw.name}</td>
                    <td className="py-2 px-3 text-muted-foreground">{sw.publisher ?? '—'}</td>
                    <td className="py-2 px-3 text-center">{sw.machineCount}</td>
                    <td className="py-2 px-3">
                      <Badge className={TYPE_STYLES[sw.licenseType] ?? 'bg-gray-100 text-gray-600'}>
                        {sw.licenseType}
                      </Badge>
                    </td>
                    <td className="py-2 px-3">
                      {sw.confidence > 0 ? `${(sw.confidence * 100).toFixed(0)}%` : '—'}
                    </td>
                  </tr>
                ))}
                {data?.items.length === 0 && (
                  <tr>
                    <td colSpan={5} className="py-8 text-center text-muted-foreground">No software found</td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>

          {data && data.totalPages > 1 && (
            <div className="flex items-center justify-between">
              <p className="text-sm text-muted-foreground">
                Page {data.page} of {data.totalPages}
              </p>
              <div className="flex gap-2">
                <Button size="sm" variant="outline" disabled={page <= 1} onClick={() => setPage(page - 1)}>
                  <ChevronLeft className="h-4 w-4" />
                </Button>
                <Button size="sm" variant="outline" disabled={page >= data.totalPages} onClick={() => setPage(page + 1)}>
                  <ChevronRight className="h-4 w-4" />
                </Button>
              </div>
            </div>
          )}
        </>
      )}
    </div>
  );
}

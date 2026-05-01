import { useState } from 'react';
import { Search, Package, Wand2, ChevronLeft, ChevronRight } from 'lucide-react';
import { useSoftwareCatalog, useUpdateCategory, useAutoDetect } from '@/api/softwareCatalog';
import { Input } from '@/components/ui/input';
import { Button } from '@/components/ui/button';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { toast } from 'sonner';

const categoryOptions = [
  { value: '', label: 'All' },
  { value: 'licensed', label: 'Licensed' },
  { value: 'remote_access', label: 'Remote Access' },
  { value: 'suspicious', label: 'Suspicious' },
  { value: 'standard', label: 'Standard' },
  { value: 'uncategorized', label: 'Uncategorized' },
];

const categoryBadge: Record<string, { label: string; className: string }> = {
  licensed: { label: 'Licensed', className: 'bg-blue-100 text-blue-800' },
  remote_access: { label: 'Remote Access', className: 'bg-amber-100 text-amber-800' },
  suspicious: { label: 'Suspicious', className: 'bg-red-100 text-red-800' },
  standard: { label: 'Standard', className: 'bg-gray-100 text-gray-600' },
};

export function SoftwareCatalogPage() {
  const [search, setSearch] = useState('');
  const [category, setCategory] = useState('');
  const [page, setPage] = useState(1);

  const { data, isLoading } = useSoftwareCatalog(search, category, page);
  const updateCat = useUpdateCategory();
  const autoDetect = useAutoDetect();

  const handleAutoDetect = () => {
    autoDetect.mutate(undefined, {
      onSuccess: (r) => toast.success(`Categorized ${r.categorized} items (${r.markedLicensed} licensed)`),
      onError: () => toast.error('Auto-detect failed'),
    });
  };

  return (
    <div className="p-6 space-y-6 max-w-6xl">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold">Software Catalog</h1>
          <p className="text-sm text-muted-foreground">Manage software classification across all organizations</p>
        </div>
        <Button size="sm" variant="outline" disabled={autoDetect.isPending} onClick={handleAutoDetect}>
          <Wand2 className={`h-4 w-4 mr-1 ${autoDetect.isPending ? 'animate-spin' : ''}`} />
          Auto-detect
        </Button>
      </div>

      {/* KPIs */}
      {data && (
        <div className="grid grid-cols-3 gap-4">
          <div className="border rounded-lg p-4">
            <div className="flex items-center gap-2 text-muted-foreground mb-1">
              <Package className="h-4 w-4" />
              <span className="text-xs">Total Software</span>
            </div>
            <div className="text-2xl font-bold">{data.stats.total.toLocaleString()}</div>
          </div>
          <div className="border rounded-lg p-4">
            <div className="flex items-center gap-2 text-blue-600 mb-1">
              <Package className="h-4 w-4" />
              <span className="text-xs">Licensed</span>
            </div>
            <div className="text-2xl font-bold text-blue-600">{data.stats.licensed.toLocaleString()}</div>
          </div>
          <div className="border rounded-lg p-4">
            <div className="flex items-center gap-2 text-muted-foreground mb-1">
              <Package className="h-4 w-4" />
              <span className="text-xs">Uncategorized</span>
            </div>
            <div className="text-2xl font-bold text-amber-600">{data.stats.uncategorized.toLocaleString()}</div>
          </div>
        </div>
      )}

      {/* Search + filters */}
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
          {categoryOptions.map((opt) => (
            <button
              key={opt.value}
              onClick={() => { setCategory(opt.value); setPage(1); }}
              className={[
                'px-3 py-1.5 text-xs font-medium rounded-md border transition-colors',
                category === opt.value
                  ? 'bg-primary text-primary-foreground border-primary'
                  : 'bg-background text-muted-foreground border-border hover:bg-accent',
              ].join(' ')}
            >
              {opt.label}
            </button>
          ))}
        </div>
      </div>

      {/* Table */}
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
                  <th className="py-2.5 px-3">Category</th>
                </tr>
              </thead>
              <tbody>
                {data?.items.map((sw) => (
                    <tr key={sw.id} className="border-b last:border-0 hover:bg-muted/30">
                      <td className="py-2 px-3 font-medium">{sw.name}</td>
                      <td className="py-2 px-3 text-muted-foreground">{sw.publisher ?? '—'}</td>
                      <td className="py-2 px-3 text-center">{sw.machineCount}</td>
                      <td className="py-2 px-3">
                        <Select
                          value={sw.category ?? '_none'}
                          onValueChange={(val) => updateCat.mutate({ id: sw.id, category: val === '_none' ? null : val })}
                        >
                          <SelectTrigger className="w-36 h-8 text-xs">
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            <SelectItem value="_none">Uncategorized</SelectItem>
                            <SelectItem value="licensed">Licensed</SelectItem>
                            <SelectItem value="remote_access">Remote Access</SelectItem>
                            <SelectItem value="suspicious">Suspicious</SelectItem>
                            <SelectItem value="standard">Standard</SelectItem>
                          </SelectContent>
                        </Select>
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

          {/* Pagination */}
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

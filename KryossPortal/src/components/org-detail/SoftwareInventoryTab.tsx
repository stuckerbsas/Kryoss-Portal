import { Fragment, useState, useMemo } from 'react';
import {
  Search,
  Package,
  Shield,
  HelpCircle,
  ChevronDown,
  ChevronRight,
} from 'lucide-react';
import { useSoftwareInventory } from '@/api/inventory';
import { useOrgParam } from '@/hooks/useOrgParam';
import { EmptyState } from '@/components/shared/EmptyState';
import { Input } from '@/components/ui/input';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Table, TableHeader, TableRow, TableHead, TableBody, TableCell,
} from '@/components/ui/table';

type LicenseFilter = 'all' | 'Commercial' | 'Free' | 'OpenSource' | 'Freemium' | 'Bundled' | 'Unknown';

const TYPE_STYLES: Record<string, { label: string; className: string }> = {
  Commercial: { label: 'Commercial', className: 'bg-red-100 text-red-800 hover:bg-red-100' },
  'Likely Commercial': { label: 'Likely Commercial', className: 'bg-orange-100 text-orange-800 hover:bg-orange-100' },
  Free: { label: 'Free', className: 'bg-green-100 text-green-800 hover:bg-green-100' },
  OpenSource: { label: 'Open Source', className: 'bg-emerald-100 text-emerald-800 hover:bg-emerald-100' },
  Freemium: { label: 'Freemium', className: 'bg-amber-100 text-amber-800 hover:bg-amber-100' },
  Bundled: { label: 'Bundled', className: 'bg-blue-100 text-blue-800 hover:bg-blue-100' },
  Unknown: { label: 'Unknown', className: 'bg-gray-100 text-gray-600 hover:bg-gray-100' },
};

const filterOptions: { value: LicenseFilter; label: string }[] = [
  { value: 'all', label: 'All' },
  { value: 'Commercial', label: 'Commercial' },
  { value: 'Free', label: 'Free' },
  { value: 'OpenSource', label: 'Open Source' },
  { value: 'Freemium', label: 'Freemium' },
  { value: 'Unknown', label: 'Unknown' },
];

export function SoftwareInventoryTab() {
  const { orgId } = useOrgParam();
  const { data, isLoading } = useSoftwareInventory(orgId);
  const [search, setSearch] = useState('');
  const [licenseFilter, setLicenseFilter] = useState<LicenseFilter>('all');
  const [expandedRow, setExpandedRow] = useState<string | null>(null);

  const filtered = useMemo(() => {
    if (!data) return [];
    let items = data.items;

    if (licenseFilter !== 'all') {
      items = items.filter((s) => s.licenseType === licenseFilter
        || (licenseFilter === 'Commercial' && s.licenseType === 'Likely Commercial'));
    }

    if (search) {
      const lower = search.toLowerCase();
      items = items.filter(
        (s) =>
          s.name.toLowerCase().includes(lower) ||
          s.publisher?.toLowerCase().includes(lower),
      );
    }

    return items;
  }, [data, search, licenseFilter]);

  if (isLoading) {
    return (
      <div className="space-y-4">
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {Array.from({ length: 4 }).map((_, i) => (
            <Card key={i}>
              <CardHeader className="pb-2"><Skeleton className="h-4 w-24" /></CardHeader>
              <CardContent><Skeleton className="h-8 w-16" /></CardContent>
            </Card>
          ))}
        </div>
        <div className="space-y-2">
          {Array.from({ length: 5 }).map((_, i) => (
            <Skeleton key={i} className="h-12 w-full" />
          ))}
        </div>
      </div>
    );
  }

  if (!data || data.items.length === 0) {
    return (
      <EmptyState
        icon={<Package className="size-10" />}
        title="No software data"
        description="Enroll machines and run an assessment to see software inventory."
      />
    );
  }

  return (
    <div className="space-y-4">
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Unique Software</CardTitle>
            <Package className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <p className="text-2xl font-bold">{data.total}</p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Commercial</CardTitle>
            <Shield className="h-4 w-4 text-red-600" />
          </CardHeader>
          <CardContent>
            <p className="text-2xl font-bold text-red-600">{data.commercial}</p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Free / OSS</CardTitle>
            <Package className="h-4 w-4 text-green-600" />
          </CardHeader>
          <CardContent>
            <p className="text-2xl font-bold text-green-600">{data.free}</p>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Unknown</CardTitle>
            <HelpCircle className="h-4 w-4 text-amber-600" />
          </CardHeader>
          <CardContent>
            <p className="text-2xl font-bold text-amber-600">{data.unknown}</p>
          </CardContent>
        </Card>
      </div>

      <div className="flex flex-wrap items-center gap-3">
        <div className="relative max-w-sm flex-1">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 size-4 text-muted-foreground" />
          <Input
            placeholder="Search by name or publisher..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="pl-9"
          />
        </div>
        <div className="flex gap-1">
          {filterOptions.map((opt) => (
            <button
              key={opt.value}
              onClick={() => setLicenseFilter(opt.value)}
              className={[
                'px-3 py-1.5 text-xs font-medium rounded-md border transition-colors',
                licenseFilter === opt.value
                  ? 'bg-primary text-primary-foreground border-primary'
                  : 'bg-background text-muted-foreground border-border hover:bg-accent',
              ].join(' ')}
            >
              {opt.label}
            </button>
          ))}
        </div>
      </div>

      <Table>
        <TableHeader>
          <TableRow>
            <TableHead className="w-8" />
            <TableHead>Software Name</TableHead>
            <TableHead>Publisher</TableHead>
            <TableHead>Version</TableHead>
            <TableHead>Machines</TableHead>
            <TableHead>License Type</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {filtered.map((s) => {
            const key = `${s.name}|||${s.publisher}|||${s.version}`;
            const isExpanded = expandedRow === key;
            const badge = TYPE_STYLES[s.licenseType] ?? TYPE_STYLES.Unknown;

            return (
              <Fragment key={key}>
                <TableRow
                  className="cursor-pointer"
                  onClick={() => setExpandedRow(isExpanded ? null : key)}
                >
                  <TableCell className="px-2">
                    {isExpanded ? (
                      <ChevronDown className="h-4 w-4 text-muted-foreground" />
                    ) : (
                      <ChevronRight className="h-4 w-4 text-muted-foreground" />
                    )}
                  </TableCell>
                  <TableCell className="font-medium">{s.name}</TableCell>
                  <TableCell className="text-muted-foreground">{s.publisher ?? 'Unknown'}</TableCell>
                  <TableCell className="text-muted-foreground text-sm font-mono">{s.version ?? '-'}</TableCell>
                  <TableCell>
                    <span className="font-medium">{s.machineCount}</span>
                  </TableCell>
                  <TableCell>
                    <Badge variant="secondary" className={badge.className}>{badge.label}</Badge>
                  </TableCell>
                </TableRow>
                {isExpanded && (
                  <TableRow key={`${key}-detail`}>
                    <TableCell />
                    <TableCell colSpan={5}>
                      <div className="py-1 text-sm text-muted-foreground max-h-32 overflow-y-auto">
                        <span className="font-medium text-foreground">Installed on: </span>
                        {s.machines.join(', ')}
                      </div>
                    </TableCell>
                  </TableRow>
                )}
              </Fragment>
            );
          })}
        </TableBody>
      </Table>
      <p className="text-sm text-muted-foreground">
        Showing {filtered.length} of {data.total} software items
      </p>
    </div>
  );
}

import { useState } from 'react';
import { CreditCard, DollarSign, HelpCircle, Loader2, RefreshCw, Shield } from 'lucide-react';
import { toast } from 'sonner';
import { useSoftwareLicenses, useClassifyLicenses, useOverrideLicense } from '@/api/softwareLicenses';
import type { LicenseItem } from '@/api/softwareLicenses';
import { useOrgParam } from '@/hooks/useOrgParam';
import { EmptyState } from '@/components/shared/EmptyState';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Table, TableBody, TableCell, TableHead, TableHeader, TableRow,
} from '@/components/ui/table';
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from '@/components/ui/select';

const LICENSE_TYPES = ['All', 'Commercial', 'Free', 'OpenSource', 'Freemium', 'Bundled', 'LikelyCommercial', 'Unknown'] as const;

const TYPE_STYLES: Record<string, string> = {
  Commercial: 'bg-red-100 text-red-800',
  Free: 'bg-green-100 text-green-800',
  OpenSource: 'bg-emerald-100 text-emerald-800',
  Freemium: 'bg-amber-100 text-amber-800',
  Bundled: 'bg-blue-100 text-blue-800',
  LikelyCommercial: 'bg-orange-100 text-orange-800',
  Unknown: 'bg-gray-100 text-gray-600',
};

function typeBadge(type: string) {
  return <Badge className={TYPE_STYLES[type] ?? 'bg-gray-100 text-gray-600'}>{type}</Badge>;
}

function confidenceDot(c: number) {
  const color = c > 0.8 ? 'bg-green-500' : c >= 0.5 ? 'bg-amber-500' : 'bg-red-500';
  return (
    <span className="flex items-center gap-1.5">
      <span className={`inline-block size-2.5 rounded-full ${color}`} />
      <span className="text-sm">{(c * 100).toFixed(0)}%</span>
    </span>
  );
}

function OverrideCell({ item, orgId }: { item: LicenseItem; orgId: string | undefined }) {
  const override = useOverrideLicense(orgId);

  const handleChange = (newType: string) => {
    override.mutate(
      { softwareName: item.softwareName, publisher: item.publisher ?? undefined, licenseType: newType },
      {
        onSuccess: () => toast.success(`${item.softwareName} -> ${newType}`),
        onError: (e: Error) => toast.error(e.message),
      },
    );
  };

  return (
    <Select onValueChange={handleChange} disabled={override.isPending}>
      <SelectTrigger className="w-28 h-7 text-xs">
        <SelectValue placeholder="Override" />
      </SelectTrigger>
      <SelectContent>
        {LICENSE_TYPES.filter((t) => t !== 'All').map((t) => (
          <SelectItem key={t} value={t}>{t}</SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}

export function LicensesTab() {
  const { orgId } = useOrgParam();
  const [filter, setFilter] = useState<string | undefined>(undefined);
  const [page, setPage] = useState(1);
  const { data, isLoading } = useSoftwareLicenses(orgId, { licenseType: filter, page });
  const classify = useClassifyLicenses(orgId);

  const handleClassify = () => {
    classify.mutate(undefined, {
      onSuccess: (r) => toast.success(`Classified ${r.classifiedCount} items`),
      onError: (e: Error) => toast.error(e.message),
    });
  };

  if (isLoading) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-8 w-64" />
        <div className="grid grid-cols-2 lg:grid-cols-5 gap-4">
          {[1, 2, 3, 4, 5].map((i) => <Skeleton key={i} className="h-24" />)}
        </div>
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }

  if (!data) {
    return (
      <EmptyState
        icon={<Shield className="size-10" />}
        title="No license data"
        description="Deploy agents and run assessments to start tracking software licenses."
      />
    );
  }

  const s = data.summary;
  const totalPages = Math.ceil(data.totalCount / data.pageSize) || 1;

  return (
    <div className="space-y-6">
      {/* KPI cards */}
      <div className="grid grid-cols-2 lg:grid-cols-5 gap-4">
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Commercial</CardTitle>
          </CardHeader>
          <CardContent>
            <span className="text-2xl font-bold text-red-700">{s.commercial + s.likelyCommercial}</span>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Free / Open Source</CardTitle>
          </CardHeader>
          <CardContent>
            <span className="text-2xl font-bold text-green-700">{s.free + s.openSource}</span>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Freemium</CardTitle>
          </CardHeader>
          <CardContent>
            <span className="text-2xl font-bold text-amber-700">{s.freemium}</span>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Unknown</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="flex items-center gap-2">
              <HelpCircle className="size-5 text-gray-400" />
              <span className="text-2xl font-bold text-gray-600">{s.unknown}</span>
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Est. Monthly Cost</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="flex items-center gap-2">
              <DollarSign className="size-5 text-primary" />
              <span className="text-2xl font-bold">${s.totalEstimatedMonthlyCost.toLocaleString()}</span>
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Filter bar */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-1.5 flex-wrap">
          {LICENSE_TYPES.map((t) => (
            <Button
              key={t}
              variant={(t === 'All' && !filter) || filter === t ? 'default' : 'outline'}
              size="sm"
              onClick={() => {
                setFilter(t === 'All' ? undefined : t);
                setPage(1);
              }}
            >
              {t}
            </Button>
          ))}
        </div>
        <Button variant="outline" size="sm" onClick={handleClassify} disabled={classify.isPending}>
          {classify.isPending ? (
            <Loader2 className="size-4 mr-1 animate-spin" />
          ) : (
            <RefreshCw className="size-4 mr-1" />
          )}
          Classify
        </Button>
      </div>

      {/* Table */}
      <Card>
        <CardContent className="p-0">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Software</TableHead>
                <TableHead>Publisher</TableHead>
                <TableHead>Version</TableHead>
                <TableHead>Machine</TableHead>
                <TableHead>License Type</TableHead>
                <TableHead className="text-right">Est. Cost</TableHead>
                <TableHead>Confidence</TableHead>
                <TableHead>Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.items.length === 0 ? (
                <TableRow>
                  <TableCell colSpan={8} className="text-center text-muted-foreground py-8">
                    No results for this filter
                  </TableCell>
                </TableRow>
              ) : (
                data.items.map((item, i) => (
                  <TableRow key={`${item.machineId}-${item.softwareName}-${i}`}>
                    <TableCell className="font-medium max-w-[200px] truncate">{item.softwareName}</TableCell>
                    <TableCell className="text-sm text-muted-foreground">{item.publisher ?? '--'}</TableCell>
                    <TableCell className="text-sm font-mono">{item.version ?? '--'}</TableCell>
                    <TableCell className="text-sm">{item.machineName}</TableCell>
                    <TableCell>{typeBadge(item.licenseType)}</TableCell>
                    <TableCell className="text-right text-sm">
                      {item.estimatedCostUsd != null ? `$${item.estimatedCostUsd}/${item.costPeriod ?? 'mo'}` : '--'}
                    </TableCell>
                    <TableCell>{confidenceDot(item.confidence)}</TableCell>
                    <TableCell>
                      <OverrideCell item={item} orgId={orgId} />
                    </TableCell>
                  </TableRow>
                ))
              )}
            </TableBody>
          </Table>
        </CardContent>
      </Card>

      {/* Pagination */}
      {totalPages > 1 && (
        <div className="flex items-center justify-center gap-4">
          <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
            Previous
          </Button>
          <span className="text-sm text-muted-foreground">
            Page {page} of {totalPages}
          </span>
          <Button variant="outline" size="sm" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)}>
            Next
          </Button>
        </div>
      )}
    </div>
  );
}

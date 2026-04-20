import { Building2, Server, AlertTriangle, Loader2 } from 'lucide-react';
import { useInfraAssessment, useStartInfraAssessmentScan } from '@/api/infraAssessment';
import { useOrgParam } from '@/hooks/useOrgParam';
import { EmptyState } from '@/components/shared/EmptyState';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { toast } from 'sonner';

export function InfraAssessmentTab() {
  const orgId = useOrgParam();
  const { data: scan, isLoading } = useInfraAssessment(orgId);
  const startScan = useStartInfraAssessmentScan(orgId);

  const handleStartScan = () => {
    startScan.mutate(undefined, {
      onSuccess: () => toast.success('Infrastructure scan started'),
      onError: (err: Error) => toast.error(err.message),
    });
  };

  if (isLoading) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-8 w-64" />
        <div className="grid grid-cols-4 gap-4">
          {[1, 2, 3, 4].map((i) => (
            <Skeleton key={i} className="h-24" />
          ))}
        </div>
      </div>
    );
  }

  if (!scan) {
    return (
      <EmptyState
        icon={Building2}
        title="No Infrastructure Assessment"
        description="Start a scan to assess your on-prem and hybrid infrastructure across sites, devices, connectivity, and capacity."
        action={
          <Button onClick={handleStartScan} disabled={startScan.isPending}>
            {startScan.isPending && <Loader2 className="size-4 mr-1 animate-spin" />}
            Start Scan
          </Button>
        }
      />
    );
  }

  const statusColor: Record<string, string> = {
    pass: 'bg-green-100 text-green-800',
    warning: 'bg-amber-100 text-amber-800',
    fail: 'bg-red-100 text-red-800',
    info: 'bg-blue-100 text-blue-800',
  };

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-lg font-semibold">Infrastructure Assessment</h2>
          <p className="text-sm text-muted-foreground">
            Status: <Badge variant="outline">{scan.status}</Badge>
            {scan.overallHealth != null && (
              <span className="ml-2">Health: {scan.overallHealth}%</span>
            )}
          </p>
        </div>
        <Button
          variant="outline"
          size="sm"
          onClick={handleStartScan}
          disabled={startScan.isPending || scan.status === 'running'}
        >
          {startScan.isPending && <Loader2 className="size-4 mr-1 animate-spin" />}
          Re-scan
        </Button>
      </div>

      <div className="grid grid-cols-4 gap-4">
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Sites</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="flex items-center gap-2">
              <Building2 className="size-5 text-primary" />
              <span className="text-2xl font-bold">{scan.siteCount}</span>
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Devices</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="flex items-center gap-2">
              <Server className="size-5 text-primary" />
              <span className="text-2xl font-bold">{scan.deviceCount}</span>
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Findings</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="flex items-center gap-2">
              <AlertTriangle className="size-5 text-amber-500" />
              <span className="text-2xl font-bold">{scan.findingCount}</span>
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-muted-foreground">Links</CardTitle>
          </CardHeader>
          <CardContent>
            <span className="text-2xl font-bold">{scan.connectivity.length}</span>
          </CardContent>
        </Card>
      </div>

      {scan.findings.length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Findings</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="space-y-2">
              {scan.findings.map((f) => (
                <div key={f.id} className="flex items-start gap-3 p-2 rounded border">
                  <Badge className={statusColor[f.status] ?? 'bg-gray-100 text-gray-800'}>
                    {f.status}
                  </Badge>
                  <div className="flex-1 min-w-0">
                    <p className="text-sm font-medium">{f.area}{f.service ? ` — ${f.service}` : ''}</p>
                    {f.observation && <p className="text-sm text-muted-foreground">{f.observation}</p>}
                    {f.recommendation && <p className="text-xs text-muted-foreground mt-1">{f.recommendation}</p>}
                  </div>
                  <Badge variant="outline" className="text-xs">{f.priority}</Badge>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>
      )}
    </div>
  );
}

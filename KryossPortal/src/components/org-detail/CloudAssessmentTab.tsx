import { Cloud, Loader2, RefreshCw } from 'lucide-react';
import { toast } from 'sonner';
import { useCloudAssessment, useCloudAssessmentScan } from '@/api/cloudAssessment';
import { useOrgParam } from '@/hooks/useOrgParam';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';

function formatDate(dateStr: string): string {
  return new Date(dateStr).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

export function CloudAssessmentTab() {
  const { orgId } = useOrgParam();
  const { data: summary, isLoading } = useCloudAssessment(orgId);
  const scanMutation = useCloudAssessmentScan();

  const scan = summary && 'id' in summary ? (summary as import('@/api/cloudAssessment').CloudAssessmentScan) : null;
  const isRunning = scan?.status === 'running';
  const notScanned = !summary || ('scanned' in summary && summary.scanned === false);

  const handleRunScan = () => {
    if (!orgId) return;
    scanMutation.mutate(
      { organizationId: orgId },
      {
        onSuccess: () => {
          toast.success('Scan started.');
        },
        onError: (err: any) => {
          toast.error(`Failed to start scan: ${err.message}`);
        },
      },
    );
  };

  // Loading state
  if (isLoading) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-8 w-64" />
        <Skeleton className="h-48 w-full" />
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h3 className="text-lg font-semibold">Cloud Assessment</h3>
          <p className="text-sm text-muted-foreground">
            Multi-area cloud security assessment (M365, Azure, Entra, Intune, Purview, Power BI).
            Scaffold — pipelines coming in CA-1+.
          </p>
        </div>
        {scan && !isRunning && (
          <Button
            size="sm"
            variant="outline"
            onClick={handleRunScan}
            disabled={scanMutation.isPending}
          >
            {scanMutation.isPending ? (
              <>
                <Loader2 className="mr-1.5 h-4 w-4 animate-spin" />
                Starting...
              </>
            ) : (
              <>
                <RefreshCw className="mr-1.5 h-4 w-4" />
                Run New Scan
              </>
            )}
          </Button>
        )}
      </div>

      {/* Not scanned yet */}
      {notScanned && !isRunning && (
        <Card>
          <CardContent className="flex flex-col items-center justify-center py-16 text-center gap-4">
            <Cloud className="h-12 w-12 text-muted-foreground" />
            <div>
              <p className="font-semibold text-lg">No cloud assessment has been run yet</p>
              <p className="text-sm text-muted-foreground mt-1 max-w-md">
                Run a scan to evaluate your cloud security posture across M365, Azure, Entra ID,
                Intune, Purview, and Power BI.
              </p>
            </div>
            <Button onClick={handleRunScan} disabled={scanMutation.isPending}>
              {scanMutation.isPending ? (
                <>
                  <Loader2 className="mr-1.5 h-4 w-4 animate-spin" />
                  Starting...
                </>
              ) : (
                <>
                  <RefreshCw className="mr-1.5 h-4 w-4" />
                  Run First Scan
                </>
              )}
            </Button>
          </CardContent>
        </Card>
      )}

      {/* Scan running */}
      {isRunning && (
        <Card>
          <CardContent className="flex items-center gap-4 py-8">
            <Loader2 className="h-8 w-8 animate-spin text-blue-500 shrink-0" />
            <div>
              <p className="font-medium">Scan in progress...</p>
              <p className="text-sm text-muted-foreground">
                Evaluating your cloud environment. This may take a moment.
              </p>
            </div>
          </CardContent>
        </Card>
      )}

      {/* Scan summary */}
      {scan && !isRunning && (
        <Card>
          <CardHeader className="pb-3">
            <CardTitle className="text-base">Latest Scan</CardTitle>
          </CardHeader>
          <CardContent>
            <dl className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
              <div>
                <dt className="text-xs text-muted-foreground uppercase tracking-wide">Verdict</dt>
                <dd className="mt-1 font-medium">{scan.verdict ?? '—'}</dd>
              </div>
              <div>
                <dt className="text-xs text-muted-foreground uppercase tracking-wide">Status</dt>
                <dd className="mt-1">
                  <Badge
                    variant="secondary"
                    className={
                      scan.status === 'completed'
                        ? 'bg-green-100 text-green-800'
                        : scan.status === 'partial'
                        ? 'bg-amber-100 text-amber-800'
                        : scan.status === 'failed'
                        ? 'bg-red-100 text-red-800'
                        : 'bg-gray-100 text-gray-600'
                    }
                  >
                    {scan.status}
                  </Badge>
                </dd>
              </div>
              <div>
                <dt className="text-xs text-muted-foreground uppercase tracking-wide">
                  Overall Score
                </dt>
                <dd className="mt-1 font-medium tabular-nums">
                  {scan.overallScore !== null ? scan.overallScore : '—'}
                </dd>
              </div>
              <div>
                <dt className="text-xs text-muted-foreground uppercase tracking-wide">Started at</dt>
                <dd className="mt-1 text-sm">{formatDate(scan.startedAt)}</dd>
              </div>
              <div>
                <dt className="text-xs text-muted-foreground uppercase tracking-wide">
                  Completed at
                </dt>
                <dd className="mt-1 text-sm">
                  {scan.completedAt ? formatDate(scan.completedAt) : '—'}
                </dd>
              </div>
            </dl>
          </CardContent>
        </Card>
      )}
    </div>
  );
}

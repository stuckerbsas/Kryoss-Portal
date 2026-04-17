import { useCloudAssessment, useCloudAssessmentDetail, type CloudAssessmentFinding } from '@/api/cloudAssessment';
import { useOrgParam } from '@/hooks/useOrgParam';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { Card, CardContent } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { ExternalLink } from 'lucide-react';
import { OverviewTab } from './OverviewTab';

function statusBadge(status: string) {
  const colors: Record<string, string> = {
    'Success': 'bg-green-100 text-green-800',
    'Action Required': 'bg-red-100 text-red-800',
    'Warning': 'bg-amber-100 text-amber-800',
    'Critical': 'bg-red-200 text-red-900',
    'Disabled': 'bg-gray-100 text-gray-600',
    'Not Licensed': 'bg-gray-100 text-gray-600',
    'Insight': 'bg-blue-100 text-blue-800',
  };
  return <Badge variant="secondary" className={colors[status] ?? 'bg-gray-100 text-gray-500'}>{status}</Badge>;
}

function priorityBadge(priority: string) {
  const colors: Record<string, string> = {
    'Critical': 'bg-red-100 text-red-800',
    'High': 'bg-orange-100 text-orange-800',
    'Medium': 'bg-amber-100 text-amber-800',
    'Low': 'bg-blue-100 text-blue-800',
    'Informational': 'bg-gray-100 text-gray-600',
  };
  return <Badge variant="secondary" className={colors[priority] ?? 'bg-gray-100 text-gray-500'}>{priority}</Badge>;
}

function AreaFindingsTab({ area, scanId }: { area: string; scanId: string | undefined }) {
  const { data: detail } = useCloudAssessmentDetail(scanId);

  if (!scanId) {
    return (
      <Card>
        <CardContent className="py-10 text-center text-sm text-muted-foreground">
          Run a scan from the Overview tab to see findings for this area.
        </CardContent>
      </Card>
    );
  }

  const findings: CloudAssessmentFinding[] = (detail?.findings ?? []).filter((f) => f.area === area);

  if (findings.length === 0) {
    return (
      <Card>
        <CardContent className="py-10 text-center text-sm text-muted-foreground">
          No findings for this area.
        </CardContent>
      </Card>
    );
  }

  return (
    <Card>
      <CardContent className="p-0 overflow-x-auto">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Service</TableHead>
              <TableHead>Feature</TableHead>
              <TableHead>Status</TableHead>
              <TableHead>Priority</TableHead>
              <TableHead>Observation</TableHead>
              <TableHead>Recommendation</TableHead>
              <TableHead className="w-20">Link</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {findings.map((f, i) => (
              <TableRow key={`${f.service}-${f.feature}-${i}`}>
                <TableCell className="text-sm font-medium whitespace-nowrap">{f.service}</TableCell>
                <TableCell className="text-sm">{f.feature}</TableCell>
                <TableCell>{statusBadge(f.status)}</TableCell>
                <TableCell>{priorityBadge(f.priority)}</TableCell>
                <TableCell className="text-sm text-muted-foreground max-w-sm">
                  <div className="truncate" title={f.observation ?? undefined}>{f.observation ?? '—'}</div>
                </TableCell>
                <TableCell className="text-sm text-muted-foreground max-w-sm">
                  <div className="truncate" title={f.recommendation ?? undefined}>{f.recommendation ?? '—'}</div>
                </TableCell>
                <TableCell>
                  {f.linkUrl ? (
                    <a href={f.linkUrl} target="_blank" rel="noopener noreferrer" className="text-blue-600 hover:underline text-xs flex items-center gap-1">
                      <ExternalLink className="h-3 w-3" />
                      {f.linkText ?? 'Docs'}
                    </a>
                  ) : '—'}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  );
}

export function CloudAssessmentPage() {
  const { orgId } = useOrgParam();
  const { data: summary } = useCloudAssessment(orgId);

  const latestScanId = summary && 'id' in summary ? summary.id : undefined;

  if (!orgId) return null;

  return (
    <Tabs defaultValue="overview" className="space-y-4">
      <TabsList>
        <TabsTrigger value="overview">Overview</TabsTrigger>
        <TabsTrigger value="identity">Identity</TabsTrigger>
        <TabsTrigger value="endpoint">Endpoint</TabsTrigger>
        <TabsTrigger value="data">Data</TabsTrigger>
        <TabsTrigger value="productivity">Productivity</TabsTrigger>
      </TabsList>
      <TabsContent value="overview"><OverviewTab orgId={orgId} /></TabsContent>
      <TabsContent value="identity"><AreaFindingsTab area="identity" scanId={latestScanId} /></TabsContent>
      <TabsContent value="endpoint"><AreaFindingsTab area="endpoint" scanId={latestScanId} /></TabsContent>
      <TabsContent value="data"><AreaFindingsTab area="data" scanId={latestScanId} /></TabsContent>
      <TabsContent value="productivity"><AreaFindingsTab area="productivity" scanId={latestScanId} /></TabsContent>
    </Tabs>
  );
}

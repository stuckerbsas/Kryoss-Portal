import { useState } from 'react';
import {
  useCloudAssessment,
  useCloudAssessmentDetail,
  useAzureSubscriptions,
  useSetFindingStatus,
  type CloudAssessmentFinding,
  type FindingRemediationStatus,
} from '@/api/cloudAssessment';
import { useOrgParam } from '@/hooks/useOrgParam';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { Card, CardContent } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Cloud, ExternalLink } from 'lucide-react';
import { OverviewTab } from './OverviewTab';
import { ConnectAzureCard } from './ConnectAzureCard';
import { AzureSubscriptionsList } from './AzureSubscriptionsList';
import { AzureInfrastructureView } from './AzureInfrastructureView';
import { RemediationTab } from './RemediationTab';
import { ComplianceTab } from './ComplianceTab';
import { PowerBiTab } from './PowerBiTab';
import { CopilotReadinessTab } from './CopilotReadinessTab';
import { ConnectProgressModal } from './ConnectProgressModal';

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

type RemediationStatusOption = Exclude<FindingRemediationStatus['status'], 'acknowledged_regression'>;

const AREA_STATUS_LABELS: Record<RemediationStatusOption, string> = {
  open: 'Open',
  in_progress: 'In Progress',
  resolved: 'Resolved',
  deferred: 'Deferred',
};

const AREA_STATUS_OPTIONS: RemediationStatusOption[] = ['open', 'in_progress', 'resolved', 'deferred'];

function AreaInlineStatusSelect({
  finding,
  orgId,
}: {
  finding: CloudAssessmentFinding;
  orgId: string;
}) {
  const setStatus = useSetFindingStatus();
  const currentStatus = finding.remediationStatus?.status ?? 'open';

  const handleChange = (value: string) => {
    setStatus.mutate({
      organizationId: orgId,
      area: finding.area,
      service: finding.service,
      feature: finding.feature,
      status: value as RemediationStatusOption,
    });
  };

  return (
    <Select value={currentStatus} onValueChange={handleChange} disabled={setStatus.isPending}>
      <SelectTrigger className="h-7 text-xs w-32">
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        {AREA_STATUS_OPTIONS.map((s) => (
          <SelectItem key={s} value={s} className="text-xs">
            {AREA_STATUS_LABELS[s]}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}

function AreaFindingsTab({ area, scanId }: { area: string; scanId: string | undefined }) {
  const { orgId } = useOrgParam();
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
              <TableHead>Remediation</TableHead>
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
                <TableCell>
                  {orgId ? (
                    <AreaInlineStatusSelect finding={f} orgId={orgId} />
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

function AzureTab({
  orgId,
  latestScanId,
  hasAzureScanData,
}: {
  orgId: string;
  latestScanId: string | undefined;
  hasAzureScanData: boolean;
}) {
  const { data: subs, isLoading } = useAzureSubscriptions(orgId);
  const [showConnect, setShowConnect] = useState(false);
  const [showManage, setShowManage] = useState(false);

  if (isLoading) {
    return (
      <Card>
        <CardContent className="py-10 text-center text-sm text-muted-foreground">
          Loading…
        </CardContent>
      </Card>
    );
  }

  const hasSubs = subs && subs.length > 0;

  // State 1: no subscriptions connected → ConnectAzureCard.
  if (!hasSubs || showConnect) {
    return <ConnectAzureCard orgId={orgId} onConnected={() => setShowConnect(false)} />;
  }

  // State 3 preference: subs + scan has Azure data → rich infrastructure view.
  // (Unless the user explicitly clicked "Manage subscriptions".)
  if (hasAzureScanData && latestScanId && !showManage) {
    return (
      <AzureInfrastructureView
        orgId={orgId}
        scanId={latestScanId}
        subs={subs!}
        onManageSubscriptions={() => setShowManage(true)}
      />
    );
  }

  // State 2: subs connected but no Azure scan data yet (or user chose to manage).
  return (
    <div className="space-y-4">
      {!hasAzureScanData && (
        <div className="rounded-lg border border-blue-200 bg-blue-50 p-4 text-sm text-blue-800 flex items-start gap-3">
          <Cloud className="h-5 w-5 shrink-0 mt-0.5 text-blue-600" />
          <div>
            <p className="font-medium">No Azure infrastructure data yet</p>
            <p className="text-xs mt-1 text-blue-700">
              Run a scan from the Overview tab to see Azure infrastructure posture.
            </p>
          </div>
        </div>
      )}
      <AzureSubscriptionsList
        orgId={orgId}
        subscriptions={subs!}
        onConnectAnother={() => setShowConnect(true)}
      />
      {showManage && hasAzureScanData && (
        <div className="text-right">
          <button
            className="text-sm text-blue-600 hover:underline"
            onClick={() => setShowManage(false)}
          >
            Back to infrastructure view
          </button>
        </div>
      )}
    </div>
  );
}

export function CloudAssessmentPage() {
  const { orgId } = useOrgParam();
  const { data: summary } = useCloudAssessment(orgId);

  const latestScanId = summary && 'id' in summary ? summary.id : undefined;
  const hasAzureScanData =
    summary !== undefined &&
    'areaScores' in summary &&
    summary.areaScores != null &&
    summary.areaScores['azure'] != null;

  if (!orgId) return null;

  return (
    <div className="space-y-4">
      <ConnectProgressModal orgId={orgId} />
      <Tabs defaultValue="overview" className="space-y-4">
        <TabsList>
        <TabsTrigger value="overview">Overview</TabsTrigger>
        <TabsTrigger value="identity">Identity</TabsTrigger>
        <TabsTrigger value="endpoint">Endpoint</TabsTrigger>
        <TabsTrigger value="data">Data</TabsTrigger>
        <TabsTrigger value="productivity">Productivity</TabsTrigger>
        <TabsTrigger value="azure">Azure</TabsTrigger>
        <TabsTrigger value="powerbi">Power BI</TabsTrigger>
        <TabsTrigger value="copilot">Copilot Readiness</TabsTrigger>
        <TabsTrigger value="compliance">Compliance</TabsTrigger>
        <TabsTrigger value="remediation">Remediation</TabsTrigger>
      </TabsList>
      <TabsContent value="overview"><OverviewTab orgId={orgId} /></TabsContent>
      <TabsContent value="identity"><AreaFindingsTab area="identity" scanId={latestScanId} /></TabsContent>
      <TabsContent value="endpoint"><AreaFindingsTab area="endpoint" scanId={latestScanId} /></TabsContent>
      <TabsContent value="data"><AreaFindingsTab area="data" scanId={latestScanId} /></TabsContent>
      <TabsContent value="productivity"><AreaFindingsTab area="productivity" scanId={latestScanId} /></TabsContent>
      <TabsContent value="azure">
        <AzureTab
          orgId={orgId}
          latestScanId={latestScanId}
          hasAzureScanData={hasAzureScanData}
        />
      </TabsContent>
      <TabsContent value="powerbi"><PowerBiTab orgId={orgId} scanId={latestScanId} /></TabsContent>
      <TabsContent value="copilot"><CopilotReadinessTab orgId={orgId} scanId={latestScanId} /></TabsContent>
      <TabsContent value="compliance"><ComplianceTab orgId={orgId} scanId={latestScanId} /></TabsContent>
      <TabsContent value="remediation"><RemediationTab orgId={orgId} /></TabsContent>
      </Tabs>
    </div>
  );
}

import { useState } from 'react';
import {
  useCloudAssessment,
  useCloudAssessmentDetail,
  useAzureSubscriptions,
  useConnectionStatus,
  useCloudDisconnect,
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
import { Cloud, ExternalLink, CheckCircle2, AlertTriangle, X, Loader2, Unlink } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
} from '@/components/ui/dialog';
import { OverviewTab } from './OverviewTab';
import { ConnectAzureCard } from './ConnectAzureCard';
import { AzureSubscriptionsList } from './AzureSubscriptionsList';
import { AzureInfrastructureView } from './AzureInfrastructureView';
import { RemediationTab } from './RemediationTab';
import { ComplianceTab } from './ComplianceTab';
import { PowerBiTab } from './PowerBiTab';
import { CopilotReadinessTab } from './CopilotReadinessTab';
import { ConnectProgressModal } from './ConnectProgressModal';
import { ConnectCloudWizard } from './ConnectCloudWizard';
import { DataTab } from './DataTab';
import { BenchmarksTab } from './BenchmarksTab';

function statusBadge(status: string) {
  const key = status.toLowerCase();
  const map: Record<string, { color: string; label: string }> = {
    success:         { color: 'bg-green-100 text-green-800', label: 'Success' },
    action_required: { color: 'bg-red-100 text-red-800',    label: 'Action Required' },
    warning:         { color: 'bg-amber-100 text-amber-800', label: 'Warning' },
    critical:        { color: 'bg-red-200 text-red-900',    label: 'Critical' },
    disabled:        { color: 'bg-gray-100 text-gray-600',  label: 'Disabled' },
    not_licensed:    { color: 'bg-gray-100 text-gray-600',  label: 'Not Licensed' },
    insight:         { color: 'bg-blue-100 text-blue-800',  label: 'Insight' },
    informational:   { color: 'bg-blue-100 text-blue-800',  label: 'Informational' },
  };
  const entry = map[key];
  return <Badge variant="secondary" className={entry?.color ?? 'bg-gray-100 text-gray-500'}>{entry?.label ?? status}</Badge>;
}

function priorityBadge(priority: string) {
  const key = priority.toLowerCase();
  const map: Record<string, { color: string; label: string }> = {
    critical:      { color: 'bg-red-100 text-red-800',    label: 'Critical' },
    high:          { color: 'bg-orange-100 text-orange-800', label: 'High' },
    medium:        { color: 'bg-amber-100 text-amber-800', label: 'Medium' },
    low:           { color: 'bg-blue-100 text-blue-800',  label: 'Low' },
    informational: { color: 'bg-gray-100 text-gray-600',  label: 'Informational' },
  };
  const entry = map[key];
  return <Badge variant="secondary" className={entry?.color ?? 'bg-gray-100 text-gray-500'}>{entry?.label ?? priority}</Badge>;
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

export function AreaFindingsTab({ area, scanId }: { area: string; scanId: string | undefined }) {
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

function DisconnectDialog({
  orgId,
  open,
  onOpenChange,
}: {
  orgId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const disconnect = useCloudDisconnect();

  const handleDisconnect = () => {
    disconnect.mutate(
      { organizationId: orgId },
      {
        onSuccess: (data) => {
          const d = data.deleted;
          toast.success(
            `Disconnected: ${d.cloudAssessmentScans} scans, ${d.m365Tenants} tenants, ${d.azureSubscriptions} Azure subs deleted`,
          );
          onOpenChange(false);
          setTimeout(() => window.location.reload(), 500);
        },
        onError: (err: any) => {
          toast.error(`Disconnect failed: ${err.message}`);
        },
      },
    );
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2 text-red-700">
            <Unlink className="h-5 w-5" />
            Disconnect All Cloud Services
          </DialogTitle>
          <DialogDescription className="text-sm text-muted-foreground">
            This will permanently delete all cloud assessment data for this organization:
          </DialogDescription>
        </DialogHeader>
        <ul className="text-sm text-muted-foreground list-disc list-inside space-y-1 ml-2">
          <li>All Cloud Assessment scans and findings</li>
          <li>All Copilot Readiness scans (legacy)</li>
          <li>Azure subscription connections</li>
          <li>Power BI connections</li>
          <li>M365 tenant connection</li>
          <li>Remediation statuses and suggestions</li>
        </ul>
        <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-800">
          This action cannot be undone. You will need to re-connect and re-scan.
        </div>
        <div className="flex justify-end gap-2 mt-2">
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={disconnect.isPending}>
            Cancel
          </Button>
          <Button
            variant="destructive"
            onClick={handleDisconnect}
            disabled={disconnect.isPending}
          >
            {disconnect.isPending ? (
              <>
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                Disconnecting...
              </>
            ) : (
              <>
                <Unlink className="mr-2 h-4 w-4" />
                Disconnect Everything
              </>
            )}
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}

function ConnectionBanner({
  orgId,
  onOpenWizard,
  onDisconnect,
}: {
  orgId: string;
  onOpenWizard: (step: number) => void;
  onDisconnect: () => void;
}) {
  const { data: status } = useConnectionStatus(orgId);
  const [dismissed, setDismissed] = useState(false);

  if (!status || dismissed) return null;

  const graphOk = status.graph === 'connected';
  const azureOk = status.azure === 'connected';
  const pbiOk = status.powerBi === 'connected';
  const pbiNA = status.powerBi === 'unavailable';
  const pbiDone = pbiOk || pbiNA;

  if (!graphOk) return null;

  if (graphOk && azureOk && pbiDone) {
    return (
      <div className="flex items-center justify-between rounded-lg border border-green-200 bg-green-50 px-4 py-2">
        <div className="flex items-center gap-2 text-sm text-green-800">
          <CheckCircle2 className="h-4 w-4" />
          <span className="font-medium">All cloud services connected</span>
          {pbiNA && (
            <Badge variant="secondary" className="bg-gray-100 text-gray-500 text-xs">
              Power BI N/A
            </Badge>
          )}
          <Badge variant="secondary" className="bg-green-100 text-green-700 text-xs">
            {status.connectionPercentage}%
          </Badge>
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={onDisconnect}
            className="text-red-500 hover:text-red-700 text-xs flex items-center gap-1"
          >
            <Unlink className="h-3 w-3" />
            Disconnect
          </button>
          <button onClick={() => setDismissed(true)} className="text-green-600 hover:text-green-800">
            <X className="h-4 w-4" />
          </button>
        </div>
      </div>
    );
  }

  const missing: Array<{ label: string; step: number }> = [];
  if (!azureOk) missing.push({ label: 'Azure', step: 1 });
  if (!pbiDone) missing.push({ label: 'Power BI', step: 2 });

  return (
    <div className="flex items-center justify-between rounded-lg border border-amber-200 bg-amber-50 px-4 py-3">
      <div className="flex items-center gap-2 text-sm text-amber-800">
        <AlertTriangle className="h-4 w-4 shrink-0" />
        <span>
          <span className="font-medium">Enhance your scan:</span>{' '}
          Connect {missing.map((m, i) => (
            <span key={m.label}>
              {i > 0 && ' | '}
              <button
                onClick={() => onOpenWizard(m.step)}
                className="underline hover:text-amber-900 font-medium"
              >
                {m.label}
              </button>
            </span>
          ))} for deeper coverage.
        </span>
      </div>
      <div className="flex items-center gap-2">
        <button
          onClick={onDisconnect}
          className="text-red-500 hover:text-red-700 text-xs flex items-center gap-1"
        >
          <Unlink className="h-3 w-3" />
          Disconnect
        </button>
        <button onClick={() => setDismissed(true)} className="text-amber-600 hover:text-amber-800 ml-1">
          <X className="h-4 w-4" />
        </button>
      </div>
    </div>
  );
}

export function CloudAssessmentPage() {
  const { orgId } = useOrgParam();
  const { data: summary } = useCloudAssessment(orgId);
  const [wizardOpen, setWizardOpen] = useState(false);
  const [wizardStep, setWizardStep] = useState(0);
  const [disconnectOpen, setDisconnectOpen] = useState(false);

  const latestScanId = summary && 'id' in summary ? summary.id : undefined;
  const hasAzureScanData =
    summary !== undefined &&
    'areaScores' in summary &&
    summary.areaScores != null &&
    summary.areaScores['azure'] != null;

  const openWizard = (step: number) => {
    setWizardStep(step);
    setWizardOpen(true);
  };

  if (!orgId) return null;

  return (
    <div className="space-y-4">
      <ConnectProgressModal orgId={orgId} />
      <ConnectionBanner orgId={orgId} onOpenWizard={openWizard} onDisconnect={() => setDisconnectOpen(true)} />
      <DisconnectDialog orgId={orgId} open={disconnectOpen} onOpenChange={setDisconnectOpen} />
      <ConnectCloudWizard
        orgId={orgId}
        open={wizardOpen}
        onOpenChange={setWizardOpen}
        initialStep={wizardStep}
      />
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
        <TabsTrigger value="benchmarks">Benchmarks</TabsTrigger>
        <TabsTrigger value="remediation">Remediation</TabsTrigger>
      </TabsList>
      <TabsContent value="overview"><OverviewTab orgId={orgId} /></TabsContent>
      <TabsContent value="identity"><AreaFindingsTab area="identity" scanId={latestScanId} /></TabsContent>
      <TabsContent value="endpoint"><AreaFindingsTab area="endpoint" scanId={latestScanId} /></TabsContent>
      <TabsContent value="data"><DataTab scanId={latestScanId} /></TabsContent>
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
      <TabsContent value="benchmarks"><BenchmarksTab orgId={orgId} scanId={latestScanId} /></TabsContent>
      <TabsContent value="remediation"><RemediationTab orgId={orgId} /></TabsContent>
      </Tabs>
    </div>
  );
}

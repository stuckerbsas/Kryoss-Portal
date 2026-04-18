import { useState } from 'react';
import { AlertTriangle, CheckCircle2, Clock, XCircle, ChevronDown, Wrench } from 'lucide-react';
import { toast } from 'sonner';
import {
  useFindingStatuses,
  useRemediationSuggestions,
  useRemediationStats,
  useSetFindingStatus,
  useDismissSuggestion,
  type FindingRemediationStatus,
  type RemediationSuggestion,
} from '@/api/cloudAssessment';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';

// ── Helpers ──

type FindingStatus = FindingRemediationStatus['status'];

const STATUS_LABELS: Record<FindingStatus, string> = {
  open: 'Open',
  in_progress: 'In Progress',
  resolved: 'Resolved',
  deferred: 'Deferred',
  acknowledged_regression: 'Acknowledged',
};

const STATUS_BADGE_CLASSES: Record<FindingStatus, string> = {
  open: 'bg-gray-100 text-gray-700',
  in_progress: 'bg-blue-100 text-blue-800',
  resolved: 'bg-green-100 text-green-800',
  deferred: 'bg-yellow-100 text-yellow-800',
  acknowledged_regression: 'bg-purple-100 text-purple-800',
};

function formatDate(s: string | null | undefined): string {
  if (!s) return '—';
  return new Date(s).toLocaleDateString();
}

function truncate(s: string | null | undefined, maxLen: number): string {
  if (!s) return '—';
  return s.length > maxLen ? `${s.slice(0, maxLen)}…` : s;
}

// ── Stat cards ──

interface StatCardProps {
  label: string;
  count: number;
  icon: React.ReactNode;
  badgeClass: string;
}

function StatCard({ label, count, icon, badgeClass }: StatCardProps) {
  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between pb-2 pt-4 px-4">
        <CardTitle className="text-sm font-medium text-muted-foreground">{label}</CardTitle>
        {icon}
      </CardHeader>
      <CardContent className="px-4 pb-4">
        <div className="text-3xl font-bold">{count}</div>
        <Badge variant="secondary" className={`mt-1 text-xs ${badgeClass}`}>{label}</Badge>
      </CardContent>
    </Card>
  );
}

// ── Suggestion banner row ──

interface SuggestionRowProps {
  suggestion: RemediationSuggestion;
  actionLabel: string;
  actionStatus: FindingStatus;
  orgId: string;
}

function SuggestionRow({ suggestion, actionLabel, actionStatus, orgId }: SuggestionRowProps) {
  const setStatus = useSetFindingStatus();
  const dismiss = useDismissSuggestion();

  const handleAction = () => {
    setStatus.mutate(
      {
        organizationId: orgId,
        area: suggestion.area,
        service: suggestion.service,
        feature: suggestion.feature,
        status: actionStatus,
      },
      {
        onSuccess: () => toast.success(`Finding marked as ${STATUS_LABELS[actionStatus]}.`),
        onError: () => toast.error('Failed to update finding status.'),
      },
    );
  };

  const handleDismiss = () => {
    dismiss.mutate(
      { suggestionId: suggestion.id, organizationId: orgId },
      {
        onSuccess: () => toast.success('Suggestion dismissed.'),
        onError: () => toast.error('Failed to dismiss suggestion.'),
      },
    );
  };

  return (
    <div className="flex items-center justify-between gap-4 py-1.5 text-sm">
      <div className="flex items-center gap-2 min-w-0">
        <span className="font-medium capitalize shrink-0">{suggestion.area}</span>
        <span className="text-muted-foreground shrink-0">/</span>
        <span className="shrink-0">{suggestion.service}</span>
        <span className="text-muted-foreground shrink-0">/</span>
        <span className="truncate">{suggestion.feature}</span>
      </div>
      <div className="flex items-center gap-2 shrink-0">
        <Button
          size="sm"
          variant="outline"
          onClick={handleAction}
          disabled={setStatus.isPending}
        >
          {actionLabel}
        </Button>
        <Button
          size="sm"
          variant="ghost"
          onClick={handleDismiss}
          disabled={dismiss.isPending}
        >
          Dismiss
        </Button>
      </div>
    </div>
  );
}

// ── Collapsible suggestion banner ──

interface SuggestionBannerProps {
  suggestions: RemediationSuggestion[];
  type: 'likely_resolved' | 'possible_regression';
  orgId: string;
}

function SuggestionBanner({ suggestions, type, orgId }: SuggestionBannerProps) {
  const filtered = suggestions.filter((s) => s.suggestionType === type);
  const defaultExpanded = filtered.length <= 5;
  const [expanded, setExpanded] = useState(defaultExpanded);

  if (filtered.length === 0) return null;

  const isRegression = type === 'possible_regression';
  const bannerClass = isRegression
    ? 'border border-red-200 bg-red-50'
    : 'border border-amber-200 bg-amber-50';
  const headerTextClass = isRegression ? 'text-red-800' : 'text-amber-800';
  const chevronClass = isRegression ? 'text-red-500' : 'text-amber-500';
  const dividerClass = isRegression ? 'border-red-100' : 'border-amber-100';

  const headerText = isRegression
    ? `${filtered.length} finding${filtered.length !== 1 ? 's' : ''} may have regressed — review and acknowledge`
    : `${filtered.length} finding${filtered.length !== 1 ? 's' : ''} may have been resolved — confirm to close them`;

  const actionLabel = isRegression ? 'Acknowledge Regression' : 'Mark Resolved';
  const actionStatus: FindingStatus = isRegression ? 'acknowledged_regression' : 'resolved';

  return (
    <div className={`rounded-lg ${bannerClass} p-4`}>
      <button
        className={`flex items-center justify-between w-full text-left gap-2 ${headerTextClass}`}
        onClick={() => setExpanded((v) => !v)}
      >
        <span className="text-sm font-medium">{headerText}</span>
        <ChevronDown
          className={`h-4 w-4 shrink-0 transition-transform ${chevronClass} ${expanded ? 'rotate-180' : ''}`}
        />
      </button>
      {expanded && (
        <div className={`mt-3 border-t ${dividerClass} pt-3 space-y-1`}>
          {filtered.map((s) => (
            <SuggestionRow
              key={s.id}
              suggestion={s}
              actionLabel={actionLabel}
              actionStatus={actionStatus}
              orgId={orgId}
            />
          ))}
        </div>
      )}
    </div>
  );
}

// ── Inline status select ──

interface InlineStatusSelectProps {
  finding: FindingRemediationStatus;
  orgId: string;
}

const ALL_STATUSES: FindingStatus[] = [
  'open',
  'in_progress',
  'resolved',
  'deferred',
  'acknowledged_regression',
];

function InlineStatusSelect({ finding, orgId }: InlineStatusSelectProps) {
  const setStatus = useSetFindingStatus();

  const handleChange = (value: string) => {
    setStatus.mutate(
      {
        organizationId: orgId,
        area: finding.area,
        service: finding.service,
        feature: finding.feature,
        status: value as FindingStatus,
      },
      {
        onSuccess: () => toast.success(`Status updated to "${STATUS_LABELS[value as FindingStatus]}".`),
        onError: () => toast.error('Failed to update status.'),
      },
    );
  };

  return (
    <Select value={finding.status} onValueChange={handleChange} disabled={setStatus.isPending}>
      <SelectTrigger className="h-7 text-xs w-36">
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        {ALL_STATUSES.map((s) => (
          <SelectItem key={s} value={s} className="text-xs">
            {STATUS_LABELS[s]}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}

// ── Fix modal ──

interface FixModalProps {
  open: boolean;
  finding: FindingRemediationStatus | null;
  onClose: () => void;
}

function FixModal({ open, finding, onClose }: FixModalProps) {
  return (
    <Dialog open={open} onOpenChange={(v) => !v && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Apply Remediation Fix</DialogTitle>
          <DialogDescription>Review the proposed changes before applying.</DialogDescription>
        </DialogHeader>
        {finding && (
          <div className="text-sm text-muted-foreground space-y-1">
            <p>
              <span className="font-medium text-foreground">Area:</span>{' '}
              <span className="capitalize">{finding.area}</span>
            </p>
            <p>
              <span className="font-medium text-foreground">Service:</span> {finding.service}
            </p>
            <p>
              <span className="font-medium text-foreground">Feature:</span> {finding.feature}
            </p>
          </div>
        )}
        <div className="rounded-md border border-dashed p-4 text-sm text-muted-foreground">
          Fix preview coming in a future release. This will show the exact commands or API calls
          that will be executed.
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Cancel
          </Button>
          <Button disabled title="Coming soon">
            <Wrench className="mr-1.5 h-4 w-4" />
            Apply Fix
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ── Main component ──

interface RemediationTabProps {
  orgId: string;
}

export function RemediationTab({ orgId }: RemediationTabProps) {
  const [statusFilter, setStatusFilter] = useState<string>('all');
  const [fixModalOpen, setFixModalOpen] = useState(false);
  const [fixModalFinding, setFixModalFinding] = useState<FindingRemediationStatus | null>(null);

  const { data: stats, isLoading: statsLoading } = useRemediationStats(orgId);
  const { data: suggestions } = useRemediationSuggestions(orgId);
  const {
    data: findings,
    isLoading: findingsLoading,
  } = useFindingStatuses(orgId, undefined, statusFilter === 'all' ? undefined : statusFilter);

  const openFixModal = (finding: FindingRemediationStatus) => {
    setFixModalFinding(finding);
    setFixModalOpen(true);
  };

  // ── Stats row ──

  const statsTotal = stats
    ? stats.open + stats.inProgress + stats.resolved + stats.deferred
    : 0;

  const showEmptyState = !statsLoading && (!stats || statsTotal === 0);

  return (
    <div className="space-y-6">
      {/* Stats row */}
      {statsLoading ? (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {Array.from({ length: 4 }).map((_, i) => (
            <Skeleton key={i} className="h-28" />
          ))}
        </div>
      ) : showEmptyState ? (
        <Card>
          <CardContent className="flex flex-col items-center justify-center py-12 text-center gap-3">
            <CheckCircle2 className="h-10 w-10 text-muted-foreground" />
            <div>
              <p className="font-semibold">No remediation activity yet</p>
              <p className="text-sm text-muted-foreground mt-1">
                Statuses will appear here once you start tracking findings.
              </p>
            </div>
          </CardContent>
        </Card>
      ) : (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <StatCard
            label="Open"
            count={stats?.open ?? 0}
            icon={<XCircle className="h-4 w-4 text-red-500" />}
            badgeClass="bg-red-100 text-red-700"
          />
          <StatCard
            label="In Progress"
            count={stats?.inProgress ?? 0}
            icon={<Clock className="h-4 w-4 text-blue-500" />}
            badgeClass="bg-blue-100 text-blue-700"
          />
          <StatCard
            label="Resolved"
            count={stats?.resolved ?? 0}
            icon={<CheckCircle2 className="h-4 w-4 text-green-500" />}
            badgeClass="bg-green-100 text-green-700"
          />
          <StatCard
            label="Deferred"
            count={stats?.deferred ?? 0}
            icon={<AlertTriangle className="h-4 w-4 text-gray-400" />}
            badgeClass="bg-gray-100 text-gray-600"
          />
        </div>
      )}

      {/* Suggestion banners */}
      {suggestions && suggestions.length > 0 && (
        <div className="space-y-3">
          <SuggestionBanner suggestions={suggestions} type="likely_resolved" orgId={orgId} />
          <SuggestionBanner suggestions={suggestions} type="possible_regression" orgId={orgId} />
        </div>
      )}

      {/* Filter bar */}
      <div className="flex items-center gap-3">
        <span className="text-sm font-medium text-muted-foreground shrink-0">Filter by status</span>
        <Select value={statusFilter} onValueChange={setStatusFilter}>
          <SelectTrigger className="w-44">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All</SelectItem>
            <SelectItem value="open">Open</SelectItem>
            <SelectItem value="in_progress">In Progress</SelectItem>
            <SelectItem value="resolved">Resolved</SelectItem>
            <SelectItem value="deferred">Deferred</SelectItem>
            <SelectItem value="acknowledged_regression">Acknowledged</SelectItem>
          </SelectContent>
        </Select>
      </div>

      {/* Findings table */}
      <Card>
        <CardContent className="p-0 overflow-x-auto">
          {findingsLoading ? (
            <div className="space-y-2 p-4">
              {Array.from({ length: 3 }).map((_, i) => (
                <Skeleton key={i} className="h-10 w-full" />
              ))}
            </div>
          ) : !findings || findings.length === 0 ? (
            <div className="py-12 text-center text-sm text-muted-foreground">
              No findings tracked yet. Statuses will appear here once you set them from the findings
              tabs.
            </div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Area</TableHead>
                  <TableHead>Service</TableHead>
                  <TableHead>Feature</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Owner</TableHead>
                  <TableHead>Notes</TableHead>
                  <TableHead>Last Updated</TableHead>
                  <TableHead className="w-24">Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {findings.map((f) => (
                  <TableRow key={f.id}>
                    <TableCell className="text-sm capitalize whitespace-nowrap">{f.area}</TableCell>
                    <TableCell className="text-sm whitespace-nowrap">{f.service}</TableCell>
                    <TableCell className="text-sm">{f.feature}</TableCell>
                    <TableCell>
                      <InlineStatusSelect finding={f} orgId={orgId} />
                    </TableCell>
                    <TableCell className="text-sm text-muted-foreground font-mono">
                      {f.ownerUserId ? f.ownerUserId.slice(0, 8) : '—'}
                    </TableCell>
                    <TableCell
                      className="text-sm text-muted-foreground max-w-[160px]"
                      title={f.notes ?? undefined}
                    >
                      {truncate(f.notes, 40)}
                    </TableCell>
                    <TableCell className="text-sm text-muted-foreground whitespace-nowrap">
                      {formatDate(f.updatedAt)}
                    </TableCell>
                    <TableCell>
                      <Button
                        size="sm"
                        variant="outline"
                        disabled
                        onClick={() => openFixModal(f)}
                        className="gap-1.5"
                      >
                        <Wrench className="h-3.5 w-3.5" />
                        Apply Fix
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>

      {/* Fix modal */}
      <FixModal
        open={fixModalOpen}
        finding={fixModalFinding}
        onClose={() => {
          setFixModalOpen(false);
          setFixModalFinding(null);
        }}
      />
    </div>
  );
}

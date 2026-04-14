import { useState } from 'react';
import {
  Activity,
  AlertTriangle,
  CheckCircle2,
  Loader2,
  Shield,
  ShieldOff,
  Users,
  XCircle,
} from 'lucide-react';
import { toast } from 'sonner';
import { useParams } from 'react-router-dom';
import {
  useOrganization,
  useToggleProtocolAudit,
} from '@/api/organizations';
import { Button } from '@/components/ui/button';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';

/**
 * v1.5.1 — Protocol Usage Audit dashboard.
 *
 * Shows the MSP:
 *   1. A toggle to enable/disable NTLM+SMBv1 auditing across the org's fleet
 *   2. Once enabled, a 90-day retention banner
 *   3. (Future) Per-machine metrics from AUDIT-* and NTLM-USE-* / SMB1-USE-*
 *      controls once data flows in
 */
export function ProtocolUsageTab() {
  const { orgId: orgSlug } = useParams<{ orgId: string }>();
  const { data: org, isLoading } = useOrganization(orgSlug);
  const toggleMutation = useToggleProtocolAudit();
  const [confirming, setConfirming] = useState(false);

  if (isLoading) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-40" />
        <Skeleton className="h-48" />
      </div>
    );
  }

  if (!org) {
    return (
      <div className="text-sm text-muted-foreground">
        Organization not found.
      </div>
    );
  }

  const enabled = org.protocolAuditEnabled ?? false;
  const enabledAt = org.protocolAuditEnabledAt
    ? new Date(org.protocolAuditEnabledAt)
    : null;
  const daysSinceEnabled = enabledAt
    ? Math.floor(
        (Date.now() - enabledAt.getTime()) / (1000 * 60 * 60 * 24),
      )
    : 0;
  const retentionComplete = enabled && daysSinceEnabled >= 90;

  const handleToggle = async () => {
    if (!enabled && !confirming) {
      setConfirming(true);
      return;
    }
    try {
      await toggleMutation.mutateAsync({ id: org.id, enabled: !enabled });
      toast.success(
        enabled
          ? 'Protocol audit disabled.'
          : 'Protocol audit enabled. Agents will configure on next run.',
      );
      setConfirming(false);
    } catch (err: any) {
      toast.error(`Toggle failed: ${err.message}`);
    }
  };

  return (
    <div className="space-y-6">
      {/* ── Header + toggle ── */}
      <Card>
        <CardHeader>
          <div className="flex items-start justify-between gap-4">
            <div>
              <CardTitle className="flex items-center gap-2 text-lg">
                <Activity className="h-5 w-5 text-blue-500" />
                Protocol Usage Audit
              </CardTitle>
              <CardDescription className="mt-1 max-w-2xl">
                Measure NTLM and SMBv1 usage across the fleet for 90 days
                before deprecating. The Kryoss Agent configures native Windows
                audit logging on each enrolled machine, enabling safe,
                evidence-based protocol deprecation decisions.
              </CardDescription>
            </div>
            <div className="flex flex-col items-end gap-2">
              {enabled ? (
                <Badge className="bg-green-100 text-green-800 border-green-300">
                  <Shield className="h-3.5 w-3.5 mr-1" />
                  Enabled
                </Badge>
              ) : (
                <Badge variant="secondary">
                  <ShieldOff className="h-3.5 w-3.5 mr-1" />
                  Disabled
                </Badge>
              )}
              {enabledAt && (
                <span className="text-xs text-muted-foreground">
                  Since {enabledAt.toLocaleDateString()} ({daysSinceEnabled}{' '}
                  days)
                </span>
              )}
            </div>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          {/* Confirmation alert (when enabling) */}
          {confirming && !enabled && (
            <div className="rounded-lg border border-amber-200 bg-amber-50 p-4 text-sm text-amber-900 space-y-2">
              <p className="font-medium flex items-center gap-2">
                <AlertTriangle className="h-4 w-4" />
                Confirm protocol audit activation
              </p>
              <p>
                Enabling protocol audit will cause the Kryoss Agent to write
                these registry values on every enrolled machine at next run:
              </p>
              <ul className="list-disc list-inside font-mono text-xs space-y-0.5">
                <li>
                  HKLM\SYSTEM\...\Lsa\MSV1_0\AuditReceivingNTLMTraffic = 2
                </li>
                <li>
                  HKLM\SYSTEM\...\Lsa\MSV1_0\RestrictSendingNTLMTraffic = 1
                </li>
                <li>
                  HKLM\SYSTEM\...\LanmanServer\Parameters\AuditSmb1Access = 1
                </li>
              </ul>
              <p>
                Event logs will be resized: Security to 500 MB,
                Microsoft-Windows-NTLM/Operational and SMBServer/Audit to 300
                MB each. This consumes roughly 1.1 GB per machine for 90-day
                retention.
              </p>
              <p className="font-medium">
                This is the only registry write the agent performs. Continue?
              </p>
              <div className="flex gap-2 pt-1">
                <Button
                  onClick={handleToggle}
                  disabled={toggleMutation.isPending}
                  size="sm"
                >
                  {toggleMutation.isPending ? (
                    <Loader2 className="h-4 w-4 animate-spin mr-1.5" />
                  ) : null}
                  Yes, enable protocol audit
                </Button>
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => setConfirming(false)}
                >
                  Cancel
                </Button>
              </div>
            </div>
          )}

          {/* Normal toggle button */}
          {!confirming && (
            <Button
              onClick={handleToggle}
              disabled={toggleMutation.isPending}
              variant={enabled ? 'outline' : 'default'}
            >
              {toggleMutation.isPending ? (
                <Loader2 className="h-4 w-4 animate-spin mr-1.5" />
              ) : enabled ? (
                <ShieldOff className="h-4 w-4 mr-1.5" />
              ) : (
                <Shield className="h-4 w-4 mr-1.5" />
              )}
              {enabled ? 'Disable Protocol Audit' : 'Enable Protocol Audit'}
            </Button>
          )}
        </CardContent>
      </Card>

      {/* ── Status / retention progress ── */}
      {enabled && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">90-Day Retention Window</CardTitle>
            <CardDescription>
              Kryoss collects NTLM and SMB1 events for a full 90 days before
              recommending deprecation.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="space-y-2">
              <div className="flex justify-between text-sm">
                <span>{daysSinceEnabled} / 90 days</span>
                <span className="font-medium">
                  {retentionComplete
                    ? 'Complete'
                    : `${90 - daysSinceEnabled} days remaining`}
                </span>
              </div>
              <div className="h-2 bg-gray-100 rounded-full overflow-hidden">
                <div
                  className={
                    retentionComplete ? 'bg-green-500' : 'bg-blue-500'
                  }
                  style={{
                    width: `${Math.min(100, (daysSinceEnabled / 90) * 100)}%`,
                    height: '100%',
                  }}
                />
              </div>
            </div>

            {retentionComplete ? (
              <div className="flex items-start gap-2 rounded-lg border border-green-200 bg-green-50 p-3 text-sm text-green-900">
                <CheckCircle2 className="h-4 w-4 mt-0.5 flex-shrink-0" />
                <div>
                  <p className="font-medium">Retention window complete</p>
                  <p className="text-xs mt-0.5">
                    You can now safely evaluate NTLM/SMB1 usage metrics below.
                    Zero events in the window means the protocol is safe to
                    disable.
                  </p>
                </div>
              </div>
            ) : (
              <div className="flex items-start gap-2 rounded-lg border border-blue-200 bg-blue-50 p-3 text-sm text-blue-900">
                <Activity className="h-4 w-4 mt-0.5 flex-shrink-0" />
                <div>
                  <p className="font-medium">Collection in progress</p>
                  <p className="text-xs mt-0.5">
                    Metrics will populate as agents report findings. Re-check
                    this tab after each scheduled scan cycle.
                  </p>
                </div>
              </div>
            )}
          </CardContent>
        </Card>
      )}

      {/* ── Placeholder for future metrics ── */}
      {enabled && (
        <div className="grid gap-4 sm:grid-cols-2">
          <Card>
            <CardHeader className="pb-3">
              <CardTitle className="text-sm font-medium text-muted-foreground flex items-center gap-2">
                <Users className="h-4 w-4" />
                NTLM Usage
              </CardTitle>
            </CardHeader>
            <CardContent>
              <p className="text-xs text-muted-foreground">
                Once agents report AUDIT-001 / NTLM-USE-001..004, this card
                will show outbound/inbound event counts and top source users.
              </p>
            </CardContent>
          </Card>

          <Card>
            <CardHeader className="pb-3">
              <CardTitle className="text-sm font-medium text-muted-foreground flex items-center gap-2">
                <XCircle className="h-4 w-4" />
                SMBv1 Usage
              </CardTitle>
            </CardHeader>
            <CardContent>
              <p className="text-xs text-muted-foreground">
                Once agents report AUDIT-003 / SMB1-USE-001..002, this card
                will show access attempt counts and top client IPs.
              </p>
            </CardContent>
          </Card>
        </div>
      )}

      {/* ── Disabled state: pitch the feature ── */}
      {!enabled && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Why measure first?</CardTitle>
          </CardHeader>
          <CardContent className="space-y-3 text-sm text-muted-foreground">
            <p>
              Disabling NTLM or SMBv1 without measurement causes outages:
              legacy printers, scanners, backup agents, and monitoring tools
              often still use these protocols in 2026.
            </p>
            <p>
              Kryoss Protocol Audit solves this by running native Windows
              audit logging for 90 days, producing a defensible report that
              tells you exactly which users and hosts are still using legacy
              protocols — so you can fix them before turning things off.
            </p>
            <p className="font-medium text-foreground">
              Enterprise-grade methodology, one-click activation.
            </p>
          </CardContent>
        </Card>
      )}
    </div>
  );
}

import { useState } from 'react';
import { useOrgParam } from '@/hooks/useOrgParam';
import { Copy, Key, RefreshCw, Check } from 'lucide-react';
import {
  useEnrollmentCodes,
  useCreateEnrollmentCode,
} from '@/api/enrollment';
import { useOrganization } from '@/api/organizations';
import { Can } from '@/components/auth/Can';
import { EmptyState } from '@/components/shared/EmptyState';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';

function CopyButton({ text }: { text: string }) {
  const [copied, setCopied] = useState(false);

  const handleCopy = async () => {
    await navigator.clipboard.writeText(text);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <Button variant="outline" size="sm" onClick={handleCopy}>
      {copied ? (
        <>
          <Check className="h-4 w-4 mr-1 text-green-600" />
          Copied
        </>
      ) : (
        <>
          <Copy className="h-4 w-4 mr-1" />
          Copy
        </>
      )}
    </Button>
  );
}

export function EnrollmentTab() {
  const { orgId, orgSlug } = useOrgParam();
  const { data: org } = useOrganization(orgSlug);
  const { data: codes, isLoading } = useEnrollmentCodes(orgId);
  const createCode = useCreateEnrollmentCode();
  const [refreshing, setRefreshing] = useState(false);

  // Find the active multi-use code (not expired, has uses left)
  const activeCode = codes?.find(
    (c) => !c.isExpired && !c.isUsed,
  );

  const handleRefresh = async () => {
    if (!orgId) return;
    setRefreshing(true);
    try {
      await createCode.mutateAsync({
        organizationId: orgId,
        label: 'Agent enrollment',
        expiryDays: 30,
        maxUses: 999,
      });
    } finally {
      setRefreshing(false);
    }
  };

  // Auto-create if no active code exists
  const handleAutoCreate = async () => {
    if (!orgId) return;
    await createCode.mutateAsync({
      organizationId: orgId,
      label: 'Agent enrollment',
      expiryDays: 30,
      maxUses: 999,
    });
  };

  if (isLoading) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-8 w-64" />
        <Skeleton className="h-40 w-full max-w-lg" />
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Header with org name */}
      <div>
        <h2 className="text-lg font-semibold">
          Enrollment — {org?.name ?? 'Organization'}
        </h2>
        <p className="text-sm text-muted-foreground">
          Use this code to enroll machines into the organization.
        </p>
      </div>

      {/* Active code card */}
      {activeCode ? (
        <Card className="p-6 max-w-lg">
          <div className="space-y-4">
            <div className="flex items-center justify-between">
              <span className="text-sm text-muted-foreground">Active enrollment code</span>
              <Can permission="enrollment:create">
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={handleRefresh}
                  disabled={refreshing}
                >
                  <RefreshCw className={`h-4 w-4 mr-1 ${refreshing ? 'animate-spin' : ''}`} />
                  {refreshing ? 'Generating...' : 'Refresh'}
                </Button>
              </Can>
            </div>

            <div className="flex items-center gap-3">
              <code className="text-3xl font-mono font-bold tracking-widest select-all">
                {activeCode.code}
              </code>
              <CopyButton text={activeCode.code} />
            </div>

            <div className="text-xs text-muted-foreground space-y-1">
              <p>Expires: {new Date(activeCode.expiresAt).toLocaleDateString()}</p>
              {activeCode.label && <p>Label: {activeCode.label}</p>}
            </div>

            <div className="rounded-md bg-muted p-4 text-sm space-y-2">
              <p className="font-medium">Quick start:</p>
              <div className="font-mono text-xs bg-background px-3 py-2 rounded border">
                KryossAgent.exe --code {activeCode.code}
              </div>
              <p className="text-xs text-muted-foreground">
                Or download the pre-configured agent from the Download Agent button above.
              </p>
            </div>
          </div>
        </Card>
      ) : (
        <Card className="p-6 max-w-lg">
          <EmptyState
            icon={<Key className="h-10 w-10" />}
            title="No active enrollment code"
            description="Generate a code to start enrolling machines."
            action={
              <Can permission="enrollment:create">
                <Button onClick={handleAutoCreate} disabled={createCode.isPending}>
                  <Key className="h-4 w-4 mr-1" />
                  {createCode.isPending ? 'Generating...' : 'Generate Code'}
                </Button>
              </Can>
            }
          />
        </Card>
      )}
    </div>
  );
}

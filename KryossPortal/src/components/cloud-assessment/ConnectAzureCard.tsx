import { useState } from 'react';
import { toast } from 'sonner';
import { Check, Copy, ExternalLink, Loader2, ShieldCheck } from 'lucide-react';
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  useAzureConnect,
  useAzureVerify,
  type AzureConnectInstructions,
} from '@/api/cloudAssessment';

const GUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function StepBadge({ n, active }: { n: number; active: boolean }) {
  return (
    <div
      className={`flex h-7 w-7 shrink-0 items-center justify-center rounded-full text-xs font-semibold ${
        active
          ? 'bg-green-600 text-white'
          : 'bg-gray-200 text-gray-600'
      }`}
    >
      {n}
    </div>
  );
}

export function ConnectAzureCard({
  orgId,
  onConnected,
}: {
  orgId: string;
  onConnected?: () => void;
}) {
  const [tenantId, setTenantId] = useState('');
  const [instructions, setInstructions] = useState<AzureConnectInstructions | null>(null);
  const [copied, setCopied] = useState(false);

  const connect = useAzureConnect();
  const verify = useAzureVerify();

  const tenantIdValid = GUID_RE.test(tenantId.trim());

  const handleGenerate = () => {
    if (!tenantIdValid) {
      toast.error('Enter a valid Entra tenant ID (GUID).');
      return;
    }
    connect.mutate(
      { organizationId: orgId, tenantId: tenantId.trim() },
      {
        onSuccess: (data) => {
          setInstructions(data);
        },
        onError: (err: Error) => {
          toast.error(`Failed to generate instructions: ${err.message}`);
        },
      },
    );
  };

  const handleCopy = () => {
    if (!instructions?.azCliCommand) return;
    navigator.clipboard
      .writeText(instructions.azCliCommand)
      .then(() => {
        setCopied(true);
        setTimeout(() => setCopied(false), 2000);
      })
      .catch(() => toast.error('Clipboard write failed.'));
  };

  const handleVerify = () => {
    if (!tenantIdValid) {
      toast.error('Tenant ID missing.');
      return;
    }
    verify.mutate(
      { organizationId: orgId, tenantId: tenantId.trim() },
      {
        onSuccess: (data) => {
          if (data.connected && data.subscriptions && data.subscriptions.length > 0) {
            toast.success(`Found ${data.subscriptions.length} subscription(s).`);
            onConnected?.();
          } else if (data.connected) {
            const missing = data.missingRoles && data.missingRoles.length > 0
              ? ` Missing roles: ${data.missingRoles.join(', ')}.`
              : '';
            const msg = data.message ?? 'Consent granted, but no subscriptions visible yet. Assign Reader role first.';
            toast.warning(`${msg}${missing}`);
          } else if (data.error) {
            toast.error(`Verification error: ${data.error}`);
          } else {
            toast.error(data.message ?? 'Verification failed for an unknown reason.');
          }
        },
        onError: (err: Error) => {
          toast.error(`Verification failed: ${err.message}`);
        },
      },
    );
  };

  return (
    <Card className="max-w-3xl">
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <ShieldCheck className="h-5 w-5 text-green-600" />
          Connect Azure Subscription
        </CardTitle>
        <CardDescription>
          Grant the Kryoss scanner read-only access to one or more Azure subscriptions.
          All actions happen in the customer's tenant; Kryoss never stores credentials.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-6">
        {/* Step 1 — Tenant ID */}
        <div className="flex gap-4">
          <StepBadge n={1} active={tenantIdValid} />
          <div className="flex-1 space-y-2">
            <Label htmlFor="azure-tenant-id" className="text-sm font-medium">
              Identify your Azure subscription
            </Label>
            <p className="text-xs text-muted-foreground">
              Paste the customer's Entra tenant ID (Directory ID). You can find it in
              the Azure portal under "Microsoft Entra ID &gt; Overview".
            </p>
            <Input
              id="azure-tenant-id"
              placeholder="Entra tenant ID (GUID)"
              value={tenantId}
              onChange={(e) => setTenantId(e.target.value)}
              className="font-mono text-sm"
            />
            {tenantId && !tenantIdValid && (
              <p className="text-xs text-red-600">
                Format: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
              </p>
            )}
          </div>
        </div>

        {/* Step 2 — Generate instructions */}
        <div className="flex gap-4">
          <StepBadge n={2} active={!!instructions} />
          <div className="flex-1 space-y-3">
            <Label className="text-sm font-medium">Grant read-only access</Label>
            <p className="text-xs text-muted-foreground">
              Click below to generate the exact az CLI command the customer's admin
              should run. The command grants the Reader role to the Kryoss scanner.
            </p>
            <Button
              onClick={handleGenerate}
              disabled={!tenantIdValid || connect.isPending}
              size="sm"
            >
              {connect.isPending ? (
                <>
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                  Generating...
                </>
              ) : (
                'Generate instructions'
              )}
            </Button>

            {instructions && (
              <div className="space-y-3 pt-2">
                <div className="rounded-md border bg-muted/50 p-3 text-xs space-y-1">
                  <div>
                    <span className="font-medium">App ID: </span>
                    <span className="font-mono">{instructions.appId}</span>
                  </div>
                  <div>
                    <span className="font-medium">Service Principal Object ID: </span>
                    {instructions.servicePrincipalObjectId ? (
                      <span className="font-mono">{instructions.servicePrincipalObjectId}</span>
                    ) : (
                      <span className="text-amber-700 italic">
                        Not yet provisioned in this tenant — the az CLI command below will create it.
                      </span>
                    )}
                  </div>
                </div>

                <div className="relative">
                  <pre className="bg-muted rounded p-3 text-xs overflow-x-auto pr-12">
                    {instructions.azCliCommand}
                  </pre>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    onClick={handleCopy}
                    aria-label={copied ? 'Copied to clipboard' : 'Copy command'}
                    className="absolute top-2 right-2 h-7 px-2"
                  >
                    {copied ? (
                      <Check className="h-4 w-4 text-green-600" />
                    ) : (
                      <Copy className="h-4 w-4" />
                    )}
                  </Button>
                </div>

                <div className="text-xs">
                  <a
                    href={instructions.portalUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="inline-flex items-center gap-1 text-blue-600 hover:underline"
                  >
                    <ExternalLink className="h-3 w-3" />
                    Open Azure portal (alternative manual assignment)
                  </a>
                </div>

                {instructions.spnResolutionNote && (
                  <p className="text-xs text-muted-foreground">
                    {instructions.spnResolutionNote}
                  </p>
                )}
              </div>
            )}
          </div>
        </div>

        {/* Step 3 — Verify */}
        <div className="flex gap-4">
          <StepBadge n={3} active={!!instructions} />
          <div className="flex-1 space-y-3">
            <Label className="text-sm font-medium">Verify connection</Label>
            <p className="text-xs text-muted-foreground">
              After the customer runs the command, click below to enumerate subscriptions
              visible to the Kryoss scanner.
            </p>
            <Button
              onClick={handleVerify}
              disabled={!instructions || verify.isPending}
              size="sm"
              variant="default"
            >
              {verify.isPending ? (
                <>
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                  Verifying...
                </>
              ) : (
                'Verify connection'
              )}
            </Button>
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

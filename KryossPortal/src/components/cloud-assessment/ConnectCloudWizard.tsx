import { useState } from 'react';
import { toast } from 'sonner';
import {
  CheckCircle2,
  Circle,
  Loader2,
  ExternalLink,
  Copy,
  Check,
  Cloud,
  BarChart3,
  Shield,
} from 'lucide-react';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  useConnectionStatus,
  useUnifiedCloudConnectUrl,
  useAzureConnect,
  useAzureVerify,
  usePowerBiConnect,
  usePowerBiVerify,
  useAzureAutoAssignUrl,
  usePbiAutoEnableUrl,
  type AzureConnectInstructions,
  type PowerBiConnectInstructions,
} from '@/api/cloudAssessment';

interface ConnectCloudWizardProps {
  orgId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  initialStep?: number;
}

const GUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

const STEPS = [
  { key: 'graph', label: 'Microsoft 365', icon: Shield },
  { key: 'azure', label: 'Azure Infrastructure', icon: Cloud },
  { key: 'powerbi', label: 'Power BI Governance', icon: BarChart3 },
] as const;

function StepIndicator({
  step,
  index,
  currentStep,
  connected,
}: {
  step: (typeof STEPS)[number];
  index: number;
  currentStep: number;
  connected: boolean;
}) {
  const Icon = step.icon;
  const isActive = index === currentStep;

  return (
    <div
      className={`flex items-center gap-2 rounded-lg px-3 py-2 text-sm transition-colors ${
        isActive
          ? 'bg-blue-50 border border-blue-200 text-blue-900'
          : 'text-muted-foreground'
      }`}
    >
      {connected ? (
        <CheckCircle2 className="h-4 w-4 text-green-600 shrink-0" />
      ) : (
        <Circle className="h-4 w-4 shrink-0" />
      )}
      <Icon className="h-4 w-4 shrink-0" />
      <span className="font-medium">{step.label}</span>
      {index !== 0 && (
        <span className="ml-auto text-xs text-muted-foreground">Optional</span>
      )}
    </div>
  );
}

function GraphStep({ orgId, connected }: { orgId: string; connected: boolean }) {
  const { data: connectUrl, isLoading } = useUnifiedCloudConnectUrl(orgId);

  if (connected) {
    return (
      <div className="space-y-3">
        <div className="flex items-center gap-2 text-green-700 bg-green-50 rounded-lg p-4">
          <CheckCircle2 className="h-5 w-5" />
          <span className="font-medium">Microsoft 365 connected</span>
        </div>
        <p className="text-sm text-muted-foreground">
          Graph API permissions are active. Identity, endpoint, data, and productivity scans are available.
        </p>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <p className="text-sm text-muted-foreground">
        Connect Microsoft 365 to enable Entra ID, Exchange, SharePoint, and Teams security scanning.
        A Global Administrator must approve read-only permissions.
      </p>
      <div className="rounded-lg border border-blue-200 bg-blue-50 p-3 text-xs text-blue-800 space-y-1">
        <p className="font-medium">What happens:</p>
        <ol className="list-decimal list-inside space-y-0.5">
          <li>Microsoft's admin consent screen opens</li>
          <li>Sign in with a Global Administrator account</li>
          <li>Approve read-only security audit permissions</li>
        </ol>
      </div>
      <Button
        onClick={() => { if (connectUrl?.url) window.location.href = connectUrl.url; }}
        disabled={isLoading || !connectUrl?.url}
        className="w-full"
      >
        {isLoading ? (
          <><Loader2 className="mr-2 h-4 w-4 animate-spin" />Loading...</>
        ) : (
          <><ExternalLink className="mr-2 h-4 w-4" />Connect Microsoft 365</>
        )}
      </Button>
    </div>
  );
}

function AzureStep({
  orgId,
  connected,
  subscriptionCount,
  onNext,
}: {
  orgId: string;
  connected: boolean;
  subscriptionCount: number;
  onNext: () => void;
}) {
  const [tenantId, setTenantId] = useState('');
  const [instructions, setInstructions] = useState<AzureConnectInstructions | null>(null);
  const [copied, setCopied] = useState(false);
  const [showManual, setShowManual] = useState(false);
  const connect = useAzureConnect();
  const verify = useAzureVerify();
  const { data: autoUrl } = useAzureAutoAssignUrl(orgId);
  const tenantIdValid = GUID_RE.test(tenantId.trim());

  if (connected) {
    return (
      <div className="space-y-3">
        <div className="flex items-center gap-2 text-green-700 bg-green-50 rounded-lg p-4">
          <CheckCircle2 className="h-5 w-5" />
          <span className="font-medium">
            Azure connected — {subscriptionCount} subscription{subscriptionCount !== 1 ? 's' : ''}
          </span>
        </div>
        <p className="text-sm text-muted-foreground">
          Azure infrastructure scanning is active.
        </p>
      </div>
    );
  }

  const handleGenerate = () => {
    if (!tenantIdValid) return;
    connect.mutate(
      { organizationId: orgId, tenantId: tenantId.trim() },
      {
        onSuccess: (data) => setInstructions(data),
        onError: (err: Error) => toast.error(`Failed: ${err.message}`),
      },
    );
  };

  const handleCopy = () => {
    if (!instructions?.azCliCommand) return;
    navigator.clipboard.writeText(instructions.azCliCommand).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    });
  };

  const handleVerify = () => {
    verify.mutate(
      { organizationId: orgId, tenantId: tenantId.trim() },
      {
        onSuccess: (data) => {
          if (data.connected && data.subscriptions && data.subscriptions.length > 0) {
            toast.success(`Found ${data.subscriptions.length} subscription(s).`);
          } else {
            toast.warning(data.message ?? 'No subscriptions found. Ensure Reader role is assigned.');
          }
        },
        onError: (err: Error) => toast.error(`Verification failed: ${err.message}`),
      },
    );
  };

  return (
    <div className="space-y-4">
      <p className="text-sm text-muted-foreground">
        Grant read-only access to Azure subscriptions for infrastructure security scanning. This step is optional.
      </p>

      <Button
        onClick={() => { if (autoUrl?.url) window.location.href = autoUrl.url; }}
        disabled={!autoUrl?.url}
        className="w-full"
      >
        <ExternalLink className="mr-2 h-4 w-4" />
        Enable automatically (recommended)
      </Button>
      <p className="text-xs text-muted-foreground text-center">
        Signs in as admin and auto-assigns Reader role on all visible subscriptions.
      </p>

      <div className="pt-2 border-t">
        <button
          className="text-xs text-muted-foreground hover:text-foreground underline"
          onClick={() => setShowManual(!showManual)}
        >
          {showManual ? 'Hide manual setup' : 'Manual setup (az CLI)'}
        </button>
      </div>

      {showManual && (
        <div className="space-y-3">
          <div className="space-y-2">
            <Label htmlFor="wizard-azure-tenant" className="text-sm font-medium">
              Entra Tenant ID
            </Label>
            <Input
              id="wizard-azure-tenant"
              placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
              value={tenantId}
              onChange={(e) => setTenantId(e.target.value)}
              className="font-mono text-sm"
            />
          </div>

          <Button
            onClick={handleGenerate}
            disabled={!tenantIdValid || connect.isPending}
            size="sm"
          >
            {connect.isPending ? (
              <><Loader2 className="mr-2 h-4 w-4 animate-spin" />Generating...</>
            ) : (
              'Generate az CLI command'
            )}
          </Button>

          {instructions && (
            <div className="space-y-3">
              <div className="relative">
                <pre className="bg-muted rounded p-3 text-xs overflow-x-auto pr-12">
                  {instructions.azCliCommand}
                </pre>
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={handleCopy}
                  className="absolute top-2 right-2 h-7 px-2"
                >
                  {copied ? <Check className="h-4 w-4 text-green-600" /> : <Copy className="h-4 w-4" />}
                </Button>
              </div>

              <Button
                onClick={handleVerify}
                disabled={verify.isPending}
                size="sm"
              >
                {verify.isPending ? (
                  <><Loader2 className="mr-2 h-4 w-4 animate-spin" />Verifying...</>
                ) : (
                  'Verify connection'
                )}
              </Button>
            </div>
          )}
        </div>
      )}

      <div className="pt-2 border-t">
        <Button variant="ghost" size="sm" onClick={onNext}>
          Skip this step
        </Button>
      </div>
    </div>
  );
}

function PowerBiStep({
  orgId,
  connected,
  onDone,
}: {
  orgId: string;
  connected: boolean;
  onDone: () => void;
}) {
  const connectPbi = usePowerBiConnect();
  const verifyPbi = usePowerBiVerify();
  const { data: autoUrl } = usePbiAutoEnableUrl(orgId);
  const [instructions, setInstructions] = useState<PowerBiConnectInstructions | null>(null);
  const [showManual, setShowManual] = useState(false);

  if (connected) {
    return (
      <div className="space-y-3">
        <div className="flex items-center gap-2 text-green-700 bg-green-50 rounded-lg p-4">
          <CheckCircle2 className="h-5 w-5" />
          <span className="font-medium">Power BI connected</span>
        </div>
        <p className="text-sm text-muted-foreground">
          Power BI governance scanning is active.
        </p>
      </div>
    );
  }

  const handleConnect = () => {
    connectPbi.mutate(
      { organizationId: orgId },
      { onSuccess: (data) => setInstructions(data) },
    );
  };

  const handleVerify = () => {
    verifyPbi.mutate(
      { organizationId: orgId },
      {
        onSuccess: (data) => {
          if (data.connected) {
            toast.success('Power BI connected successfully.');
          } else {
            toast.warning(data.error ?? 'Power BI verification failed. Ensure tenant admin settings are configured.');
          }
        },
        onError: (err: Error) => toast.error(`Verification failed: ${err.message}`),
      },
    );
  };

  return (
    <div className="space-y-4">
      <p className="text-sm text-muted-foreground">
        Enable Power BI governance scanning. A Fabric Admin must allow the service principal
        to access Power BI APIs. This step is optional.
      </p>

      <Button
        onClick={() => { if (autoUrl?.url) window.location.href = autoUrl.url; }}
        disabled={!autoUrl?.url}
        className="w-full"
      >
        <ExternalLink className="mr-2 h-4 w-4" />
        Enable automatically (recommended)
      </Button>
      <p className="text-xs text-muted-foreground text-center">
        Signs in as Fabric Admin and enables ServicePrincipal access via Fabric API.
      </p>

      <div className="pt-2 border-t">
        <button
          className="text-xs text-muted-foreground hover:text-foreground underline"
          onClick={() => setShowManual(!showManual)}
        >
          {showManual ? 'Hide manual setup' : 'Manual setup (Power BI Admin Portal)'}
        </button>
      </div>

      {showManual && (
        <>
          {!instructions ? (
            <Button onClick={handleConnect} disabled={connectPbi.isPending} size="sm">
              {connectPbi.isPending ? (
                <><Loader2 className="mr-2 h-4 w-4 animate-spin" />Loading...</>
              ) : (
                'Show setup instructions'
              )}
            </Button>
          ) : (
            <div className="space-y-3">
              <div className="rounded-lg border bg-muted/50 p-3 text-xs space-y-2">
                <p className="font-medium">Required tenant admin settings:</p>
                <ol className="list-decimal list-inside space-y-0.5">
                  {instructions.instructions.map((inst, i) => (
                    <li key={i}>{inst}</li>
                  ))}
                </ol>
                <a
                  href={instructions.tenantSettingsUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="inline-flex items-center gap-1 text-blue-600 hover:underline text-xs mt-1"
                >
                  <ExternalLink className="h-3 w-3" />
                  Open Power BI Admin Portal
                </a>
              </div>

              <Button onClick={handleVerify} disabled={verifyPbi.isPending} size="sm">
                {verifyPbi.isPending ? (
                  <><Loader2 className="mr-2 h-4 w-4 animate-spin" />Verifying...</>
                ) : (
                  'Verify Power BI connection'
                )}
              </Button>
            </div>
          )}
        </>
      )}

      <div className="pt-2 border-t">
        <Button variant="ghost" size="sm" onClick={onDone}>
          Skip this step
        </Button>
      </div>
    </div>
  );
}

export function ConnectCloudWizard({
  orgId,
  open,
  onOpenChange,
  initialStep = 0,
}: ConnectCloudWizardProps) {
  const [currentStep, setCurrentStep] = useState(initialStep);
  const { data: status } = useConnectionStatus(orgId);

  const graphConnected = status?.graph === 'connected';
  const azureConnected = status?.azure === 'connected' || status?.azure === 'partial';
  const pbiConnected = status?.powerBi === 'connected';

  const handleOpenChange = (nextOpen: boolean) => {
    if (!nextOpen) setCurrentStep(initialStep);
    onOpenChange(nextOpen);
  };

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent className="max-w-2xl max-h-[85vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Connect Cloud Services</DialogTitle>
        </DialogHeader>

        <div className="grid grid-cols-[200px_1fr] gap-6 mt-2">
          {/* Stepper sidebar */}
          <div className="space-y-2">
            {STEPS.map((step, i) => (
              <button
                key={step.key}
                onClick={() => setCurrentStep(i)}
                className="w-full text-left"
              >
                <StepIndicator
                  step={step}
                  index={i}
                  currentStep={currentStep}
                  connected={
                    i === 0 ? graphConnected :
                    i === 1 ? azureConnected :
                    pbiConnected
                  }
                />
              </button>
            ))}
          </div>

          {/* Step content */}
          <div className="min-h-[300px]">
            {currentStep === 0 && (
              <GraphStep orgId={orgId} connected={graphConnected} />
            )}
            {currentStep === 1 && (
              <AzureStep
                orgId={orgId}
                connected={azureConnected}
                subscriptionCount={status?.azureSubscriptionCount ?? 0}
                onNext={() => setCurrentStep(2)}
              />
            )}
            {currentStep === 2 && (
              <PowerBiStep
                orgId={orgId}
                connected={pbiConnected}
                onDone={() => handleOpenChange(false)}
              />
            )}
          </div>
        </div>

        {/* Footer navigation */}
        <div className="flex justify-between pt-4 border-t">
          <Button
            variant="outline"
            size="sm"
            onClick={() => setCurrentStep(Math.max(0, currentStep - 1))}
            disabled={currentStep === 0}
          >
            Previous
          </Button>
          <div className="flex gap-2">
            <Button variant="ghost" size="sm" onClick={() => handleOpenChange(false)}>
              Close
            </Button>
            {currentStep < 2 && (
              <Button size="sm" onClick={() => setCurrentStep(currentStep + 1)}>
                Next
              </Button>
            )}
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}

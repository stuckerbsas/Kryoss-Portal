import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { CheckCircle2, AlertTriangle, Loader2, Copy, Check, ExternalLink } from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { toast } from 'sonner';
import { useCloudAssessmentDetail } from '@/api/cloudAssessment';

interface ConnectResult {
  m365: boolean;
  azureSubs: number;
  azureRbacFailed: boolean;
  pbi: boolean;
  scanId: string | null;
  pbiNote: string | null;
  azureNote: string | null;
}

function parseConnectParams(params: URLSearchParams): ConnectResult | null {
  if (!params.has('cloud_connected')) return null;
  return {
    m365: params.get('m365') === 'true',
    azureSubs: parseInt(params.get('azure_subs') ?? '0'),
    azureRbacFailed: params.get('azure_rbac_failed') === 'true',
    pbi: params.get('pbi') === 'true',
    scanId: params.get('scan_id'),
    pbiNote: params.get('pbi_note') ? decodeURIComponent(params.get('pbi_note')!) : null,
    azureNote: params.get('azure_note') ? decodeURIComponent(params.get('azure_note')!) : null,
  };
}

function StatusLine({ ok, label, note }: { ok: boolean | null; label: string; note?: string | null }) {
  return (
    <div className="flex items-start gap-2 text-sm">
      {ok === true && <CheckCircle2 className="h-4 w-4 text-green-600 mt-0.5 shrink-0" />}
      {ok === false && <AlertTriangle className="h-4 w-4 text-amber-500 mt-0.5 shrink-0" />}
      {ok === null && <Loader2 className="h-4 w-4 animate-spin mt-0.5 shrink-0" />}
      <div>
        <span className={ok === false ? 'text-amber-700' : ''}>{label}</span>
        {note && <p className="text-xs text-muted-foreground mt-0.5">{note}</p>}
      </div>
    </div>
  );
}

export function ConnectProgressModal(_props: { orgId: string }) {
  const [searchParams, setSearchParams] = useSearchParams();
  const [result, setResult] = useState<ConnectResult | null>(null);
  const [dismissed, setDismissed] = useState(false);
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    const error = searchParams.get('error');
    if (error) {
      toast.error(`Cloud connect failed: ${decodeURIComponent(error)}`);
      const newParams = new URLSearchParams();
      setSearchParams(newParams, { replace: true });
      return;
    }

    // CA-14: auto-consent callbacks
    const pbiAutoEnabled = searchParams.get('pbi_autoenabled');
    const pbiError = searchParams.get('pbi_error');
    const azureAutoAssigned = searchParams.get('azure_autoassigned');
    const azureError = searchParams.get('azure_error');

    if (pbiAutoEnabled === 'true') {
      toast.success('Power BI auto-enabled successfully');
      const newParams = new URLSearchParams();
      setSearchParams(newParams, { replace: true });
      return;
    }
    if (pbiError) {
      toast.error(`Power BI auto-enable failed: ${decodeURIComponent(pbiError)}`);
      const newParams = new URLSearchParams();
      setSearchParams(newParams, { replace: true });
      return;
    }
    if (azureAutoAssigned === 'true') {
      const subs = searchParams.get('azure_subs') ?? '0';
      const failed = searchParams.get('azure_failed') === 'True';
      const note = searchParams.get('azure_note');
      const msg = failed
        ? `Azure: ${subs} subs found, some role assignments failed${note ? ` — ${decodeURIComponent(note)}` : ''}`
        : `Azure auto-assigned Reader on ${subs} subscription(s)`;
      failed ? toast.warning(msg) : toast.success(msg);
      const newParams = new URLSearchParams();
      setSearchParams(newParams, { replace: true });
      return;
    }
    if (azureError) {
      toast.error(`Azure auto-assign failed: ${decodeURIComponent(azureError)}`);
      const newParams = new URLSearchParams();
      setSearchParams(newParams, { replace: true });
      return;
    }

    const parsed = parseConnectParams(searchParams);
    if (parsed) {
      setResult(parsed);
      const newParams = new URLSearchParams();
      setSearchParams(newParams, { replace: true });
    }
  }, []);

  const { data: scanDetail } = useCloudAssessmentDetail(result?.scanId ?? undefined);
  const scanDone = scanDetail && scanDetail.status !== 'running';

  if (!result || dismissed) return null;

  const azCliCmd = `az role assignment create \\\n  --assignee-object-id $(az ad sp show --id <APP_ID> --query id -o tsv) \\\n  --assignee-principal-type ServicePrincipal \\\n  --role "Reader" \\\n  --scope "/subscriptions/<SUBSCRIPTION_ID>"`;

  const handleCopy = () => {
    navigator.clipboard.writeText(azCliCmd).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    });
  };

  return (
    <Card className="border-green-200 bg-green-50/30 mb-4">
      <CardHeader className="pb-2">
        <CardTitle className="text-base flex items-center gap-2">
          <CheckCircle2 className="h-5 w-5 text-green-600" />
          Cloud Services Connected
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        <StatusLine ok={result.m365} label="Microsoft 365 / Entra ID — Connected" />
        <StatusLine
          ok={result.azureSubs > 0}
          label={result.azureSubs > 0
            ? `Azure — ${result.azureSubs} subscription(s) connected`
            : 'Azure — Not Available'}
          note={result.azureRbacFailed && result.azureSubs === 0
            ? result.azureNote ?? 'Admin needs Owner role to auto-assign. Assign Reader manually.'
            : result.azureNote}
        />
        <StatusLine
          ok={result.pbi}
          label={result.pbi ? 'Power BI Governance — Connected' : 'Power BI — Not Available'}
          note={!result.pbi ? result.pbiNote : null}
        />

        {result.azureRbacFailed && result.azureSubs === 0 && (
          <div className="mt-2 p-3 rounded border bg-white text-xs space-y-2">
            <p className="font-medium">To enable Azure scanning, run this in Azure CLI:</p>
            <div className="relative">
              <pre className="bg-muted rounded p-2 overflow-x-auto pr-10 text-xs">{azCliCmd}</pre>
              <Button variant="ghost" size="sm" onClick={handleCopy} className="absolute top-1 right-1 h-6 px-1.5">
                {copied ? <Check className="h-3 w-3 text-green-600" /> : <Copy className="h-3 w-3" />}
              </Button>
            </div>
          </div>
        )}

        {result.pbiNote && !result.pbi && (
          <div className="mt-2 p-3 rounded border bg-white text-xs space-y-1">
            <p>{result.pbiNote}</p>
            <a
              href="https://app.powerbi.com/admin-portal/tenantSettings"
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex items-center gap-1 text-blue-600 hover:underline"
            >
              Open Power BI Admin Portal <ExternalLink className="h-3 w-3" />
            </a>
          </div>
        )}

        {result.scanId && (
          <StatusLine
            ok={scanDone ? true : null}
            label={scanDone ? 'Cloud assessment scan complete' : 'Running cloud assessment scan...'}
          />
        )}

        <div className="pt-2 text-right">
          <Button variant="ghost" size="sm" onClick={() => setDismissed(true)}>
            Dismiss
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}

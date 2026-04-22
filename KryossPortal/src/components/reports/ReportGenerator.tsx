import { useState } from 'react';
import { ExternalLink, Download, Loader2 } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Can } from '@/components/auth/Can';
import { API_BASE } from '@/auth/msalConfig';
import { msalInstance } from '@/auth/msalInstance';
import { loginRequest } from '@/auth/msalConfig';

interface ReportGeneratorProps {
  targetId: string; // organization ID (per-run reports deprecated 2026-04-15)
}

const FRAMEWORKS = [
  { value: 'all', label: 'All Frameworks' },
  { value: 'NIST', label: 'NIST' },
  { value: 'CIS', label: 'CIS' },
  { value: 'HIPAA', label: 'HIPAA' },
  { value: 'ISO27001', label: 'ISO 27001' },
  { value: 'PCI-DSS', label: 'PCI-DSS' },
] as const;

const REPORT_TYPES = [
  { value: 'c-level',           label: 'C-Level'                          },
  { value: 'technical',         label: 'Technical'                         },
  { value: 'executive',         label: 'Executive'                         },
  { value: 'preventas',         label: 'Preventas'                         },
  { value: 'exec-onepager',     label: 'Executive One-Pager'               },
  { value: 'monthly-briefing',  label: 'Monthly Briefing (MRR)'            },
  { value: 'network',           label: 'Network Assessment'                },
  { value: 'framework',         label: 'Framework Compliance'              },
  { value: 'proposal',          label: 'Business Proposal'                 },
  { value: 'cloud-executive',   label: 'Cloud Executive (Findings + Hours)'},
  { value: 'm365',              label: 'M365 Security & Copilot Readiness' },
] as const;

const TONES = [
  { value: 'opener',   label: 'Opener (2 pages)' },
  { value: 'detailed', label: 'Detailed (6 pages)' },
] as const;

const LANGUAGES = [
  { value: 'en', label: 'English' },
  { value: 'es', label: 'Español' },
] as const;

function buildApiPath(
  orgId: string,
  reportType: string,
  framework: string,
  lang: string,
  tone: string,
): string {
  const params = new URLSearchParams();
  params.set('type', reportType);
  if (framework !== 'all') params.set('framework', framework);
  if (lang !== 'en') params.set('lang', lang);
  if (reportType === 'preventas') params.set('tone', tone);
  return `/v2/reports/org/${orgId}?${params}`;
}

async function fetchReport(apiPath: string): Promise<string> {
  const accounts = msalInstance.getAllAccounts();
  if (accounts.length === 0) throw new Error('Not authenticated');

  let token: string;
  try {
    const res = await msalInstance.acquireTokenSilent({
      ...loginRequest,
      account: accounts[0],
    });
    token = res.accessToken;
  } catch {
    const res = await msalInstance.acquireTokenPopup(loginRequest);
    token = res.accessToken;
  }

  const res = await fetch(`${API_BASE}${apiPath}`, {
    headers: { Authorization: `Bearer ${token}` },
  });

  if (!res.ok) {
    const body = await res.text();
    throw new Error(`HTTP ${res.status}: ${body}`);
  }

  return res.text();
}

export function ReportGenerator({ targetId }: ReportGeneratorProps) {
  const [framework, setFramework] = useState('all');
  const [reportType, setReportType] = useState('technical');
  const [lang, setLang] = useState<'en' | 'es'>('en');
  const [tone, setTone] = useState<'opener' | 'detailed'>('opener');
  const [loading, setLoading] = useState(false);

  const apiPath = buildApiPath(targetId, reportType, framework, lang, tone);

  const handleOpenTab = async () => {
    // Open window IMMEDIATELY on user click (before async fetch)
    // Browsers block window.open after async delays
    const newWindow = window.open('about:blank', '_blank');
    if (newWindow) {
      newWindow.document.write('<html><head><title>Generating report...</title></head><body style="font-family:sans-serif;display:flex;align-items:center;justify-content:center;height:100vh;margin:0;color:#666"><p>Generating report, please wait...</p></body></html>');
    }

    setLoading(true);
    try {
      const html = await fetchReport(apiPath);
      if (newWindow) {
        newWindow.document.open();
        newWindow.document.write(html);
        newWindow.document.close();
      }
      toast.success('Report opened');
    } catch (err: any) {
      if (newWindow) {
        newWindow.document.open();
        newWindow.document.write(`<html><body style="font-family:sans-serif;padding:40px;color:#C0392B"><h2>Report generation failed</h2><p>${err.message}</p></body></html>`);
        newWindow.document.close();
      }
      toast.error(`Failed to generate report: ${err.message}`);
    } finally {
      setLoading(false);
    }
  };

  const handleDownload = async () => {
    setLoading(true);
    try {
      const html = await fetchReport(apiPath);
      const blob = new Blob([html], { type: 'text/html' });
      const a = document.createElement('a');
      a.href = URL.createObjectURL(blob);
      a.download = `Report-${framework !== 'all' ? framework + '-' : ''}${reportType}-${lang}-${new Date().toISOString().slice(0, 10)}.html`;
      a.click();
      URL.revokeObjectURL(a.href);
      toast.success('Report downloaded');
    } catch (err: any) {
      toast.error(`Failed to download report: ${err.message}`);
    } finally {
      setLoading(false);
    }
  };

  return (
    <Card className="p-4">
      <div className="flex flex-wrap items-center gap-3">
        <Select value={framework} onValueChange={setFramework}>
          <SelectTrigger className="w-[160px]">
            <SelectValue placeholder="Framework" />
          </SelectTrigger>
          <SelectContent>
            {FRAMEWORKS.map((fw) => (
              <SelectItem key={fw.value} value={fw.value}>
                {fw.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>

        <Select value={reportType} onValueChange={setReportType}>
          <SelectTrigger className="w-[200px]">
            <SelectValue placeholder="Report Type" />
          </SelectTrigger>
          <SelectContent>
            {REPORT_TYPES.map((rt) => (
              <SelectItem key={rt.value} value={rt.value}>
                {rt.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>

        {reportType === 'preventas' && (
          <Select value={tone} onValueChange={(v) => setTone(v as 'opener' | 'detailed')}>
            <SelectTrigger className="w-[180px]">
              <SelectValue placeholder="Tone" />
            </SelectTrigger>
            <SelectContent>
              {TONES.map((t) => (
                <SelectItem key={t.value} value={t.value}>{t.label}</SelectItem>
              ))}
            </SelectContent>
          </Select>
        )}

        <Select value={lang} onValueChange={(v) => setLang(v as 'en' | 'es')}>
          <SelectTrigger className="w-[120px]">
            <SelectValue placeholder="Language" />
          </SelectTrigger>
          <SelectContent>
            {LANGUAGES.map((l) => (
              <SelectItem key={l.value} value={l.value}>
                {l.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>

        <Can permission="reports:export">
          <div className="flex items-center gap-2 ml-auto">
            <Button
              variant="outline"
              size="sm"
              disabled={loading}
              onClick={handleOpenTab}
            >
              {loading ? (
                <Loader2 className="size-4 mr-1 animate-spin" />
              ) : (
                <ExternalLink className="size-4 mr-1" />
              )}
              Open in new tab
            </Button>
            <Button
              variant="outline"
              size="sm"
              disabled={loading}
              onClick={handleDownload}
            >
              {loading ? (
                <Loader2 className="size-4 mr-1 animate-spin" />
              ) : (
                <Download className="size-4 mr-1" />
              )}
              Download HTML
            </Button>
          </div>
        </Can>
      </div>
    </Card>
  );
}

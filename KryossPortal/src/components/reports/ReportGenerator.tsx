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
  targetType: 'run' | 'org';
  targetId: string;
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
  { value: 'technical', label: 'Technical' },
  { value: 'executive', label: 'Executive' },
  { value: 'presales', label: 'Presales' },
] as const;

function buildApiPath(
  targetType: 'run' | 'org',
  targetId: string,
  reportType: string,
  framework: string,
): string {
  const base =
    targetType === 'run'
      ? `/v2/reports/${targetId}`
      : `/v2/reports/org/${targetId}`;
  const params = new URLSearchParams();
  params.set('type', reportType);
  if (framework !== 'all') params.set('framework', framework);
  return `${base}?${params}`;
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

export function ReportGenerator({ targetType, targetId }: ReportGeneratorProps) {
  const [framework, setFramework] = useState('all');
  const [reportType, setReportType] = useState('technical');
  const [loading, setLoading] = useState(false);

  const apiPath = buildApiPath(targetType, targetId, reportType, framework);

  const handleOpenTab = async () => {
    setLoading(true);
    try {
      const html = await fetchReport(apiPath);
      const blob = new Blob([html], { type: 'text/html' });
      const url = URL.createObjectURL(blob);
      window.open(url, '_blank');
      // Clean up after a delay (browser needs time to load)
      setTimeout(() => URL.revokeObjectURL(url), 5000);
      toast.success('Report opened');
    } catch (err: any) {
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
      a.download = `Report-${framework !== 'all' ? framework + '-' : ''}${reportType}-${new Date().toISOString().slice(0, 10)}.html`;
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
          <SelectTrigger className="w-[140px]">
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

import { useRef, useState } from 'react';
import {
  BarChart3, Shield, AlertTriangle, FileText,
  Presentation, BookOpen, Briefcase, Scale,
  KeyRound, Cloud, CalendarDays, Network, Package,
  Eye, Download, Printer, Loader2, FlaskConical, Stethoscope,
  Globe, ExternalLink, ZoomIn, ZoomOut, RotateCcw,
  type LucideIcon,
} from 'lucide-react';
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
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Can } from '@/components/auth/Can';
import { usePermissions } from '@/hooks/usePermissions';
import { API_BASE } from '@/auth/msalConfig';
import { msalInstance } from '@/auth/msalInstance';
import { loginRequest } from '@/auth/msalConfig';

interface ReportGeneratorProps {
  targetId: string;
}

interface ReportTypeDef {
  value: string;
  label: string;
  icon: LucideIcon;
}

const REPORT_TYPES: ReportTypeDef[] = [
  { value: 'c-level',           label: 'C-Level',            icon: BarChart3     },
  { value: 'technical',         label: 'Technical',          icon: Shield        },
  { value: 'risk-assessment',   label: 'Risk Assessment',    icon: AlertTriangle },
  { value: 'exec-onepager',     label: 'One-Pager',          icon: FileText      },
  { value: 'preventa-opener',   label: 'Preventa Opener',    icon: Presentation  },
  { value: 'preventa-detailed', label: 'Preventa Detailed',  icon: BookOpen      },
  { value: 'proposal',          label: 'Proposal',           icon: Briefcase     },
  { value: 'framework',         label: 'Framework',          icon: Scale         },
  { value: 'hygiene',           label: 'AD Hygiene',         icon: KeyRound      },
  { value: 'cloud-executive',   label: 'Cloud Executive',    icon: Cloud         },
  { value: 'monthly-briefing',  label: 'Monthly',            icon: CalendarDays  },
  { value: 'network',           label: 'Network',            icon: Network       },
  { value: 'inventory',         label: 'Inventory',          icon: Package       },
];

const FRAMEWORKS = [
  { value: 'all',       label: 'All' },
  { value: 'NIST',      label: 'NIST' },
  { value: 'CIS',       label: 'CIS' },
  { value: 'HIPAA',     label: 'HIPAA' },
  { value: 'ISO27001',  label: 'ISO 27001' },
  { value: 'PCI-DSS',   label: 'PCI-DSS' },
] as const;

const LANGUAGES: { value: string; label: string }[] = [
  { value: 'en', label: 'English' },
  { value: 'es', label: 'Español' },
];

function buildApiPath(orgId: string, reportType: string, framework: string, lang: string): string {
  const params = new URLSearchParams();
  params.set('type', reportType);
  if (framework !== 'all') params.set('framework', framework);
  if (lang !== 'en') params.set('lang', lang);
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
  const [lang, setLang] = useState('en');
  const [loading, setLoading] = useState(false);
  const [previewOpen, setPreviewOpen] = useState(false);
  const [previewHtml, setPreviewHtml] = useState('');
  const [zoom, setZoom] = useState(0.8);
  const iframeRef = useRef<HTMLIFrameElement>(null);
  const { isSuperAdmin } = usePermissions();

  const apiPath = buildApiPath(targetId, reportType, framework, lang);
  const selectedType = REPORT_TYPES.find(r => r.value === reportType);
  const reportLabel = selectedType?.label ?? reportType;

  const handlePreview = async () => {
    setLoading(true);
    try {
      const html = await fetchReport(apiPath);
      setPreviewHtml(html);
      setPreviewOpen(true);
    } catch (err: any) {
      toast.error(`Failed: ${err.message}`);
    } finally {
      setLoading(false);
    }
  };

  const handleOpenTab = async () => {
    const newWindow = window.open('about:blank', '_blank');
    if (newWindow) {
      newWindow.document.write('<html><head><title>Generating report...</title></head><body style="font-family:sans-serif;display:flex;align-items:center;justify-content:center;height:100vh;margin:0;color:#666"><p>Generating report...</p></body></html>');
    }
    setLoading(true);
    try {
      const html = await fetchReport(apiPath);
      if (newWindow) {
        newWindow.document.open();
        newWindow.document.write(html);
        newWindow.document.close();
      }
    } catch (err: any) {
      const fullUrl = `${API_BASE}${apiPath}`;
      if (newWindow) {
        newWindow.document.open();
        newWindow.document.write(`<html><body style="font-family:sans-serif;padding:40px"><h2 style="color:#C0392B">Report generation failed</h2><p>${err.message}</p><p><code>${fullUrl}</code></p><p style="color:#64748b;font-size:13px">Try <code>diag=1</code> for diagnostics.</p></body></html>`);
        newWindow.document.close();
      }
      toast.error(`Failed: ${err.message}`);
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
      toast.error(`Failed: ${err.message}`);
    } finally {
      setLoading(false);
    }
  };

  const handlePrintPdf = () => {
    iframeRef.current?.contentWindow?.print();
  };

  const handleDownloadFromPreview = () => {
    if (!previewHtml) return;
    const blob = new Blob([previewHtml], { type: 'text/html' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = `Report-${framework !== 'all' ? framework + '-' : ''}${reportType}-${lang}-${new Date().toISOString().slice(0, 10)}.html`;
    a.click();
    URL.revokeObjectURL(a.href);
  };

  return (
    <>
      <Card className="p-5 space-y-5">
        {/* Header + language */}
        <div className="flex items-center justify-between">
          <h3 className="text-sm font-semibold text-muted-foreground uppercase tracking-wider">
            Report Generator
          </h3>
          <Select value={lang} onValueChange={setLang}>
            <SelectTrigger className="w-[140px]">
              <Globe className="size-3.5 mr-1.5 text-muted-foreground" />
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {LANGUAGES.map((l) => (
                <SelectItem key={l.value} value={l.value}>
                  {l.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        {/* Report type icon buttons */}
        <div>
          <div className="text-xs font-medium text-muted-foreground mb-2 uppercase tracking-wider">
            Report Type
          </div>
          <div className="flex flex-wrap gap-2">
            {REPORT_TYPES.map((rt) => {
              const Icon = rt.icon;
              const active = reportType === rt.value;
              return (
                <button
                  key={rt.value}
                  onClick={() => setReportType(rt.value)}
                  className={`
                    flex items-center gap-1.5 px-3 py-2 rounded-lg text-xs font-medium
                    border transition-all cursor-pointer
                    ${active
                      ? 'bg-primary text-primary-foreground border-primary shadow-sm'
                      : 'bg-background text-foreground border-border hover:bg-accent hover:border-accent-foreground/20'
                    }
                  `}
                >
                  <Icon className="size-3.5" />
                  {rt.label}
                </button>
              );
            })}
            {isSuperAdmin && (
              <button
                onClick={() => setReportType('test-fixture')}
                className={`
                  flex items-center gap-1.5 px-3 py-2 rounded-lg text-xs font-medium
                  border transition-all cursor-pointer
                  ${reportType === 'test-fixture'
                    ? 'bg-amber-500 text-white border-amber-500 shadow-sm'
                    : 'bg-background text-amber-600 border-amber-300 hover:bg-amber-50'
                  }
                `}
              >
                <FlaskConical className="size-3.5" />
                Test Fixture
              </button>
            )}
          </div>
        </div>

        {/* Framework pill buttons */}
        <div>
          <div className="text-xs font-medium text-muted-foreground mb-2 uppercase tracking-wider">
            Framework
          </div>
          <div className="flex flex-wrap gap-1.5">
            {FRAMEWORKS.map((fw) => {
              const active = framework === fw.value;
              return (
                <button
                  key={fw.value}
                  onClick={() => setFramework(fw.value)}
                  className={`
                    px-3 py-1 rounded-full text-xs font-medium border transition-all cursor-pointer
                    ${active
                      ? 'bg-primary text-primary-foreground border-primary'
                      : 'bg-muted/50 text-muted-foreground border-border hover:bg-accent'
                    }
                  `}
                >
                  {fw.label}
                </button>
              );
            })}
          </div>
        </div>

        {/* Action buttons */}
        <Can permission="reports:export">
          <div className="flex flex-wrap items-center gap-2 pt-3 border-t">
            <Button size="sm" disabled={loading} onClick={handlePreview}>
              {loading ? <Loader2 className="size-4 mr-1.5 animate-spin" /> : <Eye className="size-4 mr-1.5" />}
              Preview
            </Button>
            <Button variant="outline" size="sm" disabled={loading} onClick={handleOpenTab}>
              <ExternalLink className="size-4 mr-1.5" />
              New Tab
            </Button>
            <Button variant="outline" size="sm" disabled={loading} onClick={handleDownload}>
              <Download className="size-4 mr-1.5" />
              HTML
            </Button>
            {isSuperAdmin && (
              <Button
                variant="outline"
                size="sm"
                disabled={loading}
                onClick={async () => {
                  const w = window.open('about:blank', '_blank');
                  if (w) w.document.write('<html><body style="font-family:sans-serif;padding:20px"><p>Running diagnostics...</p></body></html>');
                  setLoading(true);
                  try {
                    const diagApi = apiPath + '&diag=1';
                    const json = await fetchReport(diagApi);
                    if (w) { w.document.open(); w.document.write(`<html><body style="font-family:monospace;padding:20px;white-space:pre-wrap;font-size:12px">${json}</body></html>`); w.document.close(); }
                  } catch (err: any) {
                    if (w) { w.document.open(); w.document.write(`<html><body style="font-family:sans-serif;padding:40px;color:#C0392B"><h2>Failed</h2><p>${err.message}</p></body></html>`); w.document.close(); }
                    toast.error(err.message);
                  } finally { setLoading(false); }
                }}
                className="ml-auto border-blue-300 text-blue-700 hover:bg-blue-50"
              >
                <Stethoscope className="size-4 mr-1.5" />
                Diagnose
              </Button>
            )}
          </div>
        </Can>
      </Card>

      {/* Preview dialog */}
      <Dialog open={previewOpen} onOpenChange={setPreviewOpen}>
        <DialogContent
          className="!max-w-none w-screen h-screen p-0 flex flex-col gap-0 rounded-none border-0"
          showCloseButton={false}
        >
          <DialogHeader className="px-5 pt-4 pb-3 flex-none border-b flex-row items-center justify-between">
            <DialogTitle className="text-base">
              {reportLabel}
              {framework !== 'all' && <span className="ml-2 text-xs font-normal text-muted-foreground">({framework})</span>}
            </DialogTitle>
            <div className="flex items-center gap-3">
              <div className="flex items-center gap-1 border rounded-md px-1">
                <Button variant="ghost" size="icon" className="size-7" onClick={() => setZoom(z => Math.max(0.3, z - 0.1))}>
                  <ZoomOut className="size-3.5" />
                </Button>
                <button
                  onClick={() => setZoom(0.8)}
                  className="text-xs font-mono w-10 text-center text-muted-foreground hover:text-foreground cursor-pointer"
                >
                  {Math.round(zoom * 100)}%
                </button>
                <Button variant="ghost" size="icon" className="size-7" onClick={() => setZoom(z => Math.min(2, z + 0.1))}>
                  <ZoomIn className="size-3.5" />
                </Button>
              </div>
              <Button variant="outline" size="sm" onClick={handlePrintPdf}>
                <Printer className="size-3.5 mr-1.5" />
                PDF
              </Button>
              <Button variant="outline" size="sm" onClick={handleDownloadFromPreview}>
                <Download className="size-3.5 mr-1.5" />
                HTML
              </Button>
              <DialogClose asChild>
                <Button variant="ghost" size="sm">Close</Button>
              </DialogClose>
            </div>
          </DialogHeader>
          <div className="flex-1 min-h-0">
            <iframe
              ref={iframeRef}
              srcDoc={`<style>html{zoom:${zoom}}</style>${previewHtml}`}
              className="w-full h-full border-0"
              title="Report Preview"
            />
          </div>
        </DialogContent>
      </Dialog>
    </>
  );
}

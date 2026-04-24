import { useEffect, useRef, useState } from 'react';
import {
  BarChart3, Shield, AlertTriangle, FileText,
  Presentation, BookOpen, Briefcase, Scale,
  KeyRound, Cloud, CalendarDays, Network, Package,
  Download, Printer, Loader2, FlaskConical, Stethoscope,
  Globe, ZoomIn, ZoomOut,
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

interface ReportDef {
  id: string;
  label: string;
  icon: LucideIcon;
  type: string;
  framework?: string;
  color?: string;
}

const REPORTS: ReportDef[] = [
  { id: 'c-level',           label: 'C-Level',            icon: BarChart3,      type: 'c-level' },
  { id: 'technical',         label: 'Technical',          icon: Shield,         type: 'technical' },
  { id: 'risk-assessment',   label: 'Risk Assessment',    icon: AlertTriangle,  type: 'risk-assessment' },
  { id: 'exec-onepager',     label: 'One-Pager',          icon: FileText,       type: 'exec-onepager' },
  { id: 'preventa-opener',   label: 'Pre-Sale Opener',    icon: Presentation,   type: 'preventa-opener' },
  { id: 'preventa-detailed', label: 'Pre-Sale Detailed',  icon: BookOpen,       type: 'preventa-detailed' },
  { id: 'proposal',          label: 'Proposal',           icon: Briefcase,      type: 'proposal' },
  { id: 'hygiene',           label: 'AD Hygiene',         icon: KeyRound,       type: 'hygiene' },
  { id: 'cloud-executive',   label: 'Cloud Executive',    icon: Cloud,          type: 'cloud-executive' },
  { id: 'monthly-briefing',  label: 'Monthly',            icon: CalendarDays,   type: 'monthly-briefing' },
  { id: 'network',           label: 'Network',            icon: Network,        type: 'network' },
  { id: 'inventory',         label: 'Inventory',          icon: Package,        type: 'inventory' },
  // Framework-specific reports
  { id: 'fw-nist',           label: 'NIST',               icon: Scale,          type: 'framework', framework: 'NIST',     color: 'text-blue-700 border-blue-300 hover:bg-blue-50' },
  { id: 'fw-cis',            label: 'CIS',                icon: Scale,          type: 'framework', framework: 'CIS',      color: 'text-emerald-700 border-emerald-300 hover:bg-emerald-50' },
  { id: 'fw-hipaa',          label: 'HIPAA',              icon: Scale,          type: 'framework', framework: 'HIPAA',    color: 'text-purple-700 border-purple-300 hover:bg-purple-50' },
  { id: 'fw-iso27001',       label: 'ISO 27001',          icon: Scale,          type: 'framework', framework: 'ISO27001', color: 'text-amber-700 border-amber-300 hover:bg-amber-50' },
  { id: 'fw-pci',            label: 'PCI-DSS',            icon: Scale,          type: 'framework', framework: 'PCI-DSS',  color: 'text-red-700 border-red-300 hover:bg-red-50' },
];

const LANGUAGES: { value: string; label: string }[] = [
  { value: 'en', label: 'English' },
  { value: 'es', label: 'Español' },
];

function buildApiPath(orgId: string, type: string, lang: string, framework?: string): string {
  const params = new URLSearchParams();
  params.set('type', type);
  if (framework) params.set('framework', framework);
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
  const [lang, setLang] = useState('en');
  const [loading, setLoading] = useState(false);
  const [loadingLabel, setLoadingLabel] = useState('');
  const [previewOpen, setPreviewOpen] = useState(false);
  const [previewHtml, setPreviewHtml] = useState('');
  const [previewTitle, setPreviewTitle] = useState('');
  const [zoom, setZoom] = useState(1.5);
  const iframeRef = useRef<HTMLIFrameElement>(null);
  const { isSuperAdmin } = usePermissions();

  const escHandler = useRef<((e: KeyboardEvent) => void) | null>(null);
  escHandler.current = (e: KeyboardEvent) => {
    if (e.key === 'Escape') setPreviewOpen(false);
  };

  const onIframeLoad = () => {
    try {
      const cw = iframeRef.current?.contentWindow;
      if (cw) {
        cw.addEventListener('keydown', (e) => escHandler.current?.(e));
      }
    } catch {}
  };

  const handleGenerate = async (report: ReportDef) => {
    setLoadingLabel(report.label);
    setLoading(true);
    try {
      const apiPath = buildApiPath(targetId, report.type, lang, report.framework);
      const html = await fetchReport(apiPath);
      setPreviewHtml(html);
      setPreviewTitle(report.label + (report.framework ? ` (${report.framework})` : ''));
      setPreviewOpen(true);
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
    a.download = `Report-${previewTitle.replace(/\s+/g, '-')}-${lang}-${new Date().toISOString().slice(0, 10)}.html`;
    a.click();
    URL.revokeObjectURL(a.href);
  };

  const handleDiagnose = async () => {
    const w = window.open('about:blank', '_blank');
    if (w) w.document.write('<html><body style="font-family:sans-serif;padding:20px"><p>Running diagnostics...</p></body></html>');
    setLoadingLabel('Diagnostics');
    setLoading(true);
    try {
      const apiPath = buildApiPath(targetId, 'technical', lang) + '&diag=1';
      const json = await fetchReport(apiPath);
      if (w) { w.document.open(); w.document.write(`<html><body style="font-family:monospace;padding:20px;white-space:pre-wrap;font-size:12px">${json}</body></html>`); w.document.close(); }
    } catch (err: any) {
      if (w) { w.document.open(); w.document.write(`<html><body style="font-family:sans-serif;padding:40px;color:#C0392B"><h2>Failed</h2><p>${err.message}</p></body></html>`); w.document.close(); }
      toast.error(err.message);
    } finally { setLoading(false); }
  };

  const generalReports = REPORTS.filter(r => !r.framework);
  const frameworkReports = REPORTS.filter(r => r.framework);

  return (
    <>
      <Card className="p-5 space-y-5">
        {/* Header + language */}
        <div className="flex items-center justify-between">
          <h3 className="text-sm font-semibold text-muted-foreground uppercase tracking-wider">
            Report Generator
          </h3>
          <div className="flex items-center gap-2">
            {isSuperAdmin && (
              <Button
                variant="outline"
                size="sm"
                onClick={handleDiagnose}
                disabled={loading}
                className="border-blue-300 text-blue-700 hover:bg-blue-50"
              >
                <Stethoscope className="size-3.5 mr-1.5" />
                Diagnose
              </Button>
            )}
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
        </div>

        {/* Report buttons */}
        <Can permission="reports:export">
          <div>
            <div className="text-xs font-medium text-muted-foreground mb-2 uppercase tracking-wider">
              Reports
            </div>
            <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-6 gap-2">
              {generalReports.map((r) => {
                const Icon = r.icon;
                return (
                  <button
                    key={r.id}
                    onClick={() => handleGenerate(r)}
                    disabled={loading}
                    className="flex flex-col items-center gap-1.5 p-3 rounded-lg text-xs font-medium border bg-background text-foreground border-border hover:bg-accent hover:border-accent-foreground/20 transition-all cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    <Icon className="size-5 text-muted-foreground" />
                    {r.label}
                  </button>
                );
              })}
              {isSuperAdmin && (
                <button
                  onClick={() => handleGenerate({ id: 'test-fixture', label: 'Test Fixture', icon: FlaskConical, type: 'test-fixture' })}
                  disabled={loading}
                  className="flex flex-col items-center gap-1.5 p-3 rounded-lg text-xs font-medium border bg-background text-amber-600 border-amber-300 hover:bg-amber-50 transition-all cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  <FlaskConical className="size-5" />
                  Test Fixture
                </button>
              )}
            </div>
          </div>

          {/* Framework reports */}
          <div>
            <div className="text-xs font-medium text-muted-foreground mb-2 uppercase tracking-wider">
              Framework Compliance
            </div>
            <div className="flex flex-wrap gap-2">
              {frameworkReports.map((r) => {
                const Icon = r.icon;
                return (
                  <button
                    key={r.id}
                    onClick={() => handleGenerate(r)}
                    disabled={loading}
                    className={`flex items-center gap-1.5 px-4 py-2 rounded-lg text-xs font-semibold border transition-all cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed ${r.color ?? ''}`}
                  >
                    <Icon className="size-4" />
                    {r.label}
                  </button>
                );
              })}
            </div>
          </div>
        </Can>
      </Card>

      {/* Full-screen loading overlay */}
      {loading && (
        <div className="fixed inset-0 z-[100] bg-background/80 backdrop-blur-sm flex flex-col items-center justify-center gap-4">
          <div className="relative">
            <div className="size-20 rounded-full border-4 border-muted animate-spin border-t-primary" />
            <Shield className="size-8 text-primary absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2" />
          </div>
          <div className="text-center space-y-1">
            <p className="text-lg font-semibold">{loadingLabel}</p>
            <p className="text-sm text-muted-foreground animate-pulse">Generating report...</p>
          </div>
        </div>
      )}

      {/* Preview dialog */}
      <Dialog open={previewOpen} onOpenChange={setPreviewOpen}>
        <DialogContent
          className="!max-w-none w-screen h-screen p-0 flex flex-col gap-0 rounded-none border-0"
          showCloseButton={false}
        >
          <DialogHeader className="px-5 pt-4 pb-3 flex-none border-b flex-row items-center justify-between">
            <DialogTitle className="text-base">{previewTitle}</DialogTitle>
            <div className="flex items-center gap-3">
              <div className="flex items-center gap-1 border rounded-md px-1">
                <Button variant="ghost" size="icon" className="size-7" onClick={() => setZoom(z => Math.max(0.3, z - 0.1))}>
                  <ZoomOut className="size-3.5" />
                </Button>
                <button
                  onClick={() => setZoom(1.5)}
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
              onLoad={onIframeLoad}
            />
          </div>
        </DialogContent>
      </Dialog>
    </>
  );
}

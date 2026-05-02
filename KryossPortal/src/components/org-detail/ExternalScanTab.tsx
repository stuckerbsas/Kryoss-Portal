import { useState, useMemo } from 'react';
import {
  Globe,
  Search,
  ShieldAlert,
  AlertTriangle,
  Loader2,
  Lock,
  Mail,
  ShieldCheck,
  Radar,
  Network,
  Cookie,
  Bug,
  Eye,
  ChevronDown,
  ChevronRight,
} from 'lucide-react';
import { toast } from 'sonner';
import {
  useExternalScanHistory,
  useExternalScanDetail,
  useStartExternalScan,
  useExternalScanTargets,
  useAutoExternalScan,
  useEnableExternalScanConsent,
} from '@/api/externalScan';
import type {
  ExternalScanDetail,
  ExternalScanFindingItem,
  ScanHistoryItem,
} from '@/api/externalScan';
import { useOrgParam } from '@/hooks/useOrgParam';
import { EmptyState } from '@/components/shared/EmptyState';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';
import { Tabs, TabsList, TabsTrigger, TabsContent } from '@/components/ui/tabs';

// ── Grade styling ──

const gradeConfig: Record<string, { bg: string; text: string; label: string }> = {
  A: { bg: 'bg-green-100', text: 'text-green-800', label: 'Excellent' },
  B: { bg: 'bg-blue-100', text: 'text-blue-800', label: 'Good' },
  C: { bg: 'bg-amber-100', text: 'text-amber-800', label: 'Fair' },
  D: { bg: 'bg-orange-100', text: 'text-orange-800', label: 'Poor' },
  F: { bg: 'bg-red-100', text: 'text-red-800', label: 'Critical' },
};

function GradeBadge({ grade, size = 'md' }: { grade: string; size?: 'sm' | 'md' | 'lg' }) {
  const cfg = gradeConfig[grade] ?? gradeConfig.F;
  const sizeClass = size === 'lg' ? 'text-4xl w-16 h-16' : size === 'md' ? 'text-lg w-9 h-9' : 'text-sm w-7 h-7';
  return (
    <div className={`${cfg.bg} ${cfg.text} ${sizeClass} rounded-lg font-bold flex items-center justify-center shrink-0`}>
      {grade}
    </div>
  );
}

function severityBadge(severity: string) {
  if (severity === 'pass') return <Badge variant="secondary" className="bg-green-100 text-green-800">PASS</Badge>;
  const config: Record<string, string> = {
    critical: 'bg-red-200 text-red-900',
    high: 'bg-red-100 text-red-800',
    medium: 'bg-amber-100 text-amber-800',
    low: 'bg-blue-100 text-blue-800',
    info: 'bg-gray-100 text-gray-600',
  };
  return <Badge variant="secondary" className={config[severity] ?? 'bg-gray-100 text-gray-500'}>{severity}</Badge>;
}

// ── Category config ──

const categoryMeta: Record<string, { title: string; icon: React.ReactNode; description: string }> = {
  port: { title: 'Port Exposure', icon: <AlertTriangle className="h-5 w-5" />, description: 'Open ports detected via Shodan intelligence' },
  tls: { title: 'TLS / Certificates', icon: <Lock className="h-5 w-5" />, description: 'Certificate validity, protocol version, key strength' },
  mail: { title: 'Email Authentication', icon: <Mail className="h-5 w-5" />, description: 'SPF, DKIM, DMARC, and MTA-STS configuration' },
  header: { title: 'HTTP Security Headers', icon: <ShieldCheck className="h-5 w-5" />, description: 'HSTS, CSP, X-Frame-Options and other headers' },
  dns: { title: 'DNS Health', icon: <Network className="h-5 w-5" />, description: 'Nameservers, DNSSEC, CAA, dangling CNAMEs' },
  web: { title: 'Web Security', icon: <Cookie className="h-5 w-5" />, description: 'HTTPS redirect, cookie flags' },
  vuln: { title: 'Known Vulnerabilities', icon: <Bug className="h-5 w-5" />, description: 'CVEs detected on exposed services' },
  recon: { title: 'Reconnaissance', icon: <Eye className="h-5 w-5" />, description: 'Subdomain discovery via Certificate Transparency' },
};

const categoryOrder = ['port', 'vuln', 'tls', 'mail', 'header', 'dns', 'web', 'recon'];

// ── Category Card ──

function CategoryCard({ category, grade, findings }: { category: string; grade: string; findings: ExternalScanFindingItem[] }) {
  const [open, setOpen] = useState(grade !== 'A');
  const meta = categoryMeta[category] ?? { title: category, icon: <ShieldAlert className="h-5 w-5" />, description: '' };
  const issues = findings.filter(f => f.severity !== 'pass' && f.severity !== 'info');
  const passes = findings.filter(f => f.severity === 'pass');

  return (
    <Card>
      <CardHeader
        className="cursor-pointer select-none pb-3"
        onClick={() => setOpen(!open)}
      >
        <div className="flex items-center gap-3">
          <GradeBadge grade={grade} size="md" />
          <div className="flex-1 min-w-0">
            <CardTitle className="flex items-center gap-2 text-base">
              {meta.icon}
              {meta.title}
            </CardTitle>
            <p className="text-xs text-muted-foreground mt-0.5">{meta.description}</p>
          </div>
          <div className="flex items-center gap-2">
            {issues.length > 0 && (
              <Badge variant="secondary" className="bg-red-50 text-red-700 text-xs">
                {issues.length} issue{issues.length !== 1 ? 's' : ''}
              </Badge>
            )}
            {passes.length > 0 && (
              <Badge variant="secondary" className="bg-green-50 text-green-700 text-xs">
                {passes.length} pass
              </Badge>
            )}
            {open ? <ChevronDown className="h-4 w-4 text-muted-foreground" /> : <ChevronRight className="h-4 w-4 text-muted-foreground" />}
          </div>
        </div>
      </CardHeader>
      {open && (
        <CardContent className="space-y-2 pt-0">
          {findings.map((f, i) => (
            <div key={i} className={`rounded-md p-3 ${f.severity === 'pass' ? 'bg-green-50/50 border border-green-100' : 'bg-muted/30 border'}`}>
              <div className="flex items-center gap-2 mb-1">
                {severityBadge(f.severity)}
                <span className="font-medium text-sm">{f.title}</span>
                {f.port && <span className="text-xs text-muted-foreground font-mono">:{f.port}</span>}
              </div>
              {f.description && <p className="text-xs text-muted-foreground">{f.description}</p>}
              {f.remediation && <p className="text-xs text-blue-600 mt-1">{f.remediation}</p>}
            </div>
          ))}
        </CardContent>
      )}
    </Card>
  );
}

// ── Scan Detail ──

function ScanDetail({ scan }: { scan: ExternalScanDetail }) {
  const categoryScores: Record<string, string> = useMemo(() => {
    if (!scan.categoryScores) return {};
    try { return JSON.parse(scan.categoryScores); } catch { return {}; }
  }, [scan.categoryScores]);

  const grouped = useMemo(() => {
    const map = new Map<string, ExternalScanFindingItem[]>();
    for (const f of scan.findings ?? []) {
      const cat = f.category ?? 'port';
      if (!map.has(cat)) map.set(cat, []);
      map.get(cat)!.push(f);
    }
    return map;
  }, [scan.findings]);

  const overallGrade = scan.overallGrade ?? 'A';
  const gradeCfg = gradeConfig[overallGrade] ?? gradeConfig.F;
  const issues = (scan.findings ?? []).filter(f => f.severity !== 'pass' && f.severity !== 'info');
  const criticals = issues.filter(f => f.severity === 'critical').length;
  const highs = issues.filter(f => f.severity === 'high').length;

  return (
    <div className="space-y-6">
      {/* Overall Score */}
      <Card>
        <CardContent className="py-6">
          <div className="flex items-center gap-6">
            <GradeBadge grade={overallGrade} size="lg" />
            <div className="flex-1">
              <h3 className="text-xl font-bold">{gradeCfg.label}</h3>
              <p className="text-sm text-muted-foreground">
                {scan.target} — {issues.length} issue{issues.length !== 1 ? 's' : ''} found
                {criticals > 0 && <span className="text-red-600 font-medium"> ({criticals} critical)</span>}
                {highs > 0 && criticals === 0 && <span className="text-red-600 font-medium"> ({highs} high)</span>}
              </p>
              <p className="text-xs text-muted-foreground mt-1">
                Scanned: {scan.completedAt ? new Date(scan.completedAt).toLocaleString() : '--'}
                {scan.results?.length > 0 && ` — ${scan.results.length} open port${scan.results.length !== 1 ? 's' : ''} detected`}
              </p>
            </div>
            {/* Mini grade chips */}
            <div className="hidden md:flex flex-wrap gap-1.5">
              {categoryOrder.map(cat => {
                const grade = categoryScores[cat];
                if (!grade) return null;
                const meta = categoryMeta[cat];
                return (
                  <div key={cat} className="flex items-center gap-1" title={meta?.title}>
                    <GradeBadge grade={grade} size="sm" />
                    <span className="text-[10px] text-muted-foreground">{meta?.title?.split(' ')[0]}</span>
                  </div>
                );
              })}
            </div>
          </div>
        </CardContent>
      </Card>

      {/* Category Cards */}
      {categoryOrder.map(cat => {
        const findings = grouped.get(cat);
        if (!findings || findings.length === 0) return null;
        const grade = categoryScores[cat] ?? 'A';
        return <CategoryCard key={cat} category={cat} grade={grade} findings={findings} />;
      })}

      {/* Open Ports Summary */}
      {scan.results && scan.results.length > 0 && (
        <Card>
          <CardHeader className="pb-3">
            <CardTitle className="flex items-center gap-2 text-base">
              <Globe className="h-4 w-4 text-muted-foreground" />
              Open Ports ({scan.results.length})
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="flex flex-wrap gap-2">
              {scan.results.map((r, i) => {
                const riskColor = r.risk === 'critical' ? 'bg-red-100 text-red-800 border-red-200'
                  : r.risk === 'high' ? 'bg-red-50 text-red-700 border-red-100'
                  : r.risk === 'medium' ? 'bg-amber-50 text-amber-700 border-amber-100'
                  : 'bg-gray-50 text-gray-700 border-gray-200';
                return (
                  <div key={i} className={`inline-flex items-center gap-1.5 rounded-md border px-2.5 py-1 text-xs font-mono ${riskColor}`}>
                    <span className="font-bold">{r.port}</span>
                    {r.service && <span className="text-[10px] opacity-75">/{r.service}</span>}
                    {r.serviceName && <span className="text-[10px] opacity-60">{r.serviceName}{r.serviceVersion ? ` ${r.serviceVersion}` : ''}</span>}
                  </div>
                );
              })}
            </div>
          </CardContent>
        </Card>
      )}
    </div>
  );
}

// ── Main Tab ──

export function ExternalScanTab() {
  const { orgId } = useOrgParam();
  const enableConsent = useEnableExternalScanConsent();
  const { data: historyData, isLoading } = useExternalScanHistory(orgId);
  const { data: targetsData } = useExternalScanTargets(orgId);
  const startScan = useStartExternalScan();
  const autoScan = useAutoExternalScan();
  const [target, setTarget] = useState('');
  const [selectedScanId, setSelectedScanId] = useState<string>();
  const [needsConsent, setNeedsConsent] = useState(false);

  const targets = targetsData?.targets ?? [];
  const history = historyData ?? [];

  const latestByTarget = useMemo(() => {
    const map = new Map<string, ScanHistoryItem>();
    for (const item of history) {
      if (!map.has(item.target)) map.set(item.target, item);
    }
    return Array.from(map.values());
  }, [history]);

  const activeScanId = selectedScanId ?? latestByTarget[0]?.id;
  const { data: selectedScan, isLoading: detailLoading } = useExternalScanDetail(activeScanId);

  const handleEnableConsent = async () => {
    if (!orgId) return;
    try {
      await enableConsent.mutateAsync({ organizationId: orgId });
      setNeedsConsent(false);
      toast.success('External scanning enabled — you can now run scans.');
    } catch (err: any) {
      toast.error(`Failed to enable: ${err.message}`);
    }
  };

  const isConsentError = (msg: string) => msg?.toLowerCase().includes('consent');

  const handleScan = async () => {
    if (!orgId || !target.trim()) return;
    try {
      const result = await startScan.mutateAsync({ organizationId: orgId, target: target.trim() });
      setSelectedScanId(result.scanId);
      toast.success('Scan complete');
    } catch (err: any) {
      if (isConsentError(err.message)) { setNeedsConsent(true); return; }
      toast.error(`Scan failed: ${err.message}`);
    }
  };

  const handleAutoScan = async () => {
    if (!orgId) return;
    try {
      const result = await autoScan.mutateAsync({ organizationId: orgId });
      const ok = result.scanIds.filter(s => s.scanId).length;
      const fail = result.scanIds.filter(s => !s.scanId).length;
      const first = result.scanIds.find(s => s.scanId);
      if (first?.scanId) setSelectedScanId(first.scanId);
      toast.success(`Auto-scan: ${ok} scanned${fail > 0 ? `, ${fail} failed` : ''}`);
    } catch (err: any) {
      if (isConsentError(err.message)) { setNeedsConsent(true); return; }
      toast.error(`Auto-scan failed: ${err.message}`);
    }
  };

  if (isLoading) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-10 w-full max-w-lg" />
        <Skeleton className="h-24" />
        <div className="space-y-4">
          {Array.from({ length: 3 }).map((_, i) => <Skeleton key={i} className="h-20" />)}
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div>
        <h3 className="text-lg font-semibold">External Exposure Report</h3>
        <p className="text-sm text-muted-foreground">
          Security assessment of public-facing infrastructure: ports, TLS, email auth, DNS, and web hardening.
        </p>
      </div>

      {needsConsent && (
        <Card className="border-amber-200 bg-amber-50/50">
          <CardContent className="py-5">
            <div className="flex items-center gap-4">
              <ShieldAlert className="h-8 w-8 text-amber-500 shrink-0" />
              <div className="flex-1">
                <h4 className="font-semibold text-sm">External Scanning Requires Consent</h4>
                <p className="text-xs text-muted-foreground mt-0.5">
                  External scans probe public IPs and domains to detect exposed services. Enable to continue.
                </p>
              </div>
              <Button size="sm" onClick={handleEnableConsent} disabled={enableConsent.isPending}>
                {enableConsent.isPending ? <Loader2 className="mr-1.5 h-4 w-4 animate-spin" /> : <ShieldCheck className="mr-1.5 h-4 w-4" />}
                Enable
              </Button>
            </div>
          </CardContent>
        </Card>
      )}

      <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
        <div className="flex items-center gap-3 flex-1 max-w-xl">
          <Input
            placeholder="Enter domain (e.g. example.com) or IP address"
            value={target}
            onChange={(e) => setTarget(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && handleScan()}
            disabled={startScan.isPending || autoScan.isPending}
          />
          <Button onClick={handleScan} disabled={!target.trim() || startScan.isPending || autoScan.isPending}>
            {startScan.isPending ? <Loader2 className="mr-1.5 h-4 w-4 animate-spin" /> : <Search className="mr-1.5 h-4 w-4" />}
            Scan
          </Button>
        </div>
        {targets.length > 0 && (
          <Button variant="outline" onClick={handleAutoScan} disabled={autoScan.isPending || startScan.isPending}>
            {autoScan.isPending ? <Loader2 className="mr-1.5 h-4 w-4 animate-spin" /> : <Radar className="mr-1.5 h-4 w-4" />}
            Auto-Scan ({targets.length})
          </Button>
        )}
      </div>

      {targets.length > 0 && (
        <div className="flex flex-wrap gap-2">
          {targets.map((t, i) => (
            <Badge key={i} variant="outline" className="text-xs font-mono">
              {t.source === 'network_site' ? <Globe className="h-3 w-3 mr-1" /> : <Mail className="h-3 w-3 mr-1" />}
              {t.value}{t.label ? ` (${t.label})` : ''}
            </Badge>
          ))}
        </div>
      )}

      {latestByTarget.length === 0 && !startScan.isPending && !autoScan.isPending && (
        <EmptyState
          icon={<Globe className="size-10" />}
          title="No external scans yet"
          description={targets.length > 0
            ? `${targets.length} targets discovered. Click "Auto-Scan" to scan all, or enter a target manually.`
            : 'Enter a domain or IP address above to run your first external scan.'}
        />
      )}

      {latestByTarget.length === 1 && selectedScan && <ScanDetail scan={selectedScan} />}

      {latestByTarget.length > 1 && (
        <Tabs value={activeScanId} onValueChange={setSelectedScanId}>
          <TabsList className="w-full overflow-x-auto flex-wrap h-auto">
            {latestByTarget.map((s) => (
              <TabsTrigger key={s.id} value={s.id} className="text-xs font-mono gap-1.5">
                {s.overallGrade && <GradeBadge grade={s.overallGrade} size="sm" />}
                {s.target}
                {s.totalFindings > 0 && (
                  <Badge variant="secondary" className="text-[10px] px-1 py-0">
                    {s.totalFindings}
                  </Badge>
                )}
              </TabsTrigger>
            ))}
          </TabsList>
          {latestByTarget.map((s) => (
            <TabsContent key={s.id} value={s.id}>
              {activeScanId === s.id && detailLoading && (
                <div className="space-y-4 py-4">
                  <Skeleton className="h-24" />
                  {Array.from({ length: 3 }).map((_, i) => <Skeleton key={i} className="h-20" />)}
                </div>
              )}
              {activeScanId === s.id && selectedScan && <ScanDetail scan={selectedScan} />}
            </TabsContent>
          ))}
        </Tabs>
      )}
    </div>
  );
}

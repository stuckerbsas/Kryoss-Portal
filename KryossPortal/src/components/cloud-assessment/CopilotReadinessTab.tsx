import {
  useCloudAssessment,
  useCloudAssessmentDetail,
  type CopilotReadinessScores,
  type SharepointSiteData,
  type ExternalUserData,
} from '@/api/cloudAssessment';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { BrainCircuit, ShieldCheck, Share2, FileCheck, Globe, Lock } from 'lucide-react';

const DIMENSIONS = [
  { key: 'd1Labels', label: 'D1 — Sensitivity Labels', icon: FileCheck, desc: 'File labeling coverage across SharePoint' },
  { key: 'd2Oversharing', label: 'D2 — Oversharing', icon: Share2, desc: 'Files shared too broadly (org-wide or public)' },
  { key: 'd3External', label: 'D3 — External Sharing', icon: Globe, desc: 'Guest/external user risk posture' },
  { key: 'd4ConditionalAccess', label: 'D4 — Conditional Access', icon: Lock, desc: 'CA policy coverage for Copilot access' },
  { key: 'd5ZeroTrust', label: 'D5 — Zero Trust', icon: ShieldCheck, desc: 'Identity + endpoint security gaps' },
  { key: 'd6Purview', label: 'D6 — Purview / DLP', icon: FileCheck, desc: 'Data loss prevention and compliance readiness' },
] as const;

function scoreColor(score: number): string {
  if (score >= 4) return 'text-green-700 bg-green-100';
  if (score >= 3) return 'text-amber-700 bg-amber-100';
  return 'text-red-700 bg-red-100';
}

function verdictBadge(verdict: string) {
  const colors: Record<string, string> = {
    'Ready': 'bg-green-100 text-green-800',
    'Nearly Ready': 'bg-amber-100 text-amber-800',
    'Not Ready': 'bg-red-100 text-red-800',
  };
  return <Badge className={colors[verdict] ?? 'bg-gray-100'}>{verdict}</Badge>;
}

function ScoreRadar({ scores }: { scores: CopilotReadinessScores }) {
  const dims = [
    { label: 'Labels', value: scores.d1Labels },
    { label: 'Oversharing', value: scores.d2Oversharing },
    { label: 'External', value: scores.d3External },
    { label: 'CA', value: scores.d4ConditionalAccess },
    { label: 'Zero Trust', value: scores.d5ZeroTrust },
    { label: 'Purview', value: scores.d6Purview },
  ];

  const cx = 120, cy = 120, r = 90;
  const angleStep = (2 * Math.PI) / dims.length;
  const levels = [1, 2, 3, 4, 5];

  const getPoint = (angle: number, val: number) => ({
    x: cx + (r * val / 5) * Math.cos(angle - Math.PI / 2),
    y: cy + (r * val / 5) * Math.sin(angle - Math.PI / 2),
  });

  const dataPoints = dims.map((d, i) => getPoint(i * angleStep, d.value));
  const polygon = dataPoints.map(p => `${p.x},${p.y}`).join(' ');

  return (
    <svg viewBox="0 0 240 240" className="w-full max-w-[280px] mx-auto">
      {levels.map(l => (
        <polygon
          key={l}
          points={dims.map((_, i) => {
            const p = getPoint(i * angleStep, l);
            return `${p.x},${p.y}`;
          }).join(' ')}
          fill="none"
          stroke="#e5e7eb"
          strokeWidth="1"
        />
      ))}
      {dims.map((_, i) => {
        const p = getPoint(i * angleStep, 5);
        return <line key={i} x1={cx} y1={cy} x2={p.x} y2={p.y} stroke="#e5e7eb" strokeWidth="1" />;
      })}
      <polygon points={polygon} fill="rgba(34,197,94,0.2)" stroke="#22c55e" strokeWidth="2" />
      {dims.map((d, i) => {
        const p = getPoint(i * angleStep, 5.8);
        return (
          <text key={i} x={p.x} y={p.y} textAnchor="middle" dominantBaseline="central" className="text-[9px] fill-gray-600">
            {d.label}
          </text>
        );
      })}
      {dataPoints.map((p, i) => (
        <circle key={i} cx={p.x} cy={p.y} r="3" fill="#22c55e" />
      ))}
    </svg>
  );
}

function SharepointSitesTable({ sites }: { sites: SharepointSiteData[] }) {
  if (sites.length === 0) return null;

  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-sm">SharePoint Sites — Label & Oversharing Audit</CardTitle>
      </CardHeader>
      <CardContent className="p-0 overflow-x-auto">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Site</TableHead>
              <TableHead className="text-right">Files</TableHead>
              <TableHead className="text-right">Labeled</TableHead>
              <TableHead className="text-right">Overshared</TableHead>
              <TableHead>Risk</TableHead>
              <TableHead>Labels</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {sites.map((s, i) => {
              const labelPct = s.totalFiles > 0 ? Math.round(s.labeledFiles * 100 / s.totalFiles) : 0;
              return (
                <TableRow key={i}>
                  <TableCell className="text-sm font-medium max-w-xs truncate" title={s.siteUrl}>
                    {s.siteTitle ?? s.siteUrl}
                  </TableCell>
                  <TableCell className="text-right text-sm">{s.totalFiles}</TableCell>
                  <TableCell className="text-right text-sm">{s.labeledFiles} ({labelPct}%)</TableCell>
                  <TableCell className="text-right text-sm">{s.oversharedFiles}</TableCell>
                  <TableCell>
                    <Badge variant="secondary" className={
                      s.riskLevel === 'High' ? 'bg-red-100 text-red-800' :
                      s.riskLevel === 'Medium' ? 'bg-amber-100 text-amber-800' :
                      'bg-green-100 text-green-800'
                    }>{s.riskLevel ?? 'Low'}</Badge>
                  </TableCell>
                  <TableCell className="text-xs text-muted-foreground max-w-xs truncate">{s.topLabels ?? '—'}</TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  );
}

function ExternalUsersTable({ users }: { users: ExternalUserData[] }) {
  if (users.length === 0) return null;

  const high = users.filter(u => u.riskLevel === 'High').length;
  const medium = users.filter(u => u.riskLevel === 'Medium').length;

  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-sm flex items-center gap-2">
          External / Guest Users
          <Badge variant="secondary">{users.length} total</Badge>
          {high > 0 && <Badge variant="secondary" className="bg-red-100 text-red-800">{high} high risk</Badge>}
          {medium > 0 && <Badge variant="secondary" className="bg-amber-100 text-amber-800">{medium} stale</Badge>}
        </CardTitle>
      </CardHeader>
      <CardContent className="p-0 overflow-x-auto">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>User</TableHead>
              <TableHead>Domain</TableHead>
              <TableHead>Last Sign-In</TableHead>
              <TableHead>Risk</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {users.slice(0, 50).map((u, i) => (
              <TableRow key={i}>
                <TableCell className="text-sm">{u.displayName ?? u.userPrincipal}</TableCell>
                <TableCell className="text-sm text-muted-foreground">{u.emailDomain ?? '—'}</TableCell>
                <TableCell className="text-sm text-muted-foreground">
                  {u.lastSignIn ? new Date(u.lastSignIn).toLocaleDateString() : 'Never'}
                </TableCell>
                <TableCell>
                  <Badge variant="secondary" className={
                    u.riskLevel === 'High' ? 'bg-red-100 text-red-800' :
                    u.riskLevel === 'Medium' ? 'bg-amber-100 text-amber-800' :
                    'bg-green-100 text-green-800'
                  }>{u.riskLevel ?? 'Low'}</Badge>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
        {users.length > 50 && (
          <p className="text-xs text-muted-foreground text-center py-2">
            Showing 50 of {users.length} external users
          </p>
        )}
      </CardContent>
    </Card>
  );
}

export function CopilotReadinessTab({
  orgId,
  scanId,
}: {
  orgId: string;
  scanId: string | undefined;
}) {
  const { data: summary } = useCloudAssessment(orgId);
  const { data: detail } = useCloudAssessmentDetail(scanId);

  const scores = summary && 'copilotReadiness' in summary
    ? (summary as any).copilotReadiness as CopilotReadinessScores | null
    : null;

  if (!scanId || !scores) {
    return (
      <Card>
        <CardContent className="py-10 text-center text-sm text-muted-foreground">
          <BrainCircuit className="h-8 w-8 mx-auto mb-3 text-gray-400" />
          <p>Run a Cloud Assessment scan to see Copilot Readiness scores.</p>
          <p className="text-xs mt-1">Copilot Readiness is now computed automatically as part of every Cloud Assessment scan.</p>
        </CardContent>
      </Card>
    );
  }

  const spSites: SharepointSiteData[] = detail && 'sharepointSites' in detail
    ? (detail as any).sharepointSites ?? []
    : [];
  const extUsers: ExternalUserData[] = detail && 'externalUsers' in detail
    ? (detail as any).externalUsers ?? []
    : [];

  return (
    <div className="space-y-4">
      {/* Overall verdict + radar */}
      <div className="grid md:grid-cols-2 gap-4">
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm flex items-center gap-2">
              <BrainCircuit className="h-4 w-4" />
              Copilot Readiness Score
            </CardTitle>
          </CardHeader>
          <CardContent className="flex flex-col items-center gap-3">
            <div className="text-4xl font-bold">{scores.overall.toFixed(1)}<span className="text-lg text-muted-foreground">/5.0</span></div>
            {verdictBadge(scores.copilotVerdict)}
            <p className="text-xs text-muted-foreground text-center mt-1">
              {scores.copilotVerdict === 'Ready'
                ? 'Environment is ready for Microsoft 365 Copilot deployment.'
                : scores.copilotVerdict === 'Nearly Ready'
                ? 'A few items need attention before deploying Copilot.'
                : 'Significant gaps must be addressed before Copilot deployment.'}
            </p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm">Dimension Radar</CardTitle>
          </CardHeader>
          <CardContent>
            <ScoreRadar scores={scores} />
          </CardContent>
        </Card>
      </div>

      {/* D1-D6 dimension cards */}
      <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-3">
        {DIMENSIONS.map(d => {
          const value = scores[d.key as keyof CopilotReadinessScores] as number;
          return (
            <Card key={d.key}>
              <CardContent className="py-3 px-4">
                <div className="flex items-center gap-1.5 mb-1">
                  <d.icon className="h-3.5 w-3.5 text-muted-foreground" />
                  <p className="text-xs text-muted-foreground truncate">{d.label}</p>
                </div>
                <div className="flex items-center gap-2">
                  <span className={`text-xl font-semibold rounded px-1.5 ${scoreColor(value)}`}>{value.toFixed(1)}</span>
                  <span className="text-xs text-muted-foreground">/5</span>
                </div>
                <p className="text-[10px] text-muted-foreground mt-1 leading-tight">{d.desc}</p>
              </CardContent>
            </Card>
          );
        })}
      </div>

      {/* SharePoint sites */}
      <SharepointSitesTable sites={spSites} />

      {/* External users */}
      <ExternalUsersTable users={extUsers} />
    </div>
  );
}

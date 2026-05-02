import { useState } from 'react';
import {
  useComplianceScores,
  useComplianceDrilldown,
  type ComplianceFrameworkScore,
} from '@/api/cloudAssessment';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { Button } from '@/components/ui/button';
import { ArrowLeft, Loader2, ShieldCheck } from 'lucide-react';

const GRADE_COLORS: Record<string, string> = {
  'A+': 'bg-green-100 text-green-800',
  A: 'bg-green-100 text-green-700',
  B: 'bg-lime-100 text-lime-800',
  C: 'bg-amber-100 text-amber-800',
  D: 'bg-orange-100 text-orange-800',
  F: 'bg-red-100 text-red-800',
};

const CONTROL_STATUS_BADGE: Record<string, string> = {
  passing: 'bg-green-100 text-green-800',
  failing: 'bg-red-100 text-red-800',
  unmapped: 'bg-gray-100 text-gray-600',
  no_data: 'bg-gray-100 text-gray-500',
};

function ScoreBar({ pct }: { pct: number }) {
  const color =
    pct >= 85 ? 'bg-green-500' : pct >= 60 ? 'bg-amber-500' : 'bg-red-500';
  return (
    <div className="w-full h-2 bg-gray-200 rounded-full overflow-hidden">
      <div className={`h-full ${color} rounded-full`} style={{ width: `${pct}%` }} />
    </div>
  );
}

function FrameworkCard({
  score,
  onClick,
}: {
  score: ComplianceFrameworkScore;
  onClick: () => void;
}) {
  return (
    <Card
      className="cursor-pointer hover:ring-2 hover:ring-primary/30 transition-shadow"
      onClick={onClick}
    >
      <CardHeader className="pb-2">
        <div className="flex items-center justify-between">
          <CardTitle className="text-sm font-medium">{score.frameworkName}</CardTitle>
          <Badge className={GRADE_COLORS[score.grade ?? 'F'] ?? 'bg-gray-100'}>
            {score.grade ?? '—'}
          </Badge>
        </div>
        <p className="text-xs text-muted-foreground">{score.frameworkCode}</p>
      </CardHeader>
      <CardContent className="space-y-2">
        <div className="flex items-center justify-between text-sm">
          <span className="text-muted-foreground">Score</span>
          <span className="font-semibold">{score.scorePct.toFixed(1)}%</span>
        </div>
        <ScoreBar pct={score.scorePct} />
        <div className="grid grid-cols-3 gap-2 text-xs text-muted-foreground pt-1">
          <div>
            <span className="text-green-600 font-medium">{score.passingControls}</span> passing
          </div>
          <div>
            <span className="text-red-600 font-medium">{score.failingControls}</span> failing
          </div>
          <div>
            <span className="text-gray-500 font-medium">{score.unmappedControls}</span> unmapped
          </div>
        </div>
        <div className="text-xs text-muted-foreground">
          {score.coveredControls} / {score.totalControls} controls covered
        </div>
      </CardContent>
    </Card>
  );
}

function DrilldownView({
  frameworkCode,
  scanId,
  onBack,
}: {
  frameworkCode: string;
  scanId: string;
  onBack: () => void;
}) {
  const { data, isLoading } = useComplianceDrilldown(frameworkCode, scanId);

  if (isLoading) {
    return (
      <Card>
        <CardContent className="py-16 flex flex-col items-center justify-center gap-3">
          <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
          <span className="text-sm text-muted-foreground">Loading controls…</span>
        </CardContent>
      </Card>
    );
  }

  if (!data) return null;

  const controls = data.controls;
  const passingCount = controls.filter((c) => c.status === 'passing').length;
  const failingCount = controls.filter((c) => c.status === 'failing').length;

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-3">
        <Button variant="ghost" size="sm" onClick={onBack}>
          <ArrowLeft className="h-4 w-4 mr-1" /> Back
        </Button>
        <div>
          <h3 className="text-lg font-semibold">{data.framework.name}</h3>
          <p className="text-xs text-muted-foreground">
            {data.framework.code} {data.framework.version ? `v${data.framework.version}` : ''} — {passingCount} passing, {failingCount} failing, {controls.length} total
          </p>
        </div>
      </div>

      <Card>
        <CardContent className="p-0">
          {/* Mobile cards */}
          <div className="space-y-3 p-4 sm:hidden">
            {controls.map((ctrl) => (
              <div key={ctrl.controlCode} className="rounded-lg border p-4 space-y-2">
                <div className="flex items-center justify-between gap-2">
                  <span className="font-medium text-sm font-mono">{ctrl.controlCode}</span>
                  <Badge variant="secondary" className={CONTROL_STATUS_BADGE[ctrl.status] ?? ''}>
                    {ctrl.status.replace('_', ' ')}
                  </Badge>
                </div>
                <p className="text-sm truncate" title={ctrl.title}>{ctrl.title}</p>
                {ctrl.category && (
                  <p className="text-xs text-muted-foreground">{ctrl.category}</p>
                )}
              </div>
            ))}
          </div>
          {/* Desktop table */}
          <div className="hidden sm:block overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Control</TableHead>
                <TableHead>Title</TableHead>
                <TableHead>Category</TableHead>
                <TableHead>Status</TableHead>
                <TableHead className="hidden lg:table-cell">Mapped Findings</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {controls.map((ctrl) => (
                <TableRow key={ctrl.controlCode}>
                  <TableCell className="text-sm font-mono whitespace-nowrap">
                    {ctrl.controlCode}
                  </TableCell>
                  <TableCell className="text-sm max-w-xs">
                    <div className="truncate" title={ctrl.title}>{ctrl.title}</div>
                  </TableCell>
                  <TableCell className="text-sm text-muted-foreground whitespace-nowrap">
                    {ctrl.category ?? '—'}
                  </TableCell>
                  <TableCell>
                    <Badge
                      variant="secondary"
                      className={CONTROL_STATUS_BADGE[ctrl.status] ?? ''}
                    >
                      {ctrl.status.replace('_', ' ')}
                    </Badge>
                  </TableCell>
                  <TableCell className="text-xs text-muted-foreground max-w-sm hidden lg:table-cell">
                    {ctrl.mappedFindings.length === 0
                      ? '—'
                      : ctrl.mappedFindings.map((mf, i) => (
                          <span key={i} className="block">
                            {mf.area}/{mf.service}/{mf.feature}
                            {mf.findingStatus ? ` (${mf.findingStatus})` : ''}
                            <span className="text-gray-400"> [{mf.coverage}]</span>
                          </span>
                        ))}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}

export function ComplianceTab({
  orgId,
  scanId,
}: {
  orgId: string;
  scanId: string | undefined;
}) {
  const { data: scores, isLoading } = useComplianceScores(orgId, scanId);
  const [selectedFramework, setSelectedFramework] = useState<string | null>(null);

  if (!scanId) {
    return (
      <Card>
        <CardContent className="py-10 text-center text-sm text-muted-foreground">
          Run a scan from the Overview tab to see compliance scores.
        </CardContent>
      </Card>
    );
  }

  if (isLoading) {
    return (
      <Card>
        <CardContent className="py-16 flex flex-col items-center justify-center gap-3">
          <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
          <span className="text-sm text-muted-foreground">Loading compliance data…</span>
        </CardContent>
      </Card>
    );
  }

  if (selectedFramework) {
    return (
      <DrilldownView
        frameworkCode={selectedFramework}
        scanId={scanId}
        onBack={() => setSelectedFramework(null)}
      />
    );
  }

  if (!scores || scores.length === 0) {
    return (
      <Card>
        <CardContent className="py-10 text-center text-sm text-muted-foreground">
          <ShieldCheck className="h-8 w-8 mx-auto mb-2 text-muted-foreground/50" />
          No compliance framework scores available for this scan.
        </CardContent>
      </Card>
    );
  }

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4">
        {scores.map((s) => (
          <FrameworkCard
            key={s.frameworkId}
            score={s}
            onClick={() => setSelectedFramework(s.frameworkCode)}
          />
        ))}
      </div>
    </div>
  );
}

import { useState, useEffect } from 'react';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription } from '@/components/ui/dialog';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { useCloudAssessmentHistory, useCloudAssessmentCompare } from '@/api/cloudAssessment';
import { CheckCircle, AlertTriangle, TrendingUp, TrendingDown, Minus, Loader2 } from 'lucide-react';

interface CompareModalProps {
  organizationId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

function fmtDate(s: string): string {
  return new Date(s).toLocaleString(undefined, { year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
}

function deltaBadge(delta: number) {
  if (delta > 0) {
    return (
      <span className="inline-flex items-center gap-0.5 text-green-600 text-xs font-medium">
        <TrendingUp className="h-3 w-3" /> +{delta.toFixed(2)}
      </span>
    );
  }
  if (delta < 0) {
    return (
      <span className="inline-flex items-center gap-0.5 text-red-600 text-xs font-medium">
        <TrendingDown className="h-3 w-3" /> {delta.toFixed(2)}
      </span>
    );
  }
  return (
    <span className="inline-flex items-center gap-0.5 text-muted-foreground text-xs">
      <Minus className="h-3 w-3" /> 0.00
    </span>
  );
}

const AREA_LABELS: Record<string, string> = {
  identity: 'Identity',
  endpoint: 'Endpoint',
  data: 'Data',
  productivity: 'Productivity',
};

export function CompareModal({ organizationId, open, onOpenChange }: CompareModalProps) {
  const { data: history } = useCloudAssessmentHistory(organizationId, 20);
  const completedHistory = (history ?? []).filter(h => h.status === 'completed' || h.status === 'partial');

  const [scanBId, setScanBId] = useState<string | undefined>();
  const [scanAId, setScanAId] = useState<string | undefined>();

  // Default selection = latest two scans. scanA = older (baseline), scanB = newer (current).
  useEffect(() => {
    if (!open || completedHistory.length < 2) return;
    if (!scanBId) setScanBId(completedHistory[0].id);
    if (!scanAId) setScanAId(completedHistory[1].id);
  }, [open, completedHistory, scanAId, scanBId]);

  const { data: compare, isLoading } = useCloudAssessmentCompare(scanAId, scanBId);

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-4xl max-h-[85vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Compare Scans</DialogTitle>
          <DialogDescription>
            Side-by-side diff of two Cloud Assessment scans. Baseline to Current.
          </DialogDescription>
        </DialogHeader>

        {completedHistory.length < 2 && (
          <div className="py-8 text-center text-sm text-muted-foreground">
            At least two completed scans are required to compare.
          </div>
        )}

        {completedHistory.length >= 2 && (
          <div className="space-y-5">
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label className="text-xs text-muted-foreground uppercase tracking-wide">Baseline (A)</label>
                <Select value={scanAId} onValueChange={setScanAId}>
                  <SelectTrigger><SelectValue placeholder="Pick baseline scan" /></SelectTrigger>
                  <SelectContent>
                    {completedHistory.map((h) => (
                      <SelectItem key={h.id} value={h.id}>
                        {fmtDate(h.createdAt)} — {h.verdict ?? '—'} ({h.overallScore ?? '—'})
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div>
                <label className="text-xs text-muted-foreground uppercase tracking-wide">Current (B)</label>
                <Select value={scanBId} onValueChange={setScanBId}>
                  <SelectTrigger><SelectValue placeholder="Pick current scan" /></SelectTrigger>
                  <SelectContent>
                    {completedHistory.map((h) => (
                      <SelectItem key={h.id} value={h.id}>
                        {fmtDate(h.createdAt)} — {h.verdict ?? '—'} ({h.overallScore ?? '—'})
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            </div>

            {scanAId === scanBId && scanAId && (
              <div className="text-sm text-amber-600 bg-amber-50 border border-amber-200 rounded p-2">
                Baseline and current are the same scan. Pick two different scans.
              </div>
            )}

            {isLoading && (
              <div className="flex items-center justify-center py-10 gap-2 text-sm text-muted-foreground">
                <Loader2 className="h-4 w-4 animate-spin" /> Loading comparison...
              </div>
            )}

            {compare && (
              <>
                {/* Overall delta */}
                <Card>
                  <CardHeader className="pb-2"><CardTitle className="text-sm">Overall</CardTitle></CardHeader>
                  <CardContent>
                    <div className="flex items-center gap-6">
                      <div>
                        <div className="text-xs text-muted-foreground">Baseline</div>
                        <div className="text-2xl font-bold">{compare.scanA.overallScore ?? '—'}</div>
                        <div className="text-xs text-muted-foreground">{compare.scanA.verdict ?? '—'}</div>
                      </div>
                      <div className="text-muted-foreground">→</div>
                      <div>
                        <div className="text-xs text-muted-foreground">Current</div>
                        <div className="text-2xl font-bold">{compare.scanB.overallScore ?? '—'}</div>
                        <div className="text-xs text-muted-foreground">{compare.scanB.verdict ?? '—'}</div>
                      </div>
                      <div className="ml-auto">{deltaBadge(compare.deltas.overall ?? 0)}</div>
                    </div>
                  </CardContent>
                </Card>

                {/* Area deltas */}
                <Card>
                  <CardHeader className="pb-2"><CardTitle className="text-sm">Area Scores</CardTitle></CardHeader>
                  <CardContent className="space-y-2">
                    {Object.entries(AREA_LABELS).map(([key, label]) => {
                      const a = compare.scanA.areaScores[key] ?? 0;
                      const b = compare.scanB.areaScores[key] ?? 0;
                      const d = compare.deltas[key] ?? 0;
                      return (
                        <div key={key} className="flex items-center gap-3 text-sm">
                          <div className="w-28 font-medium">{label}</div>
                          <div className="w-14 tabular-nums text-right">{a.toFixed(2)}</div>
                          <div className="text-muted-foreground">→</div>
                          <div className="w-14 tabular-nums">{b.toFixed(2)}</div>
                          <div className="ml-auto">{deltaBadge(d)}</div>
                        </div>
                      );
                    })}
                  </CardContent>
                </Card>

                {/* Resolved findings */}
                <Card>
                  <CardHeader className="pb-2">
                    <CardTitle className="text-sm flex items-center gap-2">
                      <CheckCircle className="h-4 w-4 text-green-500" />
                      Resolved ({compare.resolvedFindings.length})
                    </CardTitle>
                  </CardHeader>
                  <CardContent>
                    {compare.resolvedFindings.length === 0 ? (
                      <p className="text-sm text-muted-foreground">No findings resolved between these scans.</p>
                    ) : (
                      <ul className="space-y-1 text-sm max-h-60 overflow-y-auto">
                        {compare.resolvedFindings.slice(0, 50).map((f, i) => (
                          <li key={i} className="flex items-start gap-2">
                            <CheckCircle className="h-3.5 w-3.5 text-green-500 mt-0.5 shrink-0" />
                            <div>
                              <span className="text-xs text-muted-foreground">{f.area}/{f.service}</span>{' '}
                              <span className="font-medium">{f.feature}</span>
                            </div>
                          </li>
                        ))}
                      </ul>
                    )}
                  </CardContent>
                </Card>

                {/* New findings */}
                <Card>
                  <CardHeader className="pb-2">
                    <CardTitle className="text-sm flex items-center gap-2">
                      <AlertTriangle className="h-4 w-4 text-amber-500" />
                      New / Regressed ({compare.newFindings.length})
                    </CardTitle>
                  </CardHeader>
                  <CardContent>
                    {compare.newFindings.length === 0 ? (
                      <p className="text-sm text-muted-foreground">No new findings.</p>
                    ) : (
                      <ul className="space-y-1 text-sm max-h-60 overflow-y-auto">
                        {compare.newFindings.slice(0, 50).map((f, i) => (
                          <li key={i} className="flex items-start gap-2">
                            <AlertTriangle className="h-3.5 w-3.5 text-amber-500 mt-0.5 shrink-0" />
                            <div>
                              <span className="text-xs text-muted-foreground">{f.area}/{f.service}</span>{' '}
                              <span className="font-medium">{f.feature}</span>{' '}
                              <Badge variant="secondary" className="text-xs">{f.status}</Badge>
                            </div>
                          </li>
                        ))}
                      </ul>
                    )}
                  </CardContent>
                </Card>

                <div className="text-xs text-muted-foreground text-right">
                  {compare.unchangedCount} findings unchanged.
                </div>
              </>
            )}
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}

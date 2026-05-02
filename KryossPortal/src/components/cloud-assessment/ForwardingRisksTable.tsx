import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Forward } from 'lucide-react';
import type { MailboxRiskData } from '@/api/cloudAssessment';

const RISK_LABELS: Record<string, string> = {
  external_forward: 'External forward',
  internal_forward: 'Internal forward',
  stealth_forward: 'Stealth (forward + delete)',
};

function severityBadge(sev: string | null) {
  if (sev === 'high') return <Badge variant="secondary" className="bg-red-100 text-red-800">High</Badge>;
  if (sev === 'medium') return <Badge variant="secondary" className="bg-amber-100 text-amber-800">Medium</Badge>;
  return <Badge variant="secondary">{sev ?? '—'}</Badge>;
}

export function ForwardingRisksTable({ risks }: { risks: MailboxRiskData[] }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-lg">
          <Forward className="h-5 w-5" />
          Mailbox Forwarding Rules
          {risks.length > 0 && (
            <Badge variant="secondary" className="ml-2">{risks.length}</Badge>
          )}
        </CardTitle>
      </CardHeader>
      <CardContent className="p-0 overflow-x-auto">
        {risks.length === 0 ? (
          <p className="py-6 text-center text-sm text-muted-foreground">
            No forwarding rules detected in sampled mailboxes.
          </p>
        ) : (
          <>
          {/* Mobile cards */}
          <div className="space-y-3 p-4 sm:hidden">
            {risks.map((r, i) => (
              <div key={`${r.userPrincipalName}-${r.riskType}-${r.forwardTarget}-${i}`} className="rounded-lg border p-4 space-y-2">
                <div className="flex items-center justify-between gap-2">
                  <span className="font-medium text-sm truncate">{r.displayName ?? r.userPrincipalName}</span>
                  {severityBadge(r.severity)}
                </div>
                <p className="text-xs text-muted-foreground">{RISK_LABELS[r.riskType] ?? r.riskType}</p>
                {r.forwardTarget && (
                  <p className="text-xs text-muted-foreground truncate font-mono">{r.forwardTarget}</p>
                )}
              </div>
            ))}
          </div>
          {/* Desktop table */}
          <div className="hidden sm:block overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>User</TableHead>
                <TableHead>Risk</TableHead>
                <TableHead>Forward target</TableHead>
                <TableHead className="hidden lg:table-cell">Rule</TableHead>
                <TableHead>Severity</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {risks.map((r, i) => (
                <TableRow key={`${r.userPrincipalName}-${r.riskType}-${r.forwardTarget}-${i}`}>
                  <TableCell className="text-sm">
                    <div className="font-medium">{r.displayName ?? r.userPrincipalName}</div>
                    {r.displayName && (
                      <div className="text-xs text-muted-foreground">{r.userPrincipalName}</div>
                    )}
                  </TableCell>
                  <TableCell className="text-sm">{RISK_LABELS[r.riskType] ?? r.riskType}</TableCell>
                  <TableCell className="text-sm font-mono">{r.forwardTarget ?? '—'}</TableCell>
                  <TableCell className="text-sm text-muted-foreground hidden lg:table-cell">{r.riskDetail ?? '—'}</TableCell>
                  <TableCell>{severityBadge(r.severity)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          </div>
          </>
        )}
      </CardContent>
    </Card>
  );
}

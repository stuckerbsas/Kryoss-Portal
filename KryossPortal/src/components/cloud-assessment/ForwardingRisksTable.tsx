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
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>User</TableHead>
                <TableHead>Risk</TableHead>
                <TableHead>Forward target</TableHead>
                <TableHead>Rule</TableHead>
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
                  <TableCell className="text-sm text-muted-foreground">{r.riskDetail ?? '—'}</TableCell>
                  <TableCell>{severityBadge(r.severity)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </CardContent>
    </Card>
  );
}

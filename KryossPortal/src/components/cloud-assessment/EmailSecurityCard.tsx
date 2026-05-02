import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Shield } from 'lucide-react';
import type { MailDomainData } from '@/api/cloudAssessment';

function spfBadge(d: MailDomainData) {
  if (!d.spfValid) return <Badge variant="secondary" className="bg-red-100 text-red-800">Missing</Badge>;
  const m = d.spfMechanism ?? '';
  if (m === '-all') return <Badge variant="secondary" className="bg-green-100 text-green-800">Hard fail</Badge>;
  if (m === '~all') return <Badge variant="secondary" className="bg-amber-100 text-amber-800">Soft fail</Badge>;
  return <Badge variant="secondary" className="bg-red-100 text-red-800">{m || 'None'}</Badge>;
}

function dkimBadge(d: MailDomainData) {
  const s1 = d.dkimS1Present === true;
  const s2 = d.dkimS2Present === true;
  if (s1 && s2) return <Badge variant="secondary" className="bg-green-100 text-green-800">Full</Badge>;
  if (s1 || s2) return <Badge variant="secondary" className="bg-amber-100 text-amber-800">Partial</Badge>;
  return <Badge variant="secondary" className="bg-red-100 text-red-800">Missing</Badge>;
}

function dmarcBadge(d: MailDomainData) {
  if (!d.dmarcValid) return <Badge variant="secondary" className="bg-red-100 text-red-800">Missing</Badge>;
  const p = d.dmarcPolicy ?? '';
  if (p === 'reject') return <Badge variant="secondary" className="bg-green-100 text-green-800">p=reject</Badge>;
  if (p === 'quarantine') return <Badge variant="secondary" className="bg-green-100 text-green-800">p=quarantine</Badge>;
  if (p === 'none') return <Badge variant="secondary" className="bg-amber-100 text-amber-800">Monitor only</Badge>;
  return <Badge variant="secondary">{p}</Badge>;
}

function mtaBadge(d: MailDomainData) {
  const p = d.mtaStsPolicy;
  if (p === 'enforce') return <Badge variant="secondary" className="bg-green-100 text-green-800">Enforce</Badge>;
  if (p === 'testing') return <Badge variant="secondary" className="bg-amber-100 text-amber-800">Testing</Badge>;
  return <Badge variant="secondary" className="bg-gray-100 text-gray-600">Missing</Badge>;
}

function scoreBadge(score: number | null) {
  if (score === null) return <Badge variant="secondary">—</Badge>;
  if (score >= 7) return <Badge variant="secondary" className="bg-green-100 text-green-800">{score.toFixed(1)}/10</Badge>;
  if (score >= 5) return <Badge variant="secondary" className="bg-amber-100 text-amber-800">{score.toFixed(1)}/10</Badge>;
  return <Badge variant="secondary" className="bg-red-100 text-red-800">{score.toFixed(1)}/10</Badge>;
}

export function EmailSecurityCard({ domains }: { domains: MailDomainData[] }) {
  if (domains.length === 0) {
    return (
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-lg">
            <Shield className="h-5 w-5" />
            Email Security (DNS Posture)
          </CardTitle>
        </CardHeader>
        <CardContent className="py-6 text-center text-sm text-muted-foreground">
          No verified mail domains inspected for this scan.
        </CardContent>
      </Card>
    );
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-lg">
          <Shield className="h-5 w-5" />
          Email Security (DNS Posture)
        </CardTitle>
      </CardHeader>
      <CardContent className="p-0">
        {/* Mobile cards */}
        <div className="space-y-3 p-4 sm:hidden">
          {domains.map((d) => (
            <div key={d.domain} className="rounded-lg border p-4 space-y-2">
              <div className="flex items-center justify-between gap-2">
                <span className="font-medium text-sm truncate">
                  {d.domain}
                  {d.isDefault && (
                    <Badge variant="outline" className="ml-2 text-xs">default</Badge>
                  )}
                </span>
                {scoreBadge(d.score)}
              </div>
              <div className="flex flex-wrap gap-2">
                {spfBadge(d)}
                {dkimBadge(d)}
                {dmarcBadge(d)}
              </div>
            </div>
          ))}
        </div>
        {/* Desktop table */}
        <div className="hidden sm:block overflow-x-auto">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Domain</TableHead>
              <TableHead>SPF</TableHead>
              <TableHead>DKIM</TableHead>
              <TableHead>DMARC</TableHead>
              <TableHead className="hidden lg:table-cell">MTA-STS</TableHead>
              <TableHead className="hidden lg:table-cell">BIMI</TableHead>
              <TableHead className="text-right">Score</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {domains.map((d) => (
              <TableRow key={d.domain}>
                <TableCell className="font-medium text-sm">
                  {d.domain}
                  {d.isDefault && (
                    <Badge variant="outline" className="ml-2 text-xs">default</Badge>
                  )}
                </TableCell>
                <TableCell>{spfBadge(d)}</TableCell>
                <TableCell>{dkimBadge(d)}</TableCell>
                <TableCell>{dmarcBadge(d)}</TableCell>
                <TableCell className="hidden lg:table-cell">{mtaBadge(d)}</TableCell>
                <TableCell className="hidden lg:table-cell">
                  {d.bimiPresent ? (
                    <Badge variant="secondary" className="bg-blue-100 text-blue-800">Yes</Badge>
                  ) : (
                    <span className="text-xs text-muted-foreground">—</span>
                  )}
                </TableCell>
                <TableCell className="text-right">{scoreBadge(d.score)}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
        </div>
      </CardContent>
    </Card>
  );
}

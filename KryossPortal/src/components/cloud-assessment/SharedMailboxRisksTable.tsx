import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Inbox } from 'lucide-react';
import type { SharedMailboxData } from '@/api/cloudAssessment';

export function SharedMailboxRisksTable({ mailboxes }: { mailboxes: SharedMailboxData[] }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-lg">
          <Inbox className="h-5 w-5" />
          Shared Mailbox Sign-in
          {mailboxes.length > 0 && (
            <Badge variant="secondary" className="ml-2">{mailboxes.length}</Badge>
          )}
        </CardTitle>
      </CardHeader>
      <CardContent className="p-0 overflow-x-auto">
        {mailboxes.length === 0 ? (
          <p className="py-6 text-center text-sm text-muted-foreground">
            No unlicensed mailboxes detected in sampled users.
          </p>
        ) : (
          <>
            <p className="px-4 pt-2 pb-3 text-xs text-muted-foreground">
              Heuristic: enabled users without assigned licenses that own a mailbox.
              Verify sign-in is blocked via Exchange Online PowerShell.
            </p>
            {/* Mobile cards */}
            <div className="space-y-3 p-4 sm:hidden">
              {mailboxes.map((m) => (
                <div key={m.mailboxUpn} className="rounded-lg border p-4 space-y-2">
                  <div className="flex items-center justify-between gap-2">
                    <span className="font-medium text-sm truncate">{m.displayName ?? m.mailboxUpn}</span>
                    {m.hasPasswordEnabled ? (
                      <Badge variant="secondary" className="bg-red-100 text-red-800">Enabled</Badge>
                    ) : (
                      <Badge variant="secondary" className="bg-gray-100 text-gray-600">Unknown</Badge>
                    )}
                  </div>
                  <p className="text-xs text-muted-foreground truncate font-mono">{m.mailboxUpn}</p>
                  {m.lastActivity && (
                    <p className="text-xs text-muted-foreground">Last activity: {new Date(m.lastActivity).toLocaleDateString()}</p>
                  )}
                </div>
              ))}
            </div>
            {/* Desktop table */}
            <div className="hidden sm:block overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Mailbox</TableHead>
                  <TableHead>Display name</TableHead>
                  <TableHead>Sign-in</TableHead>
                  <TableHead>Last activity</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {mailboxes.map((m) => (
                  <TableRow key={m.mailboxUpn}>
                    <TableCell className="text-sm font-mono">{m.mailboxUpn}</TableCell>
                    <TableCell className="text-sm">{m.displayName ?? '—'}</TableCell>
                    <TableCell>
                      {m.hasPasswordEnabled ? (
                        <Badge variant="secondary" className="bg-red-100 text-red-800">Enabled</Badge>
                      ) : (
                        <Badge variant="secondary" className="bg-gray-100 text-gray-600">Unknown</Badge>
                      )}
                    </TableCell>
                    <TableCell className="text-sm text-muted-foreground">
                      {m.lastActivity ? new Date(m.lastActivity).toLocaleDateString() : '—'}
                    </TableCell>
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

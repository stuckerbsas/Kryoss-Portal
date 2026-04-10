import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { Copy, Key, Plus, Trash2, Check } from 'lucide-react';
import {
  useEnrollmentCodes,
  useCreateEnrollmentCode,
  useDeleteEnrollmentCode,
} from '@/api/enrollment';
import { Can } from '@/components/auth/Can';
import { EmptyState } from '@/components/shared/EmptyState';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';

function formatRelativeTime(dateStr: string): string {
  const diff = Date.now() - new Date(dateStr).getTime();
  const minutes = Math.floor(diff / 60_000);
  if (minutes < 1) return 'just now';
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  return `${days}d ago`;
}

function CodeStatusBadge({
  isUsed,
  isExpired,
  usedBy,
}: {
  isUsed: boolean;
  isExpired: boolean;
  usedBy: string | null;
}) {
  if (isUsed) {
    return (
      <Badge
        variant="secondary"
        className="bg-blue-100 text-blue-800 hover:bg-blue-100"
      >
        Used{usedBy ? ` (${usedBy})` : ''}
      </Badge>
    );
  }
  if (isExpired) {
    return (
      <Badge
        variant="secondary"
        className="bg-red-100 text-red-800 hover:bg-red-100"
      >
        Expired
      </Badge>
    );
  }
  return (
    <Badge
      variant="secondary"
      className="bg-green-100 text-green-800 hover:bg-green-100"
    >
      Active
    </Badge>
  );
}

function CopyButton({ text }: { text: string }) {
  const [copied, setCopied] = useState(false);

  const handleCopy = async () => {
    await navigator.clipboard.writeText(text);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <Button
      variant="ghost"
      size="sm"
      className="h-7 w-7 p-0"
      onClick={handleCopy}
    >
      {copied ? (
        <Check className="h-3.5 w-3.5 text-green-600" />
      ) : (
        <Copy className="h-3.5 w-3.5" />
      )}
      <span className="sr-only">Copy code</span>
    </Button>
  );
}

function GenerateCodeDialog({ orgId }: { orgId: string }) {
  const [open, setOpen] = useState(false);
  const [label, setLabel] = useState('');
  const [expiryDays, setExpiryDays] = useState('7');
  const [generatedCode, setGeneratedCode] = useState<string | null>(null);
  const createCode = useCreateEnrollmentCode();

  const handleGenerate = async () => {
    const result = await createCode.mutateAsync({
      organizationId: orgId,
      label: label || undefined,
      expiryDays: Number(expiryDays),
    });
    setGeneratedCode(result.code);
  };

  const handleClose = () => {
    setOpen(false);
    // Reset state after dialog close animation
    setTimeout(() => {
      setLabel('');
      setExpiryDays('7');
      setGeneratedCode(null);
    }, 200);
  };

  return (
    <Dialog open={open} onOpenChange={(v) => (v ? setOpen(true) : handleClose())}>
      <DialogTrigger asChild>
        <Button size="sm">
          <Plus className="mr-1.5 h-4 w-4" />
          Generate Code
        </Button>
      </DialogTrigger>
      <DialogContent>
        {generatedCode ? (
          <>
            <DialogHeader>
              <DialogTitle>Enrollment Code Generated</DialogTitle>
              <DialogDescription>
                Use this code to enroll a machine into the organization.
              </DialogDescription>
            </DialogHeader>
            <div className="flex items-center justify-center gap-2 py-4">
              <code className="text-2xl font-mono font-bold tracking-widest select-all">
                {generatedCode}
              </code>
              <CopyButton text={generatedCode} />
            </div>
            <div className="rounded-md bg-muted p-4 text-sm space-y-1">
              <p className="font-medium">Installation instructions:</p>
              <ol className="list-decimal list-inside space-y-1 text-muted-foreground">
                <li>Download the agent from your admin</li>
                <li>
                  Run as administrator:{' '}
                  <code className="font-mono text-xs bg-background px-1 py-0.5 rounded">
                    KryossAgent.exe --enroll
                  </code>
                </li>
                <li>Enter the code when prompted</li>
              </ol>
            </div>
            <DialogFooter>
              <Button variant="outline" onClick={handleClose}>
                Close
              </Button>
            </DialogFooter>
          </>
        ) : (
          <>
            <DialogHeader>
              <DialogTitle>Generate Enrollment Code</DialogTitle>
              <DialogDescription>
                Create a one-time enrollment code for a new machine.
              </DialogDescription>
            </DialogHeader>
            <div className="space-y-4 py-2">
              <div className="space-y-2">
                <Label htmlFor="code-label">Label (optional)</Label>
                <Input
                  id="code-label"
                  placeholder="e.g. Front desk PC"
                  value={label}
                  onChange={(e) => setLabel(e.target.value)}
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="code-expiry">Expires in</Label>
                <Select value={expiryDays} onValueChange={setExpiryDays}>
                  <SelectTrigger id="code-expiry">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="7">7 days</SelectItem>
                    <SelectItem value="14">14 days</SelectItem>
                    <SelectItem value="30">30 days</SelectItem>
                  </SelectContent>
                </Select>
              </div>
            </div>
            <DialogFooter>
              <Button
                onClick={handleGenerate}
                disabled={createCode.isPending}
              >
                {createCode.isPending ? 'Generating...' : 'Generate'}
              </Button>
            </DialogFooter>
          </>
        )}
      </DialogContent>
    </Dialog>
  );
}

export function EnrollmentTab() {
  const { orgId } = useParams<{ orgId: string }>();
  const { data: codes, isLoading } = useEnrollmentCodes(orgId);
  const deleteCode = useDeleteEnrollmentCode();

  if (isLoading) {
    return (
      <div className="space-y-3">
        <Skeleton className="h-8 w-40 ml-auto" />
        <Skeleton className="h-48 w-full" />
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {/* Header with generate button */}
      <div className="flex items-center justify-between">
        <h2 className="text-lg font-semibold">Enrollment Codes</h2>
        <Can permission="enrollment:create">
          {orgId && <GenerateCodeDialog orgId={orgId} />}
        </Can>
      </div>

      {/* Table or empty state */}
      {!codes || codes.length === 0 ? (
        <EmptyState
          icon={<Key className="h-12 w-12" />}
          title="No enrollment codes"
          description="Generate an enrollment code to start adding machines to this organization."
        />
      ) : (
        <div className="rounded-md border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Code</TableHead>
                <TableHead>Label</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>Created</TableHead>
                <TableHead className="w-[60px]" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {codes.map((ec) => (
                <TableRow key={ec.id}>
                  <TableCell>
                    <div className="flex items-center gap-1">
                      <code className="font-mono text-sm truncate max-w-[140px]">
                        {ec.code}
                      </code>
                      <CopyButton text={ec.code} />
                    </div>
                  </TableCell>
                  <TableCell className="text-muted-foreground">
                    {ec.label ?? '--'}
                  </TableCell>
                  <TableCell>
                    <CodeStatusBadge
                      isUsed={ec.isUsed}
                      isExpired={ec.isExpired}
                      usedBy={ec.usedBy}
                    />
                  </TableCell>
                  <TableCell className="text-muted-foreground text-sm">
                    {formatRelativeTime(ec.createdAt)}
                  </TableCell>
                  <TableCell>
                    {!ec.isUsed && (
                      <Can permission="enrollment:delete">
                        <Button
                          variant="ghost"
                          size="sm"
                          className="h-7 w-7 p-0 text-destructive hover:text-destructive"
                          disabled={deleteCode.isPending}
                          onClick={() =>
                            orgId &&
                            deleteCode.mutate({
                              id: ec.id,
                              organizationId: orgId,
                            })
                          }
                        >
                          <Trash2 className="h-3.5 w-3.5" />
                          <span className="sr-only">Delete code</span>
                        </Button>
                      </Can>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </div>
  );
}

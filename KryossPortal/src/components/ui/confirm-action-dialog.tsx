import { useState } from 'react';
import { toast } from 'sonner';
import { ShieldCheck } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Dialog, DialogContent, DialogDescription, DialogFooter,
  DialogHeader, DialogTitle,
} from '@/components/ui/dialog';
import { msalInstance } from '@/auth/msalInstance';
import { loginRequest } from '@/auth/msalConfig';

interface ConfirmActionDialogProps {
  open: boolean;
  onClose: () => void;
  onConfirm: (reason: string) => void;
  title: string;
  description: string;
  destructive?: boolean;
  confirmLabel?: string;
  requireReason?: boolean;
  requireFreshAuth?: boolean;
}

export function ConfirmActionDialog({
  open, onClose, onConfirm, title, description,
  destructive = false, confirmLabel = 'Confirm', requireReason = true,
  requireFreshAuth = false,
}: ConfirmActionDialogProps) {
  const [reason, setReason] = useState('');
  const [authenticating, setAuthenticating] = useState(false);

  const canConfirm = !requireReason || reason.trim().length >= 3;

  async function handleConfirm() {
    if (!canConfirm) return;

    if (requireFreshAuth) {
      try {
        setAuthenticating(true);
        await msalInstance.acquireTokenPopup({ ...loginRequest, prompt: 'login' });
      } catch {
        toast.error('Authentication cancelled');
        setAuthenticating(false);
        return;
      }
      setAuthenticating(false);
    }

    onConfirm(reason.trim());
    setReason('');
  }

  function handleClose() {
    if (authenticating) return;
    setReason('');
    onClose();
  }

  return (
    <Dialog open={open} onOpenChange={handleClose}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>{description}</DialogDescription>
        </DialogHeader>
        {requireReason && (
          <div className="space-y-2">
            <Label htmlFor="reason">Reason</Label>
            <Input
              id="reason"
              placeholder="Why are you performing this action?"
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && canConfirm && handleConfirm()}
            />
          </div>
        )}
        {requireFreshAuth && (
          <div className="flex items-center gap-2 text-sm text-muted-foreground bg-muted rounded-md p-2">
            <ShieldCheck className="h-4 w-4" />
            <span>This action requires re-authentication</span>
          </div>
        )}
        <DialogFooter>
          <Button variant="outline" onClick={handleClose} disabled={authenticating}>Cancel</Button>
          <Button
            variant={destructive ? 'destructive' : 'default'}
            onClick={handleConfirm}
            disabled={!canConfirm || authenticating}
          >
            {authenticating ? 'Authenticating...' : confirmLabel}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

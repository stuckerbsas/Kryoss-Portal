import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { useDeleteOrganization } from '@/api/organizations';
import { toast } from 'sonner';
import type { Organization } from '@/types';

interface DeleteOrgDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  org: Organization | null;
}

export function DeleteOrgDialog({
  open,
  onOpenChange,
  org,
}: DeleteOrgDialogProps) {
  const deleteMut = useDeleteOrganization();

  const handleDelete = async () => {
    if (!org) return;
    try {
      await deleteMut.mutateAsync(org.id);
      toast.success('Moved to Recycle Bin');
      onOpenChange(false);
    } catch (err: unknown) {
      const message =
        err instanceof Error ? err.message : 'Failed to delete';
      toast.error(message);
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Move to Recycle Bin</DialogTitle>
          <DialogDescription>
            This will move <strong>{org?.name}</strong> and all its data (
            {org?.machineCount ?? 0} machines, {org?.enrollmentCodeCount ?? 0}{' '}
            enrollment codes) to the Recycle Bin. This can be undone.
          </DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button
            variant="destructive"
            onClick={handleDelete}
            disabled={deleteMut.isPending}
          >
            {deleteMut.isPending ? 'Deleting...' : 'Move to Recycle Bin'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

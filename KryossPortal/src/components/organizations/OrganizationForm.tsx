import { useState, useEffect } from 'react';
import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
  SheetDescription,
} from '@/components/ui/sheet';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Separator } from '@/components/ui/separator';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  useCreateOrganization,
  useUpdateOrganization,
} from '@/api/organizations';
import { usePermissions } from '@/hooks/usePermissions';
import { toast } from 'sonner';
import type { Organization } from '@/types';

interface OrganizationFormProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  org?: Organization | null;
}

export function OrganizationForm({
  open,
  onOpenChange,
  org,
}: OrganizationFormProps) {
  const isEdit = !!org;
  const { isSuperAdmin } = usePermissions();
  const createMut = useCreateOrganization();
  const updateMut = useUpdateOrganization();

  const [name, setName] = useState('');
  const [legalName, setLegalName] = useState('');
  const [taxId, setTaxId] = useState('');
  const [status, setStatus] = useState('prospect');
  const [brandId, setBrandId] = useState<number>(1);

  useEffect(() => {
    if (org) {
      setName(org.name);
      setLegalName(org.legalName ?? '');
      setTaxId(org.taxId ?? '');
      setStatus(org.status);
      setBrandId(org.brandId);
    } else {
      setName('');
      setLegalName('');
      setTaxId('');
      setStatus('prospect');
      setBrandId(1);
    }
  }, [org, open]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!name.trim()) return;

    const data = {
      name: name.trim(),
      legalName: legalName.trim() || undefined,
      taxId: taxId.trim() || undefined,
      ...(isSuperAdmin ? { status, brandId } : {}),
    };

    try {
      if (isEdit && org) {
        await updateMut.mutateAsync({ id: org.id, ...data });
        toast.success('Organization updated');
      } else {
        await createMut.mutateAsync(data);
        toast.success('Organization created');
      }
      onOpenChange(false);
    } catch (err: unknown) {
      const message =
        err instanceof Error ? err.message : 'Failed to save organization';
      toast.error(message);
    }
  };

  const isPending = createMut.isPending || updateMut.isPending;

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent className="overflow-y-auto">
        <SheetHeader>
          <SheetTitle>
            {isEdit ? 'Edit Organization' : 'New Organization'}
          </SheetTitle>
          <SheetDescription>
            {isEdit
              ? 'Update the organization details below.'
              : 'Fill in the details to create a new client organization.'}
          </SheetDescription>
        </SheetHeader>

        <Separator className="my-4" />

        <form onSubmit={handleSubmit} className="space-y-5 px-4 pb-4">
          <div className="space-y-2">
            <Label htmlFor="name">Name *</Label>
            <Input
              id="name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="Client company name"
              required
              autoFocus
            />
          </div>

          <div className="space-y-2">
            <Label htmlFor="legalName">Legal Name</Label>
            <Input
              id="legalName"
              value={legalName}
              onChange={(e) => setLegalName(e.target.value)}
              placeholder="Full legal entity name"
            />
          </div>

          <div className="space-y-2">
            <Label htmlFor="taxId">Tax ID</Label>
            <Input
              id="taxId"
              value={taxId}
              onChange={(e) => setTaxId(e.target.value)}
              placeholder="30-12345678-9"
            />
          </div>

          {isSuperAdmin && (
            <>
              <Separator className="my-2" />
              <p className="text-xs font-medium text-muted-foreground uppercase tracking-wider">
                Admin settings
              </p>

              <div className="space-y-2">
                <Label>Status</Label>
                <Select value={status} onValueChange={setStatus}>
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="prospect">Prospect</SelectItem>
                    <SelectItem value="current">Active</SelectItem>
                    <SelectItem value="disabled">Disabled</SelectItem>
                  </SelectContent>
                </Select>
              </div>

              <div className="space-y-2">
                <Label>Brand</Label>
                <Select
                  value={String(brandId)}
                  onValueChange={(v) => setBrandId(Number(v))}
                >
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="1">TeamLogic IT</SelectItem>
                    <SelectItem value="2">Kryoss</SelectItem>
                    <SelectItem value="3">Geminis Computer</SelectItem>
                  </SelectContent>
                </Select>
              </div>
            </>
          )}

          <Separator className="my-2" />

          <div className="flex gap-3 pt-2">
            <Button type="submit" disabled={isPending} className="flex-1">
              {isPending ? 'Saving...' : isEdit ? 'Update' : 'Create Organization'}
            </Button>
            <Button
              type="button"
              variant="outline"
              onClick={() => onOpenChange(false)}
            >
              Cancel
            </Button>
          </div>
        </form>
      </SheetContent>
    </Sheet>
  );
}

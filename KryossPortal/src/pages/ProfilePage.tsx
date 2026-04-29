import { useState, useEffect } from 'react';
import { User, Phone, Briefcase, Mail, Shield, Building2, Loader2 } from 'lucide-react';
import { toast } from 'sonner';
import { useMe, useUpdateProfile } from '@/api/me';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { Badge } from '@/components/ui/badge';

export function ProfilePage() {
  const { data: me, isLoading } = useMe();
  const updateProfile = useUpdateProfile();

  const [displayName, setDisplayName] = useState('');
  const [phone, setPhone] = useState('');
  const [jobTitle, setJobTitle] = useState('');
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (me) {
      setDisplayName(me.displayName ?? '');
      setPhone(me.phone ?? '');
      setJobTitle(me.jobTitle ?? '');
      setDirty(false);
    }
  }, [me]);

  const handleChange = (setter: (v: string) => void) => (e: React.ChangeEvent<HTMLInputElement>) => {
    setter(e.target.value);
    setDirty(true);
  };

  const handleSave = async () => {
    await updateProfile.mutateAsync({ displayName, phone, jobTitle });
    setDirty(false);
    toast.success('Profile updated');
  };

  if (isLoading) {
    return (
      <div className="max-w-2xl space-y-6">
        <Skeleton className="h-8 w-48" />
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }

  if (!me) return null;

  return (
    <div className="max-w-2xl space-y-6">
      <h1 className="text-2xl font-bold tracking-tight">Profile</h1>

      {/* Read-only info */}
      <Card>
        <CardHeader>
          <CardTitle className="text-base">Account</CardTitle>
        </CardHeader>
        <CardContent className="space-y-3">
          <InfoRow icon={<Mail className="h-4 w-4" />} label="Email" value={me.email} />
          <InfoRow icon={<Shield className="h-4 w-4" />} label="Role" value={me.role.name} />
          {me.franchise && (
            <InfoRow icon={<Building2 className="h-4 w-4" />} label="Franchise" value={me.franchise.name} />
          )}
          {me.organization && (
            <InfoRow icon={<Building2 className="h-4 w-4" />} label="Organization" value={me.organization.name} />
          )}
          <InfoRow
            icon={<User className="h-4 w-4" />}
            label="Auth"
            value={
              <Badge variant="secondary" className="text-xs">
                {me.authSource === 'entra' ? 'Microsoft Entra ID' : me.authSource}
              </Badge>
            }
          />
        </CardContent>
      </Card>

      {/* Editable fields */}
      <Card>
        <CardHeader>
          <CardTitle className="text-base">Contact Information</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="displayName">Display Name</Label>
            <div className="relative">
              <User className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
              <Input
                id="displayName"
                value={displayName}
                onChange={handleChange(setDisplayName)}
                className="pl-9"
                placeholder="Your full name"
              />
            </div>
          </div>

          <div className="space-y-2">
            <Label htmlFor="phone">Phone</Label>
            <div className="relative">
              <Phone className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
              <Input
                id="phone"
                value={phone}
                onChange={handleChange(setPhone)}
                className="pl-9"
                placeholder="+1 (555) 123-4567"
              />
            </div>
            <p className="text-xs text-muted-foreground">
              Shown in generated reports as contact information
            </p>
          </div>

          <div className="space-y-2">
            <Label htmlFor="jobTitle">Job Title</Label>
            <div className="relative">
              <Briefcase className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
              <Input
                id="jobTitle"
                value={jobTitle}
                onChange={handleChange(setJobTitle)}
                className="pl-9"
                placeholder="IT Manager"
              />
            </div>
            <p className="text-xs text-muted-foreground">
              Appears in report headers alongside your name
            </p>
          </div>

          <div className="flex justify-end pt-2">
            <Button onClick={handleSave} disabled={!dirty || updateProfile.isPending}>
              {updateProfile.isPending && <Loader2 className="mr-1.5 h-4 w-4 animate-spin" />}
              Save Changes
            </Button>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}

function InfoRow({ icon, label, value }: { icon: React.ReactNode; label: string; value: React.ReactNode }) {
  return (
    <div className="flex items-center gap-3 text-sm">
      <span className="text-muted-foreground">{icon}</span>
      <span className="text-muted-foreground w-24">{label}</span>
      <span className="font-medium">{value}</span>
    </div>
  );
}

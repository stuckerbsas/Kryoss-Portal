import { useMe } from '@/api/me';
import { HqLayout } from './HqLayout';
import { Skeleton } from '@/components/ui/skeleton';
import { Button } from '@/components/ui/button';
import { Shield } from 'lucide-react';

export function AppShell() {
  const { data: me, isLoading, isError, error } = useMe();

  if (isLoading) {
    return (
      <div className="h-screen flex flex-col items-center justify-center gap-4">
        <Shield className="h-10 w-10 text-primary animate-pulse" />
        <Skeleton className="h-4 w-48" />
        <p className="text-sm text-muted-foreground">Loading portal...</p>
      </div>
    );
  }

  if (isError || !me) {
    return (
      <div className="h-screen flex flex-col items-center justify-center gap-4 text-center px-4">
        <Shield className="h-16 w-16 text-muted-foreground" />
        <h1 className="text-xl font-semibold">Authentication Required</h1>
        <p className="text-sm text-muted-foreground max-w-md">
          {(error as any)?.message || 'Please sign in to access the portal.'}
        </p>
        <Button onClick={() => { window.location.href = '/.auth/login/aad?post_login_redirect_uri=/'; }}>
          Sign in with Microsoft
        </Button>
      </div>
    );
  }

  return <HqLayout />;
}

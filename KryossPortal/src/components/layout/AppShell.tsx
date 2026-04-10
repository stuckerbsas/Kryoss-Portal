import { useMe } from '@/api/me';
import { HqLayout } from './HqLayout';
import { Skeleton } from '@/components/ui/skeleton';
import { Button } from '@/components/ui/button';
import { Shield } from 'lucide-react';

const FUNC_BASE = 'https://func-kryoss.azurewebsites.net';
const LOGIN_URL = import.meta.env.DEV
  ? '/.auth/login/aad'
  : `${FUNC_BASE}/.auth/login/aad?post_login_redirect_uri=${encodeURIComponent(window.location.origin + '/')}`;

export function AppShell() {
  const { data: me, isLoading, isError } = useMe();

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
        <img src="/tlit-logo.svg" alt="TeamLogic IT" className="h-12" />
        <h1 className="text-xl font-semibold">Security Portal</h1>
        <p className="text-sm text-muted-foreground max-w-md">
          Sign in with your Microsoft account to access the portal.
        </p>
        <Button onClick={() => { window.location.href = LOGIN_URL; }}>
          Sign in with Microsoft
        </Button>
      </div>
    );
  }

  return <HqLayout />;
}

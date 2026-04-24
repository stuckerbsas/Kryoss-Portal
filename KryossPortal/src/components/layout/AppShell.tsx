import { useIsAuthenticated, useMsal } from '@azure/msal-react';
import { InteractionStatus } from '@azure/msal-browser';
import { useMe } from '@/api/me';
import { HqLayout } from './HqLayout';
import { ClientLayout } from './ClientLayout';
import { Skeleton } from '@/components/ui/skeleton';
import { Button } from '@/components/ui/button';
import { Shield } from 'lucide-react';
import { loginRequest } from '@/auth/msalConfig';

const CLIENT_ROLES = ['client_admin', 'client_viewer'];

export function AppShell() {
  const { instance, inProgress } = useMsal();
  const isAuthenticated = useIsAuthenticated();
  const { data: me, isLoading: meLoading, isError } = useMe();

  // MSAL is initializing or in the middle of a login
  if (inProgress !== InteractionStatus.None) {
    return (
      <div className="h-screen flex flex-col items-center justify-center gap-4">
        <Shield className="h-10 w-10 text-primary animate-pulse" />
        <Skeleton className="h-4 w-48" />
        <p className="text-sm text-muted-foreground">Authenticating...</p>
      </div>
    );
  }

  // Not logged in — show sign in button
  if (!isAuthenticated) {
    return (
      <div className="h-screen flex flex-col items-center justify-center gap-4 text-center px-4">
        <img src="/tlit-logo.svg" alt="TeamLogic IT" className="h-12" />
        <h1 className="text-xl font-semibold">Security Portal</h1>
        <p className="text-sm text-muted-foreground max-w-md">
          Sign in with your Microsoft account to access the portal.
        </p>
        <Button onClick={() => instance.loginRedirect(loginRequest)}>
          Sign in with Microsoft
        </Button>
      </div>
    );
  }

  // Authenticated but loading user profile from backend
  if (meLoading) {
    return (
      <div className="h-screen flex flex-col items-center justify-center gap-4">
        <Shield className="h-10 w-10 text-primary animate-pulse" />
        <Skeleton className="h-4 w-48" />
        <p className="text-sm text-muted-foreground">Loading portal...</p>
      </div>
    );
  }

  // Backend error (user not registered, etc.)
  if (isError || !me) {
    return (
      <div className="h-screen flex flex-col items-center justify-center gap-4 text-center px-4">
        <Shield className="h-16 w-16 text-muted-foreground" />
        <h1 className="text-xl font-semibold">Access Pending</h1>
        <p className="text-sm text-muted-foreground max-w-md">
          Your account is not yet registered in the portal. Please contact your administrator.
        </p>
        <Button variant="outline" onClick={() => { instance.logoutRedirect(); }}>
          Sign out
        </Button>
      </div>
    );
  }

  return CLIENT_ROLES.includes(me.role.code) ? <ClientLayout /> : <HqLayout />;
}

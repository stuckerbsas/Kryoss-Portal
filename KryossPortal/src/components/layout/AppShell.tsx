import { useMe } from '@/api/me';
import { HqLayout } from './HqLayout';
import { Skeleton } from '@/components/ui/skeleton';

// DEV_MOCK: When backend is unavailable, show the shell with mock data.
// Remove this block when SWA Auth is configured.
const DEV_MOCK = import.meta.env.DEV;

export function AppShell() {
  const { data: me, isLoading, isError } = useMe();

  if (isLoading) {
    return (
      <div className="h-screen flex items-center justify-center">
        <Skeleton className="h-8 w-48" />
      </div>
    );
  }

  if (isError || !me) {
    if (DEV_MOCK) {
      // In dev mode without backend, render shell anyway
      return <HqLayout />;
    }
    window.location.href = '/.auth/login/aad';
    return null;
  }

  return <HqLayout />;
}

import { useMe } from '@/api/me';
import { HqLayout } from './HqLayout';
import { Skeleton } from '@/components/ui/skeleton';

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
    window.location.href = '/.auth/login/aad';
    return null;
  }

  return <HqLayout />;
}

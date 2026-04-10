import { Link } from 'react-router-dom';
import { Button } from '@/components/ui/button';
import { ShieldX } from 'lucide-react';

export function ForbiddenPage() {
  return (
    <div className="h-full flex flex-col items-center justify-center gap-4 text-center">
      <ShieldX className="h-16 w-16 text-muted-foreground" />
      <h1 className="text-2xl font-semibold">Access Denied</h1>
      <p className="text-muted-foreground">You don't have permission to view this page.</p>
      <Button asChild variant="outline">
        <Link to="/">Back to Home</Link>
      </Button>
    </div>
  );
}

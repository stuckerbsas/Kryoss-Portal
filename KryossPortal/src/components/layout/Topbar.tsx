import { useMe } from '@/api/me';
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { Button } from '@/components/ui/button';
import { LogOut, User } from 'lucide-react';

export function Topbar() {
  const { data: me } = useMe();

  return (
    <header className="h-14 border-b bg-white flex items-center justify-between px-4">
      <div className="flex items-center gap-2">
        <div className="h-8 w-8 rounded bg-primary flex items-center justify-center text-white font-bold text-sm">K</div>
        <span className="font-semibold text-lg">Kryoss Portal</span>
      </div>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button variant="ghost" size="sm" className="gap-2">
            <User className="h-4 w-4" />
            {me?.email ?? '...'}
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          <DropdownMenuItem disabled className="text-xs text-muted-foreground">
            {me?.role.name}
          </DropdownMenuItem>
          <DropdownMenuItem onClick={() => { window.location.href = '/.auth/logout'; }}>
            <LogOut className="h-4 w-4 mr-2" />
            Logout
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>
    </header>
  );
}

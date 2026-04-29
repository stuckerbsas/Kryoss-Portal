import { useNavigate } from 'react-router-dom';
import { useMe } from '@/api/me';
import { msalInstance } from '@/auth/msalInstance';
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { Button } from '@/components/ui/button';
import { LogOut, ChevronDown, Shield, UserCircle } from 'lucide-react';

export function Topbar() {
  const { data: me } = useMe();
  const navigate = useNavigate();

  return (
    <header className="h-14 border-b bg-white flex items-center justify-between px-6 shadow-sm">
      <div className="flex items-center gap-3">
        <img src="/tlit-logo.svg" alt="TeamLogic IT" className="h-8" />
        <div className="h-6 w-px bg-gray-200" />
        <span className="text-sm font-medium text-muted-foreground">
          Security Portal
        </span>
      </div>

      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button variant="ghost" size="sm" className="gap-2 text-gray-600 hover:text-gray-900">
            <div className="h-7 w-7 rounded-full bg-primary/10 flex items-center justify-center">
              <span className="text-xs font-semibold text-primary">
                {me?.displayName?.charAt(0)?.toUpperCase() ?? '?'}
              </span>
            </div>
            <div className="hidden sm:flex flex-col items-start text-left">
              <span className="text-sm font-medium leading-tight">{me?.displayName ?? '...'}</span>
              <span className="text-[11px] text-muted-foreground leading-tight">{me?.role?.name ?? ''}</span>
            </div>
            <ChevronDown className="h-3 w-3 text-muted-foreground" />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end" className="w-56">
          <div className="px-2 py-2">
            <p className="text-sm font-medium">{me?.displayName}</p>
            <p className="text-xs text-muted-foreground">{me?.email}</p>
          </div>
          <DropdownMenuSeparator />
          <DropdownMenuItem onClick={() => navigate('/profile')} className="text-xs">
            <UserCircle className="h-3 w-3 mr-2" />
            Profile
          </DropdownMenuItem>
          <DropdownMenuItem disabled className="text-xs">
            <Shield className="h-3 w-3 mr-2" />
            {me?.role?.name}
          </DropdownMenuItem>
          {me?.franchise && (
            <DropdownMenuItem disabled className="text-xs">
              {me.franchise.name}
            </DropdownMenuItem>
          )}
          <DropdownMenuSeparator />
          <DropdownMenuItem
            onClick={() => { msalInstance.logoutRedirect({ postLogoutRedirectUri: '/' }); }}
            className="text-red-600 focus:text-red-600"
          >
            <LogOut className="h-4 w-4 mr-2" />
            Sign out
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>
    </header>
  );
}

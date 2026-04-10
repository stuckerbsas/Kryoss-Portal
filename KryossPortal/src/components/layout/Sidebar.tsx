import { Link, useLocation } from 'react-router-dom';
import { usePermissions } from '@/hooks/usePermissions';
import { Building2, Trash2 } from 'lucide-react';
import { cn } from '@/lib/utils';

type NavItem =
  | { label: string; path: string; perm: string; icon: typeof Building2 }
  | { type: 'separator' };

const NAV_ITEMS: NavItem[] = [
  { label: 'Organizations', path: '/organizations', perm: 'organizations:read', icon: Building2 },
  { type: 'separator' },
  { label: 'Recycle Bin', path: '/recycle-bin', perm: 'recycle_bin:read', icon: Trash2 },
];

export function Sidebar() {
  const { has } = usePermissions();
  const location = useLocation();

  return (
    <aside className="hidden lg:flex w-56 border-r bg-white flex-col h-full">
      <nav className="flex-1 p-3 space-y-1">
        {NAV_ITEMS.map((item, i) => {
          if ('type' in item) return <hr key={i} className="my-2 border-gray-200" />;
          if (!has(item.perm)) return null;
          const Icon = item.icon;
          const active = location.pathname.startsWith(item.path);
          return (
            <Link
              key={item.path}
              to={item.path}
              className={cn(
                'flex items-center gap-2 px-3 py-2 rounded-md text-sm font-medium transition-colors',
                active ? 'bg-primary/10 text-primary' : 'text-gray-600 hover:bg-gray-100'
              )}
            >
              <Icon className="h-4 w-4" />
              {item.label}
            </Link>
          );
        })}
      </nav>
    </aside>
  );
}

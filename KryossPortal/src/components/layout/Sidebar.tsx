import { Link, useLocation } from 'react-router-dom';
import { usePermissions } from '@/hooks/usePermissions';
import { Building2, Trash2, Shield, Cloud, ScrollText } from 'lucide-react';
import { cn } from '@/lib/utils';

type NavItem =
  | { label: string; path: string; perm: string; icon: typeof Building2 }
  | { type: 'separator' };

const NAV_ITEMS: NavItem[] = [
  { label: 'Organizations', path: '/organizations', perm: 'organizations:read', icon: Building2 },
  { label: 'Cloud Assessment', path: '/organizations?view=cloud-assessment', perm: 'assessment:read', icon: Cloud },
  { label: 'Activity Log', path: '/activity-log', perm: 'admin:read', icon: ScrollText },
  { type: 'separator' },
  { label: 'Recycle Bin', path: '/recycle-bin', perm: 'recycle_bin:read', icon: Trash2 },
];

export function Sidebar() {
  const { has } = usePermissions();
  const location = useLocation();

  return (
    <aside className="hidden lg:flex w-56 bg-sidebar-bg flex-col h-full shrink-0">
      {/* Brand mark in sidebar */}
      <div className="px-5 py-5 border-b border-white/5">
        <div className="flex items-center gap-2">
          <Shield className="h-5 w-5 text-primary" />
          <span className="text-sm font-semibold text-white/70 uppercase tracking-widest">
            Security
          </span>
        </div>
      </div>

      {/* Navigation */}
      <nav className="flex-1 px-3 py-4 space-y-1">
        {NAV_ITEMS.map((item, i) => {
          if ('type' in item) return <hr key={i} className="my-3 border-white/5" />;
          if (!has(item.perm)) return null;
          const Icon = item.icon;
          const active = location.pathname.startsWith(item.path);
          return (
            <Link
              key={item.path}
              to={item.path}
              className={cn(
                'flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium transition-all duration-150',
                active
                  ? 'bg-sidebar-active text-white shadow-md shadow-primary/20'
                  : 'text-sidebar-text hover:bg-sidebar-hover hover:text-white'
              )}
            >
              <Icon className="h-4.5 w-4.5" />
              {item.label}
            </Link>
          );
        })}
      </nav>

      {/* Footer */}
      <div className="px-5 py-4 border-t border-white/5">
        <p className="text-[10px] text-white/20 uppercase tracking-wider">Powered by Kryoss v{__APP_VERSION__}</p>
      </div>
    </aside>
  );
}

import { Link, useLocation } from 'react-router-dom';
import { usePermissions } from '@/hooks/usePermissions';
import { Building2, Trash2, Shield, Cloud, ScrollText, Users, Bug, Package } from 'lucide-react';
import { cn } from '@/lib/utils';

type NavItem =
  | { label: string; path: string; perm: string; icon: typeof Building2 }
  | { type: 'separator' };

const NAV_ITEMS: NavItem[] = [
  { label: 'Organizations', path: '/organizations', perm: 'organizations:read', icon: Building2 },
  { label: 'Cloud Assessment', path: '/organizations?view=cloud-assessment', perm: 'assessment:read', icon: Cloud },
  { label: 'Users', path: '/users', perm: 'admin:read', icon: Users },
  { label: 'Activity Log', path: '/activity-log', perm: 'admin:read', icon: ScrollText },
  { label: 'CVE Database', path: '/cve-database', perm: 'admin:read', icon: Bug },
  { label: 'Software Catalog', path: '/software-catalog', perm: 'admin:read', icon: Package },
  { type: 'separator' },
  { label: 'Recycle Bin', path: '/recycle-bin', perm: 'recycle_bin:read', icon: Trash2 },
];

export function Sidebar() {
  const { has } = usePermissions();
  const location = useLocation();

  return (
    <aside className="hidden lg:flex w-56 bg-sidebar-bg flex-col h-full shrink-0 border-r border-sidebar-border">
      <div className="px-5 py-5 border-b border-sidebar-border">
        <div className="flex items-center gap-2.5">
          <div className="h-7 w-7 rounded-md bg-primary/15 flex items-center justify-center">
            <Shield className="h-4 w-4 text-primary" />
          </div>
          <span className="text-[11px] font-bold text-white/60 uppercase tracking-[0.25em]">
            Security
          </span>
        </div>
      </div>

      <nav className="flex-1 px-3 py-4 space-y-0.5">
        {NAV_ITEMS.map((item, i) => {
          if ('type' in item) return <hr key={i} className="my-3 border-sidebar-border" />;
          if (!has(item.perm)) return null;
          const Icon = item.icon;
          const active = location.pathname.startsWith(item.path);
          return (
            <Link
              key={item.path}
              to={item.path}
              className={cn(
                'flex items-center gap-3 px-3 py-2.5 rounded-lg text-[13px] font-medium transition-all duration-150',
                active
                  ? 'bg-primary text-white shadow-lg shadow-primary/25'
                  : 'text-sidebar-text hover:bg-sidebar-hover hover:text-white'
              )}
            >
              <Icon className="h-4 w-4" />
              {item.label}
            </Link>
          );
        })}
      </nav>

      <div className="px-5 py-4 border-t border-sidebar-border">
        <p className="text-[10px] text-white/15 uppercase tracking-wider font-mono">
          Kryoss v{__APP_VERSION__}
        </p>
      </div>
    </aside>
  );
}

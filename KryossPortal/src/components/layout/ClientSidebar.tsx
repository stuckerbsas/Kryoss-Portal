import { Link, useLocation } from 'react-router-dom';
import { useMe } from '@/api/me';
import { usePermissions } from '@/hooks/usePermissions';
import {
  LayoutDashboard,
  Monitor,
  ShieldCheck,
  FileText,
  Cloud,
  Network,
  Shield,
} from 'lucide-react';
import { cn } from '@/lib/utils';

type NavItem = { label: string; to: string; perm: string; icon: typeof Monitor };

export function ClientSidebar() {
  const { data: me } = useMe();
  const { has } = usePermissions();
  const location = useLocation();
  const orgId = me?.organization?.id;

  if (!orgId) return null;

  const base = `/organizations/${orgId}`;

  const items: NavItem[] = [
    { label: 'Dashboard', to: base, perm: 'assessment:read', icon: LayoutDashboard },
    { label: 'Devices', to: `${base}/devices`, perm: 'machines:read', icon: Monitor },
    { label: 'Security', to: `${base}/security`, perm: 'assessment:read', icon: ShieldCheck },
    { label: 'Network', to: `${base}/network`, perm: 'machines:read', icon: Network },
    { label: 'Cloud', to: `${base}/cloud-assessment`, perm: 'assessment:read', icon: Cloud },
    { label: 'Reports', to: `${base}/reports`, perm: 'reports:read', icon: FileText },
  ];

  return (
    <aside className="hidden lg:flex w-56 bg-sidebar-bg flex-col h-full shrink-0">
      <div className="px-5 py-5 border-b border-white/5">
        <div className="flex items-center gap-2">
          <Shield className="h-5 w-5 text-primary" />
          <span className="text-sm font-semibold text-white/70 uppercase tracking-widest">
            Security
          </span>
        </div>
        <p className="mt-2 text-xs text-white/40 truncate">{me?.organization?.name}</p>
      </div>

      <nav className="flex-1 px-3 py-4 space-y-1">
        {items.map((item) => {
          if (!has(item.perm)) return null;
          const Icon = item.icon;
          const active =
            item.to === base
              ? location.pathname === base || location.pathname === `${base}/`
              : location.pathname.startsWith(item.to);
          return (
            <Link
              key={item.to}
              to={item.to}
              className={cn(
                'flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium transition-all duration-150',
                active
                  ? 'bg-sidebar-active text-white shadow-md shadow-primary/20'
                  : 'text-sidebar-text hover:bg-sidebar-hover hover:text-white',
              )}
            >
              <Icon className="h-4.5 w-4.5" />
              {item.label}
            </Link>
          );
        })}
      </nav>

      <div className="px-5 py-4 border-t border-white/5">
        <p className="text-[10px] text-white/20 uppercase tracking-wider">
          Powered by Kryoss v{__APP_VERSION__}
        </p>
      </div>
    </aside>
  );
}

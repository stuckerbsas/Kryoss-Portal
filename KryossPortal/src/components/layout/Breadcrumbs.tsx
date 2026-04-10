import { Link, useMatches } from 'react-router-dom';
import { ChevronRight } from 'lucide-react';

interface RouteHandle {
  crumb?: (data: unknown, params: Record<string, string>) => string;
}

export function Breadcrumbs() {
  const matches = useMatches();
  const crumbs = matches
    .filter((m) => (m.handle as RouteHandle)?.crumb)
    .map((m) => ({
      label: (m.handle as RouteHandle).crumb!(m.data, m.params as Record<string, string>),
      path: m.pathname,
    }));

  if (crumbs.length === 0) return null;

  return (
    <nav className="flex items-center gap-1 text-sm text-muted-foreground mb-4">
      {crumbs.map((c, i) => (
        <span key={c.path} className="flex items-center gap-1">
          {i > 0 && <ChevronRight className="h-3 w-3" />}
          {i < crumbs.length - 1 ? (
            <Link to={c.path} className="hover:text-primary">{c.label}</Link>
          ) : (
            <span className="text-foreground font-medium">{c.label}</span>
          )}
        </span>
      ))}
    </nav>
  );
}

import { Badge } from '@/components/ui/badge';
import { cn } from '@/lib/utils';

const statusConfig: Record<string, { label: string; className: string }> = {
  prospect: {
    label: 'Prospect',
    className: 'bg-amber-100 text-amber-800 hover:bg-amber-100',
  },
  current: {
    label: 'Active',
    className: 'bg-green-100 text-green-800 hover:bg-green-100',
  },
  disabled: {
    label: 'Disabled',
    className: 'bg-gray-100 text-gray-500 hover:bg-gray-100',
  },
};

export function StatusBadge({ status }: { status: string }) {
  const config = statusConfig[status] ?? {
    label: status,
    className: 'bg-gray-100 text-gray-500',
  };
  return (
    <Badge variant="secondary" className={cn('font-medium', config.className)}>
      {config.label}
    </Badge>
  );
}

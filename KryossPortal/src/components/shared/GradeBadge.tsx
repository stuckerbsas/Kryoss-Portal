import { Badge } from '@/components/ui/badge';
import { cn } from '@/lib/utils';

const gradeColors: Record<string, string> = {
  'A+': 'bg-green-100 text-green-800',
  A: 'bg-green-100 text-green-800',
  'B+': 'bg-lime-100 text-lime-800',
  B: 'bg-lime-100 text-lime-800',
  C: 'bg-amber-100 text-amber-800',
  D: 'bg-red-100 text-red-800',
  F: 'bg-red-200 text-red-900',
};

export function GradeBadge({
  grade,
  score,
}: {
  grade?: string | null;
  score?: number | null;
}) {
  if (!grade)
    return (
      <Badge variant="secondary" className="bg-gray-100 text-gray-400">
        N/A
      </Badge>
    );
  const color = gradeColors[grade] ?? 'bg-gray-100 text-gray-500';
  return (
    <Badge variant="secondary" className={cn('font-mono font-bold', color)}>
      {score != null ? `${score} ${grade}` : grade}
    </Badge>
  );
}

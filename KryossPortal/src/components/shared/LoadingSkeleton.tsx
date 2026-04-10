import { Skeleton } from '@/components/ui/skeleton';

interface LoadingSkeletonProps {
  rows?: number;
  columns?: number;
}

export function LoadingSkeleton({ rows = 5, columns = 4 }: LoadingSkeletonProps) {
  return (
    <div className="space-y-3">
      {Array.from({ length: rows }).map((_, i) => (
        <div key={i} className="flex gap-4">
          {Array.from({ length: columns }).map((_, j) => (
            <Skeleton key={j} className="h-8 flex-1" />
          ))}
        </div>
      ))}
    </div>
  );
}

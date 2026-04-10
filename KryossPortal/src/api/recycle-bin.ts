import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiFetch } from './client';
import type { RecycleBinItem } from '../types';

interface RecycleBinResponse {
  items: RecycleBinItem[];
}

export function useRecycleBin(type?: string) {
  const qs = type ? `?type=${type}` : '';
  return useQuery({
    queryKey: ['recycle-bin', type],
    queryFn: () => apiFetch<RecycleBinResponse>(`/v2/recycle-bin${qs}`),
  });
}

export function useRestoreItem() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ entityType, id }: { entityType: string; id: string }) =>
      apiFetch(`/v2/recycle-bin/${entityType}/${id}/restore`, { method: 'POST' }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['recycle-bin'] });
      qc.invalidateQueries({ queryKey: ['organizations'] });
      qc.invalidateQueries({ queryKey: ['machines'] });
    },
  });
}

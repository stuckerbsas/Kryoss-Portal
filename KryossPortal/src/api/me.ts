import { useQuery } from '@tanstack/react-query';
import { apiFetch } from './client';
import type { MeResponse } from '../types';

export function useMe() {
  return useQuery({
    queryKey: ['me'],
    queryFn: () => apiFetch<MeResponse>('/v2/me'),
    staleTime: Infinity,
    retry: false,
  });
}

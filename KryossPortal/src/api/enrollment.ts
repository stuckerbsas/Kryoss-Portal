import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiFetch } from './client';
import type { EnrollmentCode } from '../types';

export function useEnrollmentCodes(organizationId?: string) {
  return useQuery({
    queryKey: ['enrollment-codes', organizationId],
    queryFn: () =>
      apiFetch<EnrollmentCode[]>(
        `/v2/enrollment-codes?organizationId=${organizationId}`,
      ),
    enabled: !!organizationId,
  });
}

export function useCreateEnrollmentCode() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: {
      organizationId: string;
      label?: string;
      expiryDays?: number;
    }) =>
      apiFetch<{ code: string }>('/v2/enrollment-codes', {
        method: 'POST',
        body: JSON.stringify(data),
      }),
    onSuccess: (_, vars) =>
      qc.invalidateQueries({
        queryKey: ['enrollment-codes', vars.organizationId],
      }),
  });
}

export function useDeleteEnrollmentCode() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      id,
    }: {
      id: number;
      organizationId: string;
    }) => apiFetch(`/v2/enrollment-codes/${id}`, { method: 'DELETE' }),
    onSuccess: (_, vars) =>
      qc.invalidateQueries({
        queryKey: ['enrollment-codes', vars.organizationId],
      }),
  });
}

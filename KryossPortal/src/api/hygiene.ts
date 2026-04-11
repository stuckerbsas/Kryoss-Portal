import { useQuery } from '@tanstack/react-query';
import { apiFetch } from './client';

export interface HygieneFinding {
  name: string;
  objectType: string; // Computer | User
  status: string;     // Stale | Dormant | Disabled | PwdNeverExpires | OldPassword
  daysInactive: number;
  detail: string | null;
}

export interface HygieneScan {
  id: string;
  scannedBy: string;
  scannedAt: string;
  totalMachines: number;
  totalUsers: number;
  staleMachines: number;
  dormantMachines: number;
  staleUsers: number;
  dormantUsers: number;
  disabledUsers: number;
  pwdNeverExpire: number;
  findings: HygieneFinding[];
}

export function useHygiene(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['hygiene', organizationId],
    queryFn: () =>
      apiFetch<HygieneScan>(`/v2/hygiene?organizationId=${organizationId}`),
    enabled: !!organizationId,
  });
}

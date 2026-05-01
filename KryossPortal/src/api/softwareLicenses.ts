import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiFetch } from './client';

export interface LicenseItem {
  machineId: string;
  machineName: string;
  softwareName: string;
  publisher: string | null;
  version: string | null;
  licenseType: string;
  licenseDetail: string | null;
  estimatedCostUsd: number | null;
  costPeriod: string | null;
  confidence: number;
  classificationSource: string;
  matchedRuleId: number | null;
}

export interface LicenseSummary {
  commercial: number;
  free: number;
  openSource: number;
  freemium: number;
  bundled: number;
  likelyCommercial: number;
  unknown: number;
  totalEstimatedMonthlyCost: number;
}

interface LicenseResponse {
  items: LicenseItem[];
  summary: LicenseSummary;
  totalCount: number;
  page: number;
  pageSize: number;
}

export function useSoftwareLicenses(orgId: string | undefined, opts?: { licenseType?: string; page?: number }) {
  const page = opts?.page ?? 1;
  const licenseType = opts?.licenseType;
  return useQuery({
    queryKey: ['software-licenses', orgId, licenseType, page],
    queryFn: () => {
      const params = new URLSearchParams();
      if (licenseType) params.set('license_type', licenseType);
      params.set('page', String(page));
      params.set('pageSize', '50');
      return apiFetch<LicenseResponse>(`/v2/organizations/${orgId}/software-licenses?${params}`);
    },
    enabled: !!orgId,
  });
}

export function useClassifyLicenses(orgId: string | undefined) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () =>
      apiFetch<{ status: string; classifiedCount: number }>(
        `/v2/organizations/${orgId}/software-licenses/classify`,
        { method: 'POST' },
      ),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['software-licenses', orgId] });
    },
  });
}

export function useOverrideLicense(orgId: string | undefined) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: { softwareName: string; publisher?: string; licenseType: string; licenseDetail?: string; estimatedCostUsd?: number; costPeriod?: string; notes?: string }) =>
      apiFetch(`/v2/organizations/${orgId}/software-licenses/override`, {
        method: 'POST',
        body: JSON.stringify(body),
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['software-licenses', orgId] });
    },
  });
}

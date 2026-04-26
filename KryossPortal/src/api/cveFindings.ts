import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiFetch } from './client';

export interface CveFinding {
  id: number;
  machineId: string;
  machineName: string;
  cveId: string;
  softwareName: string;
  installedVersion: string | null;
  fixedVersion: string | null;
  severity: string;
  cvssScore: number | null;
  description: string | null;
  status: string;
  foundAt: string;
}

export interface CveFindingsResponse {
  totalFindings: number;
  affectedMachines: number;
  totalMachines: number;
  uniqueCves: number;
  summary: { severity: string; count: number }[];
  findings: CveFinding[];
}

export interface CveStatsResponse {
  topCves: {
    cveId: string;
    severity: string;
    cvssScore: number | null;
    description: string | null;
    machineCount: number;
  }[];
  topSoftware: {
    softwareName: string;
    cveCount: number;
    machineCount: number;
    maxCvss: number | null;
  }[];
}

export function useCveFindings(organizationId: string | undefined, severity?: string) {
  return useQuery({
    queryKey: ['cve-findings', organizationId, severity],
    queryFn: () => {
      let url = `/v2/cve-findings?organizationId=${organizationId}`;
      if (severity) url += `&severity=${severity}`;
      return apiFetch<CveFindingsResponse>(url);
    },
    enabled: !!organizationId,
  });
}

export function useCveStats(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['cve-stats', organizationId],
    queryFn: () =>
      apiFetch<CveStatsResponse>(
        `/v2/cve-findings/stats?organizationId=${organizationId}`,
      ),
    enabled: !!organizationId,
  });
}

export function useCveRescan(organizationId: string | undefined) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () =>
      apiFetch<{ status: string; findingsCount: number }>(
        '/v2/cve-findings/rescan',
        {
          method: 'POST',
          body: JSON.stringify({ organizationId }),
        },
      ),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['cve-findings', organizationId] });
      qc.invalidateQueries({ queryKey: ['cve-stats', organizationId] });
    },
  });
}

export function useDismissCve(organizationId: string | undefined) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (findingId: number) =>
      apiFetch(`/v2/cve-findings/${findingId}/dismiss`, { method: 'PATCH' }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['cve-findings', organizationId] });
      qc.invalidateQueries({ queryKey: ['cve-stats', organizationId] });
    },
  });
}

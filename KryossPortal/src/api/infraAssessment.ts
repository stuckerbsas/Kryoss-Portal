import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiFetch } from './client';

export interface InfraAssessmentSite {
  id: string;
  siteName: string;
  location: string | null;
  siteType: string;
  deviceCount: number;
  userCount: number;
  connectivityType: string | null;
}

export interface InfraAssessmentDevice {
  id: string;
  siteId: string | null;
  hostname: string | null;
  deviceType: string;
  vendor: string | null;
  model: string | null;
  role: string | null;
  ipAddress: string | null;
  os: string | null;
  firmware: string | null;
  serialNumber: string | null;
}

export interface InfraAssessmentConnectivity {
  id: string;
  siteAId: string;
  siteBId: string;
  linkType: string;
  bandwidthMbps: number | null;
  latencyMs: number | null;
  uptimePct: number | null;
  costMonthlyUsd: number | null;
}

export interface InfraAssessmentCapacity {
  id: string;
  deviceId: string | null;
  metricKey: string;
  currentValue: number | null;
  peakValue: number | null;
  threshold: number | null;
  trendDirection: string;
}

export interface InfraAssessmentFinding {
  id: string;
  area: string;
  service: string | null;
  feature: string | null;
  status: string;
  priority: string;
  observation: string | null;
  recommendation: string | null;
  linkText: string | null;
  linkUrl: string | null;
}

export interface InfraAssessmentScan {
  id: string;
  organizationId: string;
  status: string;
  scope: string | null;
  overallHealth: number | null;
  siteCount: number;
  deviceCount: number;
  findingCount: number;
  startedAt: string | null;
  completedAt: string | null;
  createdAt: string;
  sites: InfraAssessmentSite[];
  devices: InfraAssessmentDevice[];
  connectivity: InfraAssessmentConnectivity[];
  capacity: InfraAssessmentCapacity[];
  findings: InfraAssessmentFinding[];
}

export interface InfraAssessmentHistoryItem {
  id: string;
  status: string;
  overallHealth: number | null;
  siteCount: number;
  deviceCount: number;
  findingCount: number;
  startedAt: string | null;
  completedAt: string | null;
  createdAt: string;
}

export function useInfraAssessment(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['infra-assessment', organizationId],
    queryFn: () =>
      apiFetch<{ scan: InfraAssessmentScan | null }>(
        `/v2/infra-assessment?organizationId=${organizationId}`,
      ).then((r) => r.scan),
    enabled: !!organizationId,
  });
}

export function useInfraAssessmentHistory(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['infra-assessment-history', organizationId],
    queryFn: () =>
      apiFetch<InfraAssessmentHistoryItem[]>(
        `/v2/infra-assessment/history?organizationId=${organizationId}`,
      ),
    enabled: !!organizationId,
  });
}

export function useStartInfraAssessmentScan(organizationId: string | undefined) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (scope?: string) =>
      apiFetch<{ scanId: string; status: string }>(
        '/v2/infra-assessment/scan',
        {
          method: 'POST',
          body: JSON.stringify({ organizationId, scope }),
        },
      ),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['infra-assessment', organizationId] });
      qc.invalidateQueries({ queryKey: ['infra-assessment-history', organizationId] });
    },
  });
}

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiFetch } from './client';

// ── Types ──

export type CloudAssessmentAreaKey = 'identity' | 'endpoint' | 'data' | 'productivity';

export interface CloudAssessmentScan {
  id: string;
  status: 'running' | 'completed' | 'partial' | 'failed';
  overallScore: number | null;
  areaScores: Record<string, number> | null;
  verdict: string | null;
  pipelineStatus: Record<string, string> | null;
  tenantId: string | null;
  startedAt: string;
  completedAt: string | null;
  createdAt: string;
  findingsSummary: Array<{
    area: string;
    total: number;
    actionRequired: number;
    warning: number;
    success: number;
    disabled: number;
  }>;
}

export interface CloudAssessmentFinding {
  area: string;
  service: string;
  feature: string;
  status: string;
  priority: string;
  observation: string | null;
  recommendation: string | null;
  linkText: string | null;
  linkUrl: string | null;
}

export interface CloudAssessmentScanDetail extends CloudAssessmentScan {
  findings: CloudAssessmentFinding[];
  metrics: Array<{ area: string; metricKey: string; metricValue: string }>;
  licenses: Array<{
    skuPartNumber: string;
    friendlyName: string | null;
    purchased: number;
    assigned: number;
    available: number;
  }>;
  adoption: Array<{
    area: string;
    serviceName: string;
    licensedCount: number;
    active30d: number;
    adoptionRate: number;
  }>;
  wastedLicenses: Array<{
    userPrincipal: string;
    displayName: string | null;
    sku: string | null;
    lastSignIn: string | null;
    daysInactive: number | null;
    estimatedCostYear: number | null;
  }>;
}

export interface CloudAssessmentHistoryEntry {
  id: string;
  overallScore: number | null;
  areaScores: Record<string, number> | null;
  verdict: string | null;
  status: string;
  createdAt: string;
  completedAt: string | null;
}

export interface CloudAssessmentCompareFinding {
  area: string;
  service: string;
  feature: string;
  status: string;
  priority: string;
  observation: string | null;
  recommendation: string | null;
}

export interface CloudAssessmentCompare {
  scanA: {
    id: string;
    createdAt: string;
    completedAt: string | null;
    areaScores: Record<string, number>;
    overallScore: number | null;
    verdict: string | null;
  };
  scanB: {
    id: string;
    createdAt: string;
    completedAt: string | null;
    areaScores: Record<string, number>;
    overallScore: number | null;
    verdict: string | null;
  };
  deltas: Record<string, number>; // keys: identity, endpoint, data, productivity, overall
  resolvedFindings: CloudAssessmentCompareFinding[];
  newFindings: CloudAssessmentCompareFinding[];
  unchangedCount: number;
}

// ── Hooks ──

export function useCloudAssessment(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['cloud-assessment', organizationId],
    queryFn: () =>
      apiFetch<CloudAssessmentScan | { scanned: false }>(
        `/v2/cloud-assessment?organizationId=${organizationId}`,
      ),
    enabled: !!organizationId,
    refetchInterval: (query) => {
      const data = query.state.data;
      return data && 'status' in data && data.status === 'running' ? 10000 : false;
    },
  });
}

export function useCloudAssessmentDetail(scanId: string | undefined) {
  return useQuery({
    queryKey: ['cloud-assessment-detail', scanId],
    queryFn: () =>
      apiFetch<CloudAssessmentScanDetail>(`/v2/cloud-assessment/${scanId}`),
    enabled: !!scanId,
  });
}

export function useCloudAssessmentHistory(
  organizationId: string | undefined,
  limit: number = 20,
) {
  return useQuery({
    queryKey: ['cloud-assessment-history', organizationId, limit],
    queryFn: () =>
      apiFetch<CloudAssessmentHistoryEntry[]>(
        `/v2/cloud-assessment/history?organizationId=${organizationId}&limit=${limit}`,
      ),
    enabled: !!organizationId,
  });
}

export function useCloudAssessmentCompare(
  scanAId: string | undefined,
  scanBId: string | undefined,
) {
  return useQuery({
    queryKey: ['cloud-assessment-compare', scanAId, scanBId],
    queryFn: () =>
      apiFetch<CloudAssessmentCompare>(
        `/v2/cloud-assessment/compare?scanAId=${scanAId}&scanBId=${scanBId}`,
      ),
    enabled: !!scanAId && !!scanBId && scanAId !== scanBId,
  });
}

export function useCloudAssessmentScan() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      organizationId,
      tenantId,
    }: {
      organizationId: string;
      tenantId?: string;
    }) =>
      apiFetch<{ scanId: string; status: string }>('/v2/cloud-assessment/scan', {
        method: 'POST',
        body: JSON.stringify({ organizationId, ...(tenantId ? { tenantId } : {}) }),
      }),
    onSuccess: (_data, variables) => {
      qc.invalidateQueries({ queryKey: ['cloud-assessment', variables.organizationId] });
      qc.invalidateQueries({
        queryKey: ['cloud-assessment-history', variables.organizationId],
      });
    },
  });
}

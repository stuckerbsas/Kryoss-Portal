import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiFetch } from './client';

// ── Types ──

export interface CopilotReadinessScan {
  id: string;
  status: 'running' | 'completed' | 'partial' | 'failed';
  d1Score: number | null;
  d2Score: number | null;
  d3Score: number | null;
  d4Score: number | null;
  d5Score: number | null;
  d6Score: number | null;
  overallScore: number | null;
  verdict: string | null;
  pipelineStatus: Record<string, string>;
  startedAt: string;
  completedAt: string | null;
  findingsSummary: Record<string, { total: number; actionRequired: number; warning: number; success: number }>;
}

export interface CopilotReadinessFinding {
  id: number;
  service: string;
  feature: string;
  status: string;
  priority: string;
  observation: string | null;
  recommendation: string | null;
  linkText: string | null;
  linkUrl: string | null;
}

export interface CopilotReadinessScanDetail extends CopilotReadinessScan {
  findings: CopilotReadinessFinding[];
  metrics: Record<string, string>;
  sharepointSites: Array<{
    siteUrl: string;
    siteTitle: string;
    totalFiles: number;
    labeledFiles: number;
    oversharedFiles: number;
    riskLevel: string;
  }>;
  externalUsers: Array<{
    userPrincipal: string;
    displayName: string;
    emailDomain: string;
    lastSignIn: string | null;
    riskLevel: string;
    sitesAccessed: number;
  }>;
}

export interface CopilotReadinessScanSummary {
  id: string;
  overallScore: number;
  verdict: string;
  createdAt: string;
}

// ── Hooks ──

export function useCopilotReadiness(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['copilot-readiness', organizationId],
    queryFn: () =>
      apiFetch<CopilotReadinessScan | { scanned: false }>(
        `/v2/copilot-readiness?organizationId=${organizationId}`,
      ),
    enabled: !!organizationId,
    refetchInterval: (query) => {
      const data = query.state.data;
      return data && 'status' in data && data.status === 'running' ? 10000 : false;
    },
  });
}

export function useCopilotReadinessDetail(scanId: string | undefined) {
  return useQuery({
    queryKey: ['copilot-readiness-detail', scanId],
    queryFn: () =>
      apiFetch<CopilotReadinessScanDetail>(`/v2/copilot-readiness/${scanId}`),
    enabled: !!scanId,
  });
}

export function useCopilotReadinessHistory(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['copilot-readiness-history', organizationId],
    queryFn: () =>
      apiFetch<CopilotReadinessScanSummary[]>(
        `/v2/copilot-readiness/history?organizationId=${organizationId}`,
      ),
    enabled: !!organizationId,
  });
}

export function useCopilotReadinessScan() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (organizationId: string) =>
      apiFetch<{ scanId: string; status: string }>('/v2/copilot-readiness/scan', {
        method: 'POST',
        body: JSON.stringify({ organizationId }),
      }),
    onSuccess: (_data, organizationId) => {
      qc.invalidateQueries({ queryKey: ['copilot-readiness', organizationId] });
    },
  });
}

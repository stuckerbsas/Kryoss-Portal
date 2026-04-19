import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiFetch } from './client';

// ── Types ──

export type CloudAssessmentAreaKey = 'identity' | 'endpoint' | 'data' | 'productivity' | 'azure' | 'powerbi';

export interface CopilotReadinessScores {
  d1Labels: number;
  d2Oversharing: number;
  d3External: number;
  d4ConditionalAccess: number;
  d5ZeroTrust: number;
  d6Purview: number;
  overall: number;
  copilotVerdict: string;
}

export interface SharepointSiteData {
  siteUrl: string;
  siteTitle: string | null;
  totalFiles: number;
  labeledFiles: number;
  oversharedFiles: number;
  riskLevel: string | null;
  topLabels: string | null;
}

export interface ExternalUserData {
  userPrincipal: string;
  displayName: string | null;
  emailDomain: string | null;
  lastSignIn: string | null;
  riskLevel: string | null;
  sitesAccessed: number;
  highestPermission: string | null;
}

export interface MailDomainData {
  domain: string;
  isDefault: boolean;
  isVerified: boolean;
  spfRecord: string | null;
  spfValid: boolean | null;
  spfMechanism: string | null;
  spfLookupCount: number | null;
  spfWarnings: string | null;
  dkimS1Present: boolean | null;
  dkimS2Present: boolean | null;
  dkimSelectors: string | null;
  dmarcRecord: string | null;
  dmarcValid: boolean | null;
  dmarcPolicy: string | null;
  dmarcSubdomainPolicy: string | null;
  dmarcPct: number | null;
  dmarcRua: string | null;
  dmarcRuf: string | null;
  mtaStsRecord: string | null;
  mtaStsPolicy: string | null;
  bimiPresent: boolean | null;
  score: number | null;
}

export interface MailboxRiskData {
  userPrincipalName: string;
  displayName: string | null;
  riskType: string;
  riskDetail: string | null;
  forwardTarget: string | null;
  severity: string | null;
}

export interface SharedMailboxData {
  mailboxUpn: string;
  displayName: string | null;
  delegatesCount: number | null;
  fullAccessUsers: string | null;
  sendAsUsers: string | null;
  hasPasswordEnabled: boolean | null;
  lastActivity: string | null;
}

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
  copilotReadiness: CopilotReadinessScores | null;
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
  remediationStatus?: {
    status: FindingRemediationStatus['status'];
    notes: string | null;
    ownerUserId: string | null;
    updatedAt: string;
  } | null;
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
  sharepointSites: SharepointSiteData[];
  externalUsers: ExternalUserData[];
  mailDomains: MailDomainData[];
  mailboxRisks: MailboxRiskData[];
  sharedMailboxes: SharedMailboxData[];
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

// ── Azure consent types (CA-6 Task A3) ──

export interface AzureSubscription {
  id: number;
  subscriptionId: string;
  displayName: string | null;
  state: string | null;
  tenantId: string | null;
  consentState: string | null;
  connectedAt: string | null;
  lastVerifiedAt: string | null;
  errorMessage: string | null;
}

export interface AzureConnectInstructions {
  appId: string;
  servicePrincipalObjectId: string | null;
  azCliCommand: string;
  portalUrl: string;
  spnResolutionNote: string;
}

export interface AzureVerifyResult {
  connected: boolean;
  subscriptions?: Array<{ subscriptionId: string; displayName: string | null; state: string | null }>;
  subscriptionCount?: number;
  missingRoles?: string[];
  message?: string;
  error?: string;
}

// ── Azure consent hooks ──

export function useAzureSubscriptions(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['azure-subscriptions', organizationId],
    queryFn: () =>
      apiFetch<AzureSubscription[]>(
        `/v2/cloud-assessment/azure/subscriptions?organizationId=${organizationId}`,
      ),
    enabled: !!organizationId,
  });
}

export function useAzureConnect() {
  return useMutation({
    mutationFn: ({ organizationId, tenantId }: { organizationId: string; tenantId: string }) =>
      apiFetch<AzureConnectInstructions>('/v2/cloud-assessment/azure/connect', {
        method: 'POST',
        body: JSON.stringify({ organizationId, tenantId }),
      }),
  });
}

export function useAzureVerify() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ organizationId, tenantId }: { organizationId: string; tenantId: string }) =>
      apiFetch<AzureVerifyResult>('/v2/cloud-assessment/azure/verify', {
        method: 'POST',
        body: JSON.stringify({ organizationId, tenantId }),
      }),
    onSuccess: (_data, variables) => {
      qc.invalidateQueries({ queryKey: ['azure-subscriptions', variables.organizationId] });
    },
  });
}

export function useAzureDisconnect() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ organizationId, subscriptionId }: { organizationId: string; subscriptionId: string }) =>
      apiFetch<void>(
        `/v2/cloud-assessment/azure/subscriptions/${subscriptionId}?organizationId=${organizationId}`,
        { method: 'DELETE' },
      ),
    onSuccess: (_data, variables) => {
      qc.invalidateQueries({ queryKey: ['azure-subscriptions', variables.organizationId] });
    },
  });
}

// ── Remediation tracker types (CA-7) ──

export interface FindingRemediationStatus {
  id: number;
  organizationId: string;
  area: string;
  service: string;
  feature: string;
  status: 'open' | 'in_progress' | 'resolved' | 'deferred' | 'acknowledged_regression';
  ownerUserId: string | null;
  notes: string | null;
  updatedAt: string;
  updatedBy: string | null;
}

export interface RemediationSuggestion {
  id: number;
  organizationId: string;
  scanId: string;
  area: string;
  service: string;
  feature: string;
  suggestionType: 'likely_resolved' | 'possible_regression';
  createdAt: string;
}

export interface RemediationStats {
  open: number;
  inProgress: number;
  resolved: number;
  deferred: number;
  total: number;
}

export interface SetFindingStatusRequest {
  organizationId: string;
  area: string;
  service: string;
  feature: string;
  status: FindingRemediationStatus['status'];
  notes?: string;
  ownerUserId?: string;
}

// ── Remediation tracker hooks (CA-7) ──

// GET /v2/cloud-assessment/findings/status?organizationId=X&area=Y&status=Z
export function useFindingStatuses(
  organizationId: string | undefined,
  area?: string,
  statusFilter?: string,
) {
  return useQuery({
    queryKey: ['cloud-assessment-finding-statuses', organizationId, area, statusFilter],
    queryFn: () => {
      const params = new URLSearchParams({ organizationId: organizationId! });
      if (area !== undefined) params.set('area', area);
      if (statusFilter !== undefined) params.set('status', statusFilter);
      return apiFetch<FindingRemediationStatus[]>(
        `/v2/cloud-assessment/findings/status?${params.toString()}`,
      );
    },
    enabled: !!organizationId,
  });
}

// GET /v2/cloud-assessment/suggestions?organizationId=X
export function useRemediationSuggestions(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['cloud-assessment-suggestions', organizationId],
    queryFn: () =>
      apiFetch<RemediationSuggestion[]>(
        `/v2/cloud-assessment/suggestions?organizationId=${organizationId}`,
      ),
    enabled: !!organizationId,
  });
}

// GET /v2/cloud-assessment/remediation/stats?organizationId=X
export function useRemediationStats(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['remediation-stats', organizationId],
    queryFn: () =>
      apiFetch<RemediationStats>(
        `/v2/cloud-assessment/remediation/stats?organizationId=${organizationId}`,
      ),
    enabled: !!organizationId,
  });
}

// PATCH /v2/cloud-assessment/findings/status
export function useSetFindingStatus() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: SetFindingStatusRequest) =>
      apiFetch<FindingRemediationStatus>('/v2/cloud-assessment/findings/status', {
        method: 'PATCH',
        body: JSON.stringify(body),
      }),
    onSuccess: (_data, variables) => {
      qc.invalidateQueries({
        queryKey: ['cloud-assessment-finding-statuses', variables.organizationId],
      });
      qc.invalidateQueries({
        queryKey: ['cloud-assessment-suggestions', variables.organizationId],
      });
      qc.invalidateQueries({
        queryKey: ['remediation-stats', variables.organizationId],
      });
    },
  });
}

// ── Compliance framework types (CA-8) ──

export interface ComplianceFramework {
  id: string;
  code: string;
  name: string;
  description: string | null;
  version: string | null;
  authority: string | null;
  docUrl: string | null;
  controlCount: number;
}

export interface ComplianceFrameworkScore {
  frameworkId: string;
  frameworkCode: string;
  frameworkName: string;
  totalControls: number;
  coveredControls: number;
  passingControls: number;
  failingControls: number;
  unmappedControls: number;
  scorePct: number;
  grade: string | null;
  computedAt: string;
}

export interface ComplianceControlDetail {
  controlCode: string;
  title: string;
  description: string | null;
  category: string | null;
  priority: string | null;
  status: 'passing' | 'failing' | 'unmapped' | 'no_data';
  mappedFindings: Array<{
    area: string;
    service: string;
    feature: string;
    findingStatus: string | null;
    coverage: string;
  }>;
}

export interface ComplianceDrilldown {
  framework: ComplianceFramework;
  controls: ComplianceControlDetail[];
}

// ── Compliance framework hooks (CA-8) ──

export function useComplianceFrameworks() {
  return useQuery({
    queryKey: ['compliance-frameworks'],
    queryFn: () =>
      apiFetch<ComplianceFramework[]>('/v2/cloud-assessment/compliance/frameworks'),
  });
}

export function useComplianceScores(
  organizationId: string | undefined,
  scanId?: string,
) {
  return useQuery({
    queryKey: ['compliance-scores', organizationId, scanId],
    queryFn: () => {
      const params = new URLSearchParams({ organizationId: organizationId! });
      if (scanId) params.set('scanId', scanId);
      return apiFetch<ComplianceFrameworkScore[]>(
        `/v2/cloud-assessment/compliance/scores?${params.toString()}`,
      );
    },
    enabled: !!organizationId,
  });
}

export function useComplianceDrilldown(
  frameworkCode: string | undefined,
  scanId: string | undefined,
) {
  return useQuery({
    queryKey: ['compliance-drilldown', frameworkCode, scanId],
    queryFn: () =>
      apiFetch<ComplianceDrilldown>(
        `/v2/cloud-assessment/compliance/framework/${frameworkCode}?scanId=${scanId}`,
      ),
    enabled: !!frameworkCode && !!scanId,
  });
}

// ── Power BI Governance types (CA-9) ──

export interface PowerBiConnection {
  organizationId: string;
  enabled: boolean;
  connectionState: 'none' | 'pending' | 'connected' | 'failed' | 'disconnected' | 'unavailable';
  lastVerifiedAt: string | null;
  errorMessage: string | null;
  updatedAt: string;
}

export interface PowerBiConnectInstructions {
  appId: string;
  tenantSettingsUrl: string;
  requiredPermissions: string[];
  instructions: string[];
}

// ── Power BI Governance hooks (CA-9) ──

export function usePowerBiConnection(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['powerbi-connection', organizationId],
    queryFn: () =>
      apiFetch<PowerBiConnection>(
        `/v2/cloud-assessment/powerbi/connection?organizationId=${organizationId}`,
      ),
    enabled: !!organizationId,
  });
}

export function usePowerBiConnect() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ organizationId }: { organizationId: string }) =>
      apiFetch<PowerBiConnectInstructions>('/v2/cloud-assessment/powerbi/connect', {
        method: 'POST',
        body: JSON.stringify({ organizationId }),
      }),
    onSuccess: (_data, variables) => {
      qc.invalidateQueries({ queryKey: ['powerbi-connection', variables.organizationId] });
    },
  });
}

export function usePowerBiVerify() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ organizationId }: { organizationId: string }) =>
      apiFetch<{ connected: boolean; error?: string }>('/v2/cloud-assessment/powerbi/verify', {
        method: 'POST',
        body: JSON.stringify({ organizationId }),
      }),
    onSuccess: (_data, variables) => {
      qc.invalidateQueries({ queryKey: ['powerbi-connection', variables.organizationId] });
    },
  });
}

export function usePowerBiDisconnect() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ organizationId }: { organizationId: string }) =>
      apiFetch<void>(
        `/v2/cloud-assessment/powerbi/connection?organizationId=${organizationId}`,
        { method: 'DELETE' },
      ),
    onSuccess: (_data, variables) => {
      qc.invalidateQueries({ queryKey: ['powerbi-connection', variables.organizationId] });
    },
  });
}

// ── Unified cloud connect hook (CA-10) ──

export function useUnifiedCloudConnectUrl(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['cloud-connect-url', organizationId],
    queryFn: () =>
      apiFetch<{ url: string }>(
        `/v2/cloud/connect-url?organizationId=${organizationId}`,
      ),
    enabled: !!organizationId,
  });
}

// ── Benchmark types (CA-11) ──

export interface MetricComparison {
  metricKey: string;
  displayName: string;
  category: 'area' | 'framework' | 'metric' | 'overall' | 'other';
  orgValue: number | null;
  franchiseAvg: number | null;
  franchisePercentile: number | null;
  franchiseSampleSize: number;
  industryBaseline: number | null;
  industryP25: number | null;
  industryP50: number | null;
  industryP75: number | null;
  industryPercentile: number | null;
  globalAvg: number | null;
  globalPercentile: number | null;
  globalSampleSize: number;
  verdict: 'above_peer' | 'at_peer' | 'below_peer' | 'insufficient_data';
}

export interface BenchmarkAvailability {
  franchiseBenchmarkAvailable: boolean;
  franchiseOrgCount: number;
  franchiseThreshold: number;
  industryBenchmarkAvailable: boolean;
  industryCode: string | null;
  globalBenchmarkAvailable: boolean;
  globalOrgCount: number;
  globalThreshold: number;
}

export interface BenchmarkReport {
  metrics: MetricComparison[];
  availability: BenchmarkAvailability;
}

export interface IndustryOption {
  code: string;
  label: string;
  description: string;
}

export interface IndustriesResponse {
  industries: IndustryOption[];
  employeeBands: string[];
}

export interface FranchiseLeaderboardRow {
  organizationId: string;
  organizationName: string;
  overallScore: number | null;
  topArea: string | null;
  topAreaScore: number | null;
  weakestArea: string | null;
  weakestAreaScore: number | null;
  lastScanAt: string | null;
}

export interface FranchiseLeaderboard {
  franchiseId: string;
  orgCount: number;
  available: boolean;
  rows: FranchiseLeaderboardRow[];
}

// ── Benchmark hooks (CA-11) ──

export function useBenchmarkReport(scanId: string | undefined) {
  return useQuery({
    queryKey: ['benchmark-report', scanId],
    queryFn: () =>
      apiFetch<BenchmarkReport>(`/v2/cloud-assessment/benchmarks/${scanId}`),
    enabled: !!scanId,
  });
}

export function useBenchmarkIndustries() {
  return useQuery({
    queryKey: ['benchmark-industries'],
    queryFn: () =>
      apiFetch<IndustriesResponse>('/v2/cloud-assessment/benchmarks/industries'),
    staleTime: 1000 * 60 * 60, // 1 hour — taxonomy is stable
  });
}

export function useFranchiseLeaderboard(franchiseId: string | undefined) {
  return useQuery({
    queryKey: ['benchmark-franchise-summary', franchiseId],
    queryFn: () =>
      apiFetch<FranchiseLeaderboard>(
        `/v2/cloud-assessment/benchmarks/franchise-summary?franchiseId=${franchiseId}`,
      ),
    enabled: !!franchiseId,
  });
}

export function useSetOrgIndustry() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      orgId,
      industryCode,
      industrySubcode,
      employeeBand,
    }: {
      orgId: string;
      industryCode: string;
      industrySubcode?: string;
      employeeBand?: string;
    }) =>
      apiFetch<{
        orgId: string;
        industryCode: string;
        industrySubcode: string | null;
        employeeBand: string | null;
      }>(`/v2/organizations/${orgId}/industry`, {
        method: 'PATCH',
        body: JSON.stringify({
          industryCode,
          ...(industrySubcode ? { industrySubcode } : {}),
          ...(employeeBand ? { employeeBand } : {}),
        }),
      }),
    onSuccess: (_data, variables) => {
      qc.invalidateQueries({ queryKey: ['organizations'] });
      qc.invalidateQueries({ queryKey: ['organization', variables.orgId] });
      qc.invalidateQueries({ queryKey: ['benchmark-report'] });
    },
  });
}

// ── Connection status (CA-12) ──

export interface ConnectionStatus {
  graph: 'connected' | 'not_connected';
  azure: 'connected' | 'not_connected' | 'partial';
  powerBi: 'connected' | 'not_connected';
  azureSubscriptionCount: number;
  connectionPercentage: number;
}

export function useConnectionStatus(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['connection-status', organizationId],
    queryFn: () =>
      apiFetch<ConnectionStatus>(
        `/v2/cloud-assessment/connection-status?organizationId=${organizationId}`,
      ),
    enabled: !!organizationId,
  });
}

// POST /v2/cloud-assessment/suggestions/{id}/dismiss
export function useDismissSuggestion() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ suggestionId, organizationId: _orgId }: { suggestionId: number; organizationId: string }) =>
      apiFetch<void>(`/v2/cloud-assessment/suggestions/${suggestionId}/dismiss`, {
        method: 'POST',
      }),
    onSuccess: (_data, variables) => {
      qc.invalidateQueries({ queryKey: ['cloud-assessment-suggestions', variables.organizationId] });
    },
  });
}

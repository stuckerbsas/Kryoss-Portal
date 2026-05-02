import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiFetch } from './client';

export interface NetworkSite {
  id: string;
  siteName: string;
  publicIp: string | null;
  geoCountry: string | null;
  geoRegion: string | null;
  geoCity: string | null;
  geoLat: number | null;
  geoLon: number | null;
  isp: string | null;
  asn: number | null;
  asnOrg: string | null;
  connType: string | null;
  contractedDownMbps: number | null;
  contractedUpMbps: number | null;
  agentCount: number;
  deviceCount: number;
  ipChanges90d: number;
  avgDownMbps: number | null;
  avgUpMbps: number | null;
  avgLatencyMs: number | null;
  isAutoDerived: boolean;
  updatedAt: string;
  wanScore: number | null;
  avgJitterMs: number | null;
  avgPacketLossPct: number | null;
  hopCount: number | null;
  uniqueIspCount: number;
  monthlyCost: number | null;
  linkType: string | null;
  isRedundant: boolean;
  isPrimary: boolean;
  speedTestMachineId: string | null;
  speedTestMachineName: string | null;
  findingCount: number;
  criticalCount: number;
}

export interface IpHistoryEntry {
  id: number;
  machineId: string;
  machineName: string;
  publicIp: string;
  firstSeen: string;
  lastSeen: string;
  geoCountry: string | null;
  geoCity: string | null;
  isp: string | null;
  asn: number | null;
  connType: string | null;
}

export function useNetworkSites(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['network-sites', organizationId],
    queryFn: () =>
      apiFetch<NetworkSite[]>(
        `/v2/network-sites?organizationId=${organizationId}`,
      ),
    enabled: !!organizationId,
  });
}

export function useIpHistory(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['ip-history', organizationId],
    queryFn: () =>
      apiFetch<IpHistoryEntry[]>(
        `/v2/network-sites/ip-history?organizationId=${organizationId}`,
      ),
    enabled: !!organizationId,
  });
}

export function useRebuildSites(organizationId: string | undefined) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () =>
      apiFetch<{ status: string }>('/v2/network-sites/rebuild', {
        method: 'POST',
        body: JSON.stringify({ organizationId }),
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['network-sites', organizationId] });
    },
  });
}

export interface SpeedHistoryPoint {
  scannedAt: string;
  downloadMbps: number | null;
  uploadMbps: number | null;
  internetLatencyMs: number | null;
  dnsResolutionMs: number | null;
  cloudEndpointAvgMs: number | null;
  jitterMs: number | null;
  packetLossPct: number | null;
  hopCount: number | null;
  machineName: string;
}

export interface SpeedHistoryResponse {
  siteId: string;
  siteName: string;
  contractedDownMbps: number | null;
  contractedUpMbps: number | null;
  speedTestMachineId: string | null;
  speedTestMachineName: string | null;
  history: SpeedHistoryPoint[];
}

export interface SiteMachine {
  id: string;
  hostname: string;
  osName: string | null;
  lastPublicIp: string | null;
  lastPublicIpAt: string | null;
  lastSeenAt: string | null;
  latestDiag: {
    downloadMbps: number | null;
    uploadMbps: number | null;
    internetLatencyMs: number | null;
    dnsResolutionMs: number | null;
    cloudEndpointAvgMs: number | null;
  } | null;
}

export function useSpeedHistory(siteId: string | undefined) {
  return useQuery({
    queryKey: ['speed-history', siteId],
    queryFn: () =>
      apiFetch<SpeedHistoryResponse>(
        `/v2/network-sites/${siteId}/speed-history`,
      ),
    enabled: !!siteId,
  });
}

export function useSiteMachines(siteId: string | undefined) {
  return useQuery({
    queryKey: ['site-machines', siteId],
    queryFn: () =>
      apiFetch<SiteMachine[]>(`/v2/network-sites/${siteId}/machines`),
    enabled: !!siteId,
  });
}

export function useUpdateSite(organizationId: string | undefined) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: {
      siteId: string;
      siteName?: string;
      contractedDownMbps?: number;
      contractedUpMbps?: number;
      monthlyCost?: number;
      linkType?: string;
      isRedundant?: boolean;
      isPrimary?: boolean;
      speedTestMachineId?: string;
    }) =>
      apiFetch(`/v2/network-sites/${data.siteId}`, {
        method: 'PATCH',
        body: JSON.stringify(data),
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['network-sites', organizationId] });
      qc.invalidateQueries({ queryKey: ['wan-health', organizationId] });
    },
  });
}

// WAN Health

export interface WanFinding {
  severity: string;
  category: string;
  title: string;
  detail: string | null;
  metricValue: number | null;
  metricThreshold: number | null;
}

export interface WanSiteSummary {
  id: string;
  siteName: string;
  publicIp: string | null;
  geoCity: string | null;
  isp: string | null;
  wanScore: number | null;
  avgJitterMs: number | null;
  avgPacketLossPct: number | null;
  hopCount: number | null;
  avgDownMbps: number | null;
  avgUpMbps: number | null;
  avgLatencyMs: number | null;
  contractedDownMbps: number | null;
  contractedUpMbps: number | null;
  monthlyCost: number | null;
  linkType: string | null;
  isRedundant: boolean;
  uniqueIspCount: number;
  agentCount: number;
  findings: WanFinding[];
}

export interface WanHealthResponse {
  orgScore: number;
  summary: { severity: string; count: number }[];
  sites: WanSiteSummary[];
}

export function useWanHealth(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['wan-health', organizationId],
    queryFn: () =>
      apiFetch<WanHealthResponse>(
        `/v2/network-sites/wan-health?organizationId=${organizationId}`,
      ),
    enabled: !!organizationId,
  });
}

export interface TracerouteEntry {
  machineId: string;
  machineName: string;
  tracerouteTarget: string | null;
  tracerouteJson: string | null;
  hopCount: number | null;
  jitterMs: number | null;
  packetLossPct: number | null;
  scannedAt: string;
}

export interface LocationHistoryEntry {
  publicIp: string;
  geoCity: string | null;
  geoCountry: string | null;
  geoRegion: string | null;
  isp: string | null;
  asn: number | null;
  connType: string | null;
  firstSeen: string;
  lastSeen: string;
  machineName: string;
}

export function useSiteLocationHistory(siteId: string | undefined) {
  return useQuery({
    queryKey: ['site-location-history', siteId],
    queryFn: () =>
      apiFetch<LocationHistoryEntry[]>(
        `/v2/network-sites/${siteId}/location-history`,
      ),
    enabled: !!siteId,
  });
}

export function useSiteTraceroute(siteId: string | undefined) {
  return useQuery({
    queryKey: ['site-traceroute', siteId],
    queryFn: () =>
      apiFetch<TracerouteEntry[]>(
        `/v2/network-sites/${siteId}/traceroute`,
      ),
    enabled: !!siteId,
  });
}

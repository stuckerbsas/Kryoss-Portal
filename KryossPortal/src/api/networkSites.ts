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
  machineName: string;
}

export interface SpeedHistoryResponse {
  siteId: string;
  siteName: string;
  contractedDownMbps: number | null;
  contractedUpMbps: number | null;
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
    }) =>
      apiFetch(`/v2/network-sites/${data.siteId}`, {
        method: 'PATCH',
        body: JSON.stringify(data),
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['network-sites', organizationId] });
    },
  });
}

import { useQuery } from '@tanstack/react-query';
import { apiFetch } from './client';

export interface LatencyPeer {
  host: string;
  subnet: string;
  reachable: boolean;
  avgMs: number;
  minMs: number;
  maxMs: number;
  jitterMs: number;
  packetLoss: number;
  totalSent: number;
}

export interface Route {
  destination: string;
  mask: string;
  nextHop: string;
  interfaceIndex: number;
  metric: number;
  routeType: string;
  protocol: string;
}

export interface NetworkDiag {
  id: string;
  machineId: string;
  machineName: string;
  runId: string;
  downloadMbps: number;
  uploadMbps: number;
  internetLatencyMs: number;
  gatewayLatencyMs: number | null;
  gatewayIp: string | null;
  routeCount: number;
  vpnDetected: boolean;
  adapterCount: number;
  wifiCount: number;
  vpnAdapterCount: number;
  ethCount: number;
  bandwidthSendMbps: number;
  bandwidthRecvMbps: number;
  dnsResolutionMs: number | null;
  cloudEndpointCount: number | null;
  cloudEndpointAvgMs: number | null;
  triggeredByIpChange: boolean;
  scannedAt: string;
  latencyPeers: LatencyPeer[];
  routes: Route[];
}

export function useNetworkDiagnostics(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['network-diagnostics', organizationId],
    queryFn: () =>
      apiFetch<NetworkDiag[]>(
        `/v2/network-diagnostics?organizationId=${organizationId}`,
      ),
    enabled: !!organizationId,
  });
}

export interface NetworkDiagDetail extends NetworkDiag {
  vpnAdapters: string | null;
}

export function useNetworkDiagDetail(machineId: string | undefined, enabled: boolean) {
  return useQuery({
    queryKey: ['network-diagnostics-detail', machineId],
    queryFn: () =>
      apiFetch<NetworkDiagDetail>(
        `/v2/network-diagnostics/${machineId}`,
      ),
    enabled: !!machineId && enabled,
  });
}

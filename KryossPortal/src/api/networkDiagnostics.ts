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
  routeCount: number;
  vpnDetected: boolean;
  adapterCount: number;
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

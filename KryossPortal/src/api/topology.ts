import { useQuery } from '@tanstack/react-query';
import { apiFetch } from './client';

export interface TopologyNode {
  id: number | string;
  label: string;
  ip: string | null;
  mac: string | null;
  type: string;
  vendor: string | null;
  model: string | null;
  manufacturer: string | null;
  location: string | null;
  interfaceCount: number;
  neighborCount: number;
  cpuLoadPct: number | null;
  memoryTotalMb: number | null;
  memoryUsedMb: number | null;
  isAgent: boolean;
  phantom?: boolean;
  platform?: string | null;
}

export interface TopologyEdge {
  source: number | string;
  target: number | string;
  protocol: string;
  sourcePort: string | null;
  targetPort: string | null;
  trafficInBps: number | null;
  trafficOutBps: number | null;
}

export interface TopologyResponse {
  nodes: TopologyNode[];
  edges: TopologyEdge[];
  stats: {
    totalDevices: number;
    resolvedLinks: number;
    phantomDevices: number;
  };
}

export function useTopology(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['topology', organizationId],
    queryFn: () =>
      apiFetch<TopologyResponse>(
        `/v2/topology?organizationId=${organizationId}`,
      ),
    enabled: !!organizationId,
  });
}

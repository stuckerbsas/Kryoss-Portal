import { useQuery } from '@tanstack/react-query';
import { apiFetch } from './client';

export interface NetworkPortMachine {
  id: string;
  hostname: string;
  state: string;
}

export interface NetworkPort {
  port: number;
  protocol: string;
  service: string | null;
  machineCount: number;
  isRisky: boolean;
  machines: NetworkPortMachine[];
}

export interface NetworkPortsResponse {
  ports: NetworkPort[];
  total: number;
  totalOpenPorts: number;
}

export function useNetworkPorts(
  organizationId: string | undefined,
  state?: string,
) {
  return useQuery({
    queryKey: ['network-ports', organizationId, state],
    queryFn: () => {
      let url = `/v2/network-ports?organizationId=${organizationId}`;
      if (state) url += `&state=${state}`;
      return apiFetch<NetworkPortsResponse>(url);
    },
    enabled: !!organizationId,
  });
}

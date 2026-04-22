import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiFetch } from './client';

export interface SnmpConfigResponse {
  configured: boolean;
  enabled?: boolean;
  version?: number;
  community?: string | null;
  username?: string | null;
  authProtocol?: string | null;
  privProtocol?: string | null;
  targets?: string[] | null;
  updatedAt?: string;
}

export interface SnmpConfigPayload {
  organizationId: string;
  version: number;
  community?: string | null;
  username?: string | null;
  authProtocol?: string | null;
  authPassword?: string | null;
  privProtocol?: string | null;
  privPassword?: string | null;
  targets?: string[] | null;
  enabled: boolean;
}

export interface SnmpDeviceInterface {
  ifIndex: number;
  name: string | null;
  description: string | null;
  ifType: number;
  speedMbps: number | null;
  macAddress: string | null;
  adminStatus: number | null;
  operStatus: number | null;
  inErrors: number;
  outErrors: number;
}

export interface LldpNeighbor {
  localPort: string | null;
  remoteChassisId: string | null;
  remotePortId: string | null;
  remotePortDesc: string | null;
  remoteSysName: string | null;
  remoteSysDesc: string | null;
}

export interface CdpNeighbor {
  localPort: string | null;
  remoteDeviceId: string | null;
  remotePortId: string | null;
  remoteIp: string | null;
  remotePlatform: string | null;
}

export interface SnmpDevice {
  id: string;
  ipAddress: string;
  sysName: string | null;
  sysDescr: string | null;
  uptimeDays: number | null;
  sysLocation: string | null;
  entityModel: string | null;
  entitySerial: string | null;
  entityMfg: string | null;
  entityFirmware: string | null;
  interfaceCount: number;
  lldpNeighborCount: number;
  cdpNeighborCount: number;
  scannedAt: string;
  interfaces: SnmpDeviceInterface[];
  lldpNeighbors: LldpNeighbor[] | null;
  cdpNeighbors: CdpNeighbor[] | null;
}

export function useSnmpConfig(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['snmp-config', organizationId],
    queryFn: () =>
      apiFetch<SnmpConfigResponse>(
        `/v2/snmp-config?organizationId=${organizationId}`,
      ),
    enabled: !!organizationId,
  });
}

export function useSaveSnmpConfig() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: SnmpConfigPayload) =>
      apiFetch<{ saved: boolean }>('/v2/snmp-config', {
        method: 'PUT',
        body: JSON.stringify(payload),
      }),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ['snmp-config', vars.organizationId] });
    },
  });
}

export function useSnmpDevices(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['snmp-devices', organizationId],
    queryFn: () =>
      apiFetch<SnmpDevice[]>(
        `/v2/snmp-devices?organizationId=${organizationId}`,
      ),
    enabled: !!organizationId,
  });
}

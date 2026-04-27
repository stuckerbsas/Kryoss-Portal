import { useQuery } from '@tanstack/react-query';
import { apiFetch } from './client';

export interface ProtocolUsageData {
  machines: number;
  auditConfigured: number;
  ntlm: { outbound: number; inbound: number; safeToDisable: boolean };
  smb1: { events: number; safeToDisable: boolean };
  perMachine: {
    machineId: string;
    hostname: string;
    controls: Record<string, { status: string; finding: string | null; actualValue: string | null }>;
  }[];
}

export function useProtocolUsage(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['protocol-usage', organizationId],
    queryFn: () =>
      apiFetch<ProtocolUsageData>(
        `/v2/protocol-usage?organizationId=${organizationId}`,
      ),
    enabled: !!organizationId,
  });
}

import { useQuery } from '@tanstack/react-query';
import { apiFetch } from './client';

export interface PatchMachineSummary {
  id: string;
  hostname: string;
  osName: string | null;
  updateSource: string | null;
  lastCheckUtc: string | null;
  lastInstallUtc: string | null;
  rebootPending: boolean;
  installedCount30d: number;
  complianceScore: number;
  ninjaManaged: boolean;
  wuServiceStatus: string | null;
}

export interface SourceDistribution {
  source: string;
  count: number;
}

export interface PatchComplianceResponse {
  totalMachines: number;
  reportingMachines: number;
  avgComplianceScore: number;
  rebootPending: number;
  unmanaged: number;
  wuStopped: number;
  neverChecked: number;
  staleCheck: number;
  ninjaManaged: number;
  sourceDistribution: SourceDistribution[];
  machines: PatchMachineSummary[];
}

export interface HotfixEntry {
  hotfixId: string;
  description: string | null;
  installedOn: string | null;
  installedBy: string | null;
}

export function usePatchCompliance(orgId: string | undefined) {
  return useQuery({
    queryKey: ['patch-compliance', orgId],
    queryFn: () =>
      apiFetch<PatchComplianceResponse>(
        `/v2/patch-compliance?organizationId=${orgId}`,
      ),
    enabled: !!orgId,
  });
}

export function useMachinePatches(machineId: string | undefined) {
  return useQuery({
    queryKey: ['machine-patches', machineId],
    queryFn: () =>
      apiFetch<{ patches: HotfixEntry[]; total: number }>(
        `/v2/patch-compliance/${machineId}/patches`,
      ),
    enabled: !!machineId,
  });
}

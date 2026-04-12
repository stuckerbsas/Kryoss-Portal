import { useQuery } from '@tanstack/react-query';
import { apiFetch } from './client';

// ── Hardware Inventory ──

export interface HardwareItem {
  id: string;
  hostname: string;
  osName: string | null;
  osVersion: string | null;
  cpuName: string | null;
  cpuCores: number | null;
  ramGb: number | null;
  diskType: string | null;
  diskSizeGb: number | null;
  diskFreeGb: number | null;
  manufacturer: string | null;
  model: string | null;
  serialNumber: string | null;
  tpmPresent: boolean | null;
  tpmVersion: string | null;
  secureBoot: boolean | null;
  bitlocker: boolean | null;
  ipAddress: string | null;
  macAddress: string | null;
  lastSeenAt: string | null;
  win11Ready: boolean | null; // null = N/A (servers)
  win11Blockers: string[];
  disks: { driveLetter: string; label: string | null; diskType: string | null; totalGb: number | null; freeGb: number | null; fileSystem: string | null; }[];
}

export interface HardwareInventoryResponse {
  total: number;
  workstations?: number;
  servers?: number;
  win11Ready: number;
  win11NotReady: number;
  items: HardwareItem[];
}

export function useHardwareInventory(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['hardware-inventory', organizationId],
    queryFn: () =>
      apiFetch<HardwareInventoryResponse>(
        `/v2/inventory/hardware?organizationId=${organizationId}`,
      ),
    enabled: !!organizationId,
  });
}

// ── Software Inventory ──

export interface SoftwareInventoryItem {
  name: string;
  publisher: string | null;
  version: string | null;
  machineCount: number;
  category: string;
  machines: string[];
}

export interface SoftwareInventoryResponse {
  total: number;
  licensed: number;
  remoteAccess: number;
  suspicious: number;
  items: SoftwareInventoryItem[];
}

export function useSoftwareInventory(organizationId: string | undefined) {
  return useQuery({
    queryKey: ['software-inventory', organizationId],
    queryFn: () =>
      apiFetch<SoftwareInventoryResponse>(
        `/v2/inventory/software?organizationId=${organizationId}`,
      ),
    enabled: !!organizationId,
  });
}

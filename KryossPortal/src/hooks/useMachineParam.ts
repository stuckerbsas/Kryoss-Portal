import { useParams } from 'react-router-dom';
import { useResolvedMachineId } from '@/api/machines';
import { useOrgParam } from './useOrgParam';

/**
 * Returns the resolved machine GUID from the URL param, whether it's a GUID or hostname.
 * Also returns the org context for scoping.
 */
export function useMachineParam() {
  const { orgId, orgSlug } = useOrgParam();
  const { machineId: machineSlug } = useParams<{ machineId: string }>();
  const machineId = useResolvedMachineId(machineSlug, orgId);
  return { machineId, machineSlug, orgId, orgSlug };
}

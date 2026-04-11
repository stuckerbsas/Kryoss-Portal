import { useParams } from 'react-router-dom';
import { useResolvedOrgId } from '@/api/organizations';

/**
 * Returns the resolved org GUID from the URL param, whether it's a GUID or a slug.
 * Also returns the raw URL slug for navigation.
 */
export function useOrgParam() {
  const { orgId: orgSlug } = useParams<{ orgId: string }>();
  const orgId = useResolvedOrgId(orgSlug);
  return { orgId, orgSlug };
}

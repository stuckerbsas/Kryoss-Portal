import { useSearchParams } from 'react-router-dom';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { useHygiene } from '@/api/hygiene';
import { useDcHealth } from '@/api/dcHealth';
import { useOrgParam } from '@/hooks/useOrgParam';
import { ActiveDirectoryTab } from './ActiveDirectoryTab';
import { ThreatsTab } from './ThreatsTab';
import { CveFindingsTab } from './CveFindingsTab';
import { PatchComplianceTab } from './PatchComplianceTab';
import { LocalAdminsTab } from './LocalAdminsTab';

export function SecurityTab() {
  const [params, setParams] = useSearchParams();
  const { orgId } = useOrgParam();
  const { data: hygiene } = useHygiene(orgId);
  const { data: dcHealth } = useDcHealth(orgId);

  const hasAdData = !!(hygiene?.id || dcHealth?.latest);
  const active = params.get('section') ?? (hasAdData ? 'active-directory' : 'local-admins');

  const sections = [
    { value: 'active-directory', label: hasAdData ? 'Active Directory' : 'Active Directory (N/A)' },
    { value: 'local-admins', label: 'Local Admins' },
    { value: 'threats', label: 'Threats' },
    { value: 'cve', label: 'CVE' },
    { value: 'patches', label: 'Patches' },
  ];

  return (
    <Tabs
      value={active}
      onValueChange={(v) => setParams({ section: v }, { replace: true })}
    >
      <TabsList>
        {sections.map((s) => (
          <TabsTrigger
            key={s.value}
            value={s.value}
            className={s.value === 'active-directory' && !hasAdData ? 'opacity-50 pointer-events-none' : undefined}
            disabled={s.value === 'active-directory' && !hasAdData}
          >
            {s.label}
          </TabsTrigger>
        ))}
      </TabsList>

      <TabsContent value="active-directory">
        <ActiveDirectoryTab />
      </TabsContent>
      <TabsContent value="local-admins">
        <LocalAdminsTab />
      </TabsContent>
      <TabsContent value="threats">
        <ThreatsTab />
      </TabsContent>
      <TabsContent value="cve">
        <CveFindingsTab />
      </TabsContent>
      <TabsContent value="patches">
        <PatchComplianceTab />
      </TabsContent>
    </Tabs>
  );
}

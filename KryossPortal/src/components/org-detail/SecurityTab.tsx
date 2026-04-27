import { useSearchParams } from 'react-router-dom';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { ActiveDirectoryTab } from './ActiveDirectoryTab';
import { ThreatsTab } from './ThreatsTab';
import { CveFindingsTab } from './CveFindingsTab';
import { PatchComplianceTab } from './PatchComplianceTab';

const sections = [
  { value: 'active-directory', label: 'Active Directory' },
  { value: 'threats', label: 'Threats' },
  { value: 'cve', label: 'CVE' },
  { value: 'patches', label: 'Patches' },
] as const;

export function SecurityTab() {
  const [params, setParams] = useSearchParams();
  const active = params.get('section') ?? 'active-directory';

  return (
    <Tabs
      value={active}
      onValueChange={(v) => setParams({ section: v }, { replace: true })}
    >
      <TabsList>
        {sections.map((s) => (
          <TabsTrigger key={s.value} value={s.value}>
            {s.label}
          </TabsTrigger>
        ))}
      </TabsList>

      <TabsContent value="active-directory">
        <ActiveDirectoryTab />
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

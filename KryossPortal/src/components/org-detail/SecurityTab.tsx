import { useSearchParams } from 'react-router-dom';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
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

  const handleChange = (v: string) => setParams({ section: v }, { replace: true });

  const sections = [
    { value: 'active-directory', label: hasAdData ? 'Active Directory' : 'Active Directory (N/A)', disabled: !hasAdData },
    { value: 'local-admins', label: 'Local Admins', disabled: false },
    { value: 'threats', label: 'Threats', disabled: false },
    { value: 'cve', label: 'CVE', disabled: false },
    { value: 'patches', label: 'Patches', disabled: false },
  ];

  return (
    <Tabs value={active} onValueChange={handleChange}>
      {/* Mobile: select */}
      <div className="sm:hidden">
        <Select value={active} onValueChange={handleChange}>
          <SelectTrigger className="w-full">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {sections.map((s) => (
              <SelectItem key={s.value} value={s.value} disabled={s.disabled}>
                {s.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      {/* Desktop: tabs */}
      <TabsList className="hidden sm:inline-flex">
        {sections.map((s) => (
          <TabsTrigger
            key={s.value}
            value={s.value}
            className={s.disabled ? 'opacity-50 pointer-events-none' : undefined}
            disabled={s.disabled}
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

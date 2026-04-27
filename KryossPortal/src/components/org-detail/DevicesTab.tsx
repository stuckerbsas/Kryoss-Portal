import { useSearchParams } from 'react-router-dom';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { FleetTab } from './FleetTab';
import { HardwareInventoryTab } from './HardwareInventoryTab';
import { SoftwareInventoryTab } from './SoftwareInventoryTab';

const sections = [
  { value: 'fleet', label: 'Fleet' },
  { value: 'hardware', label: 'Hardware' },
  { value: 'software', label: 'Software' },
] as const;

export function DevicesTab() {
  const [params, setParams] = useSearchParams();
  const active = params.get('section') ?? 'fleet';

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

      <TabsContent value="fleet">
        <FleetTab />
      </TabsContent>
      <TabsContent value="hardware">
        <HardwareInventoryTab />
      </TabsContent>
      <TabsContent value="software">
        <SoftwareInventoryTab />
      </TabsContent>
    </Tabs>
  );
}

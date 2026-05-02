import { useSearchParams } from 'react-router-dom';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
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

  const handleChange = (v: string) => setParams({ section: v }, { replace: true });

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
              <SelectItem key={s.value} value={s.value}>
                {s.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      {/* Desktop: tabs */}
      <TabsList className="hidden sm:inline-flex">
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

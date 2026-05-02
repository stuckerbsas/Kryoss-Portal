import { useSearchParams } from 'react-router-dom';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { NetworkDiagnosticsTab } from './NetworkDiagnosticsTab';
import { SnmpTab } from './SnmpTab';
import { ExternalScanTab } from './ExternalScanTab';
import { NetworkSitesTab } from './NetworkSitesTab';
import { ProtocolUsageTab } from './ProtocolUsageTab';
import { TopologyTab } from './TopologyTab';
import { WanHealthTab } from './WanHealthTab';
import { NetworkPortsTab } from '@/components/network/NetworkPortsTab';

const sections = [
  { value: 'topology', label: 'Topology' },
  { value: 'wan-health', label: 'WAN Health' },
  { value: 'diagnostics', label: 'Diagnostics' },
  { value: 'ports', label: 'Ports' },
  { value: 'sites', label: 'Sites' },
  { value: 'external-scan', label: 'External Scan' },
  { value: 'snmp', label: 'SNMP Devices' },
  { value: 'protocol-usage', label: 'Protocol Usage' },
] as const;

export function NetworkTab() {
  const [params, setParams] = useSearchParams();
  const isMobile = typeof window !== 'undefined' && window.innerWidth < 640;
  const active = params.get('section') ?? (isMobile ? 'wan-health' : 'topology');

  const handleChange = (v: string) => setParams({ section: v }, { replace: true });

  const mobileSections = sections.filter((s) => s.value !== 'topology');

  return (
    <Tabs value={active} onValueChange={handleChange}>
      {/* Mobile: select (no topology) */}
      <div className="sm:hidden">
        <Select value={active} onValueChange={handleChange}>
          <SelectTrigger className="w-full">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {mobileSections.map((s) => (
              <SelectItem key={s.value} value={s.value}>{s.label}</SelectItem>
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

      <TabsContent value="topology">
        <TopologyTab />
      </TabsContent>
      <TabsContent value="wan-health">
        <WanHealthTab />
      </TabsContent>
      <TabsContent value="diagnostics">
        <NetworkDiagnosticsTab />
      </TabsContent>
      <TabsContent value="ports">
        <NetworkPortsTab />
      </TabsContent>
      <TabsContent value="sites">
        <NetworkSitesTab />
      </TabsContent>
      <TabsContent value="external-scan">
        <ExternalScanTab />
      </TabsContent>
      <TabsContent value="snmp">
        <SnmpTab />
      </TabsContent>
      <TabsContent value="protocol-usage">
        <ProtocolUsageTab />
      </TabsContent>
    </Tabs>
  );
}

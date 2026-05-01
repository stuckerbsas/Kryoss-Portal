import { useSearchParams } from 'react-router-dom';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { PortsTab } from './PortsTab';
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
  { value: 'consolidated-ports', label: 'Ports (All)' },
  { value: 'sites', label: 'Sites' },
  { value: 'external-scan', label: 'External Scan' },
  { value: 'snmp', label: 'SNMP Devices' },
  { value: 'protocol-usage', label: 'Protocol Usage' },
] as const;

export function NetworkTab() {
  const [params, setParams] = useSearchParams();
  const active = params.get('section') ?? 'topology';

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
        <PortsTab />
      </TabsContent>
      <TabsContent value="consolidated-ports">
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

import { useEffect, useRef, useState, useCallback } from 'react';
import * as d3 from 'd3';
import { useTopology, type TopologyNode, type TopologyEdge } from '@/api/topology';
import { useOrgParam } from '@/hooks/useOrgParam';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { EmptyState } from '@/components/shared/EmptyState';
import { Network, Router, Server, Monitor, Printer, Wifi, Shield, Phone, HelpCircle, X, Circle } from 'lucide-react';

const DEVICE_COLORS: Record<string, string> = {
  switch: '#3B82F6',
  router: '#10B981',
  access_point: '#8B5CF6',
  server: '#F59E0B',
  printer: '#EC4899',
  firewall: '#EF4444',
  phone: '#06B6D4',
  workstation: '#6366F1',
  subnet: '#6D28D9',
  unknown: '#9CA3AF',
};

const DEVICE_ICONS: Record<string, typeof Network> = {
  switch: Network,
  router: Router,
  access_point: Wifi,
  server: Server,
  printer: Printer,
  firewall: Shield,
  phone: Phone,
  workstation: Monitor,
  subnet: Circle,
  unknown: HelpCircle,
};

// SVG paths from lucide icons (viewBox 0 0 24 24) for D3 inline rendering
const DEVICE_SVG_PATHS: Record<string, string[]> = {
  switch: [
    'M9 2v6', 'M15 2v6', 'M12 16v6',
    'M4 8h16a1 1 0 0 1 1 1v4a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V9a1 1 0 0 1 1-1z',
  ],
  router: [
    'M12 2L2 7l10 5 10-5-10-5z', 'M2 17l10 5 10-5', 'M2 12l10 5 10-5',
  ],
  access_point: [
    'M12 20h.01', 'M2 8.82a15 15 0 0 1 20 0', 'M5 12.86a9 9 0 0 1 14 0', 'M8.5 16.43a5 5 0 0 1 7 0',
  ],
  server: [
    'M2 5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v4a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5z',
    'M2 15a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v4a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2v-4z',
    'M6 7h.01', 'M6 17h.01',
  ],
  printer: [
    'M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2',
    'M6 9V3h12v6', 'M6 18h12v4H6v-4z',
  ],
  firewall: [
    'M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z',
  ],
  phone: [
    'M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6A19.79 19.79 0 0 1 2.12 4.18 2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72 12.84 12.84 0 0 0 .7 2.81 2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45 12.84 12.84 0 0 0 2.81.7A2 2 0 0 1 22 16.92z',
  ],
  workstation: [
    'M20 3H4a1 1 0 0 0-1 1v10a1 1 0 0 0 1 1h16a1 1 0 0 0 1-1V4a1 1 0 0 0-1-1z',
    'M8 21h8', 'M12 15v6',
  ],
  subnet: [
    'M12 22c5.523 0 10-4.477 10-10S17.523 2 12 2 2 6.477 2 12s4.477 10 10 10z',
  ],
  unknown: [
    'M12 22c5.523 0 10-4.477 10-10S17.523 2 12 2 2 6.477 2 12s4.477 10 10 10z',
    'M9.09 9a3 3 0 0 1 5.83 1c0 2-3 3-3 3', 'M12 17h.01',
  ],
};

interface SimNode extends d3.SimulationNodeDatum {
  id: number | string;
  label: string;
  type: string;
  ip: string | null;
  vendor: string | null;
  phantom?: boolean;
  neighborCount: number;
  isAgent: boolean;
  cpuLoadPct: number | null;
  memoryTotalMb: number | null;
  memoryUsedMb: number | null;
  model: string | null;
  location: string | null;
}

interface SimLink extends d3.SimulationLinkDatum<SimNode> {
  protocol: string;
  type?: 'lldp' | 'cdp' | 'inferred';
  sourcePort: string | null;
  targetPort: string | null;
  trafficInBps: number | null;
  trafficOutBps: number | null;
}

function formatBps(bps: number): string {
  if (bps >= 1_000_000_000) return `${(bps / 1_000_000_000).toFixed(1)} Gbps`;
  if (bps >= 1_000_000) return `${(bps / 1_000_000).toFixed(1)} Mbps`;
  if (bps >= 1_000) return `${(bps / 1_000).toFixed(0)} Kbps`;
  return `${bps} bps`;
}

function DeviceDetail({ node, onClose }: { node: SimNode; onClose: () => void }) {
  const Icon = DEVICE_ICONS[node.type] ?? HelpCircle;
  const color = DEVICE_COLORS[node.type] ?? DEVICE_COLORS.unknown;
  return (
    <div className="absolute top-4 right-4 w-72 bg-white dark:bg-gray-900 border rounded-lg shadow-lg p-4 z-50">
      <div className="flex items-center justify-between mb-3">
        <div className="flex items-center gap-2">
          <Icon size={18} style={{ color }} />
          <span className="font-semibold text-sm">{node.label}</span>
        </div>
        <button onClick={onClose} className="text-gray-400 hover:text-gray-600"><X size={16} /></button>
      </div>
      <div className="space-y-1 text-xs text-gray-600 dark:text-gray-400">
        {node.ip && <div><span className="font-medium">IP:</span> {node.ip}</div>}
        {node.vendor && <div><span className="font-medium">Vendor:</span> {node.vendor}</div>}
        {node.model && <div><span className="font-medium">Model:</span> {node.model}</div>}
        {node.location && <div><span className="font-medium">Location:</span> {node.location}</div>}
        <div><span className="font-medium">Type:</span> <Badge variant="secondary" style={{ backgroundColor: color + '20', color }}>{node.type}</Badge></div>
        {node.neighborCount > 0 && <div><span className="font-medium">Neighbors:</span> {node.neighborCount}</div>}
        {node.cpuLoadPct != null && <div><span className="font-medium">CPU:</span> {node.cpuLoadPct}%</div>}
        {node.memoryTotalMb != null && node.memoryUsedMb != null && (
          <div><span className="font-medium">Memory:</span> {Math.round(node.memoryUsedMb / node.memoryTotalMb * 100)}% of {Math.round(node.memoryTotalMb / 1024)} GB</div>
        )}
        {node.phantom && <div className="text-amber-500 font-medium mt-1">Detected via neighbor discovery — not directly scanned</div>}
        {node.isAgent && <div className="text-green-600 font-medium mt-1">Kryoss Agent enrolled</div>}
      </div>
    </div>
  );
}

export function TopologyTab() {
  const { orgId } = useOrgParam();
  const { data, isLoading } = useTopology(orgId);
  const svgRef = useRef<SVGSVGElement | null>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const [selectedNode, setSelectedNode] = useState<SimNode | null>(null);
  const simulationRef = useRef<d3.Simulation<SimNode, SimLink> | null>(null);

  const renderGraph = useCallback((topo: { nodes: TopologyNode[]; edges: TopologyEdge[] }) => {
    const svgEl = svgRef.current;
    if (!svgEl) return;
    const svg = d3.select(svgEl);
    svg.selectAll('*').remove();

    const container = containerRef.current;
    if (!container) return;
    const width = container.clientWidth;
    const height = Math.max(500, container.clientHeight);

    svg.attr('width', width).attr('height', height);

    const simNodes: SimNode[] = topo.nodes.map(n => ({
      ...n,
      x: width / 2 + (Math.random() - 0.5) * 200,
      y: height / 2 + (Math.random() - 0.5) * 200,
    }));

    const nodeMap = new Map(simNodes.map(n => [n.id, n]));
    const simLinks: SimLink[] = topo.edges
      .filter(e => nodeMap.has(e.source) && nodeMap.has(e.target))
      .map(e => ({
        source: nodeMap.get(e.source)!,
        target: nodeMap.get(e.target)!,
        protocol: e.protocol,
        type: e.type,
        sourcePort: e.sourcePort,
        targetPort: e.targetPort,
        trafficInBps: e.trafficInBps,
        trafficOutBps: e.trafficOutBps,
      }));

    const g = svg.append('g');

    // Zoom
    const zoom = d3.zoom<SVGSVGElement, unknown>()
      .scaleExtent([0.2, 4])
      .on('zoom', (event) => g.attr('transform', event.transform));
    svg.call(zoom);

    // Links — width based on traffic
    const linkWidth = (d: SimLink) => {
      const total = (d.trafficInBps ?? 0) + (d.trafficOutBps ?? 0);
      if (total === 0) return 2;
      if (total > 1_000_000_000) return 8;
      if (total > 100_000_000) return 6;
      if (total > 10_000_000) return 4;
      return 3;
    };
    const link = g.append('g')
      .selectAll('line')
      .data(simLinks)
      .join('line')
      .attr('stroke', d => d.type === 'inferred' ? '#A78BFA' : d.protocol === 'lldp' ? '#93C5FD' : '#FCD34D')
      .attr('stroke-width', linkWidth)
      .attr('stroke-opacity', d => d.type === 'inferred' ? 0.45 : 0.6)
      .attr('stroke-dasharray', d => d.type === 'inferred' ? '6,3' : 'none');

    // Link labels — show traffic rate if available, else port names
    const linkLabel = g.append('g')
      .selectAll('text')
      .data(simLinks)
      .join('text')
      .attr('font-size', 9)
      .attr('fill', '#9CA3AF')
      .attr('text-anchor', 'middle')
      .text(d => {
        const total = (d.trafficInBps ?? 0) + (d.trafficOutBps ?? 0);
        if (total > 0) return `↑${formatBps(d.trafficOutBps ?? 0)} ↓${formatBps(d.trafficInBps ?? 0)}`;
        const parts: string[] = [];
        if (d.sourcePort) parts.push(d.sourcePort);
        if (d.targetPort) parts.push(d.targetPort);
        return parts.join(' ↔ ');
      });

    // Nodes — icon groups
    const nodeSize = (d: SimNode) => d.phantom ? 28 : Math.max(32, Math.min(44, 32 + (d.neighborCount ?? 0) * 2));

    const node = g.append('g')
      .selectAll<SVGGElement, SimNode>('g.node')
      .data(simNodes)
      .join('g')
      .attr('class', 'node')
      .attr('cursor', 'pointer')
      .on('click', (_event, d) => setSelectedNode(d))
      .call(d3.drag<SVGGElement, SimNode>()
        .on('start', (event, d) => {
          if (!event.active) simulationRef.current?.alphaTarget(0.3).restart();
          d.fx = d.x;
          d.fy = d.y;
        })
        .on('drag', (event, d) => {
          d.fx = event.x;
          d.fy = event.y;
        })
        .on('end', (event, d) => {
          if (!event.active) simulationRef.current?.alphaTarget(0);
          d.fx = null;
          d.fy = null;
        }));

    // Background rounded rect
    node.append('rect')
      .attr('width', d => nodeSize(d))
      .attr('height', d => nodeSize(d))
      .attr('x', d => -nodeSize(d) / 2)
      .attr('y', d => -nodeSize(d) / 2)
      .attr('rx', 6)
      .attr('fill', d => {
        const c = DEVICE_COLORS[d.type] ?? DEVICE_COLORS.unknown;
        return c + '20';
      })
      .attr('stroke', d => d.isAgent ? '#008852' : d.phantom ? '#F59E0B' : (DEVICE_COLORS[d.type] ?? DEVICE_COLORS.unknown))
      .attr('stroke-width', d => d.isAgent ? 2.5 : d.phantom ? 2 : 1.5)
      .attr('stroke-dasharray', d => d.phantom ? '4,2' : 'none');

    // Icon paths
    node.each(function(d) {
      const paths = DEVICE_SVG_PATHS[d.type] ?? DEVICE_SVG_PATHS.unknown;
      const s = nodeSize(d);
      const iconScale = (s * 0.5) / 24;
      const color = DEVICE_COLORS[d.type] ?? DEVICE_COLORS.unknown;
      const iconG = d3.select(this).append('g')
        .attr('transform', `translate(${-s * 0.25}, ${-s * 0.25}) scale(${iconScale})`);
      paths.forEach(p => {
        iconG.append('path')
          .attr('d', p)
          .attr('fill', 'none')
          .attr('stroke', color)
          .attr('stroke-width', 2)
          .attr('stroke-linecap', 'round')
          .attr('stroke-linejoin', 'round');
      });
    });

    // Node labels
    const nodeLabel = g.append('g')
      .selectAll('text')
      .data(simNodes)
      .join('text')
      .attr('font-size', 10)
      .attr('font-weight', d => d.phantom ? 'normal' : '500')
      .attr('fill', '#374151')
      .attr('text-anchor', 'middle')
      .attr('dy', d => nodeSize(d) / 2 + 14)
      .text(d => d.label.length > 20 ? d.label.slice(0, 18) + '…' : d.label);

    // Simulation
    const simulation = d3.forceSimulation<SimNode>(simNodes)
      .force('link', d3.forceLink<SimNode, SimLink>(simLinks).id(d => d.id).distance(120))
      .force('charge', d3.forceManyBody().strength(-300))
      .force('center', d3.forceCenter(width / 2, height / 2))
      .force('collision', d3.forceCollide().radius(d => nodeSize(d as SimNode) / 2 + 15))
      .on('tick', () => {
        link
          .attr('x1', d => (d.source as SimNode).x!)
          .attr('y1', d => (d.source as SimNode).y!)
          .attr('x2', d => (d.target as SimNode).x!)
          .attr('y2', d => (d.target as SimNode).y!);
        linkLabel
          .attr('x', d => ((d.source as SimNode).x! + (d.target as SimNode).x!) / 2)
          .attr('y', d => ((d.source as SimNode).y! + (d.target as SimNode).y!) / 2 - 6);
        node
          .attr('transform', d => `translate(${d.x!},${d.y!})`);
        nodeLabel
          .attr('x', d => d.x!)
          .attr('y', d => d.y!);
      });

    simulationRef.current = simulation;

    // Fit to view after settling
    setTimeout(() => {
      const bounds = (g.node() as SVGGElement)?.getBBox();
      if (bounds && bounds.width > 0) {
        const padding = 40;
        const scale = Math.min(
          (width - padding * 2) / bounds.width,
          (height - padding * 2) / bounds.height,
          1.5,
        );
        const tx = width / 2 - (bounds.x + bounds.width / 2) * scale;
        const ty = height / 2 - (bounds.y + bounds.height / 2) * scale;
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        (svg.transition().duration(500) as any).call(
          zoom.transform,
          d3.zoomIdentity.translate(tx, ty).scale(scale),
        );
      }
    }, 2000);
  }, []);

  useEffect(() => {
    if (!data || !svgRef.current) return;
    renderGraph(data);
    return () => { simulationRef.current?.stop(); };
  }, [data, renderGraph]);

  if (isLoading) return <Skeleton className="h-[500px] w-full" />;
  if (!data || (data.nodes.length === 0 && data.edges.length === 0)) {
    return <EmptyState icon={<Network size={48} />} title="No topology data" description="SNMP devices with LLDP/CDP neighbors will appear here as an interactive network map." />;
  }

  const typeCounts: Record<string, number> = {};
  data.nodes.forEach(n => { typeCounts[n.type] = (typeCounts[n.type] ?? 0) + 1; });

  return (
    <div className="space-y-4">
      {/* KPI cards */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
        <Card><CardHeader className="pb-1"><CardTitle className="text-xs text-gray-500">Devices</CardTitle></CardHeader><CardContent><div className="text-2xl font-bold">{data.stats.totalDevices}</div></CardContent></Card>
        <Card><CardHeader className="pb-1"><CardTitle className="text-xs text-gray-500">Links</CardTitle></CardHeader><CardContent><div className="text-2xl font-bold">{data.stats.resolvedLinks}</div></CardContent></Card>
        <Card><CardHeader className="pb-1"><CardTitle className="text-xs text-gray-500">Phantom Devices</CardTitle></CardHeader><CardContent><div className="text-2xl font-bold text-amber-500">{data.stats.phantomDevices}</div></CardContent></Card>
        <Card>
          <CardHeader className="pb-1"><CardTitle className="text-xs text-gray-500">Device Types</CardTitle></CardHeader>
          <CardContent>
            <div className="flex flex-wrap gap-1">
              {Object.entries(typeCounts).map(([type, count]) => (
                <Badge key={type} variant="secondary" style={{ backgroundColor: (DEVICE_COLORS[type] ?? '#9CA3AF') + '20', color: DEVICE_COLORS[type] ?? '#9CA3AF' }}>
                  {type} ({count})
                </Badge>
              ))}
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Legend */}
      <div className="flex flex-wrap gap-4 text-xs text-gray-500 px-1">
        {Object.entries(DEVICE_ICONS).map(([type, Icon]) => (
          <div key={type} className="flex items-center gap-1">
            <Icon size={14} style={{ color: DEVICE_COLORS[type] ?? '#9CA3AF' }} />
            <span className="capitalize">{type.replace('_', ' ')}</span>
          </div>
        ))}
        <div className="flex items-center gap-1"><div className="w-4 h-0.5 bg-blue-300" /> LLDP</div>
        <div className="flex items-center gap-1"><div className="w-4 h-0.5 bg-yellow-300" /> CDP</div>
        <div className="flex items-center gap-1"><div className="w-4 h-0.5 border-t-2 border-dashed border-purple-400" /> Inferred</div>
        <div className="flex items-center gap-1"><div className="w-3 h-3 rounded border-2 border-[#008852]" /> Agent</div>
        <div className="flex items-center gap-1"><div className="w-3 h-3 rounded border-2 border-dashed border-amber-500" /> Phantom</div>
      </div>

      {/* Graph */}
      <Card className="relative">
        <CardContent className="p-0">
          <div ref={containerRef} className="w-full" style={{ height: '600px' }}>
            <svg ref={svgRef} className="w-full h-full" />
          </div>
          {selectedNode && <DeviceDetail node={selectedNode} onClose={() => setSelectedNode(null)} />}
        </CardContent>
      </Card>
    </div>
  );
}

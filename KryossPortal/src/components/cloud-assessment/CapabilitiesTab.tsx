import { useCloudAssessmentDetail, type FeatureInventoryEntry } from '@/api/cloudAssessment';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { CheckCircle2, XCircle, Minus, Loader2 } from 'lucide-react';

const AREA_LABELS: Record<string, string> = {
  connections: 'Connections',
  identity: 'Identity & Access',
  endpoint: 'Endpoint Management',
  data: 'Data Protection',
  productivity: 'Productivity & Collaboration',
  mail_flow: 'Email Security',
  azure: 'Azure Infrastructure',
  powerbi: 'Power BI Governance',
};

const AREA_ORDER = ['connections', 'identity', 'endpoint', 'data', 'productivity', 'mail_flow', 'azure', 'powerbi'];

function StatusIcon({ value }: { value: boolean }) {
  return value
    ? <CheckCircle2 className="h-4 w-4 text-green-600" />
    : <XCircle className="h-4 w-4 text-red-400" />;
}

const TIER_STYLES: Record<string, { label: string; className: string }> = {
  premium: { label: 'Premium', className: 'bg-purple-50 text-purple-700 border-purple-200' },
  standard: { label: 'Standard', className: 'bg-blue-50 text-blue-700 border-blue-200' },
  none: { label: 'Not Licensed', className: 'bg-gray-50 text-gray-500 border-gray-200' },
};

function TierBadge({ tier }: { tier: string | null }) {
  if (!tier) return null;
  const style = TIER_STYLES[tier];
  if (!style) return <span className="text-xs text-muted-foreground">{tier}</span>;
  return <Badge variant="outline" className={`text-xs ${style.className}`}>{style.label}</Badge>;
}

function AdoptionCell({ pct }: { pct: number | null }) {
  if (pct === null) return <Minus className="h-4 w-4 text-gray-300" />;
  const color = pct >= 80 ? 'bg-green-500' : pct >= 50 ? 'bg-amber-500' : 'bg-red-500';
  return (
    <div className="flex items-center gap-2 min-w-[120px]">
      <div className="h-2 flex-1 rounded-full bg-gray-100 overflow-hidden">
        <div className={`h-full rounded-full ${color}`} style={{ width: `${pct}%` }} />
      </div>
      <span className="text-xs text-muted-foreground w-8 text-right">{pct}%</span>
    </div>
  );
}

function AreaGroup({ area, entries }: { area: string; entries: FeatureInventoryEntry[] }) {
  const licensedCount = entries.filter(e => e.licensed).length;
  const implementedCount = entries.filter(e => e.implemented).length;

  return (
    <Card>
      <CardHeader className="pb-2">
        <div className="flex items-center justify-between">
          <CardTitle className="text-sm font-semibold">{AREA_LABELS[area] ?? area}</CardTitle>
          <div className="flex gap-2">
            <Badge variant="outline" className="text-xs">
              {licensedCount}/{entries.length} licensed
            </Badge>
            <Badge variant="outline" className="text-xs">
              {implementedCount}/{entries.length} active
            </Badge>
          </div>
        </div>
      </CardHeader>
      <CardContent className="pt-0">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="w-[200px]">Feature</TableHead>
              <TableHead className="w-[90px] text-center">Tier</TableHead>
              <TableHead className="w-[80px] text-center">Licensed</TableHead>
              <TableHead className="w-[80px] text-center">Active</TableHead>
              <TableHead className="w-[180px]">Adoption</TableHead>
              <TableHead>Detail</TableHead>
              <TableHead className="w-[140px]">Requires</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {entries.map((e) => (
              <TableRow key={e.feature} className={!e.licensed ? 'opacity-50' : undefined}>
                <TableCell className="font-medium text-sm">{e.feature}</TableCell>
                <TableCell className="text-center"><TierBadge tier={e.licenseTier} /></TableCell>
                <TableCell className="text-center"><StatusIcon value={e.licensed} /></TableCell>
                <TableCell className="text-center"><StatusIcon value={e.implemented} /></TableCell>
                <TableCell><AdoptionCell pct={e.adoptionPct} /></TableCell>
                <TableCell className="text-xs text-muted-foreground max-w-[300px] truncate">{e.detail}</TableCell>
                <TableCell>
                  {e.licenseRequired && !e.licensed && (
                    <Badge variant="secondary" className="text-xs bg-amber-50 text-amber-700">{e.licenseRequired}</Badge>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  );
}

export function CapabilitiesTab({ scanId }: { scanId: string | undefined }) {
  const { data: detail, isLoading } = useCloudAssessmentDetail(scanId);
  const inventory = detail?.featureInventory;

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-12">
        <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (!inventory || inventory.length === 0) {
    return (
      <Card>
        <CardContent className="py-8 text-center text-muted-foreground">
          No feature inventory available. Run a new scan to generate capability data.
        </CardContent>
      </Card>
    );
  }

  const grouped = AREA_ORDER
    .map(area => ({
      area,
      entries: inventory.filter(e => e.area === area),
    }))
    .filter(g => g.entries.length > 0);

  const totalLicensed = inventory.filter(e => e.licensed).length;
  const totalImplemented = inventory.filter(e => e.implemented).length;
  const totalFeatures = inventory.length;

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-3 gap-4">
        <Card>
          <CardContent className="pt-4 pb-4 text-center">
            <div className="text-2xl font-bold">{totalFeatures}</div>
            <div className="text-xs text-muted-foreground">Features Detected</div>
          </CardContent>
        </Card>
        <Card>
          <CardContent className="pt-4 pb-4 text-center">
            <div className="text-2xl font-bold text-green-600">{totalLicensed}</div>
            <div className="text-xs text-muted-foreground">Licensed</div>
          </CardContent>
        </Card>
        <Card>
          <CardContent className="pt-4 pb-4 text-center">
            <div className="text-2xl font-bold text-blue-600">{totalImplemented}</div>
            <div className="text-xs text-muted-foreground">Active / Implemented</div>
          </CardContent>
        </Card>
      </div>

      {grouped.map(g => (
        <AreaGroup key={g.area} area={g.area} entries={g.entries} />
      ))}
    </div>
  );
}

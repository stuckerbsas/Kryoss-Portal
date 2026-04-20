import { useEffect } from 'react';
import { MapContainer, TileLayer, Marker, Popup, useMap } from 'react-leaflet';
import L from 'leaflet';
import 'leaflet/dist/leaflet.css';
import type { NetworkSite } from '@/api/networkSites';

// Fix default marker icon path issue with bundlers
delete (L.Icon.Default.prototype as unknown as Record<string, unknown>)._getIconUrl;
L.Icon.Default.mergeOptions({
  iconRetinaUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon-2x.png',
  iconUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon.png',
  shadowUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-shadow.png',
});

function FitBounds({ sites }: { sites: NetworkSite[] }) {
  const map = useMap();
  useEffect(() => {
    const pts = sites
      .filter((s) => s.geoLat != null && s.geoLon != null)
      .map((s) => [s.geoLat!, s.geoLon!] as [number, number]);
    if (pts.length > 0) {
      map.fitBounds(pts, { padding: [40, 40], maxZoom: 12 });
    }
  }, [sites, map]);
  return null;
}

function speedLabel(mbps: number | null) {
  if (mbps == null) return '--';
  return `${mbps.toFixed(1)} Mbps`;
}

interface SiteMapProps {
  sites: NetworkSite[];
  onSiteClick?: (siteId: string) => void;
}

export function SiteMap({ sites, onSiteClick }: SiteMapProps) {
  const geoSites = sites.filter((s) => s.geoLat != null && s.geoLon != null);

  if (geoSites.length === 0) {
    return (
      <div className="flex items-center justify-center h-64 bg-muted/30 rounded-lg text-sm text-muted-foreground">
        No geo data available. Run agents to detect public IPs and rebuild sites.
      </div>
    );
  }

  const center = geoSites.length === 1
    ? [geoSites[0].geoLat!, geoSites[0].geoLon!] as [number, number]
    : [0, 0] as [number, number];

  return (
    <MapContainer
      center={center}
      zoom={4}
      className="h-[400px] w-full rounded-lg border"
      scrollWheelZoom
    >
      <TileLayer
        attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OSM</a>'
        url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
      />
      <FitBounds sites={geoSites} />
      {geoSites.map((site) => (
        <Marker
          key={site.id}
          position={[site.geoLat!, site.geoLon!]}
          eventHandlers={{
            click: () => onSiteClick?.(site.id),
          }}
        >
          <Popup>
            <div className="text-sm space-y-1 min-w-[180px]">
              <p className="font-semibold">{site.siteName}</p>
              <p className="text-xs text-gray-500">{site.publicIp}</p>
              {site.geoCity && <p className="text-xs">{site.geoCity}, {site.geoCountry}</p>}
              {site.isp && <p className="text-xs">ISP: {site.isp}</p>}
              <div className="flex gap-3 text-xs pt-1">
                <span>Down: {speedLabel(site.avgDownMbps)}</span>
                <span>Up: {speedLabel(site.avgUpMbps)}</span>
              </div>
              <p className="text-xs">Agents: {site.agentCount}</p>
            </div>
          </Popup>
        </Marker>
      ))}
    </MapContainer>
  );
}

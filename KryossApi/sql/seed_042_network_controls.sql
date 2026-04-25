SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO
-- ============================================================
-- seed_042_network_controls.sql
-- Phase 5a: Network diagnostic controls (NET-001 through NET-050)
--
-- 50 controls evaluated server-side from the network diagnostics
-- payload submitted by the agent (machine_network_diag tables).
--
-- Linked to ALL workstation + server + DC platforms.
-- Linked to NIST + CIS. Selective HIPAA/ISO/PCI tagging.
--
-- Run AFTER 041_network_diagnostics.sql. Fully idempotent.
-- ============================================================

-- Expand type constraint to include network_diag
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'ck_ctrldef_type')
    ALTER TABLE control_defs DROP CONSTRAINT ck_ctrldef_type;
GO
ALTER TABLE control_defs ADD CONSTRAINT ck_ctrldef_type CHECK ([type] IN (
    'registry','secedit','auditpol','firewall','service','netaccount','command',
    'eventlog','certstore','bitlocker','tpm','network_diag','dc'
));
GO

BEGIN TRANSACTION;

DECLARE @systemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

-- Framework IDs
DECLARE @fwCIS   INT = (SELECT id FROM frameworks WHERE code='CIS');
DECLARE @fwNIST  INT = (SELECT id FROM frameworks WHERE code='NIST');
DECLARE @fwHIPAA INT = (SELECT id FROM frameworks WHERE code='HIPAA');
DECLARE @fwPCI   INT = (SELECT id FROM frameworks WHERE code='PCI-DSS');
DECLARE @fwISO   INT = (SELECT id FROM frameworks WHERE code='ISO27001');

-- Platform IDs (all 8)
DECLARE @platW10  INT = (SELECT id FROM platforms WHERE code='W10');
DECLARE @platW11  INT = (SELECT id FROM platforms WHERE code='W11');
DECLARE @platMS19 INT = (SELECT id FROM platforms WHERE code='MS19');
DECLARE @platMS22 INT = (SELECT id FROM platforms WHERE code='MS22');
DECLARE @platMS25 INT = (SELECT id FROM platforms WHERE code='MS25');
DECLARE @platDC19 INT = (SELECT id FROM platforms WHERE code='DC19');
DECLARE @platDC22 INT = (SELECT id FROM platforms WHERE code='DC22');
DECLARE @platDC25 INT = (SELECT id FROM platforms WHERE code='DC25');

-- Category: Network Security (reuse or create)
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Network Security')
    INSERT INTO control_categories (name, sort_order, created_by)
    VALUES (N'Network Security', 900, @systemUserId);
DECLARE @catNet INT = (SELECT id FROM control_categories WHERE name = N'Network Security');

-- Category: Network Performance
IF NOT EXISTS (SELECT 1 FROM control_categories WHERE name = N'Network Performance')
    INSERT INTO control_categories (name, sort_order, created_by)
    VALUES (N'Network Performance', 910, @systemUserId);
DECLARE @catPerf INT = (SELECT id FROM control_categories WHERE name = N'Network Performance');

-- ============================================================
-- INSERT CONTROLS (using correct column names: control_id, name, [type])
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-001')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-001', @catPerf, N'Internet download speed above 10 Mbps', 'network_diag', 'medium',
    '{"field":"downloadMbps","operator":"gte","expected":10}',
    N'Upgrade internet connection or investigate bandwidth saturation. Check for QoS policies limiting throughput.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-002')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-002', @catPerf, N'Internet upload speed above 5 Mbps', 'network_diag', 'medium',
    '{"field":"uploadMbps","operator":"gte","expected":5}',
    N'Upload bandwidth critical for cloud backup, Teams video, and OneDrive sync. Consider upgrading to symmetric connection.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-003')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-003', @catPerf, N'Internet latency below 100ms', 'network_diag', 'medium',
    '{"field":"internetLatencyMs","operator":"lte","expected":100}',
    N'High latency impacts cloud service performance. Check ISP connection, routing, and DNS resolution times.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-004')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-004', @catPerf, N'Internet latency below 50ms (optimal)', 'network_diag', 'low',
    '{"field":"internetLatencyMs","operator":"lte","expected":50}',
    N'Latency under 50ms optimal for Teams/VoIP. Consider dedicated internet circuit or SD-WAN for latency-sensitive traffic.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-005')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-005', @catPerf, N'No internal peers with >5% packet loss', 'network_diag', 'high',
    '{"field":"maxPeerPacketLoss","operator":"lte","expected":5}',
    N'Packet loss indicates network congestion, faulty cabling, or switch port errors. Check switch error counters and cable integrity.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-006')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-006', @catPerf, N'Internal peer average latency below 10ms', 'network_diag', 'medium',
    '{"field":"avgPeerLatencyMs","operator":"lte","expected":10}',
    N'Same-segment latency should be under 10ms. High latency suggests switch congestion or duplex mismatch.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-007')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-007', @catPerf, N'Internal peer jitter below 5ms', 'network_diag', 'low',
    '{"field":"maxPeerJitterMs","operator":"lte","expected":5}',
    N'High jitter degrades VoIP and video quality. Implement QoS prioritization for real-time traffic.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-008')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-008', @catPerf, N'All internal peers reachable', 'network_diag', 'medium',
    '{"field":"unreachablePeerCount","operator":"eq","expected":0}',
    N'Unreachable peers may indicate firewall blocking ICMP, host-based firewall, or network segmentation issues.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-009')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-009', @catNet, N'VPN connection detected (informational)', 'network_diag', 'low',
    '{"field":"vpnDetected","operator":"info"}',
    NULL, 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-010')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-010', @catNet, N'Route table size reasonable (<100 routes)', 'network_diag', 'low',
    '{"field":"routeCount","operator":"lte","expected":100}',
    N'Excessive routes may indicate split-tunnel misconfiguration. Review VPN and static route policies.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-011')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-011', @catNet, N'No default gateway conflicts', 'network_diag', 'high',
    '{"field":"defaultGatewayCount","operator":"lte","expected":1}',
    N'Multiple default gateways cause unpredictable routing. Remove extra gateways or configure route metrics properly.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-012')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-012', @catNet, N'Network adapter count (informational)', 'network_diag', 'low',
    '{"field":"adapterCount","operator":"info"}',
    NULL, 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-013')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-013', @catNet, N'DNS servers configured', 'network_diag', 'critical',
    '{"field":"hasDnsServers","operator":"eq","expected":true}',
    N'Without DNS, name resolution fails completely. Configure at least one DNS server on the primary adapter.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-014')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-014', @catNet, N'No public DNS on domain-joined machines', 'network_diag', 'high',
    '{"field":"publicDnsOnDomainJoined","operator":"eq","expected":false}',
    N'Domain-joined machines must use internal DNS for AD authentication and resource location. Remove 8.8.8.8/1.1.1.1 entries.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-015')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-015', @catNet, N'DHCP status (informational)', 'network_diag', 'low',
    '{"field":"dhcpEnabled","operator":"info"}',
    NULL, 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-016')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-016', @catNet, N'Gateway configured on primary adapter', 'network_diag', 'high',
    '{"field":"hasGateway","operator":"eq","expected":true}',
    N'Primary adapter must have a default gateway for internet and cross-subnet access.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-017')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-017', @catPerf, N'Bandwidth utilization send (informational)', 'network_diag', 'low',
    '{"field":"bandwidthSendMbps","operator":"info"}',
    NULL, 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-018')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-018', @catPerf, N'Bandwidth utilization receive (informational)', 'network_diag', 'low',
    '{"field":"bandwidthRecvMbps","operator":"info"}',
    NULL, 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-019')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-019', @catNet, N'No open/WEP WiFi connections', 'network_diag', 'critical',
    '{"field":"openWifiCount","operator":"eq","expected":0}',
    N'Open/WEP WiFi is trivially interceptable. Enforce WPA2-Enterprise or WPA3 on all wireless connections.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-020')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-020', @catPerf, N'WiFi link speed >= 100 Mbps', 'network_diag', 'low',
    '{"field":"wifiSpeedMbps","operator":"gte","expected":100}',
    N'Low WiFi speed may indicate distance from AP, interference, or legacy 802.11n hardware. Consider WiFi 6 upgrade.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-021')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-021', @catNet, N'IPv6 status (informational)', 'network_diag', 'low',
    '{"field":"ipv6Enabled","operator":"info"}',
    NULL, 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-022')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-022', @catNet, N'WPAD disabled', 'network_diag', 'high',
    '{"field":"wpadEnabled","operator":"eq","expected":false}',
    N'WPAD auto-discovery can be exploited for MITM attacks. Disable via GPO or registry: HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Internet Settings\WinHttp\DisableWpad=1', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-023')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-023', @catNet, N'Hosts file not tampered (<5 custom entries)', 'network_diag', 'high',
    '{"field":"hostsFileEntryCount","operator":"lte","expected":5}',
    N'Excessive hosts file entries may indicate malware DNS hijacking. Review C:\Windows\System32\drivers\etc\hosts for suspicious entries.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-024')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-024', @catNet, N'NTP time source configured', 'network_diag', 'high',
    '{"field":"ntpConfigured","operator":"eq","expected":true}',
    N'Time sync critical for Kerberos auth (5-min skew = auth failure) and log correlation. Configure w32time to DC or reliable NTP source.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-025')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-025', @catPerf, N'Primary adapter link speed >= 1 Gbps', 'network_diag', 'low',
    '{"field":"primaryLinkSpeedMbps","operator":"gte","expected":1000}',
    N'Wired connections should be 1 Gbps. Check cable category (Cat5e minimum), switch port speed, and NIC driver settings.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-026')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-026', @catNet, N'Subnet count (informational)', 'network_diag', 'low',
    '{"field":"distinctSubnetCount","operator":"info"}',
    NULL, 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-027')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-027', @catPerf, N'Cross-subnet latency below 50ms', 'network_diag', 'medium',
    '{"field":"maxCrossSubnetLatencyMs","operator":"lte","expected":50}',
    N'High cross-subnet latency indicates WAN link saturation or misconfigured routing. Review site-to-site VPN or MPLS performance.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-028')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-028', @catNet, N'No duplicate IP addresses', 'network_diag', 'critical',
    '{"field":"duplicateIpCount","operator":"eq","expected":0}',
    N'IP conflicts cause intermittent connectivity. Investigate DHCP scope overlaps or static IP assignments outside DHCP range.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-029')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-029', @catNet, N'ARP table size reasonable (<500)', 'network_diag', 'low',
    '{"field":"arpEntryCount","operator":"lte","expected":500}',
    N'Large ARP table may indicate broadcast storm or scanning activity on the network.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-030')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-030', @catNet, N'Listening ports within expected range (<20)', 'network_diag', 'medium',
    '{"field":"listeningPortCount","operator":"lte","expected":20}',
    N'Excessive listening ports increase attack surface. Audit with netstat and disable unnecessary services.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-031')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-031', @catPerf, N'Download speed above 50 Mbps', 'network_diag', 'low',
    '{"field":"downloadMbps","operator":"gte","expected":50}',
    N'50+ Mbps recommended for cloud-heavy workloads (M365, Teams, OneDrive sync).', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-032')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-032', @catPerf, N'Download speed above 100 Mbps', 'network_diag', 'low',
    '{"field":"downloadMbps","operator":"gte","expected":100}',
    N'100+ Mbps optimal for large file transfers, video conferencing, and remote desktop performance.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-033')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-033', @catPerf, N'Upload speed above 20 Mbps', 'network_diag', 'low',
    '{"field":"uploadMbps","operator":"gte","expected":20}',
    N'20+ Mbps upload recommended for cloud backup and video conferencing quality.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-034')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-034', @catNet, N'No disconnected adapters with IP config', 'network_diag', 'medium',
    '{"field":"disconnectedWithIpCount","operator":"eq","expected":0}',
    N'Adapters configured but disconnected may indicate hardware failure or driver issues.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-035')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-035', @catNet, N'At least two DNS servers configured', 'network_diag', 'medium',
    '{"field":"dnsServerCount","operator":"gte","expected":2}',
    N'DNS redundancy prevents single point of failure. Configure primary and secondary DNS servers.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-036')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-036', @catNet, N'VPN split tunnel detection (informational)', 'network_diag', 'medium',
    '{"field":"vpnSplitTunnel","operator":"info"}',
    N'Full tunnel: all traffic through VPN (more secure). Split tunnel: only corporate traffic via VPN (better performance). Evaluate based on security policy.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-037')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-037', @catNet, N'No invalid subnet masks', 'network_diag', 'high',
    '{"field":"invalidSubnetMask","operator":"eq","expected":false}',
    N'Extreme subnet masks (/32 or /0) indicate misconfiguration. Correct adapter subnet settings.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-038')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-038', @catNet, N'No APIPA addresses (169.254.x.x)', 'network_diag', 'critical',
    '{"field":"apipaCount","operator":"eq","expected":0}',
    N'APIPA addresses indicate DHCP server unreachable. Check DHCP service, scope availability, and network connectivity to DHCP server.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-039')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-039', @catNet, N'Single gateway per adapter', 'network_diag', 'medium',
    '{"field":"multiGatewayAdapterCount","operator":"eq","expected":0}',
    N'Multiple gateways on one adapter causes asymmetric routing. Remove extra gateways.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-040')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-040', @catNet, N'MAC address inventory (informational)', 'network_diag', 'low',
    '{"field":"macAddresses","operator":"info"}',
    NULL, 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-041')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-041', @catNet, N'NIC teaming detection (informational)', 'network_diag', 'low',
    '{"field":"nicTeamingDetected","operator":"info"}',
    NULL, 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-042')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-042', @catPerf, N'MTU size standard (1500)', 'network_diag', 'low',
    '{"field":"nonStandardMtuCount","operator":"eq","expected":0}',
    N'Non-standard MTU can cause fragmentation. Reset to 1500 unless jumbo frames intentionally configured.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-043')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-043', @catPerf, N'No bandwidth saturation (>80% link)', 'network_diag', 'high',
    '{"field":"bandwidthSaturationPct","operator":"lte","expected":80}',
    N'Bandwidth saturation causes packet drops and latency spikes. Upgrade link or implement QoS.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-044')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-044', @catPerf, N'Cross-subnet peers reachable', 'network_diag', 'medium',
    '{"field":"crossSubnetUnreachableCount","operator":"eq","expected":0}',
    N'Cross-subnet peers unreachable may indicate routing or firewall issues between sites.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-045')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-045', @catNet, N'Site topology map (informational)', 'network_diag', 'low',
    '{"field":"siteTopology","operator":"info"}',
    NULL, 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-046')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-046', @catNet, N'No excessive loopback routes (<3)', 'network_diag', 'medium',
    '{"field":"loopbackRouteCount","operator":"lte","expected":3}',
    N'Extra loopback routes may indicate malware or misconfigured software. Investigate route origin.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-047')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-047', @catNet, N'Persistent route inventory (informational)', 'network_diag', 'low',
    '{"field":"persistentRouteCount","operator":"info"}',
    NULL, 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-048')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-048', @catNet, N'No route metric conflicts', 'network_diag', 'medium',
    '{"field":"routeMetricConflictCount","operator":"eq","expected":0}',
    N'Routes with identical metrics to different gateways cause unpredictable routing. Set explicit metrics.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-049')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-049', @catNet, N'LLMNR/NetBIOS disabled', 'network_diag', 'high',
    '{"field":"llmnrNetbiosEnabled","operator":"eq","expected":false}',
    N'LLMNR and NetBIOS enable name resolution poisoning (Responder/MITM). Disable via GPO: Computer Configuration > Administrative Templates > Network > DNS Client > Turn off multicast name resolution.', 1, 1, @systemUserId);

IF NOT EXISTS (SELECT 1 FROM control_defs WHERE control_id='NET-050')
INSERT INTO control_defs (control_id, category_id, name, [type], severity, check_json, remediation, is_active, version, created_by)
VALUES ('NET-050', @catPerf, N'Network health composite score >= 70', 'network_diag', 'medium',
    '{"field":"networkHealthScore","operator":"gte","expected":70}',
    N'Composite score derived from speed, latency, packet loss, and configuration checks. Address individual failing controls to improve.', 1, 1, @systemUserId);

-- ============================================================
-- Link NET controls to ALL platforms
-- ============================================================
DECLARE @netCodes TABLE (code VARCHAR(10));
INSERT INTO @netCodes VALUES
    ('NET-001'),('NET-002'),('NET-003'),('NET-004'),('NET-005'),
    ('NET-006'),('NET-007'),('NET-008'),('NET-009'),('NET-010'),
    ('NET-011'),('NET-012'),('NET-013'),('NET-014'),('NET-015'),
    ('NET-016'),('NET-017'),('NET-018'),('NET-019'),('NET-020'),
    ('NET-021'),('NET-022'),('NET-023'),('NET-024'),('NET-025'),
    ('NET-026'),('NET-027'),('NET-028'),('NET-029'),('NET-030'),
    ('NET-031'),('NET-032'),('NET-033'),('NET-034'),('NET-035'),
    ('NET-036'),('NET-037'),('NET-038'),('NET-039'),('NET-040'),
    ('NET-041'),('NET-042'),('NET-043'),('NET-044'),('NET-045'),
    ('NET-046'),('NET-047'),('NET-048'),('NET-049'),('NET-050');

DECLARE @allPlats TABLE (plat_id INT);
INSERT INTO @allPlats VALUES
    (@platW10),(@platW11),(@platMS19),(@platMS22),(@platMS25),
    (@platDC19),(@platDC22),(@platDC25);

INSERT INTO control_platforms (control_def_id, platform_id)
SELECT cd.id, ap.plat_id
FROM control_defs cd
CROSS JOIN @allPlats ap
INNER JOIN @netCodes nc ON nc.code = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_platforms cp
    WHERE cp.control_def_id = cd.id AND cp.platform_id = ap.plat_id
);

-- ============================================================
-- Link to frameworks (all NET controls -> NIST + CIS)
-- ============================================================
INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, fw.id
FROM control_defs cd
CROSS JOIN (SELECT @fwNIST AS id UNION ALL SELECT @fwCIS) fw
INNER JOIN @netCodes nc ON nc.code = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_frameworks cf
    WHERE cf.control_def_id = cd.id AND cf.framework_id = fw.id
);

-- Selective HIPAA tagging
DECLARE @hipaaNet TABLE (code VARCHAR(10));
INSERT INTO @hipaaNet VALUES
    ('NET-005'),('NET-011'),('NET-013'),('NET-014'),('NET-019'),
    ('NET-022'),('NET-023'),('NET-024'),('NET-028'),('NET-038'),
    ('NET-049');

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwHIPAA
FROM control_defs cd
INNER JOIN @hipaaNet hn ON hn.code = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_frameworks cf
    WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwHIPAA
);

-- Selective ISO 27001 tagging
DECLARE @isoNet TABLE (code VARCHAR(10));
INSERT INTO @isoNet VALUES
    ('NET-005'),('NET-011'),('NET-013'),('NET-014'),('NET-019'),
    ('NET-022'),('NET-023'),('NET-024'),('NET-028'),('NET-035'),
    ('NET-037'),('NET-038'),('NET-049');

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwISO
FROM control_defs cd
INNER JOIN @isoNet isn ON isn.code = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_frameworks cf
    WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwISO
);

-- Selective PCI-DSS tagging
DECLARE @pciNet TABLE (code VARCHAR(10));
INSERT INTO @pciNet VALUES
    ('NET-013'),('NET-014'),('NET-019'),('NET-022'),('NET-024'),
    ('NET-028'),('NET-030'),('NET-049');

INSERT INTO control_frameworks (control_def_id, framework_id)
SELECT cd.id, @fwPCI
FROM control_defs cd
INNER JOIN @pciNet pn ON pn.code = cd.control_id
WHERE NOT EXISTS (
    SELECT 1 FROM control_frameworks cf
    WHERE cf.control_def_id = cd.id AND cf.framework_id = @fwPCI
);

COMMIT TRANSACTION;
GO

PRINT 'Network diagnostic controls (NET-001..NET-050) seeded successfully';

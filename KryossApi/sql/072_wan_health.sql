-- 072_wan_health.sql — IA-3: WAN & Site Connectivity Health
-- Adds WAN quality tracking to network_sites + traceroute storage + WAN findings

-- ── Extend network_sites with WAN health metrics ──
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('network_sites') AND name = 'monthly_cost')
BEGIN
    ALTER TABLE network_sites ADD monthly_cost DECIMAL(10,2) NULL;
    ALTER TABLE network_sites ADD link_type NVARCHAR(50) NULL; -- internet, mpls, sdwan, ipsec, expressroute, cellular, satellite, lte
    ALTER TABLE network_sites ADD is_redundant BIT NOT NULL DEFAULT 0;
    ALTER TABLE network_sites ADD wan_score DECIMAL(5,2) NULL;
    ALTER TABLE network_sites ADD avg_jitter_ms DECIMAL(10,2) NULL;
    ALTER TABLE network_sites ADD avg_packet_loss_pct DECIMAL(5,2) NULL;
    ALTER TABLE network_sites ADD hop_count INT NULL;
    ALTER TABLE network_sites ADD unique_isp_count INT NOT NULL DEFAULT 1;
END
GO

-- ── Extend machine_network_diag with WAN probe fields ──
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('machine_network_diag') AND name = 'jitter_ms')
BEGIN
    ALTER TABLE machine_network_diag ADD jitter_ms DECIMAL(10,2) NULL;
    ALTER TABLE machine_network_diag ADD packet_loss_pct DECIMAL(5,2) NULL;
    ALTER TABLE machine_network_diag ADD hop_count INT NULL;
    ALTER TABLE machine_network_diag ADD traceroute_target NVARCHAR(256) NULL;
    ALTER TABLE machine_network_diag ADD traceroute_json NVARCHAR(MAX) NULL; -- JSON array of hops
END
GO

-- ── WAN findings table (server-generated per site rebuild) ──
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('wan_findings') AND type = 'U')
BEGIN
    CREATE TABLE wan_findings (
        id INT IDENTITY(1,1) PRIMARY KEY,
        organization_id UNIQUEIDENTIFIER NOT NULL,
        site_id UNIQUEIDENTIFIER NULL,
        severity NVARCHAR(20) NOT NULL, -- critical, high, medium, low, info
        category NVARCHAR(50) NOT NULL, -- bandwidth, latency, jitter, packet_loss, redundancy, cost, stability
        title NVARCHAR(256) NOT NULL,
        detail NVARCHAR(1024) NULL,
        metric_value DECIMAL(10,2) NULL,
        metric_threshold DECIMAL(10,2) NULL,
        created_at DATETIME2 NOT NULL DEFAULT GETUTCDATE(),

        CONSTRAINT fk_wan_findings_org FOREIGN KEY (organization_id) REFERENCES organizations(id),
        CONSTRAINT fk_wan_findings_site FOREIGN KEY (site_id) REFERENCES network_sites(id)
    );

    CREATE INDEX ix_wan_findings_org ON wan_findings(organization_id);
    CREATE INDEX ix_wan_findings_site ON wan_findings(site_id);
END
GO

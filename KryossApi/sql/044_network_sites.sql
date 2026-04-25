-- 044: Network Sites + Public IP tracking (IA-11 Track A)
-- Enables auto-clustering machines by public IP → site, GeoIP enrichment,
-- speedtest-on-IP-change, and SLA tracking.

-- 1. Add public IP columns to machines
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('machines') AND name = 'last_public_ip')
    ALTER TABLE machines ADD last_public_ip NVARCHAR(45) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('machines') AND name = 'last_public_ip_at')
    ALTER TABLE machines ADD last_public_ip_at DATETIME2 NULL;

-- 2. Public IP history per machine
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'machine_public_ip_history')
CREATE TABLE machine_public_ip_history (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    machine_id      UNIQUEIDENTIFIER NOT NULL REFERENCES machines(id) ON DELETE CASCADE,
    public_ip       NVARCHAR(45)     NOT NULL,
    first_seen      DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
    last_seen       DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
    geo_country     NVARCHAR(2)      NULL,
    geo_region      NVARCHAR(100)    NULL,
    geo_city        NVARCHAR(200)    NULL,
    geo_lat         DECIMAL(9,6)     NULL,
    geo_lon         DECIMAL(9,6)     NULL,
    isp             NVARCHAR(200)    NULL,
    asn             INT              NULL,
    asn_org         NVARCHAR(200)    NULL,
    conn_type       NVARCHAR(30)     NULL  -- residential|business|cellular|satellite|hosting
);

-- 3. Network sites (auto-derived from public IP clusters, editable by MSP)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'network_sites')
CREATE TABLE network_sites (
    id                  UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    organization_id     UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
    site_name           NVARCHAR(200)    NOT NULL,
    public_ip           NVARCHAR(45)     NULL,
    geo_country         NVARCHAR(2)      NULL,
    geo_region          NVARCHAR(100)    NULL,
    geo_city            NVARCHAR(200)    NULL,
    geo_lat             DECIMAL(9,6)     NULL,
    geo_lon             DECIMAL(9,6)     NULL,
    isp                 NVARCHAR(200)    NULL,
    asn                 INT              NULL,
    asn_org             NVARCHAR(200)    NULL,
    conn_type           NVARCHAR(30)     NULL,
    contracted_down_mbps DECIMAL(12,2)   NULL,
    contracted_up_mbps  DECIMAL(12,2)    NULL,
    agent_count         INT              NOT NULL DEFAULT 0,
    device_count        INT              NOT NULL DEFAULT 0,
    ip_changes_90d      INT              NOT NULL DEFAULT 0,
    avg_down_mbps       DECIMAL(12,2)    NULL,
    avg_up_mbps         DECIMAL(12,2)    NULL,
    avg_latency_ms      DECIMAL(8,2)     NULL,
    is_auto_derived     BIT              NOT NULL DEFAULT 1,
    created_at          DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
    updated_at          DATETIME2        NOT NULL DEFAULT GETUTCDATE()
);

-- Indexes
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_machine_pip_history_machine')
    CREATE NONCLUSTERED INDEX IX_machine_pip_history_machine
    ON machine_public_ip_history(machine_id, last_seen DESC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_network_sites_org')
    CREATE NONCLUSTERED INDEX IX_network_sites_org
    ON network_sites(organization_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_network_sites_ip')
    CREATE NONCLUSTERED INDEX IX_network_sites_ip
    ON network_sites(public_ip);

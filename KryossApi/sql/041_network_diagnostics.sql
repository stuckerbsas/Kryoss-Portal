-- ============================================================
-- 041_network_diagnostics.sql
-- Phase 5a: Network diagnostics storage
--
-- Stores per-machine network assessment data: speed test results,
-- internal latency measurements, route table, VPN interfaces,
-- adapter details, and bandwidth snapshots.
-- ============================================================
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- Main diagnostics table (one row per machine per scan)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'machine_network_diag')
CREATE TABLE machine_network_diag (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    machine_id      UNIQUEIDENTIFIER NOT NULL REFERENCES machines(id),
    run_id          UNIQUEIDENTIFIER NULL REFERENCES assessment_runs(id),
    download_mbps   DECIMAL(10,2) NULL,
    upload_mbps     DECIMAL(10,2) NULL,
    internet_latency_ms DECIMAL(10,1) NULL,
    route_count     INT NULL,
    vpn_detected    BIT NOT NULL DEFAULT 0,
    vpn_adapters    NVARCHAR(MAX) NULL,       -- JSON array of VPN interface names
    adapter_count   INT NULL,
    bandwidth_send_mbps DECIMAL(10,2) NULL,
    bandwidth_recv_mbps DECIMAL(10,2) NULL,
    raw_data        NVARCHAR(MAX) NULL,       -- full NetworkDiagResult JSON
    scanned_at      DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT uq_machine_network_diag_run UNIQUE (machine_id, run_id)
);
GO

-- Internal latency peers (one row per peer per scan)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'machine_network_latency')
CREATE TABLE machine_network_latency (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    diag_id         INT NOT NULL REFERENCES machine_network_diag(id) ON DELETE CASCADE,
    host            VARCHAR(45) NOT NULL,
    subnet          VARCHAR(20) NULL,
    reachable       BIT NOT NULL DEFAULT 0,
    avg_ms          DECIMAL(10,1) NULL,
    min_ms          INT NULL,
    max_ms          INT NULL,
    jitter_ms       DECIMAL(10,1) NULL,
    packet_loss     SMALLINT NULL,
    total_sent      SMALLINT NULL
);
GO

-- Route table entries (one row per route per scan)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'machine_network_routes')
CREATE TABLE machine_network_routes (
    id              INT IDENTITY(1,1) PRIMARY KEY,
    diag_id         INT NOT NULL REFERENCES machine_network_diag(id) ON DELETE CASCADE,
    destination     VARCHAR(45) NOT NULL,
    mask            VARCHAR(45) NOT NULL,
    next_hop        VARCHAR(45) NOT NULL,
    interface_index INT NULL,
    metric          INT NULL,
    route_type      SMALLINT NULL,
    protocol        SMALLINT NULL
);
GO

-- Indexes
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_machine_network_diag_machine')
    CREATE INDEX ix_machine_network_diag_machine ON machine_network_diag(machine_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_machine_network_latency_diag')
    CREATE INDEX ix_machine_network_latency_diag ON machine_network_latency(diag_id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_machine_network_routes_diag')
    CREATE INDEX ix_machine_network_routes_diag ON machine_network_routes(diag_id);
GO

PRINT 'Network diagnostics tables created successfully';

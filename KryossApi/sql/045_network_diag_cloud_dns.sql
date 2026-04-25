-- 045: Add cloud endpoint latency + DNS resolution columns to machine_network_diag
-- Part of IA-11 Track B (agent v1.6 upgrade)
-- Idempotent: IF NOT EXISTS guards

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('machine_network_diag') AND name = 'dns_resolution_ms')
    ALTER TABLE machine_network_diag ADD dns_resolution_ms DECIMAL(10,2) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('machine_network_diag') AND name = 'cloud_endpoint_count')
    ALTER TABLE machine_network_diag ADD cloud_endpoint_count INT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('machine_network_diag') AND name = 'cloud_endpoint_avg_ms')
    ALTER TABLE machine_network_diag ADD cloud_endpoint_avg_ms DECIMAL(10,2) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('machine_network_diag') AND name = 'triggered_by_ip_change')
    ALTER TABLE machine_network_diag ADD triggered_by_ip_change BIT NOT NULL DEFAULT 0;
GO

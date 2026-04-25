-- 051: Network diagnostics v2 — gateway latency + adapter classification
-- Adds gateway_latency_ms, gateway_ip, wifi_count, vpn_adapter_count, eth_count

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('machine_network_diag') AND name = 'gateway_latency_ms')
    ALTER TABLE machine_network_diag ADD gateway_latency_ms DECIMAL(10,2) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('machine_network_diag') AND name = 'gateway_ip')
    ALTER TABLE machine_network_diag ADD gateway_ip NVARCHAR(50) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('machine_network_diag') AND name = 'wifi_count')
    ALTER TABLE machine_network_diag ADD wifi_count INT NOT NULL DEFAULT 0;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('machine_network_diag') AND name = 'vpn_adapter_count')
    ALTER TABLE machine_network_diag ADD vpn_adapter_count INT NOT NULL DEFAULT 0;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('machine_network_diag') AND name = 'eth_count')
    ALTER TABLE machine_network_diag ADD eth_count INT NOT NULL DEFAULT 0;
GO

PRINT '051: Network diagnostics v2 columns added';

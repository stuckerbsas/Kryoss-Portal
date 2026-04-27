-- 069: Agent remote configuration (portal-controlled, delivered via heartbeat)
ALTER TABLE machines ADD
    config_compliance_interval_hours INT NOT NULL DEFAULT 24,
    config_snmp_interval_minutes    INT NOT NULL DEFAULT 240,
    config_enable_network_scan      BIT NOT NULL DEFAULT 0,
    config_network_scan_interval_hours INT NOT NULL DEFAULT 12,
    config_enable_passive_discovery BIT NOT NULL DEFAULT 1;

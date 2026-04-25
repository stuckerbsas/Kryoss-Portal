-- 064: Add reverse DNS + ping metrics to snmp_devices
-- Supports: hostname resolution and network quality per device

ALTER TABLE snmp_devices ADD
    reverse_dns varchar(255) NULL,
    ping_latency_ms float NULL,
    ping_loss_pct float NULL,
    ping_jitter_ms float NULL;

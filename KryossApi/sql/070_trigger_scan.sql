-- 070: On-demand scan trigger from portal
-- Portal sets force_scan_requested_at, heartbeat delivers forceScan=true to agent, then clears

ALTER TABLE machines ADD
    force_scan_requested_at DATETIME2 NULL,
    force_scan_requested_by UNIQUEIDENTIFIER NULL;
GO

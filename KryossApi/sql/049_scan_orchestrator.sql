-- 049_scan_orchestrator.sql
-- A-13: Server-side scan orchestrator — scan windows + slot assignment

BEGIN TRANSACTION;

ALTER TABLE organizations ADD
    scan_window_start TIME NOT NULL CONSTRAINT DF_org_scan_start DEFAULT '02:00',
    scan_window_end   TIME NOT NULL CONSTRAINT DF_org_scan_end   DEFAULT '06:00';

ALTER TABLE machines ADD
    scan_slot_offset_sec INT       NULL,
    last_checkin_at      DATETIME2 NULL;

COMMIT;

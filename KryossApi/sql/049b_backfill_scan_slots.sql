-- 049b_backfill_scan_slots.sql
-- Backfill scan slots for machines enrolled before A-13.
-- Distributes uniformly within each org's scan window.

;WITH numbered AS (
    SELECT
        m.id,
        m.organization_id,
        ROW_NUMBER() OVER (PARTITION BY m.organization_id ORDER BY m.first_seen_at) - 1 AS rn,
        COUNT(*) OVER (PARTITION BY m.organization_id) AS total,
        DATEDIFF(SECOND, o.scan_window_start, o.scan_window_end) AS window_sec
    FROM machines m
    JOIN organizations o ON o.id = m.organization_id
    WHERE m.is_active = 1
      AND m.scan_slot_offset_sec IS NULL
      AND m.deleted_at IS NULL
)
UPDATE numbered
SET scan_slot_offset_sec = CASE
    WHEN total <= 1 THEN 0
    ELSE (rn * window_sec) / total
END
FROM machines
WHERE machines.id = numbered.id;

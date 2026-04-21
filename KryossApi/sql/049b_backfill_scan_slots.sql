-- 049b_backfill_scan_slots.sql
-- Backfill scan slots for machines enrolled before A-13.
-- Distributes uniformly within each org's scan window.

;WITH numbered AS (
    SELECT
        m.id,
        ROW_NUMBER() OVER (PARTITION BY m.organization_id ORDER BY m.first_seen_at) - 1 AS rn,
        COUNT(*) OVER (PARTITION BY m.organization_id) AS total,
        DATEDIFF(SECOND, o.scan_window_start, o.scan_window_end) AS window_sec
    FROM machines m
    JOIN organizations o ON o.id = m.organization_id
    WHERE m.is_active = 1
      AND m.scan_slot_offset_sec IS NULL
      AND m.deleted_at IS NULL
)
UPDATE m
SET m.scan_slot_offset_sec = CASE
    WHEN n.total <= 1 THEN 0
    ELSE (n.rn * n.window_sec) / n.total
END
FROM machines m
JOIN numbered n ON m.id = n.id;

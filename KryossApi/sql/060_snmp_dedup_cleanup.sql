-- 060: Clean up duplicate SNMP devices
-- Keeps the most recently scanned device per (org, MAC) or (org, IP) if no MAC
-- Deletes children first (interfaces, supplies, neighbors), then device rows

-- Step 1: Identify duplicates by MAC (prefer newest scanned_at)
;WITH ranked AS (
    SELECT id,
           ROW_NUMBER() OVER (
               PARTITION BY organization_id, mac_address
               ORDER BY scanned_at DESC
           ) AS rn
    FROM snmp_devices
    WHERE mac_address IS NOT NULL
)
DELETE FROM snmp_device_interfaces WHERE device_id IN (SELECT id FROM ranked WHERE rn > 1);

;WITH ranked AS (
    SELECT id,
           ROW_NUMBER() OVER (
               PARTITION BY organization_id, mac_address
               ORDER BY scanned_at DESC
           ) AS rn
    FROM snmp_devices
    WHERE mac_address IS NOT NULL
)
DELETE FROM snmp_device_supplies WHERE device_id IN (SELECT id FROM ranked WHERE rn > 1);

;WITH ranked AS (
    SELECT id,
           ROW_NUMBER() OVER (
               PARTITION BY organization_id, mac_address
               ORDER BY scanned_at DESC
           ) AS rn
    FROM snmp_devices
    WHERE mac_address IS NOT NULL
)
DELETE FROM snmp_device_neighbors WHERE device_id IN (SELECT id FROM ranked WHERE rn > 1);

;WITH ranked AS (
    SELECT id,
           ROW_NUMBER() OVER (
               PARTITION BY organization_id, mac_address
               ORDER BY scanned_at DESC
           ) AS rn
    FROM snmp_devices
    WHERE mac_address IS NOT NULL
)
DELETE FROM snmp_devices WHERE id IN (SELECT id FROM ranked WHERE rn > 1);

-- Step 2: Duplicates by IP (no MAC) — keep newest
;WITH ranked AS (
    SELECT id,
           ROW_NUMBER() OVER (
               PARTITION BY organization_id, ip_address
               ORDER BY scanned_at DESC
           ) AS rn
    FROM snmp_devices
    WHERE mac_address IS NULL
)
DELETE FROM snmp_device_interfaces WHERE device_id IN (SELECT id FROM ranked WHERE rn > 1);

;WITH ranked AS (
    SELECT id,
           ROW_NUMBER() OVER (
               PARTITION BY organization_id, ip_address
               ORDER BY scanned_at DESC
           ) AS rn
    FROM snmp_devices
    WHERE mac_address IS NULL
)
DELETE FROM snmp_device_supplies WHERE device_id IN (SELECT id FROM ranked WHERE rn > 1);

;WITH ranked AS (
    SELECT id,
           ROW_NUMBER() OVER (
               PARTITION BY organization_id, ip_address
               ORDER BY scanned_at DESC
           ) AS rn
    FROM snmp_devices
    WHERE mac_address IS NULL
)
DELETE FROM snmp_device_neighbors WHERE device_id IN (SELECT id FROM ranked WHERE rn > 1);

;WITH ranked AS (
    SELECT id,
           ROW_NUMBER() OVER (
               PARTITION BY organization_id, ip_address
               ORDER BY scanned_at DESC
           ) AS rn
    FROM snmp_devices
    WHERE mac_address IS NULL
)
DELETE FROM snmp_devices WHERE id IN (SELECT id FROM ranked WHERE rn > 1);

-- Verify
SELECT COUNT(*) AS remaining_devices FROM snmp_devices;

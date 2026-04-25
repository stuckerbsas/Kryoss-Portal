-- 067: Per-machine auth keys for Kerberos-inspired rotation
-- Adds machine_secret (long-term), session_key (48h), prev_session_key (grace)

ALTER TABLE machines ADD
    machine_secret       NVARCHAR(128)  NULL,
    session_key          NVARCHAR(128)  NULL,
    session_key_expires_at DATETIME2    NULL,
    prev_session_key     NVARCHAR(128)  NULL,
    prev_key_expires_at  DATETIME2      NULL,
    key_rotated_at       DATETIME2      NULL,
    auth_version         INT            NOT NULL DEFAULT 1;
GO

-- auth_version: 1 = legacy org-key, 2 = per-machine session keys
-- Agents < v2.2 stay at auth_version=1, middleware falls back to org secret

-- Index for middleware lookup (session_key is checked on every request)
CREATE NONCLUSTERED INDEX IX_machines_session_key
    ON machines (session_key)
    WHERE session_key IS NOT NULL;

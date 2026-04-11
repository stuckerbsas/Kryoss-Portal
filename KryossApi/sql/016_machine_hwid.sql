-- =============================================================================
-- 016_machine_hwid.sql
-- =============================================================================
-- Adds machines.hwid — a stable hardware fingerprint produced by the agent
-- at enrollment time and sent on every subsequent signed request via the
-- X-Hwid header. See KryossApi/docs/security-baseline.md §Hardware binding
-- (P1 #7).
--
-- Semantics:
--   * hwid is NULL for machines enrolled before this migration — rollout
--     window will backfill them on the first request that carries the header.
--   * Once a non-NULL hwid is stored, the server REJECTS any request whose
--     X-Hwid does not match. This prevents token cloning across machines.
--   * Format is opaque to the server: agent ships a lowercase hex SHA-256
--     digest (64 chars). The server only compares, never parses.
--
-- Idempotent. Safe to re-run.
-- =============================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.machines')
      AND name = N'hwid'
)
BEGIN
    ALTER TABLE dbo.machines ADD hwid NVARCHAR(128) NULL;
    PRINT 'Added machines.hwid column.';
END
ELSE
BEGIN
    PRINT 'machines.hwid column already exists. Skipping.';
END

-- Sparse index — most rows will have a value in steady state, but we only
-- look it up by (agent_id, hwid) together, not alone, so no separate index
-- is strictly needed. The existing unique index on agent_id suffices.
PRINT '=== 016_machine_hwid.sql: done ===';

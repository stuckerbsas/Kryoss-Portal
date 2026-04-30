-- 085: Agent health diagnostic view
-- Shows machines that need attention: not in service mode, stale heartbeat,
-- old agent version, never scanned, errors, etc.

CREATE OR ALTER VIEW vw_agent_health AS
WITH latest_version AS (
    SELECT TOP 1 agent_version AS current_version
    FROM machines
    WHERE is_active = 1 AND deleted_at IS NULL AND agent_version IS NOT NULL
    ORDER BY
        TRY_CAST(PARSENAME(agent_version, 3) AS INT) DESC,
        TRY_CAST(PARSENAME(agent_version, 2) AS INT) DESC,
        TRY_CAST(PARSENAME(agent_version, 1) AS INT) DESC
)
SELECT
    m.id,
    m.hostname,
    o.name AS organization_name,
    m.agent_version,
    lv.current_version AS latest_agent_version,
    m.agent_mode,
    m.last_heartbeat_at,
    m.last_seen_at,
    m.last_checkin_at,
    m.latest_scan_at,
    m.latest_score,
    m.latest_grade,
    m.last_error_at,
    m.last_error_phase,
    m.last_error_msg,
    m.ip_address,
    m.os_name,
    m.is_trial,
    m.trial_expires_at,
    m.is_active,

    -- Flags
    CASE WHEN m.agent_mode IS NULL OR m.agent_mode != 'service'
         THEN 1 ELSE 0 END AS flag_not_service_mode,

    CASE WHEN m.last_heartbeat_at IS NULL
              OR DATEDIFF(MINUTE, m.last_heartbeat_at, GETUTCDATE()) > 30
         THEN 1 ELSE 0 END AS flag_heartbeat_stale,

    CASE WHEN m.agent_version IS NULL
              OR m.agent_version != lv.current_version
         THEN 1 ELSE 0 END AS flag_outdated_version,

    CASE WHEN m.latest_scan_at IS NULL
         THEN 1 ELSE 0 END AS flag_never_scanned,

    CASE WHEN m.latest_scan_at IS NOT NULL
              AND DATEDIFF(HOUR, m.latest_scan_at, GETUTCDATE()) > 48
         THEN 1 ELSE 0 END AS flag_scan_stale,

    CASE WHEN m.last_error_at IS NOT NULL
              AND DATEDIFF(HOUR, m.last_error_at, GETUTCDATE()) < 24
         THEN 1 ELSE 0 END AS flag_recent_error,

    CASE WHEN m.is_trial = 1
              AND m.trial_expires_at IS NOT NULL
              AND m.trial_expires_at < GETUTCDATE()
         THEN 1 ELSE 0 END AS flag_trial_expired,

    CASE WHEN m.hwid IS NULL
         THEN 1 ELSE 0 END AS flag_no_hwid,

    -- Summary: total flags
    (CASE WHEN m.agent_mode IS NULL OR m.agent_mode != 'service' THEN 1 ELSE 0 END)
    + (CASE WHEN m.last_heartbeat_at IS NULL OR DATEDIFF(MINUTE, m.last_heartbeat_at, GETUTCDATE()) > 30 THEN 1 ELSE 0 END)
    + (CASE WHEN m.agent_version IS NULL OR m.agent_version != lv.current_version THEN 1 ELSE 0 END)
    + (CASE WHEN m.latest_scan_at IS NULL THEN 1 ELSE 0 END)
    + (CASE WHEN m.latest_scan_at IS NOT NULL AND DATEDIFF(HOUR, m.latest_scan_at, GETUTCDATE()) > 48 THEN 1 ELSE 0 END)
    + (CASE WHEN m.last_error_at IS NOT NULL AND DATEDIFF(HOUR, m.last_error_at, GETUTCDATE()) < 24 THEN 1 ELSE 0 END)
    + (CASE WHEN m.is_trial = 1 AND m.trial_expires_at IS NOT NULL AND m.trial_expires_at < GETUTCDATE() THEN 1 ELSE 0 END)
    + (CASE WHEN m.hwid IS NULL THEN 1 ELSE 0 END)
    AS total_flags,

    -- Health status: broken > unhealthy > degraded > healthy
    CASE
        WHEN m.agent_version IS NULL OR m.latest_scan_at IS NULL
            THEN 'broken'
        WHEN m.last_error_at IS NOT NULL AND DATEDIFF(HOUR, m.last_error_at, GETUTCDATE()) < 24
            THEN 'unhealthy'
        WHEN (m.agent_mode IS NULL OR m.agent_mode != 'service')
              OR m.agent_version != lv.current_version
              OR (m.latest_scan_at IS NOT NULL AND DATEDIFF(HOUR, m.latest_scan_at, GETUTCDATE()) > 48)
              OR (m.is_trial = 1 AND m.trial_expires_at IS NOT NULL AND m.trial_expires_at < GETUTCDATE())
            THEN 'degraded'
        ELSE 'healthy'
    END AS health_status

FROM machines m
JOIN organizations o ON o.id = m.organization_id
CROSS JOIN latest_version lv
WHERE m.is_active = 1
  AND m.deleted_at IS NULL;
GO

-- Convenience: only machines that need attention
CREATE OR ALTER VIEW vw_agent_issues AS
SELECT *
FROM vw_agent_health
WHERE health_status != 'healthy'
GO

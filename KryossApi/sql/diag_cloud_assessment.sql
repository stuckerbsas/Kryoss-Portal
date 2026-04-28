-- Diagnostic: Cloud Assessment tenant + scan status
-- Run against KryossDb to find why a tenant returns no data

-- 1. All M365 tenants with org name
SELECT
    t.id, t.tenant_id, t.tenant_name, t.status,
    t.last_scan_at, t.consent_granted_at, t.client_id,
    o.name AS org_name
FROM m365_tenants t
JOIN organizations o ON o.id = t.organization_id
ORDER BY o.name;

-- 2. Latest scan per org
SELECT
    s.id AS scan_id, s.organization_id, o.name AS org_name,
    s.status, s.started_at, s.completed_at,
    s.overall_score, s.area_scores, s.verdict,
    s.pipeline_status
FROM cloud_assessment_scans s
JOIN organizations o ON o.id = s.organization_id
WHERE s.created_at = (
    SELECT MAX(s2.created_at) FROM cloud_assessment_scans s2 WHERE s2.organization_id = s.organization_id
)
ORDER BY o.name;

-- 3. Actlog errors for cloud assessment scans (last 50)
SELECT TOP 50
    a.severity, a.module, a.action, a.message,
    a.entity_id, a.timestamp
FROM actlog a
WHERE a.module = 'cloud_assessment'
  AND a.severity IN ('ERROR', 'WARN')
ORDER BY a.timestamp DESC;

-- 4. Finding count per scan (latest per org)
SELECT
    s.id AS scan_id, o.name AS org_name, s.status,
    COUNT(f.id) AS finding_count,
    s.pipeline_status
FROM cloud_assessment_scans s
JOIN organizations o ON o.id = s.organization_id
LEFT JOIN cloud_assessment_findings f ON f.scan_id = s.id
WHERE s.created_at = (
    SELECT MAX(s2.created_at) FROM cloud_assessment_scans s2 WHERE s2.organization_id = s.organization_id
)
GROUP BY s.id, o.name, s.status, s.pipeline_status
ORDER BY o.name;

-- Migration 033: CA-7 Remediation Tracker — suggestions table
-- Tracks auto-detected status transitions (likely resolved / possible regression)
-- that surface as nudges in the portal remediation tracker.

CREATE TABLE cloud_assessment_suggestions (
    id              BIGINT IDENTITY(1,1) PRIMARY KEY,
    organization_id UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
    scan_id         UNIQUEIDENTIFIER NOT NULL REFERENCES cloud_assessment_scans(id) ON DELETE CASCADE,
    area            NVARCHAR(30) NOT NULL,
    service         NVARCHAR(30) NOT NULL,
    feature         NVARCHAR(200) NOT NULL,
    suggestion_type NVARCHAR(30) NOT NULL CHECK (suggestion_type IN ('likely_resolved','possible_regression')),
    created_at      DATETIME2 DEFAULT SYSUTCDATETIME(),
    dismissed_at    DATETIME2 NULL,
    dismissed_by    UNIQUEIDENTIFIER NULL
);

CREATE INDEX IX_ca_suggestions_org_scan ON cloud_assessment_suggestions (organization_id, scan_id);
CREATE INDEX IX_ca_suggestions_active ON cloud_assessment_suggestions (organization_id, dismissed_at) WHERE dismissed_at IS NULL;

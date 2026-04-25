-- 065: External exposure — consent + findings + banner fields
-- Consent on organizations
ALTER TABLE organizations ADD
    external_scan_consent bit NOT NULL DEFAULT 0,
    external_scan_consent_at datetime2 NULL,
    external_scan_consent_by uniqueidentifier NULL;

-- Add banner fields to existing external_scan_results
ALTER TABLE external_scan_results ADD
    service_name varchar(100) NULL,
    service_version varchar(100) NULL;

-- External scan findings (derived from scan results)
CREATE TABLE external_scan_findings (
    id bigint IDENTITY(1,1) PRIMARY KEY,
    scan_id uniqueidentifier NOT NULL,
    scan_result_id bigint NULL,
    severity varchar(20) NOT NULL,
    title varchar(200) NOT NULL,
    description varchar(1000) NULL,
    remediation varchar(1000) NULL,
    port int NULL,
    public_ip varchar(45) NULL,
    CONSTRAINT fk_ext_finding_scan FOREIGN KEY (scan_id) REFERENCES external_scans(id),
    CONSTRAINT fk_ext_finding_result FOREIGN KEY (scan_result_id) REFERENCES external_scan_results(id)
);
CREATE INDEX ix_ext_finding_scan ON external_scan_findings(scan_id, severity);

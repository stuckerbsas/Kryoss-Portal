-- 088: Expand cve_entries.description from NVARCHAR(1024) to NVARCHAR(MAX)
-- NVD CVE descriptions can exceed 1024 chars
IF COL_LENGTH('cve_entries', 'description') IS NOT NULL
    ALTER TABLE cve_entries ALTER COLUMN description NVARCHAR(MAX) NULL;

-- Also expand machine_cve_findings.description if needed
IF COL_LENGTH('machine_cve_findings', 'description') IS NOT NULL
    ALTER TABLE machine_cve_findings ALTER COLUMN description NVARCHAR(MAX) NULL;

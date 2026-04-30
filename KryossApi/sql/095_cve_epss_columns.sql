-- 095: Add EPSS (Exploit Prediction Scoring System) columns to cve_entries
-- EPSS score = probability of exploitation in next 30 days (0.0 - 1.0)
-- EPSS percentile = relative rank among all CVEs (0.0 - 1.0)

ALTER TABLE cve_entries ADD epss_score DECIMAL(5,4) NULL;
ALTER TABLE cve_entries ADD epss_percentile DECIMAL(5,4) NULL;

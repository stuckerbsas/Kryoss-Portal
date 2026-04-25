-- seed_038_industry_benchmarks.sql
-- CA-11: Initial industry benchmark seed. Values are educated estimates
-- from MSP field experience + CIS/NIST implementation rate studies.
-- Refine quarterly via RefreshGlobalAggregatesAsync feedback.

DELETE FROM cloud_assessment_industry_benchmarks WHERE source = 'Kryoss industry seed 2026-04';
GO

-- ===== HEALTHCARE (regulated, high baseline) =====
INSERT INTO cloud_assessment_industry_benchmarks (industry_code, metric_key, baseline_value, percentile_25, percentile_50, percentile_75, sample_size, source) VALUES
('healthcare', 'area.identity', 3.5, 2.8, 3.5, 4.2, 100, 'Kryoss industry seed 2026-04'),
('healthcare', 'area.endpoint', 3.6, 2.9, 3.6, 4.3, 100, 'Kryoss industry seed 2026-04'),
('healthcare', 'area.data', 3.8, 3.0, 3.8, 4.5, 100, 'Kryoss industry seed 2026-04'),
('healthcare', 'area.productivity', 3.0, 2.2, 3.0, 3.8, 100, 'Kryoss industry seed 2026-04'),
('healthcare', 'area.azure', 3.2, 2.5, 3.2, 4.0, 100, 'Kryoss industry seed 2026-04'),
('healthcare', 'area.powerbi', 2.8, 2.0, 2.8, 3.6, 100, 'Kryoss industry seed 2026-04'),
('healthcare', 'overall_score', 3.4, 2.7, 3.4, 4.1, 100, 'Kryoss industry seed 2026-04'),
('healthcare', 'framework.HIPAA', 72.0, 58.0, 72.0, 85.0, 100, 'Kryoss industry seed 2026-04'),
('healthcare', 'framework.NIST_CSF', 65.0, 52.0, 65.0, 78.0, 100, 'Kryoss industry seed 2026-04'),
('healthcare', 'framework.ISO27001', 60.0, 47.0, 60.0, 74.0, 100, 'Kryoss industry seed 2026-04'),
('healthcare', 'framework.SOC2', 58.0, 45.0, 58.0, 72.0, 100, 'Kryoss industry seed 2026-04'),
('healthcare', 'framework.CIS', 68.0, 55.0, 68.0, 80.0, 100, 'Kryoss industry seed 2026-04'),
('healthcare', 'metric.mfa_registration_pct', 78.0, 65.0, 78.0, 92.0, 100, 'Kryoss industry seed 2026-04'),
('healthcare', 'metric.ca_compat_score_pct', 72.0, 58.0, 72.0, 85.0, 100, 'Kryoss industry seed 2026-04'),
('healthcare', 'metric.secure_score_pct', 62.0, 48.0, 62.0, 76.0, 100, 'Kryoss industry seed 2026-04'),
('healthcare', 'metric.devices_compliant_pct', 68.0, 55.0, 68.0, 82.0, 100, 'Kryoss industry seed 2026-04'),
('healthcare', 'metric.label_coverage_pct', 55.0, 38.0, 55.0, 72.0, 100, 'Kryoss industry seed 2026-04'),
('healthcare', 'metric.overshared_pct', 18.0, 28.0, 18.0, 10.0, 100, 'Kryoss industry seed 2026-04'),
('healthcare', 'metric.external_user_risk_count', 12.0, 25.0, 12.0, 5.0, 100, 'Kryoss industry seed 2026-04'),
('healthcare', 'metric.azure_secure_score_pct', 58.0, 45.0, 58.0, 72.0, 100, 'Kryoss industry seed 2026-04'),
('healthcare', 'metric.mail_domains_strict_dmarc_pct', 62.0, 42.0, 62.0, 82.0, 100, 'Kryoss industry seed 2026-04'),
('healthcare', 'metric.copilot_adoption_pct', 22.0, 10.0, 22.0, 38.0, 100, 'Kryoss industry seed 2026-04'),
('healthcare', 'metric.license_waste_pct', 18.0, 28.0, 18.0, 10.0, 100, 'Kryoss industry seed 2026-04');
GO

-- ===== FINANCE (heaviest regulation, highest expected) =====
INSERT INTO cloud_assessment_industry_benchmarks (industry_code, metric_key, baseline_value, percentile_25, percentile_50, percentile_75, sample_size, source) VALUES
('finance', 'area.identity', 4.0, 3.2, 4.0, 4.6, 100, 'Kryoss industry seed 2026-04'),
('finance', 'area.endpoint', 3.9, 3.1, 3.9, 4.5, 100, 'Kryoss industry seed 2026-04'),
('finance', 'area.data', 4.1, 3.5, 4.1, 4.7, 100, 'Kryoss industry seed 2026-04'),
('finance', 'area.productivity', 3.3, 2.5, 3.3, 4.0, 100, 'Kryoss industry seed 2026-04'),
('finance', 'area.azure', 3.6, 2.8, 3.6, 4.3, 100, 'Kryoss industry seed 2026-04'),
('finance', 'area.powerbi', 3.2, 2.4, 3.2, 4.0, 100, 'Kryoss industry seed 2026-04'),
('finance', 'overall_score', 3.8, 3.0, 3.8, 4.4, 100, 'Kryoss industry seed 2026-04'),
('finance', 'framework.PCI_DSS', 81.0, 68.0, 81.0, 92.0, 100, 'Kryoss industry seed 2026-04'),
('finance', 'framework.SOC2', 76.0, 62.0, 76.0, 88.0, 100, 'Kryoss industry seed 2026-04'),
('finance', 'framework.NIST_CSF', 74.0, 60.0, 74.0, 86.0, 100, 'Kryoss industry seed 2026-04'),
('finance', 'framework.ISO27001', 72.0, 58.0, 72.0, 85.0, 100, 'Kryoss industry seed 2026-04'),
('finance', 'framework.CIS', 76.0, 62.0, 76.0, 88.0, 100, 'Kryoss industry seed 2026-04'),
('finance', 'metric.mfa_registration_pct', 88.0, 75.0, 88.0, 96.0, 100, 'Kryoss industry seed 2026-04'),
('finance', 'metric.ca_compat_score_pct', 80.0, 68.0, 80.0, 92.0, 100, 'Kryoss industry seed 2026-04'),
('finance', 'metric.secure_score_pct', 72.0, 58.0, 72.0, 84.0, 100, 'Kryoss industry seed 2026-04'),
('finance', 'metric.devices_compliant_pct', 78.0, 65.0, 78.0, 90.0, 100, 'Kryoss industry seed 2026-04'),
('finance', 'metric.label_coverage_pct', 65.0, 48.0, 65.0, 82.0, 100, 'Kryoss industry seed 2026-04'),
('finance', 'metric.overshared_pct', 12.0, 20.0, 12.0, 6.0, 100, 'Kryoss industry seed 2026-04'),
('finance', 'metric.azure_secure_score_pct', 68.0, 55.0, 68.0, 82.0, 100, 'Kryoss industry seed 2026-04'),
('finance', 'metric.mail_domains_strict_dmarc_pct', 72.0, 55.0, 72.0, 88.0, 100, 'Kryoss industry seed 2026-04');
GO

-- ===== MANUFACTURING (OT heavy, lower cloud maturity) =====
INSERT INTO cloud_assessment_industry_benchmarks (industry_code, metric_key, baseline_value, percentile_25, percentile_50, percentile_75, sample_size, source) VALUES
('manufacturing', 'area.identity', 2.6, 1.9, 2.6, 3.4, 100, 'Kryoss industry seed 2026-04'),
('manufacturing', 'area.endpoint', 2.9, 2.2, 2.9, 3.6, 100, 'Kryoss industry seed 2026-04'),
('manufacturing', 'area.data', 2.4, 1.7, 2.4, 3.2, 100, 'Kryoss industry seed 2026-04'),
('manufacturing', 'area.productivity', 2.6, 1.9, 2.6, 3.4, 100, 'Kryoss industry seed 2026-04'),
('manufacturing', 'area.azure', 2.5, 1.8, 2.5, 3.3, 100, 'Kryoss industry seed 2026-04'),
('manufacturing', 'area.powerbi', 2.2, 1.5, 2.2, 3.0, 100, 'Kryoss industry seed 2026-04'),
('manufacturing', 'overall_score', 2.6, 1.9, 2.6, 3.3, 100, 'Kryoss industry seed 2026-04'),
('manufacturing', 'framework.NIST_CSF', 48.0, 35.0, 48.0, 62.0, 100, 'Kryoss industry seed 2026-04'),
('manufacturing', 'framework.ISO27001', 44.0, 32.0, 44.0, 58.0, 100, 'Kryoss industry seed 2026-04'),
('manufacturing', 'framework.CIS', 52.0, 38.0, 52.0, 66.0, 100, 'Kryoss industry seed 2026-04'),
('manufacturing', 'framework.CMMC_L2', 42.0, 28.0, 42.0, 58.0, 100, 'Kryoss industry seed 2026-04'),
('manufacturing', 'metric.mfa_registration_pct', 58.0, 42.0, 58.0, 75.0, 100, 'Kryoss industry seed 2026-04'),
('manufacturing', 'metric.ca_compat_score_pct', 52.0, 38.0, 52.0, 68.0, 100, 'Kryoss industry seed 2026-04'),
('manufacturing', 'metric.secure_score_pct', 48.0, 35.0, 48.0, 62.0, 100, 'Kryoss industry seed 2026-04'),
('manufacturing', 'metric.devices_compliant_pct', 55.0, 40.0, 55.0, 70.0, 100, 'Kryoss industry seed 2026-04'),
('manufacturing', 'metric.label_coverage_pct', 32.0, 18.0, 32.0, 48.0, 100, 'Kryoss industry seed 2026-04'),
('manufacturing', 'metric.azure_secure_score_pct', 45.0, 32.0, 45.0, 60.0, 100, 'Kryoss industry seed 2026-04'),
('manufacturing', 'metric.mail_domains_strict_dmarc_pct', 38.0, 22.0, 38.0, 58.0, 100, 'Kryoss industry seed 2026-04');
GO

-- ===== PROFESSIONAL SERVICES (SMB baseline) =====
INSERT INTO cloud_assessment_industry_benchmarks (industry_code, metric_key, baseline_value, percentile_25, percentile_50, percentile_75, sample_size, source) VALUES
('professional_services', 'area.identity', 2.8, 2.0, 2.8, 3.6, 100, 'Kryoss industry seed 2026-04'),
('professional_services', 'area.endpoint', 2.9, 2.1, 2.9, 3.7, 100, 'Kryoss industry seed 2026-04'),
('professional_services', 'area.data', 2.5, 1.8, 2.5, 3.4, 100, 'Kryoss industry seed 2026-04'),
('professional_services', 'area.productivity', 2.8, 2.0, 2.8, 3.6, 100, 'Kryoss industry seed 2026-04'),
('professional_services', 'area.azure', 2.6, 1.9, 2.6, 3.4, 100, 'Kryoss industry seed 2026-04'),
('professional_services', 'area.powerbi', 2.4, 1.7, 2.4, 3.2, 100, 'Kryoss industry seed 2026-04'),
('professional_services', 'overall_score', 2.7, 2.0, 2.7, 3.5, 100, 'Kryoss industry seed 2026-04'),
('professional_services', 'framework.NIST_CSF', 52.0, 38.0, 52.0, 66.0, 100, 'Kryoss industry seed 2026-04'),
('professional_services', 'framework.ISO27001', 48.0, 35.0, 48.0, 62.0, 100, 'Kryoss industry seed 2026-04'),
('professional_services', 'framework.SOC2', 50.0, 36.0, 50.0, 64.0, 100, 'Kryoss industry seed 2026-04'),
('professional_services', 'framework.CIS', 55.0, 42.0, 55.0, 68.0, 100, 'Kryoss industry seed 2026-04'),
('professional_services', 'metric.mfa_registration_pct', 62.0, 45.0, 62.0, 78.0, 100, 'Kryoss industry seed 2026-04'),
('professional_services', 'metric.ca_compat_score_pct', 58.0, 42.0, 58.0, 72.0, 100, 'Kryoss industry seed 2026-04'),
('professional_services', 'metric.secure_score_pct', 52.0, 38.0, 52.0, 66.0, 100, 'Kryoss industry seed 2026-04'),
('professional_services', 'metric.mail_domains_strict_dmarc_pct', 42.0, 26.0, 42.0, 60.0, 100, 'Kryoss industry seed 2026-04'),
('professional_services', 'metric.copilot_adoption_pct', 28.0, 14.0, 28.0, 45.0, 100, 'Kryoss industry seed 2026-04');
GO

-- ===== LEGAL (confidentiality-focused, mid-high baseline) =====
INSERT INTO cloud_assessment_industry_benchmarks (industry_code, metric_key, baseline_value, percentile_25, percentile_50, percentile_75, sample_size, source) VALUES
('legal', 'area.identity', 3.2, 2.4, 3.2, 4.0, 100, 'Kryoss industry seed 2026-04'),
('legal', 'area.endpoint', 3.1, 2.3, 3.1, 3.9, 100, 'Kryoss industry seed 2026-04'),
('legal', 'area.data', 3.5, 2.7, 3.5, 4.2, 100, 'Kryoss industry seed 2026-04'),
('legal', 'area.productivity', 2.8, 2.0, 2.8, 3.6, 100, 'Kryoss industry seed 2026-04'),
('legal', 'area.azure', 2.8, 2.0, 2.8, 3.6, 100, 'Kryoss industry seed 2026-04'),
('legal', 'overall_score', 3.1, 2.3, 3.1, 3.9, 100, 'Kryoss industry seed 2026-04'),
('legal', 'framework.ISO27001', 62.0, 48.0, 62.0, 76.0, 100, 'Kryoss industry seed 2026-04'),
('legal', 'framework.SOC2', 60.0, 46.0, 60.0, 74.0, 100, 'Kryoss industry seed 2026-04'),
('legal', 'metric.mfa_registration_pct', 72.0, 58.0, 72.0, 86.0, 100, 'Kryoss industry seed 2026-04'),
('legal', 'metric.label_coverage_pct', 52.0, 36.0, 52.0, 68.0, 100, 'Kryoss industry seed 2026-04');
GO

-- ===== RETAIL (PCI if card payments, mixed) =====
INSERT INTO cloud_assessment_industry_benchmarks (industry_code, metric_key, baseline_value, percentile_25, percentile_50, percentile_75, sample_size, source) VALUES
('retail', 'area.identity', 2.7, 1.9, 2.7, 3.5, 100, 'Kryoss industry seed 2026-04'),
('retail', 'area.endpoint', 2.9, 2.1, 2.9, 3.7, 100, 'Kryoss industry seed 2026-04'),
('retail', 'area.data', 2.6, 1.9, 2.6, 3.4, 100, 'Kryoss industry seed 2026-04'),
('retail', 'area.productivity', 2.5, 1.8, 2.5, 3.3, 100, 'Kryoss industry seed 2026-04'),
('retail', 'overall_score', 2.7, 1.9, 2.7, 3.5, 100, 'Kryoss industry seed 2026-04'),
('retail', 'framework.PCI_DSS', 62.0, 48.0, 62.0, 76.0, 100, 'Kryoss industry seed 2026-04'),
('retail', 'framework.CIS', 52.0, 38.0, 52.0, 66.0, 100, 'Kryoss industry seed 2026-04'),
('retail', 'metric.mfa_registration_pct', 55.0, 40.0, 55.0, 70.0, 100, 'Kryoss industry seed 2026-04'),
('retail', 'metric.secure_score_pct', 48.0, 34.0, 48.0, 62.0, 100, 'Kryoss industry seed 2026-04');
GO

-- ===== EDUCATION (budget-constrained, compliance-varied) =====
INSERT INTO cloud_assessment_industry_benchmarks (industry_code, metric_key, baseline_value, percentile_25, percentile_50, percentile_75, sample_size, source) VALUES
('education', 'area.identity', 2.5, 1.8, 2.5, 3.3, 100, 'Kryoss industry seed 2026-04'),
('education', 'area.endpoint', 2.6, 1.9, 2.6, 3.4, 100, 'Kryoss industry seed 2026-04'),
('education', 'area.data', 2.4, 1.7, 2.4, 3.2, 100, 'Kryoss industry seed 2026-04'),
('education', 'area.productivity', 2.6, 1.9, 2.6, 3.4, 100, 'Kryoss industry seed 2026-04'),
('education', 'overall_score', 2.5, 1.8, 2.5, 3.3, 100, 'Kryoss industry seed 2026-04'),
('education', 'framework.NIST_CSF', 45.0, 32.0, 45.0, 58.0, 100, 'Kryoss industry seed 2026-04'),
('education', 'metric.mfa_registration_pct', 52.0, 38.0, 52.0, 68.0, 100, 'Kryoss industry seed 2026-04'),
('education', 'metric.secure_score_pct', 45.0, 32.0, 45.0, 58.0, 100, 'Kryoss industry seed 2026-04');
GO

-- ===== GOVERNMENT (FedRAMP/StateRAMP, high baseline) =====
INSERT INTO cloud_assessment_industry_benchmarks (industry_code, metric_key, baseline_value, percentile_25, percentile_50, percentile_75, sample_size, source) VALUES
('government', 'area.identity', 3.6, 2.8, 3.6, 4.3, 100, 'Kryoss industry seed 2026-04'),
('government', 'area.endpoint', 3.7, 2.9, 3.7, 4.4, 100, 'Kryoss industry seed 2026-04'),
('government', 'area.data', 3.8, 3.0, 3.8, 4.5, 100, 'Kryoss industry seed 2026-04'),
('government', 'area.productivity', 3.1, 2.3, 3.1, 3.9, 100, 'Kryoss industry seed 2026-04'),
('government', 'area.azure', 3.5, 2.7, 3.5, 4.2, 100, 'Kryoss industry seed 2026-04'),
('government', 'overall_score', 3.6, 2.8, 3.6, 4.3, 100, 'Kryoss industry seed 2026-04'),
('government', 'framework.NIST_CSF', 72.0, 58.0, 72.0, 85.0, 100, 'Kryoss industry seed 2026-04'),
('government', 'framework.CMMC_L2', 68.0, 54.0, 68.0, 82.0, 100, 'Kryoss industry seed 2026-04');
GO

-- ===== NONPROFIT (budget limits, low baseline) =====
INSERT INTO cloud_assessment_industry_benchmarks (industry_code, metric_key, baseline_value, percentile_25, percentile_50, percentile_75, sample_size, source) VALUES
('nonprofit', 'area.identity', 2.3, 1.6, 2.3, 3.1, 100, 'Kryoss industry seed 2026-04'),
('nonprofit', 'area.endpoint', 2.4, 1.7, 2.4, 3.2, 100, 'Kryoss industry seed 2026-04'),
('nonprofit', 'area.data', 2.2, 1.5, 2.2, 3.0, 100, 'Kryoss industry seed 2026-04'),
('nonprofit', 'area.productivity', 2.5, 1.8, 2.5, 3.3, 100, 'Kryoss industry seed 2026-04'),
('nonprofit', 'overall_score', 2.3, 1.6, 2.3, 3.1, 100, 'Kryoss industry seed 2026-04'),
('nonprofit', 'metric.mfa_registration_pct', 48.0, 32.0, 48.0, 65.0, 100, 'Kryoss industry seed 2026-04'),
('nonprofit', 'metric.secure_score_pct', 42.0, 28.0, 42.0, 56.0, 100, 'Kryoss industry seed 2026-04');
GO

-- ===== TECHNOLOGY (security-aware, high baseline) =====
INSERT INTO cloud_assessment_industry_benchmarks (industry_code, metric_key, baseline_value, percentile_25, percentile_50, percentile_75, sample_size, source) VALUES
('technology', 'area.identity', 3.7, 2.9, 3.7, 4.4, 100, 'Kryoss industry seed 2026-04'),
('technology', 'area.endpoint', 3.5, 2.7, 3.5, 4.2, 100, 'Kryoss industry seed 2026-04'),
('technology', 'area.data', 3.4, 2.6, 3.4, 4.1, 100, 'Kryoss industry seed 2026-04'),
('technology', 'area.productivity', 3.5, 2.7, 3.5, 4.2, 100, 'Kryoss industry seed 2026-04'),
('technology', 'area.azure', 3.8, 3.0, 3.8, 4.4, 100, 'Kryoss industry seed 2026-04'),
('technology', 'area.powerbi', 3.3, 2.5, 3.3, 4.0, 100, 'Kryoss industry seed 2026-04'),
('technology', 'overall_score', 3.5, 2.7, 3.5, 4.2, 100, 'Kryoss industry seed 2026-04'),
('technology', 'framework.SOC2', 72.0, 58.0, 72.0, 85.0, 100, 'Kryoss industry seed 2026-04'),
('technology', 'framework.ISO27001', 68.0, 54.0, 68.0, 82.0, 100, 'Kryoss industry seed 2026-04'),
('technology', 'metric.mfa_registration_pct', 82.0, 70.0, 82.0, 94.0, 100, 'Kryoss industry seed 2026-04'),
('technology', 'metric.azure_secure_score_pct', 68.0, 55.0, 68.0, 82.0, 100, 'Kryoss industry seed 2026-04');
GO

-- ===== CONSTRUCTION =====
INSERT INTO cloud_assessment_industry_benchmarks (industry_code, metric_key, baseline_value, percentile_25, percentile_50, percentile_75, sample_size, source) VALUES
('construction', 'area.identity', 2.2, 1.5, 2.2, 3.0, 100, 'Kryoss industry seed 2026-04'),
('construction', 'area.endpoint', 2.5, 1.8, 2.5, 3.3, 100, 'Kryoss industry seed 2026-04'),
('construction', 'area.data', 2.0, 1.3, 2.0, 2.8, 100, 'Kryoss industry seed 2026-04'),
('construction', 'overall_score', 2.3, 1.6, 2.3, 3.1, 100, 'Kryoss industry seed 2026-04'),
('construction', 'metric.mfa_registration_pct', 42.0, 28.0, 42.0, 58.0, 100, 'Kryoss industry seed 2026-04');
GO

-- ===== TRANSPORTATION =====
INSERT INTO cloud_assessment_industry_benchmarks (industry_code, metric_key, baseline_value, percentile_25, percentile_50, percentile_75, sample_size, source) VALUES
('transportation', 'area.identity', 2.5, 1.8, 2.5, 3.3, 100, 'Kryoss industry seed 2026-04'),
('transportation', 'area.endpoint', 2.7, 2.0, 2.7, 3.5, 100, 'Kryoss industry seed 2026-04'),
('transportation', 'area.data', 2.3, 1.6, 2.3, 3.1, 100, 'Kryoss industry seed 2026-04'),
('transportation', 'overall_score', 2.5, 1.8, 2.5, 3.3, 100, 'Kryoss industry seed 2026-04');
GO

-- ===== HOSPITALITY (PCI exposure, general low baseline) =====
INSERT INTO cloud_assessment_industry_benchmarks (industry_code, metric_key, baseline_value, percentile_25, percentile_50, percentile_75, sample_size, source) VALUES
('hospitality', 'area.identity', 2.4, 1.7, 2.4, 3.2, 100, 'Kryoss industry seed 2026-04'),
('hospitality', 'area.endpoint', 2.6, 1.9, 2.6, 3.4, 100, 'Kryoss industry seed 2026-04'),
('hospitality', 'area.data', 2.2, 1.5, 2.2, 3.0, 100, 'Kryoss industry seed 2026-04'),
('hospitality', 'overall_score', 2.4, 1.7, 2.4, 3.2, 100, 'Kryoss industry seed 2026-04'),
('hospitality', 'framework.PCI_DSS', 55.0, 40.0, 55.0, 70.0, 100, 'Kryoss industry seed 2026-04');
GO

-- ===== REAL ESTATE =====
INSERT INTO cloud_assessment_industry_benchmarks (industry_code, metric_key, baseline_value, percentile_25, percentile_50, percentile_75, sample_size, source) VALUES
('real_estate', 'area.identity', 2.5, 1.8, 2.5, 3.3, 100, 'Kryoss industry seed 2026-04'),
('real_estate', 'area.endpoint', 2.6, 1.9, 2.6, 3.4, 100, 'Kryoss industry seed 2026-04'),
('real_estate', 'area.data', 2.4, 1.7, 2.4, 3.2, 100, 'Kryoss industry seed 2026-04'),
('real_estate', 'overall_score', 2.5, 1.8, 2.5, 3.3, 100, 'Kryoss industry seed 2026-04');
GO

-- ===== OTHER (catch-all SMB baseline) =====
INSERT INTO cloud_assessment_industry_benchmarks (industry_code, metric_key, baseline_value, percentile_25, percentile_50, percentile_75, sample_size, source) VALUES
('other', 'area.identity', 2.6, 1.9, 2.6, 3.4, 100, 'Kryoss industry seed 2026-04'),
('other', 'area.endpoint', 2.7, 2.0, 2.7, 3.5, 100, 'Kryoss industry seed 2026-04'),
('other', 'area.data', 2.5, 1.8, 2.5, 3.3, 100, 'Kryoss industry seed 2026-04'),
('other', 'area.productivity', 2.6, 1.9, 2.6, 3.4, 100, 'Kryoss industry seed 2026-04'),
('other', 'area.azure', 2.5, 1.8, 2.5, 3.3, 100, 'Kryoss industry seed 2026-04'),
('other', 'area.powerbi', 2.3, 1.6, 2.3, 3.1, 100, 'Kryoss industry seed 2026-04'),
('other', 'overall_score', 2.6, 1.9, 2.6, 3.4, 100, 'Kryoss industry seed 2026-04'),
('other', 'framework.NIST_CSF', 50.0, 36.0, 50.0, 64.0, 100, 'Kryoss industry seed 2026-04'),
('other', 'framework.CIS', 52.0, 38.0, 52.0, 66.0, 100, 'Kryoss industry seed 2026-04'),
('other', 'metric.mfa_registration_pct', 58.0, 42.0, 58.0, 74.0, 100, 'Kryoss industry seed 2026-04'),
('other', 'metric.secure_score_pct', 50.0, 36.0, 50.0, 64.0, 100, 'Kryoss industry seed 2026-04'),
('other', 'metric.mail_domains_strict_dmarc_pct', 42.0, 26.0, 42.0, 60.0, 100, 'Kryoss industry seed 2026-04');
GO

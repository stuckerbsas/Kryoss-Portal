namespace KryossApi.Services.CloudAssessment.Benchmarks;

public static class BenchmarkMetrics
{
    // Area scores (0-5 scale)
    public const string AreaIdentity = "area.identity";
    public const string AreaEndpoint = "area.endpoint";
    public const string AreaData = "area.data";
    public const string AreaProductivity = "area.productivity";
    public const string AreaAzure = "area.azure";
    public const string AreaCompliance = "area.compliance";
    public const string AreaPowerBi = "area.powerbi";

    public const string OverallScore = "overall_score";

    // Framework scores (0-100 pct)
    public const string FrameworkHipaa = "framework.HIPAA";
    public const string FrameworkIso27001 = "framework.ISO27001";
    public const string FrameworkNistCsf = "framework.NIST_CSF";
    public const string FrameworkSoc2 = "framework.SOC2";
    public const string FrameworkPciDss = "framework.PCI_DSS";
    public const string FrameworkCis = "framework.CIS";
    public const string FrameworkCmmcL2 = "framework.CMMC_L2";

    // Operational metrics (0-100 pct unless noted)
    public const string MetricMfaRegistrationPct = "metric.mfa_registration_pct";
    public const string MetricCaCompatScorePct = "metric.ca_compat_score_pct";
    public const string MetricCopilotAdoptionPct = "metric.copilot_adoption_pct";
    public const string MetricLicenseWastePct = "metric.license_waste_pct";
    public const string MetricSecureScorePct = "metric.secure_score_pct";
    public const string MetricDevicesCompliantPct = "metric.devices_compliant_pct";
    public const string MetricLabelCoveragePct = "metric.label_coverage_pct";
    public const string MetricOversharedPct = "metric.overshared_pct";
    public const string MetricExternalUserRiskCount = "metric.external_user_risk_count";
    public const string MetricAzureSecureScorePct = "metric.azure_secure_score_pct";
    public const string MetricMailDomainsStrictDmarcPct = "metric.mail_domains_strict_dmarc_pct";

    public static readonly string[] All =
    {
        AreaIdentity, AreaEndpoint, AreaData, AreaProductivity, AreaAzure, AreaCompliance, AreaPowerBi,
        OverallScore,
        FrameworkHipaa, FrameworkIso27001, FrameworkNistCsf, FrameworkSoc2, FrameworkPciDss, FrameworkCis, FrameworkCmmcL2,
        MetricMfaRegistrationPct, MetricCaCompatScorePct, MetricCopilotAdoptionPct, MetricLicenseWastePct,
        MetricSecureScorePct, MetricDevicesCompliantPct, MetricLabelCoveragePct, MetricOversharedPct,
        MetricExternalUserRiskCount, MetricAzureSecureScorePct, MetricMailDomainsStrictDmarcPct,
    };

    public static string Category(string metricKey)
    {
        if (metricKey.StartsWith("area.")) return "area";
        if (metricKey.StartsWith("framework.")) return "framework";
        if (metricKey.StartsWith("metric.")) return "metric";
        if (metricKey == OverallScore) return "overall";
        return "other";
    }

    public static string DisplayName(string metricKey) => metricKey switch
    {
        AreaIdentity => "Identity",
        AreaEndpoint => "Endpoint",
        AreaData => "Data",
        AreaProductivity => "Productivity",
        AreaAzure => "Azure",
        AreaCompliance => "Compliance",
        AreaPowerBi => "Power BI",
        OverallScore => "Overall score",
        FrameworkHipaa => "HIPAA",
        FrameworkIso27001 => "ISO 27001",
        FrameworkNistCsf => "NIST CSF",
        FrameworkSoc2 => "SOC 2",
        FrameworkPciDss => "PCI DSS",
        FrameworkCis => "CIS Controls",
        FrameworkCmmcL2 => "CMMC Level 2",
        MetricMfaRegistrationPct => "MFA registration",
        MetricCaCompatScorePct => "Conditional Access compatibility",
        MetricCopilotAdoptionPct => "Copilot adoption",
        MetricLicenseWastePct => "License waste",
        MetricSecureScorePct => "Secure Score",
        MetricDevicesCompliantPct => "Device compliance",
        MetricLabelCoveragePct => "Label coverage",
        MetricOversharedPct => "Oversharing ratio",
        MetricExternalUserRiskCount => "External user risks",
        MetricAzureSecureScorePct => "Azure Secure Score",
        MetricMailDomainsStrictDmarcPct => "Domains with strict DMARC",
        _ => metricKey,
    };

    /// <summary>Higher = better for most metrics; lower is better for waste, oversharing, external_user_risk.</summary>
    public static bool HigherIsBetter(string metricKey) => metricKey switch
    {
        MetricLicenseWastePct => false,
        MetricOversharedPct => false,
        MetricExternalUserRiskCount => false,
        _ => true,
    };
}

public static class IndustryCodes
{
    public const string Healthcare = "healthcare";
    public const string Finance = "finance";
    public const string Manufacturing = "manufacturing";
    public const string Retail = "retail";
    public const string ProfessionalServices = "professional_services";
    public const string Education = "education";
    public const string Government = "government";
    public const string Nonprofit = "nonprofit";
    public const string Technology = "technology";
    public const string Legal = "legal";
    public const string RealEstate = "real_estate";
    public const string Construction = "construction";
    public const string Transportation = "transportation";
    public const string Hospitality = "hospitality";
    public const string Other = "other";

    public static readonly (string Code, string Label, string Description)[] All =
    {
        (Healthcare, "Healthcare", "Hospitals, clinics, medical practices (NAICS 62)"),
        (Finance, "Finance", "Banks, insurance, investment (NAICS 52)"),
        (Manufacturing, "Manufacturing", "Industrial production, OT environments (NAICS 31-33)"),
        (Retail, "Retail", "Physical + e-commerce retail (NAICS 44-45)"),
        (ProfessionalServices, "Professional Services", "Consulting, accounting, admin (NAICS 54)"),
        (Education, "Education", "K-12, higher education, training (NAICS 61)"),
        (Government, "Government", "Federal, state, local (NAICS 92)"),
        (Nonprofit, "Nonprofit", "Non-profit organizations"),
        (Technology, "Technology", "Software, IT services (NAICS 5112/5415)"),
        (Legal, "Legal", "Law firms, legal services (NAICS 5411)"),
        (RealEstate, "Real Estate", "Property management, brokerage (NAICS 53)"),
        (Construction, "Construction", "General and specialty contractors (NAICS 23)"),
        (Transportation, "Transportation", "Freight, logistics, delivery (NAICS 48-49)"),
        (Hospitality, "Hospitality", "Hotels, restaurants, tourism (NAICS 72)"),
        (Other, "Other", "Fallback — not otherwise classified"),
    };
}

public static class EmployeeBands
{
    public const string B1To10 = "1-10";
    public const string B11To50 = "11-50";
    public const string B51To200 = "51-200";
    public const string B201To1000 = "201-1000";
    public const string B1000Plus = "1000+";

    public static readonly string[] All = { B1To10, B11To50, B51To200, B201To1000, B1000Plus };
}

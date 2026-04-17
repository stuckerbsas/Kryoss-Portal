using KryossApi.Services.CopilotReadiness.Pipelines;
using KryossApi.Services.CopilotReadiness.Recommendations;

namespace KryossApi.Services.CloudAssessment.Pipelines;

/// <summary>
/// Pre-computed Entra identity, security and governance metrics for the
/// Cloud Assessment (CA-1) identity pipeline.
///
/// Mirrors every property of <see cref="EntraInsights"/> (so existing
/// recommendation logic can be reused verbatim) and adds Cloud Assessment
/// specific fields covering service principal credential hygiene, B2B
/// cross-tenant trust posture, PIM activation auditing, access review
/// freshness, user lifecycle hygiene, BitLocker coverage, admin MFA
/// gaps and consent grant scope.
///
/// Built once per assessment run, shared across all recommendation
/// generators (Task 2 will add IdentityRecommendations.Generate(...)).
/// </summary>
public class IdentityInsights
{
    // ============================================================
    // Mirrored from EntraInsights — keep names identical so Entra
    // recommendation logic can be reused without modification.
    // ============================================================

    // --- Conditional Access ---
    public int CaPolicyTotal { get; set; }
    public int CaPolicyEnabled { get; set; }
    public int CaPolicyDisabled { get; set; }
    public int CaPolicyReportOnly { get; set; }
    public int CaPolicyMfaRequired { get; set; }
    public int CaPolicyCompliantDevice { get; set; }
    public int CaPolicyManagedDevice { get; set; }
    public int CaPolicyLegacyAuthBlocked { get; set; }
    public int CaPolicyAllApps { get; set; }
    public int CaPolicyTargetM365 { get; set; }
    public int CaPolicyLocationBased { get; set; }
    public int CaPolicyUserRiskBased { get; set; }
    public int CaPolicySignInRiskBased { get; set; }
    public double CaCompatScorePct { get; set; }

    // --- MFA & Authentication Methods ---
    public int TotalUsers { get; set; }
    public int MfaRegistered { get; set; }
    public int MfaCapable { get; set; }
    public double MfaRegistrationPct { get; set; }
    public int PasswordlessEnabled { get; set; }
    public double PasswordlessPct { get; set; }
    public Dictionary<string, int> AuthMethodBreakdown { get; set; } = new()
    {
        ["microsoftAuthenticator"] = 0,
        ["fido2"] = 0,
        ["windowsHello"] = 0,
        ["phone"] = 0,
        ["email"] = 0,
        ["softwareOath"] = 0,
        ["temporaryAccessPass"] = 0,
    };

    // --- Identity Protection: Risky Users ---
    public int RiskyUsersHigh { get; set; }
    public int RiskyUsersMedium { get; set; }
    public int RiskyUsersLow { get; set; }
    public int ConfirmedCompromised { get; set; }
    public int AtRisk { get; set; }
    public int Remediated { get; set; }

    // --- Identity Protection: Risk Detections ---
    public int RiskDetectionsTotal { get; set; }
    public int RiskDetectionsHigh { get; set; }

    // --- Risk-based CA policy existence flags ---
    public bool UserRiskPolicyExists { get; set; }
    public bool SignInRiskPolicyExists { get; set; }

    // --- PIM ---
    public int PermanentGlobalAdmins { get; set; }
    public int PermanentAssignmentsTotal { get; set; }
    public int EligibleAssignments { get; set; }
    public int TimeBoundAssignments { get; set; }

    // --- Access Reviews ---
    public int AccessReviewTotal { get; set; }
    public int AccessReviewActive { get; set; }
    public int AccessReviewGroupScope { get; set; }
    public int AccessReviewRoleScope { get; set; }
    public int AccessReviewGuestScope { get; set; }
    public int AccessReviewRecurring { get; set; }

    // --- Devices ---
    public int DevicesCompliant { get; set; }
    public int DevicesNonCompliant { get; set; }
    public int DevicesInGracePeriod { get; set; }
    public int DevicesCorporate { get; set; }
    public int DevicesPersonal { get; set; }
    public int DevicesTotalManaged { get; set; }
    public int CompliancePoliciesCount { get; set; }
    public bool CaRequiresCompliance { get; set; }

    // --- Group-based Licensing ---
    public int GroupsWithLicenses { get; set; }
    public int GroupsWithErrors { get; set; }
    public int CopilotLicenseGroups { get; set; }
    public int DynamicGroups { get; set; }

    // --- B2B ---
    public int GuestUsersTotal { get; set; }
    public int GuestUsersWithLicenses { get; set; }
    public string GuestInviteSetting { get; set; } = "Unknown";
    public bool CrossTenantConfigured { get; set; }
    public int PartnerConfigurations { get; set; }

    // --- OAuth / App Consent ---
    public int TotalApps { get; set; }
    public int UnverifiedPublishers { get; set; }
    public int HighRiskApps { get; set; }
    public int OverPrivilegedApps { get; set; }
    public bool UserConsentAllowed { get; set; }
    public bool AdminConsentRequired { get; set; }
    public int AppsWithGraphAccess { get; set; }
    public int AppsWithMailAccess { get; set; }
    public int AppsWithFilesAccess { get; set; }
    public int PermissionGrantCount { get; set; }

    // --- Sign-in Logs ---
    public int LegacyAuthSignIns { get; set; }
    public int MfaRequiredSignIns { get; set; }
    public int MfaSuccessSignIns { get; set; }
    public int FailedSignIns { get; set; }
    public int RiskySignInsHigh { get; set; }
    public int RiskySignInsMedium { get; set; }

    // --- Global Secure Access (nullable = not attempted / no license) ---
    public string? GsaStatus { get; set; }
    public int? FilteringPolicies { get; set; }
    public int? FqdnRules { get; set; }
    public int? WebCategoryRules { get; set; }
    public bool? M365ForwardingEnabled { get; set; }
    public bool? InternetForwardingEnabled { get; set; }
    public int? ForwardingProfilesCount { get; set; }

    // --- Private Access (nullable) ---
    public string? PrivateAccessStatus { get; set; }
    public int? PrivateAccessConnectors { get; set; }
    public int? PrivateAccessActiveConnectors { get; set; }
    public int? PrivateAccessAppSegments { get; set; }

    // ============================================================
    // Cloud-Assessment-only extensions — broader MSP identity audit
    // ============================================================

    // --- Service Principal credential hygiene ---
    public int ServicePrincipalCredentialsExpiring30d { get; set; }
    public int ServicePrincipalCredentialsExpired { get; set; }
    public int ServicePrincipalCredentialsOlderThan2Years { get; set; }

    // --- B2B cross-tenant trust posture ---
    public int B2bAllowedDomains { get; set; }
    public int B2bBlockedDomains { get; set; }
    public string? B2bInboundTrustMfa { get; set; } // "accepted" | "not_accepted" | null
    public string? B2bInboundTrustCompliantDevice { get; set; }

    // --- PIM activation auditing & policy enforcement ---
    public int PimAuditEntriesLast30d { get; set; }
    public int PimRolesWithoutMfaRequirement { get; set; }
    public int PimRolesWithoutJustificationRequirement { get; set; }

    // --- Access review freshness ---
    public int AccessReviewsCompletedLast90d { get; set; }
    public int AccessReviewsOverdue { get; set; }

    // --- User lifecycle hygiene ---
    public int UsersPasswordNeverExpires { get; set; }
    public int UsersCreatedLast30d { get; set; }
    public int UsersNoSignIn90d { get; set; }

    // --- Device coverage gaps ---
    public int DevicesWithoutCompliancePolicy { get; set; }
    public int BitlockerCoveragePct { get; set; }

    // --- Admin MFA gap ---
    public int AdminsWithoutMfa { get; set; }

    // --- Consent surface ---
    public int ConsentedPartnerApps { get; set; }
    public int OAuthConsentGrantsToAllUsers { get; set; }
}

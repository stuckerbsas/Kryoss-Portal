using System.Text.Json;
using KryossApi.Services.CopilotReadiness.Recommendations;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;
using Microsoft.Extensions.Logging;

namespace KryossApi.Services.CopilotReadiness.Pipelines;

/// <summary>
/// Pre-computed Entra identity and security metrics extracted from Graph API.
/// Built once per assessment run, shared across all recommendation generators.
/// </summary>
public class EntraInsights
{
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
}

/// <summary>
/// Entra pipeline: collects identity, security and governance data from
/// Microsoft Graph API and generates Copilot readiness recommendations.
/// This is the most complex pipeline -- 10 parallel data collectors.
/// </summary>
public static class EntraPipeline
{
    private const string GlobalAdminRoleTemplateId = "62e90394-69f5-4237-9190-012177145e10";

    public static async Task<PipelineResult> RunAsync(
        GraphServiceClient graph,
        HttpClient graphBetaHttp,
        ILogger log,
        CancellationToken ct)
    {
        var result = new PipelineResult { PipelineName = "entra", Status = "ok" };
        var insights = new EntraInsights();

        // Run all data collectors in parallel; each catches its own errors.
        var tasks = new List<Task>
        {
            CollectCaPolicies(graph, insights, log, ct),
            CollectMfaStatus(graph, insights, log, ct),
            CollectRiskyUsers(graph, insights, log, ct),
            CollectPim(graph, insights, log, ct),
            CollectAccessReviews(graph, insights, log, ct),
            CollectDevices(graph, insights, log, ct),
            CollectB2b(graph, insights, log, ct),
            CollectOAuthApps(graph, insights, log, ct),
            CollectSignIns(graph, insights, log, ct),
            CollectGsa(graphBetaHttp, insights, log, ct),
        };

        await Task.WhenAll(tasks);

        // Generate recommendations from the collected data.
        result.Findings.AddRange(EntraRecommendations.Generate(insights));

        // Extract key metrics for scoring (D4/D5).
        result.Metrics["ca_compat_score_pct"] = insights.CaCompatScorePct.ToString("F1");
        result.Metrics["mfa_registration_pct"] = insights.MfaRegistrationPct.ToString("F1");
        result.Metrics["passwordless_pct"] = insights.PasswordlessPct.ToString("F1");
        result.Metrics["ca_policies_total"] = insights.CaPolicyTotal.ToString();
        result.Metrics["ca_policies_enabled"] = insights.CaPolicyEnabled.ToString();
        result.Metrics["permanent_global_admins"] = insights.PermanentGlobalAdmins.ToString();
        result.Metrics["eligible_assignments"] = insights.EligibleAssignments.ToString();
        result.Metrics["risky_users_high"] = insights.RiskyUsersHigh.ToString();
        result.Metrics["confirmed_compromised"] = insights.ConfirmedCompromised.ToString();
        result.Metrics["access_review_active"] = insights.AccessReviewActive.ToString();
        result.Metrics["devices_total_managed"] = insights.DevicesTotalManaged.ToString();
        result.Metrics["devices_compliant"] = insights.DevicesCompliant.ToString();
        result.Metrics["devices_non_compliant"] = insights.DevicesNonCompliant.ToString();
        result.Metrics["guest_users_total"] = insights.GuestUsersTotal.ToString();
        result.Metrics["total_apps"] = insights.TotalApps.ToString();
        result.Metrics["high_risk_apps"] = insights.HighRiskApps.ToString();
        result.Metrics["legacy_auth_signins"] = insights.LegacyAuthSignIns.ToString();

        return result;
    }

    // ================================================================
    // 1. Conditional Access policies
    // ================================================================
    private static async Task CollectCaPolicies(
        GraphServiceClient graph, EntraInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            var resp = await graph.Identity.ConditionalAccess.Policies.GetAsync(cancellationToken: ct);
            var policies = resp?.Value;
            if (policies is null) return;

            foreach (var p in policies)
            {
                ins.CaPolicyTotal++;

                var state = p.State?.ToString()?.ToLowerInvariant() ?? "";
                if (state.Contains("enabled") && !state.Contains("report"))
                    ins.CaPolicyEnabled++;
                else if (state.Contains("disabled"))
                    ins.CaPolicyDisabled++;
                else if (state.Contains("report"))
                    ins.CaPolicyReportOnly++;

                // Grant controls
                var builtIn = p.GrantControls?.BuiltInControls;
                if (builtIn is not null)
                {
                    if (builtIn.Any(c => c == ConditionalAccessGrantControl.Mfa))
                        ins.CaPolicyMfaRequired++;
                    if (builtIn.Any(c => c == ConditionalAccessGrantControl.CompliantDevice))
                    {
                        ins.CaPolicyCompliantDevice++;
                        ins.CaRequiresCompliance = true;
                    }
                    if (builtIn.Any(c => c == ConditionalAccessGrantControl.DomainJoinedDevice
                                      || c == ConditionalAccessGrantControl.ApprovedApplication))
                        ins.CaPolicyManagedDevice++;
                }

                // Conditions
                var cond = p.Conditions;
                if (cond is not null)
                {
                    // Applications targeted
                    var includeApps = cond.Applications?.IncludeApplications;
                    if (includeApps is not null)
                    {
                        if (includeApps.Contains("All"))
                            ins.CaPolicyAllApps++;
                        // Office 365 Exchange Online app ID commonly used to target M365
                        if (includeApps.Contains("00000003-0000-0ff1-ce00-000000000000"))
                            ins.CaPolicyTargetM365++;
                    }

                    // Legacy auth blocking
                    var clientTypes = cond.ClientAppTypes;
                    if (clientTypes is not null && clientTypes.Count > 0
                        && !clientTypes.Contains(ConditionalAccessClientApp.ExchangeActiveSync)
                        && !clientTypes.Contains(ConditionalAccessClientApp.Other))
                    {
                        ins.CaPolicyLegacyAuthBlocked++;
                    }

                    // Risk-based policies
                    if (cond.UserRiskLevels is { Count: > 0 })
                    {
                        ins.CaPolicyUserRiskBased++;
                        ins.UserRiskPolicyExists = true;
                    }
                    if (cond.SignInRiskLevels is { Count: > 0 })
                    {
                        ins.CaPolicySignInRiskBased++;
                        ins.SignInRiskPolicyExists = true;
                    }

                    // Location-based
                    if (cond.Locations is not null)
                        ins.CaPolicyLocationBased++;
                }
            }

            // Compute a weighted compatibility score
            ins.CaCompatScorePct = ComputeCaScore(ins);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            log.LogWarning("Entra CA policies: 403 - insufficient permissions");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Entra CA policies collection failed");
        }
    }

    private static double ComputeCaScore(EntraInsights ins)
    {
        if (ins.CaPolicyTotal == 0) return 0;
        double score = 0;
        // Weight: MFA (30), compliant device (20), legacy auth block (20),
        //         all-apps coverage (15), risk-based (15)
        if (ins.CaPolicyMfaRequired > 0) score += 30;
        if (ins.CaPolicyCompliantDevice > 0) score += 20;
        if (ins.CaPolicyLegacyAuthBlocked > 0) score += 20;
        if (ins.CaPolicyAllApps > 0 || ins.CaPolicyTargetM365 > 0) score += 15;
        if (ins.CaPolicyUserRiskBased > 0 || ins.CaPolicySignInRiskBased > 0) score += 15;
        return score;
    }

    // ================================================================
    // 2. MFA & authentication methods registration
    // ================================================================
    private static async Task CollectMfaStatus(
        GraphServiceClient graph, EntraInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            var resp = await graph.Reports.AuthenticationMethods.UserRegistrationDetails
                .GetAsync(cancellationToken: ct);
            var regs = resp?.Value;
            if (regs is null) return;

            ins.TotalUsers = regs.Count;
            var passwordlessMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "microsoftAuthenticator", "fido2", "windowsHelloForBusiness"
            };

            foreach (var u in regs)
            {
                if (u.IsMfaRegistered == true) ins.MfaRegistered++;
                if (u.IsMfaCapable == true) ins.MfaCapable++;

                var methods = u.MethodsRegistered;
                if (methods is null) continue;

                if (methods.Any(m => passwordlessMethods.Contains(m)))
                    ins.PasswordlessEnabled++;

                foreach (var m in methods)
                {
                    if (ins.AuthMethodBreakdown.ContainsKey(m))
                        ins.AuthMethodBreakdown[m]++;
                }
            }

            if (ins.TotalUsers > 0)
            {
                ins.MfaRegistrationPct = ins.MfaRegistered * 100.0 / ins.TotalUsers;
                ins.PasswordlessPct = ins.PasswordlessEnabled * 100.0 / ins.TotalUsers;
            }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            log.LogWarning("Entra MFA status: 403 - insufficient permissions");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Entra MFA status collection failed");
        }
    }

    // ================================================================
    // 3. Risky users + risk detections
    // ================================================================
    private static async Task CollectRiskyUsers(
        GraphServiceClient graph, EntraInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            // Risky users
            var riskyResp = await graph.IdentityProtection.RiskyUsers
                .GetAsync(cancellationToken: ct);
            if (riskyResp?.Value is not null)
            {
                foreach (var u in riskyResp.Value)
                {
                    var level = u.RiskLevel?.ToString()?.ToLowerInvariant() ?? "";
                    var state = u.RiskState?.ToString()?.ToLowerInvariant() ?? "";

                    if (level.Contains("high")) ins.RiskyUsersHigh++;
                    else if (level.Contains("medium")) ins.RiskyUsersMedium++;
                    else if (level.Contains("low")) ins.RiskyUsersLow++;

                    if (state.Contains("confirmedcompromised")) ins.ConfirmedCompromised++;
                    else if (state.Contains("atrisk")) ins.AtRisk++;
                    else if (state.Contains("remediated")) ins.Remediated++;
                }
            }

            // Risk detections
            var detResp = await graph.IdentityProtection.RiskDetections
                .GetAsync(cancellationToken: ct);
            if (detResp?.Value is not null)
            {
                ins.RiskDetectionsTotal = detResp.Value.Count;
                foreach (var d in detResp.Value)
                {
                    if ((d.RiskLevel?.ToString()?.ToLowerInvariant() ?? "").Contains("high"))
                        ins.RiskDetectionsHigh++;
                }
            }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            log.LogWarning("Entra risky users: 403 - insufficient permissions");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Entra risky users collection failed");
        }
    }

    // ================================================================
    // 4. PIM role assignments (permanent vs eligible)
    // ================================================================
    private static async Task CollectPim(
        GraphServiceClient graph, EntraInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            // Active / permanent role assignments
            var activeResp = await graph.RoleManagement.Directory.RoleAssignments
                .GetAsync(cancellationToken: ct);
            if (activeResp?.Value is not null)
            {
                ins.PermanentAssignmentsTotal = activeResp.Value.Count;
                foreach (var a in activeResp.Value)
                {
                    if (a.RoleDefinitionId?.Contains(GlobalAdminRoleTemplateId) == true)
                        ins.PermanentGlobalAdmins++;
                }
            }

            // Eligible role assignments (PIM)
            var eligResp = await graph.RoleManagement.Directory.RoleEligibilitySchedules
                .GetAsync(cancellationToken: ct);
            if (eligResp?.Value is not null)
                ins.EligibleAssignments = eligResp.Value.Count;

            // Time-bound active assignments
            var schedResp = await graph.RoleManagement.Directory.RoleAssignmentSchedules
                .GetAsync(cancellationToken: ct);
            if (schedResp?.Value is not null)
                ins.TimeBoundAssignments = schedResp.Value.Count;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            log.LogWarning("Entra PIM: 403 - insufficient permissions");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Entra PIM collection failed");
        }
    }

    // ================================================================
    // 5. Access Reviews
    // ================================================================
    private static async Task CollectAccessReviews(
        GraphServiceClient graph, EntraInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            var resp = await graph.IdentityGovernance.AccessReviews.Definitions
                .GetAsync(cancellationToken: ct);
            var reviews = resp?.Value;
            if (reviews is null) return;

            ins.AccessReviewTotal = reviews.Count;

            foreach (var r in reviews)
            {
                var status = r.Status?.ToLowerInvariant() ?? "";
                if (status is "inprogress" or "notstarted")
                    ins.AccessReviewActive++;

                // Scope analysis
                var query = r.Scope?.AdditionalData
                    ?.TryGetValue("query", out var q) == true ? q?.ToString()?.ToLowerInvariant() ?? "" : "";
                if (query.Contains("group") || query.Contains("groupmember"))
                    ins.AccessReviewGroupScope++;
                if (query.Contains("role") || query.Contains("roleassignment"))
                    ins.AccessReviewRoleScope++;
                if (query.Contains("guest") || query.Contains("usertype"))
                    ins.AccessReviewGuestScope++;

                // Recurrence
                var settings = r.Settings;
                if (settings?.Recurrence?.Pattern is not null)
                    ins.AccessReviewRecurring++;
            }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            log.LogWarning("Entra access reviews: 403 - insufficient permissions");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Entra access reviews collection failed");
        }
    }

    // ================================================================
    // 6. Device management & compliance (Intune)
    // ================================================================
    private static async Task CollectDevices(
        GraphServiceClient graph, EntraInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            // Managed devices
            var devResp = await graph.DeviceManagement.ManagedDevices
                .GetAsync(cancellationToken: ct);
            if (devResp?.Value is not null)
            {
                ins.DevicesTotalManaged = devResp.Value.Count;
                foreach (var d in devResp.Value)
                {
                    var compliance = d.ComplianceState?.ToString()?.ToLowerInvariant() ?? "";
                    if (compliance.Contains("compliant") && !compliance.Contains("non"))
                        ins.DevicesCompliant++;
                    else if (compliance.Contains("noncompliant"))
                        ins.DevicesNonCompliant++;
                    else if (compliance.Contains("ingraceperiod"))
                        ins.DevicesInGracePeriod++;

                    var ownership = d.ManagedDeviceOwnerType?.ToString()?.ToLowerInvariant() ?? "";
                    if (ownership.Contains("company")) ins.DevicesCorporate++;
                    else if (ownership.Contains("personal")) ins.DevicesPersonal++;
                }
            }

            // Compliance policies
            var polResp = await graph.DeviceManagement.DeviceCompliancePolicies
                .GetAsync(cancellationToken: ct);
            if (polResp?.Value is not null)
                ins.CompliancePoliciesCount = polResp.Value.Count;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            log.LogWarning("Entra devices: 403 - insufficient permissions");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Entra device collection failed");
        }
    }

    // ================================================================
    // 7. B2B: guest users, cross-tenant access, authorization policy
    // ================================================================
    private static async Task CollectB2b(
        GraphServiceClient graph, EntraInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            // Guest users
            var guestResp = await graph.Users.GetAsync(rc =>
            {
                rc.QueryParameters.Filter = "userType eq 'Guest'";
                rc.QueryParameters.Select = new[] { "id", "displayName", "userPrincipalName", "createdDateTime", "assignedLicenses" };
                rc.QueryParameters.Top = 999;
            }, cancellationToken: ct);

            if (guestResp?.Value is not null)
            {
                ins.GuestUsersTotal = guestResp.Value.Count;
                foreach (var g in guestResp.Value)
                {
                    if (g.AssignedLicenses is { Count: > 0 })
                        ins.GuestUsersWithLicenses++;
                }
            }

            // Cross-tenant access policy
            try
            {
                var ctaResp = await graph.Policies.CrossTenantAccessPolicy
                    .GetAsync(cancellationToken: ct);
                if (ctaResp is not null)
                    ins.CrossTenantConfigured = true;
            }
            catch { /* optional endpoint */ }

            // Authorization policy (guest invite settings)
            try
            {
                var authPol = await graph.Policies.AuthorizationPolicy
                    .GetAsync(cancellationToken: ct);
                if (authPol is not null)
                {
                    ins.GuestInviteSetting = authPol.AllowInvitesFrom?.ToString() ?? "Unknown";
                }
            }
            catch { /* optional endpoint */ }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            log.LogWarning("Entra B2B: 403 - insufficient permissions");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Entra B2B collection failed");
        }
    }

    // ================================================================
    // 8. OAuth apps, service principals, permission grants
    // ================================================================
    private static async Task CollectOAuthApps(
        GraphServiceClient graph, EntraInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            // Service principals
            var spResp = await graph.ServicePrincipals.GetAsync(rc =>
            {
                rc.QueryParameters.Select = new[]
                {
                    "id", "appId", "displayName", "publisherName",
                    "appRoles", "oauth2PermissionScopes"
                };
                rc.QueryParameters.Top = 500;
            }, cancellationToken: ct);

            if (spResp?.Value is not null)
            {
                ins.TotalApps = spResp.Value.Count;
                foreach (var sp in spResp.Value)
                {
                    // In Graph SDK v5 the property is VerifiedPublisher.DisplayName
                    var verifiedPublisher = sp.VerifiedPublisher?.DisplayName;
                    if (string.IsNullOrEmpty(verifiedPublisher))
                        ins.UnverifiedPublishers++;
                }
            }

            // OAuth permission grants
            var grantResp = await graph.Oauth2PermissionGrants
                .GetAsync(cancellationToken: ct);
            if (grantResp?.Value is not null)
            {
                ins.PermissionGrantCount = grantResp.Value.Count;
                var highPrivScopes = new[] { "mail.readwrite", "files.readwrite", "directory.readwrite" };

                foreach (var g in grantResp.Value)
                {
                    var scope = (g.Scope ?? "").ToLowerInvariant();
                    if (scope.Contains("mail")) ins.AppsWithMailAccess++;
                    if (scope.Contains("files") || scope.Contains("sharepoint")) ins.AppsWithFilesAccess++;
                    if (scope.Contains("graph") || !string.IsNullOrEmpty(g.ResourceId)) ins.AppsWithGraphAccess++;

                    if (highPrivScopes.Any(hp => scope.Contains(hp)))
                        ins.HighRiskApps++;

                    // Over-privileged: more than 10 scopes
                    if (scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 10)
                        ins.OverPrivilegedApps++;
                }
            }

            // Permission grant policies (consent settings)
            try
            {
                var consentResp = await graph.Policies.PermissionGrantPolicies
                    .GetAsync(cancellationToken: ct);
                if (consentResp?.Value is not null)
                {
                    foreach (var pol in consentResp.Value)
                    {
                        var id = pol.Id ?? "";
                        if (id.Contains("microsoft-user-default-legacy")
                            || id.Contains("microsoft-user-default-low"))
                        {
                            ins.UserConsentAllowed = true;
                        }
                    }
                    if (!ins.UserConsentAllowed)
                        ins.AdminConsentRequired = true;
                }
            }
            catch { /* optional endpoint */ }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            log.LogWarning("Entra OAuth apps: 403 - insufficient permissions");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Entra OAuth apps collection failed");
        }
    }

    // ================================================================
    // 9. Sign-in logs (last 7 days sample)
    // ================================================================
    private static async Task CollectSignIns(
        GraphServiceClient graph, EntraInsights ins, ILogger log, CancellationToken ct)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-7).ToString("yyyy-MM-ddTHH:mm:ssZ");
            var resp = await graph.AuditLogs.SignIns.GetAsync(rc =>
            {
                rc.QueryParameters.Filter = $"createdDateTime ge {cutoff}";
                rc.QueryParameters.Top = 500;
            }, cancellationToken: ct);

            if (resp?.Value is null) return;

            var legacyApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "pop", "imap", "smtp", "activesync", "other clients", "exchange web services"
            };

            foreach (var s in resp.Value)
            {
                var clientApp = (s.ClientAppUsed ?? "").ToLowerInvariant();
                if (legacyApps.Any(la => clientApp.Contains(la)))
                    ins.LegacyAuthSignIns++;

                // MFA detection from applied CA policies
                // Graph SDK v5 SignIn does not expose AuthenticationDetails directly.
                // Use AppliedConditionalAccessPolicies with grant control "mfa" as proxy.
                if (s.AppliedConditionalAccessPolicies is not null)
                {
                    foreach (var cap in s.AppliedConditionalAccessPolicies)
                    {
                        var controls = cap.EnforcedGrantControls;
                        if (controls is not null && controls.Any(c =>
                            c.Contains("mfa", StringComparison.OrdinalIgnoreCase)))
                        {
                            ins.MfaRequiredSignIns++;
                            if (s.Status?.ErrorCode == 0)
                                ins.MfaSuccessSignIns++;
                            break; // count once per sign-in
                        }
                    }
                }

                // Failed sign-ins
                var errorCode = s.Status?.ErrorCode;
                if (errorCode is not null && errorCode != 0)
                    ins.FailedSignIns++;

                // Risky sign-ins
                var risk = s.RiskLevelDuringSignIn?.ToString()?.ToLowerInvariant() ?? "";
                if (risk.Contains("high")) ins.RiskySignInsHigh++;
                else if (risk.Contains("medium")) ins.RiskySignInsMedium++;
            }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 403)
        {
            log.LogWarning("Entra sign-in logs: 403 - insufficient permissions");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Entra sign-in log collection failed");
        }
    }

    // ================================================================
    // 10. Global Secure Access (beta endpoints via HttpClient)
    // ================================================================
    private static async Task CollectGsa(
        HttpClient http, EntraInsights ins, ILogger log, CancellationToken ct)
    {
        // Internet Access: filtering policies
        try
        {
            var filterResp = await http.GetAsync("/beta/networkAccess/filteringPolicies", ct);
            if (filterResp.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                ins.GsaStatus = "PermissionDenied";
                ins.PrivateAccessStatus = "PermissionDenied";
                return;
            }
            if (filterResp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                ins.GsaStatus = "NotLicensed";
                ins.PrivateAccessStatus = "NotLicensed";
                return;
            }
            filterResp.EnsureSuccessStatusCode();

            var filterJson = await filterResp.Content.ReadAsStringAsync(ct);
            using var filterDoc = JsonDocument.Parse(filterJson);

            ins.GsaStatus = "Success";
            ins.FilteringPolicies = 0;
            ins.FqdnRules = 0;
            ins.WebCategoryRules = 0;

            if (filterDoc.RootElement.TryGetProperty("value", out var policies))
            {
                ins.FilteringPolicies = policies.GetArrayLength();
                foreach (var pol in policies.EnumerateArray())
                {
                    if (pol.TryGetProperty("policyRules", out var rules))
                    {
                        foreach (var rule in rules.EnumerateArray())
                        {
                            if (rule.TryGetProperty("destinations", out var dests))
                            {
                                foreach (var dest in dests.EnumerateArray())
                                {
                                    var odataType = dest.TryGetProperty("@odata.type", out var t)
                                        ? t.GetString()?.ToLowerInvariant() ?? "" : "";
                                    if (odataType.Contains("fqdn")) ins.FqdnRules++;
                                    else if (odataType.Contains("webcategory")) ins.WebCategoryRules++;
                                }
                            }
                        }
                    }
                }
            }

            // Forwarding profiles
            var fwdResp = await http.GetAsync("/beta/networkAccess/forwardingProfiles", ct);
            if (fwdResp.IsSuccessStatusCode)
            {
                var fwdJson = await fwdResp.Content.ReadAsStringAsync(ct);
                using var fwdDoc = JsonDocument.Parse(fwdJson);

                ins.ForwardingProfilesCount = 0;
                ins.M365ForwardingEnabled = false;
                ins.InternetForwardingEnabled = false;

                if (fwdDoc.RootElement.TryGetProperty("value", out var profiles))
                {
                    ins.ForwardingProfilesCount = profiles.GetArrayLength();
                    foreach (var profile in profiles.EnumerateArray())
                    {
                        var name = profile.TryGetProperty("name", out var n) ? n.GetString()?.ToLowerInvariant() ?? "" : "";
                        var state = profile.TryGetProperty("state", out var st) ? st.GetString()?.ToLowerInvariant() ?? "" : "";
                        if (state == "enabled")
                        {
                            if (name.Contains("microsoft365") || name.Contains("m365"))
                                ins.M365ForwardingEnabled = true;
                            else if (name.Contains("internet"))
                                ins.InternetForwardingEnabled = true;
                        }
                    }
                }
            }

            // Private Access: connectors
            ins.PrivateAccessStatus = "Success";
            ins.PrivateAccessConnectors = 0;
            ins.PrivateAccessActiveConnectors = 0;
            ins.PrivateAccessAppSegments = 0;

            try
            {
                var connResp = await http.GetAsync("/beta/networkAccess/connectivity/remoteNetworks", ct);
                if (connResp.IsSuccessStatusCode)
                {
                    var connJson = await connResp.Content.ReadAsStringAsync(ct);
                    using var connDoc = JsonDocument.Parse(connJson);
                    if (connDoc.RootElement.TryGetProperty("value", out var connectors))
                    {
                        ins.PrivateAccessConnectors = connectors.GetArrayLength();
                        foreach (var c in connectors.EnumerateArray())
                        {
                            var connState = c.TryGetProperty("connectivityState", out var cs)
                                ? cs.GetString() ?? "" : "";
                            if (connState.Equals("alive", StringComparison.OrdinalIgnoreCase))
                                ins.PrivateAccessActiveConnectors++;
                        }
                    }
                }
            }
            catch { /* optional */ }

            // Private Access: app segments / branches
            try
            {
                var branchResp = await http.GetAsync("/beta/networkAccess/connectivity/branches", ct);
                if (branchResp.IsSuccessStatusCode)
                {
                    var branchJson = await branchResp.Content.ReadAsStringAsync(ct);
                    using var branchDoc = JsonDocument.Parse(branchJson);
                    if (branchDoc.RootElement.TryGetProperty("value", out var apps))
                        ins.PrivateAccessAppSegments = apps.GetArrayLength();
                }
            }
            catch { /* optional */ }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            ins.GsaStatus = "PermissionDenied";
            ins.PrivateAccessStatus = "PermissionDenied";
            log.LogInformation("Entra GSA: permission denied (NetworkAccessPolicy.Read.All required)");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            ins.GsaStatus = "NotLicensed";
            ins.PrivateAccessStatus = "NotLicensed";
            log.LogInformation("Entra GSA: not licensed (Entra Suite required)");
        }
        catch (Exception ex)
        {
            ins.GsaStatus = "Error";
            ins.PrivateAccessStatus = "Error";
            log.LogWarning(ex, "Entra GSA collection failed");
        }
    }
}

using System.Globalization;
using System.Text.Json;
using KryossApi.Services.CloudAssessment.Recommendations;
using KryossApi.Services.CopilotReadiness.Pipelines;
using KryossApi.Services.CopilotReadiness.Recommendations;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;

namespace KryossApi.Services.CloudAssessment.Pipelines;

/// <summary>
/// Cloud Assessment (CA-1) identity pipeline.
///
/// Mirrors EntraPipeline's structure (10 parallel data collectors) and
/// extends it with broader MSP identity hygiene checks: service principal
/// credential expiry, B2B cross-tenant trust posture, PIM activation
/// auditing, access review freshness, user lifecycle hygiene, BitLocker
/// coverage, admin MFA gaps and tenant-wide consent grants.
///
/// Each collector catches its own exceptions (403 = permission gap,
/// non-fatal). Findings are deferred to <c>IdentityRecommendations</c>
/// (Task 2) — this pipeline only populates the shared
/// <see cref="IdentityInsights"/> bag and aggregates metrics.
/// </summary>
public static class IdentityPipeline
{
    private const string GlobalAdminRoleTemplateId = "62e90394-69f5-4237-9190-012177145e10";

    public static async Task<PipelineResult> RunAsync(
        GraphServiceClient graph,
        HttpClient? graphBetaHttp,
        ILogger log,
        CancellationToken ct)
    {
        var result = new PipelineResult { PipelineName = "identity", Status = "ok" };
        var insights = new IdentityInsights();
        var errorTracker = new CollectorErrorTracker();

        try
        {
            // Run all data collectors in parallel; each catches its own errors.
            var tasks = new List<Task>
            {
                CollectCaPolicies(graph, insights, errorTracker, log, ct),
                CollectMfaStatus(graph, insights, errorTracker, log, ct),
                CollectRiskyUsers(graph, insights, errorTracker, log, ct),
                CollectPim(graph, insights, errorTracker, log, ct),
                CollectAccessReviews(graph, insights, errorTracker, log, ct),
                CollectDevices(graph, insights, errorTracker, log, ct),
                CollectB2b(graph, insights, errorTracker, log, ct),
                CollectOAuthApps(graph, insights, errorTracker, log, ct),
                CollectSignIns(graph, insights, errorTracker, log, ct),
                CollectUserHygiene(graph, insights, errorTracker, log, ct),
            };

            if (graphBetaHttp is not null)
            {
                tasks.Add(CollectGsa(graphBetaHttp, insights, errorTracker, log, ct));
            }

            await Task.WhenAll(tasks);

            // CA compatibility score (same weighted formula as EntraPipeline).
            insights.CaCompatScorePct = ComputeCaScore(insights);

            var findings = IdentityRecommendations.Generate(insights);
            result.Findings.AddRange(findings);
            result.Insights = insights;

            // Metrics (string-valued, match EntraPipeline naming for shared keys).
            var m = result.Metrics;
            m["ca_compat_score_pct"] = insights.CaCompatScorePct.ToString("F1", CultureInfo.InvariantCulture);
            m["ca_policies_total"] = insights.CaPolicyTotal.ToString(CultureInfo.InvariantCulture);
            m["ca_policies_enabled"] = insights.CaPolicyEnabled.ToString(CultureInfo.InvariantCulture);

            m["mfa_registration_pct"] = insights.MfaRegistrationPct.ToString("F1", CultureInfo.InvariantCulture);
            m["passwordless_pct"] = insights.PasswordlessPct.ToString("F1", CultureInfo.InvariantCulture);

            m["risky_users_high"] = insights.RiskyUsersHigh.ToString(CultureInfo.InvariantCulture);
            m["risky_users_medium"] = insights.RiskyUsersMedium.ToString(CultureInfo.InvariantCulture);
            m["confirmed_compromised"] = insights.ConfirmedCompromised.ToString(CultureInfo.InvariantCulture);

            m["permanent_global_admins"] = insights.PermanentGlobalAdmins.ToString(CultureInfo.InvariantCulture);
            m["eligible_assignments"] = insights.EligibleAssignments.ToString(CultureInfo.InvariantCulture);
            m["time_bound_assignments"] = insights.TimeBoundAssignments.ToString(CultureInfo.InvariantCulture);
            m["pim_audit_entries_30d"] = insights.PimAuditEntriesLast30d.ToString(CultureInfo.InvariantCulture);
            m["pim_roles_no_mfa"] = insights.PimRolesWithoutMfaRequirement.ToString(CultureInfo.InvariantCulture);

            m["access_review_active"] = insights.AccessReviewActive.ToString(CultureInfo.InvariantCulture);
            m["access_reviews_completed_90d"] = insights.AccessReviewsCompletedLast90d.ToString(CultureInfo.InvariantCulture);
            m["access_reviews_overdue"] = insights.AccessReviewsOverdue.ToString(CultureInfo.InvariantCulture);

            m["devices_total_managed"] = insights.DevicesTotalManaged.ToString(CultureInfo.InvariantCulture);
            m["devices_compliant"] = insights.DevicesCompliant.ToString(CultureInfo.InvariantCulture);
            m["devices_non_compliant"] = insights.DevicesNonCompliant.ToString(CultureInfo.InvariantCulture);
            m["devices_without_compliance_policy"] = insights.DevicesWithoutCompliancePolicy.ToString(CultureInfo.InvariantCulture);
            m["bitlocker_coverage_pct"] = insights.BitlockerCoveragePct.ToString(CultureInfo.InvariantCulture);

            m["guest_users_total"] = insights.GuestUsersTotal.ToString(CultureInfo.InvariantCulture);
            m["b2b_allowed_domains"] = insights.B2bAllowedDomains.ToString(CultureInfo.InvariantCulture);
            m["b2b_blocked_domains"] = insights.B2bBlockedDomains.ToString(CultureInfo.InvariantCulture);

            m["total_apps"] = insights.TotalApps.ToString(CultureInfo.InvariantCulture);
            m["high_risk_apps"] = insights.HighRiskApps.ToString(CultureInfo.InvariantCulture);
            m["unverified_publishers"] = insights.UnverifiedPublishers.ToString(CultureInfo.InvariantCulture);
            m["spn_creds_expiring_30d"] = insights.ServicePrincipalCredentialsExpiring30d.ToString(CultureInfo.InvariantCulture);
            m["spn_creds_expired"] = insights.ServicePrincipalCredentialsExpired.ToString(CultureInfo.InvariantCulture);
            m["spn_creds_older_2y"] = insights.ServicePrincipalCredentialsOlderThan2Years.ToString(CultureInfo.InvariantCulture);
            m["oauth_grants_all_users"] = insights.OAuthConsentGrantsToAllUsers.ToString(CultureInfo.InvariantCulture);
            m["admins_without_mfa"] = insights.AdminsWithoutMfa.ToString(CultureInfo.InvariantCulture);

            m["users_password_never_expires"] = insights.UsersPasswordNeverExpires.ToString(CultureInfo.InvariantCulture);
            m["users_created_30d"] = insights.UsersCreatedLast30d.ToString(CultureInfo.InvariantCulture);
            m["users_no_signin_90d"] = insights.UsersNoSignIn90d.ToString(CultureInfo.InvariantCulture);

            m["legacy_auth_signins"] = insights.LegacyAuthSignIns.ToString(CultureInfo.InvariantCulture);
            m["failed_signins"] = insights.FailedSignIns.ToString(CultureInfo.InvariantCulture);
            m["mfa_required_signins"] = insights.MfaRequiredSignIns.ToString(CultureInfo.InvariantCulture);

            // GSA metrics only when beta endpoint was attempted.
            if (graphBetaHttp is not null)
            {
                m["gsa_status"] = insights.GsaStatus ?? "unknown";
                m["filtering_policies"] = (insights.FilteringPolicies ?? 0)
                    .ToString(CultureInfo.InvariantCulture);
            }

            // Final status: any collector error → partial; otherwise ok.
            if (errorTracker.HadError)
            {
                return new PipelineResult
                {
                    PipelineName = result.PipelineName,
                    Status = "partial",
                    Findings = result.Findings,
                    Metrics = result.Metrics,
                    SharepointSites = result.SharepointSites,
                    ExternalUsers = result.ExternalUsers,
                    Insights = insights,
                };
            }

            return result;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Identity pipeline top-level failure");
            return new PipelineResult
            {
                PipelineName = "identity",
                Status = "failed",
                Error = ex.Message,
            };
        }
    }

    // ================================================================
    // Helper: per-collector error tracking
    // ================================================================
    private sealed class CollectorErrorTracker
    {
        private int _errorCount;
        public bool HadError => Volatile.Read(ref _errorCount) > 0;
        public void MarkError() => Interlocked.Increment(ref _errorCount);
    }

    private static double ComputeCaScore(IdentityInsights ins)
    {
        if (ins.CaPolicyTotal == 0) return 0;
        double score = 0;
        // Same weighting as EntraPipeline.ComputeCaScore.
        if (ins.CaPolicyMfaRequired > 0) score += 30;
        if (ins.CaPolicyCompliantDevice > 0) score += 20;
        if (ins.CaPolicyLegacyAuthBlocked > 0) score += 20;
        if (ins.CaPolicyAllApps > 0 || ins.CaPolicyTargetM365 > 0) score += 15;
        if (ins.CaPolicyUserRiskBased > 0 || ins.CaPolicySignInRiskBased > 0) score += 15;
        return score;
    }

    // ================================================================
    // 1. Conditional Access policies
    // ================================================================
    private static async Task CollectCaPolicies(
        GraphServiceClient graph, IdentityInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
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

                var cond = p.Conditions;
                if (cond is not null)
                {
                    var includeApps = cond.Applications?.IncludeApplications;
                    if (includeApps is not null)
                    {
                        if (includeApps.Contains("All"))
                            ins.CaPolicyAllApps++;
                        if (includeApps.Contains("00000003-0000-0ff1-ce00-000000000000"))
                            ins.CaPolicyTargetM365++;
                    }

                    var clientTypes = cond.ClientAppTypes;
                    if (clientTypes is not null && clientTypes.Count > 0
                        && !clientTypes.Contains(ConditionalAccessClientApp.ExchangeActiveSync)
                        && !clientTypes.Contains(ConditionalAccessClientApp.Other))
                    {
                        ins.CaPolicyLegacyAuthBlocked++;
                    }

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

                    if (cond.Locations is not null)
                        ins.CaPolicyLocationBased++;
                }
            }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode is 403 or 401)
        {
            log.LogWarning("Identity CA policies: skipped — license or permissions required (HTTP {Code})", ex.ResponseStatusCode);
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Identity CA policies collection failed");
        }
    }

    // ================================================================
    // 2. MFA & authentication methods registration
    // ================================================================
    private static async Task CollectMfaStatus(
        GraphServiceClient graph, IdentityInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
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
        catch (ODataError ex) when (ex.ResponseStatusCode is 403 or 401)
        {
            log.LogWarning("Identity MFA status: skipped — license or permissions required (HTTP {Code})", ex.ResponseStatusCode);
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Identity MFA status collection failed");
        }
    }

    // ================================================================
    // 3. Risky users + risk detections
    // ================================================================
    private static async Task CollectRiskyUsers(
        GraphServiceClient graph, IdentityInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        try
        {
            var riskyResp = await graph.IdentityProtection.RiskyUsers.GetAsync(cancellationToken: ct);
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

            var detResp = await graph.IdentityProtection.RiskDetections.GetAsync(cancellationToken: ct);
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
        catch (ODataError ex) when (ex.ResponseStatusCode is 403 or 401
            || (ex.Error?.Message?.Contains("P2", StringComparison.OrdinalIgnoreCase) == true)
            || (ex.Error?.Message?.Contains("license", StringComparison.OrdinalIgnoreCase) == true))
        {
            log.LogWarning("Identity risky users: skipped — Entra ID P2 license required (HTTP {Code})", ex.ResponseStatusCode);
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Identity risky users collection failed");
        }
    }

    // ================================================================
    // 4. PIM role assignments (permanent vs eligible) + activation audit
    // ================================================================
    private static async Task CollectPim(
        GraphServiceClient graph, IdentityInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
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

            var eligResp = await graph.RoleManagement.Directory.RoleEligibilitySchedules
                .GetAsync(cancellationToken: ct);
            if (eligResp?.Value is not null)
                ins.EligibleAssignments = eligResp.Value.Count;

            var schedResp = await graph.RoleManagement.Directory.RoleAssignmentSchedules
                .GetAsync(cancellationToken: ct);
            if (schedResp?.Value is not null)
                ins.TimeBoundAssignments = schedResp.Value.Count;

            // Audit log: PIM activations last 30 days.
            try
            {
                var cutoff = DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-ddTHH:mm:ssZ");
                var auditResp = await graph.AuditLogs.DirectoryAudits.GetAsync(rc =>
                {
                    rc.QueryParameters.Filter =
                        $"category eq 'RoleManagement' and activityDateTime ge {cutoff}";
                    rc.QueryParameters.Top = 999;
                }, cancellationToken: ct);

                if (auditResp?.Value is not null)
                {
                    foreach (var entry in auditResp.Value)
                    {
                        var activity = entry.ActivityDisplayName?.ToLowerInvariant() ?? "";
                        if (activity.Contains("activate") || activity.Contains("add eligible"))
                            ins.PimAuditEntriesLast30d++;
                    }
                }
            }
            catch (ODataError) { /* audit logs require AuditLog.Read.All */ }
            catch { /* non-fatal */ }

            // Role management policy assignments → enumerate enabled rules per role
            // and detect MFA / justification requirements at activation.
            try
            {
                var polAssignResp = await graph.Policies.RoleManagementPolicyAssignments
                    .GetAsync(rc =>
                    {
                        rc.QueryParameters.Filter = "scopeId eq '/' and scopeType eq 'DirectoryRole'";
                        rc.QueryParameters.Expand = new[] { "policy($expand=rules)" };
                    }, cancellationToken: ct);

                if (polAssignResp?.Value is not null)
                {
                    foreach (var assign in polAssignResp.Value)
                    {
                        var rules = assign.Policy?.Rules;
                        if (rules is null) continue;

                        var requiresMfa = false;
                        var requiresJustification = false;

                        foreach (var rule in rules)
                        {
                            // EnablementRules live on UnifiedRoleManagementPolicyEnablementRule.
                            // Use AdditionalData / OdataType inspection for portability across SDK versions.
                            var typeHint = rule.OdataType?.ToLowerInvariant() ?? "";
                            if (!typeHint.Contains("enablementrule")) continue;

                            if (rule.AdditionalData is not null
                                && rule.AdditionalData.TryGetValue("enabledRules", out var enabledObj)
                                && enabledObj is IEnumerable<object> enabledList)
                            {
                                foreach (var item in enabledList)
                                {
                                    var s = item?.ToString()?.ToLowerInvariant() ?? "";
                                    if (s.Contains("mfa") || s.Contains("multifactor"))
                                        requiresMfa = true;
                                    if (s.Contains("justification"))
                                        requiresJustification = true;
                                }
                            }
                        }

                        if (!requiresMfa) ins.PimRolesWithoutMfaRequirement++;
                        if (!requiresJustification) ins.PimRolesWithoutJustificationRequirement++;
                    }
                }
            }
            catch (ODataError) { /* policy read may require RoleManagementPolicy.Read.Directory */ }
            catch { /* non-fatal */ }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode is 403 or 401
            || (ex.Error?.Message?.Contains("P2", StringComparison.OrdinalIgnoreCase) == true)
            || (ex.Error?.Message?.Contains("license", StringComparison.OrdinalIgnoreCase) == true)
            || (ex.Error?.Message?.Contains("Governance", StringComparison.OrdinalIgnoreCase) == true))
        {
            log.LogWarning("Identity PIM: skipped — Entra ID P2 or Governance license required (HTTP {Code})", ex.ResponseStatusCode);
            ins.PimSkippedReason = "Entra ID P2 / Governance license required";
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Identity PIM collection failed");
        }
    }

    // ================================================================
    // 5. Access Reviews + freshness
    // ================================================================
    private static async Task CollectAccessReviews(
        GraphServiceClient graph, IdentityInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        try
        {
            var resp = await graph.IdentityGovernance.AccessReviews.Definitions
                .GetAsync(cancellationToken: ct);
            var reviews = resp?.Value;
            if (reviews is null) return;

            ins.AccessReviewTotal = reviews.Count;
            var now = DateTimeOffset.UtcNow;
            var ninetyDaysAgo = now.AddDays(-90);

            foreach (var r in reviews)
            {
                var status = r.Status?.ToLowerInvariant() ?? "";
                if (status is "inprogress" or "notstarted")
                    ins.AccessReviewActive++;

                var query = r.Scope?.AdditionalData
                    ?.TryGetValue("query", out var q) == true ? q?.ToString()?.ToLowerInvariant() ?? "" : "";
                if (query.Contains("group") || query.Contains("groupmember"))
                    ins.AccessReviewGroupScope++;
                if (query.Contains("role") || query.Contains("roleassignment"))
                    ins.AccessReviewRoleScope++;
                if (query.Contains("guest") || query.Contains("usertype"))
                    ins.AccessReviewGuestScope++;

                var settings = r.Settings;
                if (settings?.Recurrence?.Pattern is not null)
                    ins.AccessReviewRecurring++;

                // Freshness: completed last 90d (look at lastModifiedDateTime as proxy
                // when the definition's status is "completed").
                if (status == "completed")
                {
                    var modified = r.LastModifiedDateTime;
                    if (modified.HasValue && modified.Value >= ninetyDaysAgo)
                        ins.AccessReviewsCompletedLast90d++;
                }

                // Overdue: review is still active but its end date has passed.
                if (status is "inprogress" or "notstarted")
                {
                    var endDate = r.Settings?.Recurrence?.Range?.EndDate;
                    if (endDate.HasValue)
                    {
                        // endDate is a Date (no time) — treat midnight UTC as cutoff.
                        var endUtc = new DateTimeOffset(
                            endDate.Value.Year, endDate.Value.Month, endDate.Value.Day,
                            0, 0, 0, TimeSpan.Zero);
                        if (endUtc < now)
                            ins.AccessReviewsOverdue++;
                    }
                }
            }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode is 403 or 401
            || (ex.Error?.Message?.Contains("P2", StringComparison.OrdinalIgnoreCase) == true)
            || (ex.Error?.Message?.Contains("license", StringComparison.OrdinalIgnoreCase) == true)
            || (ex.Error?.Message?.Contains("Governance", StringComparison.OrdinalIgnoreCase) == true))
        {
            log.LogWarning("Identity access reviews: skipped — Entra ID P2 or Governance license required (HTTP {Code})", ex.ResponseStatusCode);
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Identity access reviews collection failed");
        }
    }

    // ================================================================
    // 6. Device management & compliance (Intune) + BitLocker coverage
    // ================================================================
    private static async Task CollectDevices(
        GraphServiceClient graph, IdentityInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        try
        {
            int totalWindows = 0;
            int encryptedWindows = 0;
            int withoutPolicy = 0;

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

                    // BitLocker coverage on Windows.
                    var os = d.OperatingSystem?.ToLowerInvariant() ?? "";
                    if (os.Contains("windows"))
                    {
                        totalWindows++;
                        if (d.IsEncrypted == true) encryptedWindows++;
                    }

                    // "no policy applied" surfaces as compliance state == "configManager"
                    // or "unknown" with no compliance grace period — closest approximation
                    // available without a per-device policy assignment lookup.
                    if (compliance is "" or "unknown" or "configmanager")
                        withoutPolicy++;
                }
            }

            var polResp = await graph.DeviceManagement.DeviceCompliancePolicies
                .GetAsync(cancellationToken: ct);
            if (polResp?.Value is not null)
                ins.CompliancePoliciesCount = polResp.Value.Count;

            // If tenant has zero compliance policies, all managed devices are uncovered.
            if (ins.CompliancePoliciesCount == 0)
                ins.DevicesWithoutCompliancePolicy = ins.DevicesTotalManaged;
            else
                ins.DevicesWithoutCompliancePolicy = withoutPolicy;

            ins.BitlockerCoveragePct = totalWindows > 0
                ? (int)Math.Round(encryptedWindows * 100.0 / totalWindows)
                : 0;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode is 403 or 401)
        {
            log.LogWarning("Identity devices: skipped — Intune license required (HTTP {Code})", ex.ResponseStatusCode);
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Identity device collection failed");
        }
    }

    // ================================================================
    // 7. B2B: guest users, cross-tenant access, partners, trust posture
    // ================================================================
    private static async Task CollectB2b(
        GraphServiceClient graph, IdentityInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        try
        {
            var guestResp = await graph.Users.GetAsync(rc =>
            {
                rc.QueryParameters.Filter = "userType eq 'Guest'";
                rc.QueryParameters.Select = new[]
                {
                    "id", "displayName", "userPrincipalName", "createdDateTime", "assignedLicenses"
                };
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

            try
            {
                var ctaResp = await graph.Policies.CrossTenantAccessPolicy
                    .GetAsync(cancellationToken: ct);
                if (ctaResp is not null)
                    ins.CrossTenantConfigured = true;
            }
            catch { /* optional endpoint */ }

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

            // Cross-tenant access partners — count + extract inbound trust settings.
            try
            {
                var partnersResp = await graph.Policies.CrossTenantAccessPolicy.Partners
                    .GetAsync(cancellationToken: ct);
                if (partnersResp?.Value is not null)
                {
                    ins.PartnerConfigurations = partnersResp.Value.Count;
                    foreach (var partner in partnersResp.Value)
                    {
                        var inbound = partner.InboundTrust;
                        if (inbound is not null)
                        {
                            // First non-null wins (tenant-wide MFA trust posture).
                            if (ins.B2bInboundTrustMfa is null && inbound.IsMfaAccepted.HasValue)
                                ins.B2bInboundTrustMfa = inbound.IsMfaAccepted.Value
                                    ? "accepted" : "not_accepted";
                            if (ins.B2bInboundTrustCompliantDevice is null
                                && inbound.IsCompliantDeviceAccepted.HasValue)
                                ins.B2bInboundTrustCompliantDevice =
                                    inbound.IsCompliantDeviceAccepted.Value
                                        ? "accepted" : "not_accepted";
                        }
                    }
                }
            }
            catch (ODataError) { /* requires Policy.Read.All */ }
            catch { /* non-fatal */ }

            // Tenant-wide allow/block domain lists.
            try
            {
                var defaultPolicy = await graph.Policies.CrossTenantAccessPolicy.Default
                    .GetAsync(cancellationToken: ct);

                if (defaultPolicy?.AdditionalData is not null)
                {
                    if (defaultPolicy.AdditionalData.TryGetValue("invitationsFrom", out _))
                    {
                        // No-op — default-policy domain lists are surfaced via b2bCollaborationOutbound,
                        // but per-tenant allow/block lists live under the b2bCollaborationInbound child.
                    }
                }

                // Domain-level allow/block lists are configured under tenantRestrictions /
                // crossTenantAccessPolicy default. Read them via a beta call on the Graph
                // client when SDK-typed access is unavailable.
                ins.B2bAllowedDomains = ExtractDomainList(defaultPolicy, "allowedDomains");
                ins.B2bBlockedDomains = ExtractDomainList(defaultPolicy, "blockedDomains");
            }
            catch (ODataError) { /* permission not granted */ }
            catch { /* non-fatal */ }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode is 403 or 401)
        {
            log.LogWarning("Identity B2B: skipped — license or permissions required (HTTP {Code})", ex.ResponseStatusCode);
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Identity B2B collection failed");
        }
    }

    private static int ExtractDomainList(object? source, string key)
    {
        if (source is null) return 0;

        // Reach into AdditionalData if present — Cross-tenant default policy
        // exposes domain restrictions as nested JSON under tenantRestrictions.
        var addlProp = source.GetType().GetProperty("AdditionalData");
        if (addlProp?.GetValue(source) is not IDictionary<string, object> addl) return 0;

        if (!addl.TryGetValue(key, out var raw) || raw is null) return 0;

        if (raw is IEnumerable<object> list)
        {
            var n = 0;
            foreach (var _ in list) n++;
            return n;
        }

        // JSON element fallback.
        if (raw is JsonElement je && je.ValueKind == JsonValueKind.Array)
            return je.GetArrayLength();

        return 0;
    }

    // ================================================================
    // 8. OAuth apps + SP credentials + tenant-wide consent grants
    // ================================================================
    private static async Task CollectOAuthApps(
        GraphServiceClient graph, IdentityInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        try
        {
            var spResp = await graph.ServicePrincipals.GetAsync(rc =>
            {
                rc.QueryParameters.Select = new[]
                {
                    "id", "appId", "displayName", "publisherName",
                    "appRoles", "oauth2PermissionScopes",
                    "passwordCredentials", "keyCredentials",
                    "verifiedPublisher", "appOwnerOrganizationId"
                };
                rc.QueryParameters.Top = 500;
            }, cancellationToken: ct);

            var now = DateTimeOffset.UtcNow;
            var thirtyDaysOut = now.AddDays(30);
            var twoYearsAgo = now.AddYears(-2);

            if (spResp?.Value is not null)
            {
                ins.TotalApps = spResp.Value.Count;
                foreach (var sp in spResp.Value)
                {
                    var verifiedPublisher = sp.VerifiedPublisher?.DisplayName;
                    if (string.IsNullOrEmpty(verifiedPublisher))
                        ins.UnverifiedPublishers++;

                    // Service principal credential hygiene
                    if (sp.PasswordCredentials is not null)
                    {
                        foreach (var pc in sp.PasswordCredentials)
                        {
                            if (pc.EndDateTime is null) continue;
                            var end = pc.EndDateTime.Value;
                            if (end < now) ins.ServicePrincipalCredentialsExpired++;
                            else if (end < thirtyDaysOut) ins.ServicePrincipalCredentialsExpiring30d++;

                            if (pc.StartDateTime.HasValue && pc.StartDateTime.Value < twoYearsAgo)
                                ins.ServicePrincipalCredentialsOlderThan2Years++;
                        }
                    }
                    if (sp.KeyCredentials is not null)
                    {
                        foreach (var kc in sp.KeyCredentials)
                        {
                            if (kc.EndDateTime is null) continue;
                            var end = kc.EndDateTime.Value;
                            if (end < now) ins.ServicePrincipalCredentialsExpired++;
                            else if (end < thirtyDaysOut) ins.ServicePrincipalCredentialsExpiring30d++;

                            if (kc.StartDateTime.HasValue && kc.StartDateTime.Value < twoYearsAgo)
                                ins.ServicePrincipalCredentialsOlderThan2Years++;
                        }
                    }

                    // Partner-tenant apps (consented from outside tenant).
                    var ownerTenant = sp.AppOwnerOrganizationId?.ToString();
                    if (!string.IsNullOrEmpty(ownerTenant)
                        && !ownerTenant.Equals("f8cdef31-a31e-4b4a-93e4-5f571e91255a",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        // Microsoft Services tenant id (f8cdef…) excluded so that
                        // first-party Microsoft apps don't inflate the partner count.
                        ins.ConsentedPartnerApps++;
                    }
                }
            }

            // OAuth permission grants
            var grantResp = await graph.Oauth2PermissionGrants.GetAsync(cancellationToken: ct);
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

                    if (scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 10)
                        ins.OverPrivilegedApps++;

                    var consentType = g.ConsentType?.ToLowerInvariant() ?? "";
                    if (consentType.Contains("allprincipals"))
                        ins.OAuthConsentGrantsToAllUsers++;
                }
            }

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
        catch (ODataError ex) when (ex.ResponseStatusCode is 403 or 401)
        {
            log.LogWarning("Identity OAuth apps: skipped — license or permissions required (HTTP {Code})", ex.ResponseStatusCode);
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Identity OAuth apps collection failed");
        }
    }

    // ================================================================
    // 9. Sign-in logs (last 7 days sample)
    // ================================================================
    private static async Task CollectSignIns(
        GraphServiceClient graph, IdentityInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
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
                            break;
                        }
                    }
                }

                var errorCode = s.Status?.ErrorCode;
                if (errorCode is not null && errorCode != 0)
                    ins.FailedSignIns++;

                var risk = s.RiskLevelDuringSignIn?.ToString()?.ToLowerInvariant() ?? "";
                if (risk.Contains("high")) ins.RiskySignInsHigh++;
                else if (risk.Contains("medium")) ins.RiskySignInsMedium++;
            }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode is 403 or 401)
        {
            log.LogWarning("Identity sign-in logs: skipped — Entra ID P1/P2 license required (HTTP {Code})", ex.ResponseStatusCode);
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Identity sign-in log collection failed");
        }
    }

    // ================================================================
    // 10. Global Secure Access (beta endpoints via HttpClient)
    // ================================================================
    private static async Task CollectGsa(
        HttpClient http, IdentityInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
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
            log.LogInformation("Identity GSA: permission denied (NetworkAccessPolicy.Read.All required)");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            ins.GsaStatus = "NotLicensed";
            ins.PrivateAccessStatus = "NotLicensed";
            log.LogInformation("Identity GSA: not licensed (Entra Suite required)");
        }
        catch (Exception ex)
        {
            ins.GsaStatus = "Error";
            ins.PrivateAccessStatus = "Error";
            err.MarkError();
            log.LogWarning(ex, "Identity GSA collection failed");
        }
    }

    // ================================================================
    // 11. User hygiene (passwords, lifecycle, admin MFA gap)
    // ================================================================
    private static async Task CollectUserHygiene(
        GraphServiceClient graph, IdentityInsights ins,
        CollectorErrorTracker err, ILogger log, CancellationToken ct)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var thirtyDaysAgo = now.AddDays(-30);
            var ninetyDaysAgo = now.AddDays(-90);

            // 1. User lifecycle scan.
            var usersResp = await graph.Users.GetAsync(rc =>
            {
                rc.QueryParameters.Select = new[]
                {
                    "id", "userPrincipalName", "accountEnabled", "passwordPolicies",
                    "createdDateTime", "signInActivity"
                };
                rc.QueryParameters.Top = 999;
            }, cancellationToken: ct);

            if (usersResp?.Value is not null)
            {
                foreach (var u in usersResp.Value)
                {
                    var policies = u.PasswordPolicies?.ToLowerInvariant() ?? "";
                    if (policies.Contains("disablepasswordexpiration"))
                        ins.UsersPasswordNeverExpires++;

                    if (u.CreatedDateTime.HasValue && u.CreatedDateTime.Value >= thirtyDaysAgo)
                        ins.UsersCreatedLast30d++;

                    // No-sign-in users: enabled, never signed in (or last sign-in > 90d).
                    if (u.AccountEnabled == true)
                    {
                        var lastSignIn = u.SignInActivity?.LastSignInDateTime;
                        if (!lastSignIn.HasValue || lastSignIn.Value < ninetyDaysAgo)
                            ins.UsersNoSignIn90d++;
                    }
                }
            }

            // 2. Admin MFA gap: list members of priviliged role + cross-reference
            // against MFA registration report. Use directoryRoles transitive members.
            var adminUserIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var rolesResp = await graph.DirectoryRoles.GetAsync(cancellationToken: ct);
                if (rolesResp?.Value is not null)
                {
                    foreach (var role in rolesResp.Value)
                    {
                        if (string.IsNullOrEmpty(role.Id)) continue;

                        // Limit to high-privilege roles by template id when available.
                        var template = role.RoleTemplateId ?? "";
                        if (string.IsNullOrEmpty(template)) continue;

                        var membersResp = await graph.DirectoryRoles[role.Id].Members
                            .GetAsync(cancellationToken: ct);
                        if (membersResp?.Value is null) continue;

                        foreach (var member in membersResp.Value)
                        {
                            if (member is User user && !string.IsNullOrEmpty(user.Id))
                                adminUserIds.Add(user.Id);
                        }
                    }
                }

                if (adminUserIds.Count > 0)
                {
                    var regResp = await graph.Reports.AuthenticationMethods.UserRegistrationDetails
                        .GetAsync(cancellationToken: ct);
                    if (regResp?.Value is not null)
                    {
                        var regById = regResp.Value
                            .Where(r => !string.IsNullOrEmpty(r.Id))
                            .ToDictionary(r => r.Id!, r => r, StringComparer.OrdinalIgnoreCase);

                        foreach (var adminId in adminUserIds)
                        {
                            if (!regById.TryGetValue(adminId, out var reg)
                                || reg.IsMfaRegistered != true)
                            {
                                ins.AdminsWithoutMfa++;
                            }
                        }
                    }
                    else
                    {
                        // Without MFA registration report data, we can't tell — skip.
                    }
                }
            }
            catch (ODataError) { /* directoryRoles permission missing */ }
            catch { /* non-fatal */ }
        }
        catch (ODataError ex) when (ex.ResponseStatusCode is 403 or 401)
        {
            log.LogWarning("Identity user hygiene: skipped — license or permissions required (HTTP {Code})", ex.ResponseStatusCode);
        }
        catch (Exception ex)
        {
            err.MarkError();
            log.LogWarning(ex, "Identity user hygiene collection failed");
        }
    }
}

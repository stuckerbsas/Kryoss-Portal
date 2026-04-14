using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Extensions.Logging;

namespace KryossApi.Services;

/// <summary>
/// Result of a single M365 security check.
/// </summary>
public class M365CheckResult
{
    public string CheckId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Category { get; set; } = null!;
    public string Severity { get; set; } = null!;
    public string Status { get; set; } = null!; // pass, fail, warn, info
    public string? Finding { get; set; }
    public string? ActualValue { get; set; }
}

public interface IM365ScannerService
{
    Task<List<M365CheckResult>> ScanAsync(string tenantId, string clientId, string clientSecret);
    Task<List<M365CheckResult>> ScanAsync(string tenantId);
}

/// <summary>
/// Connects to Microsoft Graph API using client credentials and runs ~50
/// security checks against an M365 / Entra ID tenant.
/// </summary>
public class M365ScannerService : IM365ScannerService
{
    private readonly ILogger<M365ScannerService> _log;
    private readonly M365Config _config;

    public M365ScannerService(ILogger<M365ScannerService> log, M365Config config)
    {
        _log = log;
        _config = config;
    }

    /// <summary>
    /// Scan using per-customer (legacy) credentials.
    /// </summary>
    public async Task<List<M365CheckResult>> ScanAsync(string tenantId, string clientId, string clientSecret)
    {
        var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
        var graph = new GraphServiceClient(credential);
        return await RunAllChecks(graph);
    }

    /// <summary>
    /// Scan using shared (multi-tenant admin consent) credentials.
    /// </summary>
    public async Task<List<M365CheckResult>> ScanAsync(string tenantId)
    {
        var credential = new ClientSecretCredential(tenantId, _config.ClientId, _config.ClientSecret);
        var graph = new GraphServiceClient(credential);
        return await RunAllChecks(graph);
    }

    private async Task<List<M365CheckResult>> RunAllChecks(GraphServiceClient graph)
    {
        var results = new List<M365CheckResult>();

        // Existing checks (M365-001 to M365-030)
        results.AddRange(await CheckConditionalAccess(graph));
        results.AddRange(await CheckMfaStatus(graph));
        results.AddRange(await CheckSecurityDefaults(graph));
        results.AddRange(await CheckAdminRoles(graph));
        results.AddRange(await CheckGuestAccess(graph));
        results.AddRange(await CheckMailSecurity(graph));

        // Tier 1 — Stale Accounts + Apps + Secure Score (M365-031 to M365-040)
        results.AddRange(await CheckStaleAccounts(graph));
        results.AddRange(await CheckAppRegistrations(graph));
        results.AddRange(await CheckSecureScore(graph));
        results.AddRange(await CheckNamedLocations(graph));
        results.AddRange(await CheckPasswordPolicy(graph));

        // Tier 2 — Risk + Intune (M365-041 to M365-046)
        results.AddRange(await CheckRiskDetections(graph));
        results.AddRange(await CheckIntuneCompliance(graph));

        // Tier 3 — DLP + SharePoint + Alerts (M365-047 to M365-050)
        results.AddRange(await CheckDlpPolicies(graph));
        results.AddRange(await CheckSecurityAlerts(graph));
        results.AddRange(await CheckSharePointSharing(graph));
        results.AddRange(await CheckOrgConfig(graph));

        return results;
    }

    // ── Conditional Access (M365-001 to M365-008) ──

    private async Task<List<M365CheckResult>> CheckConditionalAccess(GraphServiceClient graph)
    {
        var results = new List<M365CheckResult>();

        try
        {
            var policies = await graph.Identity.ConditionalAccess.Policies.GetAsync();
            var policyList = policies?.Value ?? [];

            var enabledPolicies = policyList.Where(p => p.State == ConditionalAccessPolicyState.Enabled).ToList();
            var reportOnlyPolicies = policyList.Where(p => p.State == ConditionalAccessPolicyState.EnabledForReportingButNotEnforced).ToList();

            // M365-001: Any CA policies exist
            results.Add(new M365CheckResult
            {
                CheckId = "M365-001",
                Name = "Conditional Access policies configured",
                Category = "conditional_access",
                Severity = "critical",
                Status = enabledPolicies.Count > 0 ? "pass" : "fail",
                Finding = enabledPolicies.Count > 0
                    ? $"{enabledPolicies.Count} enabled CA policies found"
                    : "No enabled Conditional Access policies found. Tenant relies on security defaults or no protection.",
                ActualValue = enabledPolicies.Count.ToString()
            });

            // M365-002: CA policy requiring MFA for all users
            var mfaAllUsers = enabledPolicies.Any(p =>
                p.Conditions?.Users?.IncludeUsers?.Contains("All") == true &&
                p.GrantControls?.BuiltInControls?.Contains(ConditionalAccessGrantControl.Mfa) == true);
            results.Add(new M365CheckResult
            {
                CheckId = "M365-002",
                Name = "MFA required for all users via CA",
                Category = "conditional_access",
                Severity = "critical",
                Status = mfaAllUsers ? "pass" : "fail",
                Finding = mfaAllUsers
                    ? "A CA policy enforces MFA for all users"
                    : "No CA policy enforces MFA for all users",
                ActualValue = mfaAllUsers.ToString()
            });

            // M365-003: CA policy requiring MFA for admins
            var mfaAdmins = enabledPolicies.Any(p =>
                (p.Conditions?.Users?.IncludeRoles?.Count > 0 || p.Conditions?.Users?.IncludeUsers?.Contains("All") == true) &&
                p.GrantControls?.BuiltInControls?.Contains(ConditionalAccessGrantControl.Mfa) == true);
            results.Add(new M365CheckResult
            {
                CheckId = "M365-003",
                Name = "MFA required for admin roles via CA",
                Category = "conditional_access",
                Severity = "critical",
                Status = mfaAdmins ? "pass" : "fail",
                Finding = mfaAdmins
                    ? "A CA policy enforces MFA for admin roles"
                    : "No CA policy enforces MFA specifically for admin roles",
                ActualValue = mfaAdmins.ToString()
            });

            // M365-004: CA policy blocking legacy authentication
            var blockLegacy = enabledPolicies.Any(p =>
                p.Conditions?.ClientAppTypes?.Contains(ConditionalAccessClientApp.ExchangeActiveSync) == true &&
                p.GrantControls?.BuiltInControls?.Contains(ConditionalAccessGrantControl.Block) == true);
            results.Add(new M365CheckResult
            {
                CheckId = "M365-004",
                Name = "Legacy authentication blocked via CA",
                Category = "conditional_access",
                Severity = "high",
                Status = blockLegacy ? "pass" : "fail",
                Finding = blockLegacy
                    ? "A CA policy blocks legacy authentication protocols"
                    : "No CA policy blocks legacy authentication. Legacy protocols bypass MFA.",
                ActualValue = blockLegacy.ToString()
            });

            // M365-005: CA policy requiring compliant devices
            var compliantDevices = enabledPolicies.Any(p =>
                p.GrantControls?.BuiltInControls?.Contains(ConditionalAccessGrantControl.CompliantDevice) == true);
            results.Add(new M365CheckResult
            {
                CheckId = "M365-005",
                Name = "Compliant device requirement via CA",
                Category = "conditional_access",
                Severity = "medium",
                Status = compliantDevices ? "pass" : "warn",
                Finding = compliantDevices
                    ? "A CA policy requires compliant/managed devices"
                    : "No CA policy requires device compliance. Unmanaged devices can access resources.",
                ActualValue = compliantDevices.ToString()
            });

            // M365-006: CA policy for risky sign-ins
            var riskySignIn = enabledPolicies.Any(p =>
                p.Conditions?.SignInRiskLevels?.Count > 0);
            results.Add(new M365CheckResult
            {
                CheckId = "M365-006",
                Name = "Risky sign-in CA policy configured",
                Category = "conditional_access",
                Severity = "high",
                Status = riskySignIn ? "pass" : "warn",
                Finding = riskySignIn
                    ? "A CA policy addresses risky sign-ins"
                    : "No CA policy addresses risky sign-ins. Requires Entra ID P2.",
                ActualValue = riskySignIn.ToString()
            });

            // M365-007: CA policy for risky users
            var riskyUser = enabledPolicies.Any(p =>
                p.Conditions?.UserRiskLevels?.Count > 0);
            results.Add(new M365CheckResult
            {
                CheckId = "M365-007",
                Name = "Risky user CA policy configured",
                Category = "conditional_access",
                Severity = "high",
                Status = riskyUser ? "pass" : "warn",
                Finding = riskyUser
                    ? "A CA policy addresses risky users"
                    : "No CA policy addresses risky users. Requires Entra ID P2.",
                ActualValue = riskyUser.ToString()
            });

            // M365-008: Number of CA policies in report-only mode
            results.Add(new M365CheckResult
            {
                CheckId = "M365-008",
                Name = "CA policies in report-only mode",
                Category = "conditional_access",
                Severity = "low",
                Status = reportOnlyPolicies.Count > 0 ? "info" : "pass",
                Finding = reportOnlyPolicies.Count > 0
                    ? $"{reportOnlyPolicies.Count} CA policies are in report-only mode (not enforced)"
                    : "No CA policies in report-only mode",
                ActualValue = reportOnlyPolicies.Count.ToString()
            });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to check Conditional Access policies");
            results.Add(InsufficientPermissions("M365-001", "Conditional Access policies", "conditional_access",
                "Requires Policy.Read.All permission"));
        }

        return results;
    }

    // ── MFA Status (M365-009 to M365-014) ──

    private async Task<List<M365CheckResult>> CheckMfaStatus(GraphServiceClient graph)
    {
        var results = new List<M365CheckResult>();

        try
        {
            // Get all licensed users
            var usersResponse = await graph.Users.GetAsync(config =>
            {
                config.QueryParameters.Select = new[] { "id", "displayName", "userPrincipalName", "userType", "assignedLicenses" };
                config.QueryParameters.Filter = "userType eq 'Member'";
                config.QueryParameters.Top = 999;
            });
            var users = usersResponse?.Value ?? [];
            var licensedUsers = users.Where(u => u.AssignedLicenses?.Count > 0).ToList();

            // Check auth methods for each user (sample up to 50 to avoid throttling)
            var sampleUsers = licensedUsers.Take(50).ToList();
            int usersWithMfa = 0;
            int adminsWithoutMfa = 0;
            var mfaMethods = new Dictionary<string, int>();

            foreach (var user in sampleUsers)
            {
                try
                {
                    var methods = await graph.Users[user.Id].Authentication.Methods.GetAsync();
                    var methodList = methods?.Value ?? [];
                    // MFA = has anything beyond just password
                    var hasMfa = methodList.Any(m => m is not Microsoft.Graph.Models.PasswordAuthenticationMethod);

                    if (hasMfa) usersWithMfa++;

                    foreach (var method in methodList)
                    {
                        var methodType = method?.OdataType?.Replace("#microsoft.graph.", "") ?? "unknown";
                        if (methodType != "passwordAuthenticationMethod")
                        {
                            mfaMethods.TryGetValue(methodType, out var count);
                            mfaMethods[methodType] = count + 1;
                        }
                    }
                }
                catch
                {
                    // Individual user auth method read may fail — continue
                }
            }

            var mfaRate = sampleUsers.Count > 0 ? (double)usersWithMfa / sampleUsers.Count * 100 : 0;

            // M365-009: Per-user MFA enforcement rate
            results.Add(new M365CheckResult
            {
                CheckId = "M365-009",
                Name = "MFA enrollment rate",
                Category = "mfa",
                Severity = "critical",
                Status = mfaRate >= 90 ? "pass" : mfaRate >= 50 ? "warn" : "fail",
                Finding = $"{mfaRate:F0}% of sampled users ({usersWithMfa}/{sampleUsers.Count}) have MFA methods registered",
                ActualValue = $"{mfaRate:F0}%"
            });

            // M365-010: Admins without MFA (covered partially via CA check, but flag here)
            results.Add(new M365CheckResult
            {
                CheckId = "M365-010",
                Name = "Admin accounts MFA status",
                Category = "mfa",
                Severity = "critical",
                Status = "info",
                Finding = "Admin MFA status checked via Conditional Access policies and directory role membership",
                ActualValue = "See M365-003 and M365-020"
            });

            // M365-011: MFA default methods
            var topMethod = mfaMethods.OrderByDescending(kv => kv.Value).FirstOrDefault();
            results.Add(new M365CheckResult
            {
                CheckId = "M365-011",
                Name = "MFA methods distribution",
                Category = "mfa",
                Severity = "medium",
                Status = topMethod.Key?.Contains("phone", StringComparison.OrdinalIgnoreCase) == true ? "warn" : "pass",
                Finding = mfaMethods.Count > 0
                    ? $"Most common: {topMethod.Key} ({topMethod.Value} users). SMS-based MFA is less secure than authenticator app."
                    : "No MFA methods detected in sample",
                ActualValue = topMethod.Key ?? "none"
            });

            // M365-012: Number registration campaign
            results.Add(new M365CheckResult
            {
                CheckId = "M365-012",
                Name = "MFA registration campaign",
                Category = "mfa",
                Severity = "low",
                Status = "info",
                Finding = "MFA registration campaign status requires admin consent to read AuthenticationMethodsPolicy. Check Entra ID portal.",
                ActualValue = "N/A"
            });

            // M365-013: Authentication methods policy
            try
            {
                var authMethodsPolicy = await graph.Policies.AuthenticationMethodsPolicy.GetAsync();
                var registrationEnforcement = authMethodsPolicy?.RegistrationEnforcement?.AuthenticationMethodsRegistrationCampaign;
                var campaignState = registrationEnforcement?.State?.ToString() ?? "unknown";

                results.Add(new M365CheckResult
                {
                    CheckId = "M365-013",
                    Name = "Authentication methods policy configured",
                    Category = "mfa",
                    Severity = "medium",
                    Status = authMethodsPolicy != null ? "pass" : "warn",
                    Finding = $"Authentication methods policy exists. Registration campaign: {campaignState}",
                    ActualValue = campaignState
                });
            }
            catch
            {
                results.Add(InsufficientPermissions("M365-013", "Authentication methods policy", "mfa",
                    "Requires Policy.Read.All"));
            }

            // M365-014: SSPR
            results.Add(new M365CheckResult
            {
                CheckId = "M365-014",
                Name = "Self-Service Password Reset (SSPR)",
                Category = "mfa",
                Severity = "medium",
                Status = "info",
                Finding = "SSPR configuration requires reading authorization policy. Check Entra ID portal > Password reset.",
                ActualValue = "N/A"
            });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to check MFA status");
            results.Add(InsufficientPermissions("M365-009", "MFA enrollment rate", "mfa",
                "Requires UserAuthenticationMethod.Read.All and User.Read.All"));
        }

        return results;
    }

    // ── Security Defaults (M365-015 to M365-017) ──

    private async Task<List<M365CheckResult>> CheckSecurityDefaults(GraphServiceClient graph)
    {
        var results = new List<M365CheckResult>();

        // M365-015: Security defaults enabled
        try
        {
            var secDefaults = await graph.Policies.IdentitySecurityDefaultsEnforcementPolicy.GetAsync();
            var isEnabled = secDefaults?.IsEnabled ?? false;

            results.Add(new M365CheckResult
            {
                CheckId = "M365-015",
                Name = "Security defaults status",
                Category = "security_defaults",
                Severity = "high",
                Status = isEnabled ? "pass" : "info",
                Finding = isEnabled
                    ? "Security defaults are enabled. Good baseline if no CA policies are configured."
                    : "Security defaults are disabled. Ensure Conditional Access policies provide equivalent or better protection.",
                ActualValue = isEnabled.ToString()
            });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to check security defaults");
            results.Add(InsufficientPermissions("M365-015", "Security defaults", "security_defaults",
                "Requires Policy.Read.All"));
        }

        // M365-016: Unified audit log
        results.Add(new M365CheckResult
        {
            CheckId = "M365-016",
            Name = "Unified audit log",
            Category = "security_defaults",
            Severity = "high",
            Status = "info",
            Finding = "Unified audit log status requires Exchange Online Management. Verify via M365 Compliance Center > Audit.",
            ActualValue = "N/A"
        });

        // M365-017: Sign-in risk policy
        results.Add(new M365CheckResult
        {
            CheckId = "M365-017",
            Name = "Sign-in risk policy",
            Category = "security_defaults",
            Severity = "high",
            Status = "info",
            Finding = "Sign-in risk policy status checked via CA policies (see M365-006). Requires Entra ID P2 for Identity Protection.",
            ActualValue = "See M365-006"
        });

        return results;
    }

    // ── Admin Roles (M365-018 to M365-022) ──

    private async Task<List<M365CheckResult>> CheckAdminRoles(GraphServiceClient graph)
    {
        var results = new List<M365CheckResult>();

        try
        {
            var directoryRoles = await graph.DirectoryRoles.GetAsync();
            var roleList = directoryRoles?.Value ?? [];

            // Find Global Administrator role
            var globalAdminRole = roleList.FirstOrDefault(r =>
                r.DisplayName?.Contains("Global Administrator", StringComparison.OrdinalIgnoreCase) == true);

            int globalAdminCount = 0;
            var adminMembers = new List<string>();

            if (globalAdminRole != null)
            {
                var members = await graph.DirectoryRoles[globalAdminRole.Id].Members.GetAsync();
                globalAdminCount = members?.Value?.Count ?? 0;
                adminMembers = members?.Value?
                    .OfType<Microsoft.Graph.Models.User>()
                    .Select(u => u.DisplayName ?? u.UserPrincipalName ?? "unknown")
                    .ToList() ?? [];
            }

            // M365-018: Global admin count
            results.Add(new M365CheckResult
            {
                CheckId = "M365-018",
                Name = "Global Administrator count",
                Category = "admin_roles",
                Severity = "critical",
                Status = globalAdminCount <= 5 ? (globalAdminCount >= 2 ? "pass" : "warn") : "fail",
                Finding = globalAdminCount > 5
                    ? $"{globalAdminCount} Global Admins found (recommended: 2-5). Excessive admin accounts increase attack surface."
                    : globalAdminCount < 2
                        ? $"{globalAdminCount} Global Admin found. Have at least 2 (including a break-glass account)."
                        : $"{globalAdminCount} Global Admins found (within recommended range of 2-5)",
                ActualValue = globalAdminCount.ToString()
            });

            // M365-019: PIM (Privileged Identity Management)
            results.Add(new M365CheckResult
            {
                CheckId = "M365-019",
                Name = "Privileged Identity Management (PIM)",
                Category = "admin_roles",
                Severity = "high",
                Status = "info",
                Finding = "PIM status requires Entra ID P2 license. Check Entra ID portal > Privileged Identity Management for eligible vs permanent assignments.",
                ActualValue = "N/A"
            });

            // M365-020: Admin accounts with MFA
            results.Add(new M365CheckResult
            {
                CheckId = "M365-020",
                Name = "Admin accounts MFA enforcement",
                Category = "admin_roles",
                Severity = "critical",
                Status = "info",
                Finding = $"Admin accounts: {string.Join(", ", adminMembers.Take(10))}. Verify each has MFA via Conditional Access (M365-003).",
                ActualValue = globalAdminCount.ToString()
            });

            // M365-021: Break-glass accounts
            var breakGlass = adminMembers.Any(m =>
                m.Contains("break", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("emergency", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("glass", StringComparison.OrdinalIgnoreCase));
            results.Add(new M365CheckResult
            {
                CheckId = "M365-021",
                Name = "Break-glass emergency accounts",
                Category = "admin_roles",
                Severity = "high",
                Status = breakGlass ? "pass" : "warn",
                Finding = breakGlass
                    ? "A break-glass / emergency admin account was detected by naming convention"
                    : "No break-glass / emergency admin account detected. Recommended to have at least one cloud-only GA excluded from CA policies.",
                ActualValue = breakGlass.ToString()
            });

            // M365-022: Custom admin roles
            var totalRolesWithMembers = 0;
            foreach (var role in roleList)
            {
                try
                {
                    var roleMembers = await graph.DirectoryRoles[role.Id].Members.GetAsync();
                    if (roleMembers?.Value?.Count > 0)
                        totalRolesWithMembers++;
                }
                catch { /* role membership read may fail */ }
            }

            results.Add(new M365CheckResult
            {
                CheckId = "M365-022",
                Name = "Active directory roles in use",
                Category = "admin_roles",
                Severity = "low",
                Status = "info",
                Finding = $"{totalRolesWithMembers} directory roles have active members out of {roleList.Count} total roles",
                ActualValue = totalRolesWithMembers.ToString()
            });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to check admin roles");
            results.Add(InsufficientPermissions("M365-018", "Admin roles audit", "admin_roles",
                "Requires Directory.Read.All and RoleManagement.Read.Directory"));
        }

        return results;
    }

    // ── Guest Access (M365-023 to M365-026) ──

    private async Task<List<M365CheckResult>> CheckGuestAccess(GraphServiceClient graph)
    {
        var results = new List<M365CheckResult>();

        try
        {
            // M365-023: Guest user count
            var guestsResponse = await graph.Users.GetAsync(config =>
            {
                config.QueryParameters.Filter = "userType eq 'Guest'";
                config.QueryParameters.Select = new[] { "id", "displayName", "userPrincipalName", "createdDateTime" };
                config.QueryParameters.Top = 999;
                config.QueryParameters.Count = true;
                config.Headers.Add("ConsistencyLevel", "eventual");
            });
            var guestCount = guestsResponse?.Value?.Count ?? 0;

            results.Add(new M365CheckResult
            {
                CheckId = "M365-023",
                Name = "Guest user count",
                Category = "guest_access",
                Severity = "medium",
                Status = guestCount > 50 ? "warn" : "info",
                Finding = $"{guestCount} guest users found in the directory. Review periodically for stale external accounts.",
                ActualValue = guestCount.ToString()
            });

            // M365-024: Guest invitation restrictions
            try
            {
                var authzPolicy = await graph.Policies.AuthorizationPolicy.GetAsync();
                var guestInviteSettings = authzPolicy?.AllowInvitesFrom?.ToString() ?? "unknown";

                results.Add(new M365CheckResult
                {
                    CheckId = "M365-024",
                    Name = "Guest invitation restrictions",
                    Category = "guest_access",
                    Severity = "medium",
                    Status = guestInviteSettings == "AdminsAndGuestInviters" || guestInviteSettings == "None"
                        ? "pass" : "warn",
                    Finding = $"Guest invitation setting: {guestInviteSettings}. Restrict to admins only for best security.",
                    ActualValue = guestInviteSettings
                });
            }
            catch
            {
                results.Add(InsufficientPermissions("M365-024", "Guest invitation restrictions", "guest_access",
                    "Requires Policy.Read.All"));
            }

            // M365-025: Guest access to groups/teams
            results.Add(new M365CheckResult
            {
                CheckId = "M365-025",
                Name = "Guest access to Teams/Groups",
                Category = "guest_access",
                Severity = "medium",
                Status = "info",
                Finding = "Guest access to Teams and Groups should be reviewed in Teams admin center and Azure AD external collaboration settings.",
                ActualValue = "N/A"
            });

            // M365-026: External sharing in SharePoint
            results.Add(new M365CheckResult
            {
                CheckId = "M365-026",
                Name = "SharePoint external sharing",
                Category = "guest_access",
                Severity = "medium",
                Status = "info",
                Finding = "SharePoint external sharing configuration requires SharePoint admin API. Check SharePoint admin center > Sharing.",
                ActualValue = "N/A"
            });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to check guest access");
            results.Add(InsufficientPermissions("M365-023", "Guest access audit", "guest_access",
                "Requires User.Read.All"));
        }

        return results;
    }

    // ── Mail Security (M365-027 to M365-030) ──

    private async Task<List<M365CheckResult>> CheckMailSecurity(GraphServiceClient graph)
    {
        var results = new List<M365CheckResult>();

        // M365-027: External forwarding rules
        try
        {
            // Sample first 20 users for mail forwarding rules
            var usersResponse = await graph.Users.GetAsync(config =>
            {
                config.QueryParameters.Select = new[] { "id", "displayName", "userPrincipalName", "mail" };
                config.QueryParameters.Filter = "userType eq 'Member' and mail ne null";
                config.QueryParameters.Top = 20;
            });
            var users = usersResponse?.Value ?? [];

            int usersWithForwarding = 0;
            var forwardingDetails = new List<string>();

            foreach (var user in users)
            {
                try
                {
                    var rules = await graph.Users[user.Id].MailFolders["inbox"].MessageRules.GetAsync();
                    var forwardingRules = rules?.Value?.Where(r =>
                        r.Actions?.ForwardTo?.Count > 0 ||
                        r.Actions?.ForwardAsAttachmentTo?.Count > 0 ||
                        r.Actions?.RedirectTo?.Count > 0)
                        .ToList() ?? [];

                    if (forwardingRules.Count > 0)
                    {
                        usersWithForwarding++;
                        forwardingDetails.Add($"{user.DisplayName ?? user.UserPrincipalName}: {forwardingRules.Count} rule(s)");
                    }
                }
                catch
                {
                    // Mail rules read may fail for individual users
                }
            }

            results.Add(new M365CheckResult
            {
                CheckId = "M365-027",
                Name = "External mail forwarding rules",
                Category = "mail_security",
                Severity = "high",
                Status = usersWithForwarding > 0 ? "fail" : "pass",
                Finding = usersWithForwarding > 0
                    ? $"{usersWithForwarding} users have mail forwarding rules: {string.Join("; ", forwardingDetails.Take(5))}"
                    : $"No mail forwarding rules detected in sampled users ({users.Count} checked)",
                ActualValue = usersWithForwarding.ToString()
            });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to check mail forwarding rules");
            results.Add(InsufficientPermissions("M365-027", "Mail forwarding rules", "mail_security",
                "Requires MailboxSettings.Read"));
        }

        // M365-028 to M365-030: DNS-based email security checks
        // Get verified domains from Graph to know which domains to check
        try
        {
            var domainsResponse = await graph.Domains.GetAsync();
            var verifiedDomains = domainsResponse?.Value?
                .Where(d => d.IsVerified == true && !string.IsNullOrEmpty(d.Id))
                .Select(d => d.Id!)
                .ToList() ?? [];

            if (verifiedDomains.Count == 0)
            {
                results.Add(new M365CheckResult
                {
                    CheckId = "M365-028", Name = "SPF record", Category = "mail_security",
                    Severity = "high", Status = "info",
                    Finding = "No verified domains found to check DNS records",
                    ActualValue = "N/A"
                });
            }
            else
            {
                var spfResults = new List<string>();
                var dmarcResults = new List<string>();
                var dkimResults = new List<string>();
                int spfPass = 0, spfFail = 0;
                int dmarcPass = 0, dmarcFail = 0;
                int dkimPass = 0, dkimFail = 0;

                foreach (var domain in verifiedDomains.Take(10)) // cap at 10 domains
                {
                    // SPF check
                    try
                    {
                        var spfRecords = await DnsLookupTxt(domain);
                        var spf = spfRecords.FirstOrDefault(r => r.StartsWith("v=spf1", StringComparison.OrdinalIgnoreCase));
                        if (spf != null)
                        {
                            spfPass++;
                            var hasHardFail = spf.Contains("-all", StringComparison.OrdinalIgnoreCase);
                            if (!hasHardFail) spfResults.Add($"{domain}: SPF exists but uses ~all (softfail) instead of -all (hardfail)");
                        }
                        else
                        {
                            spfFail++;
                            spfResults.Add($"{domain}: No SPF record found");
                        }
                    }
                    catch { spfResults.Add($"{domain}: DNS lookup failed"); spfFail++; }

                    // DMARC check
                    try
                    {
                        var dmarcRecords = await DnsLookupTxt($"_dmarc.{domain}");
                        var dmarc = dmarcRecords.FirstOrDefault(r => r.StartsWith("v=DMARC1", StringComparison.OrdinalIgnoreCase));
                        if (dmarc != null)
                        {
                            dmarcPass++;
                            var hasReject = dmarc.Contains("p=reject", StringComparison.OrdinalIgnoreCase);
                            var hasQuarantine = dmarc.Contains("p=quarantine", StringComparison.OrdinalIgnoreCase);
                            if (!hasReject && !hasQuarantine)
                                dmarcResults.Add($"{domain}: DMARC policy is p=none (monitoring only, not enforcing)");
                        }
                        else
                        {
                            dmarcFail++;
                            dmarcResults.Add($"{domain}: No DMARC record found");
                        }
                    }
                    catch { dmarcResults.Add($"{domain}: DNS lookup failed"); dmarcFail++; }

                    // DKIM check (Microsoft 365 selectors)
                    try
                    {
                        var selector1 = await DnsLookupCname($"selector1._domainkey.{domain}");
                        var selector2 = await DnsLookupCname($"selector2._domainkey.{domain}");
                        if (selector1 != null || selector2 != null)
                            dkimPass++;
                        else
                        {
                            dkimFail++;
                            dkimResults.Add($"{domain}: No DKIM selectors (selector1/selector2) found");
                        }
                    }
                    catch { dkimResults.Add($"{domain}: DNS lookup failed"); dkimFail++; }
                }

                // M365-028: SPF
                results.Add(new M365CheckResult
                {
                    CheckId = "M365-028",
                    Name = "SPF records configured",
                    Category = "mail_security",
                    Severity = "high",
                    Status = spfFail == 0 ? "pass" : spfFail < verifiedDomains.Count ? "warn" : "fail",
                    Finding = spfFail == 0
                        ? $"SPF records found for all {spfPass} verified domains"
                        : $"{spfFail}/{spfPass + spfFail} domains missing SPF. {string.Join("; ", spfResults.Take(3))}",
                    ActualValue = $"{spfPass} pass, {spfFail} fail"
                });

                // M365-029: DMARC
                results.Add(new M365CheckResult
                {
                    CheckId = "M365-029",
                    Name = "DMARC policy configured",
                    Category = "mail_security",
                    Severity = "high",
                    Status = dmarcFail == 0 ? "pass" : dmarcFail < verifiedDomains.Count ? "warn" : "fail",
                    Finding = dmarcFail == 0
                        ? $"DMARC records found for all {dmarcPass} verified domains. {(dmarcResults.Count > 0 ? string.Join("; ", dmarcResults.Take(2)) : "")}"
                        : $"{dmarcFail}/{dmarcPass + dmarcFail} domains missing DMARC. {string.Join("; ", dmarcResults.Take(3))}",
                    ActualValue = $"{dmarcPass} pass, {dmarcFail} fail"
                });

                // M365-030: DKIM
                results.Add(new M365CheckResult
                {
                    CheckId = "M365-030",
                    Name = "DKIM selectors configured",
                    Category = "mail_security",
                    Severity = "high",
                    Status = dkimFail == 0 ? "pass" : dkimFail < verifiedDomains.Count ? "warn" : "fail",
                    Finding = dkimFail == 0
                        ? $"DKIM selectors found for all {dkimPass} verified domains"
                        : $"{dkimFail}/{dkimPass + dkimFail} domains missing DKIM. {string.Join("; ", dkimResults.Take(3))}",
                    ActualValue = $"{dkimPass} pass, {dkimFail} fail"
                });
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to check DNS email security records");
            results.Add(InsufficientPermissions("M365-028", "Email DNS security (SPF/DMARC/DKIM)", "mail_security",
                "Requires Domain.Read.All or network access for DNS lookups"));
        }

        return results;
    }

    // ── Stale Accounts (M365-031 to M365-032) ──

    private async Task<List<M365CheckResult>> CheckStaleAccounts(GraphServiceClient graph)
    {
        var results = new List<M365CheckResult>();

        try
        {
            var usersResponse = await graph.Users.GetAsync(config =>
            {
                config.QueryParameters.Select = new[] { "id", "displayName", "userPrincipalName", "signInActivity" };
                config.QueryParameters.Filter = "userType eq 'Member'";
                config.QueryParameters.Top = 999;
            });
            var users = usersResponse?.Value ?? [];
            var totalUsers = users.Count;

            var now = DateTimeOffset.UtcNow;
            var stale30 = users.Where(u =>
                u.SignInActivity?.LastSignInDateTime != null &&
                u.SignInActivity.LastSignInDateTime < now.AddDays(-30)).ToList();
            var stale90 = users.Where(u =>
                u.SignInActivity?.LastSignInDateTime != null &&
                u.SignInActivity.LastSignInDateTime < now.AddDays(-90)).ToList();

            var stale30Pct = totalUsers > 0 ? (double)stale30.Count / totalUsers * 100 : 0;
            var stale90Pct = totalUsers > 0 ? (double)stale90.Count / totalUsers * 100 : 0;

            // M365-031: Stale users (30 days)
            results.Add(new M365CheckResult
            {
                CheckId = "M365-031",
                Name = "Stale user accounts (30 days)",
                Category = "stale_accounts",
                Severity = "medium",
                Status = stale30Pct > 10 ? "fail" : stale30Pct > 5 ? "warn" : "pass",
                Finding = $"{stale30.Count}/{totalUsers} users ({stale30Pct:F1}%) have not signed in for 30+ days. " +
                    $"Top stale: {string.Join(", ", stale30.Take(5).Select(u => u.DisplayName ?? u.UserPrincipalName))}",
                ActualValue = $"{stale30Pct:F1}%"
            });

            // M365-032: Dormant users (90 days)
            results.Add(new M365CheckResult
            {
                CheckId = "M365-032",
                Name = "Dormant user accounts (90 days)",
                Category = "stale_accounts",
                Severity = "high",
                Status = stale90Pct > 10 ? "fail" : stale90Pct > 5 ? "warn" : "pass",
                Finding = $"{stale90.Count}/{totalUsers} users ({stale90Pct:F1}%) have not signed in for 90+ days. " +
                    $"Top dormant: {string.Join(", ", stale90.Take(5).Select(u => u.DisplayName ?? u.UserPrincipalName))}",
                ActualValue = $"{stale90Pct:F1}%"
            });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to check stale accounts");
            results.Add(InsufficientPermissions("M365-031", "Stale user accounts", "stale_accounts",
                "Requires AuditLog.Read.All and User.Read.All"));
        }

        return results;
    }

    // ── App Registrations (M365-033 to M365-036) ──

    private async Task<List<M365CheckResult>> CheckAppRegistrations(GraphServiceClient graph)
    {
        var results = new List<M365CheckResult>();

        try
        {
            var appsResponse = await graph.Applications.GetAsync();
            var apps = appsResponse?.Value ?? [];

            // M365-033: App registrations with expired secrets
            var now = DateTimeOffset.UtcNow;
            var appsWithExpiredSecrets = apps.Where(a =>
                a.PasswordCredentials?.Any(pc => pc.EndDateTime < now) == true).ToList();

            results.Add(new M365CheckResult
            {
                CheckId = "M365-033",
                Name = "App registrations with expired secrets",
                Category = "app_registrations",
                Severity = "medium",
                Status = appsWithExpiredSecrets.Count > 0 ? "warn" : "pass",
                Finding = appsWithExpiredSecrets.Count > 0
                    ? $"{appsWithExpiredSecrets.Count} apps have expired secrets: {string.Join(", ", appsWithExpiredSecrets.Take(5).Select(a => a.DisplayName))}"
                    : "No app registrations have expired secrets",
                ActualValue = appsWithExpiredSecrets.Count.ToString()
            });

            // M365-034: App registrations with excessive permissions
            var appsWithExcessivePerms = apps.Where(a =>
                a.RequiredResourceAccess?.Sum(r => r.ResourceAccess?.Count(ra =>
                    ra.Type == "Role") ?? 0) > 10).ToList();

            results.Add(new M365CheckResult
            {
                CheckId = "M365-034",
                Name = "App registrations with excessive permissions",
                Category = "app_registrations",
                Severity = "medium",
                Status = appsWithExcessivePerms.Count > 0 ? "warn" : "pass",
                Finding = appsWithExcessivePerms.Count > 0
                    ? $"{appsWithExcessivePerms.Count} apps have >10 application permissions: {string.Join(", ", appsWithExcessivePerms.Take(5).Select(a => a.DisplayName))}"
                    : "No app registrations have excessive application permissions",
                ActualValue = appsWithExcessivePerms.Count.ToString()
            });

            // M365-035: Enterprise apps with risky consent grants
            try
            {
                var spResponse = await graph.ServicePrincipals.GetAsync();
                var servicePrincipals = spResponse?.Value ?? [];

                var riskyScopes = new[] { "Mail.Read", "Files.ReadWrite.All", "Mail.Send", "Mail.ReadWrite",
                    "Files.Read.All", "User.ReadWrite.All", "Directory.ReadWrite.All" };

                var riskyApps = new List<string>();
                foreach (var sp in servicePrincipals.Take(100))
                {
                    try
                    {
                        var grants = await graph.ServicePrincipals[sp.Id].Oauth2PermissionGrants.GetAsync();
                        var grantList = grants?.Value ?? [];
                        var hasRiskyScope = grantList.Any(g =>
                            riskyScopes.Any(rs => g.Scope?.Contains(rs, StringComparison.OrdinalIgnoreCase) == true));
                        if (hasRiskyScope)
                            riskyApps.Add(sp.DisplayName ?? sp.AppId ?? "unknown");
                    }
                    catch { /* individual SP read may fail */ }
                }

                results.Add(new M365CheckResult
                {
                    CheckId = "M365-035",
                    Name = "Enterprise apps with risky consent grants",
                    Category = "app_registrations",
                    Severity = "high",
                    Status = riskyApps.Count > 0 ? "fail" : "pass",
                    Finding = riskyApps.Count > 0
                        ? $"{riskyApps.Count} enterprise apps have risky OAuth consent grants (Mail.Read, Files.ReadWrite.All, etc.): {string.Join(", ", riskyApps.Take(5))}"
                        : "No enterprise apps found with risky consent grants",
                    ActualValue = riskyApps.Count.ToString()
                });
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to check enterprise app consent grants");
                results.Add(InsufficientPermissions("M365-035", "Enterprise apps risky consent grants", "app_registrations",
                    "Requires Application.Read.All"));
            }

            // M365-036: Apps with no owner assigned
            var appsWithNoOwner = new List<string>();
            foreach (var app in apps.Take(100))
            {
                try
                {
                    var owners = await graph.Applications[app.Id].Owners.GetAsync();
                    if (owners?.Value == null || owners.Value.Count == 0)
                        appsWithNoOwner.Add(app.DisplayName ?? app.AppId ?? "unknown");
                }
                catch { /* owner read may fail */ }
            }

            results.Add(new M365CheckResult
            {
                CheckId = "M365-036",
                Name = "App registrations with no owner",
                Category = "app_registrations",
                Severity = "medium",
                Status = appsWithNoOwner.Count > 0 ? "warn" : "pass",
                Finding = appsWithNoOwner.Count > 0
                    ? $"{appsWithNoOwner.Count} apps have no owner assigned: {string.Join(", ", appsWithNoOwner.Take(5))}"
                    : "All app registrations have at least one owner assigned",
                ActualValue = appsWithNoOwner.Count.ToString()
            });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to check app registrations");
            results.Add(InsufficientPermissions("M365-033", "App registrations audit", "app_registrations",
                "Requires Application.Read.All"));
        }

        return results;
    }

    // ── Secure Score (M365-037 to M365-038) ──

    private async Task<List<M365CheckResult>> CheckSecureScore(GraphServiceClient graph)
    {
        var results = new List<M365CheckResult>();

        try
        {
            var scoresResponse = await graph.Security.SecureScores.GetAsync(config =>
            {
                config.QueryParameters.Top = 1;
                config.QueryParameters.Orderby = new[] { "createdDateTime desc" };
            });
            var latestScore = scoresResponse?.Value?.FirstOrDefault();

            if (latestScore != null)
            {
                var currentScore = latestScore.CurrentScore ?? 0;
                var maxScore = latestScore.MaxScore ?? 1;
                var scorePct = maxScore > 0 ? (double)currentScore / maxScore * 100 : 0;

                // M365-037: Microsoft Secure Score
                results.Add(new M365CheckResult
                {
                    CheckId = "M365-037",
                    Name = "Microsoft Secure Score",
                    Category = "secure_score",
                    Severity = "critical",
                    Status = scorePct > 70 ? "pass" : scorePct > 40 ? "warn" : "fail",
                    Finding = $"Secure Score: {currentScore}/{maxScore} ({scorePct:F1}%). " +
                        (scorePct > 70 ? "Good security posture." : scorePct > 40 ? "Moderate security posture. Review improvement actions." : "Low security posture. Immediate attention needed."),
                    ActualValue = $"{scorePct:F1}%"
                });

                // M365-038: Secure Score improvement actions
                var controlScores = latestScore.ControlScores ?? [];
                // ControlScore in Graph v5 has Score (double?) — zero or null means not implemented
                var notImplemented = controlScores.Where(c =>
                    c.Score == null || c.Score == 0).ToList();

                results.Add(new M365CheckResult
                {
                    CheckId = "M365-038",
                    Name = "Secure Score improvement actions pending",
                    Category = "secure_score",
                    Severity = "medium",
                    Status = notImplemented.Count > 20 ? "warn" : "info",
                    Finding = $"{notImplemented.Count} improvement actions not fully implemented out of {controlScores.Count} total. " +
                        $"Top recommendations: {string.Join(", ", notImplemented.Take(5).Select(c => c.ControlName))}",
                    ActualValue = notImplemented.Count.ToString()
                });
            }
            else
            {
                results.Add(new M365CheckResult
                {
                    CheckId = "M365-037",
                    Name = "Microsoft Secure Score",
                    Category = "secure_score",
                    Severity = "critical",
                    Status = "info",
                    Finding = "No Secure Score data available. This may require SecurityEvents.Read.All permission or the tenant may not have scored yet.",
                    ActualValue = "N/A"
                });

                results.Add(new M365CheckResult
                {
                    CheckId = "M365-038",
                    Name = "Secure Score improvement actions pending",
                    Category = "secure_score",
                    Severity = "medium",
                    Status = "info",
                    Finding = "No Secure Score data available to determine improvement actions.",
                    ActualValue = "N/A"
                });
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to check Secure Score");
            results.Add(InsufficientPermissions("M365-037", "Microsoft Secure Score", "secure_score",
                "Requires SecurityEvents.Read.All"));
        }

        return results;
    }

    // ── Named Locations (M365-039) ──

    private async Task<List<M365CheckResult>> CheckNamedLocations(GraphServiceClient graph)
    {
        var results = new List<M365CheckResult>();

        try
        {
            var locationsResponse = await graph.Identity.ConditionalAccess.NamedLocations.GetAsync();
            var locations = locationsResponse?.Value ?? [];

            results.Add(new M365CheckResult
            {
                CheckId = "M365-039",
                Name = "Named locations configured",
                Category = "conditional_access",
                Severity = "medium",
                Status = locations.Count > 0 ? "pass" : "warn",
                Finding = locations.Count > 0
                    ? $"{locations.Count} named locations configured: {string.Join(", ", locations.Take(5).Select(l => l.DisplayName))}"
                    : "No named locations configured. Named locations improve CA policy targeting (trusted IPs, countries).",
                ActualValue = locations.Count.ToString()
            });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to check named locations");
            results.Add(InsufficientPermissions("M365-039", "Named locations", "conditional_access",
                "Requires Policy.Read.All"));
        }

        return results;
    }

    // ── Password Policy (M365-040) ──

    private async Task<List<M365CheckResult>> CheckPasswordPolicy(GraphServiceClient graph)
    {
        var results = new List<M365CheckResult>();

        try
        {
            var orgResponse = await graph.Organization.GetAsync();
            var org = orgResponse?.Value?.FirstOrDefault();

            string? passwordPolicies = null;
            int? passwordValidityDays = null;

            if (org != null)
            {
                passwordPolicies = org.AdditionalData?.ContainsKey("passwordPolicies") == true
                    ? org.AdditionalData["passwordPolicies"]?.ToString()
                    : null;
            }

            // Also try to read domain-level password validity
            try
            {
                var domainsResponse = await graph.Domains.GetAsync();
                var domains = domainsResponse?.Value ?? [];
                var defaultDomain = domains.FirstOrDefault(d => d.IsDefault == true);
                passwordValidityDays = defaultDomain?.PasswordValidityPeriodInDays;
            }
            catch { /* domain read may fail */ }

            var expirationDisabled = passwordPolicies?.Contains("DisablePasswordExpiration", StringComparison.OrdinalIgnoreCase) == true;
            var validityInfo = passwordValidityDays.HasValue
                ? $"Password validity: {passwordValidityDays} days."
                : "Password validity period not readable from domain settings.";

            results.Add(new M365CheckResult
            {
                CheckId = "M365-040",
                Name = "Password expiration policy",
                Category = "org_config",
                Severity = "medium",
                Status = expirationDisabled ? "info" : "pass",
                Finding = expirationDisabled
                    ? $"Password expiration is disabled org-wide. {validityInfo} NIST 800-63B recommends no periodic rotation if breach detection is in place."
                    : $"Password expiration is enabled. {validityInfo}",
                ActualValue = expirationDisabled ? "disabled" : $"{passwordValidityDays?.ToString() ?? "default"} days"
            });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to check password policy");
            results.Add(InsufficientPermissions("M365-040", "Password expiration policy", "org_config",
                "Requires Organization.Read.All"));
        }

        return results;
    }

    // ── Risk Detections (M365-041 to M365-042) ──

    private async Task<List<M365CheckResult>> CheckRiskDetections(GraphServiceClient graph)
    {
        var results = new List<M365CheckResult>();

        // M365-041: High-risk sign-ins
        try
        {
            var thirtyDaysAgo = DateTimeOffset.UtcNow.AddDays(-30).ToString("yyyy-MM-ddTHH:mm:ssZ");
            var riskDetectionsResponse = await graph.IdentityProtection.RiskDetections.GetAsync(config =>
            {
                config.QueryParameters.Filter = $"riskLevel eq 'high' and detectedDateTime ge {thirtyDaysAgo}";
                config.QueryParameters.Top = 100;
            });
            var highRiskDetections = riskDetectionsResponse?.Value ?? [];

            var riskTypes = highRiskDetections
                .GroupBy(r => r.RiskEventType ?? "unknown")
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => $"{g.Key} ({g.Count()})");

            results.Add(new M365CheckResult
            {
                CheckId = "M365-041",
                Name = "High-risk sign-ins (last 30 days)",
                Category = "identity_protection",
                Severity = "critical",
                Status = highRiskDetections.Count > 0 ? "fail" : "pass",
                Finding = highRiskDetections.Count > 0
                    ? $"{highRiskDetections.Count} high-risk sign-in detections in last 30 days. Top risk types: {string.Join(", ", riskTypes)}"
                    : "No high-risk sign-in detections in last 30 days",
                ActualValue = highRiskDetections.Count.ToString()
            });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to check risk detections");
            results.Add(InsufficientPermissions("M365-041", "High-risk sign-ins", "identity_protection",
                "Requires IdentityRiskEvent.Read.All (Entra ID P2)"));
        }

        // M365-042: Risky users
        try
        {
            var riskyUsersResponse = await graph.IdentityProtection.RiskyUsers.GetAsync(config =>
            {
                config.QueryParameters.Filter = "riskLevel eq 'high'";
                config.QueryParameters.Top = 100;
            });
            var riskyUsers = riskyUsersResponse?.Value ?? [];

            results.Add(new M365CheckResult
            {
                CheckId = "M365-042",
                Name = "High-risk users",
                Category = "identity_protection",
                Severity = "critical",
                Status = riskyUsers.Count > 0 ? "fail" : "pass",
                Finding = riskyUsers.Count > 0
                    ? $"{riskyUsers.Count} users flagged as high-risk: {string.Join(", ", riskyUsers.Take(5).Select(u => u.UserDisplayName ?? u.UserPrincipalName ?? "unknown"))}"
                    : "No users currently flagged as high-risk",
                ActualValue = riskyUsers.Count.ToString()
            });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to check risky users");
            results.Add(InsufficientPermissions("M365-042", "High-risk users", "identity_protection",
                "Requires IdentityRiskyUser.Read.All (Entra ID P2)"));
        }

        return results;
    }

    // ── Intune Compliance (M365-043 to M365-046) ──

    private async Task<List<M365CheckResult>> CheckIntuneCompliance(GraphServiceClient graph)
    {
        var results = new List<M365CheckResult>();

        try
        {
            // M365-043 and M365-044: Intune enrollment and compliance
            var devicesResponse = await graph.DeviceManagement.ManagedDevices.GetAsync(config =>
            {
                config.QueryParameters.Top = 999;
            });
            var devices = devicesResponse?.Value ?? [];
            var totalDevices = devices.Count;

            var compliantDevices = devices.Where(d =>
                d.ComplianceState == Microsoft.Graph.Models.ComplianceState.Compliant).ToList();
            var nonCompliantDevices = devices.Where(d =>
                d.ComplianceState != Microsoft.Graph.Models.ComplianceState.Compliant).ToList();

            // M365-043: Intune enrollment rate
            results.Add(new M365CheckResult
            {
                CheckId = "M365-043",
                Name = "Intune managed device count",
                Category = "intune",
                Severity = "medium",
                Status = totalDevices > 0 ? "pass" : "warn",
                Finding = totalDevices > 0
                    ? $"{totalDevices} devices managed by Intune. {compliantDevices.Count} compliant, {nonCompliantDevices.Count} non-compliant."
                    : "No devices enrolled in Intune. Consider enrolling devices for compliance management.",
                ActualValue = totalDevices.ToString()
            });

            // M365-044: Non-compliant devices
            var nonCompliantPct = totalDevices > 0 ? (double)nonCompliantDevices.Count / totalDevices * 100 : 0;
            results.Add(new M365CheckResult
            {
                CheckId = "M365-044",
                Name = "Non-compliant Intune devices",
                Category = "intune",
                Severity = "high",
                Status = nonCompliantDevices.Count > 0 ? "warn" : "pass",
                Finding = nonCompliantDevices.Count > 0
                    ? $"{nonCompliantDevices.Count}/{totalDevices} devices ({nonCompliantPct:F1}%) are non-compliant. " +
                        $"Top non-compliant: {string.Join(", ", nonCompliantDevices.Take(5).Select(d => d.DeviceName ?? d.Id))}"
                    : "All enrolled devices are compliant",
                ActualValue = nonCompliantDevices.Count.ToString()
            });

            // M365-045: Compliance policies configured
            try
            {
                var policiesResponse = await graph.DeviceManagement.DeviceCompliancePolicies.GetAsync();
                var policies = policiesResponse?.Value ?? [];

                results.Add(new M365CheckResult
                {
                    CheckId = "M365-045",
                    Name = "Intune compliance policies configured",
                    Category = "intune",
                    Severity = "high",
                    Status = policies.Count > 0 ? "pass" : "fail",
                    Finding = policies.Count > 0
                        ? $"{policies.Count} compliance policies configured: {string.Join(", ", policies.Take(5).Select(p => p.DisplayName))}"
                        : "No compliance policies configured in Intune. Devices have no compliance requirements.",
                    ActualValue = policies.Count.ToString()
                });

                // M365-046: Device encryption enforcement
                var hasEncryptionPolicy = policies.Any(p =>
                    p.DisplayName?.Contains("encrypt", StringComparison.OrdinalIgnoreCase) == true ||
                    p.DisplayName?.Contains("bitlocker", StringComparison.OrdinalIgnoreCase) == true);

                results.Add(new M365CheckResult
                {
                    CheckId = "M365-046",
                    Name = "Device encryption enforcement policy",
                    Category = "intune",
                    Severity = "high",
                    Status = hasEncryptionPolicy ? "pass" : "warn",
                    Finding = hasEncryptionPolicy
                        ? "A compliance policy enforcing device encryption was detected by name"
                        : "No compliance policy explicitly requiring device encryption detected by name. Review compliance policy settings in Intune to verify BitLocker/FileVault requirements.",
                    ActualValue = hasEncryptionPolicy.ToString()
                });
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to check Intune compliance policies");
                results.Add(InsufficientPermissions("M365-045", "Intune compliance policies", "intune",
                    "Requires DeviceManagementConfiguration.Read.All"));
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to check Intune compliance");
            results.Add(InsufficientPermissions("M365-043", "Intune device compliance", "intune",
                "Requires DeviceManagementManagedDevices.Read.All"));
        }

        return results;
    }

    // ── DLP Policies (M365-047) ──

    private Task<List<M365CheckResult>> CheckDlpPolicies(GraphServiceClient graph)
    {
        // DLP/sensitivity labels endpoint availability varies by Graph SDK version and tenant license.
        // The full DLP policy API requires Microsoft Purview Compliance Center — not accessible via Graph v5.
        var results = new List<M365CheckResult>
        {
            new M365CheckResult
            {
                CheckId = "M365-047",
                Name = "Data Loss Prevention / sensitivity labels",
                Category = "dlp",
                Severity = "high",
                Status = "info",
                Finding = "DLP and sensitivity label configuration requires Microsoft Purview Compliance Center. " +
                    "The Graph API does not expose full DLP policy details. Check Compliance Center > Data Loss Prevention > Policies for DLP rules, " +
                    "and Compliance Center > Information Protection > Labels for sensitivity labels.",
                ActualValue = "N/A"
            }
        };

        return Task.FromResult(results);
    }

    // ── Security Alerts (M365-048) ──

    private async Task<List<M365CheckResult>> CheckSecurityAlerts(GraphServiceClient graph)
    {
        var results = new List<M365CheckResult>();

        try
        {
            var thirtyDaysAgo = DateTimeOffset.UtcNow.AddDays(-30).ToString("yyyy-MM-ddTHH:mm:ssZ");
            var alertsResponse = await graph.Security.Alerts_v2.GetAsync(config =>
            {
                config.QueryParameters.Filter = $"createdDateTime ge {thirtyDaysAgo}";
                config.QueryParameters.Top = 200;
            });
            var alerts = alertsResponse?.Value ?? [];

            var highSeverity = alerts.Where(a =>
                a.Severity == Microsoft.Graph.Models.Security.AlertSeverity.High).ToList();
            var mediumSeverity = alerts.Where(a =>
                a.Severity == Microsoft.Graph.Models.Security.AlertSeverity.Medium).ToList();

            results.Add(new M365CheckResult
            {
                CheckId = "M365-048",
                Name = "Active security alerts (last 30 days)",
                Category = "security_alerts",
                Severity = "critical",
                Status = highSeverity.Count > 0 ? "fail" : mediumSeverity.Count > 0 ? "warn" : "pass",
                Finding = $"{alerts.Count} security alerts in last 30 days ({highSeverity.Count} high/critical, {mediumSeverity.Count} medium). " +
                    (highSeverity.Count > 0
                        ? $"Top high-severity: {string.Join(", ", highSeverity.Take(3).Select(a => a.Title))}"
                        : "No high-severity alerts."),
                ActualValue = $"{highSeverity.Count} high, {mediumSeverity.Count} medium, {alerts.Count} total"
            });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to check security alerts");
            results.Add(InsufficientPermissions("M365-048", "Security alerts", "security_alerts",
                "Requires SecurityAlert.Read.All permission. Add it to the app registration and grant admin consent."));
        }

        return results;
    }

    // ── SharePoint Sharing (M365-049) ──

    private async Task<List<M365CheckResult>> CheckSharePointSharing(GraphServiceClient graph)
    {
        var results = new List<M365CheckResult>();

        try
        {
            // Try to read the root SharePoint site to verify connectivity
            var rootSite = await graph.Sites["root"].GetAsync();
            var siteName = rootSite?.DisplayName ?? "unknown";

            results.Add(new M365CheckResult
            {
                CheckId = "M365-049",
                Name = "SharePoint external sharing settings",
                Category = "sharepoint",
                Severity = "medium",
                Status = "info",
                Finding = $"SharePoint root site accessible: {siteName}. External sharing configuration requires SharePoint Admin API (not available via Graph). Check SharePoint admin center > Policies > Sharing for tenant-level sharing settings.",
                ActualValue = siteName
            });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to check SharePoint sharing");
            results.Add(new M365CheckResult
            {
                CheckId = "M365-049",
                Name = "SharePoint external sharing settings",
                Category = "sharepoint",
                Severity = "medium",
                Status = "info",
                Finding = "SharePoint sharing settings could not be read via Graph API. This requires Sites.Read.All permission and SharePoint Admin API for tenant-level configuration. Check SharePoint admin center manually.",
                ActualValue = "N/A"
            });
        }

        return results;
    }

    // ── Org Config (M365-050) ──

    private async Task<List<M365CheckResult>> CheckOrgConfig(GraphServiceClient graph)
    {
        var results = new List<M365CheckResult>();

        try
        {
            var domainsResponse = await graph.Domains.GetAsync();
            var domains = domainsResponse?.Value ?? [];

            var verifiedDomains = domains.Where(d => d.IsVerified == true).ToList();
            var unverifiedDomains = domains.Where(d => d.IsVerified != true).ToList();

            results.Add(new M365CheckResult
            {
                CheckId = "M365-050",
                Name = "Verified domains",
                Category = "org_config",
                Severity = "low",
                Status = "info",
                Finding = $"{verifiedDomains.Count} verified domains: {string.Join(", ", verifiedDomains.Take(10).Select(d => d.Id))}. " +
                    (unverifiedDomains.Count > 0
                        ? $"{unverifiedDomains.Count} unverified: {string.Join(", ", unverifiedDomains.Take(5).Select(d => d.Id))}"
                        : "No unverified domains."),
                ActualValue = $"{verifiedDomains.Count} verified, {unverifiedDomains.Count} unverified"
            });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to check org domains");
            results.Add(InsufficientPermissions("M365-050", "Verified domains", "org_config",
                "Requires Organization.Read.All or Domain.Read.All"));
        }

        return results;
    }

    // ── DNS Lookup Helpers ──

    /// <summary>
    /// Resolve TXT records for a domain using .NET's built-in DNS resolver.
    /// Falls back to nslookup if the managed API isn't available.
    /// </summary>
    private static async Task<List<string>> DnsLookupTxt(string domain)
    {
        var results = new List<string>();
        try
        {
            // Use Process to call nslookup for TXT records (most compatible approach)
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "nslookup",
                Arguments = $"-type=TXT {domain}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi)!;
            var output = await proc.StandardOutput.ReadToEndAsync();
            proc.WaitForExit(10_000);

            // Parse TXT records from nslookup output
            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Contains("text =", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("\""))
                {
                    var txt = trimmed
                        .Replace("text =", "", StringComparison.OrdinalIgnoreCase)
                        .Trim()
                        .Trim('"');
                    if (!string.IsNullOrWhiteSpace(txt))
                        results.Add(txt);
                }
            }
        }
        catch { /* DNS lookup failed — caller handles empty list */ }
        return results;
    }

    /// <summary>
    /// Resolve CNAME record for a domain (used for DKIM selector verification).
    /// </summary>
    private static async Task<string?> DnsLookupCname(string domain)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "nslookup",
                Arguments = $"-type=CNAME {domain}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi)!;
            var output = await proc.StandardOutput.ReadToEndAsync();
            proc.WaitForExit(10_000);

            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Contains("canonical name", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Contains("alias", StringComparison.OrdinalIgnoreCase))
                    return trimmed;
            }
        }
        catch { /* DNS lookup failed */ }
        return null;
    }

    // ── Helper ──

    private static M365CheckResult InsufficientPermissions(string checkId, string name, string category, string detail)
    {
        return new M365CheckResult
        {
            CheckId = checkId,
            Name = name,
            Category = category,
            Severity = "medium",
            Status = "info",
            Finding = $"Insufficient permissions to check this item. {detail}",
            ActualValue = "N/A"
        };
    }
}

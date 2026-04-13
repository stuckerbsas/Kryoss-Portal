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
}

/// <summary>
/// Connects to Microsoft Graph API using client credentials and runs ~30
/// security checks against an M365 / Entra ID tenant.
/// </summary>
public class M365ScannerService : IM365ScannerService
{
    private readonly ILogger<M365ScannerService> _log;

    public M365ScannerService(ILogger<M365ScannerService> log)
    {
        _log = log;
    }

    public async Task<List<M365CheckResult>> ScanAsync(string tenantId, string clientId, string clientSecret)
    {
        var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
        var graph = new GraphServiceClient(credential);

        var results = new List<M365CheckResult>();

        results.AddRange(await CheckConditionalAccess(graph));
        results.AddRange(await CheckMfaStatus(graph));
        results.AddRange(await CheckSecurityDefaults(graph));
        results.AddRange(await CheckAdminRoles(graph));
        results.AddRange(await CheckGuestAccess(graph));
        results.AddRange(await CheckMailSecurity(graph));

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

        // M365-028: DKIM
        results.Add(new M365CheckResult
        {
            CheckId = "M365-028",
            Name = "DKIM configuration",
            Category = "mail_security",
            Severity = "high",
            Status = "info",
            Finding = "DKIM configuration requires Exchange Online Management module. Verify via Exchange admin center > Mail flow > DKIM.",
            ActualValue = "N/A"
        });

        // M365-029: DMARC
        results.Add(new M365CheckResult
        {
            CheckId = "M365-029",
            Name = "DMARC policy",
            Category = "mail_security",
            Severity = "high",
            Status = "info",
            Finding = "DMARC policy is configured via DNS TXT records (_dmarc.domain.com). Verify externally or via DNS lookup.",
            ActualValue = "N/A"
        });

        // M365-030: Anti-phishing policies
        results.Add(new M365CheckResult
        {
            CheckId = "M365-030",
            Name = "Anti-phishing policies",
            Category = "mail_security",
            Severity = "high",
            Status = "info",
            Finding = "Anti-phishing policy configuration requires Exchange Online Protection API. Check Microsoft 365 Defender > Policies > Anti-phishing.",
            ActualValue = "N/A"
        });

        return results;
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

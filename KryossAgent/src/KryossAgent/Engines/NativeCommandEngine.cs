using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Eventing.Reader;
using System.Management;
using System.Text;
using KryossAgent.Models;
using Microsoft.Win32;

namespace KryossAgent.Engines;

/// <summary>
/// Replaces ShellEngine for Type="command" controls with native .NET/WMI/Registry
/// implementations. Uses the control's <c>parent</c> field (e.g. "Test-DeviceJoinStatus")
/// as a discriminator to route to the correct native handler.
///
/// v1.4.0 security hardening: zero Process.Start calls, zero external executables.
/// If a control references an unknown parent, returns an "info" result rather than
/// falling back to shell execution — legacy controls must be migrated or deactivated.
/// </summary>
public class NativeCommandEngine : ICheckEngine
{
    public string Type => "command";

    public List<CheckResult> Execute(IReadOnlyList<ControlDef> controls)
    {
        var results = new List<CheckResult>();

        foreach (var control in controls)
        {
            try
            {
                // v1.5.0: dispatch by CheckType first (tls/user_right/applocker/
                // registry/custom), fall back to Parent for the original 5 handlers.
                var checkType = control.CheckType?.Trim().ToLowerInvariant() ?? "";
                var parent = control.Parent?.Trim() ?? "";

                CheckResult result = checkType switch
                {
                    "tls"        => CheckTls(control),
                    "user_right" => CheckUserRight(control),
                    "applocker"  => CheckAppLocker(control),
                    "registry"   => CheckInlineRegistry(control),
                    "custom"     => CheckCustom(control),
                    _ => parent switch
                    {
                        "Test-DeviceJoinStatus"  => CheckDeviceJoinStatus(control),
                        "Test-WHfBProvisioned"   => CheckNgcContainer(control),
                        "Test-EventLogRetention" => CheckEventLogConfig(control),
                        "Test-BackupPosture"     => CheckBackupPosture(control),
                        "Test-WdacPolicies"      => CheckWdacPolicies(control),
                        _                        => UnsupportedCheck(control, parent)
                    }
                };
                results.Add(result);
            }
            catch (Exception ex)
            {
                results.Add(new CheckResult
                {
                    Id = control.Id,
                    Exists = false,
                    Stderr = $"Native check failed: {ex.Message}"
                });
            }
        }

        return results;
    }

    // ── BL-0449: Device Join Status (dsregcmd /status → registry) ──
    //
    // The portal parses fields like AzureAdJoined, DomainJoined, WamDefaultSet,
    // NgcSet. All of these are readable from registry without spawning dsregcmd.
    private static CheckResult CheckDeviceJoinStatus(ControlDef c)
    {
        var joinInfo = new Dictionary<string, object?>();

        // Azure AD Join → HKLM\SYSTEM\CurrentControlSet\Control\CloudDomainJoin\JoinInfo
        try
        {
            using var joinKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\CloudDomainJoin\JoinInfo");
            var azureAdJoined = false;
            string? tenantId = null;
            string? tenantName = null;
            if (joinKey != null)
            {
                foreach (var subKeyName in joinKey.GetSubKeyNames())
                {
                    using var sub = joinKey.OpenSubKey(subKeyName);
                    if (sub != null)
                    {
                        azureAdJoined = true;
                        tenantId = sub.GetValue("TenantId") as string ?? tenantId;
                        tenantName = sub.GetValue("TenantDisplayName") as string ?? tenantName;
                    }
                }
            }
            joinInfo["AzureAdJoined"] = azureAdJoined;
            if (tenantId != null) joinInfo["TenantId"] = tenantId;
            if (tenantName != null) joinInfo["TenantDisplayName"] = tenantName;
        }
        catch { joinInfo["AzureAdJoined"] = false; }

        // Domain join → Environment.UserDomainName vs MachineName, or registry
        try
        {
            using var tcpipKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters");
            var domain = tcpipKey?.GetValue("Domain") as string ?? "";
            joinInfo["DomainJoined"] = !string.IsNullOrEmpty(domain);
            if (!string.IsNullOrEmpty(domain))
                joinInfo["DomainName"] = domain;
        }
        catch { joinInfo["DomainJoined"] = false; }

        // Workplace Join (Hybrid) → HKCU\SOFTWARE\Microsoft\Windows NT\CurrentVersion\WorkplaceJoin
        try
        {
            using var wpjKey = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\WorkplaceJoin");
            joinInfo["WorkplaceJoined"] = wpjKey != null;
        }
        catch { joinInfo["WorkplaceJoined"] = false; }

        // NGC (Windows Hello for Business) set → registry check
        try
        {
            using var ngcKey = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI\NgcProvisioning");
            joinInfo["NgcSet"] = ngcKey != null;
        }
        catch { joinInfo["NgcSet"] = false; }

        // Value is a primitive summary (bool: any join at all).
        // Full details are in Stdout for server-side parsing.
        var anyJoin = (joinInfo.GetValueOrDefault("AzureAdJoined") as bool? ?? false)
            || (joinInfo.GetValueOrDefault("DomainJoined") as bool? ?? false)
            || (joinInfo.GetValueOrDefault("WorkplaceJoined") as bool? ?? false);

        return new CheckResult
        {
            Id = c.Id,
            Exists = true,
            Value = anyJoin,
            Stdout = ToJson(joinInfo)
        };
    }

    // ── BL-0450: NGC Container (cmd.exe dir Ngc → Directory.Exists + enum) ──
    //
    // Non-empty NGC folder = at least one user has provisioned WHfB.
    // Directory.Exists + GetDirectories replaces `cmd.exe /c dir`.
    private static CheckResult CheckNgcContainer(ControlDef c)
    {
        var ngcPath = @"C:\Windows\ServiceProfiles\LocalService\AppData\Local\Microsoft\Ngc";

        try
        {
            if (!Directory.Exists(ngcPath))
            {
                return new CheckResult
                {
                    Id = c.Id,
                    Exists = false,
                    Value = false,
                    Stdout = "NGC folder does not exist"
                };
            }

            var subdirs = Directory.GetDirectories(ngcPath);
            var provisioned = subdirs.Length > 0;

            return new CheckResult
            {
                Id = c.Id,
                Exists = true,
                Value = provisioned,
                Stdout = $"NGC folder exists with {subdirs.Length} entries"
            };
        }
        catch (UnauthorizedAccessException)
        {
            // NGC folder ACL blocks enumeration for non-SYSTEM — that's normal
            return new CheckResult
            {
                Id = c.Id,
                Exists = true,
                Value = (object?)null,
                Stdout = "NGC folder exists but cannot be enumerated (ACL)"
            };
        }
    }

    // ── BL-0458 / BL-0459: Event Log Config (wevtutil gl → EventLogConfiguration) ──
    //
    // EventLogEngine already does this for type="eventlog" controls, but these
    // legacy controls use type="command". We use System.Diagnostics.Eventing.Reader.
    private static CheckResult CheckEventLogConfig(ControlDef c)
    {
        // Derive log name from control ID: BL-0458 → Security, BL-0459 → System
        var logName = c.Id switch
        {
            "BL-0458" => "Security",
            "BL-0459" => "System",
            _ => c.LogName ?? "System"
        };

        try
        {
            using var cfg = new EventLogConfiguration(logName);
            var data = new Dictionary<string, object?>
            {
                ["LogName"] = cfg.LogName,
                ["MaximumSizeInBytes"] = cfg.MaximumSizeInBytes,
                ["LogMode"] = cfg.LogMode.ToString(),
                ["IsEnabled"] = cfg.IsEnabled,
                ["LogFilePath"] = cfg.LogFilePath,
                ["SecurityDescriptor"] = cfg.SecurityDescriptor
            };

            // Value = MaximumSizeInBytes (primitive long) for quick server eval
            return new CheckResult
            {
                Id = c.Id,
                Exists = true,
                Value = cfg.MaximumSizeInBytes,
                Stdout = ToJson(data)
            };
        }
        catch (Exception ex)
        {
            return new CheckResult
            {
                Id = c.Id,
                Exists = false,
                Stderr = $"Cannot read {logName} log config: {ex.Message}"
            };
        }
    }

    // ── BL-0460 / BL-0461 / BL-0462: Backup Posture (wbadmin/vssadmin → WMI) ──
    //
    // Uses WMI Win32_ShadowCopy (read-only query, no process spawn) plus registry
    // checks for Windows Backup history. BL-0462 (VSS writers health) requires the
    // VSS COM API which isn't AOT-safe — we return the shadow copy info instead.
    private static CheckResult CheckBackupPosture(ControlDef c)
    {
        var data = new Dictionary<string, object?>();

        try
        {
            // Query shadow copies via WMI
            using var searcher = new ManagementObjectSearcher(
                @"root\CIMV2",
                "SELECT * FROM Win32_ShadowCopy");
            var shadows = new List<Dictionary<string, object?>>();
            foreach (ManagementObject shadow in searcher.Get())
            {
                shadows.Add(new Dictionary<string, object?>
                {
                    ["ID"] = shadow["ID"]?.ToString(),
                    ["InstallDate"] = ParseWmiDate(shadow["InstallDate"]?.ToString()),
                    ["VolumeName"] = shadow["VolumeName"]?.ToString(),
                    ["DeviceObject"] = shadow["DeviceObject"]?.ToString()
                });
                shadow.Dispose();
            }
            data["ShadowCopyCount"] = shadows.Count;
            data["ShadowCopies"] = shadows;

            if (shadows.Count > 0)
            {
                var mostRecent = shadows
                    .Select(s => s["InstallDate"] as DateTime?)
                    .Where(d => d.HasValue)
                    .OrderByDescending(d => d)
                    .FirstOrDefault();
                if (mostRecent.HasValue)
                {
                    data["MostRecentShadow"] = mostRecent.Value.ToString("O");
                    data["ShadowAgeDays"] = (DateTime.UtcNow - mostRecent.Value.ToUniversalTime()).TotalDays;
                }
            }
        }
        catch (Exception ex)
        {
            data["ShadowCopyError"] = ex.Message;
        }

        // Windows Backup history from registry
        try
        {
            using var backupKey = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\CompatibilityAdapter\Signatures");
            // Registry-based check — presence of WindowsBackup task indicates config
            using var taskKey = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsBackup");
            data["WindowsBackupConfigured"] = taskKey != null;

            if (taskKey != null)
            {
                var lastSuccess = taskKey.GetValue("LastSuccess");
                if (lastSuccess != null)
                    data["LastBackupSuccess"] = lastSuccess.ToString();
            }
        }
        catch { /* registry may not have WindowsBackup key */ }

        // Value = shadow copy count (primitive int) for quick server eval
        var shadowCount = data.GetValueOrDefault("ShadowCopyCount") as int? ?? 0;
        return new CheckResult
        {
            Id = c.Id,
            Exists = true,
            Value = shadowCount,
            Stdout = ToJson(data)
        };
    }

    // ── BL-0339: WDAC Active Policies (citool → registry) ──
    //
    // Windows Defender Application Control policies are stored in
    // HKLM\SYSTEM\CurrentControlSet\Control\CI\Policy (XML/binary blobs).
    private static CheckResult CheckWdacPolicies(ControlDef c)
    {
        var policies = new List<string>();

        try
        {
            using var ciKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\CI\Policy");
            if (ciKey != null)
            {
                foreach (var subKeyName in ciKey.GetSubKeyNames())
                {
                    policies.Add(subKeyName);
                }
            }

            // Also check the Boot CI key
            using var bootCiKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\CI\Protected");
            var hvciEnabled = false;
            if (bootCiKey != null)
            {
                hvciEnabled = (bootCiKey.GetValue("HVCIEnabled") as int? ?? 0) == 1;
            }

            var data = new Dictionary<string, object?>
            {
                ["ActivePolicyCount"] = policies.Count,
                ["ActivePolicies"] = policies,
                ["HVCIEnabled"] = hvciEnabled
            };

            // Value = active policy count (primitive int)
            return new CheckResult
            {
                Id = c.Id,
                Exists = policies.Count > 0 || hvciEnabled,
                Value = policies.Count,
                Stdout = ToJson(data)
            };
        }
        catch (Exception ex)
        {
            return new CheckResult
            {
                Id = c.Id,
                Exists = false,
                Stderr = $"Cannot read WDAC policies: {ex.Message}"
            };
        }
    }

    // ── v1.5.0: TLS (SCHANNEL) handler ──
    //
    // Covers 16 BL-02xx controls checking SSL 2.0/3.0, TLS 1.0/1.1/1.2/1.3
    // Client/Server Enabled and DisabledByDefault flags. All of them read
    // from HKLM\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\
    // Protocols\{protocol}\{side}, value name = {property}.
    //
    // Absence of the value is MEANINGFUL: Windows defaults apply. We return
    // exists=false so the server can decide based on OS version + control.
    private static CheckResult CheckTls(ControlDef c)
    {
        if (string.IsNullOrEmpty(c.Protocol) || string.IsNullOrEmpty(c.Side)
            || string.IsNullOrEmpty(c.Property))
        {
            return new CheckResult
            {
                Id = c.Id,
                Exists = false,
                Stderr = "TLS check missing protocol/side/property fields"
            };
        }

        var regPath = $@"SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\{c.Protocol}\{c.Side}";

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(regPath);
            if (key is null)
            {
                return new CheckResult
                {
                    Id = c.Id,
                    Exists = false,
                    Value = (object?)null,
                    Stdout = $"SCHANNEL key not present: {regPath}"
                };
            }

            var raw = key.GetValue(c.Property);
            if (raw is null)
            {
                return new CheckResult
                {
                    Id = c.Id,
                    Exists = false,
                    Value = (object?)null,
                    Stdout = $"Value '{c.Property}' not set under {regPath}"
                };
            }

            // SCHANNEL values are REG_DWORD — normalize to int for server eval
            int intVal = raw is int i ? i
                : int.TryParse(raw.ToString(), out var p) ? p
                : 0;

            return new CheckResult
            {
                Id = c.Id,
                Exists = true,
                Value = intVal,
                RegType = "REG_DWORD",
                Stdout = $"{regPath}\\{c.Property} = {intVal}"
            };
        }
        catch (Exception ex)
        {
            return new CheckResult
            {
                Id = c.Id,
                Exists = false,
                Stderr = $"TLS check failed: {ex.Message}"
            };
        }
    }

    // ── v1.5.0: User Rights Assignment handler ──
    //
    // Covers 16 BL-04xx controls checking LSA privilege assignments via
    // P/Invoke LsaEnumerateAccountsWithUserRight (advapi32.dll). Returns the
    // SIDs currently holding the right as a pipe-separated string in Value.
    // Server compares against ExpectedSidsOrAccounts.
    private static CheckResult CheckUserRight(ControlDef c)
    {
        if (string.IsNullOrEmpty(c.Privilege))
        {
            return new CheckResult
            {
                Id = c.Id,
                Exists = false,
                Stderr = "User right check missing 'privilege' field"
            };
        }

        try
        {
            var sids = UserRightsApi.GetAccountsWithRight(c.Privilege);
            // Value = pipe-separated SID list (primitive string, trim-safe).
            // Empty string = no accounts hold the right (which is the expected
            // state for several BL-0389..0393 checks).
            var joined = string.Join("|", sids);

            return new CheckResult
            {
                Id = c.Id,
                Exists = true,
                Value = joined,
                Stdout = $"{c.Privilege} held by: {(sids.Count == 0 ? "(none)" : joined)}"
            };
        }
        catch (Exception ex)
        {
            return new CheckResult
            {
                Id = c.Id,
                Exists = false,
                Stderr = $"LSA query failed for {c.Privilege}: {ex.Message}"
            };
        }
    }

    // ── v1.5.0: AppLocker handler ──
    //
    // Covers 5 BL-0334..0338 controls checking AppLocker rule collections.
    // AppLocker policies live in HKLM\SOFTWARE\Policies\Microsoft\Windows\
    // SrpV2\{Collection}\{RuleId}\Value. Rather than parsing XML blobs, we
    // just count the rule subkeys under each collection.
    private static CheckResult CheckAppLocker(ControlDef c)
    {
        if (string.IsNullOrEmpty(c.Collection))
        {
            return new CheckResult
            {
                Id = c.Id,
                Exists = false,
                Stderr = "AppLocker check missing 'collection' field"
            };
        }

        // Map "Any" to the union of all collections
        string[] collections = c.Collection.Equals("Any", StringComparison.OrdinalIgnoreCase)
            ? new[] { "Exe", "Script", "Msi", "Dll", "Appx" }
            : new[] { c.Collection };

        int totalRules = 0;
        try
        {
            foreach (var coll in collections)
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    $@"SOFTWARE\Policies\Microsoft\Windows\SrpV2\{coll}");
                if (key != null)
                {
                    totalRules += key.SubKeyCount;
                }
            }

            return new CheckResult
            {
                Id = c.Id,
                Exists = true,
                Value = totalRules,
                Stdout = $"AppLocker {c.Collection}: {totalRules} rule(s) configured"
            };
        }
        catch (Exception ex)
        {
            return new CheckResult
            {
                Id = c.Id,
                Exists = false,
                Stderr = $"AppLocker check failed: {ex.Message}"
            };
        }
    }

    // ── v1.5.0: Inline registry handler (with "exists" operator) ──
    //
    // Replaces the broken check_type="registry" inside command-type controls.
    // These 19 controls (TLS ciphers, PS hardening direct keys, DoH, M365
    // Computer IE feature control) have check_json.check_type="registry"
    // but DB type="command", so RegistryEngine never sees them. We handle
    // them here. Supports operator="exists" which RegistryEngine doesn't.
    private static CheckResult CheckInlineRegistry(ControlDef c)
    {
        if (string.IsNullOrEmpty(c.Hive) || string.IsNullOrEmpty(c.Path))
        {
            return new CheckResult
            {
                Id = c.Id,
                Exists = false,
                Stderr = "Registry check missing hive/path"
            };
        }

        try
        {
            RegistryKey? baseKey = c.Hive.ToUpperInvariant() switch
            {
                "HKLM" => Registry.LocalMachine,
                "HKCU" => Registry.CurrentUser,
                "HKCR" => Registry.ClassesRoot,
                "HKU"  => Registry.Users,
                _ => null
            };

            if (baseKey is null)
            {
                return new CheckResult
                {
                    Id = c.Id,
                    Exists = false,
                    Stderr = $"Unknown hive: {c.Hive}"
                };
            }

            using var key = baseKey.OpenSubKey(c.Path);
            if (key is null)
            {
                return new CheckResult
                {
                    Id = c.Id,
                    Exists = false,
                    Value = (object?)null,
                    Stdout = $"Key not present: {c.Hive}\\{c.Path}"
                };
            }

            // "exists" operator: just checking value presence under a key
            if (c.Operator?.Equals("exists", StringComparison.OrdinalIgnoreCase) == true
                && !string.IsNullOrEmpty(c.ValueName))
            {
                var present = key.GetValue(c.ValueName) is not null;
                return new CheckResult
                {
                    Id = c.Id,
                    Exists = present,
                    Value = present,
                    Stdout = $"{c.ValueName} {(present ? "present" : "missing")} under {c.Hive}\\{c.Path}"
                };
            }

            // Normal value read
            if (string.IsNullOrEmpty(c.ValueName))
            {
                // Key presence only (no value name specified)
                return new CheckResult
                {
                    Id = c.Id,
                    Exists = true,
                    Value = true,
                    Stdout = $"Key present: {c.Hive}\\{c.Path}"
                };
            }

            var raw = key.GetValue(c.ValueName);
            if (raw is null)
            {
                return new CheckResult
                {
                    Id = c.Id,
                    Exists = false,
                    Value = (object?)null,
                    Stdout = $"Value '{c.ValueName}' not set under {c.Hive}\\{c.Path}"
                };
            }

            // Normalize to primitive (int preferred, else string)
            object normalized = raw switch
            {
                int i => i,
                long l => l,
                string s => s,
                byte[] b => Convert.ToBase64String(b),
                _ => raw.ToString() ?? ""
            };

            var regType = key.GetValueKind(c.ValueName).ToString();

            return new CheckResult
            {
                Id = c.Id,
                Exists = true,
                Value = normalized,
                RegType = regType,
                Stdout = $"{c.Hive}\\{c.Path}\\{c.ValueName} = {normalized}"
            };
        }
        catch (Exception ex)
        {
            return new CheckResult
            {
                Id = c.Id,
                Exists = false,
                Stderr = $"Registry read failed: {ex.Message}"
            };
        }
    }

    // ── v1.5.0: Custom check handler ──
    //
    // Covers 5 miscellaneous BL controls whose check_json has check_type="custom".
    // The `notes` field describes what the check should do, but we implement
    // each one natively by matching on control ID.
    private static CheckResult CheckCustom(ControlDef c)
    {
        return c.Id switch
        {
            "BL-0276" => CheckXboxGameSaveTaskDisabled(c),
            "BL-0314" => CheckPowerShellModuleLoggingWildcard(c),
            "BL-0315" => CheckWindowsPowerShellV2FeatureDisabled(c),
            "BL-0324" => CheckDohServerConfigured(c),
            "BL-0388" => CheckLegacyJScriptFeatureKey(c),
            _ => new CheckResult
            {
                Id = c.Id,
                Exists = false,
                Stderr = $"No native implementation for custom check {c.Id}. Notes: {c.Notes}"
            }
        };
    }

    // BL-0276: XblGameSaveTask scheduled task disabled/absent.
    // Scheduled tasks are stored under HKLM\SOFTWARE\Microsoft\Windows NT\
    // CurrentVersion\Schedule\TaskCache\Tasks — each has an "Id" GUID and
    // the task path under Tree\. We check presence in Tree\Microsoft\XblGameSave\.
    private static CheckResult CheckXboxGameSaveTaskDisabled(ControlDef c)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\TaskCache\Tree\Microsoft\XblGameSave");
            if (key is null)
            {
                return new CheckResult
                {
                    Id = c.Id, Exists = true, Value = "absent",
                    Stdout = "XblGameSave task tree not present (PASS)"
                };
            }

            // If present, check if any sub-task is enabled. Enabled=0x02 (disabled).
            var anyEnabled = false;
            foreach (var subName in key.GetSubKeyNames())
            {
                using var sub = key.OpenSubKey(subName);
                var enabledVal = sub?.GetValue("Enabled");
                if (enabledVal is int e && e == 1) { anyEnabled = true; break; }
            }

            return new CheckResult
            {
                Id = c.Id,
                Exists = true,
                Value = anyEnabled ? "enabled" : "disabled",
                Stdout = $"XblGameSave tasks: {(anyEnabled ? "at least one enabled (FAIL)" : "all disabled (PASS)")}"
            };
        }
        catch (Exception ex)
        {
            return new CheckResult { Id = c.Id, Exists = false, Stderr = ex.Message };
        }
    }

    // BL-0314: PowerShell ModuleLogging wildcard '*' present in ModuleNames.
    private static CheckResult CheckPowerShellModuleLoggingWildcard(ControlDef c)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Policies\Microsoft\Windows\PowerShell\ModuleLogging\ModuleNames");
            if (key is null)
            {
                return new CheckResult
                {
                    Id = c.Id, Exists = false, Value = false,
                    Stdout = "ModuleNames key not present"
                };
            }

            var wildcardPresent = false;
            foreach (var name in key.GetValueNames())
            {
                if (name == "*")
                {
                    wildcardPresent = true;
                    break;
                }
                var v = key.GetValue(name) as string;
                if (v == "*") { wildcardPresent = true; break; }
            }

            return new CheckResult
            {
                Id = c.Id,
                Exists = true,
                Value = wildcardPresent,
                Stdout = wildcardPresent
                    ? "ModuleLogging wildcard '*' configured (PASS)"
                    : "ModuleLogging does not include '*' (FAIL)"
            };
        }
        catch (Exception ex)
        {
            return new CheckResult { Id = c.Id, Exists = false, Stderr = ex.Message };
        }
    }

    // BL-0315: PowerShell v2 optional feature disabled.
    // Windows optional features live under HKLM\SOFTWARE\Microsoft\Windows\
    // CurrentVersion\Optional Features\{FeatureName} or are queried via
    // DISM. We check the component-based servicing registry for PS v2.
    private static CheckResult CheckWindowsPowerShellV2FeatureDisabled(ControlDef c)
    {
        try
        {
            // Component Based Servicing tracks all optional features
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\Packages");
            if (key is null)
            {
                return new CheckResult
                {
                    Id = c.Id, Exists = false,
                    Stderr = "Cannot enumerate component packages"
                };
            }

            // Look for MicrosoftWindowsPowerShellV2Root package names
            var enabledCount = 0;
            var disabledCount = 0;
            foreach (var subName in key.GetSubKeyNames())
            {
                if (!subName.Contains("PowerShell-V2", StringComparison.OrdinalIgnoreCase)
                    && !subName.Contains("PowerShellV2", StringComparison.OrdinalIgnoreCase))
                    continue;

                using var sub = key.OpenSubKey(subName);
                var stateVal = sub?.GetValue("CurrentState");
                // 0x70 = Installed, 0x50 = Removed/Disabled (approximate)
                if (stateVal is int s)
                {
                    if (s == 0x70) enabledCount++;
                    else disabledCount++;
                }
            }

            var disabled = enabledCount == 0;
            return new CheckResult
            {
                Id = c.Id,
                Exists = true,
                Value = disabled,
                Stdout = $"PowerShell V2 packages: {enabledCount} installed, {disabledCount} disabled"
            };
        }
        catch (Exception ex)
        {
            return new CheckResult { Id = c.Id, Exists = false, Stderr = ex.Message };
        }
    }

    // BL-0324: At least one DoH server address configured.
    // DoH servers are stored under HKLM\SYSTEM\CurrentControlSet\Services\
    // Dnscache\Parameters\DohWellKnownServers\{ip} subkeys.
    private static CheckResult CheckDohServerConfigured(ControlDef c)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters\DohWellKnownServers");
            if (key is null)
            {
                return new CheckResult
                {
                    Id = c.Id, Exists = false, Value = 0,
                    Stdout = "No DoH servers configured"
                };
            }

            var count = key.SubKeyCount;
            return new CheckResult
            {
                Id = c.Id,
                Exists = count > 0,
                Value = count,
                Stdout = $"{count} DoH server(s) configured"
            };
        }
        catch (Exception ex)
        {
            return new CheckResult { Id = c.Id, Exists = false, Stderr = ex.Message };
        }
    }

    // BL-0388: FEATURE_RESTRICT_LEGACY_JSCRIPT_PER_SECURITY_ZONE key exists.
    private static CheckResult CheckLegacyJScriptFeatureKey(ControlDef c)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"Software\Policies\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_RESTRICT_LEGACY_JSCRIPT_PER_SECURITY_ZONE");
            var exists = key is not null;
            return new CheckResult
            {
                Id = c.Id,
                Exists = exists,
                Value = exists,
                Stdout = exists ? "Legacy JScript feature key present" : "Legacy JScript feature key missing"
            };
        }
        catch (Exception ex)
        {
            return new CheckResult { Id = c.Id, Exists = false, Stderr = ex.Message };
        }
    }

    // ── Unsupported fallback ──
    //
    // Legacy controls with unknown `parent` values should be migrated server-side
    // or deactivated. Returning "info" rather than executing shell commands is the
    // v1.4.0 security contract.
    private static CheckResult UnsupportedCheck(ControlDef c, string parent)
    {
        return new CheckResult
        {
            Id = c.Id,
            Exists = false,
            Stderr = $"Native engine does not support parent='{parent}'. This control must be migrated to a native check type or deactivated."
        };
    }

    // ── Helper: parse WMI CIM_DATETIME (yyyyMMddHHmmss.ffffff±UUU) ──
    private static DateTime? ParseWmiDate(string? cimDate)
    {
        if (string.IsNullOrEmpty(cimDate) || cimDate.Length < 14) return null;
        try
        {
            return ManagementDateTimeConverter.ToDateTime(cimDate);
        }
        catch { return null; }
    }

    // ── Helper: trim-safe JSON serialization for Dictionary<string, object?>.
    // Replaces JsonSerializer.Serialize<T>() which requires reflection and
    // breaks under IL trimming / AOT. Handles primitives, strings, bools,
    // dates, lists, and nested dictionaries.
    private static string ToJson(Dictionary<string, object?> dict)
    {
        var sb = new StringBuilder();
        WriteObject(sb, dict);
        return sb.ToString();
    }

    private static void WriteObject(StringBuilder sb, Dictionary<string, object?> dict)
    {
        sb.Append('{');
        bool first = true;
        foreach (var kvp in dict)
        {
            if (!first) sb.Append(',');
            first = false;
            WriteString(sb, kvp.Key);
            sb.Append(':');
            WriteValue(sb, kvp.Value);
        }
        sb.Append('}');
    }

    private static void WriteValue(StringBuilder sb, object? value)
    {
        switch (value)
        {
            case null:
                sb.Append("null");
                break;
            case string s:
                WriteString(sb, s);
                break;
            case bool b:
                sb.Append(b ? "true" : "false");
                break;
            case DateTime dt:
                WriteString(sb, dt.ToString("O"));
                break;
            case Dictionary<string, object?> nested:
                WriteObject(sb, nested);
                break;
            case System.Collections.IEnumerable list when value is not string:
                sb.Append('[');
                bool firstItem = true;
                foreach (var item in list)
                {
                    if (!firstItem) sb.Append(',');
                    firstItem = false;
                    WriteValue(sb, item);
                }
                sb.Append(']');
                break;
            case IFormattable f:
                // Numbers — use invariant culture
                sb.Append(f.ToString(null, System.Globalization.CultureInfo.InvariantCulture));
                break;
            default:
                WriteString(sb, value.ToString() ?? "");
                break;
        }
    }

    private static void WriteString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"':  sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b");  break;
                case '\f': sb.Append("\\f");  break;
                case '\n': sb.Append("\\n");  break;
                case '\r': sb.Append("\\r");  break;
                case '\t': sb.Append("\\t");  break;
                default:
                    if (c < 0x20)
                        sb.Append($"\\u{(int)c:x4}");
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
}

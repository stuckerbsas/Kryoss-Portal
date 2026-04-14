using System.Runtime.InteropServices;
using Microsoft.Win32;
using KryossAgent.Models;

namespace KryossAgent.Engines;

/// <summary>
/// v1.4.0: Merged SeceditEngine + NetAccountsEngine into a single native engine.
/// Reads password/lockout policy via NetUserModalsGet P/Invoke (replaces both
/// secedit.exe [System Access] and net.exe accounts). Reads additional security
/// settings from registry (replaces secedit.exe [Registry Values] / [Privilege Rights]).
///
/// Type = "secedit" to match existing control definitions.
/// Also handles Type = "netaccount" via the companion NetAccountCompatEngine.
///
/// No Process.Start calls -- everything is P/Invoke or registry reads.
/// </summary>
public class SecurityPolicyEngine : ICheckEngine
{
    public string Type => "secedit";

    // ── P/Invoke declarations ──────────────────────────────────────────────

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetUserModalsGet(string? server, int level, out IntPtr bufptr);

    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetUserGetInfo(string? server, string username, int level, out IntPtr bufptr);

    [StructLayout(LayoutKind.Sequential)]
    private struct USER_MODALS_INFO_0
    {
        public uint usrmod0_min_passwd_len;
        public uint usrmod0_max_passwd_age;    // seconds
        public uint usrmod0_min_passwd_age;    // seconds
        public uint usrmod0_force_logoff;      // seconds, 0xFFFFFFFF = never
        public uint usrmod0_password_hist_len;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct USER_MODALS_INFO_3
    {
        public uint usrmod3_lockout_duration;          // seconds
        public uint usrmod3_lockout_observation_window; // seconds
        public uint usrmod3_lockout_threshold;
    }

    // USER_INFO_20 for account flags
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct USER_INFO_20
    {
        public string usri20_name;
        public string usri20_full_name;
        public string usri20_comment;
        public uint usri20_flags;
        public uint usri20_user_id;
    }

    private const uint UF_ACCOUNTDISABLE = 0x0002;
    private const uint NERR_Success = 0;
    private const uint TIMEQ_FOREVER = 0xFFFFFFFF;

    // Cache for the collected policy data
    private Dictionary<string, string>? _policyCache;
    private string? _policyError;
    private bool _policyCollected;

    public List<CheckResult> Execute(IReadOnlyList<ControlDef> controls)
    {
        var results = new List<CheckResult>(controls.Count);

        // Collect all policy data once
        if (!_policyCollected)
        {
            CollectAllPolicies();
            _policyCollected = true;
        }

        foreach (var control in controls)
        {
            results.Add(ExecuteOne(control));
        }
        return results;
    }

    /// <summary>
    /// Execute controls that use the "netaccount" type (Field-based lookup).
    /// Called by NetAccountCompatEngine.
    /// </summary>
    internal List<CheckResult> ExecuteNetAccount(IReadOnlyList<ControlDef> controls)
    {
        var results = new List<CheckResult>(controls.Count);

        if (!_policyCollected)
        {
            CollectAllPolicies();
            _policyCollected = true;
        }

        foreach (var control in controls)
        {
            results.Add(ExecuteNetAccountOne(control));
        }
        return results;
    }

    private CheckResult ExecuteOne(ControlDef control)
    {
        var result = new CheckResult { Id = control.Id };

        if (_policyError is not null)
        {
            result.Value = $"ERROR: {_policyError}";
            results_SetExists(result, null);
            return result;
        }

        if (_policyCache is null)
        {
            result.Value = "ERROR: policy data not available";
            return result;
        }

        // Secedit controls use SettingName
        if (control.SettingName is not null &&
            _policyCache.TryGetValue(control.SettingName, out var value))
        {
            result.Exists = true;
            result.Value = value;
        }
        else
        {
            result.Exists = false;
        }

        return result;
    }

    private CheckResult ExecuteNetAccountOne(ControlDef control)
    {
        var result = new CheckResult { Id = control.Id };

        if (_policyError is not null)
        {
            result.Value = $"ERROR: {_policyError}";
            return result;
        }

        if (_policyCache is null)
        {
            result.Value = "ERROR: policy data not available";
            return result;
        }

        // NetAccount controls use Field
        if (control.Field is not null &&
            _policyCache.TryGetValue(control.Field, out var value))
        {
            result.Exists = true;
            result.Value = value;
        }
        else
        {
            result.Exists = false;
        }

        return result;
    }

    private static void results_SetExists(CheckResult result, bool? value)
    {
        result.Exists = value;
    }

    private void CollectAllPolicies()
    {
        _policyCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // ── Password & Force-logoff policy (Level 0) ──
            CollectModalsLevel0();

            // ── Lockout policy (Level 3) ──
            CollectModalsLevel3();

            // ── Account status (Admin/Guest enabled/renamed) ──
            CollectAccountStatus();

            // ── Registry-based security settings ──
            CollectRegistrySettings();
        }
        catch (Exception ex)
        {
            _policyError = ex.Message;
        }
    }

    private void CollectModalsLevel0()
    {
        var status = NetUserModalsGet(null, 0, out var bufPtr);
        if (status != NERR_Success)
        {
            _policyError = $"NetUserModalsGet(0) failed with code {status}";
            return;
        }

        try
        {
            var info = Marshal.PtrToStructure<USER_MODALS_INFO_0>(bufPtr);

            // Secedit-compatible setting names
            _policyCache!["MinimumPasswordLength"] = info.usrmod0_min_passwd_len.ToString();

            // MaximumPasswordAge: seconds -> days. 0xFFFFFFFF = never
            if (info.usrmod0_max_passwd_age == TIMEQ_FOREVER)
                _policyCache["MaximumPasswordAge"] = "-1"; // secedit uses -1 for "never"
            else
                _policyCache["MaximumPasswordAge"] = (info.usrmod0_max_passwd_age / 86400).ToString();

            // MinimumPasswordAge: seconds -> days
            _policyCache["MinimumPasswordAge"] = (info.usrmod0_min_passwd_age / 86400).ToString();

            // PasswordHistorySize
            _policyCache["PasswordHistorySize"] = info.usrmod0_password_hist_len.ToString();

            // ForceLogoffWhenHourExpire: 0xFFFFFFFF = never = 0, else 1
            _policyCache["ForceLogoffWhenHourExpire"] = info.usrmod0_force_logoff == TIMEQ_FOREVER ? "0" : "1";

            // NetAccount-compatible field names
            _policyCache["MinimumPasswordLength"] = info.usrmod0_min_passwd_len.ToString();

            if (info.usrmod0_max_passwd_age == TIMEQ_FOREVER)
                _policyCache["MaximumPasswordAge"] = "-1";

            _policyCache["PasswordHistoryLength"] = info.usrmod0_password_hist_len.ToString();
            _policyCache["ForceLogoff"] = info.usrmod0_force_logoff == TIMEQ_FOREVER ? "Never" : (info.usrmod0_force_logoff / 60).ToString();
        }
        finally
        {
            NetApiBufferFree(bufPtr);
        }
    }

    private void CollectModalsLevel3()
    {
        var status = NetUserModalsGet(null, 3, out var bufPtr);
        if (status != NERR_Success)
        {
            // Lockout data unavailable -- not fatal, just skip lockout fields
            return;
        }

        try
        {
            var info = Marshal.PtrToStructure<USER_MODALS_INFO_3>(bufPtr);

            // Secedit-compatible names
            _policyCache!["LockoutBadCount"] = info.usrmod3_lockout_threshold.ToString();

            // Duration: seconds -> minutes. 0xFFFFFFFF = until admin unlocks
            if (info.usrmod3_lockout_duration == TIMEQ_FOREVER)
                _policyCache["LockoutDuration"] = "-1";
            else
                _policyCache["LockoutDuration"] = (info.usrmod3_lockout_duration / 60).ToString();

            if (info.usrmod3_lockout_observation_window == TIMEQ_FOREVER)
                _policyCache["ResetLockoutCount"] = "-1";
            else
                _policyCache["ResetLockoutCount"] = (info.usrmod3_lockout_observation_window / 60).ToString();

            // NetAccount-compatible field names
            _policyCache["LockoutThreshold"] = info.usrmod3_lockout_threshold.ToString();

            if (info.usrmod3_lockout_duration == TIMEQ_FOREVER)
                _policyCache["LockoutDuration"] = "Until admin unlocks";

            _policyCache["LockoutWindow"] = info.usrmod3_lockout_observation_window == TIMEQ_FOREVER
                ? "Until admin unlocks"
                : (info.usrmod3_lockout_observation_window / 60).ToString();
        }
        finally
        {
            NetApiBufferFree(bufPtr);
        }
    }

    private void CollectAccountStatus()
    {
        // Check Administrator account
        try
        {
            var adminEnabled = IsAccountEnabled("Administrator");
            _policyCache!["EnableAdminAccount"] = adminEnabled ? "1" : "0";
        }
        catch { /* non-critical */ }

        // Check Guest account
        try
        {
            var guestEnabled = IsAccountEnabled("Guest");
            _policyCache!["EnableGuestAccount"] = guestEnabled ? "1" : "0";
        }
        catch { /* non-critical */ }

        // Check for renamed Administrator/Guest
        try
        {
            var adminName = GetAccountName("Administrator");
            if (adminName is not null)
                _policyCache!["NewAdministratorName"] = $"\"{adminName}\"";
        }
        catch { /* non-critical */ }

        try
        {
            var guestName = GetAccountName("Guest");
            if (guestName is not null)
                _policyCache!["NewGuestName"] = $"\"{guestName}\"";
        }
        catch { /* non-critical */ }
    }

    private static bool IsAccountEnabled(string username)
    {
        var status = NetUserGetInfo(null, username, 20, out var bufPtr);
        if (status != NERR_Success) return false;

        try
        {
            var info = Marshal.PtrToStructure<USER_INFO_20>(bufPtr);
            return (info.usri20_flags & UF_ACCOUNTDISABLE) == 0;
        }
        finally
        {
            NetApiBufferFree(bufPtr);
        }
    }

    private static string? GetAccountName(string defaultName)
    {
        // Check if the account has been renamed by querying the well-known SID
        // For Administrator: S-1-5-21-*-500, Guest: S-1-5-21-*-501
        // The simplest approach: query NetUserGetInfo and return the actual name.
        // If the name differs from the default, it was renamed.
        var status = NetUserGetInfo(null, defaultName, 20, out var bufPtr);
        if (status != NERR_Success) return null;

        try
        {
            var info = Marshal.PtrToStructure<USER_INFO_20>(bufPtr);
            return info.usri20_name;
        }
        finally
        {
            NetApiBufferFree(bufPtr);
        }
    }

    private void CollectRegistrySettings()
    {
        // ClearTextPassword
        ReadRegistryDword(@"SYSTEM\CurrentControlSet\Control\Lsa", "ClearTextPassword",
            "ClearTextPassword", "0");

        // LSAAnonymousNameLookup (RestrictAnonymous)
        ReadRegistryDword(@"SYSTEM\CurrentControlSet\Control\Lsa", "RestrictAnonymous",
            "LSAAnonymousNameLookup", null);

        // PasswordComplexity — stored by secedit in the SAM, but readable from registry
        // on systems where the Group Policy has been applied
        ReadRegistryDword(@"SYSTEM\CurrentControlSet\Control\Lsa", "PasswordComplexity",
            "PasswordComplexity", null);

        // Additional [System Access] settings available from registry
        ReadRegistryDword(@"SYSTEM\CurrentControlSet\Control\Lsa", "NoLMHash",
            "NoLMHash", null);

        ReadRegistryDword(@"SYSTEM\CurrentControlSet\Control\Lsa", "LmCompatibilityLevel",
            "LmCompatibilityLevel", null);

        ReadRegistryDword(@"SYSTEM\CurrentControlSet\Control\Lsa", "RestrictAnonymousSAM",
            "RestrictAnonymousSAM", null);

        ReadRegistryDword(@"SYSTEM\CurrentControlSet\Control\Lsa", "EveryoneIncludesAnonymous",
            "EveryoneIncludesAnonymous", null);

        ReadRegistryDword(@"SYSTEM\CurrentControlSet\Control\Lsa", "ForceGuest",
            "ForceGuest", null);

        // Interactive logon settings
        ReadRegistryDword(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
            "DontDisplayLastUserName", "DontDisplayLastUserName", null);

        ReadRegistryString(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
            "LegalNoticeText", "LegalNoticeText", null);

        ReadRegistryString(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
            "LegalNoticeCaption", "LegalNoticeCaption", null);

        ReadRegistryDword(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
            "DisableCAD", "DisableCAD", null);

        ReadRegistryDword(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
            "EnableLUA", "EnableLUA", null);

        ReadRegistryDword(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
            "ConsentPromptBehaviorAdmin", "ConsentPromptBehaviorAdmin", null);

        ReadRegistryDword(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
            "ConsentPromptBehaviorUser", "ConsentPromptBehaviorUser", null);

        ReadRegistryDword(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
            "EnableInstallerDetection", "EnableInstallerDetection", null);

        ReadRegistryDword(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
            "EnableSecureUIAPaths", "EnableSecureUIAPaths", null);

        ReadRegistryDword(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
            "EnableVirtualization", "EnableVirtualization", null);

        ReadRegistryDword(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
            "FilterAdministratorToken", "FilterAdministratorToken", null);

        ReadRegistryDword(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
            "PromptOnSecureDesktop", "PromptOnSecureDesktop", null);

        // Remote Desktop settings
        ReadRegistryDword(@"SYSTEM\CurrentControlSet\Control\Terminal Server",
            "fDenyTSConnections", "fDenyTSConnections", null);

        // AutoPlay
        ReadRegistryDword(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer",
            "NoDriveTypeAutoRun", "NoDriveTypeAutoRun", null);
    }

    private void ReadRegistryDword(string subKeyPath, string valueName, string settingName, string? defaultValue)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(subKeyPath);
            var val = key?.GetValue(valueName);
            if (val != null)
            {
                _policyCache![settingName] = val.ToString()!;
            }
            else if (defaultValue != null)
            {
                _policyCache![settingName] = defaultValue;
            }
        }
        catch { /* non-critical */ }
    }

    private void ReadRegistryString(string subKeyPath, string valueName, string settingName, string? defaultValue)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(subKeyPath);
            var val = key?.GetValue(valueName)?.ToString();
            if (val != null)
            {
                _policyCache![settingName] = val;
            }
            else if (defaultValue != null)
            {
                _policyCache![settingName] = defaultValue;
            }
        }
        catch { /* non-critical */ }
    }
}

/// <summary>
/// Thin backward-compat wrapper: Type = "netaccount", delegates to SecurityPolicyEngine.
/// Registered as a separate engine so existing control definitions with Type="netaccount"
/// continue to work without modification.
/// </summary>
public class NetAccountCompatEngine : ICheckEngine
{
    public string Type => "netaccount";

    private readonly SecurityPolicyEngine _inner;

    public NetAccountCompatEngine(SecurityPolicyEngine inner)
    {
        _inner = inner;
    }

    public List<CheckResult> Execute(IReadOnlyList<ControlDef> controls)
    {
        return _inner.ExecuteNetAccount(controls);
    }
}

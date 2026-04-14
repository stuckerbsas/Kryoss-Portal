using System.Runtime.InteropServices;
using KryossAgent.Models;

namespace KryossAgent.Engines;

/// <summary>
/// v1.4.0: Reads audit policy via P/Invoke AuditQuerySystemPolicy (advapi32.dll).
/// Replaces auditpol.exe shell-out with native API calls.
///
/// Runs once per engine invocation, queries all known subcategory GUIDs,
/// maps results to human-readable setting strings that match the control
/// definitions (Success, Failure, Success and Failure, No Auditing).
/// </summary>
public class AuditpolEngine : ICheckEngine
{
    public string Type => "auditpol";

    // ── P/Invoke declarations ──────────────────────────────────────────────

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AuditQuerySystemPolicy(
        [In] Guid[] pSubCategoryGuids,
        uint dwPolicyCount,
        out IntPtr ppAuditPolicy);

    [DllImport("advapi32.dll")]
    private static extern void AuditFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct AUDIT_POLICY_INFORMATION
    {
        public Guid AuditSubCategoryGuid;
        public uint AuditingInformation;
        public Guid AuditCategoryGuid;
    }

    // POLICY_AUDIT_EVENT flags
    private const uint POLICY_AUDIT_EVENT_UNCHANGED = 0x00;
    private const uint POLICY_AUDIT_EVENT_SUCCESS   = 0x01;
    private const uint POLICY_AUDIT_EVENT_FAILURE   = 0x02;
    private const uint POLICY_AUDIT_EVENT_NONE      = 0x04;

    // ── Well-known audit subcategory GUIDs ─────────────────────────────────
    // Source: Microsoft docs - Audit Policy Subcategory GUIDs
    private static readonly Dictionary<string, Guid> SubcategoryGuids = new(StringComparer.OrdinalIgnoreCase)
    {
        // Security System
        ["Security State Change"]        = new("0CCE9210-69AE-11D9-BED3-505054503030"),
        ["Security System Extension"]    = new("0CCE9211-69AE-11D9-BED3-505054503030"),
        ["System Integrity"]             = new("0CCE9212-69AE-11D9-BED3-505054503030"),
        ["IPsec Driver"]                 = new("0CCE9213-69AE-11D9-BED3-505054503030"),
        ["Other System Events"]          = new("0CCE9214-69AE-11D9-BED3-505054503030"),

        // Logon/Logoff
        ["Logon"]                        = new("0CCE9215-69AE-11D9-BED3-505054503030"),
        ["Logoff"]                       = new("0CCE9216-69AE-11D9-BED3-505054503030"),
        ["Account Lockout"]              = new("0CCE9217-69AE-11D9-BED3-505054503030"),
        ["IPsec Main Mode"]              = new("0CCE9218-69AE-11D9-BED3-505054503030"),
        ["IPsec Quick Mode"]             = new("0CCE9219-69AE-11D9-BED3-505054503030"),
        ["IPsec Extended Mode"]          = new("0CCE921A-69AE-11D9-BED3-505054503030"),
        ["Special Logon"]                = new("0CCE921B-69AE-11D9-BED3-505054503030"),
        ["Other Logon/Logoff Events"]    = new("0CCE921C-69AE-11D9-BED3-505054503030"),
        ["Network Policy Server"]        = new("0CCE9243-69AE-11D9-BED3-505054503030"),
        ["User / Device Claims"]         = new("0CCE9247-69AE-11D9-BED3-505054503030"),
        ["Group Membership"]             = new("0CCE9249-69AE-11D9-BED3-505054503030"),

        // Object Access
        ["File System"]                  = new("0CCE921D-69AE-11D9-BED3-505054503030"),
        ["Registry"]                     = new("0CCE921E-69AE-11D9-BED3-505054503030"),
        ["Kernel Object"]                = new("0CCE921F-69AE-11D9-BED3-505054503030"),
        ["SAM"]                          = new("0CCE9220-69AE-11D9-BED3-505054503030"),
        ["Certification Services"]       = new("0CCE9221-69AE-11D9-BED3-505054503030"),
        ["Application Generated"]        = new("0CCE9222-69AE-11D9-BED3-505054503030"),
        ["Handle Manipulation"]          = new("0CCE9223-69AE-11D9-BED3-505054503030"),
        ["File Share"]                   = new("0CCE9224-69AE-11D9-BED3-505054503030"),
        ["Filtering Platform Packet Drop"] = new("0CCE9225-69AE-11D9-BED3-505054503030"),
        ["Filtering Platform Connection"] = new("0CCE9226-69AE-11D9-BED3-505054503030"),
        ["Other Object Access Events"]   = new("0CCE9227-69AE-11D9-BED3-505054503030"),
        ["Detailed File Share"]          = new("0CCE9244-69AE-11D9-BED3-505054503030"),
        ["Removable Storage"]            = new("0CCE9245-69AE-11D9-BED3-505054503030"),
        ["Central Policy Staging"]       = new("0CCE9246-69AE-11D9-BED3-505054503030"),

        // Privilege Use
        ["Sensitive Privilege Use"]      = new("0CCE9228-69AE-11D9-BED3-505054503030"),
        ["Non Sensitive Privilege Use"]   = new("0CCE9229-69AE-11D9-BED3-505054503030"),
        ["Other Privilege Use Events"]   = new("0CCE922A-69AE-11D9-BED3-505054503030"),

        // Detailed Tracking
        ["Process Creation"]             = new("0CCE922B-69AE-11D9-BED3-505054503030"),
        ["Process Termination"]          = new("0CCE922C-69AE-11D9-BED3-505054503030"),
        ["DPAPI Activity"]               = new("0CCE922D-69AE-11D9-BED3-505054503030"),
        ["RPC Events"]                   = new("0CCE922E-69AE-11D9-BED3-505054503030"),
        ["Plug and Play Events"]         = new("0CCE9248-69AE-11D9-BED3-505054503030"),
        ["Token Right Adjusted Events"]  = new("0CCE924A-69AE-11D9-BED3-505054503030"),

        // Policy Change
        ["Audit Policy Change"]          = new("0CCE922F-69AE-11D9-BED3-505054503030"),
        ["Authentication Policy Change"] = new("0CCE9230-69AE-11D9-BED3-505054503030"),
        ["Authorization Policy Change"]  = new("0CCE9231-69AE-11D9-BED3-505054503030"),
        ["MPSSVC Rule-Level Policy Change"] = new("0CCE9232-69AE-11D9-BED3-505054503030"),
        ["Filtering Platform Policy Change"] = new("0CCE9233-69AE-11D9-BED3-505054503030"),
        ["Other Policy Change Events"]   = new("0CCE9234-69AE-11D9-BED3-505054503030"),

        // Account Management
        ["User Account Management"]      = new("0CCE9235-69AE-11D9-BED3-505054503030"),
        ["Computer Account Management"]  = new("0CCE9236-69AE-11D9-BED3-505054503030"),
        ["Security Group Management"]    = new("0CCE9237-69AE-11D9-BED3-505054503030"),
        ["Distribution Group Management"] = new("0CCE9238-69AE-11D9-BED3-505054503030"),
        ["Application Group Management"] = new("0CCE9239-69AE-11D9-BED3-505054503030"),
        ["Other Account Management Events"] = new("0CCE923A-69AE-11D9-BED3-505054503030"),

        // DS Access
        ["Directory Service Access"]     = new("0CCE923B-69AE-11D9-BED3-505054503030"),
        ["Directory Service Changes"]    = new("0CCE923C-69AE-11D9-BED3-505054503030"),
        ["Directory Service Replication"] = new("0CCE923D-69AE-11D9-BED3-505054503030"),
        ["Detailed Directory Service Replication"] = new("0CCE923E-69AE-11D9-BED3-505054503030"),

        // Account Logon
        ["Credential Validation"]        = new("0CCE923F-69AE-11D9-BED3-505054503030"),
        ["Kerberos Service Ticket Operations"] = new("0CCE9240-69AE-11D9-BED3-505054503030"),
        ["Other Account Logon Events"]   = new("0CCE9241-69AE-11D9-BED3-505054503030"),
        ["Kerberos Authentication Service"] = new("0CCE9242-69AE-11D9-BED3-505054503030"),
    };

    // Reverse lookup: GUID -> display name
    private static readonly Dictionary<Guid, string> GuidToName;

    static AuditpolEngine()
    {
        GuidToName = new Dictionary<Guid, string>(SubcategoryGuids.Count);
        foreach (var (name, guid) in SubcategoryGuids)
        {
            GuidToName[guid] = name;
        }
    }

    public List<CheckResult> Execute(IReadOnlyList<ControlDef> controls)
    {
        var results = new List<CheckResult>(controls.Count);

        // Query all policies in one shot
        Dictionary<string, string>? policies;
        string? queryError;
        try
        {
            policies = QueryAllAuditPolicies(out queryError);
        }
        catch (Exception ex)
        {
            policies = null;
            queryError = ex.Message;
        }

        foreach (var control in controls)
        {
            var result = new CheckResult { Id = control.Id };

            if (queryError is not null || policies is null)
            {
                result.Value = $"ERROR: {queryError ?? "audit policy query failed"}";
                results.Add(result);
                continue;
            }

            if (control.Subcategory is not null &&
                policies.TryGetValue(control.Subcategory, out var value))
            {
                result.Exists = true;
                result.Value = value;
            }
            else
            {
                // Unknown subcategory -- return info result instead of error
                result.Exists = false;
                if (control.Subcategory is not null && !SubcategoryGuids.ContainsKey(control.Subcategory))
                {
                    result.Value = $"INFO: unknown audit subcategory '{control.Subcategory}'";
                }
            }

            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// Query all known audit subcategory policies via AuditQuerySystemPolicy.
    /// Returns dictionary of subcategory display name -> setting string.
    /// </summary>
    private static Dictionary<string, string>? QueryAllAuditPolicies(out string? error)
    {
        error = null;

        var guids = SubcategoryGuids.Values.ToArray();
        var count = (uint)guids.Length;

        if (!AuditQuerySystemPolicy(guids, count, out var policyBuffer))
        {
            var lastError = Marshal.GetLastWin32Error();
            error = $"AuditQuerySystemPolicy failed (Win32 error {lastError})";
            return null;
        }

        try
        {
            var policies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var structSize = Marshal.SizeOf<AUDIT_POLICY_INFORMATION>();
            var currentPtr = policyBuffer;

            for (var i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<AUDIT_POLICY_INFORMATION>(currentPtr);

                if (GuidToName.TryGetValue(info.AuditSubCategoryGuid, out var name))
                {
                    policies[name] = AuditFlagsToString(info.AuditingInformation);
                }

                currentPtr = IntPtr.Add(currentPtr, structSize);
            }

            return policies;
        }
        finally
        {
            AuditFree(policyBuffer);
        }
    }

    /// <summary>
    /// Convert POLICY_AUDIT_EVENT flags to the same strings that auditpol.exe outputs.
    /// </summary>
    private static string AuditFlagsToString(uint flags)
    {
        // Strip the NONE bit -- it's set when explicitly configured to "No Auditing"
        var hasSuccess = (flags & POLICY_AUDIT_EVENT_SUCCESS) != 0;
        var hasFailure = (flags & POLICY_AUDIT_EVENT_FAILURE) != 0;

        if (hasSuccess && hasFailure) return "Success and Failure";
        if (hasSuccess) return "Success";
        if (hasFailure) return "Failure";
        return "No Auditing";
    }
}

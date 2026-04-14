using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace KryossAgent.Engines;

/// <summary>
/// Native P/Invoke wrapper for the Local Security Authority (LSA) user rights
/// enumeration API. Replaces the legacy `secedit /export /cfg` + INF parsing
/// for User Rights Assignment checks (BL-0389..0404).
///
/// Uses LsaOpenPolicy + LsaEnumerateAccountsWithUserRight from advapi32.dll.
/// Read-only, AOT-safe, no external process spawn.
///
/// Reference: https://learn.microsoft.com/en-us/windows/win32/api/ntsecapi/nf-ntsecapi-lsaenumerateaccountswithuserright
/// </summary>
internal static class UserRightsApi
{
    // ── NTSTATUS codes ──
    private const uint STATUS_SUCCESS = 0x00000000;
    private const uint STATUS_NO_MORE_ENTRIES = 0x8000001A;

    // ── POLICY_* access rights ──
    private const uint POLICY_LOOKUP_NAMES = 0x00000800;
    private const uint POLICY_VIEW_LOCAL_INFORMATION = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct LSA_OBJECT_ATTRIBUTES
    {
        public int Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LSA_UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LSA_ENUMERATION_INFORMATION
    {
        public IntPtr PSid;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint LsaOpenPolicy(
        IntPtr SystemName,
        ref LSA_OBJECT_ATTRIBUTES ObjectAttributes,
        uint DesiredAccess,
        out IntPtr PolicyHandle);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint LsaEnumerateAccountsWithUserRight(
        IntPtr PolicyHandle,
        ref LSA_UNICODE_STRING UserRight,
        out IntPtr Buffer,
        out uint CountReturned);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern uint LsaClose(IntPtr ObjectHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern uint LsaFreeMemory(IntPtr Buffer);

    /// <summary>
    /// Returns the list of SIDs (as strings) currently holding the specified
    /// user right on the local machine. Empty list means no accounts hold it.
    /// </summary>
    public static List<string> GetAccountsWithRight(string privilegeName)
    {
        var result = new List<string>();

        var objectAttributes = new LSA_OBJECT_ATTRIBUTES
        {
            Length = Marshal.SizeOf<LSA_OBJECT_ATTRIBUTES>()
        };

        IntPtr policyHandle = IntPtr.Zero;
        var status = LsaOpenPolicy(
            IntPtr.Zero,
            ref objectAttributes,
            POLICY_LOOKUP_NAMES | POLICY_VIEW_LOCAL_INFORMATION,
            out policyHandle);

        if (status != STATUS_SUCCESS)
        {
            throw new Win32Exception((int)status, $"LsaOpenPolicy failed: 0x{status:X8}");
        }

        try
        {
            // Build the LSA_UNICODE_STRING for the privilege name
            var rightBuffer = Marshal.StringToHGlobalUni(privilegeName);
            try
            {
                var lsaRight = new LSA_UNICODE_STRING
                {
                    Length = (ushort)(privilegeName.Length * 2),
                    MaximumLength = (ushort)((privilegeName.Length + 1) * 2),
                    Buffer = rightBuffer
                };

                IntPtr enumBuffer = IntPtr.Zero;
                uint countReturned = 0;

                var enumStatus = LsaEnumerateAccountsWithUserRight(
                    policyHandle,
                    ref lsaRight,
                    out enumBuffer,
                    out countReturned);

                if (enumStatus == STATUS_NO_MORE_ENTRIES)
                {
                    // No accounts hold this right — valid, return empty list
                    return result;
                }

                if (enumStatus != STATUS_SUCCESS)
                {
                    throw new Win32Exception((int)enumStatus,
                        $"LsaEnumerateAccountsWithUserRight failed for '{privilegeName}': 0x{enumStatus:X8}");
                }

                try
                {
                    // Walk the LSA_ENUMERATION_INFORMATION array
                    var structSize = Marshal.SizeOf<LSA_ENUMERATION_INFORMATION>();
                    for (int i = 0; i < countReturned; i++)
                    {
                        var entryPtr = IntPtr.Add(enumBuffer, i * structSize);
                        var entry = Marshal.PtrToStructure<LSA_ENUMERATION_INFORMATION>(entryPtr);

                        if (entry.PSid != IntPtr.Zero)
                        {
                            try
                            {
                                var sid = new SecurityIdentifier(entry.PSid);
                                result.Add(sid.Value);
                            }
                            catch
                            {
                                // Skip malformed SIDs
                            }
                        }
                    }
                }
                finally
                {
                    LsaFreeMemory(enumBuffer);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(rightBuffer);
            }
        }
        finally
        {
            LsaClose(policyHandle);
        }

        return result;
    }
}

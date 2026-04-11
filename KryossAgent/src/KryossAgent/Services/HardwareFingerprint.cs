using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace KryossAgent.Services;

/// <summary>
/// Produces a stable, opaque hardware fingerprint for this machine. The
/// output is a lowercase hex SHA-256 digest (64 chars) computed from a
/// concatenation of:
/// <list type="bullet">
///   <item>Windows <c>MachineGuid</c> (never rotates unless sysprep runs)</item>
///   <item>BIOS / system serial number (from registry — set by OEM firmware)</item>
///   <item>System manufacturer + product (from registry)</item>
/// </list>
///
/// <para>
/// Why not TPM? TPM attestation would be the ideal binding but it is not
/// AOT-friendly on .NET 8 and requires elevation on some builds. A later
/// pass will layer <c>Tbsi_*</c> P/Invoke attestation on top of this hash
/// (see security-baseline.md §Hardware binding — "Phase 2: TPM-bound").
/// For now, the MachineGuid is already good enough to detect token cloning
/// across distinct machines — an attacker copying the registry config to a
/// second host will not reproduce the MachineGuid.
/// </para>
///
/// <para>
/// Why opaque hash instead of raw fields? The server only needs equality
/// comparison, and hashing means (a) the DB never stores PII-like serials,
/// (b) rotation is trivial (bump the <see cref="Salt"/> constant and all
/// machines re-bind on next contact), (c) the wire format is fixed size.
/// </para>
///
/// <para>
/// Stability: all inputs come from persistent registry locations written
/// by Windows setup / OEM firmware. A cloned VM / imaged disk WILL produce
/// the same hash unless sysprep /generalize is run — that's a deliberate
/// tradeoff: we want two sysprepped copies of the same image to look like
/// two different machines (which they are), and we want an upgraded OS to
/// still look like the same machine (same MachineGuid).
/// </para>
/// </summary>
public static class HardwareFingerprint
{
    // Bump this to force all agents to re-derive their fingerprint on the
    // next call. Server-side, this invalidates every existing binding —
    // coordinate with a rotation runbook before changing.
    private const string Salt = "kryoss-hwid-v1";

    /// <summary>
    /// Computes the fingerprint and returns it as a 64-char lowercase hex
    /// string. Never throws: if every registry read fails, falls back to a
    /// deterministic hash over the salt + machine name so the agent can
    /// still send SOMETHING. The server will accept that degraded value
    /// but log a warning because it's easy to clone.
    /// </summary>
    public static string Compute()
    {
        var parts = new StringBuilder(Salt);
        parts.Append('|');

        TryAppend(parts, () => ReadString(
            @"SOFTWARE\Microsoft\Cryptography", "MachineGuid", RegistryHive.LocalMachine));
        parts.Append('|');

        TryAppend(parts, () => ReadString(
            @"HARDWARE\DESCRIPTION\System\BIOS", "SystemManufacturer", RegistryHive.LocalMachine));
        parts.Append('|');

        TryAppend(parts, () => ReadString(
            @"HARDWARE\DESCRIPTION\System\BIOS", "SystemProductName", RegistryHive.LocalMachine));
        parts.Append('|');

        TryAppend(parts, () => ReadString(
            @"HARDWARE\DESCRIPTION\System\BIOS", "SystemSerialNumber", RegistryHive.LocalMachine));
        parts.Append('|');

        // Baseboard info provides a second independent source tied to the
        // motherboard — resists drive-swap attacks more than BIOS fields.
        TryAppend(parts, () => ReadString(
            @"HARDWARE\DESCRIPTION\System\BIOS", "BaseBoardManufacturer", RegistryHive.LocalMachine));
        parts.Append('|');

        TryAppend(parts, () => ReadString(
            @"HARDWARE\DESCRIPTION\System\BIOS", "BaseBoardProduct", RegistryHive.LocalMachine));

        // If ALL registry reads failed the hash would collapse to the salt
        // alone — mix in MachineName as a weak tiebreaker.
        parts.Append('|');
        parts.Append(Environment.MachineName);

        var bytes = Encoding.UTF8.GetBytes(parts.ToString());
        var digest = SHA256.HashData(bytes);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private static void TryAppend(StringBuilder sb, Func<string?> reader)
    {
        try
        {
            var value = reader();
            if (!string.IsNullOrWhiteSpace(value))
                sb.Append(value.Trim());
        }
        catch
        {
            // Swallow — missing inputs just produce a weaker (but still
            // deterministic) fingerprint.
        }
    }

    private static string? ReadString(string subkey, string valueName, RegistryHive hive)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(subkey);
        return key?.GetValue(valueName) as string;
    }
}

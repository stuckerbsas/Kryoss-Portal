namespace KryossAgent.Config;

/// <summary>
/// Compile-time sentinel constants that the server's BinaryPatcher replaces
/// with real values. When a sentinel still contains "PLACEHOLDER" or "PLCHLD",
/// it means the binary is unpatched (generic installer).
/// </summary>
public static class EmbeddedConfig
{
    // ── Sentinels (fixed-length, padded, binary-patchable) ──────────────
    // Format: prefix + payload + trailing "@@"
    // Total lengths: ENROLLMENT=64, APIURL=256, ORGNAM=128, MSPNAM=128,
    //                CLRPRI=17, CLRACC=17

    private const string SentinelEnrollment =
        "@@KRYOSS_ENROLL:_PLACEHOLDER_VALUE_000000000000000000000000000@@";
    //   16-char prefix  +  46-char payload                           + 2  = 64

    private const string SentinelApiUrl =
        "@@KRYOSS_APIURL:_PLACEHOLDER_VALUE_000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000@@";
    //   16-char prefix  +  238-char payload                                                                                                                                                                                                                         + 2  = 256

    private const string SentinelOrgName =
        "@@KRYOSS_ORGNAM:_PLACEHOLDER_VALUE_0000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000@@";
    //   16-char prefix  +  110-char payload                                                                                             + 2  = 128

    private const string SentinelMspName =
        "@@KRYOSS_MSPNAM:_PLACEHOLDER_VALUE_0000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000@@";
    //   16-char prefix  +  110-char payload                                                                                             + 2  = 128

    private const string SentinelPrimaryColor =
        "@@CLRPRI:_PLACEHOLDER_VALUE_0000@@";
    //   9-char prefix + 21-char payload + 2 = 32

    private const string SentinelAccentColor =
        "@@CLRACC:_PLACEHOLDER_VALUE_0000@@";
    //   9-char prefix + 21-char payload + 2 = 32

    // ── Public properties ──────────────────────────────────────────────

    public static string? EnrollmentCode => ReadSentinel(SentinelEnrollment, "@@KRYOSS_ENROLL:");
    public static string? ApiUrl         => ReadSentinel(SentinelApiUrl,     "@@KRYOSS_APIURL:");
    public static string? OrgName        => ReadSentinel(SentinelOrgName,    "@@KRYOSS_ORGNAM:");
    public static string? MspName        => ReadSentinel(SentinelMspName,    "@@KRYOSS_MSPNAM:");
    public static string? PrimaryColor   => ReadSentinel(SentinelPrimaryColor, "@@CLRPRI:");
    public static string? AccentColor    => ReadSentinel(SentinelAccentColor,  "@@CLRACC:");

    /// <summary>
    /// True when at least one sentinel has been replaced with a real value
    /// (i.e. the binary was patched by the server).
    /// </summary>
    public static bool IsPatched =>
        EnrollmentCode is not null ||
        ApiUrl is not null ||
        OrgName is not null ||
        MspName is not null ||
        PrimaryColor is not null ||
        AccentColor is not null;

    // ── Sentinel parser ────────────────────────────────────────────────

    /// <summary>
    /// Extracts the patched value from a sentinel string.
    /// Returns null if the sentinel still contains "PLACEHOLDER" or "PLCHLD"
    /// (meaning it was never patched).
    /// </summary>
    private static string? ReadSentinel(string raw, string prefix)
    {
        // Unpatched: still has placeholder marker
        if (raw.Contains("PLACEHOLDER", StringComparison.Ordinal) ||
            raw.Contains("PLCHLD", StringComparison.Ordinal))
        {
            return null;
        }

        // Extract value between prefix and trailing "@@"
        if (!raw.StartsWith(prefix, StringComparison.Ordinal))
            return null;

        var value = raw[prefix.Length..];

        // Strip trailing "@@"
        var terminator = value.LastIndexOf("@@", StringComparison.Ordinal);
        if (terminator >= 0)
            value = value[..terminator];

        // Trim padding characters (nulls, zeros, underscores)
        value = value.TrimEnd('\0', '0', '_');

        return string.IsNullOrEmpty(value) ? null : value;
    }
}

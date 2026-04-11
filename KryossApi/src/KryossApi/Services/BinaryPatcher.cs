using System.Text;

namespace KryossApi.Services;

/// <summary>
/// Patches sentinel placeholder strings in a compiled .NET agent binary.
/// .NET embeds const strings as UTF-16LE in the PE. This patcher searches
/// and replaces in UTF-16LE encoding to match.
/// </summary>
public static class BinaryPatcher
{
    private static readonly (string Prefix, int TotalCharLen, string Key)[] Sentinels =
    [
        ("@@KRYOSS_ENROLL:", 64, "enrollmentCode"),
        ("@@KRYOSS_APIURL:", 256, "apiUrl"),
        ("@@KRYOSS_ORGNAM:", 128, "orgName"),
        ("@@KRYOSS_MSPNAM:", 128, "mspName"),
        ("@@CLRPRI:", 32, "primaryColor"),
        ("@@CLRACC:", 32, "accentColor"),
    ];

    /// <summary>
    /// Patches sentinel placeholders in a template binary with the provided values.
    /// Searches for UTF-16LE encoded sentinel prefixes and replaces the payload area.
    /// </summary>
    public static byte[] Patch(byte[] templateBinary, Dictionary<string, string> values)
    {
        var result = new byte[templateBinary.Length];
        Buffer.BlockCopy(templateBinary, 0, result, 0, templateBinary.Length);

        foreach (var (prefix, totalCharLen, key) in Sentinels)
        {
            if (!values.TryGetValue(key, out var value) || value is null)
                continue;

            // .NET stores strings as UTF-16LE (2 bytes per char)
            var prefixBytes = Encoding.Unicode.GetBytes(prefix);
            var offset = FindBytes(result, prefixBytes);
            if (offset < 0)
                continue;

            // Total byte length of the sentinel in the binary = totalCharLen * 2 (UTF-16LE)
            var totalByteLen = totalCharLen * 2;
            // Suffix "@@" = 4 bytes in UTF-16LE
            var suffixByteLen = 4;
            var prefixByteLen = prefixBytes.Length;
            var payloadByteLen = totalByteLen - prefixByteLen - suffixByteLen;

            // Payload area starts right after prefix
            var payloadStart = offset + prefixByteLen;

            // Zero the payload area (preserve the @@ suffix at the end)
            Array.Clear(result, payloadStart, payloadByteLen);

            // Write value as UTF-16LE, truncated to fit
            var valueBytes = Encoding.Unicode.GetBytes(value);
            var copyLen = Math.Min(valueBytes.Length, payloadByteLen);
            Buffer.BlockCopy(valueBytes, 0, result, payloadStart, copyLen);
        }

        return result;
    }

    private static int FindBytes(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
            return -1;

        int limit = haystack.Length - needle.Length;
        for (int i = 0; i <= limit; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
                return i;
        }
        return -1;
    }
}

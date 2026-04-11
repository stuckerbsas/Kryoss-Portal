using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using KryossAgent.Models;

namespace KryossAgent.Engines;

/// <summary>
/// Inspects Windows certificate stores using <see cref="X509Store"/>.
/// AOT-safe: no reflection, no dynamic code, in-box System.Security.Cryptography.X509Certificates.
///
/// Supported <c>CheckType</c> values:
///   count_self_signed  -> int count of certs where Subject == Issuer
///   count_expiring     -> int count of certs where NotAfter is within 30 days
///   count_weak_key     -> int count of certs with RSA &lt; 2048 or ECC &lt; 256
///   list_thumbprints   -> comma-separated thumbprints (capped at 4 KB)
///
/// Required ControlDef fields: <c>StoreName</c> and <c>StoreLocation</c>.
/// Defaults: StoreLocation = "LocalMachine" if not provided.
/// </summary>
public class CertStoreEngine : ICheckEngine
{
    public string Type => "certstore";

    private const int ThumbprintCapBytes = 4096;
    private const int WeakRsaBits = 2048;
    private const int WeakEccBits = 256;

    public List<CheckResult> Execute(IReadOnlyList<ControlDef> controls)
    {
        var results = new List<CheckResult>(controls.Count);
        foreach (var control in controls)
        {
            results.Add(ExecuteOne(control));
        }
        return results;
    }

    private static CheckResult ExecuteOne(ControlDef control)
    {
        var result = new CheckResult { Id = control.Id };

        if (string.IsNullOrEmpty(control.StoreName))
        {
            result.Exists = false;
            result.Value = "ERROR: storeName is required";
            return result;
        }

        var locationStr = control.StoreLocation ?? "LocalMachine";
        if (!TryParseLocation(locationStr, out var location))
        {
            result.Exists = false;
            result.Value = $"ERROR: unknown storeLocation '{locationStr}'";
            return result;
        }

        var checkType = control.CheckType ?? "count_self_signed";

        try
        {
            using var store = new X509Store(control.StoreName, location);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);

            switch (checkType)
            {
                case "count_self_signed":
                {
                    var count = 0;
                    foreach (var cert in store.Certificates)
                    {
                        if (string.Equals(cert.Subject, cert.Issuer, StringComparison.Ordinal))
                            count++;
                        cert.Dispose();
                    }
                    result.Exists = true;
                    result.Value = count;
                    break;
                }

                case "count_expiring":
                {
                    var cutoff = DateTime.UtcNow.AddDays(30);
                    var count = 0;
                    foreach (var cert in store.Certificates)
                    {
                        if (cert.NotAfter.ToUniversalTime() < cutoff)
                            count++;
                        cert.Dispose();
                    }
                    result.Exists = true;
                    result.Value = count;
                    break;
                }

                case "count_weak_key":
                {
                    var count = 0;
                    foreach (var cert in store.Certificates)
                    {
                        if (IsWeakKey(cert))
                            count++;
                        cert.Dispose();
                    }
                    result.Exists = true;
                    result.Value = count;
                    break;
                }

                case "list_thumbprints":
                {
                    // Cap output to ~4 KB (each thumbprint is 40 hex chars + comma = 41 bytes).
                    var sb = new System.Text.StringBuilder(ThumbprintCapBytes);
                    var first = true;
                    foreach (var cert in store.Certificates)
                    {
                        var tp = cert.Thumbprint ?? "";
                        cert.Dispose();
                        if (tp.Length == 0) continue;
                        var addLen = tp.Length + (first ? 0 : 1);
                        if (sb.Length + addLen > ThumbprintCapBytes) break;
                        if (!first) sb.Append(',');
                        sb.Append(tp);
                        first = false;
                    }
                    result.Exists = true;
                    result.Value = sb.ToString();
                    break;
                }

                default:
                    result.Exists = false;
                    result.Value = $"ERROR: unknown checkType '{checkType}'";
                    break;
            }
        }
        catch (CryptographicException ex)
        {
            result.Exists = false;
            result.Value = $"ERROR: cannot open store '{control.StoreName}': {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            result.Exists = null;
            result.Value = $"ERROR: access denied to store '{control.StoreName}': {ex.Message}";
        }
        catch (Exception ex)
        {
            result.Exists = null;
            result.Value = $"ERROR: {ex.Message}";
        }

        return result;
    }

    private static bool TryParseLocation(string value, out StoreLocation location)
    {
        if (string.Equals(value, "LocalMachine", StringComparison.OrdinalIgnoreCase))
        {
            location = StoreLocation.LocalMachine;
            return true;
        }
        if (string.Equals(value, "CurrentUser", StringComparison.OrdinalIgnoreCase))
        {
            location = StoreLocation.CurrentUser;
            return true;
        }
        location = default;
        return false;
    }

    private static bool IsWeakKey(X509Certificate2 cert)
    {
        // Try RSA first
        using (var rsa = cert.GetRSAPublicKey())
        {
            if (rsa is not null)
                return rsa.KeySize < WeakRsaBits;
        }
        // Then ECDsa
        using (var ecdsa = cert.GetECDsaPublicKey())
        {
            if (ecdsa is not null)
                return ecdsa.KeySize < WeakEccBits;
        }
        // Unknown / DSA / other: treat as weak (legacy)
        return true;
    }
}

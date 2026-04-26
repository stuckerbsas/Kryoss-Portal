using System.Security.Cryptography;
using System.Text.Json;
using KryossAgent.Config;
using KryossAgent.Models;

namespace KryossAgent.Services;

/// <summary>
/// Offline fallback: saves assessment payload to local disk when API is unreachable.
/// On next run, checks for pending payloads and uploads them.
/// </summary>
public static class OfflineStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Kryoss", "PendingResults"
    );

    public static void SavePayload(AssessmentPayload payload)
    {
        try
        {
            Directory.CreateDirectory(StorePath);
            var filename = $"result_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.json";
            var path = Path.Combine(StorePath, filename);
            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(payload, KryossJsonContext.Default.AssessmentPayload);
            var protectedBytes = ProtectedData.Protect(jsonBytes, null, DataProtectionScope.LocalMachine);
            File.WriteAllBytes(path, protectedBytes);
            CryptographicOperations.ZeroMemory(jsonBytes);
            Console.WriteLine($"  Saved results offline (DPAPI): {filename}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [WARN] Could not save results offline: {ex.Message}");
        }
    }

    /// <summary>
    /// Save payload as OfflineCollectPayload to a shared folder for later collection.
    /// Includes machine identity so the collector can upload on behalf.
    /// </summary>
    public static void SaveCollectPayload(AssessmentPayload payload, AgentConfig config, string targetDir, bool silent)
    {
        try
        {
            Directory.CreateDirectory(targetDir);
            var hostname = Environment.MachineName;
            var platform = PlatformDetector.DetectPlatform();
            var hw = PlatformDetector.DetectHardware();

            var envelope = new OfflineCollectPayload
            {
                Hostname = hostname,
                Hwid = HardwareFingerprint.Compute(),
                EnrollmentCode = EmbeddedConfig.EnrollmentCode,
                OsName = platform?.Os,
                OsVersion = platform?.Version,
                OsBuild = platform?.Build,
                ProductType = hw.ProductType,
                AgentVersion = "1.5.1",
                CollectedAt = DateTime.UtcNow.ToString("o"),
                Payload = payload,
            };

            var filename = $"collect_{hostname}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
            var path = Path.Combine(targetDir, filename);
            var json = JsonSerializer.Serialize(envelope, KryossJsonContext.Default.OfflineCollectPayload);
            File.WriteAllText(path, json);
            if (!silent) Console.WriteLine($"  Saved to shared folder: {filename}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [ERROR] Could not save to shared folder: {ex.Message}");
        }
    }

    public static List<(string Path, AssessmentPayload Payload)> LoadPending()
    {
        if (!Directory.Exists(StorePath))
            return [];

        var results = new List<(string, AssessmentPayload)>();
        foreach (var file in Directory.GetFiles(StorePath, "result_*.json"))
        {
            try
            {
                var raw = File.ReadAllBytes(file);
                byte[] jsonBytes;
                try
                {
                    jsonBytes = ProtectedData.Unprotect(raw, null, DataProtectionScope.LocalMachine);
                }
                catch (CryptographicException)
                {
                    // Legacy unprotected file — read as plain text
                    jsonBytes = raw;
                }
                var payload = JsonSerializer.Deserialize(jsonBytes, KryossJsonContext.Default.AssessmentPayload);
                if (payload is not null)
                    results.Add((file, payload));
            }
            catch { /* skip corrupt files */ }
        }
        return results;
    }

    public static void RemovePending(string filePath)
    {
        try { File.Delete(filePath); }
        catch { /* non-critical */ }
    }
}

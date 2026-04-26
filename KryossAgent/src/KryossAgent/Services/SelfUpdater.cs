using System.Security.Cryptography;
using System.Text.Json;
using KryossAgent.Config;

namespace KryossAgent.Services;

public static class SelfUpdater
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Kryoss", "update-log.txt");

    public static async Task<bool> CheckAndUpdateAsync(AgentConfig config, bool verbose)
    {
        try
        {
            using var client = new ApiClient(config);
            var versionInfo = await client.CheckLatestVersionAsync();
            if (versionInfo == null) return false;

            var currentVersion = typeof(SelfUpdater).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            if (versionInfo.Version == null || !IsNewer(versionInfo.Version, currentVersion))
            {
                if (verbose) Console.WriteLine($"[UPDATE] Current {currentVersion}, latest {versionInfo.Version ?? "unknown"} — no update needed");
                return false;
            }

            Log($"Update available: {currentVersion} → {versionInfo.Version}");
            Console.WriteLine($"[UPDATE] Downloading v{versionInfo.Version}...");

            var exePath = Environment.ProcessPath ?? typeof(SelfUpdater).Assembly.Location;
            var dir = Path.GetDirectoryName(exePath)!;
            var tempPath = Path.Combine(dir, "KryossAgent.update.exe");

            // Download new binary
            var bytes = await client.DownloadAgentBinaryAsync();
            if (bytes == null || bytes.Length < 1024)
            {
                Log("Download failed or file too small");
                return false;
            }

            if (string.IsNullOrEmpty(versionInfo.Hash))
            {
                Log("Update rejected: server did not provide SHA256 hash");
                Console.Error.WriteLine("[UPDATE] Rejected — no hash provided by server");
                return false;
            }

            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            if (!hash.Equals(versionInfo.Hash, StringComparison.OrdinalIgnoreCase))
            {
                Log($"Hash mismatch: expected {versionInfo.Hash}, got {hash}");
                return false;
            }

            // Write to temp
            await File.WriteAllBytesAsync(tempPath, bytes);

            Log($"Update to v{versionInfo.Version} staged at {tempPath}");
            Console.WriteLine($"[UPDATE] v{versionInfo.Version} staged. Exiting for SCM restart...");

            // Exit with non-zero code — SCM recovery restarts the service.
            // ServiceWorker applies the staged binary on next startup.
            Environment.Exit(1);
            return true; // unreachable but satisfies compiler
        }
        catch (Exception ex)
        {
            Log($"Update check failed: {ex.Message}");
            if (verbose) Console.Error.WriteLine($"[UPDATE] Failed: {ex.Message}");
            return false;
        }
    }

    private static bool IsNewer(string latest, string current)
    {
        if (Version.TryParse(latest, out var latestVer) && Version.TryParse(current, out var currentVer))
            return latestVer > currentVer;
        return false;
    }

    private static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {message}\n");
        }
        catch { }
    }
}

public class VersionInfo
{
    public string? Version { get; set; }
    public string? Hash { get; set; }
    public string? Url { get; set; }
    public string? ReleaseNotes { get; set; }
}

using System.Text.Json;
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
            var json = JsonSerializer.Serialize(payload, KryossAgent.Models.KryossJsonContext.Default.AssessmentPayload);
            File.WriteAllText(path, json);
            Console.WriteLine($"  Saved results offline: {filename}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [WARN] Could not save results offline: {ex.Message}");
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
                var json = File.ReadAllText(file);
                var payload = JsonSerializer.Deserialize(json,
                    KryossAgent.Models.KryossJsonContext.Default.AssessmentPayload);
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

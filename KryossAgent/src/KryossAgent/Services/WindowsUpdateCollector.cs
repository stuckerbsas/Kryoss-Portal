using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace KryossAgent.Services;

public static class WindowsUpdateCollector
{
    public static Task<List<AvailableUpdateItem>> CollectAsync(CancellationToken ct)
    {
        if (Environment.GetEnvironmentVariable("KRYOSS_DISABLE_WU_COLLECTOR") == "true")
        {
            AgentLogger.Log("WU-COLLECT", "Disabled via KRYOSS_DISABLE_WU_COLLECTOR");
            return Task.FromResult(new List<AvailableUpdateItem>());
        }

        var tcs = new TaskCompletionSource<List<AvailableUpdateItem>>();

        var thread = new Thread(() =>
        {
            try
            {
                var result = SearchAvailableUpdates(ct);
                tcs.TrySetResult(result);
            }
            catch (OperationCanceledException)
            {
                AgentLogger.Error("WU-COLLECT", "Timed out");
                tcs.TrySetResult([]);
            }
            catch (COMException ex)
            {
                AgentLogger.Error("WU-COLLECT", $"COM error: 0x{ex.ErrorCode:X8} {ex.Message}");
                tcs.TrySetResult([]);
            }
            catch (Exception ex)
            {
                AgentLogger.Error("WU-COLLECT", $"Failed: {ex.Message}");
                tcs.TrySetResult([]);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Name = "WUA-Collect";
        thread.Start();

        return tcs.Task;
    }

    private static List<AvailableUpdateItem> SearchAvailableUpdates(CancellationToken ct)
    {
        var session = ComCreate("Microsoft.Update.Session");
        try
        {
            ct.ThrowIfCancellationRequested();
            var searcher = ComCall(session, "CreateUpdateSearcher");
            var searchResult = ComCall(searcher, "Search", "IsInstalled=0 AND Type='Software'");
            var updates = ComGet(searchResult, "Updates");
            var count = (int)ComGet(updates, "Count");

            AgentLogger.Log("WU-COLLECT", $"Found {count} available update(s)");
            if (count == 0) return [];

            var result = new List<AvailableUpdateItem>(count);
            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var update = ComGetItem(updates, i);

                var title = (string)ComGet(update, "Title");
                var isMandatory = (bool)ComGet(update, "IsMandatory");
                var maxDownloadSize = (decimal)ComGet(update, "MaxDownloadSize");

                string? kbNumber = null;
                try
                {
                    var kbIds = ComGet(update, "KBArticleIDs");
                    var kbCount = (int)ComGet(kbIds, "Count");
                    if (kbCount > 0)
                        kbNumber = "KB" + (string)ComGetItem(kbIds, 0);
                }
                catch { }

                string? severity = null;
                try { severity = (string)ComGet(update, "MsrcSeverity"); }
                catch { }
                if (string.IsNullOrWhiteSpace(severity)) severity = "Unspecified";

                string? classification = null;
                try
                {
                    var categories = ComGet(update, "Categories");
                    var catCount = (int)ComGet(categories, "Count");
                    for (int c = 0; c < catCount; c++)
                    {
                        var cat = ComGetItem(categories, c);
                        var catName = (string)ComGet(cat, "Name");
                        if (catName is "Security Updates" or "Critical Updates" or "Update Rollups"
                            or "Service Packs" or "Definition Updates" or "Feature Packs" or "Updates")
                        {
                            classification = catName;
                            break;
                        }
                    }
                }
                catch { }

                DateTime? releaseDate = null;
                try { releaseDate = (DateTime)ComGet(update, "LastDeploymentChangeTime"); }
                catch { }

                string? supportUrl = null;
                try { supportUrl = (string)ComGet(update, "SupportUrl"); }
                catch { }
                if (string.IsNullOrWhiteSpace(supportUrl)) supportUrl = null;

                result.Add(new AvailableUpdateItem
                {
                    KbNumber = kbNumber ?? $"NoKB-{i}",
                    Title = title,
                    Severity = severity,
                    Classification = classification,
                    IsMandatory = isMandatory,
                    MaxDownloadSize = (long)maxDownloadSize,
                    ReleaseDate = releaseDate,
                    SupportUrl = supportUrl,
                });
            }

            return result;
        }
        finally
        {
            if (Marshal.IsComObject(session))
                Marshal.ReleaseComObject(session);
        }
    }

    private static object ComCreate(string progId)
    {
        var type = Type.GetTypeFromProgID(progId)
            ?? throw new COMException($"COM class not registered: {progId}");
        return Activator.CreateInstance(type)
            ?? throw new COMException($"Failed to create COM object: {progId}");
    }

    private static object ComCall(object obj, string method, params object?[] args) =>
        obj.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, obj, args)!;

    private static object ComGet(object obj, string property) =>
        obj.GetType().InvokeMember(property, BindingFlags.GetProperty, null, obj, null)!;

    private static object ComGetItem(object collection, int index) =>
        collection.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, collection, [index])!;
}

public class AvailableUpdateItem
{
    [JsonPropertyName("kbNumber")]
    public string KbNumber { get; set; } = null!;

    [JsonPropertyName("title")]
    public string Title { get; set; } = null!;

    [JsonPropertyName("severity")]
    public string? Severity { get; set; }

    [JsonPropertyName("classification")]
    public string? Classification { get; set; }

    [JsonPropertyName("isMandatory")]
    public bool IsMandatory { get; set; }

    [JsonPropertyName("maxDownloadSize")]
    public long MaxDownloadSize { get; set; }

    [JsonPropertyName("releaseDate")]
    public DateTime? ReleaseDate { get; set; }

    [JsonPropertyName("supportUrl")]
    public string? SupportUrl { get; set; }
}

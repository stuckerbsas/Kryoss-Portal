using System.Collections.Concurrent;

namespace KryossAgent.Services;

public static class AgentLogger
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Kryoss", "Logs");

    private static readonly ConcurrentQueue<string> _queue = new();
    private static CancellationTokenSource? _cts;
    private static Task? _writerTask;
    private const long MaxFileSize = 10 * 1024 * 1024; // 10 MB
    private const int RetentionDays = 7;

    public static void Initialize()
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            PruneOldFiles();
            _cts = new CancellationTokenSource();
            _writerTask = Task.Run(() => WriterLoop(_cts.Token));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[LOGGER] Failed to initialize: {ex.Message}");
        }
    }

    public static void Log(string phase, string message)
    {
        var line = $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ} [{phase.ToUpperInvariant()}] {message}";
        _queue.Enqueue(line);
        Console.WriteLine(line);
    }

    public static void Error(string phase, string message)
    {
        var line = $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ} [{phase.ToUpperInvariant()}] ERROR {message}";
        _queue.Enqueue(line);
        Console.Error.WriteLine(line);
    }

    public static void Shutdown()
    {
        _cts?.Cancel();
        try { _writerTask?.Wait(TimeSpan.FromSeconds(3)); } catch { }
        FlushAll();
    }

    private static async Task WriterLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
            FlushAll();
        }
    }

    private static void FlushAll()
    {
        if (_queue.IsEmpty) return;
        try
        {
            var path = GetCurrentLogPath();
            using var writer = new StreamWriter(path, append: true);
            while (_queue.TryDequeue(out var line))
                writer.WriteLine(line);
        }
        catch { /* logging must never crash the agent */ }
    }

    private static string GetCurrentLogPath()
    {
        var baseName = $"agent-{DateTime.UtcNow:yyyy-MM-dd}";
        var path = Path.Combine(LogDir, $"{baseName}.log");
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length >= MaxFileSize)
            {
                var rotated = Path.Combine(LogDir, $"{baseName}.1.log");
                if (File.Exists(rotated)) File.Delete(rotated);
                File.Move(path, rotated);
            }
        }
        catch { /* non-fatal */ }
        return path;
    }

    private static void PruneOldFiles()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
            foreach (var file in Directory.GetFiles(LogDir, "agent-*.log"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                    File.Delete(file);
            }
        }
        catch { /* non-fatal */ }
    }
}

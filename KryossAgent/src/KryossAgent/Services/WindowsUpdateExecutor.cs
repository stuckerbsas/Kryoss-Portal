using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using KryossAgent.Models;
using Microsoft.Win32;

namespace KryossAgent.Services;

/// <summary>
/// Executes Windows Update operations via WUA COM APIs on a dedicated STA thread.
/// Phase 1: search + download + install, report rebootRequired. No auto-reboot.
/// </summary>
public static class WindowsUpdateExecutor
{
    public static Task<TaskResultPayload> ExecuteAsync(
        PendingRemediationTask task, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<TaskResultPayload>();

        var thread = new Thread(() =>
        {
            try
            {
                var result = RunWuaOperations(task, ct);
                tcs.TrySetResult(result);
            }
            catch (OperationCanceledException)
            {
                tcs.TrySetResult(new TaskResultPayload
                {
                    TaskId = task.Id,
                    Success = false,
                    ErrorMessage = "Cancelled by timeout",
                    ExecutedAt = DateTime.UtcNow,
                });
            }
            catch (COMException ex)
            {
                AgentLogger.Error("WU", $"Task {task.Id} COM error: 0x{ex.ErrorCode:X8} {ex.Message}");
                tcs.TrySetResult(new TaskResultPayload
                {
                    TaskId = task.Id,
                    Success = false,
                    ErrorMessage = $"COM 0x{ex.ErrorCode:X8}: {ex.Message}",
                    ExecutedAt = DateTime.UtcNow,
                });
            }
            catch (Exception ex)
            {
                AgentLogger.Error("WU", $"Task {task.Id} failed: {ex.Message}");
                tcs.TrySetResult(new TaskResultPayload
                {
                    TaskId = task.Id,
                    Success = false,
                    ErrorMessage = ex.Message,
                    ExecutedAt = DateTime.UtcNow,
                });
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Name = $"WUA-{task.Id}";
        thread.Start();

        return tcs.Task;
    }

    private static TaskResultPayload RunWuaOperations(
        PendingRemediationTask task, CancellationToken ct)
    {
        var mode = "security_only";
        if (!string.IsNullOrEmpty(task.Params))
        {
            try
            {
                var p = JsonSerializer.Deserialize(task.Params, KryossJsonContext.Default.DictionaryStringString);
                if (p is not null && p.TryGetValue("mode", out var m))
                    mode = m;
            }
            catch { /* server validates params, fallback to default */ }
        }

        // security_only: AutoSelectOnWebSites=1 captures Security + Critical updates
        // Type='Software' excludes driver updates (risky for remote execution)
        var criteria = mode == "all"
            ? "IsInstalled=0 AND IsHidden=0 AND Type='Software'"
            : "IsInstalled=0 AND IsHidden=0 AND AutoSelectOnWebSites=1 AND Type='Software'";

        AgentLogger.Log("WU", $"Task {task.Id}: searching (mode={mode})");

        // ── Phase 0: System Restore Point (best-effort) ──
        var (rpCreated, rpName) = TryCreateRestorePoint(task.Id);

        var session = ComCreate("Microsoft.Update.Session");
        try
        {
            // ── Phase 1: Search ──
            ct.ThrowIfCancellationRequested();
            var searcher = ComCall(session, "CreateUpdateSearcher");
            var searchResult = ComCall(searcher, "Search", criteria);
            var foundUpdates = ComGet(searchResult, "Updates");
            var totalFound = (int)ComGet(foundUpdates, "Count");

            AgentLogger.Log("WU", $"Task {task.Id}: found {totalFound} update(s)");

            if (totalFound == 0)
            {
                return new TaskResultPayload
                {
                    TaskId = task.Id,
                    Success = true,
                    NewValue = BuildResult(0, 0, 0, false, [], rpCreated, rpName),
                    RestorePointCreated = rpCreated,
                    RestorePointName = rpName,
                    ExecutedAt = DateTime.UtcNow,
                };
            }

            var allTitles = new List<string>();
            for (int i = 0; i < totalFound; i++)
            {
                var update = ComGetItem(foundUpdates, i);
                allTitles.Add((string)ComGet(update, "Title"));
            }

            // ── Phase 2: Download ──
            ct.ThrowIfCancellationRequested();
            var toDownload = ComCreate("Microsoft.Update.UpdateColl");
            var needDownload = 0;
            for (int i = 0; i < totalFound; i++)
            {
                var update = ComGetItem(foundUpdates, i);
                if (!(bool)ComGet(update, "IsDownloaded"))
                {
                    ComCall(toDownload, "Add", update);
                    needDownload++;
                }
            }

            if (needDownload > 0)
            {
                AgentLogger.Log("WU", $"Task {task.Id}: downloading {needDownload} update(s) (low priority)");
                var downloader = ComCall(session, "CreateUpdateDownloader");
                ComSet(downloader, "Updates", toDownload);
                try { ComSet(downloader, "Priority", 1); } // dpLow = 1, reduces bandwidth contention
                catch { /* priority not supported on older WUA versions */ }
                var dlResult = ComCall(downloader, "Download");
                var dlCode = (int)ComGet(dlResult, "ResultCode");
                AgentLogger.Log("WU", $"Task {task.Id}: download resultCode={dlCode}");
            }

            // ── Phase 3: Install ──
            ct.ThrowIfCancellationRequested();
            var toInstall = ComCreate("Microsoft.Update.UpdateColl");
            var installTitles = new List<string>();
            for (int i = 0; i < totalFound; i++)
            {
                var update = ComGetItem(foundUpdates, i);
                if ((bool)ComGet(update, "IsDownloaded"))
                {
                    ComCall(toInstall, "Add", update);
                    installTitles.Add(allTitles[i]);
                }
            }

            var readyCount = (int)ComGet(toInstall, "Count");
            if (readyCount == 0)
            {
                return new TaskResultPayload
                {
                    TaskId = task.Id,
                    Success = false,
                    NewValue = BuildResult(totalFound, 0, 0, false, allTitles, rpCreated, rpName),
                    ErrorMessage = "Download failed — no updates ready for install",
                    RestorePointCreated = rpCreated,
                    RestorePointName = rpName,
                    ExecutedAt = DateTime.UtcNow,
                };
            }

            AgentLogger.Log("WU", $"Task {task.Id}: installing {readyCount} update(s)");
            var installer = ComCall(session, "CreateUpdateInstaller");
            ComSet(installer, "Updates", toInstall);
            try { ComSet(installer, "ForceQuiet", true); } catch { }
            try { ComSet(installer, "AllowSourcePrompts", false); } catch { }

            var installResult = ComCall(installer, "Install");
            var instCode = (int)ComGet(installResult, "ResultCode");
            var rebootRequired = (bool)ComGet(installResult, "RebootRequired");

            // ResultCode: 2=Succeeded, 3=SucceededWithErrors, 4=Failed, 5=Aborted
            var installed = new List<string>();
            var failedNames = new List<string>();
            for (int i = 0; i < readyCount; i++)
            {
                var ur = ComCall(installResult, "GetUpdateResult", i);
                var code = (int)ComGet(ur, "ResultCode");
                if (code is 2 or 3)
                    installed.Add(installTitles[i]);
                else
                    failedNames.Add(installTitles[i]);
            }

            AgentLogger.Log("WU", $"Task {task.Id}: {installed.Count} installed, {failedNames.Count} failed, reboot={rebootRequired}");

            return new TaskResultPayload
            {
                TaskId = task.Id,
                Success = instCode is 2 or 3,
                PreviousValue = $"{{\"pending\":{totalFound}}}",
                NewValue = BuildResult(totalFound, readyCount, installed.Count, rebootRequired, installed, rpCreated, rpName),
                ErrorMessage = failedNames.Count > 0
                    ? $"{failedNames.Count} update(s) failed: {string.Join("; ", failedNames.Take(3))}"
                    : null,
                RestorePointCreated = rpCreated,
                RestorePointName = rpName,
                ExecutedAt = DateTime.UtcNow,
            };
        }
        finally
        {
            if (Marshal.IsComObject(session))
                Marshal.ReleaseComObject(session);
        }
    }

    // ── Phase 0: System Restore Point ──

    private static (bool Created, string? Name) TryCreateRestorePoint(long taskId)
    {
        if (Environment.GetEnvironmentVariable("KRYOSS_SKIP_RESTORE_POINT") == "true")
        {
            AgentLogger.Log("WU", $"Task {taskId}: restore point skipped (KRYOSS_SKIP_RESTORE_POINT=true)");
            return (false, null);
        }

        try
        {
            using var srKey = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore");
            if (srKey is null)
            {
                AgentLogger.Log("WU", $"Task {taskId}: SystemRestore registry key not found — skipping restore point");
                return (false, null);
            }

            var disableSR = srKey.GetValue("DisableSR");
            if (disableSR is int d && d == 1)
            {
                AgentLogger.Log("WU", $"Task {taskId}: System Protection disabled — skipping restore point");
                return (false, null);
            }

            using var srKeyW = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", writable: true);
            if (srKeyW is null)
            {
                AgentLogger.Log("WU", $"Task {taskId}: cannot open SystemRestore for write — skipping restore point");
                return (false, null);
            }

            var originalFreq = srKeyW.GetValue("SystemRestorePointCreationFrequency") as int?;
            srKeyW.SetValue("SystemRestorePointCreationFrequency", 0, RegistryValueKind.DWord);

            try
            {
                var rpName = $"Kryoss Pre-Update {DateTime.UtcNow:yyyy-MM-dd HH:mm}";

                var scope = new ManagementScope(@"\\.\root\default");
                scope.Connect();
                using var restoreClass = new ManagementClass(scope, new ManagementPath("SystemRestore"), null);
                var inParams = restoreClass.GetMethodParameters("CreateRestorePoint");
                inParams["Description"] = rpName;
                inParams["RestorePointType"] = 0;   // APPLICATION_INSTALL
                inParams["EventType"] = 100;         // BEGIN_SYSTEM_CHANGE

                var outParams = restoreClass.InvokeMethod("CreateRestorePoint", inParams, null);
                var returnValue = Convert.ToUInt32(outParams["ReturnValue"]);

                if (returnValue == 0)
                {
                    AgentLogger.Log("WU", $"Task {taskId}: restore point created: {rpName}");
                    return (true, rpName);
                }

                AgentLogger.Log("WU", $"Task {taskId}: restore point returned code {returnValue} — continuing without");
                return (false, null);
            }
            finally
            {
                if (originalFreq.HasValue)
                    srKeyW.SetValue("SystemRestorePointCreationFrequency", originalFreq.Value, RegistryValueKind.DWord);
                else
                    srKeyW.DeleteValue("SystemRestorePointCreationFrequency", throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            AgentLogger.Log("WU", $"Task {taskId}: restore point failed: {ex.Message} — continuing without");
            return (false, null);
        }
    }

    // ── COM late-binding helpers (IDispatch, trim-safe — no dynamic/Microsoft.CSharp needed) ──

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

    private static void ComSet(object obj, string property, object value) =>
        obj.GetType().InvokeMember(property, BindingFlags.SetProperty, null, obj, [value]);

    private static object ComGetItem(object collection, int index) =>
        collection.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, collection, [index])!;

    private static string BuildResult(int searched, int downloaded, int installed,
        bool rebootRequired, List<string> titles, bool restorePointCreated, string? restorePointName)
    {
        var sb = new StringBuilder(256);
        sb.Append("{\"searched\":");
        sb.Append(searched);
        sb.Append(",\"downloaded\":");
        sb.Append(downloaded);
        sb.Append(",\"installed\":");
        sb.Append(installed);
        sb.Append(",\"rebootRequired\":");
        sb.Append(rebootRequired ? "true" : "false");
        sb.Append(",\"restorePointCreated\":");
        sb.Append(restorePointCreated ? "true" : "false");
        if (restorePointName is not null)
        {
            sb.Append(",\"restorePointName\":\"");
            sb.Append(restorePointName.Replace("\\", "\\\\").Replace("\"", "\\\""));
            sb.Append('"');
        }
        if (titles.Count > 0)
        {
            sb.Append(",\"updates\":[");
            for (int i = 0; i < titles.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"');
                sb.Append(titles[i].Replace("\\", "\\\\").Replace("\"", "\\\""));
                sb.Append('"');
            }
            sb.Append(']');
        }
        sb.Append('}');
        return sb.ToString();
    }
}

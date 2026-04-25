using System.ServiceProcess;
using System.Text.Json;
using KryossAgent.Models;
using Microsoft.Win32;

namespace KryossAgent.Services;

public static class RemediationExecutor
{
    private static readonly HashSet<string> AllowedActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "set_registry",
        "enable_service",
        "disable_service",
        "set_audit_policy",
        "set_account_policy",
    };

    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Kryoss", "Remediation");

    public static async Task ExecuteTasksAsync(
        ApiClient apiClient,
        IReadOnlyList<PendingRemediationTask> tasks,
        bool verbose = false)
    {
        Directory.CreateDirectory(LogDir);

        foreach (var task in tasks)
        {
            if (!AllowedActions.Contains(task.ActionType))
            {
                if (verbose) Console.WriteLine($"[REMEDIATE] REJECTED unknown action: {task.ActionType} (task {task.Id})");
                await apiClient.ReportTaskResultAsync(new TaskResultPayload
                {
                    TaskId = task.Id,
                    Success = false,
                    ErrorMessage = $"Action type '{task.ActionType}' not in agent whitelist",
                    ExecutedAt = DateTime.UtcNow,
                });
                continue;
            }

            if (verbose) Console.WriteLine($"[REMEDIATE] Executing task {task.Id}: {task.ActionType}");

            var result = task.ActionType.ToLowerInvariant() switch
            {
                "set_registry" => ExecuteSetRegistry(task),
                "enable_service" => ExecuteServiceAction(task, enable: true),
                "disable_service" => ExecuteServiceAction(task, enable: false),
                "set_audit_policy" => ExecuteSetAuditPolicy(task),
                "set_account_policy" => ExecuteSetAccountPolicy(task),
                _ => new TaskResultPayload
                {
                    TaskId = task.Id,
                    Success = false,
                    ErrorMessage = "Unhandled action type",
                    ExecutedAt = DateTime.UtcNow,
                },
            };

            LogResult(task, result);

            await apiClient.ReportTaskResultAsync(result);
            if (verbose) Console.WriteLine($"[REMEDIATE] Task {task.Id}: {(result.Success ? "OK" : "FAILED")} {result.ErrorMessage}");
        }
    }

    private static TaskResultPayload ExecuteSetRegistry(PendingRemediationTask task)
    {
        try
        {
            var p = JsonSerializer.Deserialize<RegistryParams>(task.Params ?? "{}");
            if (p is null || string.IsNullOrEmpty(p.Path) || string.IsNullOrEmpty(p.ValueName))
                return Fail(task, "Invalid registry params");

            var (hive, subPath) = ParseRegistryPath(p.Path);
            if (hive is null)
                return Fail(task, $"Unsupported hive in path: {p.Path}");

            using var key = hive.OpenSubKey(subPath, writable: true)
                ?? hive.CreateSubKey(subPath);

            var previousValue = key.GetValue(p.ValueName)?.ToString();
            var valueKind = (p.ValueType?.ToUpperInvariant()) switch
            {
                "DWORD" => RegistryValueKind.DWord,
                "QWORD" => RegistryValueKind.QWord,
                "SZ" => RegistryValueKind.String,
                "EXPAND_SZ" => RegistryValueKind.ExpandString,
                "MULTI_SZ" => RegistryValueKind.MultiString,
                _ => RegistryValueKind.String,
            };

            object writeValue = valueKind switch
            {
                RegistryValueKind.DWord => int.Parse(p.ValueData ?? "0"),
                RegistryValueKind.QWord => long.Parse(p.ValueData ?? "0"),
                _ => p.ValueData ?? "",
            };

            key.SetValue(p.ValueName, writeValue, valueKind);

            var newValue = key.GetValue(p.ValueName)?.ToString();

            return new TaskResultPayload
            {
                TaskId = task.Id,
                Success = true,
                PreviousValue = JsonSerializer.Serialize(new { path = p.Path, valueName = p.ValueName, value = previousValue }),
                NewValue = JsonSerializer.Serialize(new { path = p.Path, valueName = p.ValueName, value = newValue }),
                ExecutedAt = DateTime.UtcNow,
            };
        }
        catch (Exception ex)
        {
            return Fail(task, ex.Message);
        }
    }

    private static TaskResultPayload ExecuteServiceAction(PendingRemediationTask task, bool enable)
    {
        try
        {
            var p = JsonSerializer.Deserialize<ServiceParams>(task.Params ?? "{}");
            if (p is null || string.IsNullOrEmpty(p.ServiceName))
                return Fail(task, "Invalid service params");

            using var sc = new ServiceController(p.ServiceName);
            var previousStatus = sc.Status.ToString();
            var previousStartType = sc.StartType.ToString();

            if (enable)
            {
                using var regKey = Registry.LocalMachine.OpenSubKey(
                    $@"SYSTEM\CurrentControlSet\Services\{p.ServiceName}", writable: true);
                if (regKey is not null)
                    regKey.SetValue("Start", 2, RegistryValueKind.DWord); // Automatic

                sc.Refresh();
                if (sc.Status == ServiceControllerStatus.Stopped)
                {
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                }
            }
            else
            {
                if (sc.Status == ServiceControllerStatus.Running)
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                }

                using var regKey = Registry.LocalMachine.OpenSubKey(
                    $@"SYSTEM\CurrentControlSet\Services\{p.ServiceName}", writable: true);
                if (regKey is not null)
                    regKey.SetValue("Start", 4, RegistryValueKind.DWord); // Disabled
            }

            sc.Refresh();
            return new TaskResultPayload
            {
                TaskId = task.Id,
                Success = true,
                PreviousValue = JsonSerializer.Serialize(new { serviceName = p.ServiceName, status = previousStatus, startType = previousStartType }),
                NewValue = JsonSerializer.Serialize(new { serviceName = p.ServiceName, status = sc.Status.ToString(), startType = sc.StartType.ToString() }),
                ExecutedAt = DateTime.UtcNow,
            };
        }
        catch (Exception ex)
        {
            return Fail(task, ex.Message);
        }
    }

    private static TaskResultPayload ExecuteSetAuditPolicy(PendingRemediationTask task)
    {
        try
        {
            var p = JsonSerializer.Deserialize<AuditPolicyParams>(task.Params ?? "{}");
            if (p is null || string.IsNullOrEmpty(p.Subcategory))
                return Fail(task, "Invalid audit policy params");

            // Audit policies via registry (Advanced Audit Policy Configuration)
            // HKLM\SYSTEM\CurrentControlSet\Control\Lsa → SCENoApplyLegacyAuditPolicy = 1
            // Then subcategory GUIDs in HKLM\Security\Policy\PolAdtEv
            // Safest approach: use AuditPol registry equivalents
            var auditRegPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\Audit";
            using var key = Registry.LocalMachine.OpenSubKey(auditRegPath, writable: true)
                ?? Registry.LocalMachine.CreateSubKey(auditRegPath);

            var previousValue = key.GetValue(p.Subcategory)?.ToString();

            int settingValue = (p.Setting?.ToLowerInvariant()) switch
            {
                "success" => 1,
                "failure" => 2,
                "successandfailure" => 3,
                "none" => 0,
                _ => 3,
            };

            key.SetValue(p.Subcategory, settingValue, RegistryValueKind.DWord);

            return new TaskResultPayload
            {
                TaskId = task.Id,
                Success = true,
                PreviousValue = JsonSerializer.Serialize(new { subcategory = p.Subcategory, value = previousValue }),
                NewValue = JsonSerializer.Serialize(new { subcategory = p.Subcategory, value = settingValue.ToString() }),
                ExecutedAt = DateTime.UtcNow,
            };
        }
        catch (Exception ex)
        {
            return Fail(task, ex.Message);
        }
    }

    private static TaskResultPayload ExecuteSetAccountPolicy(PendingRemediationTask task)
    {
        // Account policies are set via net accounts / LSA — agent uses registry approach
        return Fail(task, "set_account_policy not yet implemented — use GPO");
    }

    private static (RegistryKey? hive, string subPath) ParseRegistryPath(string path)
    {
        var normalized = path.Replace("HKLM:", "HKLM").Replace("HKCU:", "HKCU")
            .TrimStart('\\');

        if (normalized.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase))
            return (Registry.LocalMachine, normalized[5..]);
        if (normalized.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase))
            return (Registry.CurrentUser, normalized[5..]);
        if (normalized.StartsWith("HKEY_LOCAL_MACHINE\\", StringComparison.OrdinalIgnoreCase))
            return (Registry.LocalMachine, normalized[19..]);
        if (normalized.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase))
            return (Registry.CurrentUser, normalized[18..]);

        return (null, path);
    }

    private static TaskResultPayload Fail(PendingRemediationTask task, string error) => new()
    {
        TaskId = task.Id,
        Success = false,
        ErrorMessage = error,
        ExecutedAt = DateTime.UtcNow,
    };

    private static void LogResult(PendingRemediationTask task, TaskResultPayload result)
    {
        try
        {
            var logFile = Path.Combine(LogDir, $"{task.Id}.json");
            var log = JsonSerializer.Serialize(new
            {
                task.Id,
                task.ActionType,
                task.Params,
                result.Success,
                result.PreviousValue,
                result.NewValue,
                result.ErrorMessage,
                result.ExecutedAt,
            });
            File.WriteAllText(logFile, log);
        }
        catch { }
    }
}

internal class RegistryParams
{
    public string? Path { get; set; }
    public string? ValueName { get; set; }
    public string? ValueData { get; set; }
    public string? ValueType { get; set; }
}

internal class ServiceParams
{
    public string? ServiceName { get; set; }
}

internal class AuditPolicyParams
{
    public string? Subcategory { get; set; }
    public string? Setting { get; set; }
}

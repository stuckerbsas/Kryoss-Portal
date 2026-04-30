using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using KryossAgent.Config;
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
        "restart_service",
        "stop_service",
        "set_service_startup",
        "set_audit_policy",
        "set_account_policy",
        "windows_update",
    };

    private static readonly string[] AllowedRegistryPrefixes =
    {
        @"HKLM\SYSTEM\CurrentControlSet\Services\",
        @"HKLM\SOFTWARE\Policies\",
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\",
        @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\",
    };

    internal static readonly HashSet<string> ProtectedServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "RpcSs", "RpcEptMapper", "DcomLaunch", "LSM",
        "SamSs", "lsass", "services", "wininit",
        "CryptSvc", "TrustedInstaller", "WinDefend",
        "EventLog", "Winmgmt", "BFE", "mpssvc",
        "KryossAgent",
    };

    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Kryoss", "Remediation");

    public static async Task ExecuteTasksAsync(
        ApiClient apiClient,
        IReadOnlyList<PendingRemediationTask> tasks,
        bool verbose = false,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(LogDir);
        var config = AgentConfig.Load();

        foreach (var task in tasks)
        {
            if (task.Signature is not null && !string.IsNullOrEmpty(config.MachineSecret))
            {
                var signingString = $"{task.Id}|{task.ActionType}|{task.Params}|{task.ApprovedAt:O}";
                var keyBytes = Encoding.UTF8.GetBytes(config.MachineSecret);
                var expected = Convert.ToHexString(
                    HMACSHA256.HashData(keyBytes, Encoding.UTF8.GetBytes(signingString))
                ).ToLowerInvariant();

                if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(expected),
                    Encoding.UTF8.GetBytes(task.Signature.ToLowerInvariant())))
                {
                    AgentLogger.Error("REMEDIATE", $"Task {task.Id}: REJECTED — invalid signature");
                    await apiClient.ReportTaskResultAsync(new TaskResultPayload
                    {
                        TaskId = task.Id,
                        Success = false,
                        ErrorMessage = "rejected_invalid_signature",
                        ExecutedAt = DateTime.UtcNow,
                    });
                    continue;
                }
            }
            else if (task.Signature is null && !string.IsNullOrEmpty(config.MachineSecret))
            {
                AgentLogger.Error("REMEDIATE", $"Task {task.Id}: REJECTED — unsigned task");
                await apiClient.ReportTaskResultAsync(new TaskResultPayload
                {
                    TaskId = task.Id,
                    Success = false,
                    ErrorMessage = "rejected_unsigned_task",
                    ExecutedAt = DateTime.UtcNow,
                });
                continue;
            }

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

            if (task.ActionType is "enable_service" or "disable_service" or "restart_service"
                or "stop_service" or "set_service_startup")
            {
                var svcName = ExtractServiceName(task.Params);
                if (!string.IsNullOrEmpty(svcName) && ProtectedServices.Contains(svcName))
                {
                    AgentLogger.Error("REMEDIATE", $"Task {task.Id}: REJECTED — '{svcName}' is a protected service");
                    await apiClient.ReportTaskResultAsync(new TaskResultPayload
                    {
                        TaskId = task.Id,
                        Success = false,
                        ErrorMessage = $"Service '{svcName}' is protected",
                        ExecutedAt = DateTime.UtcNow,
                    });
                    continue;
                }
            }

            if (verbose) Console.WriteLine($"[REMEDIATE] Executing task {task.Id}: {task.ActionType}");

            TaskResultPayload result;
            if (task.ActionType.Equals("windows_update", StringComparison.OrdinalIgnoreCase))
            {
                result = await WindowsUpdateExecutor.ExecuteAsync(task, ct);
            }
            else
            {
                result = task.ActionType.ToLowerInvariant() switch
                {
                    "set_registry" => ExecuteSetRegistry(task),
                    "enable_service" => ExecuteServiceAction(task, enable: true),
                    "disable_service" => ExecuteServiceAction(task, enable: false),
                    "restart_service" => ExecuteRestartService(task),
                    "stop_service" => ExecuteStopService(task),
                    "set_service_startup" => ExecuteSetServiceStartup(task),
                    "set_audit_policy" => ExecuteSetAuditPolicy(task),
                    "set_account_policy" => ExecuteSetAccountPolicy(task),
                    _ => Fail(task, "Unhandled action type"),
                };
            }

            LogResult(task, result);
            await apiClient.ReportTaskResultAsync(result);
            AgentLogger.Log("REMEDIATE", $"Task {task.Id}: {(result.Success ? "OK" : "FAILED")} {result.ErrorMessage}");
        }
    }

    private static string? ExtractServiceName(string? paramsJson)
    {
        if (string.IsNullOrEmpty(paramsJson)) return null;
        try
        {
            var p = JsonSerializer.Deserialize(paramsJson, KryossJsonContext.Default.ServiceParams);
            return p?.ServiceName;
        }
        catch { return null; }
    }

    private static TaskResultPayload ExecuteRestartService(PendingRemediationTask task)
    {
        try
        {
            var p = JsonSerializer.Deserialize(task.Params ?? "{}", KryossJsonContext.Default.ServiceParams);
            if (p is null || string.IsNullOrEmpty(p.ServiceName))
                return Fail(task, "Invalid service params");

            using var sc = new ServiceController(p.ServiceName);
            var previousStatus = sc.Status.ToString();

            if (sc.Status == ServiceControllerStatus.Running)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            }
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));

            sc.Refresh();
            return new TaskResultPayload
            {
                TaskId = task.Id,
                Success = true,
                PreviousValue = SvcJson(p.ServiceName, previousStatus),
                NewValue = SvcJson(p.ServiceName, sc.Status.ToString()),
                ExecutedAt = DateTime.UtcNow,
            };
        }
        catch (Exception ex) { return Fail(task, ex.Message); }
    }

    private static TaskResultPayload ExecuteStopService(PendingRemediationTask task)
    {
        try
        {
            var p = JsonSerializer.Deserialize(task.Params ?? "{}", KryossJsonContext.Default.ServiceParams);
            if (p is null || string.IsNullOrEmpty(p.ServiceName))
                return Fail(task, "Invalid service params");

            using var sc = new ServiceController(p.ServiceName);
            var previousStatus = sc.Status.ToString();

            if (sc.Status == ServiceControllerStatus.Running)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            }

            sc.Refresh();
            return new TaskResultPayload
            {
                TaskId = task.Id,
                Success = true,
                PreviousValue = SvcJson(p.ServiceName, previousStatus),
                NewValue = SvcJson(p.ServiceName, sc.Status.ToString()),
                ExecutedAt = DateTime.UtcNow,
            };
        }
        catch (Exception ex) { return Fail(task, ex.Message); }
    }

    private static TaskResultPayload ExecuteSetServiceStartup(PendingRemediationTask task)
    {
        try
        {
            var p = JsonSerializer.Deserialize(task.Params ?? "{}", KryossJsonContext.Default.ServiceStartupParams);
            if (p is null || string.IsNullOrEmpty(p.ServiceName) || string.IsNullOrEmpty(p.StartupType))
                return Fail(task, "Invalid service startup params");

            int startValue = p.StartupType.ToLowerInvariant() switch
            {
                "automatic" => 2,
                "manual" => 3,
                "disabled" => 4,
                _ => -1,
            };
            if (startValue < 0) return Fail(task, $"Invalid startup type: {p.StartupType}");

            using var sc = new ServiceController(p.ServiceName);
            var previousStartType = sc.StartType.ToString();

            using var regKey = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{p.ServiceName}", writable: true);
            if (regKey is null) return Fail(task, $"Service registry key not found: {p.ServiceName}");
            regKey.SetValue("Start", startValue, RegistryValueKind.DWord);

            return new TaskResultPayload
            {
                TaskId = task.Id,
                Success = true,
                PreviousValue = SvcJson(p.ServiceName, previousStartType),
                NewValue = SvcJson(p.ServiceName, p.StartupType),
                ExecutedAt = DateTime.UtcNow,
            };
        }
        catch (Exception ex) { return Fail(task, ex.Message); }
    }

    private static TaskResultPayload ExecuteSetRegistry(PendingRemediationTask task)
    {
        try
        {
            var p = JsonSerializer.Deserialize(task.Params ?? "{}", KryossJsonContext.Default.RegistryParams);
            if (p is null || string.IsNullOrEmpty(p.Path) || string.IsNullOrEmpty(p.ValueName))
                return Fail(task, "Invalid registry params");

            if (!IsPathAllowed(p.Path))
            {
                AgentLogger.Error("REMEDIATE", $"REJECTED registry path outside whitelist: {p.Path}");
                return Fail(task, $"Registry path not in allowed prefixes: {p.Path}");
            }

            var (hive, subPath) = ParseRegistryPath(p.Path);
            if (hive is null) return Fail(task, $"Unsupported hive in path: {p.Path}");

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
                PreviousValue = RegJson(p.Path, p.ValueName, previousValue),
                NewValue = RegJson(p.Path, p.ValueName, newValue),
                ExecutedAt = DateTime.UtcNow,
            };
        }
        catch (Exception ex) { return Fail(task, ex.Message); }
    }

    private static TaskResultPayload ExecuteServiceAction(PendingRemediationTask task, bool enable)
    {
        try
        {
            var p = JsonSerializer.Deserialize(task.Params ?? "{}", KryossJsonContext.Default.ServiceParams);
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
                    regKey.SetValue("Start", 2, RegistryValueKind.DWord);

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
                    regKey.SetValue("Start", 4, RegistryValueKind.DWord);
            }

            sc.Refresh();
            return new TaskResultPayload
            {
                TaskId = task.Id,
                Success = true,
                PreviousValue = SvcJson(p.ServiceName, previousStatus, previousStartType),
                NewValue = SvcJson(p.ServiceName, sc.Status.ToString(), sc.StartType.ToString()),
                ExecutedAt = DateTime.UtcNow,
            };
        }
        catch (Exception ex) { return Fail(task, ex.Message); }
    }

    private static TaskResultPayload ExecuteSetAuditPolicy(PendingRemediationTask task)
    {
        try
        {
            var p = JsonSerializer.Deserialize(task.Params ?? "{}", KryossJsonContext.Default.AuditPolicyParams);
            if (p is null || string.IsNullOrEmpty(p.Subcategory))
                return Fail(task, "Invalid audit policy params");

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
                PreviousValue = $"{{\"subcategory\":\"{Esc(p.Subcategory)}\",\"value\":\"{Esc(previousValue)}\"}}",
                NewValue = $"{{\"subcategory\":\"{Esc(p.Subcategory)}\",\"value\":\"{settingValue}\"}}",
                ExecutedAt = DateTime.UtcNow,
            };
        }
        catch (Exception ex) { return Fail(task, ex.Message); }
    }

    private static TaskResultPayload ExecuteSetAccountPolicy(PendingRemediationTask task) =>
        Fail(task, "set_account_policy not yet implemented — use GPO");

    private static bool IsPathAllowed(string path)
    {
        var normalized = path.Replace("HKEY_LOCAL_MACHINE\\", "HKLM\\")
            .Replace("HKLM:", "HKLM")
            .TrimStart('\\');
        return AllowedRegistryPrefixes.Any(prefix =>
            normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static (RegistryKey? hive, string subPath) ParseRegistryPath(string path)
    {
        var normalized = path.Replace("HKLM:", "HKLM").Replace("HKCU:", "HKCU").TrimStart('\\');
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
            var log = $"{{\"id\":{task.Id},\"actionType\":\"{Esc(task.ActionType)}\",\"params\":\"{Esc(task.Params)}\",\"success\":{(result.Success ? "true" : "false")},\"previousValue\":\"{Esc(result.PreviousValue)}\",\"newValue\":\"{Esc(result.NewValue)}\",\"errorMessage\":\"{Esc(result.ErrorMessage)}\",\"executedAt\":\"{result.ExecutedAt:O}\"}}";
            File.WriteAllText(logFile, log);
        }
        catch { }
    }

    private static string Esc(string? s) => s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";
    private static string RegJson(string path, string name, string? val) =>
        $"{{\"path\":\"{Esc(path)}\",\"valueName\":\"{Esc(name)}\",\"value\":\"{Esc(val)}\"}}";
    private static string SvcJson(string name, string status, string? startType = null) =>
        startType is null
            ? $"{{\"serviceName\":\"{Esc(name)}\",\"status\":\"{Esc(status)}\"}}"
            : $"{{\"serviceName\":\"{Esc(name)}\",\"status\":\"{Esc(status)}\",\"startType\":\"{Esc(startType)}\"}}";
}

public class RegistryParams
{
    public string? Path { get; set; }
    public string? ValueName { get; set; }
    public string? ValueData { get; set; }
    public string? ValueType { get; set; }
}

public class ServiceParams
{
    public string? ServiceName { get; set; }
}

public class ServiceStartupParams
{
    public string? ServiceName { get; set; }
    public string? StartupType { get; set; }
}

public class AuditPolicyParams
{
    public string? Subcategory { get; set; }
    public string? Setting { get; set; }
}

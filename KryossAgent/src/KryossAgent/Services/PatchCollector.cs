using System.ServiceProcess;
using KryossAgent.Models;
using Microsoft.Win32;

namespace KryossAgent.Services;

public static class PatchCollector
{
    public static PatchStatusInfo Collect()
    {
        var info = new PatchStatusInfo();

        DetectUpdateSource(info);
        DetectRebootPending(info);
        DetectLastCheckTimes(info);
        DetectWuServiceStatus(info);
        DetectNinjaManaged(info);
        CollectHotfixes(info);

        return info;
    }

    private static void DetectUpdateSource(PatchStatusInfo info)
    {
        try
        {
            using var wuKey = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate");
            using var auKey = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU");

            var wsusServer = wuKey?.GetValue("WUServer") as string;
            var useWsus = auKey?.GetValue("UseWUServer");

            if (!string.IsNullOrEmpty(wsusServer) && useWsus is int wsusVal && wsusVal == 1)
            {
                info.UpdateSource = "wsus";
                info.WsusServer = wsusServer;
                return;
            }

            using var wufbKey = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate");
            var deferQuality = wufbKey?.GetValue("DeferQualityUpdatesPeriodInDays");
            var deferFeature = wufbKey?.GetValue("DeferFeatureUpdatesPeriodInDays");
            var branchLevel = wufbKey?.GetValue("BranchReadinessLevel");

            if (deferQuality != null || deferFeature != null || branchLevel != null)
            {
                info.UpdateSource = "wufb";
                if (branchLevel is int bl)
                    info.WufbRing = bl switch
                    {
                        2 => "insider-slow",
                        4 => "insider-fast",
                        8 => "release-preview",
                        16 => "semi-annual",
                        32 => "semi-annual-targeted",
                        _ => bl.ToString()
                    };
                return;
            }

            info.UpdateSource = "standalone";
        }
        catch
        {
            info.UpdateSource = "unknown";
        }
    }

    private static void DetectRebootPending(PatchStatusInfo info)
    {
        try
        {
            using var wuReboot = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
            using var cbsReboot = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");

            info.RebootPending = wuReboot != null || cbsReboot != null;
        }
        catch
        {
            info.RebootPending = false;
        }
    }

    private static void DetectLastCheckTimes(PatchStatusInfo info)
    {
        try
        {
            using var detectKey = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\Results\Detect");
            var lastDetect = detectKey?.GetValue("LastSuccessTime") as string;
            if (DateTime.TryParse(lastDetect, out var detectTime))
                info.LastCheckUtc = detectTime.ToUniversalTime();

            using var installKey = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\Results\Install");
            var lastInstall = installKey?.GetValue("LastSuccessTime") as string;
            if (DateTime.TryParse(lastInstall, out var installTime))
                info.LastInstallUtc = installTime.ToUniversalTime();
        }
        catch { }
    }

    private static void DetectWuServiceStatus(PatchStatusInfo info)
    {
        try
        {
            using var sc = new ServiceController("wuauserv");
            info.WuServiceStatus = sc.Status switch
            {
                ServiceControllerStatus.Running => "running",
                ServiceControllerStatus.Stopped => "stopped",
                ServiceControllerStatus.StartPending => "starting",
                ServiceControllerStatus.StopPending => "stopping",
                ServiceControllerStatus.Paused => "paused",
                _ => "unknown"
            };
        }
        catch
        {
            info.WuServiceStatus = "not-found";
        }
    }

    private static void DetectNinjaManaged(PatchStatusInfo info)
    {
        try
        {
            using var sc = new ServiceController("NinjaRMMAgent");
            info.NinjaManaged = sc.Status == ServiceControllerStatus.Running;
            if (info.NinjaManaged && info.UpdateSource == "standalone")
                info.UpdateSource = "ninja";
        }
        catch
        {
            info.NinjaManaged = false;
        }
    }

    private static void CollectHotfixes(PatchStatusInfo info)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT HotFixID, Description, InstalledOn, InstalledBy FROM Win32_QuickFixEngineering");

            var now = DateTime.UtcNow;
            int count30 = 0, count90 = 0;

            foreach (System.Management.ManagementObject obj in searcher.Get())
            {
                var hotfixId = obj["HotFixID"]?.ToString();
                if (string.IsNullOrEmpty(hotfixId)) { obj.Dispose(); continue; }

                DateTime? installedOn = null;
                var installedOnStr = obj["InstalledOn"]?.ToString();
                if (DateTime.TryParse(installedOnStr, out var parsed))
                    installedOn = parsed.ToUniversalTime();

                info.Hotfixes.Add(new HotfixItem
                {
                    HotfixId = hotfixId,
                    Description = obj["Description"]?.ToString(),
                    InstalledOn = installedOn,
                    InstalledBy = obj["InstalledBy"]?.ToString(),
                });

                if (installedOn.HasValue)
                {
                    var age = now - installedOn.Value;
                    if (age.TotalDays <= 30) count30++;
                    if (age.TotalDays <= 90) count90++;
                }

                obj.Dispose();
            }

            info.InstalledCount30d = count30;
            info.InstalledCount90d = count90;

            if (info.Hotfixes.Count > 0 && info.LastInstallUtc == null)
            {
                var latest = info.Hotfixes
                    .Where(h => h.InstalledOn.HasValue)
                    .MaxBy(h => h.InstalledOn!.Value);
                if (latest != null)
                    info.LastInstallUtc = latest.InstalledOn;
            }
        }
        catch { }
    }
}

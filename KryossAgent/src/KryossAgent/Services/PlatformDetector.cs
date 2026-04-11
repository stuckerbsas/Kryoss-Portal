using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using KryossAgent.Models;
using Microsoft.Win32;

namespace KryossAgent.Services;

/// <summary>
/// Detects OS, hardware, security, network, and identity info.
/// Uses registry + .NET APIs only (AOT-safe, no WMI/COM).
/// </summary>
public static class PlatformDetector
{
    public static PlatformInfo DetectPlatform()
    {
        var info = new PlatformInfo();
        try
        {
            using var ntKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (ntKey is not null)
            {
                var productName = ntKey.GetValue("ProductName") as string ?? "Windows";
                var displayVersion = ntKey.GetValue("DisplayVersion") as string;
                var buildNumber = ntKey.GetValue("CurrentBuildNumber") as string;
                var ubr = ntKey.GetValue("UBR");

                if (int.TryParse(buildNumber, out var buildInt) && buildInt >= 22000
                    && productName.Contains("Windows 10", StringComparison.OrdinalIgnoreCase))
                {
                    productName = productName.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase);
                }

                info.Os = productName;
                info.Version = displayVersion;
                info.Build = ubr is not null ? $"{buildNumber}.{ubr}" : buildNumber;
            }
        }
        catch { /* non-critical */ }
        return info;
    }

    public static HardwareInfo DetectHardware()
    {
        var info = new HardwareInfo();
        try
        {
            DetectCpu(info);
            DetectMemory(info);
            DetectDisk(info);
            DetectSystemInfo(info);
            DetectSecurity(info);
            DetectNetwork(info);
            DetectDomain(info);
            DetectLifecycle(info);
        }
        catch { /* non-critical */ }
        return info;
    }

    private static void DetectCpu(HardwareInfo info)
    {
        try
        {
            using var cpuKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            info.Cpu = cpuKey?.GetValue("ProcessorNameString") as string;

            // Count logical processors by enumerating subkeys
            using var cpuRoot = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor");
            if (cpuRoot is not null)
                info.CpuCores = (short)cpuRoot.GetSubKeyNames().Length;
        }
        catch { /* non-critical */ }
    }

    private static void DetectMemory(HardwareInfo info)
    {
        try
        {
            var gcInfo = GC.GetGCMemoryInfo();
            info.RamGb = (short)(gcInfo.TotalAvailableMemoryBytes / (1024L * 1024 * 1024));
        }
        catch { /* non-critical */ }
    }

    private static void DetectDisk(HardwareInfo info)
    {
        try
        {
            info.DiskType = DetectDiskType();

            var sysDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var driveInfo = new DriveInfo(sysDrive);
            if (driveInfo.IsReady)
            {
                info.DiskSizeGb = (int)(driveInfo.TotalSize / (1024L * 1024 * 1024));
                info.DiskFreeGb = Math.Round((decimal)driveInfo.AvailableFreeSpace / (1024L * 1024 * 1024), 2);
            }
        }
        catch { /* non-critical */ }
    }

    private static void DetectSystemInfo(HardwareInfo info)
    {
        try
        {
            using var biosKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            if (biosKey is not null)
            {
                info.Manufacturer = biosKey.GetValue("SystemManufacturer") as string;
                info.Model = biosKey.GetValue("SystemProductName") as string;
                info.SerialNumber = biosKey.GetValue("SystemSKU") as string;

                // SKU is often empty — try BaseBoardSerialNumber as fallback
                if (string.IsNullOrWhiteSpace(info.SerialNumber))
                    info.SerialNumber = biosKey.GetValue("BaseBoardSerialNumber") as string;
            }
        }
        catch { /* non-critical */ }
    }

    private static void DetectSecurity(HardwareInfo info)
    {
        try
        {
            // TPM
            using var tpmWmi = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\TPM\WMI");
            using var tpmSw = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Tpm");
            info.TpmPresent = tpmWmi is not null || tpmSw is not null;
            if (info.TpmPresent == true)
            {
                var specVersion = tpmSw?.GetValue("ManufacturerVersionFull20") as string;
                info.TpmVersion = !string.IsNullOrWhiteSpace(specVersion) ? "2.0"
                    : tpmWmi is not null ? "2.0" : "1.2";
            }

            // Secure Boot
            using var sbKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            var sbValue = sbKey?.GetValue("UEFISecureBootEnabled");
            info.SecureBoot = sbValue is int sb && sb == 1;

            // BitLocker — check if system drive has protection
            using var blKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\FVEAPI");
            if (blKey is not null)
            {
                // FVE present = BitLocker feature installed. Check protection status via registry.
                using var volKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\BitlockerStatus");
                info.Bitlocker = volKey is not null;
            }
            else
            {
                info.Bitlocker = false;
            }
        }
        catch { /* non-critical */ }
    }

    private static void DetectNetwork(HardwareInfo info)
    {
        try
        {
            // Get the first active network interface with a gateway (= the one connected to the network)
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback
                    or NetworkInterfaceType.Tunnel) continue;

                var props = nic.GetIPProperties();
                if (props.GatewayAddresses.Count == 0) continue;

                // IP
                var ipv4 = props.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                if (ipv4 is not null)
                    info.IpAddress = ipv4.Address.ToString();

                // MAC
                var mac = nic.GetPhysicalAddress().ToString();
                if (mac.Length == 12)
                    info.MacAddress = string.Join(":", Enumerable.Range(0, 6).Select(i => mac.Substring(i * 2, 2)));

                break;
            }
        }
        catch { /* non-critical */ }
    }

    private static void DetectDomain(HardwareInfo info)
    {
        try
        {
            // Check Azure AD join status
            using var aadKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CloudDomainJoin\JoinInfo");
            var isAadJoined = aadKey?.GetSubKeyNames().Length > 0;

            // Check traditional AD join
            using var domKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters");
            var domain = domKey?.GetValue("Domain") as string;
            var isAdJoined = !string.IsNullOrWhiteSpace(domain);

            if (isAdJoined && isAadJoined == true)
            {
                info.DomainStatus = "HybridJoined";
                info.DomainName = domain;
            }
            else if (isAadJoined == true)
            {
                info.DomainStatus = "AzureADJoined";
            }
            else if (isAdJoined)
            {
                info.DomainStatus = "DomainJoined";
                info.DomainName = domain;
            }
            else
            {
                info.DomainStatus = "Workgroup";
            }
        }
        catch { /* non-critical */ }
    }

    private static void DetectLifecycle(HardwareInfo info)
    {
        try
        {
            // System install date → system age
            using var ntKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var installDateRaw = ntKey?.GetValue("InstallDate");
            if (installDateRaw is int installEpoch)
            {
                var installDate = DateTimeOffset.FromUnixTimeSeconds(installEpoch).UtcDateTime;
                info.SystemAgeDays = (int)(DateTime.UtcNow - installDate).TotalDays;
            }

            // Last boot time
            info.LastBootAt = DateTime.UtcNow.AddMilliseconds(-Environment.TickCount64);
        }
        catch { /* non-critical */ }
    }

    private static string DetectDiskType()
    {
        try
        {
            using var scsiKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\stornvme\Enum");
            if (scsiKey is not null)
            {
                var count = scsiKey.GetValue("Count");
                if (count is int c && c > 0) return "NVMe";
            }

            using var diskKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\FileSystem");
            var ntfsDisableDeleteNotification = diskKey?.GetValue("NtfsDisableDeleteNotification");
            if (ntfsDisableDeleteNotification is int v && v == 0) return "SSD";
        }
        catch { /* fallback */ }
        return "Unknown";
    }
}

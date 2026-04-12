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
            // Build a mapping: drive letter -> disk type from PowerShell
            var driveTypeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                driveTypeMap = GetPhysicalDiskTypeMap();
            }
            catch { /* fallback: all drives get "Unknown" type */ }

            // Detect the system drive type for the aggregate field
            var systemDrive = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var systemDriveLetter = systemDrive.Length > 0 ? systemDrive[..1].ToUpperInvariant() : "C";
            info.DiskType = driveTypeMap.TryGetValue(systemDriveLetter, out var sysType) ? sysType : DetectDiskType();

            // Enumerate all fixed drives and build per-disk inventory
            var diskInfos = new List<Models.DiskInfo>();
            long totalSize = 0, totalFree = 0;

            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;

                var letter = drive.Name[..1].ToUpperInvariant();
                totalSize += drive.TotalSize;
                totalFree += drive.AvailableFreeSpace;

                driveTypeMap.TryGetValue(letter, out var diskType);

                diskInfos.Add(new Models.DiskInfo
                {
                    DriveLetter = letter,
                    Label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? null : drive.VolumeLabel,
                    DiskType = diskType,
                    TotalGb = (int)(drive.TotalSize / (1024L * 1024 * 1024)),
                    FreeGb = Math.Round((decimal)drive.AvailableFreeSpace / (1024L * 1024 * 1024), 2),
                    FileSystem = drive.DriveFormat,
                });
            }

            info.Disks = diskInfos;

            if (totalSize > 0)
            {
                info.DiskSizeGb = (int)(totalSize / (1024L * 1024 * 1024));
                info.DiskFreeGb = Math.Round((decimal)totalFree / (1024L * 1024 * 1024), 2);
            }
        }
        catch { /* non-critical */ }
    }

    /// <summary>
    /// Uses PowerShell to map each drive letter to its physical disk MediaType.
    /// Get-PhysicalDisk for type, Get-Partition to link disk number to drive letter.
    /// </summary>
    private static Dictionary<string, string> GetPhysicalDiskTypeMap()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Step 1: Get physical disk info (DeviceId -> MediaType)
        var diskTypes = new Dictionary<string, string>();
        var psi1 = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = "-NoProfile -Command \"Get-PhysicalDisk | Select-Object DeviceId, MediaType, BusType | ConvertTo-Csv -NoTypeInformation\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        using var proc1 = System.Diagnostics.Process.Start(psi1);
        if (proc1 is null) return result;
        var csv1 = proc1.StandardOutput.ReadToEnd();
        proc1.WaitForExit(10000);

        foreach (var line in csv1.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith('"') && !line.StartsWith("\"DeviceId\"", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split(',');
                if (parts.Length >= 3)
                {
                    var deviceId = parts[0].Trim('"');
                    var mediaType = parts[1].Trim('"');
                    var busType = parts[2].Trim('"');

                    var type = "Unknown";
                    if (busType.Contains("NVMe", StringComparison.OrdinalIgnoreCase))
                        type = "NVMe";
                    else if (mediaType.Contains("SSD", StringComparison.OrdinalIgnoreCase))
                        type = "SSD";
                    else if (mediaType.Contains("HDD", StringComparison.OrdinalIgnoreCase))
                        type = "HDD";
                    else if (mediaType.Contains("Unspecified", StringComparison.OrdinalIgnoreCase))
                        type = "SSD"; // Unspecified with 0 RPM is typically SSD/VM

                    diskTypes[deviceId] = type;
                }
            }
        }

        // Step 2: Map disk number to drive letters via Get-Partition
        var psi2 = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = "-NoProfile -Command \"Get-Partition | Where-Object { $_.DriveLetter } | Select-Object DiskNumber, DriveLetter | ConvertTo-Csv -NoTypeInformation\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        using var proc2 = System.Diagnostics.Process.Start(psi2);
        if (proc2 is null) return result;
        var csv2 = proc2.StandardOutput.ReadToEnd();
        proc2.WaitForExit(10000);

        foreach (var line in csv2.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith('"') && !line.StartsWith("\"DiskNumber\"", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split(',');
                if (parts.Length >= 2)
                {
                    var diskNumber = parts[0].Trim('"');
                    var driveLetter = parts[1].Trim('"');
                    if (!string.IsNullOrEmpty(driveLetter) && diskTypes.TryGetValue(diskNumber, out var type))
                    {
                        result[driveLetter] = type;
                    }
                }
            }
        }

        return result;
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
        // Try PowerShell Get-PhysicalDisk first (most reliable)
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-NoProfile -Command \"(Get-PhysicalDisk | Select -First 1).MediaType\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is not null)
            {
                var output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit(5000);
                if (output.Contains("SSD", StringComparison.OrdinalIgnoreCase)) return "SSD";
                if (output.Contains("NVMe", StringComparison.OrdinalIgnoreCase)) return "NVMe";
                if (output.Contains("HDD", StringComparison.OrdinalIgnoreCase)) return "HDD";
                if (output.Contains("Unspecified", StringComparison.OrdinalIgnoreCase))
                {
                    // Unspecified often means SSD in VMs or newer drives — check rotation
                    var psi2 = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "powershell",
                        Arguments = "-NoProfile -Command \"(Get-PhysicalDisk | Select -First 1).SpindleSpeed\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };
                    using var proc2 = System.Diagnostics.Process.Start(psi2);
                    if (proc2 is not null)
                    {
                        var rpm = proc2.StandardOutput.ReadToEnd().Trim();
                        proc2.WaitForExit(5000);
                        if (rpm == "0" || string.IsNullOrEmpty(rpm)) return "SSD";
                    }
                }
            }
        }
        catch { /* fallback to registry */ }

        // Fallback: registry heuristic
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

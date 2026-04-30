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
            DetectProductType(info);
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
    /// Maps each drive letter to its physical disk type (SSD/HDD/NVMe) via WMI.
    /// Uses MSFT_PhysicalDisk (same data Get-PhysicalDisk queries) — no PowerShell.
    /// v1.4.0: native WMI, zero Process.Start.
    /// </summary>
    private static Dictionary<string, string> GetPhysicalDiskTypeMap()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // Step 1: MSFT_PhysicalDisk in root\Microsoft\Windows\Storage gives
            // DeviceId, MediaType (3=HDD, 4=SSD, 5=SCM), BusType (17=NVMe, etc.)
            var diskTypes = new Dictionary<string, string>();
            using (var searcher = new System.Management.ManagementObjectSearcher(
                @"root\Microsoft\Windows\Storage",
                "SELECT DeviceId, MediaType, BusType, SpindleSpeed FROM MSFT_PhysicalDisk"))
            {
                foreach (System.Management.ManagementObject disk in searcher.Get())
                {
                    var deviceId = disk["DeviceId"]?.ToString() ?? "";
                    var mediaType = Convert.ToUInt16(disk["MediaType"] ?? (ushort)0);
                    var busType = Convert.ToUInt16(disk["BusType"] ?? (ushort)0);
                    var spindleSpeed = Convert.ToUInt32(disk["SpindleSpeed"] ?? 0u);

                    // BusType 17 = NVMe
                    var type = busType == 17 ? "NVMe"
                        : mediaType == 4 ? "SSD"           // 4 = SSD
                        : mediaType == 3 ? "HDD"           // 3 = HDD
                        : spindleSpeed == 0 ? "SSD"        // Unspecified + 0 RPM = SSD (VM/newer)
                        : "Unknown";

                    diskTypes[deviceId] = type;
                    disk.Dispose();
                }
            }

            // Step 2: MSFT_Partition links disk number to drive letter
            using (var searcher = new System.Management.ManagementObjectSearcher(
                @"root\Microsoft\Windows\Storage",
                "SELECT DiskNumber, DriveLetter FROM MSFT_Partition WHERE DriveLetter > 0"))
            {
                foreach (System.Management.ManagementObject partition in searcher.Get())
                {
                    var diskNumber = partition["DiskNumber"]?.ToString() ?? "";
                    var letterObj = partition["DriveLetter"];
                    if (letterObj != null && diskTypes.TryGetValue(diskNumber, out var type))
                    {
                        // DriveLetter is a UInt16 char code (e.g. 67 = 'C')
                        var letterChar = (char)Convert.ToUInt16(letterObj);
                        result[letterChar.ToString()] = type;
                    }
                    partition.Dispose();
                }
            }
        }
        catch { /* WMI query failed — caller uses fallback */ }

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
                info.SerialNumber = biosKey.GetValue("SystemSerialNumber") as string;

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

    private static void DetectProductType(HardwareInfo info)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT ProductType FROM Win32_OperatingSystem");
            foreach (System.Management.ManagementObject os in searcher.Get())
            {
                info.ProductType = Convert.ToInt32(os["ProductType"] ?? 0);
                os.Dispose();
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
            var aadSubKeys = aadKey?.GetSubKeyNames();
            var isAadJoined = aadSubKeys?.Length > 0;

            if (isAadJoined == true)
            {
                try
                {
                    using var joinKey = aadKey!.OpenSubKey(aadSubKeys![0]);
                    info.AadTenantId = joinKey?.GetValue("TenantId") as string;
                }
                catch { /* non-critical */ }
            }

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
        // v1.4.0: WMI MSFT_PhysicalDisk (native, no PowerShell)
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                @"root\Microsoft\Windows\Storage",
                "SELECT MediaType, BusType, SpindleSpeed FROM MSFT_PhysicalDisk");
            foreach (System.Management.ManagementObject disk in searcher.Get())
            {
                var mediaType = Convert.ToUInt16(disk["MediaType"] ?? (ushort)0);
                var busType = Convert.ToUInt16(disk["BusType"] ?? (ushort)0);
                var spindleSpeed = Convert.ToUInt32(disk["SpindleSpeed"] ?? 0u);
                disk.Dispose();

                if (busType == 17) return "NVMe";
                if (mediaType == 4) return "SSD";
                if (mediaType == 3) return "HDD";
                if (spindleSpeed == 0) return "SSD"; // Unspecified + 0 RPM = SSD
                return "Unknown";
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

    public static List<LocalAdminItem> EnumerateLocalAdmins()
    {
        var admins = new List<LocalAdminItem>();
        try
        {
            using var group = new System.DirectoryServices.DirectoryEntry(
                $"WinNT://./{Environment.MachineName}/Administrators,group");
            foreach (var memberObj in (System.Collections.IEnumerable)group.Invoke("Members")!)
            {
                using var member = new System.DirectoryServices.DirectoryEntry(memberObj);
                var name = member.Name;
                var schemaClass = member.SchemaClassName; // User or Group
                var path = member.Path; // WinNT://DOMAIN/name or WinNT://MACHINE/name
                var source = path.Contains($"/{Environment.MachineName}/", StringComparison.OrdinalIgnoreCase)
                    ? "Local" : "Domain";
                admins.Add(new LocalAdminItem { Name = name, Type = schemaClass, Source = source });
            }
        }
        catch { }
        return admins;
    }
}

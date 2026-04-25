using System.Management;
using KryossAgent.Models;

namespace KryossAgent.Services;

public static class WmiProbe
{
    public static async Task<List<SnmpDeviceResult>> ProbeAsync(
        IReadOnlyList<string> targets, bool verbose, int concurrency = 10)
    {
        var results = new List<SnmpDeviceResult>();
        var sem = new SemaphoreSlim(concurrency);
        var lockObj = new object();

        var tasks = targets.Select(async ip =>
        {
            await sem.WaitAsync();
            try
            {
                var dev = ProbeHost(ip, verbose);
                if (dev != null)
                    lock (lockObj) results.Add(dev);
            }
            finally { sem.Release(); }
        });

        await Task.WhenAll(tasks);
        return results;
    }

    private static SnmpDeviceResult? ProbeHost(string ip, bool verbose)
    {
        try
        {
            var scope = new ManagementScope($@"\\{ip}\root\cimv2");
            scope.Options.Timeout = TimeSpan.FromSeconds(10);
            scope.Connect();
            if (!scope.IsConnected) return null;

            var dev = new SnmpDeviceResult { Ip = ip, DeviceType = "computer" };

            // OS info
            using (var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT Caption, Version, BuildNumber, CSName FROM Win32_OperatingSystem")))
            {
                foreach (var obj in searcher.Get())
                {
                    dev.SysDescr = obj["Caption"]?.ToString();
                    dev.SysName = obj["CSName"]?.ToString();
                    break;
                }
            }

            // CPU
            string? cpuName = null;
            int? cpuCores = null;
            using (var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT Name, NumberOfCores FROM Win32_Processor")))
            {
                foreach (var obj in searcher.Get())
                {
                    cpuName = obj["Name"]?.ToString()?.Trim();
                    cpuCores = Convert.ToInt32(obj["NumberOfCores"]);
                    break;
                }
            }

            // Memory
            long? totalMemMb = null;
            using (var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem")))
            {
                foreach (var obj in searcher.Get())
                {
                    if (obj["TotalPhysicalMemory"] is not null)
                        totalMemMb = Convert.ToInt64(obj["TotalPhysicalMemory"]) / (1024 * 1024);
                    break;
                }
            }

            // Disk
            int? diskTotalGb = null;
            int? diskUsedGb = null;
            using (var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT Size, FreeSpace FROM Win32_LogicalDisk WHERE DriveType=3")))
            {
                long totalBytes = 0, freeBytes = 0;
                foreach (var obj in searcher.Get())
                {
                    if (obj["Size"] is not null) totalBytes += Convert.ToInt64(obj["Size"]);
                    if (obj["FreeSpace"] is not null) freeBytes += Convert.ToInt64(obj["FreeSpace"]);
                }
                if (totalBytes > 0)
                {
                    diskTotalGb = (int)(totalBytes / (1024L * 1024 * 1024));
                    diskUsedGb = (int)((totalBytes - freeBytes) / (1024L * 1024 * 1024));
                }
            }

            // Services (running)
            int serviceCount = 0;
            using (var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT Name FROM Win32_Service WHERE State='Running'")))
            {
                foreach (var _ in searcher.Get()) serviceCount++;
            }

            // Shares
            var shares = new List<string>();
            using (var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT Name FROM Win32_Share")))
            {
                foreach (var obj in searcher.Get())
                {
                    var name = obj["Name"]?.ToString();
                    if (name != null) shares.Add(name);
                }
            }

            // MAC address (first physical adapter)
            using (var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT MACAddress FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=True")))
            {
                foreach (var obj in searcher.Get())
                {
                    var mac = obj["MACAddress"]?.ToString();
                    if (!string.IsNullOrEmpty(mac))
                    {
                        dev.MacAddress = mac.Replace(':', '-').ToUpperInvariant();
                        break;
                    }
                }
            }

            dev.HostResources = new SnmpHostResources
            {
                CpuLoadPercent = null,
                MemoryTotalMb = totalMemMb,
                MemoryUsedMb = null,
                ProcessCount = serviceCount,
                Storage = diskTotalGb.HasValue
                    ? [new SnmpStorageEntry
                    {
                        Description = "All fixed disks",
                        Type = "fixedDisk",
                        TotalMb = diskTotalGb.Value * 1024L,
                        UsedMb = (diskUsedGb ?? 0) * 1024L,
                    }]
                    : null,
            };

            // Store extra info in VendorData
            var extra = new Dictionary<string, string>();
            if (cpuName != null) extra["cpu"] = cpuName;
            if (cpuCores.HasValue) extra["cpuCores"] = cpuCores.Value.ToString();
            if (shares.Count > 0) extra["shares"] = string.Join(", ", shares);
            if (extra.Count > 0) dev.VendorData = extra;

            if (verbose)
                Console.WriteLine($"  [WMI] {ip}: {dev.SysName} — {dev.SysDescr}");

            return dev;
        }
        catch (Exception ex)
        {
            if (verbose) Console.Error.WriteLine($"  [WMI] {ip} failed: {ex.Message}");
            return null;
        }
    }
}

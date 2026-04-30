using System.Net;
using KryossAgent.Models;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;

namespace KryossAgent.Services;

public static class SnmpScanner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private const int Port = 161;
    private const int MaxRepetitions = 20;

    // Standard OIDs
    private static class Oids
    {
        public const string SysDescr    = "1.3.6.1.2.1.1.1.0";
        public const string SysObjectId = "1.3.6.1.2.1.1.2.0";
        public const string SysUptime   = "1.3.6.1.2.1.1.3.0";
        public const string SysContact  = "1.3.6.1.2.1.1.4.0";
        public const string SysName     = "1.3.6.1.2.1.1.5.0";
        public const string SysLocation = "1.3.6.1.2.1.1.6.0";

        // IF-MIB ifTable
        public const string IfNumber    = "1.3.6.1.2.1.2.1.0";
        public const string IfDescr     = "1.3.6.1.2.1.2.2.1.2";
        public const string IfType      = "1.3.6.1.2.1.2.2.1.3";
        public const string IfSpeed     = "1.3.6.1.2.1.2.2.1.5";
        public const string IfPhysAddr  = "1.3.6.1.2.1.2.2.1.6";
        public const string IfAdminStat = "1.3.6.1.2.1.2.2.1.7";
        public const string IfOperStat  = "1.3.6.1.2.1.2.2.1.8";
        public const string IfInOctets  = "1.3.6.1.2.1.2.2.1.10";
        public const string IfInErrors  = "1.3.6.1.2.1.2.2.1.14";
        public const string IfInDisc    = "1.3.6.1.2.1.2.2.1.13";
        public const string IfOutOctets = "1.3.6.1.2.1.2.2.1.16";
        public const string IfOutErrors = "1.3.6.1.2.1.2.2.1.20";
        public const string IfOutDisc   = "1.3.6.1.2.1.2.2.1.19";
        public const string IfName      = "1.3.6.1.2.1.31.1.1.1.1";

        // ENTITY-MIB (chassis info)
        public const string EntPhysDescr    = "1.3.6.1.2.1.47.1.1.1.1.2";
        public const string EntPhysSerial   = "1.3.6.1.2.1.47.1.1.1.1.11";
        public const string EntPhysMfg      = "1.3.6.1.2.1.47.1.1.1.1.12";
        public const string EntPhysModel    = "1.3.6.1.2.1.47.1.1.1.1.13";
        public const string EntPhysFirmware = "1.3.6.1.2.1.47.1.1.1.1.9";

        // LLDP-MIB (1.0.8802.1.1.2)
        public const string LldpRemChassisId   = "1.0.8802.1.1.2.1.4.1.1.5";
        public const string LldpRemPortId      = "1.0.8802.1.1.2.1.4.1.1.7";
        public const string LldpRemPortDesc    = "1.0.8802.1.1.2.1.4.1.1.8";
        public const string LldpRemSysName     = "1.0.8802.1.1.2.1.4.1.1.9";
        public const string LldpRemSysDesc     = "1.0.8802.1.1.2.1.4.1.1.10";
        public const string LldpLocPortId      = "1.0.8802.1.1.2.1.3.7.1.3";
        public const string LldpLocPortDesc    = "1.0.8802.1.1.2.1.3.7.1.4";

        // CISCO-CDP-MIB
        public const string CdpCacheDeviceId   = "1.3.6.1.4.1.9.9.23.1.2.1.1.6";
        public const string CdpCacheDevicePort = "1.3.6.1.4.1.9.9.23.1.2.1.1.7";
        public const string CdpCacheAddress    = "1.3.6.1.4.1.9.9.23.1.2.1.1.4";
        public const string CdpCachePlatform   = "1.3.6.1.4.1.9.9.23.1.2.1.1.8";

        // PRINTER-MIB (RFC 3805)
        public const string PrtMarkerSuppliesDesc = "1.3.6.1.2.1.43.11.1.1.6";
        public const string PrtMarkerSuppliesType = "1.3.6.1.2.1.43.11.1.1.4";
        public const string PrtMarkerSuppliesMax  = "1.3.6.1.2.1.43.11.1.1.8";
        public const string PrtMarkerSuppliesLvl  = "1.3.6.1.2.1.43.11.1.1.9";
        public const string PrtMarkerColorant     = "1.3.6.1.2.1.43.12.1.1.4";
        public const string PrtMarkerLifeCount    = "1.3.6.1.2.1.43.10.2.1.4";

        // HOST-RESOURCES-MIB (RFC 2790)
        public const string HrProcessorLoad    = "1.3.6.1.2.1.25.3.3.1.2";
        public const string HrMemorySize       = "1.3.6.1.2.1.25.2.2.0";
        public const string HrStorageType      = "1.3.6.1.2.1.25.2.3.1.2";
        public const string HrStorageDescr     = "1.3.6.1.2.1.25.2.3.1.3";
        public const string HrStorageAllocUnit = "1.3.6.1.2.1.25.2.3.1.4";
        public const string HrStorageSize      = "1.3.6.1.2.1.25.2.3.1.5";
        public const string HrStorageUsed      = "1.3.6.1.2.1.25.2.3.1.6";
        public const string HrSystemProcesses  = "1.3.6.1.2.1.25.1.6.0";

        // hrStorageType OID suffixes
        public const string HrStorageRam       = "1.3.6.1.2.1.25.2.1.2";
        public const string HrStorageFixedDisk = "1.3.6.1.2.1.25.2.1.4";
        public const string HrStorageVirtMem   = "1.3.6.1.2.1.25.2.1.3";
    }

    /// <summary>
    /// Sweep all local subnets with SNMP GET sysObjectID to discover devices
    /// that aren't in AD or ARP (switches, routers, APs, printers, etc.).
    /// Returns IPs that responded.
    /// </summary>
    public static async Task<List<string>> DiscoverSubnetAsync(SnmpCredentials creds, bool verbose = false)
    {
        var discovered = new List<string>();
        var localIps = GetLocalSubnets();
        if (localIps.Count == 0) return discovered;

        if (verbose) Console.WriteLine($"  [SNMP] Sweeping {localIps.Count} local subnet(s) for SNMP devices...");

        foreach (var (networkAddr, prefixLen) in localIps)
        {
            if (prefixLen < 16 || prefixLen > 30) continue; // skip weird masks

            var hostCount = 1 << (32 - prefixLen);
            if (hostCount > 1024) continue; // cap at /22

            var baseBytes = networkAddr.GetAddressBytes();
            var baseLong = ((long)baseBytes[0] << 24) | ((long)baseBytes[1] << 16)
                         | ((long)baseBytes[2] << 8) | baseBytes[3];

            var probeTasks = new List<Task<string?>>();
            var sem = new SemaphoreSlim(50);

            for (int i = 1; i < hostCount - 1; i++)
            {
                var targetLong = baseLong + i;
                var ipStr = $"{(targetLong >> 24) & 0xFF}.{(targetLong >> 16) & 0xFF}.{(targetLong >> 8) & 0xFF}.{targetLong & 0xFF}";

                probeTasks.Add(Task.Run(async () =>
                {
                    await sem.WaitAsync();
                    try
                    {
                        var ep = new IPEndPoint(IPAddress.Parse(ipStr), Port);
                        var community = new OctetString(creds.Community ?? "public");
                        try
                        {
                            var result = await Task.Run(() =>
                                Messenger.Get(
                                    VersionCode.V2,
                                    ep,
                                    community,
                                    new List<Variable> { new(new ObjectIdentifier(Oids.SysObjectId)) },
                                    2000));

                            if (result != null && result.Count > 0
                                && result[0].Data.TypeCode != SnmpType.NoSuchObject
                                && result[0].Data.TypeCode != SnmpType.NoSuchInstance)
                                return ipStr;
                        }
                        catch { }
                        return null;
                    }
                    finally { sem.Release(); }
                }));
            }

            var results = await Task.WhenAll(probeTasks);
            var found = results.Where(ip => ip != null).Cast<string>().ToList();
            discovered.AddRange(found);

            if (verbose && found.Count > 0)
                Console.WriteLine($"  [SNMP] Subnet {networkAddr}/{prefixLen}: {found.Count} SNMP device(s) discovered");
        }

        return discovered;
    }

    private static List<(IPAddress Network, int PrefixLength)> GetLocalSubnets()
    {
        var subnets = new List<(IPAddress, int)>();
        try
        {
            foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is System.Net.NetworkInformation.NetworkInterfaceType.Loopback
                    or System.Net.NetworkInformation.NetworkInterfaceType.Tunnel) continue;

                foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;

                    var ipBytes = addr.Address.GetAddressBytes();
                    var maskBytes = addr.IPv4Mask.GetAddressBytes();
                    var networkBytes = new byte[4];
                    for (int j = 0; j < 4; j++) networkBytes[j] = (byte)(ipBytes[j] & maskBytes[j]);

                    var prefix = 0;
                    foreach (var b in maskBytes)
                        for (int bit = 7; bit >= 0; bit--)
                            if ((b & (1 << bit)) != 0) prefix++;
                            else goto done;
                    done:

                    var networkAddr = new IPAddress(networkBytes);
                    if (!subnets.Any(s => s.Item1.Equals(networkAddr)))
                        subnets.Add((networkAddr, prefix));
                }
            }
        }
        catch { }
        return subnets;
    }

    public static async Task<SnmpScanResult> ScanAsync(
        SnmpCredentials creds, List<string> targets, bool verbose = false, CancellationToken ct = default)
    {
        var result = new SnmpScanResult();
        var semaphore = new SemaphoreSlim(10);

        var tasks = targets.Select(async ip =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                using var deviceCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deviceCts.CancelAfter(TimeSpan.FromSeconds(60));
                var device = await ScanDeviceAsync(ip, creds, verbose);
                if (device != null)
                    lock (result.Devices) result.Devices.Add(device);
                else
                    lock (result.Unreachable) result.Unreachable.Add(ip);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                if (verbose) Console.Error.WriteLine($"  [SNMP] {ip}: timeout after 60s");
                lock (result.Unreachable) result.Unreachable.Add(ip);
            }
            finally { semaphore.Release(); }
        });

        await Task.WhenAll(tasks);
        result.ScannedAt = DateTime.UtcNow;
        return result;
    }

    private static async Task<SnmpDeviceResult?> ScanDeviceAsync(
        string ip, SnmpCredentials creds, bool verbose)
    {
        try
        {
            var endpoint = new IPEndPoint(IPAddress.Parse(ip), Port);

            // Test connectivity with sysName
            var sysVars = await GetScalarsAsync(endpoint, creds, new[]
            {
                Oids.SysDescr, Oids.SysObjectId, Oids.SysUptime,
                Oids.SysContact, Oids.SysName, Oids.SysLocation
            });

            if (sysVars == null || sysVars.Count == 0) return null;

            var device = new SnmpDeviceResult
            {
                Ip = ip,
                SysDescr = GetString(sysVars, Oids.SysDescr),
                SysObjectId = GetString(sysVars, Oids.SysObjectId),
                SysName = GetString(sysVars, Oids.SysName),
                SysContact = GetString(sysVars, Oids.SysContact),
                SysLocation = GetString(sysVars, Oids.SysLocation),
            };

            // Parse uptime (hundredths of seconds → seconds)
            if (sysVars.TryGetValue(Oids.SysUptime, out var uptimeVar) && uptimeVar is TimeTicks tt)
                device.SysUptimeSeconds = (long)(tt.ToTimeSpan().TotalSeconds);

            if (verbose) Console.WriteLine($"  [SNMP] {ip}: {device.SysName} ({device.SysDescr?[..Math.Min(60, device.SysDescr.Length)]})");

            // Walk interfaces
            device.Interfaces = await WalkInterfacesAsync(endpoint, creds);

            // Entity info (chassis)
            device.Entity = await GetEntityInfoAsync(endpoint, creds);

            // LLDP neighbors (port mapping)
            device.LldpNeighbors = await GetLldpNeighborsAsync(endpoint, creds);

            // CDP neighbors (Cisco port mapping)
            device.CdpNeighbors = await GetCdpNeighborsAsync(endpoint, creds);

            // HOST-RESOURCES-MIB (CPU, RAM, storage — works on any SNMP device)
            device.HostResources = await GetHostResourcesAsync(endpoint, creds);

            // Classify device type from sysObjectId + sysDescr
            device.DeviceType = ClassifyDeviceType(device.SysObjectId, device.SysDescr);

            // Printer: get toner/ink levels + page count
            if (device.DeviceType == "printer")
            {
                device.PrinterSupplies = await GetPrinterSuppliesAsync(endpoint, creds);
                device.PageCount = await GetPrinterPageCountAsync(endpoint, creds);
                if (verbose && device.PrinterSupplies?.Count > 0)
                    Console.WriteLine($"  [SNMP] {ip}: {device.PrinterSupplies.Count} supplies, {device.PageCount ?? 0} pages");
            }

            if (verbose) Console.WriteLine($"  [SNMP] {ip}: type={device.DeviceType}");

            return device;
        }
        catch (Exception ex)
        {
            if (verbose) Console.WriteLine($"  [SNMP] {ip}: FAILED - {ex.Message}");
            return null;
        }
    }

    private static async Task<Dictionary<string, ISnmpData>?> GetScalarsAsync(
        IPEndPoint endpoint, SnmpCredentials creds, string[] oids)
    {
        var variables = oids.Select(o => new Variable(new ObjectIdentifier(o))).ToList();

        try
        {
            IList<Variable> results;
            var version = creds.Version == 1 ? VersionCode.V1 : VersionCode.V2;
            // v12 SharpSnmpLib: v3 requires discovery + privacy provider on BulkWalk/Walk.
            // For Get, we use the simple v1/v2c API even for v3 conceptually —
            // full SNMPv3 support deferred (most MSP gear uses v2c).
            results = await Task.Run(() => Messenger.Get(
                version, endpoint, new OctetString(creds.Community ?? "public"),
                variables, (int)Timeout.TotalMilliseconds));

            var dict = new Dictionary<string, ISnmpData>();
            foreach (var v in results)
            {
                if (v.Data.TypeCode != SnmpType.NoSuchObject &&
                    v.Data.TypeCode != SnmpType.NoSuchInstance &&
                    v.Data.TypeCode != SnmpType.EndOfMibView)
                {
                    dict[v.Id.ToString()] = v.Data;
                }
            }
            return dict.Count > 0 ? dict : null;
        }
        catch { return null; }
    }

    private static async Task<List<SnmpInterfaceResult>> WalkInterfacesAsync(
        IPEndPoint endpoint, SnmpCredentials creds)
    {
        var interfaces = new List<SnmpInterfaceResult>();

        try
        {
            var ifDescrs = await WalkOidAsync(endpoint, creds, Oids.IfDescr);
            var ifTypes = await WalkOidAsync(endpoint, creds, Oids.IfType);
            var ifSpeeds = await WalkOidAsync(endpoint, creds, Oids.IfSpeed);
            var ifPhysAddrs = await WalkOidAsync(endpoint, creds, Oids.IfPhysAddr);
            var ifAdminStats = await WalkOidAsync(endpoint, creds, Oids.IfAdminStat);
            var ifOperStats = await WalkOidAsync(endpoint, creds, Oids.IfOperStat);
            var ifInOctets = await WalkOidAsync(endpoint, creds, Oids.IfInOctets);
            var ifOutOctets = await WalkOidAsync(endpoint, creds, Oids.IfOutOctets);
            var ifInErrors = await WalkOidAsync(endpoint, creds, Oids.IfInErrors);
            var ifOutErrors = await WalkOidAsync(endpoint, creds, Oids.IfOutErrors);
            var ifInDisc = await WalkOidAsync(endpoint, creds, Oids.IfInDisc);
            var ifOutDisc = await WalkOidAsync(endpoint, creds, Oids.IfOutDisc);
            var ifNames = await WalkOidAsync(endpoint, creds, Oids.IfName);

            foreach (var (idx, descr) in ifDescrs)
            {
                var iface = new SnmpInterfaceResult
                {
                    Index = idx,
                    Description = descr,
                    Name = ifNames.GetValueOrDefault(idx),
                    Type = ParseInt(ifTypes.GetValueOrDefault(idx)),
                    SpeedMbps = ParseLong(ifSpeeds.GetValueOrDefault(idx)) / 1_000_000,
                    MacAddress = ifPhysAddrs.GetValueOrDefault(idx),
                    AdminStatus = ParseInt(ifAdminStats.GetValueOrDefault(idx)),
                    OperStatus = ParseInt(ifOperStats.GetValueOrDefault(idx)),
                    InOctets = ParseLong(ifInOctets.GetValueOrDefault(idx)),
                    OutOctets = ParseLong(ifOutOctets.GetValueOrDefault(idx)),
                    InErrors = ParseLong(ifInErrors.GetValueOrDefault(idx)),
                    OutErrors = ParseLong(ifOutErrors.GetValueOrDefault(idx)),
                    InDiscards = ParseLong(ifInDisc.GetValueOrDefault(idx)),
                    OutDiscards = ParseLong(ifOutDisc.GetValueOrDefault(idx)),
                };
                interfaces.Add(iface);
            }
        }
        catch { /* interface walk failed — return what we have */ }

        return interfaces;
    }

    private static async Task<Dictionary<int, string>> WalkOidAsync(
        IPEndPoint endpoint, SnmpCredentials creds, string baseOid)
    {
        var result = new Dictionary<int, string>();
        try
        {
            var oid = new ObjectIdentifier(baseOid);
            var community = new OctetString(creds.Community ?? "public");
            var walked = new List<Variable>();

            if (creds.Version == 1)
            {
                await Task.Run(() => Messenger.Walk(
                    VersionCode.V1, endpoint, community,
                    oid, walked, (int)Timeout.TotalMilliseconds, WalkMode.WithinSubtree));
            }
            else
            {
                // v2c BulkWalk: (version, endpoint, community, contextName, oid, walked, timeout, maxRep, walkMode, privacy, report)
                await Task.Run(() => Messenger.BulkWalk(
                    VersionCode.V2, endpoint, community, OctetString.Empty,
                    oid, walked,
                    (int)Timeout.TotalMilliseconds, MaxRepetitions,
                    WalkMode.WithinSubtree, null, null));
            }

            foreach (var v in walked)
            {
                var suffix = v.Id.ToString()[(baseOid.Length + 1)..];
                if (int.TryParse(suffix, out var idx))
                {
                    var val = v.Data.TypeCode == SnmpType.OctetString
                        ? FormatOctetString(v.Data)
                        : v.Data.ToString();
                    result[idx] = val ?? "";
                }
            }
        }
        catch { /* walk failed */ }

        return result;
    }

    private static async Task<SnmpEntityInfo?> GetEntityInfoAsync(
        IPEndPoint endpoint, SnmpCredentials creds)
    {
        try
        {
            var descrs = await WalkOidAsync(endpoint, creds, Oids.EntPhysDescr);
            var serials = await WalkOidAsync(endpoint, creds, Oids.EntPhysSerial);
            var mfgs = await WalkOidAsync(endpoint, creds, Oids.EntPhysMfg);
            var models = await WalkOidAsync(endpoint, creds, Oids.EntPhysModel);
            var fws = await WalkOidAsync(endpoint, creds, Oids.EntPhysFirmware);

            if (descrs.Count == 0 && serials.Count == 0 && models.Count == 0) return null;

            // First chassis entry (index 1 typically)
            var firstIdx = descrs.Keys.Concat(models.Keys).Concat(serials.Keys).Min();

            return new SnmpEntityInfo
            {
                Model = models.GetValueOrDefault(firstIdx) ?? descrs.GetValueOrDefault(firstIdx),
                Serial = serials.GetValueOrDefault(firstIdx),
                Manufacturer = mfgs.GetValueOrDefault(firstIdx),
                FirmwareVersion = fws.GetValueOrDefault(firstIdx),
            };
        }
        catch { return null; }
    }

    private static async Task<List<LldpNeighbor>> GetLldpNeighborsAsync(
        IPEndPoint endpoint, SnmpCredentials creds)
    {
        var neighbors = new List<LldpNeighbor>();
        try
        {
            var localPortIds = await WalkOidAsync(endpoint, creds, Oids.LldpLocPortId);
            var localPortDescs = await WalkOidAsync(endpoint, creds, Oids.LldpLocPortDesc);
            var remChassisIds = await WalkNestedOidAsync(endpoint, creds, Oids.LldpRemChassisId);
            var remPortIds = await WalkNestedOidAsync(endpoint, creds, Oids.LldpRemPortId);
            var remPortDescs = await WalkNestedOidAsync(endpoint, creds, Oids.LldpRemPortDesc);
            var remSysNames = await WalkNestedOidAsync(endpoint, creds, Oids.LldpRemSysName);
            var remSysDescs = await WalkNestedOidAsync(endpoint, creds, Oids.LldpRemSysDesc);

            foreach (var (key, chassisId) in remChassisIds)
            {
                var localPortNum = key.Split('.').FirstOrDefault();
                int.TryParse(localPortNum, out var lpIdx);
                var localPort = localPortDescs.GetValueOrDefault(lpIdx)
                    ?? localPortIds.GetValueOrDefault(lpIdx)
                    ?? $"port {lpIdx}";

                neighbors.Add(new LldpNeighbor
                {
                    LocalPort = localPort,
                    RemoteChassisId = chassisId,
                    RemotePortId = remPortIds.GetValueOrDefault(key),
                    RemotePortDesc = remPortDescs.GetValueOrDefault(key),
                    RemoteSysName = remSysNames.GetValueOrDefault(key),
                    RemoteSysDesc = remSysDescs.GetValueOrDefault(key),
                });
            }
        }
        catch { /* LLDP not supported on this device */ }
        return neighbors;
    }

    private static async Task<List<CdpNeighbor>> GetCdpNeighborsAsync(
        IPEndPoint endpoint, SnmpCredentials creds)
    {
        var neighbors = new List<CdpNeighbor>();
        try
        {
            var deviceIds = await WalkNestedOidAsync(endpoint, creds, Oids.CdpCacheDeviceId);
            var devicePorts = await WalkNestedOidAsync(endpoint, creds, Oids.CdpCacheDevicePort);
            var addresses = await WalkNestedOidAsync(endpoint, creds, Oids.CdpCacheAddress);
            var platforms = await WalkNestedOidAsync(endpoint, creds, Oids.CdpCachePlatform);

            // Local port index → name mapping from IF-MIB
            var ifNames = await WalkOidAsync(endpoint, creds, Oids.IfName);
            var ifDescrs = await WalkOidAsync(endpoint, creds, Oids.IfDescr);

            foreach (var (key, deviceId) in deviceIds)
            {
                var localIfIdx = key.Split('.').FirstOrDefault();
                int.TryParse(localIfIdx, out var ifIdx);
                var localPort = ifNames.GetValueOrDefault(ifIdx)
                    ?? ifDescrs.GetValueOrDefault(ifIdx)
                    ?? $"port {ifIdx}";

                var rawAddr = addresses.GetValueOrDefault(key);
                string? ip = null;
                if (rawAddr is { Length: > 0 })
                {
                    // CDP address is 4 bytes for IPv4
                    var parts = rawAddr.Split(':');
                    if (parts.Length == 4 && parts.All(p => byte.TryParse(p, System.Globalization.NumberStyles.HexNumber, null, out _)))
                        ip = string.Join(".", parts.Select(p => byte.Parse(p, System.Globalization.NumberStyles.HexNumber)));
                    else
                        ip = rawAddr;
                }

                neighbors.Add(new CdpNeighbor
                {
                    LocalPort = localPort,
                    RemoteDeviceId = deviceId,
                    RemotePortId = devicePorts.GetValueOrDefault(key),
                    RemoteIp = ip,
                    RemotePlatform = platforms.GetValueOrDefault(key),
                });
            }
        }
        catch { /* CDP not supported */ }
        return neighbors;
    }

    /// <summary>
    /// Walk OIDs with multi-level index (e.g. LLDP timeMark.localPort.index, CDP ifIndex.deviceIndex).
    /// Returns dict keyed by the full suffix after the base OID.
    /// </summary>
    private static async Task<Dictionary<string, string>> WalkNestedOidAsync(
        IPEndPoint endpoint, SnmpCredentials creds, string baseOid)
    {
        var result = new Dictionary<string, string>();
        try
        {
            var oid = new ObjectIdentifier(baseOid);
            var community = new OctetString(creds.Community ?? "public");
            var walked = new List<Variable>();

            if (creds.Version == 1)
            {
                await Task.Run(() => Messenger.Walk(
                    VersionCode.V1, endpoint, community,
                    oid, walked, (int)Timeout.TotalMilliseconds, WalkMode.WithinSubtree));
            }
            else
            {
                await Task.Run(() => Messenger.BulkWalk(
                    VersionCode.V2, endpoint, community, OctetString.Empty,
                    oid, walked,
                    (int)Timeout.TotalMilliseconds, MaxRepetitions,
                    WalkMode.WithinSubtree, null, null));
            }

            foreach (var v in walked)
            {
                var suffix = v.Id.ToString()[(baseOid.Length + 1)..];
                var val = v.Data.TypeCode == SnmpType.OctetString
                    ? FormatOctetString(v.Data)
                    : v.Data.ToString();
                result[suffix] = val ?? "";
            }
        }
        catch { /* walk failed */ }
        return result;
    }

    /// <summary>
    /// Pass 2: query vendor-specific OIDs returned by the server profile endpoint.
    /// </summary>
    public static async Task<Dictionary<string, string>> ScanVendorOidsAsync(
        string ip, List<Models.SnmpProfileOidEntry> oids, SnmpCredentials creds, bool verbose = false)
    {
        var data = new Dictionary<string, string>();
        var endpoint = new IPEndPoint(IPAddress.Parse(ip), Port);

        var getOids = oids.Where(o => !o.Walk).ToList();
        var walkOids = oids.Where(o => o.Walk).ToList();

        if (getOids.Count > 0)
        {
            var scalars = await GetScalarsAsync(endpoint, creds,
                getOids.Select(o => o.Oid).ToArray());
            if (scalars != null)
            {
                foreach (var o in getOids)
                {
                    if (scalars.TryGetValue(o.Oid, out var val))
                        data[o.Name] = val.ToString() ?? "";
                }
            }
        }

        foreach (var o in walkOids)
        {
            try
            {
                var walked = await WalkNestedOidAsync(endpoint, creds, o.Oid);
                if (walked.Count == 1)
                    data[o.Name] = walked.Values.First();
                else if (walked.Count > 1)
                    data[o.Name] = string.Join("; ", walked.Values.Take(10));
            }
            catch { }
        }

        if (verbose && data.Count > 0)
            Console.WriteLine($"  [SNMP] {ip}: pass 2 got {data.Count} vendor-specific values");

        return data;
    }

    private static async Task<SnmpHostResources?> GetHostResourcesAsync(
        IPEndPoint endpoint, SnmpCredentials creds)
    {
        try
        {
            var hr = new SnmpHostResources();

            // CPU load — walk hrProcessorLoad, average across all CPUs
            var cpuLoads = await WalkOidAsync(endpoint, creds, Oids.HrProcessorLoad);
            if (cpuLoads.Count > 0)
            {
                var total = cpuLoads.Values.Sum(v => ParseInt(v));
                hr.CpuLoadPercent = total / cpuLoads.Count;
            }

            // Total memory (hrMemorySize returns KB)
            var memScalars = await GetScalarsAsync(endpoint, creds, new[] { Oids.HrMemorySize, Oids.HrSystemProcesses });
            if (memScalars != null)
            {
                if (memScalars.TryGetValue(Oids.HrMemorySize, out var memData))
                {
                    var memKb = ParseLong(memData.ToString());
                    if (memKb > 0) hr.MemoryTotalMb = memKb / 1024;
                }
                if (memScalars.TryGetValue(Oids.HrSystemProcesses, out var procData))
                    hr.ProcessCount = ParseInt(procData.ToString());
            }

            // Storage entries — walk all 4 columns, correlate by index
            var stTypes = await WalkOidAsync(endpoint, creds, Oids.HrStorageType);
            var stDescrs = await WalkOidAsync(endpoint, creds, Oids.HrStorageDescr);
            var stAllocUnits = await WalkOidAsync(endpoint, creds, Oids.HrStorageAllocUnit);
            var stSizes = await WalkOidAsync(endpoint, creds, Oids.HrStorageSize);
            var stUsed = await WalkOidAsync(endpoint, creds, Oids.HrStorageUsed);

            if (stTypes.Count > 0)
            {
                var storage = new List<SnmpStorageEntry>();
                foreach (var (idx, typeOid) in stTypes)
                {
                    var typeName = typeOid switch
                    {
                        var t when t.EndsWith(".2") => "ram",
                        var t when t.EndsWith(".3") => "virtualMemory",
                        var t when t.EndsWith(".4") => "fixedDisk",
                        var t when t.EndsWith(".5") => "removableDisk",
                        var t when t.EndsWith(".9") => "networkDisk",
                        _ => "other"
                    };

                    // Only keep ram and disk types
                    if (typeName is "other" or "virtualMemory") continue;

                    var allocUnit = ParseLong(stAllocUnits.GetValueOrDefault(idx));
                    var size = ParseLong(stSizes.GetValueOrDefault(idx));
                    var used = ParseLong(stUsed.GetValueOrDefault(idx));
                    if (allocUnit <= 0 || size <= 0) continue;

                    var totalMb = (size * allocUnit) / (1024 * 1024);
                    var usedMb = (used * allocUnit) / (1024 * 1024);

                    storage.Add(new SnmpStorageEntry
                    {
                        Description = stDescrs.GetValueOrDefault(idx) ?? $"storage-{idx}",
                        TotalMb = totalMb,
                        UsedMb = usedMb,
                        Type = typeName,
                    });

                    // Extract RAM used from storage (more accurate than hrMemorySize)
                    if (typeName == "ram" && totalMb > 0)
                    {
                        hr.MemoryTotalMb = totalMb;
                        hr.MemoryUsedMb = usedMb;
                    }
                }
                if (storage.Count > 0) hr.Storage = storage;
            }

            // Only return if we got at least one useful field
            if (hr.CpuLoadPercent == null && hr.MemoryTotalMb == null && hr.Storage == null && hr.ProcessCount == null)
                return null;

            return hr;
        }
        catch { return null; }
    }

    private static string ClassifyDeviceType(string? sysObjectId, string? sysDescr)
    {
        var oid = sysObjectId ?? "";
        var d = (sysDescr ?? "").ToLowerInvariant();

        // Printer
        if (d.Contains("printer") || d.Contains("laserjet") || d.Contains("officejet")
            || d.Contains("mfp") || d.Contains("imagerunner") || d.Contains("bizhub")
            || d.Contains("phaser") || d.Contains("colorqube") || d.Contains("workcentre")
            || d.Contains("brother") || d.Contains("epson") && d.Contains("print"))
            return "printer";

        // UPS
        if (oid.StartsWith("1.3.6.1.4.1.318.") // APC
            || oid.StartsWith("1.3.6.1.4.1.534.") // Eaton
            || oid.StartsWith("1.3.6.1.4.1.476.") // Liebert
            || d.Contains("ups") || d.Contains("uninterruptible"))
            return "ups";

        // Firewall / security appliance
        if (oid.StartsWith("1.3.6.1.4.1.12356.") // Fortinet
            || oid.StartsWith("1.3.6.1.4.1.8741.") // SonicWall
            || oid.StartsWith("1.3.6.1.4.1.3224.") // Juniper ScreenOS
            || d.Contains("fortigate") || d.Contains("sonicwall") || d.Contains("pfsense")
            || d.Contains("firewall") || d.Contains("palo alto") || d.Contains("sophos"))
            return "firewall";

        // Access point
        if (oid.StartsWith("1.3.6.1.4.1.14823.") // Aruba
            || d.Contains("access point") || d.Contains("unifi ap") || d.Contains("wireless ap"))
            return "access-point";

        // Router
        if (d.Contains("router") || d.Contains("routeros"))
            return "router";
        if (oid.StartsWith("1.3.6.1.4.1.14988.")) return "router"; // MikroTik

        // Switch
        if (d.Contains("switch") || d.Contains("procurve") || d.Contains("catalyst")
            || d.Contains("aruba") || d.Contains("managed ethernet") || d.Contains("powerconnect"))
            return "switch";

        // Ubiquiti (could be AP or switch — default AP)
        if (oid.StartsWith("1.3.6.1.4.1.41112.")) return "access-point";

        // NAS
        if (d.Contains("synology") || d.Contains("qnap") || d.Contains("readynas") || d.Contains("nas"))
            return "nas";

        // Server / host
        if (d.Contains("windows") || d.Contains("linux") || d.Contains("net-snmp") || d.Contains("vmware"))
            return "server";

        return "unknown";
    }

    private static async Task<List<PrinterSupply>?> GetPrinterSuppliesAsync(
        IPEndPoint endpoint, SnmpCredentials creds)
    {
        try
        {
            var descs = await WalkOidAsync(endpoint, creds, Oids.PrtMarkerSuppliesDesc);
            if (descs.Count == 0) return null;

            var types = await WalkOidAsync(endpoint, creds, Oids.PrtMarkerSuppliesType);
            var maxCaps = await WalkOidAsync(endpoint, creds, Oids.PrtMarkerSuppliesMax);
            var levels = await WalkOidAsync(endpoint, creds, Oids.PrtMarkerSuppliesLvl);
            var colorants = await WalkOidAsync(endpoint, creds, Oids.PrtMarkerColorant);

            var supplies = new List<PrinterSupply>();
            foreach (var (idx, desc) in descs)
            {
                var typeCode = ParseInt(types.GetValueOrDefault(idx));
                var maxCap = ParseInt(maxCaps.GetValueOrDefault(idx));
                var level = ParseInt(levels.GetValueOrDefault(idx));

                int? pct = null;
                if (level == -1) pct = 100;       // "ok" sentinel
                else if (level == -2) pct = 10;    // "warning"
                else if (level == -3) pct = 0;     // "critical/empty"
                else if (maxCap > 0 && level >= 0) pct = (int)((long)level * 100 / maxCap);

                supplies.Add(new PrinterSupply
                {
                    Description = desc,
                    SupplyType = typeCode switch
                    {
                        3 => "toner", 4 => "ink", 5 => "ink-cartridge",
                        7 => "drum", 12 => "fuser", 9 => "opc",
                        _ => "other"
                    },
                    Color = GuessColor(desc, colorants.GetValueOrDefault(idx)),
                    LevelPercent = pct,
                    MaxCapacity = maxCap > 0 ? maxCap : null,
                    CurrentLevel = level >= 0 ? level : null,
                });
            }
            return supplies.Count > 0 ? supplies : null;
        }
        catch { return null; }
    }

    private static async Task<long?> GetPrinterPageCountAsync(
        IPEndPoint endpoint, SnmpCredentials creds)
    {
        try
        {
            var counts = await WalkOidAsync(endpoint, creds, Oids.PrtMarkerLifeCount);
            if (counts.Count > 0)
            {
                var first = counts.Values.First();
                if (long.TryParse(first, out var pages) && pages > 0) return pages;
            }
        }
        catch { }
        return null;
    }

    private static string? GuessColor(string description, string? colorantValue)
    {
        var combined = ((colorantValue ?? "") + " " + description).ToLowerInvariant();
        if (combined.Contains("black") || combined.Contains("negro") || combined.Contains(" bk")) return "black";
        if (combined.Contains("cyan")) return "cyan";
        if (combined.Contains("magenta")) return "magenta";
        if (combined.Contains("yellow") || combined.Contains("amarillo")) return "yellow";
        if (combined.Contains("photo")) return "photo";
        return null;
    }

    private static string? GetString(Dictionary<string, ISnmpData> vars, string oid)
    {
        if (!vars.TryGetValue(oid, out var data)) return null;
        var str = data.TypeCode == SnmpType.OctetString ? FormatOctetString(data) : data.ToString();
        return string.IsNullOrWhiteSpace(str) ? null : str.Trim();
    }

    private static string? FormatOctetString(ISnmpData data)
    {
        if (data is OctetString os)
        {
            var bytes = os.GetRaw();
            if (bytes.Length == 6 && bytes.All(b => b < 128 == false || !char.IsLetterOrDigit((char)b)))
                return BitConverter.ToString(bytes).Replace("-", ":");
            return os.ToString();
        }
        return data.ToString();
    }

    private static int ParseInt(string? val) =>
        int.TryParse(val, out var i) ? i : 0;

    private static long ParseLong(string? val) =>
        long.TryParse(val, out var l) ? l : 0;
}

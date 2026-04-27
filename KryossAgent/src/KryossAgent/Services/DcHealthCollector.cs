using System.DirectoryServices;
using System.Management;
using KryossAgent.Models;

namespace KryossAgent.Services;

public static class DcHealthCollector
{
    private static readonly Dictionary<int, string> SchemaVersionMap = new()
    {
        [13] = "Windows 2000",
        [30] = "Windows Server 2003",
        [31] = "Windows Server 2003 R2",
        [44] = "Windows Server 2008",
        [47] = "Windows Server 2008 R2",
        [56] = "Windows Server 2012",
        [69] = "Windows Server 2012 R2",
        [87] = "Windows Server 2016",
        [88] = "Windows Server 2019",
        [90] = "Windows Server 2022",
        [91] = "Windows Server 2025",
    };

    public static DcHealthPayload? Collect(bool verbose)
    {
        DirectoryEntry rootDse;
        try
        {
            rootDse = new DirectoryEntry("LDAP://RootDSE");
            _ = rootDse.Properties["defaultNamingContext"]?.Value;
        }
        catch
        {
            return null; // not a DC or LDAP unavailable
        }

        using (rootDse)
        {
            var domainDn = rootDse.Properties["defaultNamingContext"]?.Value?.ToString();
            var configDn = rootDse.Properties["configurationNamingContext"]?.Value?.ToString();
            var schemaDn = rootDse.Properties["schemaNamingContext"]?.Value?.ToString();

            if (domainDn == null || configDn == null) return null;

            var payload = new DcHealthPayload
            {
                ScannedBy = Environment.MachineName,
                DomainName = DnToDomain(domainDn),
            };

            CollectSchema(payload, schemaDn, verbose);
            CollectFunctionalLevels(payload, rootDse, verbose);
            CollectFsmoRoles(payload, domainDn, configDn, verbose);
            CollectSites(payload, configDn, verbose);
            CollectReplication(payload, verbose);

            return payload;
        }
    }

    private static void CollectSchema(DcHealthPayload payload, string? schemaDn, bool verbose)
    {
        if (schemaDn == null) return;
        try
        {
            using var schemaEntry = new DirectoryEntry($"LDAP://{schemaDn}");
            var objVer = schemaEntry.Properties["objectVersion"]?.Value;
            if (objVer is int version)
            {
                payload.SchemaVersion = version;
                payload.SchemaVersionLabel = SchemaVersionMap.TryGetValue(version, out var label)
                    ? label : $"Unknown ({version})";
            }
        }
        catch (Exception ex)
        {
            if (verbose) Console.Error.WriteLine($"  [DC-HEALTH] Schema query failed: {ex.Message}");
        }
    }

    private static void CollectFunctionalLevels(DcHealthPayload payload, DirectoryEntry rootDse, bool verbose)
    {
        try
        {
            var forestLevel = rootDse.Properties["forestFunctionality"]?.Value?.ToString();
            var domainLevel = rootDse.Properties["domainFunctionality"]?.Value?.ToString();

            payload.ForestLevel = MapFunctionalLevel(forestLevel);
            payload.DomainLevel = MapFunctionalLevel(domainLevel);

            var forestDn = rootDse.Properties["rootDomainNamingContext"]?.Value?.ToString();
            if (forestDn != null)
                payload.ForestName = DnToDomain(forestDn);
        }
        catch (Exception ex)
        {
            if (verbose) Console.Error.WriteLine($"  [DC-HEALTH] Functional level query failed: {ex.Message}");
        }
    }

    private static void CollectFsmoRoles(DcHealthPayload payload, string domainDn, string configDn, bool verbose)
    {
        try
        {
            payload.SchemaMaster = GetFsmoHolder($"LDAP://CN=Schema,{configDn}");
            payload.DomainNamingMaster = GetFsmoHolder($"LDAP://CN=Partitions,{configDn}");
            payload.PdcEmulator = GetFsmoHolder($"LDAP://{domainDn}");
            payload.RidMaster = GetFsmoHolder($"LDAP://CN=RID Manager$,CN=System,{domainDn}");
            payload.InfrastructureMaster = GetFsmoHolder($"LDAP://CN=Infrastructure,{domainDn}");

            var holders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (payload.SchemaMaster != null) holders.Add(payload.SchemaMaster);
            if (payload.DomainNamingMaster != null) holders.Add(payload.DomainNamingMaster);
            if (payload.PdcEmulator != null) holders.Add(payload.PdcEmulator);
            if (payload.RidMaster != null) holders.Add(payload.RidMaster);
            if (payload.InfrastructureMaster != null) holders.Add(payload.InfrastructureMaster);
            payload.FsmoSinglePoint = holders.Count == 1 && holders.Count < 5;
        }
        catch (Exception ex)
        {
            if (verbose) Console.Error.WriteLine($"  [DC-HEALTH] FSMO query failed: {ex.Message}");
        }
    }

    private static void CollectSites(DcHealthPayload payload, string configDn, bool verbose)
    {
        try
        {
            using var sitesEntry = new DirectoryEntry($"LDAP://CN=Sites,{configDn}");
            using var searcher = new DirectorySearcher(sitesEntry, "(objectClass=site)", ["cn"]);
            payload.SiteCount = searcher.FindAll().Count;

            using var subnetsEntry = new DirectoryEntry($"LDAP://CN=Subnets,CN=Sites,{configDn}");
            using var subSearcher = new DirectorySearcher(subnetsEntry, "(objectClass=subnet)", ["cn"]);
            payload.SubnetCount = subSearcher.FindAll().Count;

            // Count DCs and GCs across all sites
            int dcCount = 0, gcCount = 0;
            using var dcSearcher = new DirectorySearcher(sitesEntry,
                "(objectClass=nTDSDSA)", ["options"], SearchScope.Subtree);
            foreach (SearchResult sr in dcSearcher.FindAll())
            {
                dcCount++;
                var options = sr.Properties["options"];
                if (options.Count > 0 && options[0] is int opt && (opt & 1) == 1)
                    gcCount++;
            }
            payload.DcCount = dcCount;
            payload.GcCount = gcCount;
        }
        catch (Exception ex)
        {
            if (verbose) Console.Error.WriteLine($"  [DC-HEALTH] Sites query failed: {ex.Message}");
        }
    }

    private static void CollectReplication(DcHealthPayload payload, bool verbose)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\MicrosoftActiveDirectory",
                "SELECT * FROM MSAD_ReplNeighbor");

            int partners = 0, failures = 0;
            DateTime? lastSuccess = null;

            foreach (ManagementObject obj in searcher.Get())
            {
                partners++;
                var numFailures = Convert.ToInt32(obj["NumConsecutiveSyncFailures"] ?? 0);
                if (numFailures > 0) failures++;

                var sourceServer = obj["SourceDsaCN"]?.ToString();
                var namingCtx = obj["NamingContextDN"]?.ToString();
                var lastResult = Convert.ToInt32(obj["LastSyncResult"] ?? -1);

                DateTime? attemptTime = null, successTime = null;
                var attemptStr = obj["TimeOfLastSyncAttempt"]?.ToString();
                var successStr = obj["TimeOfLastSyncSuccess"]?.ToString();
                if (attemptStr != null && ManagementDateTimeConverter.ToDateTime(attemptStr) is var at && at > DateTime.MinValue)
                    attemptTime = at.ToUniversalTime();
                if (successStr != null && ManagementDateTimeConverter.ToDateTime(successStr) is var st && st > DateTime.MinValue)
                {
                    successTime = st.ToUniversalTime();
                    if (lastSuccess == null || successTime > lastSuccess)
                        lastSuccess = successTime;
                }

                payload.ReplicationPartners.Add(new DcReplPartner
                {
                    PartnerHostname = sourceServer,
                    PartnerDn = obj["SourceDsaDN"]?.ToString(),
                    Direction = "inbound",
                    NamingContext = namingCtx,
                    LastSuccess = successTime,
                    LastAttempt = attemptTime,
                    FailureCount = numFailures,
                    LastError = lastResult != 0 ? $"LDAP error {lastResult}" : null,
                    Transport = obj["ReplicaTransportBridgeServerCN"] != null ? "SMTP" : "IP",
                });

                obj.Dispose();
            }

            payload.ReplPartnerCount = partners;
            payload.ReplFailureCount = failures;
            payload.LastSuccessfulRepl = lastSuccess;
        }
        catch (Exception ex)
        {
            if (verbose) Console.Error.WriteLine($"  [DC-HEALTH] Replication query failed: {ex.Message}");
        }
    }

    private static string? GetFsmoHolder(string ldapPath)
    {
        try
        {
            using var entry = new DirectoryEntry(ldapPath);
            var roleOwner = entry.Properties["fSMORoleOwner"]?.Value?.ToString();
            if (roleOwner == null) return null;
            return ExtractHostnameFromNtdsDn(roleOwner);
        }
        catch { return null; }
    }

    private static string ExtractHostnameFromNtdsDn(string ntdsDn)
    {
        // DN: CN=NTDS Settings,CN=DC1,CN=Servers,CN=SiteName,CN=Sites,...
        var parts = ntdsDn.Split(',');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("CN=", StringComparison.OrdinalIgnoreCase)
                && !trimmed.Equals("CN=NTDS Settings", StringComparison.OrdinalIgnoreCase)
                && !trimmed.Equals("CN=Servers", StringComparison.OrdinalIgnoreCase)
                && !trimmed.Equals("CN=Sites", StringComparison.OrdinalIgnoreCase)
                && !trimmed.StartsWith("CN=Configuration", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[3..]; // strip "CN="
            }
        }
        return ntdsDn;
    }

    private static string DnToDomain(string dn)
    {
        return string.Join(".", dn.Split(',')
            .Where(p => p.Trim().StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Trim()[3..]));
    }

    private static string MapFunctionalLevel(string? level)
    {
        return level switch
        {
            "0" => "Windows 2000",
            "1" => "Windows Server 2003 Interim",
            "2" => "Windows Server 2003",
            "3" => "Windows Server 2008",
            "4" => "Windows Server 2008 R2",
            "5" => "Windows Server 2012",
            "6" => "Windows Server 2012 R2",
            "7" => "Windows Server 2016",
            "10" => "Windows Server 2025",
            _ => level ?? "Unknown"
        };
    }
}

using System.DirectoryServices;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace KryossAgent.Services;

/// <summary>
/// Discovers Windows machines on the network via multiple methods:
/// explicit list, file, Active Directory, ARP table, or subnet TCP probe.
/// Used by the remote-scan feature to build a list of scan targets.
/// </summary>
public static class TargetDiscovery
{
    /// <summary>
    /// A machine discovered for remote scanning.
    /// </summary>
    public record ScanTarget(string Hostname, string Address, string Source);

    /// <summary>
    /// A stale/dormant AD object found during hygiene audit.
    /// </summary>
    public record AdHygieneItem(string Name, string Type, string Status, int DaysInactive, string? Detail);

    /// <summary>
    /// Results of the AD hygiene audit (stale machines + inactive users).
    /// </summary>
    public record AdHygieneReport(
        List<AdHygieneItem> StaleMachines,
        List<AdHygieneItem> DormantMachines,
        List<AdHygieneItem> StaleUsers,
        List<AdHygieneItem> DormantUsers,
        List<AdHygieneItem> DisabledUsers,
        List<AdHygieneItem> NeverExpirePasswords,
        // Security findings
        List<AdHygieneItem> PrivilegedAccounts,
        List<AdHygieneItem> KerberoastableAccounts,
        List<AdHygieneItem> UnconstrainedDelegation,
        List<AdHygieneItem> AdminCountResidual,
        List<AdHygieneItem> NoLaps,
        List<AdHygieneItem> DomainInfo
    );

    /// <summary>
    /// Discover scan targets based on CLI arguments. Merges and deduplicates
    /// results from all specified discovery methods. If no discovery flags are
    /// provided, tries AD first then falls back to ARP.
    /// </summary>
    public static async Task<List<ScanTarget>> DiscoverAsync(string[] args)
    {
        var targets = new List<ScanTarget>();
        bool anyDiscoveryFlag = false;

        // --targets host1,host2,host3
        var explicit_ = GetArg(args, "--targets");
        if (explicit_ is not null)
        {
            anyDiscoveryFlag = true;
            var hosts = explicit_.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var h in hosts)
                targets.Add(new ScanTarget(h, h, "explicit"));
            Console.WriteLine($"Explicit targets: {hosts.Length} hosts");
        }

        // --targets-file machines.txt
        var file = GetArg(args, "--targets-file");
        if (file is not null)
        {
            anyDiscoveryFlag = true;
            var lines = await File.ReadAllLinesAsync(file);
            int count = 0;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                    continue;
                targets.Add(new ScanTarget(trimmed, trimmed, "file"));
                count++;
            }
            Console.WriteLine($"File targets: {count} hosts from {file}");
        }

        // --discover-ad [OU]
        if (HasFlag(args, "--discover-ad"))
        {
            anyDiscoveryFlag = true;
            var ouPath = GetArg(args, "--discover-ad");
            var adTargets = DiscoverAd(ouPath);
            targets.AddRange(adTargets);
            Console.WriteLine($"AD discovery: {adTargets.Count} machines found");
        }

        // --discover-arp
        if (HasFlag(args, "--discover-arp"))
        {
            anyDiscoveryFlag = true;
            var arpTargets = await DiscoverArpAsync();
            targets.AddRange(arpTargets);
            Console.WriteLine($"ARP discovery: {arpTargets.Count} hosts found");
        }

        // --discover-subnet 192.168.1.0/24
        var subnet = GetArg(args, "--discover-subnet");
        if (subnet is not null)
        {
            anyDiscoveryFlag = true;
            var subnetTargets = await DiscoverSubnetAsync(subnet);
            targets.AddRange(subnetTargets);
            Console.WriteLine($"Subnet discovery: {subnetTargets.Count} hosts found");
        }

        // Default: try AD first, then ARP fallback
        if (!anyDiscoveryFlag && targets.Count == 0)
        {
            Console.WriteLine("No discovery flags specified, trying AD...");
            var adTargets = DiscoverAd(null);
            if (adTargets.Count > 0)
            {
                targets.AddRange(adTargets);
                Console.WriteLine($"AD discovery: {adTargets.Count} machines found");
            }
            else
            {
                Console.WriteLine("AD not available, falling back to ARP...");
                var arpTargets = await DiscoverArpAsync();
                targets.AddRange(arpTargets);
                Console.WriteLine($"ARP discovery: {arpTargets.Count} hosts found");
            }
        }

        // Dedup by short hostname (before first dot), case-insensitive
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deduped = new List<ScanTarget>();
        foreach (var t in targets)
        {
            var shortName = ShortHostname(t.Hostname);
            if (seen.Add(shortName))
                deduped.Add(t);
        }

        // Remove self
        var self = Environment.MachineName;
        deduped.RemoveAll(t => string.Equals(ShortHostname(t.Hostname), self, StringComparison.OrdinalIgnoreCase));

        return deduped;
    }

    // Store hygiene report for access after discovery
    public static AdHygieneReport? LastHygieneReport { get; private set; }

    /// <summary>
    /// Query Active Directory for Windows computer objects.
    /// Active machines (&lt;30 days) go to scan list.
    /// Stale (30-60 days) get cross-checked with ARP — if alive, scan them too.
    /// Dormant (&gt;60 days) are reported but not scanned.
    /// Also audits user accounts for hygiene report.
    /// </summary>
    private static List<ScanTarget> DiscoverAd(string? ouPath)
    {
        var active = new List<ScanTarget>();
        var staleMachines = new List<AdHygieneItem>();
        var dormantMachines = new List<AdHygieneItem>();

        try
        {
            var root = ouPath is not null
                ? new DirectoryEntry($"LDAP://{ouPath}")
                : new DirectoryEntry();

            using var searcher = new DirectorySearcher(root)
            {
                Filter = "(&(objectClass=computer)(operatingSystem=Windows*)(!(userAccountControl:1.2.840.113556.1.4.803:=2)))",
                PageSize = 1000,
            };
            searcher.PropertiesToLoad.AddRange(new[] { "name", "dNSHostName", "operatingSystem", "lastLogonTimestamp" });

            var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30).ToFileTimeUtc();
            var sixtyDaysAgo = DateTime.UtcNow.AddDays(-60).ToFileTimeUtc();

            using var found = searcher.FindAll();
            foreach (SearchResult sr in found)
            {
                var name = sr.Properties.Contains("name") && sr.Properties["name"].Count > 0
                    ? (string)sr.Properties["name"][0] : null;
                var dnsName = sr.Properties.Contains("dNSHostName") && sr.Properties["dNSHostName"].Count > 0
                    ? (string)sr.Properties["dNSHostName"][0] : null;
                var os = sr.Properties.Contains("operatingSystem") && sr.Properties["operatingSystem"].Count > 0
                    ? (string)sr.Properties["operatingSystem"][0] : "Unknown";

                if (name is null) continue;
                var address = dnsName ?? name;

                // Classify by last logon
                if (sr.Properties.Contains("lastLogonTimestamp") && sr.Properties["lastLogonTimestamp"].Count > 0)
                {
                    var lastLogon = (long)sr.Properties["lastLogonTimestamp"][0];
                    var lastLogonDate = DateTime.FromFileTimeUtc(lastLogon);
                    var daysInactive = (int)(DateTime.UtcNow - lastLogonDate).TotalDays;

                    if (lastLogon >= thirtyDaysAgo)
                    {
                        // Active — scan it
                        active.Add(new ScanTarget(name, address, "ad"));
                    }
                    else if (lastLogon >= sixtyDaysAgo)
                    {
                        // Stale (30-60 days) — will cross-check with ARP
                        staleMachines.Add(new AdHygieneItem(name, "Computer", "Stale", daysInactive,
                            $"{os} — last logon {lastLogonDate:yyyy-MM-dd}"));
                    }
                    else
                    {
                        // Dormant (>60 days) — candidate for removal
                        dormantMachines.Add(new AdHygieneItem(name, "Computer", "Dormant", daysInactive,
                            $"{os} — last logon {lastLogonDate:yyyy-MM-dd} — candidate for removal"));
                    }
                }
                else
                {
                    // Never logged on
                    dormantMachines.Add(new AdHygieneItem(name, "Computer", "Dormant", 9999,
                        $"{os} — never logged on — candidate for removal"));
                }
            }

            Console.WriteLine($"  AD machines: {active.Count} active, {staleMachines.Count} stale (30-60d), {dormantMachines.Count} dormant (>60d)");

            // Audit users
            var (staleUsers, dormantUsers, disabledUsers, neverExpire) = AuditAdUsers(root);

            // Audit security
            var (privileged, kerberoastable, unconstrained, adminCount, noLaps, domainInfo) = AuditAdSecurity(root);

            LastHygieneReport = new AdHygieneReport(
                staleMachines, dormantMachines,
                staleUsers, dormantUsers, disabledUsers, neverExpire,
                privileged, kerberoastable, unconstrained, adminCount, noLaps, domainInfo);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"AD discovery failed: {ex.Message}");
        }

        return active;
    }

    /// <summary>
    /// Cross-check stale machines (30-60 days without AD logon) against ARP table.
    /// If a stale machine responds on the network, add it to the scan targets.
    /// </summary>
    public static async Task<List<ScanTarget>> CrossCheckStaleWithArp()
    {
        var recovered = new List<ScanTarget>();
        if (LastHygieneReport is null || LastHygieneReport.StaleMachines.Count == 0)
            return recovered;

        Console.WriteLine($"  Cross-checking {LastHygieneReport.StaleMachines.Count} stale machines with ARP...");

        try
        {
            // Quick ARP grab without reverse DNS (fast)
            using var proc = new System.Diagnostics.Process();
            proc.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "arp",
                Arguments = "-a",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            proc.Start();
            var output = await proc.StandardOutput.ReadToEndAsync();
            proc.WaitForExit(5000);

            // Collect all IPs from ARP
            var arpIps = new HashSet<string>();
            foreach (var line in output.Split('\n'))
            {
                var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 && System.Net.IPAddress.TryParse(parts[0], out _))
                    arpIps.Add(parts[0]);
            }

            // Try to resolve each stale machine's name to an IP and check ARP
            foreach (var stale in LastHygieneReport.StaleMachines)
            {
                try
                {
                    var entry = await Dns.GetHostEntryAsync(stale.Name);
                    var ip = entry.AddressList.FirstOrDefault()?.ToString();
                    if (ip is not null && arpIps.Contains(ip))
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"    {stale.Name} — stale ({stale.DaysInactive}d) but alive on network → adding to scan");
                        Console.ResetColor();
                        recovered.Add(new ScanTarget(stale.Name, stale.Name, "stale+arp"));
                    }
                }
                catch { /* DNS failed, skip */ }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"    ARP cross-check failed: {ex.Message}");
        }

        if (recovered.Count == 0)
            Console.WriteLine("    No stale machines found on network");

        return recovered;
    }

    /// <summary>
    /// Audit AD user accounts for inactive/stale/disabled/password-never-expires.
    /// </summary>
    private static (List<AdHygieneItem> Stale, List<AdHygieneItem> Dormant,
        List<AdHygieneItem> Disabled, List<AdHygieneItem> NeverExpire) AuditAdUsers(DirectoryEntry root)
    {
        var stale = new List<AdHygieneItem>();
        var dormant = new List<AdHygieneItem>();
        var disabled = new List<AdHygieneItem>();
        var neverExpire = new List<AdHygieneItem>();

        try
        {
            using var searcher = new DirectorySearcher(root)
            {
                Filter = "(&(objectClass=user)(objectCategory=person))",
                PageSize = 1000,
            };
            searcher.PropertiesToLoad.AddRange(new[]
            {
                "sAMAccountName", "displayName", "lastLogonTimestamp",
                "pwdLastSet", "userAccountControl"
            });

            var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30).ToFileTimeUtc();
            var sixtyDaysAgo = DateTime.UtcNow.AddDays(-60).ToFileTimeUtc();
            var ninetyDaysAgo = DateTime.UtcNow.AddDays(-90).ToFileTimeUtc();

            using var found = searcher.FindAll();
            foreach (SearchResult sr in found)
            {
                var samName = sr.Properties.Contains("sAMAccountName") && sr.Properties["sAMAccountName"].Count > 0
                    ? (string)sr.Properties["sAMAccountName"][0] : null;
                var displayName = sr.Properties.Contains("displayName") && sr.Properties["displayName"].Count > 0
                    ? (string)sr.Properties["displayName"][0] : samName;

                if (samName is null) continue;

                // Skip built-in accounts
                if (samName.EndsWith("$") || samName.Equals("Administrator", StringComparison.OrdinalIgnoreCase)
                    || samName.Equals("Guest", StringComparison.OrdinalIgnoreCase)
                    || samName.Equals("krbtgt", StringComparison.OrdinalIgnoreCase))
                    continue;

                var uac = sr.Properties.Contains("userAccountControl") && sr.Properties["userAccountControl"].Count > 0
                    ? (int)sr.Properties["userAccountControl"][0] : 0;
                var isDisabled = (uac & 0x2) != 0;
                var pwdNeverExpires = (uac & 0x10000) != 0;

                // Disabled accounts still in AD
                if (isDisabled)
                {
                    disabled.Add(new AdHygieneItem(samName, "User", "Disabled", 0,
                        $"{displayName} — account disabled but still in AD"));
                    continue;
                }

                // Password never expires
                if (pwdNeverExpires)
                {
                    neverExpire.Add(new AdHygieneItem(samName, "User", "PwdNeverExpires", 0,
                        $"{displayName} — password set to never expire"));
                }

                // Last logon check
                if (sr.Properties.Contains("lastLogonTimestamp") && sr.Properties["lastLogonTimestamp"].Count > 0)
                {
                    var lastLogon = (long)sr.Properties["lastLogonTimestamp"][0];
                    var lastLogonDate = DateTime.FromFileTimeUtc(lastLogon);
                    var daysInactive = (int)(DateTime.UtcNow - lastLogonDate).TotalDays;

                    if (lastLogon < sixtyDaysAgo)
                    {
                        dormant.Add(new AdHygieneItem(samName, "User", "Dormant", daysInactive,
                            $"{displayName} — last logon {lastLogonDate:yyyy-MM-dd} — candidate for removal"));
                    }
                    else if (lastLogon < thirtyDaysAgo)
                    {
                        stale.Add(new AdHygieneItem(samName, "User", "Stale", daysInactive,
                            $"{displayName} — last logon {lastLogonDate:yyyy-MM-dd}"));
                    }
                }

                // Password age check
                if (sr.Properties.Contains("pwdLastSet") && sr.Properties["pwdLastSet"].Count > 0)
                {
                    var pwdLastSet = (long)sr.Properties["pwdLastSet"][0];
                    if (pwdLastSet > 0 && pwdLastSet < ninetyDaysAgo && !pwdNeverExpires)
                    {
                        var pwdDate = DateTime.FromFileTimeUtc(pwdLastSet);
                        var pwdAge = (int)(DateTime.UtcNow - pwdDate).TotalDays;
                        stale.Add(new AdHygieneItem(samName, "User", "OldPassword", pwdAge,
                            $"{displayName} — password {pwdAge} days old (last set {pwdDate:yyyy-MM-dd})"));
                    }
                }
            }

            Console.WriteLine($"  AD users: {stale.Count} stale, {dormant.Count} dormant, {disabled.Count} disabled, {neverExpire.Count} pwd-never-expires");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  AD user audit failed: {ex.Message}");
        }

        return (stale, dormant, disabled, neverExpire);
    }

    /// <summary>
    /// Audit AD security: privileged accounts, kerberoastable SPNs,
    /// unconstrained delegation, adminCount residual, LAPS coverage, domain level.
    /// </summary>
    private static (List<AdHygieneItem> Privileged, List<AdHygieneItem> Kerberoastable,
        List<AdHygieneItem> Unconstrained, List<AdHygieneItem> AdminCount,
        List<AdHygieneItem> NoLaps, List<AdHygieneItem> DomainInfo) AuditAdSecurity(DirectoryEntry root)
    {
        var privileged = new List<AdHygieneItem>();
        var kerberoastable = new List<AdHygieneItem>();
        var unconstrained = new List<AdHygieneItem>();
        var adminCount = new List<AdHygieneItem>();
        var noLaps = new List<AdHygieneItem>();
        var domainInfo = new List<AdHygieneItem>();

        try
        {
            // ── 1. Privileged group members (Domain Admins, Enterprise Admins, Schema Admins, Administrators) ──
            string[] privilegedGroups = ["Domain Admins", "Enterprise Admins", "Schema Admins", "Administrators"];
            foreach (var groupName in privilegedGroups)
            {
                try
                {
                    using var groupSearcher = new DirectorySearcher(root)
                    {
                        Filter = $"(&(objectClass=group)(cn={groupName}))",
                        PageSize = 10,
                    };
                    groupSearcher.PropertiesToLoad.Add("member");
                    var groupResult = groupSearcher.FindOne();
                    if (groupResult?.Properties.Contains("member") == true)
                    {
                        foreach (var memberDn in groupResult.Properties["member"])
                        {
                            var memberName = memberDn?.ToString()?.Split(',')[0]?.Replace("CN=", "") ?? "Unknown";
                            // Skip built-in Administrator
                            if (memberName.Equals("Administrator", StringComparison.OrdinalIgnoreCase)) continue;
                            privileged.Add(new AdHygieneItem(memberName, "Security", "PrivilegedAccount", 0,
                                $"Member of {groupName}"));
                        }
                    }
                }
                catch { /* group might not exist */ }
            }

            // ── 2. Kerberoastable accounts (users with SPN set) ──
            using var spnSearcher = new DirectorySearcher(root)
            {
                Filter = "(&(objectClass=user)(objectCategory=person)(servicePrincipalName=*)(!(userAccountControl:1.2.840.113556.1.4.803:=2)))",
                PageSize = 1000,
            };
            spnSearcher.PropertiesToLoad.AddRange(new[] { "sAMAccountName", "displayName", "servicePrincipalName" });

            using var spnResults = spnSearcher.FindAll();
            foreach (SearchResult sr in spnResults)
            {
                var sam = sr.Properties.Contains("sAMAccountName") ? sr.Properties["sAMAccountName"][0]?.ToString() : null;
                var display = sr.Properties.Contains("displayName") ? sr.Properties["displayName"][0]?.ToString() : sam;
                if (sam is null || sam.EndsWith("$")) continue; // Skip machine accounts
                var spn = sr.Properties.Contains("servicePrincipalName") ? sr.Properties["servicePrincipalName"][0]?.ToString() : "";
                kerberoastable.Add(new AdHygieneItem(sam!, "Security", "Kerberoastable", 0,
                    $"{display} — SPN: {spn} (vulnerable to Kerberoasting)"));
            }

            // ── 3. Unconstrained delegation ──
            // UAC flag 0x80000 = TRUSTED_FOR_DELEGATION (unconstrained)
            using var delegSearcher = new DirectorySearcher(root)
            {
                Filter = "(&(objectCategory=computer)(userAccountControl:1.2.840.113556.1.4.803:=524288))",
                PageSize = 1000,
            };
            delegSearcher.PropertiesToLoad.AddRange(new[] { "name", "operatingSystem" });

            using var delegResults = delegSearcher.FindAll();
            foreach (SearchResult sr in delegResults)
            {
                var name = sr.Properties.Contains("name") ? sr.Properties["name"][0]?.ToString() : "Unknown";
                var os = sr.Properties.Contains("operatingSystem") ? sr.Properties["operatingSystem"][0]?.ToString() : "";
                // Skip domain controllers (they naturally have this flag)
                if (os?.Contains("Domain Controller", StringComparison.OrdinalIgnoreCase) == true) continue;
                unconstrained.Add(new AdHygieneItem(name!, "Security", "UnconstrainedDelegation", 0,
                    $"{os} — trusted for unconstrained delegation (high risk)"));
            }

            // ── 4. AdminCount residual (users with adminCount=1 who aren't current admins) ──
            using var acSearcher = new DirectorySearcher(root)
            {
                Filter = "(&(objectClass=user)(objectCategory=person)(adminCount=1)(!(userAccountControl:1.2.840.113556.1.4.803:=2)))",
                PageSize = 1000,
            };
            acSearcher.PropertiesToLoad.AddRange(new[] { "sAMAccountName", "displayName" });

            var currentAdmins = new HashSet<string>(privileged.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

            using var acResults = acSearcher.FindAll();
            foreach (SearchResult sr in acResults)
            {
                var sam = sr.Properties.Contains("sAMAccountName") ? sr.Properties["sAMAccountName"][0]?.ToString() : null;
                var display = sr.Properties.Contains("displayName") ? sr.Properties["displayName"][0]?.ToString() : sam;
                if (sam is null || sam.Equals("Administrator", StringComparison.OrdinalIgnoreCase)) continue;
                if (currentAdmins.Contains(sam)) continue; // Skip actual current admins
                adminCount.Add(new AdHygieneItem(sam, "Security", "AdminCountResidue", 0,
                    $"{display} — adminCount=1 but not in privileged groups (residual permissions)"));
            }

            // ── 5. LAPS coverage (machines without ms-Mcs-AdmPwd attribute) ──
            using var lapsSearcher = new DirectorySearcher(root)
            {
                Filter = "(&(objectClass=computer)(operatingSystem=Windows*)(!(userAccountControl:1.2.840.113556.1.4.803:=2)))",
                PageSize = 1000,
            };
            lapsSearcher.PropertiesToLoad.AddRange(new[] { "name", "ms-Mcs-AdmPwd", "ms-Mcs-AdmPwdExpirationTime" });

            int lapsTotal = 0, lapsManaged = 0;
            using var lapsResults = lapsSearcher.FindAll();
            foreach (SearchResult sr in lapsResults)
            {
                lapsTotal++;
                var hasLaps = sr.Properties.Contains("ms-Mcs-AdmPwd") && sr.Properties["ms-Mcs-AdmPwd"].Count > 0;
                if (!hasLaps)
                {
                    var name = sr.Properties.Contains("name") ? sr.Properties["name"][0]?.ToString() : "Unknown";
                    noLaps.Add(new AdHygieneItem(name!, "Security", "NoLAPS", 0,
                        "No LAPS password managed — local admin password may be shared/static"));
                }
                else
                {
                    lapsManaged++;
                }
            }

            // ── 6. Domain functional level ──
            try
            {
                var domainLevel = root.Properties.Contains("msDS-Behavior-Version")
                    ? root.Properties["msDS-Behavior-Version"][0]?.ToString() : null;
                var levelName = domainLevel switch
                {
                    "0" => "Windows 2000",
                    "1" => "Windows Server 2003 Interim",
                    "2" => "Windows Server 2003",
                    "3" => "Windows Server 2008",
                    "4" => "Windows Server 2008 R2",
                    "5" => "Windows Server 2012",
                    "6" => "Windows Server 2012 R2",
                    "7" => "Windows Server 2016",
                    _ => $"Unknown ({domainLevel})"
                };
                var isOld = int.TryParse(domainLevel, out var level) && level < 7;
                domainInfo.Add(new AdHygieneItem("DomainLevel", "Config", isOld ? "OutdatedDomainLevel" : "DomainLevel", 0,
                    $"Domain functional level: {levelName}{(isOld ? " — consider upgrading to 2016+" : "")}"));

                if (lapsTotal > 0)
                {
                    var pct = lapsManaged * 100 / lapsTotal;
                    domainInfo.Add(new AdHygieneItem("LAPS", "Config",
                        pct < 80 ? "LowLAPSCoverage" : "LAPSCoverage", 0,
                        $"LAPS coverage: {lapsManaged}/{lapsTotal} machines ({pct}%)"));
                }
            }
            catch { /* domain info not accessible */ }

            Console.WriteLine($"  AD security: {privileged.Count} privileged, {kerberoastable.Count} kerberoastable, {unconstrained.Count} unconstrained deleg, {noLaps.Count} no-LAPS");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  AD security audit failed: {ex.Message}");
        }

        return (privileged, kerberoastable, unconstrained, adminCount, noLaps, domainInfo);
    }

    /// <summary>
    /// Parse the ARP table and attempt reverse DNS on each entry.
    /// Skips broadcast addresses, gateways, and broadcast MACs.
    /// </summary>
    private static async Task<List<ScanTarget>> DiscoverArpAsync()
    {
        var results = new List<ScanTarget>();
        try
        {
            using var proc = new System.Diagnostics.Process();
            proc.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "arp",
                Arguments = "-a",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            proc.Start();
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            // Parse lines like: "  192.168.1.10    00-aa-bb-cc-dd-ee     dynamic"
            var lineRegex = new Regex(@"^\s+(\d+\.\d+\.\d+\.\d+)\s+([\w-]+)\s+", RegexOptions.Multiline);
            foreach (Match m in lineRegex.Matches(output))
            {
                var ip = m.Groups[1].Value;
                var mac = m.Groups[2].Value.ToLowerInvariant();

                // Skip broadcast MAC
                if (mac == "ff-ff-ff-ff-ff-ff")
                    continue;

                // Skip broadcast (.255) and typical gateway (.1)
                var lastOctet = ip.Split('.').LastOrDefault();
                if (lastOctet is "255" or "1")
                    continue;

                // Reverse DNS best-effort
                string hostname = ip;
                try
                {
                    var entry = await Dns.GetHostEntryAsync(ip);
                    if (!string.IsNullOrWhiteSpace(entry.HostName))
                        hostname = entry.HostName;
                }
                catch
                {
                    // Reverse DNS failed — use IP as hostname
                }

                results.Add(new ScanTarget(hostname, ip, "arp"));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ARP discovery failed: {ex.Message}");
        }

        return results;
    }

    /// <summary>
    /// Probe a CIDR subnet for hosts with TCP port 445 (SMB) open.
    /// Hosts that respond are likely Windows machines with file sharing enabled.
    /// </summary>
    private static async Task<List<ScanTarget>> DiscoverSubnetAsync(string cidr)
    {
        var results = new List<ScanTarget>();
        try
        {
            var (baseIp, hostCount) = ParseCidr(cidr);
            Console.WriteLine($"Probing {hostCount} addresses on {cidr} (TCP 445)...");

            var tasks = new List<Task<ScanTarget?>>();
            for (int i = 1; i < hostCount - 1; i++) // skip network (.0) and broadcast
            {
                var ipBytes = baseIp.GetAddressBytes();
                // Add offset to last octet(s)
                var ipLong = ((long)ipBytes[0] << 24) | ((long)ipBytes[1] << 16) | ((long)ipBytes[2] << 8) | ipBytes[3];
                ipLong += i;
                var targetIp = new IPAddress(new byte[]
                {
                    (byte)((ipLong >> 24) & 0xFF),
                    (byte)((ipLong >> 16) & 0xFF),
                    (byte)((ipLong >> 8) & 0xFF),
                    (byte)(ipLong & 0xFF),
                });

                var ip = targetIp.ToString();
                tasks.Add(ProbeSmbAsync(ip));
            }

            var probeResults = await Task.WhenAll(tasks);
            foreach (var t in probeResults)
            {
                if (t is not null)
                    results.Add(t);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Subnet discovery failed: {ex.Message}");
        }

        return results;
    }

    /// <summary>
    /// Try to connect to TCP 445 on a host with a 2-second timeout.
    /// Returns a ScanTarget if the port is open, null otherwise.
    /// </summary>
    private static async Task<ScanTarget?> ProbeSmbAsync(string ip)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await client.ConnectAsync(IPAddress.Parse(ip), 445, cts.Token);

            // Reverse DNS best-effort
            string hostname = ip;
            try
            {
                var entry = await Dns.GetHostEntryAsync(ip);
                if (!string.IsNullOrWhiteSpace(entry.HostName))
                    hostname = entry.HostName;
            }
            catch { /* use IP */ }

            return new ScanTarget(hostname, ip, "subnet");
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parse a CIDR notation string (e.g. "192.168.1.0/24") into the base
    /// network address and the total number of host addresses.
    /// </summary>
    private static (IPAddress BaseIp, int HostCount) ParseCidr(string cidr)
    {
        var parts = cidr.Split('/');
        var ip = IPAddress.Parse(parts[0]);
        var prefix = int.Parse(parts[1]);
        int hostBits = 32 - prefix;
        int hostCount = 1 << hostBits; // 2^hostBits
        return (ip, hostCount);
    }

    /// <summary>
    /// Get the short hostname (before the first dot), for dedup purposes.
    /// </summary>
    private static string ShortHostname(string hostname)
    {
        var dot = hostname.IndexOf('.');
        return dot >= 0 ? hostname[..dot] : hostname;
    }

    /// <summary>
    /// Get the value of a named CLI argument (e.g. --targets host1,host2 returns "host1,host2").
    /// Returns null if the argument is not present or has no value.
    /// </summary>
    private static string? GetArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                var next = args[i + 1];
                // Don't return the next arg if it looks like another flag
                if (!next.StartsWith("--"))
                    return next;
            }
        }
        return null;
    }

    /// <summary>
    /// Check if a flag is present in the CLI arguments. For flags that can
    /// optionally take a value (like --discover-ad [OU]), this just checks
    /// presence regardless of whether a value follows.
    /// </summary>
    private static bool HasFlag(string[] args, string name)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}

using System.DirectoryServices;
using System.Management;
using System.Text.Json;
using KryossAgent.Models;
using Microsoft.Win32;

namespace KryossAgent.Engines;

/// <summary>
/// Engine for Domain Controller security checks. Uses System.DirectoryServices
/// (LDAP), WMI, registry, and EventLog — zero Process.Start.
///
/// Controls with type="dc" are dispatched here. The CheckType field determines
/// which native handler runs.
/// </summary>
public class DcEngine : ICheckEngine
{
    public string Type => "dc";

    private DirectoryEntry? _rootDse;
    private DirectoryEntry? _domainRoot;
    private string? _domainDn;
    private string? _configDn;
    private bool _isDc;
    private bool _initialized;

    public List<CheckResult> Execute(IReadOnlyList<ControlDef> controls)
    {
        var results = new List<CheckResult>();
        EnsureInitialized();

        foreach (var control in controls)
        {
            try
            {
                var result = _isDc ? RunCheck(control) : SkipNotDc(control);
                results.Add(result);
            }
            catch (Exception ex)
            {
                results.Add(new CheckResult
                {
                    Id = control.Id,
                    Exists = false,
                    Stderr = $"DC check failed: {ex.Message}"
                });
            }
        }

        return results;
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            _rootDse = new DirectoryEntry("LDAP://RootDSE");
            _domainDn = _rootDse.Properties["defaultNamingContext"]?.Value?.ToString();
            _configDn = _rootDse.Properties["configurationNamingContext"]?.Value?.ToString();
            if (_domainDn != null)
            {
                _domainRoot = new DirectoryEntry($"LDAP://{_domainDn}");
                _isDc = true;
            }
        }
        catch
        {
            _isDc = false;
        }
    }

    private CheckResult RunCheck(ControlDef control)
    {
        var checkType = control.CheckType?.Trim().ToLowerInvariant() ?? "";
        return checkType switch
        {
            "dc_krbtgt_age" => CheckKrbtgtAge(control),
            "dc_asrep_roastable" => CheckAsrepRoastable(control),
            "dc_protected_users" => CheckProtectedUsers(control),
            "dc_recycle_bin" => CheckRecycleBin(control),
            "dc_schema_admins" => CheckSchemaAdmins(control),
            "dc_preauth_disabled" => CheckPreauthDisabled(control),
            "dc_unconstrained_deleg" => CheckUnconstrainedDelegation(control),
            "dc_kerberoastable" => CheckKerberoastable(control),
            "dc_stale_computers" => CheckStaleComputers(control),
            "dc_pwd_never_expire" => CheckPwdNeverExpire(control),
            "dc_inactive_admins" => CheckInactiveAdmins(control),
            "dc_domain_level" => CheckDomainFunctionalLevel(control),
            "dc_laps_coverage" => CheckLapsCoverage(control),
            "dc_admin_count_orphan" => CheckAdminCountOrphan(control),
            "dc_gpo_count" => CheckGpoCount(control),
            "dc_tombstone_lifetime" => CheckTombstoneLifetime(control),
            "dc_dsrm_password_set" => CheckDsrmPasswordSet(control),
            "dc_ntds_service" => CheckNtdsService(control),
            "dc_replication_queue" => CheckReplicationQueue(control),
            "dc_time_source" => CheckTimeSource(control),
            "dc_dns_forwarders" => CheckDnsForwarders(control),
            "dc_print_spooler" => CheckPrintSpooler(control),
            "dc_smb_signing" => CheckSmbSigning(control),
            "dc_ldap_signing" => CheckLdapSigning(control),
            "dc_secure_channel" => CheckSecureChannel(control),
            "dc_ntlm_restrict" => CheckNtlmRestrict(control),
            "dc_crl_validity" => CheckCrlValidity(control),
            _ => new CheckResult { Id = control.Id, Exists = false, Stderr = $"Unknown DC CheckType: {checkType}" }
        };
    }

    private static CheckResult SkipNotDc(ControlDef control) => new()
    {
        Id = control.Id,
        Exists = false,
        Stderr = "Machine is not a Domain Controller"
    };

    // ── DC checks using DirectoryServices ──

    private CheckResult CheckKrbtgtAge(ControlDef c)
    {
        using var searcher = new DirectorySearcher(_domainRoot!)
        {
            Filter = "(sAMAccountName=krbtgt)",
            PropertiesToLoad = { "pwdLastSet" }
        };
        var result = searcher.FindOne();
        if (result == null) return new CheckResult { Id = c.Id, Exists = false, Stderr = "krbtgt not found" };

        var pwdLastSet = (long)(result.Properties["pwdLastSet"][0] ?? 0);
        var lastSetDate = DateTime.FromFileTimeUtc(pwdLastSet);
        var ageDays = (int)(DateTime.UtcNow - lastSetDate).TotalDays;

        return new CheckResult
        {
            Id = c.Id,
            Exists = true,
            Value = ageDays,
            Stdout = $"krbtgt password age: {ageDays} days (last set: {lastSetDate:yyyy-MM-dd})"
        };
    }

    private CheckResult CheckAsrepRoastable(ControlDef c)
    {
        // UserAccountControl bit 0x400000 = DONT_REQUIRE_PREAUTH
        using var searcher = new DirectorySearcher(_domainRoot!)
        {
            Filter = "(&(objectCategory=person)(objectClass=user)(userAccountControl:1.2.840.113556.1.4.803:=4194304)(!(userAccountControl:1.2.840.113556.1.4.803:=2)))",
            PropertiesToLoad = { "sAMAccountName" },
            SizeLimit = 100
        };
        var results = searcher.FindAll();
        var count = results.Count;

        return new CheckResult
        {
            Id = c.Id,
            Exists = true,
            Value = count,
            Stdout = $"{count} enabled accounts with pre-auth disabled"
        };
    }

    private CheckResult CheckProtectedUsers(ControlDef c)
    {
        using var searcher = new DirectorySearcher(_domainRoot!)
        {
            Filter = "(&(objectClass=group)(cn=Protected Users))",
            PropertiesToLoad = { "member" }
        };
        var result = searcher.FindOne();
        var memberCount = result?.Properties["member"]?.Count ?? 0;

        return new CheckResult
        {
            Id = c.Id,
            Exists = true,
            Value = memberCount,
            Stdout = $"Protected Users group has {memberCount} members"
        };
    }

    private CheckResult CheckRecycleBin(ControlDef c)
    {
        try
        {
            using var configEntry = new DirectoryEntry($"LDAP://CN=Recycle Bin Feature,CN=Optional Features,CN=Directory Service,CN=Windows NT,CN=Services,{_configDn}");
            var enabledScopes = configEntry.Properties["msDS-EnabledFeatureBL"];
            var enabled = enabledScopes != null && enabledScopes.Count > 0;
            return new CheckResult
            {
                Id = c.Id,
                Exists = true,
                Value = enabled,
                Stdout = enabled ? "AD Recycle Bin is enabled" : "AD Recycle Bin is NOT enabled"
            };
        }
        catch
        {
            return new CheckResult { Id = c.Id, Exists = true, Value = false, Stdout = "AD Recycle Bin is NOT enabled" };
        }
    }

    private CheckResult CheckSchemaAdmins(ControlDef c)
    {
        using var searcher = new DirectorySearcher(_domainRoot!)
        {
            Filter = "(&(objectClass=group)(cn=Schema Admins))",
            PropertiesToLoad = { "member" }
        };
        var result = searcher.FindOne();
        var count = result?.Properties["member"]?.Count ?? 0;

        return new CheckResult
        {
            Id = c.Id,
            Exists = true,
            Value = count,
            Stdout = $"Schema Admins has {count} members"
        };
    }

    private CheckResult CheckPreauthDisabled(ControlDef c) => CheckAsrepRoastable(c);

    private CheckResult CheckUnconstrainedDelegation(ControlDef c)
    {
        // UAC bit 0x80000 = TRUSTED_FOR_DELEGATION, exclude DCs
        using var searcher = new DirectorySearcher(_domainRoot!)
        {
            Filter = "(&(objectCategory=computer)(userAccountControl:1.2.840.113556.1.4.803:=524288)(!(primaryGroupID=516)))",
            PropertiesToLoad = { "cn" },
            SizeLimit = 100
        };
        var results = searcher.FindAll();
        var count = results.Count;

        return new CheckResult
        {
            Id = c.Id,
            Exists = true,
            Value = count,
            Stdout = $"{count} non-DC computers with unconstrained delegation"
        };
    }

    private CheckResult CheckKerberoastable(ControlDef c)
    {
        using var searcher = new DirectorySearcher(_domainRoot!)
        {
            Filter = "(&(objectCategory=person)(objectClass=user)(servicePrincipalName=*)(!(userAccountControl:1.2.840.113556.1.4.803:=2))(!(sAMAccountName=krbtgt)))",
            PropertiesToLoad = { "sAMAccountName" },
            SizeLimit = 200
        };
        var results = searcher.FindAll();
        var count = results.Count;

        return new CheckResult
        {
            Id = c.Id,
            Exists = true,
            Value = count,
            Stdout = $"{count} kerberoastable accounts (enabled users with SPNs)"
        };
    }

    private CheckResult CheckStaleComputers(ControlDef c)
    {
        var cutoff = DateTime.UtcNow.AddDays(-90);
        var cutoffFileTime = cutoff.ToFileTimeUtc();
        using var searcher = new DirectorySearcher(_domainRoot!)
        {
            Filter = $"(&(objectCategory=computer)(lastLogonTimestamp<={cutoffFileTime}))",
            PropertiesToLoad = { "cn" },
            SizeLimit = 500
        };
        var results = searcher.FindAll();
        var count = results.Count;

        return new CheckResult
        {
            Id = c.Id,
            Exists = true,
            Value = count,
            Stdout = $"{count} computer accounts with no logon in 90+ days"
        };
    }

    private CheckResult CheckPwdNeverExpire(ControlDef c)
    {
        // UAC bit 0x10000 = DONT_EXPIRE_PASSWORD
        using var searcher = new DirectorySearcher(_domainRoot!)
        {
            Filter = "(&(objectCategory=person)(objectClass=user)(userAccountControl:1.2.840.113556.1.4.803:=65536)(!(userAccountControl:1.2.840.113556.1.4.803:=2)))",
            PropertiesToLoad = { "sAMAccountName" },
            SizeLimit = 500
        };
        var results = searcher.FindAll();
        var count = results.Count;

        return new CheckResult
        {
            Id = c.Id,
            Exists = true,
            Value = count,
            Stdout = $"{count} enabled accounts with password never expires"
        };
    }

    private CheckResult CheckInactiveAdmins(ControlDef c)
    {
        var cutoff = DateTime.UtcNow.AddDays(-60);
        var cutoffFileTime = cutoff.ToFileTimeUtc();
        using var searcher = new DirectorySearcher(_domainRoot!)
        {
            Filter = $"(&(objectCategory=person)(objectClass=user)(adminCount=1)(lastLogonTimestamp<={cutoffFileTime})(!(userAccountControl:1.2.840.113556.1.4.803:=2)))",
            PropertiesToLoad = { "sAMAccountName" },
            SizeLimit = 100
        };
        var results = searcher.FindAll();
        var count = results.Count;

        return new CheckResult
        {
            Id = c.Id,
            Exists = true,
            Value = count,
            Stdout = $"{count} admin accounts with no logon in 60+ days"
        };
    }

    private CheckResult CheckDomainFunctionalLevel(ControlDef c)
    {
        try
        {
            var level = _rootDse!.Properties["domainFunctionality"]?.Value?.ToString() ?? "0";
            var levelInt = int.Parse(level);
            // 7 = 2016, 6 = 2012R2, 5 = 2012, 4 = 2008R2, 3 = 2008
            var levelName = levelInt switch
            {
                7 => "Windows Server 2016",
                6 => "Windows Server 2012 R2",
                5 => "Windows Server 2012",
                4 => "Windows Server 2008 R2",
                3 => "Windows Server 2008",
                _ => $"Level {levelInt}"
            };

            return new CheckResult
            {
                Id = c.Id,
                Exists = true,
                Value = levelInt,
                Stdout = $"Domain functional level: {levelName} ({levelInt})"
            };
        }
        catch (Exception ex)
        {
            return new CheckResult { Id = c.Id, Exists = false, Stderr = ex.Message };
        }
    }

    private CheckResult CheckLapsCoverage(ControlDef c)
    {
        // Count computers with ms-Mcs-AdmPwdExpirationTime set (LAPS managed)
        using var allSearcher = new DirectorySearcher(_domainRoot!)
        {
            Filter = "(&(objectCategory=computer)(!(userAccountControl:1.2.840.113556.1.4.803:=2)))",
            PropertiesToLoad = { "cn" },
            SizeLimit = 1000
        };
        var total = allSearcher.FindAll().Count;

        using var lapsSearcher = new DirectorySearcher(_domainRoot!)
        {
            Filter = "(&(objectCategory=computer)(ms-Mcs-AdmPwdExpirationTime=*)(!(userAccountControl:1.2.840.113556.1.4.803:=2)))",
            PropertiesToLoad = { "cn" },
            SizeLimit = 1000
        };
        var withLaps = lapsSearcher.FindAll().Count;

        var pct = total > 0 ? (int)(withLaps * 100.0 / total) : 0;
        return new CheckResult
        {
            Id = c.Id,
            Exists = true,
            Value = pct,
            Stdout = $"LAPS coverage: {withLaps}/{total} ({pct}%)"
        };
    }

    private CheckResult CheckAdminCountOrphan(ControlDef c)
    {
        // adminCount=1 but NOT in any protected group (orphaned adminCount)
        using var searcher = new DirectorySearcher(_domainRoot!)
        {
            Filter = "(&(objectCategory=person)(objectClass=user)(adminCount=1)(!(memberOf=CN=Domain Admins,*))(!(memberOf=CN=Enterprise Admins,*))(!(memberOf=CN=Administrators,*)))",
            PropertiesToLoad = { "sAMAccountName" },
            SizeLimit = 200
        };
        var results = searcher.FindAll();
        var count = results.Count;

        return new CheckResult
        {
            Id = c.Id,
            Exists = true,
            Value = count,
            Stdout = $"{count} accounts with orphaned adminCount attribute"
        };
    }

    private CheckResult CheckGpoCount(ControlDef c)
    {
        try
        {
            using var gpoContainer = new DirectoryEntry($"LDAP://CN=Policies,CN=System,{_domainDn}");
            using var searcher = new DirectorySearcher(gpoContainer)
            {
                Filter = "(objectClass=groupPolicyContainer)",
                PropertiesToLoad = { "displayName" },
                SizeLimit = 500
            };
            var count = searcher.FindAll().Count;
            return new CheckResult
            {
                Id = c.Id,
                Exists = true,
                Value = count,
                Stdout = $"{count} Group Policy Objects in domain"
            };
        }
        catch (Exception ex)
        {
            return new CheckResult { Id = c.Id, Exists = false, Stderr = ex.Message };
        }
    }

    private CheckResult CheckTombstoneLifetime(ControlDef c)
    {
        try
        {
            using var configEntry = new DirectoryEntry($"LDAP://CN=Directory Service,CN=Windows NT,CN=Services,{_configDn}");
            var val = configEntry.Properties["tombstoneLifetime"]?.Value;
            var days = val != null ? Convert.ToInt32(val) : 180; // default is 180

            return new CheckResult
            {
                Id = c.Id,
                Exists = true,
                Value = days,
                Stdout = $"Tombstone lifetime: {days} days"
            };
        }
        catch (Exception ex)
        {
            return new CheckResult { Id = c.Id, Exists = false, Stderr = ex.Message };
        }
    }

    // ── Registry/Service-based DC checks ──

    private static CheckResult CheckDsrmPasswordSet(ControlDef c)
    {
        // DSRM password behavior: 0=no change needed, 1=sync with domain admin
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Lsa");
        var val = key?.GetValue("DsrmAdminLogonBehavior");
        var behavior = val != null ? Convert.ToInt32(val) : 0;

        return new CheckResult
        {
            Id = c.Id,
            Exists = val != null,
            Value = behavior,
            Stdout = $"DSRM admin logon behavior: {behavior}"
        };
    }

    private static CheckResult CheckNtdsService(ControlDef c)
    {
        try
        {
            using var sc = new System.ServiceProcess.ServiceController("NTDS");
            var running = sc.Status == System.ServiceProcess.ServiceControllerStatus.Running;
            return new CheckResult
            {
                Id = c.Id,
                Exists = true,
                Value = running,
                Stdout = $"NTDS service: {sc.Status}"
            };
        }
        catch (Exception ex)
        {
            return new CheckResult { Id = c.Id, Exists = false, Stderr = ex.Message };
        }
    }

    private CheckResult CheckReplicationQueue(ControlDef c)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\MicrosoftActiveDirectory",
                "SELECT * FROM MSAD_ReplNeighbor WHERE NumConsecutiveSyncFailures > 0");
            var failures = 0;
            foreach (var obj in searcher.Get()) failures++;

            return new CheckResult
            {
                Id = c.Id,
                Exists = true,
                Value = failures,
                Stdout = $"{failures} replication neighbors with consecutive failures"
            };
        }
        catch (Exception ex)
        {
            return new CheckResult { Id = c.Id, Exists = false, Stderr = $"WMI query failed: {ex.Message}" };
        }
    }

    private static CheckResult CheckTimeSource(ControlDef c)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\W32Time\Parameters");
        var ntpServer = key?.GetValue("NtpServer") as string;
        var type = key?.GetValue("Type") as string;
        var configured = !string.IsNullOrEmpty(ntpServer) && type != "NoSync";

        return new CheckResult
        {
            Id = c.Id,
            Exists = true,
            Value = configured,
            Stdout = $"Time source: Type={type}, Server={ntpServer ?? "(none)"}"
        };
    }

    private static CheckResult CheckDnsForwarders(ControlDef c)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\DNS\Parameters");
        if (key == null)
            return new CheckResult { Id = c.Id, Exists = false, Stderr = "DNS service not configured" };

        var forwarders = key.GetValue("Forwarders") as string[];
        var count = forwarders?.Length ?? 0;

        return new CheckResult
        {
            Id = c.Id,
            Exists = true,
            Value = count,
            Stdout = $"DNS forwarders configured: {count}"
        };
    }

    private static CheckResult CheckPrintSpooler(ControlDef c)
    {
        try
        {
            using var sc = new System.ServiceProcess.ServiceController("Spooler");
            var disabled = sc.StartType == System.ServiceProcess.ServiceStartMode.Disabled;
            var stopped = sc.Status == System.ServiceProcess.ServiceControllerStatus.Stopped;
            var safe = disabled || stopped;

            return new CheckResult
            {
                Id = c.Id,
                Exists = true,
                Value = safe,
                Stdout = $"Print Spooler: StartType={sc.StartType}, Status={sc.Status}"
            };
        }
        catch
        {
            return new CheckResult { Id = c.Id, Exists = true, Value = true, Stdout = "Print Spooler service not found (safe)" };
        }
    }

    private static CheckResult CheckSmbSigning(ControlDef c)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\LanManServer\Parameters");
        var requireSigning = key?.GetValue("RequireSecuritySignature");
        var enabled = requireSigning != null && Convert.ToInt32(requireSigning) == 1;

        return new CheckResult
        {
            Id = c.Id,
            Exists = requireSigning != null,
            Value = enabled,
            Stdout = $"SMB server RequireSecuritySignature: {(enabled ? "enabled" : "not required")}"
        };
    }

    private static CheckResult CheckLdapSigning(ControlDef c)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\NTDS\Parameters");
        var val = key?.GetValue("LDAPServerIntegrity");
        var level = val != null ? Convert.ToInt32(val) : 0;

        return new CheckResult
        {
            Id = c.Id,
            Exists = val != null,
            Value = level,
            Stdout = $"LDAP server signing: {(level == 2 ? "require" : level == 1 ? "negotiate" : "none")} ({level})"
        };
    }

    private static CheckResult CheckSecureChannel(ControlDef c)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Netlogon\Parameters");
        var val = key?.GetValue("FullSecureChannelProtection");
        var enabled = val != null && Convert.ToInt32(val) == 1;

        return new CheckResult
        {
            Id = c.Id,
            Exists = val != null,
            Value = enabled,
            Stdout = $"Netlogon FullSecureChannelProtection: {(enabled ? "enforced" : "not enforced")}"
        };
    }

    private static CheckResult CheckNtlmRestrict(ControlDef c)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Lsa\MSV1_0");
        var restrict = key?.GetValue("RestrictSendingNTLMTraffic");
        var level = restrict != null ? Convert.ToInt32(restrict) : 0;

        return new CheckResult
        {
            Id = c.Id,
            Exists = restrict != null,
            Value = level,
            Stdout = $"NTLM restriction level: {level} (0=allow all, 1=audit, 2=deny all)"
        };
    }

    private static CheckResult CheckCrlValidity(ControlDef c)
    {
        // Check if any CRL in LocalMachine is expired
        try
        {
            using var store = new System.Security.Cryptography.X509Certificates.X509Store(
                System.Security.Cryptography.X509Certificates.StoreName.CertificateAuthority,
                System.Security.Cryptography.X509Certificates.StoreLocation.LocalMachine);
            store.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadOnly);
            var expired = store.Certificates
                .Cast<System.Security.Cryptography.X509Certificates.X509Certificate2>()
                .Count(cert => cert.NotAfter < DateTime.UtcNow);
            store.Close();

            return new CheckResult
            {
                Id = c.Id,
                Exists = true,
                Value = expired,
                Stdout = $"{expired} expired certificates in CA store"
            };
        }
        catch (Exception ex)
        {
            return new CheckResult { Id = c.Id, Exists = false, Stderr = ex.Message };
        }
    }
}

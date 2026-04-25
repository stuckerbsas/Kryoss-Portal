using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KryossApi.Data;
using KryossApi.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KryossApi.Services.InfraAssessment.Pipelines;

public interface IHypervisorPipeline
{
    Task<HypervisorScanResult> CollectAsync(Guid scanId, Guid organizationId);
}

public class HypervisorScanResult
{
    public int HostsDiscovered { get; set; }
    public int VmsDiscovered { get; set; }
    public List<InfraAssessmentFinding> Findings { get; set; } = new();
}

public class HypervisorPipeline : IHypervisorPipeline
{
    private readonly KryossDbContext _db;
    private readonly ILogger<HypervisorPipeline> _log;

    public HypervisorPipeline(KryossDbContext db, ILogger<HypervisorPipeline> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<HypervisorScanResult> CollectAsync(Guid scanId, Guid organizationId)
    {
        var configs = await _db.InfraHypervisorConfigs
            .Where(c => c.OrganizationId == organizationId && c.IsActive)
            .ToListAsync();

        var result = new HypervisorScanResult();

        foreach (var config in configs)
        {
            try
            {
                var (hosts, vms) = config.Platform switch
                {
                    "vmware" => await CollectVmware(scanId, config),
                    "proxmox" => await CollectProxmox(scanId, config),
                    _ => (new List<InfraHypervisor>(), new List<InfraVm>())
                };

                _db.InfraHypervisors.AddRange(hosts);
                _db.InfraVms.AddRange(vms);
                result.HostsDiscovered += hosts.Count;
                result.VmsDiscovered += vms.Count;

                config.LastTestedAt = DateTime.UtcNow;
                config.LastTestOk = true;
                config.LastError = null;
            }
            catch (Exception ex)
            {
                _log.LogWarning("Hypervisor collect failed for {ConfigId} ({Platform}): {Error}",
                    config.Id, config.Platform, ex.Message);
                config.LastTestedAt = DateTime.UtcNow;
                config.LastTestOk = false;
                config.LastError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            }
        }

        await _db.SaveChangesAsync();

        // Generate findings from collected data
        var allHosts = await _db.InfraHypervisors.Where(h => h.ScanId == scanId).ToListAsync();
        var allVms = await _db.InfraVms.Where(v => v.ScanId == scanId).ToListAsync();
        result.Findings = GenerateFindings(scanId, allHosts, allVms);

        return result;
    }

    private async Task<(List<InfraHypervisor>, List<InfraVm>)> CollectVmware(Guid scanId, InfraHypervisorConfig config)
    {
        using var handler = new HttpClientHandler();
        if (!config.VerifySsl)
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;

        using var http = new HttpClient(handler) { BaseAddress = new Uri(config.HostUrl.TrimEnd('/')) };
        http.Timeout = TimeSpan.FromSeconds(30);

        // Authenticate - vSphere REST API session
        var authContent = new StringContent("", Encoding.UTF8, "application/json");
        var authReq = new HttpRequestMessage(HttpMethod.Post, "/api/session");
        authReq.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.Username}:{DecryptPassword(config)}")));
        using var authResp = await http.SendAsync(authReq);
        authResp.EnsureSuccessStatusCode();
        var sessionId = (await authResp.Content.ReadAsStringAsync()).Trim('"');

        http.DefaultRequestHeaders.Add("vmware-api-session-id", sessionId);

        var hosts = new List<InfraHypervisor>();
        var vms = new List<InfraVm>();

        // Get hosts
        using var hostsResp = await http.GetAsync("/api/vcenter/host");
        if (hostsResp.IsSuccessStatusCode)
        {
            var hostsJson = await hostsResp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(hostsJson);
            foreach (var h in doc.RootElement.EnumerateArray())
            {
                var host = new InfraHypervisor
                {
                    Id = Guid.NewGuid(),
                    ScanId = scanId,
                    ConfigId = config.Id,
                    Platform = "vmware",
                    HostFqdn = h.TryGetProperty("name", out var n) ? n.GetString()! : "unknown",
                    PowerState = h.TryGetProperty("power_state", out var ps) ? ps.GetString()?.ToLower() ?? "on" : "on",
                };
                hosts.Add(host);
            }
        }

        // Get VMs
        using var vmsResp = await http.GetAsync("/api/vcenter/vm");
        if (vmsResp.IsSuccessStatusCode)
        {
            var vmsJson = await vmsResp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(vmsJson);
            foreach (var v in doc.RootElement.EnumerateArray())
            {
                var hostName = v.TryGetProperty("host", out var hv) ? hv.GetString() : null;
                var parentHost = hosts.FirstOrDefault(h => h.HostFqdn == hostName) ?? hosts.FirstOrDefault();

                var vm = new InfraVm
                {
                    Id = Guid.NewGuid(),
                    ScanId = scanId,
                    HypervisorId = parentHost?.Id ?? Guid.Empty,
                    VmName = v.TryGetProperty("name", out var nm) ? nm.GetString()! : "unknown",
                    PowerState = v.TryGetProperty("power_state", out var pstate) ? pstate.GetString()?.ToLower() ?? "off" : "off",
                    CpuCores = v.TryGetProperty("cpu_count", out var cpu) ? cpu.GetInt32() : null,
                    RamGb = v.TryGetProperty("memory_size_MiB", out var ram) ? Math.Round((decimal)ram.GetInt64() / 1024, 2) : null,
                };
                if (parentHost != null) vms.Add(vm);
            }
        }

        // Update host VM counts
        foreach (var host in hosts)
        {
            host.VmCount = vms.Count(v => v.HypervisorId == host.Id);
            host.VmRunning = vms.Count(v => v.HypervisorId == host.Id && v.PowerState == "on");
        }

        // Cleanup session
        try { await http.DeleteAsync("/api/session"); } catch { }

        return (hosts, vms);
    }

    private async Task<(List<InfraHypervisor>, List<InfraVm>)> CollectProxmox(Guid scanId, InfraHypervisorConfig config)
    {
        using var handler = new HttpClientHandler();
        if (!config.VerifySsl)
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;

        using var http = new HttpClient(handler) { BaseAddress = new Uri(config.HostUrl.TrimEnd('/')) };
        http.Timeout = TimeSpan.FromSeconds(30);

        // Auth: either API token or username/password ticket
        if (!string.IsNullOrEmpty(config.ApiToken))
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("PVEAPIToken", config.ApiToken);
        }
        else
        {
            var ticketBody = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("username", config.Username ?? ""),
                new KeyValuePair<string, string>("password", DecryptPassword(config)),
            });
            using var ticketResp = await http.PostAsync("/api2/json/access/ticket", ticketBody);
            ticketResp.EnsureSuccessStatusCode();
            var ticketJson = await ticketResp.Content.ReadAsStringAsync();
            using var ticketDoc = JsonDocument.Parse(ticketJson);
            var ticket = ticketDoc.RootElement.GetProperty("data").GetProperty("ticket").GetString()!;
            var csrf = ticketDoc.RootElement.GetProperty("data").GetProperty("CSRFPreventionToken").GetString()!;
            http.DefaultRequestHeaders.Add("Cookie", $"PVEAuthCookie={ticket}");
            http.DefaultRequestHeaders.Add("CSRFPreventionToken", csrf);
        }

        var hosts = new List<InfraHypervisor>();
        var vms = new List<InfraVm>();

        // Get nodes
        using var nodesResp = await http.GetAsync("/api2/json/nodes");
        if (!nodesResp.IsSuccessStatusCode) return (hosts, vms);

        var nodesJson = await nodesResp.Content.ReadAsStringAsync();
        using var nodesDoc = JsonDocument.Parse(nodesJson);
        var nodes = nodesDoc.RootElement.GetProperty("data").EnumerateArray().ToList();

        foreach (var node in nodes)
        {
            var nodeName = node.GetProperty("node").GetString()!;
            var host = new InfraHypervisor
            {
                Id = Guid.NewGuid(),
                ScanId = scanId,
                ConfigId = config.Id,
                Platform = "proxmox",
                HostFqdn = nodeName,
                PowerState = node.TryGetProperty("status", out var st) ? st.GetString()?.ToLower() ?? "on" : "on",
                CpuCoresTotal = node.TryGetProperty("maxcpu", out var mc) ? mc.GetInt32() : null,
                RamGbTotal = node.TryGetProperty("maxmem", out var mm) ? Math.Round((decimal)mm.GetInt64() / 1073741824, 2) : null,
                CpuUsagePct = node.TryGetProperty("cpu", out var cpuPct) ? Math.Round((decimal)cpuPct.GetDouble() * 100, 1) : null,
                RamUsagePct = node.TryGetProperty("mem", out var memUsed) && node.TryGetProperty("maxmem", out var maxMem2)
                    ? Math.Round((decimal)memUsed.GetInt64() / maxMem2.GetInt64() * 100, 1) : null,
            };
            hosts.Add(host);

            // Get VMs for this node (QEMU)
            using var qemuResp = await http.GetAsync($"/api2/json/nodes/{nodeName}/qemu");
            if (qemuResp.IsSuccessStatusCode)
            {
                var qemuJson = await qemuResp.Content.ReadAsStringAsync();
                using var qemuDoc = JsonDocument.Parse(qemuJson);
                foreach (var q in qemuDoc.RootElement.GetProperty("data").EnumerateArray())
                {
                    var vm = new InfraVm
                    {
                        Id = Guid.NewGuid(),
                        ScanId = scanId,
                        HypervisorId = host.Id,
                        VmName = q.TryGetProperty("name", out var qn) ? qn.GetString()! : $"VM-{q.GetProperty("vmid").GetInt32()}",
                        PowerState = q.TryGetProperty("status", out var qs) ? qs.GetString()?.ToLower() ?? "off" : "off",
                        CpuCores = q.TryGetProperty("cpus", out var qcpu) ? qcpu.GetInt32() : null,
                        RamGb = q.TryGetProperty("maxmem", out var qmem) ? Math.Round((decimal)qmem.GetInt64() / 1073741824, 2) : null,
                        DiskGb = q.TryGetProperty("maxdisk", out var qdisk) ? Math.Round((decimal)qdisk.GetInt64() / 1073741824, 2) : null,
                        IsTemplate = q.TryGetProperty("template", out var qt) && qt.GetInt32() == 1,
                    };
                    vms.Add(vm);
                }
            }

            // Get LXC containers too
            using var lxcResp = await http.GetAsync($"/api2/json/nodes/{nodeName}/lxc");
            if (lxcResp.IsSuccessStatusCode)
            {
                var lxcJson = await lxcResp.Content.ReadAsStringAsync();
                using var lxcDoc = JsonDocument.Parse(lxcJson);
                foreach (var c in lxcDoc.RootElement.GetProperty("data").EnumerateArray())
                {
                    var vm = new InfraVm
                    {
                        Id = Guid.NewGuid(),
                        ScanId = scanId,
                        HypervisorId = host.Id,
                        VmName = c.TryGetProperty("name", out var cn) ? cn.GetString()! : $"CT-{c.GetProperty("vmid").GetInt32()}",
                        PowerState = c.TryGetProperty("status", out var cs) ? cs.GetString()?.ToLower() ?? "off" : "off",
                        CpuCores = c.TryGetProperty("cpus", out var ccpu) ? ccpu.GetInt32() : null,
                        RamGb = c.TryGetProperty("maxmem", out var cmem) ? Math.Round((decimal)cmem.GetInt64() / 1073741824, 2) : null,
                        DiskGb = c.TryGetProperty("maxdisk", out var cdisk) ? Math.Round((decimal)cdisk.GetInt64() / 1073741824, 2) : null,
                        Notes = "LXC container",
                    };
                    vms.Add(vm);
                }
            }

            host.VmCount = vms.Count(v => v.HypervisorId == host.Id);
            host.VmRunning = vms.Count(v => v.HypervisorId == host.Id && v.PowerState == "running");
        }

        return (hosts, vms);
    }

    private List<InfraAssessmentFinding> GenerateFindings(Guid scanId, List<InfraHypervisor> hosts, List<InfraVm> vms)
    {
        var findings = new List<InfraAssessmentFinding>();

        // Idle VMs: CPU < 5% + powered on + not a template
        foreach (var vm in vms.Where(v => v.PowerState == "on" && !v.IsTemplate && v.CpuAvgPct < 5 && v.CpuAvgPct != null))
        {
            vm.IsIdle = true;
            findings.Add(new InfraAssessmentFinding
            {
                Id = Guid.NewGuid(),
                ScanId = scanId,
                Area = "compute",
                Service = "hypervisor",
                Feature = vm.VmName,
                Status = "warning",
                Priority = "medium",
                Observation = $"VM '{vm.VmName}' appears idle (CPU avg {vm.CpuAvgPct:F1}%)",
                Recommendation = "Consolidate or decommission idle VM to reclaim resources",
            });
        }

        // Over-provisioned VMs: allocated >8 vCPU but avg <10%
        foreach (var vm in vms.Where(v => v.CpuCores > 8 && v.CpuAvgPct != null && v.CpuAvgPct < 10 && !v.IsTemplate))
        {
            findings.Add(new InfraAssessmentFinding
            {
                Id = Guid.NewGuid(),
                ScanId = scanId,
                Area = "compute",
                Service = "hypervisor",
                Feature = vm.VmName,
                Status = "warning",
                Priority = "medium",
                Observation = $"VM '{vm.VmName}' over-provisioned: {vm.CpuCores} vCPUs allocated, avg usage {vm.CpuAvgPct:F1}%",
                Recommendation = "Right-size vCPU allocation to match workload demand",
            });
        }

        // Stale snapshots (>7 days)
        foreach (var vm in vms.Where(v => v.OldestSnapshotDays > 7))
        {
            findings.Add(new InfraAssessmentFinding
            {
                Id = Guid.NewGuid(),
                ScanId = scanId,
                Area = "storage",
                Service = "hypervisor",
                Feature = vm.VmName,
                Status = "warning",
                Priority = "low",
                Observation = $"VM '{vm.VmName}' has snapshot(s) older than {vm.OldestSnapshotDays} days ({vm.SnapshotCount} total)",
                Recommendation = "Remove stale snapshots — they consume growing disk space and degrade I/O performance",
            });
        }

        // Missing backup
        foreach (var vm in vms.Where(v => v.PowerState == "on" && !v.IsTemplate && v.LastBackup == null))
        {
            findings.Add(new InfraAssessmentFinding
            {
                Id = Guid.NewGuid(),
                ScanId = scanId,
                Area = "backup",
                Service = "hypervisor",
                Feature = vm.VmName,
                Status = "action_required",
                Priority = "high",
                Observation = $"VM '{vm.VmName}' has no recorded backup",
                Recommendation = "Configure automated backup schedule for this VM",
            });
        }

        // Host capacity exhaustion (CPU or RAM > 85%)
        foreach (var host in hosts.Where(h => h.CpuUsagePct > 85 || h.RamUsagePct > 85))
        {
            var metric = host.CpuUsagePct > 85 ? $"CPU {host.CpuUsagePct:F1}%" : $"RAM {host.RamUsagePct:F1}%";
            findings.Add(new InfraAssessmentFinding
            {
                Id = Guid.NewGuid(),
                ScanId = scanId,
                Area = "capacity",
                Service = "hypervisor",
                Feature = host.HostFqdn,
                Status = "action_required",
                Priority = "high",
                Observation = $"Host '{host.HostFqdn}' approaching capacity exhaustion ({metric})",
                Recommendation = "Plan capacity expansion or migrate VMs to reduce load",
            });
        }

        // Single host with no HA
        var nonHaHosts = hosts.Where(h => h.HaEnabled == false && h.VmCount > 0).ToList();
        if (nonHaHosts.Count > 0 && hosts.Count == 1)
        {
            findings.Add(new InfraAssessmentFinding
            {
                Id = Guid.NewGuid(),
                ScanId = scanId,
                Area = "availability",
                Service = "hypervisor",
                Feature = nonHaHosts[0].HostFqdn,
                Status = "warning",
                Priority = "medium",
                Observation = $"Single hypervisor host with no HA cluster ({nonHaHosts[0].VmCount} VMs at risk)",
                Recommendation = "Deploy second host for HA/failover or ensure backup/DR plan covers host failure",
            });
        }

        // EOL OS detection
        var eolPatterns = new[] { "2012", "2008", "Windows XP", "Windows 7", "CentOS 6", "Ubuntu 14", "Ubuntu 16" };
        foreach (var vm in vms.Where(v => v.Os != null && eolPatterns.Any(p => v.Os!.Contains(p, StringComparison.OrdinalIgnoreCase))))
        {
            findings.Add(new InfraAssessmentFinding
            {
                Id = Guid.NewGuid(),
                ScanId = scanId,
                Area = "security",
                Service = "hypervisor",
                Feature = vm.VmName,
                Status = "action_required",
                Priority = "high",
                Observation = $"VM '{vm.VmName}' running end-of-life OS: {vm.Os}",
                Recommendation = "Migrate workload to supported OS version — EOL systems receive no security patches",
            });
        }

        return findings;
    }

    private static string DecryptPassword(InfraHypervisorConfig config)
    {
        // TODO: decrypt via CryptoService when envelope encryption is wired
        // For now, stored as plaintext during development
        return config.EncryptedPassword ?? "";
    }
}

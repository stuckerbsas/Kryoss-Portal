# Agent Self-Contained Evolution — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Transform KryossAgent into a single self-contained .exe that embeds config, discovers networks, and deploys itself — replacing all PS1 scripts.

**Architecture:** Six compile-time sentinel strings in the binary are patched server-side with org-specific values. The agent auto-detects whether it's patched and acts accordingly. A `--scan` mode discovers machines, copies itself via SMB, and executes via embedded PsExec.

**Tech Stack:** .NET 8 (win-x64, self-contained), Azure Functions (.NET 8 isolated), React 18 + shadcn/ui, Azure Blob Storage for template binary.

---

### Task 1: EmbeddedConfig — Sentinel Constants + Reader

**Files:**
- Create: `KryossAgent/src/KryossAgent/Config/EmbeddedConfig.cs`

- [ ] **Step 1: Create EmbeddedConfig.cs with sentinel constants and reader**

```csharp
// KryossAgent/src/KryossAgent/Config/EmbeddedConfig.cs
namespace KryossAgent.Config;

/// <summary>
/// Reads compile-time sentinel strings embedded in the binary.
/// The portal's BinaryPatcher replaces PLACEHOLDER payloads with real
/// org-specific values. If a sentinel still contains "PLACEHOLDER",
/// the binary is generic (unpatched).
/// </summary>
public static class EmbeddedConfig
{
    // ── Sentinel constants ──
    // IMPORTANT: each sentinel is a FIXED byte length. The patcher replaces
    // the payload area with real values right-padded with \0 to maintain
    // the exact same byte count. Never change these lengths without updating
    // BinaryPatcher on the server.

    // 64 bytes: @@KRYOSS_ENROLL: (16) + payload (45) + @@ (3)
    private const string ENROLLMENT_SENTINEL =
        "@@KRYOSS_ENROLL:_____________PLACEHOLDER_VALUE_00000000000@@";

    // 256 bytes: @@KRYOSS_APIURL: (16) + payload (237) + @@ (3)
    private const string APIURL_SENTINEL =
        "@@KRYOSS_APIURL:_____________PLACEHOLDER_VALUE_" +
        "000000000000000000000000000000000000000000000000" +
        "000000000000000000000000000000000000000000000000" +
        "000000000000000000000000000000000000000000000000" +
        "000000000000000000000000000000000000000000000000" +
        "0000000000000000000000000@@";

    // 128 bytes: @@KRYOSS_ORGNAM: (16) + payload (109) + @@ (3)
    private const string ORGNAME_SENTINEL =
        "@@KRYOSS_ORGNAM:_____________PLACEHOLDER_VALUE_" +
        "000000000000000000000000000000000000000000000000" +
        "00000000000000000@@";

    // 128 bytes: @@KRYOSS_MSPNAM: (16) + payload (109) + @@ (3)
    private const string MSPNAME_SENTINEL =
        "@@KRYOSS_MSPNAM:_____________PLACEHOLDER_VALUE_" +
        "000000000000000000000000000000000000000000000000" +
        "00000000000000000@@";

    // 16 bytes: @@CLRPRI:PLA@@__ (sentinel is short — prefix 9 + payload 4 + suffix 3)
    private const string PRIMARY_COLOR_SENTINEL =
        "@@CLRPRI:PLCHLD@@";

    // 16 bytes
    private const string ACCENT_COLOR_SENTINEL =
        "@@CLRACC:PLCHLD@@";

    // ── Public properties ──

    public static string? EnrollmentCode => ReadSentinel(ENROLLMENT_SENTINEL, "@@KRYOSS_ENROLL:");
    public static string? ApiUrl => ReadSentinel(APIURL_SENTINEL, "@@KRYOSS_APIURL:");
    public static string? OrgName => ReadSentinel(ORGNAME_SENTINEL, "@@KRYOSS_ORGNAM:");
    public static string? MspName => ReadSentinel(MSPNAME_SENTINEL, "@@KRYOSS_MSPNAM:");
    public static string? PrimaryColor => ReadSentinel(PRIMARY_COLOR_SENTINEL, "@@CLRPRI:");
    public static string? AccentColor => ReadSentinel(ACCENT_COLOR_SENTINEL, "@@CLRACC:");

    /// <summary>True if the binary has been patched with org-specific values.</summary>
    public static bool IsPatched => EnrollmentCode is not null;

    private static string? ReadSentinel(string raw, string prefix)
    {
        if (raw.Contains("PLACEHOLDER") || raw.Contains("PLCHLD"))
            return null;

        var start = prefix.Length;
        var end = raw.LastIndexOf("@@", StringComparison.Ordinal);
        if (end <= start) return null;

        var value = raw[start..end].TrimEnd('\0', '0', '_').Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
```

**Note on sentinel lengths:** The exact string lengths must be verified after compilation. The constants above are sized to approximate the spec (64, 256, 128, 128, 16, 16 bytes). The actual byte representation in the PE depends on the .NET string encoding. We'll verify in Task 2 and adjust padding if needed.

- [ ] **Step 2: Build to verify compilation**

Run: `cd KryossAgent/src/KryossAgent && dotnet build --no-restore`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add KryossAgent/src/KryossAgent/Config/EmbeddedConfig.cs
git commit -m "feat(agent): add EmbeddedConfig sentinel reader for binary patching"
```

---

### Task 2: Auto-Detect Flow in Program.cs

**Files:**
- Modify: `KryossAgent/src/KryossAgent/Program.cs`

- [ ] **Step 1: Rewrite Program.cs top section with auto-detect logic and branded banner**

Replace the top of Program.cs (lines 1-45, before the enrollment block) with:

```csharp
using System.Diagnostics;
using KryossAgent.Config;
using KryossAgent.Engines;
using KryossAgent.Models;
using KryossAgent.Services;

// ── Kryoss Security Agent ──
// v1.0.0

var sw = Stopwatch.StartNew();
var silent = args.Contains("--silent", StringComparer.OrdinalIgnoreCase);
var scanMode = args.Contains("--scan", StringComparer.OrdinalIgnoreCase);

// ── Branded banner ──
if (!silent)
{
    var mspName = EmbeddedConfig.MspName;
    var orgName = EmbeddedConfig.OrgName;

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("╔══════════════════════════════════════════╗");
    if (mspName is not null && orgName is not null)
    {
        var line1 = $"{mspName} — Security Assessment";
        var line2 = orgName;
        Console.WriteLine($"║  {line1,-40}║");
        Console.WriteLine($"║  {line2,-40}║");
    }
    else
    {
        Console.WriteLine("║       Kryoss Security Agent v1.0.0      ║");
        Console.WriteLine("║         Security Assessment              ║");
    }
    Console.WriteLine("╚══════════════════════════════════════════╝");
    Console.ResetColor();
    Console.WriteLine();
}

// ── Network scan mode ──
if (scanMode)
{
    var scanExitCode = await NetworkScanner.RunAsync(args, silent);
    Environment.Exit(scanExitCode);
    return;
}

// ── Load or create config ──
var config = AgentConfig.Load();

// ── Apply embedded config ──
if (EmbeddedConfig.ApiUrl is not null)
    config.ApiUrl = EmbeddedConfig.ApiUrl;

// ── Parse CLI arguments ──
var cliCode = GetArg(args, "--code");
var cliApiUrl = GetArg(args, "--api-url");
var forceReenroll = args.Contains("--reenroll", StringComparer.OrdinalIgnoreCase);
if (!string.IsNullOrEmpty(cliApiUrl)) config.ApiUrl = cliApiUrl;

// Resolve enrollment code: CLI > embedded > interactive
var enrollCode = cliCode ?? EmbeddedConfig.EnrollmentCode;

// ── Force re-enrollment: wipe existing config ──
if (forceReenroll && !string.IsNullOrEmpty(enrollCode))
{
    if (!silent) Console.WriteLine("  Re-enrollment requested. Clearing existing config...");
    try
    {
        Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\Kryoss\Agent", throwOnMissingSubKey: false);
    }
    catch { /* non-critical */ }
    config = new AgentConfig();
    if (EmbeddedConfig.ApiUrl is not null) config.ApiUrl = EmbeddedConfig.ApiUrl;
    if (!string.IsNullOrEmpty(cliApiUrl)) config.ApiUrl = cliApiUrl;
}
```

- [ ] **Step 2: Update the enrollment block to use enrollCode**

Replace the enrollment block (the `if (!config.IsEnrolled)` section) — change the code resolution to use `enrollCode`:

```csharp
// ── Enrollment (first run or re-enrollment) ──
if (!config.IsEnrolled)
{
    // In silent mode with embedded config, auto-enroll
    if (silent && string.IsNullOrEmpty(enrollCode))
    {
        Console.Error.WriteLine("[ERROR] Agent not enrolled. Provide --code <enrollment-code> or use a patched binary.");
        Console.WriteLine($"RESULT: ENROLL_FAILED | {Environment.MachineName} | No enrollment code available");
        Environment.Exit(1);
        return;
    }

    string? code;
    if (!string.IsNullOrEmpty(enrollCode))
    {
        code = enrollCode;
        // If embedded, force silent enrollment
        if (EmbeddedConfig.IsPatched) silent = true;
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("  No configuration found. Entering enrollment mode.");
        Console.ResetColor();
        Console.WriteLine();

        Console.Write("  Enter enrollment code: ");
        code = Console.ReadLine()?.Trim();
    }

    if (string.IsNullOrEmpty(code))
    {
        Console.Error.WriteLine("[ERROR] Enrollment code is required.");
        Console.WriteLine($"RESULT: ENROLL_FAILED | {Environment.MachineName} | No enrollment code provided");
        Environment.Exit(1);
        return;
    }

    // API URL: use embedded > CLI > compiled default. No interactive prompt.

    var hostname = Environment.MachineName;
    var platform = PlatformDetector.DetectPlatform();
    var hardware = PlatformDetector.DetectHardware();

    if (!silent) Console.WriteLine($"  Enrolling {hostname}...");

    try
    {
        using var enrollClient = new ApiClient(config);
        var enrollment = await enrollClient.EnrollAsync(code, hostname, platform, hardware);
        if (enrollment is null)
        {
            Console.Error.WriteLine("[ERROR] Enrollment returned null.");
            Console.WriteLine($"RESULT: ENROLL_FAILED | {Environment.MachineName} | Enrollment returned null");
            Environment.Exit(1);
            return;
        }

        config.AgentId = enrollment.AgentId;
        config.ApiKey = enrollment.ApiKey;
        config.ApiSecret = enrollment.ApiSecret;
        config.PublicKeyPem = enrollment.PublicKey;
        config.AssessmentId = enrollment.AssessmentId;
        config.AssessmentName = enrollment.AssessmentName;
        config.Save();

        if (silent)
        {
            Console.WriteLine($"Kryoss: Enrolled {hostname} — Assessment: {enrollment.AssessmentName} ({enrollment.AssessmentId})");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  Enrolled successfully as {hostname}");
            Console.WriteLine($"  Assessment: {enrollment.AssessmentName ?? "Default"} ({enrollment.AssessmentId})");
            Console.ResetColor();
            Console.WriteLine();
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"[ERROR] Enrollment failed: {ex.Message}");
        Console.ResetColor();
        Console.WriteLine($"RESULT: ENROLL_FAILED | {Environment.MachineName} | {ex.Message}");
        Environment.Exit(1);
        return;
    }
}
```

- [ ] **Step 3: Build to verify**

Run: `cd KryossAgent/src/KryossAgent && dotnet build --no-restore`
Expected: Build will fail because `NetworkScanner` doesn't exist yet. That's OK — we'll create it in Task 4. For now, comment out the `NetworkScanner.RunAsync` call temporarily to verify the rest compiles.

- [ ] **Step 4: Commit**

```bash
git add KryossAgent/src/KryossAgent/Program.cs
git commit -m "feat(agent): auto-detect flow with embedded config and branded banner"
```

---

### Task 3: PsExec Embedded Resource + Runner

**Files:**
- Create: `KryossAgent/src/KryossAgent/Resources/` (directory)
- Create: `KryossAgent/src/KryossAgent/Services/PsExecRunner.cs`
- Modify: `KryossAgent/src/KryossAgent/KryossAgent.csproj`

- [ ] **Step 1: Download PsExec64.exe and add as embedded resource**

```bash
# Download PsExec64.exe
cd KryossAgent/src/KryossAgent
mkdir -p Resources
curl -L -o Resources/PsExec64.exe "https://live.sysinternals.com/PsExec64.exe"
```

- [ ] **Step 2: Update .csproj to embed PsExec**

Add to `KryossAgent.csproj` inside a new `<ItemGroup>`:

```xml
  <ItemGroup>
    <EmbeddedResource Include="Resources\PsExec64.exe" LogicalName="PsExec64.exe" />
  </ItemGroup>
```

Also add the `System.DirectoryServices` package for AD discovery:

```xml
  <ItemGroup>
    <!-- existing packages... -->
    <PackageReference Include="System.DirectoryServices" Version="8.0.*" />
  </ItemGroup>
```

- [ ] **Step 3: Create PsExecRunner.cs**

```csharp
// KryossAgent/src/KryossAgent/Services/PsExecRunner.cs
using System.Diagnostics;
using System.Reflection;

namespace KryossAgent.Services;

/// <summary>
/// Extracts PsExec64.exe from embedded resources and runs it against remote targets.
/// The embedded resource is extracted to %TEMP% on first use and cleaned up on dispose.
/// </summary>
public sealed class PsExecRunner : IDisposable
{
    private readonly string _psExecPath;
    private bool _disposed;

    public PsExecRunner()
    {
        var pid = Environment.ProcessId;
        _psExecPath = Path.Combine(Path.GetTempPath(), $"KryossAgent_PsExec64_{pid}.exe");
        ExtractResource();
    }

    private void ExtractResource()
    {
        if (File.Exists(_psExecPath)) return;

        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("PsExec64.exe");

        if (stream is null)
            throw new InvalidOperationException(
                "PsExec64.exe embedded resource not found. Rebuild with the EmbeddedResource in .csproj.");

        using var file = File.Create(_psExecPath);
        stream.CopyTo(file);
    }

    /// <summary>
    /// Runs KryossAgent.exe on a remote machine via PsExec.
    /// Returns (exitCode, stdout, stderr).
    /// </summary>
    public async Task<PsExecResult> RunRemoteAsync(
        string target,
        string remoteExePath,
        string agentArgs,
        string? username = null,
        string? password = null,
        int timeoutMs = 300_000)
    {
        var psArgs = new List<string>
        {
            $"\\\\{target}",
        };

        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
        {
            psArgs.AddRange(["-u", username, "-p", password]);
        }

        psArgs.AddRange([
            "-s", "-h",
            "-n", "30",
            "-accepteula",
            remoteExePath
        ]);

        // Split agent args and add individually
        foreach (var arg in agentArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            psArgs.Add(arg);

        var psi = new ProcessStartInfo
        {
            FileName = _psExecPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in psArgs) psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var completed = await Task.Run(() => process.WaitForExit(timeoutMs));
        if (!completed)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new PsExecResult(-1, stdout.ToString(), "Timed out after " + timeoutMs / 1000 + "s");
        }

        return new PsExecResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { File.Delete(_psExecPath); } catch { /* best effort */ }
    }
}

public record PsExecResult(int ExitCode, string Stdout, string Stderr)
{
    /// <summary>Parse the RESULT: line from agent stdout.</summary>
    public string? ParseResultLine()
    {
        foreach (var line in Stdout.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("RESULT:", StringComparison.OrdinalIgnoreCase))
                return trimmed;
        }
        return null;
    }
}
```

- [ ] **Step 4: Build to verify**

Run: `cd KryossAgent/src/KryossAgent && dotnet build`
Expected: Build succeeded (will restore System.DirectoryServices).

- [ ] **Step 5: Commit**

```bash
git add KryossAgent/src/KryossAgent/Resources/PsExec64.exe
git add KryossAgent/src/KryossAgent/Services/PsExecRunner.cs
git add KryossAgent/src/KryossAgent/KryossAgent.csproj
git commit -m "feat(agent): embed PsExec64 as resource + PsExecRunner service"
```

---

### Task 4: Target Discovery (AD, ARP, Subnet)

**Files:**
- Create: `KryossAgent/src/KryossAgent/Services/TargetDiscovery.cs`

- [ ] **Step 1: Create TargetDiscovery.cs with all discovery methods**

```csharp
// KryossAgent/src/KryossAgent/Services/TargetDiscovery.cs
using System.Diagnostics;
using System.DirectoryServices;
using System.Net;
using System.Net.Sockets;

namespace KryossAgent.Services;

public record ScanTarget(string Hostname, string Address, string Source);

/// <summary>
/// Discovers Windows machines via Active Directory, ARP table, subnet probe,
/// explicit list, or file. All sources merge and deduplicate by hostname.
/// </summary>
public static class TargetDiscovery
{
    public static async Task<List<ScanTarget>> DiscoverAsync(string[] args)
    {
        var targets = new List<ScanTarget>();

        // Explicit --targets host1,host2,host3
        var explicitList = GetArg(args, "--targets");
        if (!string.IsNullOrEmpty(explicitList))
        {
            foreach (var h in explicitList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                targets.Add(new ScanTarget(h, h, "explicit"));
        }

        // --targets-file machines.txt
        var targetsFile = GetArg(args, "--targets-file");
        if (!string.IsNullOrEmpty(targetsFile) && File.Exists(targetsFile))
        {
            foreach (var line in await File.ReadAllLinesAsync(targetsFile))
            {
                var h = line.Trim();
                if (!string.IsNullOrEmpty(h) && !h.StartsWith('#'))
                    targets.Add(new ScanTarget(h, h, "file"));
            }
        }

        // --discover-ad [OU path]
        if (args.Any(a => a.Equals("--discover-ad", StringComparison.OrdinalIgnoreCase)))
        {
            var ouPath = GetArg(args, "--discover-ad");
            targets.AddRange(DiscoverAD(ouPath));
        }

        // --discover-arp
        if (args.Any(a => a.Equals("--discover-arp", StringComparison.OrdinalIgnoreCase)))
        {
            targets.AddRange(DiscoverARP());
        }

        // --discover-subnet 192.168.1.0/24
        var subnet = GetArg(args, "--discover-subnet");
        if (!string.IsNullOrEmpty(subnet))
        {
            targets.AddRange(await DiscoverSubnetAsync(subnet));
        }

        // Default: if no discovery flags and no explicit targets, try AD then ARP
        if (targets.Count == 0)
        {
            Console.WriteLine("  No discovery flags specified. Trying AD...");
            targets.AddRange(DiscoverAD(null));
            if (targets.Count == 0)
            {
                Console.WriteLine("  AD returned no results. Trying ARP...");
                targets.AddRange(DiscoverARP());
            }
        }

        // Deduplicate by hostname (case-insensitive)
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deduped = new List<ScanTarget>();
        foreach (var t in targets)
        {
            var key = t.Hostname.Split('.')[0]; // Use short hostname for dedup
            if (seen.Add(key))
                deduped.Add(t);
        }

        // Remove self
        var self = Environment.MachineName;
        deduped.RemoveAll(t => t.Hostname.Split('.')[0].Equals(self, StringComparison.OrdinalIgnoreCase));

        return deduped;
    }

    private static List<ScanTarget> DiscoverAD(string? ouPath)
    {
        var results = new List<ScanTarget>();
        try
        {
            var searchRoot = string.IsNullOrEmpty(ouPath)
                ? new DirectoryEntry()
                : new DirectoryEntry($"LDAP://{ouPath}");

            using var searcher = new DirectorySearcher(searchRoot)
            {
                Filter = "(&(objectClass=computer)(operatingSystem=Windows*)(!(userAccountControl:1.2.840.113556.1.4.803:=2)))",
                PageSize = 1000
            };
            searcher.PropertiesToLoad.AddRange(["name", "dNSHostName", "operatingSystem", "lastLogonTimestamp"]);

            var cutoff = DateTime.UtcNow.AddDays(-30).ToFileTimeUtc();

            foreach (SearchResult sr in searcher.FindAll())
            {
                var name = sr.Properties["name"]?.Count > 0 ? sr.Properties["name"][0]?.ToString() : null;
                var dns = sr.Properties["dNSHostName"]?.Count > 0 ? sr.Properties["dNSHostName"][0]?.ToString() : null;
                var lastLogon = sr.Properties["lastLogonTimestamp"]?.Count > 0 ? (long)sr.Properties["lastLogonTimestamp"][0]! : 0;

                if (string.IsNullOrEmpty(name)) continue;
                if (lastLogon > 0 && lastLogon < cutoff) continue; // Skip stale (>30 days)

                results.Add(new ScanTarget(name, dns ?? name, "ad"));
            }

            Console.WriteLine($"  AD discovery: {results.Count} machines found");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [WARN] AD discovery failed: {ex.Message}");
        }
        return results;
    }

    private static List<ScanTarget> DiscoverARP()
    {
        var results = new List<ScanTarget>();
        try
        {
            var psi = new ProcessStartInfo("arp", "-a")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null) return results;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            foreach (var line in output.Split('\n'))
            {
                var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3) continue;

                var ip = parts[0];
                if (!IPAddress.TryParse(ip, out var addr)) continue;
                if (addr.Equals(IPAddress.Broadcast)) continue;
                if (ip.EndsWith(".255") || ip.EndsWith(".1")) continue; // Skip broadcast/gateway
                if (parts[2] == "ff-ff-ff-ff-ff-ff") continue; // Skip broadcast MAC

                // Reverse DNS best-effort
                string hostname = ip;
                try
                {
                    var entry = Dns.GetHostEntry(addr);
                    if (!string.IsNullOrEmpty(entry.HostName))
                        hostname = entry.HostName;
                }
                catch { /* keep IP */ }

                results.Add(new ScanTarget(hostname, ip, "arp"));
            }

            Console.WriteLine($"  ARP discovery: {results.Count} hosts found");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [WARN] ARP discovery failed: {ex.Message}");
        }
        return results;
    }

    private static async Task<List<ScanTarget>> DiscoverSubnetAsync(string cidr)
    {
        var results = new List<ScanTarget>();
        try
        {
            var parts = cidr.Split('/');
            if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var baseIp) || !int.TryParse(parts[1], out var prefixLen))
            {
                Console.Error.WriteLine($"  [WARN] Invalid CIDR: {cidr}");
                return results;
            }

            var baseBytes = baseIp.GetAddressBytes();
            var hostBits = 32 - prefixLen;
            var hostCount = (1 << hostBits) - 2; // Exclude network + broadcast

            Console.WriteLine($"  Subnet scan: probing {hostCount} addresses on port 445...");

            var tasks = new List<Task<ScanTarget?>>();
            for (int i = 1; i <= hostCount; i++)
            {
                var ipBytes = (byte[])baseBytes.Clone();
                var offset = i;
                ipBytes[3] = (byte)((baseBytes[3] & (0xFF << hostBits)) | offset);
                var ip = new IPAddress(ipBytes);
                var ipStr = ip.ToString();

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        using var client = new TcpClient();
                        var connectTask = client.ConnectAsync(ip, 445);
                        if (await Task.WhenAny(connectTask, Task.Delay(2000)) == connectTask && client.Connected)
                            return new ScanTarget(ipStr, ipStr, "subnet");
                    }
                    catch { /* not reachable */ }
                    return null;
                }));
            }

            var scanResults = await Task.WhenAll(tasks);
            results.AddRange(scanResults.Where(r => r is not null)!);

            Console.WriteLine($"  Subnet scan: {results.Count} hosts with SMB open");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [WARN] Subnet scan failed: {ex.Message}");
        }
        return results;
    }

    private static string? GetArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        // For flags that may not have a value (like --discover-ad without OU)
        return null;
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `cd KryossAgent/src/KryossAgent && dotnet build`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add KryossAgent/src/KryossAgent/Services/TargetDiscovery.cs
git commit -m "feat(agent): target discovery via AD, ARP, and subnet scan"
```

---

### Task 5: NetworkScanner — Scan Orchestrator

**Files:**
- Create: `KryossAgent/src/KryossAgent/Services/NetworkScanner.cs`

- [ ] **Step 1: Create NetworkScanner.cs**

```csharp
// KryossAgent/src/KryossAgent/Services/NetworkScanner.cs
using System.Diagnostics;
using System.Net.Sockets;
using KryossAgent.Config;

namespace KryossAgent.Services;

/// <summary>
/// Orchestrates network-wide scanning: discover targets, push agent binary,
/// execute via PsExec, collect results. Replaces Invoke-KryossDeployment.ps1.
/// </summary>
public static class NetworkScanner
{
    public static async Task<int> RunAsync(string[] args, bool silent)
    {
        var threads = 5;
        var threadsStr = GetArg(args, "--threads");
        if (int.TryParse(threadsStr, out var t)) threads = Math.Clamp(t, 1, 20);

        var reenroll = args.Any(a => a.Equals("--reenroll", StringComparison.OrdinalIgnoreCase));
        var code = GetArg(args, "--code") ?? EmbeddedConfig.EnrollmentCode;
        var credential = args.Any(a => a.Equals("--credential", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrEmpty(code))
        {
            Console.Error.WriteLine("[ERROR] --scan requires an enrollment code. Use --code or a patched binary.");
            return 1;
        }

        // Get credentials if requested
        string? username = null, password = null;
        if (credential)
        {
            Console.Write("  Username (DOMAIN\\user): ");
            username = Console.ReadLine()?.Trim();
            Console.Write("  Password: ");
            password = ReadMaskedPassword();
            Console.WriteLine();
        }

        // ── Phase 1: Discover ──
        if (!silent) Console.WriteLine("\n  === PHASE 1: DISCOVER ===\n");
        var targets = await TargetDiscovery.DiscoverAsync(args);

        if (targets.Count == 0)
        {
            Console.Error.WriteLine("[ERROR] No targets discovered.");
            return 1;
        }

        if (!silent)
        {
            Console.WriteLine($"\n  Found {targets.Count} target(s):");
            foreach (var target in targets)
                Console.WriteLine($"    {target.Hostname,-25} {target.Address,-20} [{target.Source}]");

            if (!args.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase)))
            {
                Console.Write("\n  Proceed? [Y/n] ");
                var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (answer == "n") return 0;
            }
        }

        // ── Phase 2: Deploy + Scan ──
        if (!silent) Console.WriteLine($"\n  === PHASE 2: DEPLOY & SCAN ({threads} threads) ===\n");

        var selfPath = Environment.ProcessPath ?? "KryossAgent.exe";
        var remotePath = @"C:\Windows\Temp\KryossAgent.exe";

        // Build agent args for remote execution
        var remoteArgs = "--silent";
        if (reenroll) remoteArgs += " --reenroll";
        remoteArgs += $" --code {code}";

        using var psExec = new PsExecRunner();
        var results = new List<ScanResult>();
        var total = targets.Count;
        var completed = 0;
        var logLock = new object();

        var semaphore = new SemaphoreSlim(threads);

        var tasks = targets.Select(async target =>
        {
            await semaphore.WaitAsync();
            var targetSw = Stopwatch.StartNew();
            try
            {
                var result = await ScanSingleTarget(
                    target, selfPath, remotePath, remoteArgs,
                    username, password, psExec);

                targetSw.Stop();
                result = result with { DurationMs = (int)targetSw.ElapsedMilliseconds };

                lock (logLock)
                {
                    completed++;
                    results.Add(result);
                    PrintResult(result, completed, total);
                }
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        // ── Summary ──
        PrintSummary(results);

        var failures = results.Count(r => r.Status is "ScanFailed" or "Unreachable" or "DeployFailed");
        return failures > 0 && results.All(r => r.Status != "OK") ? 1 : 0;
    }

    private static async Task<ScanResult> ScanSingleTarget(
        ScanTarget target, string selfPath, string remotePath, string remoteArgs,
        string? username, string? password, PsExecRunner psExec)
    {
        var host = target.Address;
        var name = target.Hostname.Split('.')[0];

        // 1. Connectivity check (TCP 445)
        try
        {
            using var tcp = new TcpClient();
            var connectTask = tcp.ConnectAsync(host, 445);
            if (await Task.WhenAny(connectTask, Task.Delay(2000)) != connectTask || !tcp.Connected)
                return new ScanResult(name, host, "Unreachable", "SMB port 445 not reachable", null, 0);
        }
        catch
        {
            return new ScanResult(name, host, "Unreachable", "Connection failed", null, 0);
        }

        // 2. Deploy: copy self via SMB admin share
        var adminShare = $"\\\\{host}\\C$\\Windows\\Temp";
        var remoteFile = $"{adminShare}\\KryossAgent.exe";
        try
        {
            if (username is not null && password is not null)
            {
                // Map admin share with credentials
                var netResult = RunCmd("net", $"use \"{adminShare}\" /user:{username} \"{password}\" /y");
                if (netResult.ExitCode != 0)
                    return new ScanResult(name, host, "DeployFailed", $"Admin share access denied", null, 0);
            }

            File.Copy(selfPath, remoteFile, overwrite: true);
        }
        catch (Exception ex)
        {
            return new ScanResult(name, host, "DeployFailed", $"Copy failed: {ex.Message}", null, 0);
        }

        // 3. Execute via PsExec
        try
        {
            var result = await psExec.RunRemoteAsync(
                host, remotePath, remoteArgs,
                username, password,
                timeoutMs: 300_000);

            var resultLine = result.ParseResultLine();
            var status = result.ExitCode == 0 ? "OK"
                : result.ExitCode == 2 ? "Partial"
                : "ScanFailed";

            return new ScanResult(name, host, status,
                resultLine ?? $"exit {result.ExitCode}", resultLine, 0);
        }
        catch (Exception ex)
        {
            return new ScanResult(name, host, "ScanFailed", ex.Message, null, 0);
        }
        finally
        {
            // 4. Cleanup remote binary
            try { File.Delete(remoteFile); } catch { }
            if (username is not null)
            {
                try { RunCmd("net", $"use \"{adminShare}\" /delete /y"); } catch { }
            }
        }
    }

    private static (int ExitCode, string Output) RunCmd(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(10_000);
        return (p.ExitCode, output);
    }

    private static void PrintResult(ScanResult r, int current, int total)
    {
        var color = r.Status switch
        {
            "OK" => ConsoleColor.Green,
            "Partial" => ConsoleColor.Yellow,
            _ => ConsoleColor.Red
        };
        var duration = r.DurationMs > 0 ? $" — {r.DurationMs / 1000}s" : "";
        var detail = r.ResultLine ?? r.Error ?? "";

        Console.ForegroundColor = color;
        Console.Write($"  [{current}/{total}] {r.Name,-20} ");
        Console.ResetColor();
        Console.WriteLine($"{r.Status,-12} {detail}{duration}");
    }

    private static void PrintSummary(List<ScanResult> results)
    {
        var ok = results.Count(r => r.Status == "OK");
        var partial = results.Count(r => r.Status == "Partial");
        var failed = results.Count(r => r.Status == "ScanFailed");
        var unreachable = results.Count(r => r.Status == "Unreachable");
        var deployFailed = results.Count(r => r.Status == "DeployFailed");

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  ══════════════════════════════════════════");
        Console.WriteLine("    Kryoss Network Scan Complete");
        Console.WriteLine("  ══════════════════════════════════════════");
        Console.ResetColor();
        Console.WriteLine($"    Targets:      {results.Count}");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"    Scanned OK:   {ok}");
        Console.ResetColor();
        if (partial > 0) { Console.ForegroundColor = ConsoleColor.Yellow; Console.WriteLine($"    Partial:      {partial}"); Console.ResetColor(); }
        if (failed > 0) { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine($"    Failed:       {failed}"); Console.ResetColor(); }
        if (unreachable > 0) Console.WriteLine($"    Unreachable:  {unreachable}");
        if (deployFailed > 0) Console.WriteLine($"    Deploy fail:  {deployFailed}");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  ══════════════════════════════════════════");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static string ReadMaskedPassword()
    {
        var password = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace && password.Length > 0)
            {
                password.Length--;
                Console.Write("\b \b");
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
                Console.Write('*');
            }
        }
        return password.ToString();
    }

    private static string? GetArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }
}

public record ScanResult(
    string Name,
    string Address,
    string Status,
    string? Error,
    string? ResultLine,
    int DurationMs);
```

- [ ] **Step 2: Uncomment the NetworkScanner call in Program.cs**

Ensure Program.cs line ~48 has:
```csharp
if (scanMode)
{
    var scanExitCode = await NetworkScanner.RunAsync(args, silent);
    Environment.Exit(scanExitCode);
    return;
}
```

- [ ] **Step 3: Build to verify**

Run: `cd KryossAgent/src/KryossAgent && dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add KryossAgent/src/KryossAgent/Services/NetworkScanner.cs
git commit -m "feat(agent): network scan orchestrator replaces PS1 deployment script"
```

---

### Task 6: Backend — BinaryPatcher + AgentDownloadFunction

**Files:**
- Create: `KryossApi/src/KryossApi/Services/BinaryPatcher.cs`
- Create: `KryossApi/src/KryossApi/Functions/Portal/AgentDownloadFunction.cs`

- [ ] **Step 1: Create BinaryPatcher.cs**

```csharp
// KryossApi/src/KryossApi/Services/BinaryPatcher.cs
using System.Text;

namespace KryossApi.Services;

/// <summary>
/// Performs byte-level patching of sentinel strings in the KryossAgent binary.
/// Each sentinel is a fixed-length ASCII string. The patcher finds the sentinel
/// prefix, replaces the payload area with the real value (null-padded), and
/// preserves the exact byte length so the PE stays valid.
/// </summary>
public static class BinaryPatcher
{
    private static readonly (string Prefix, int TotalLen, string Key)[] Sentinels =
    [
        ("@@KRYOSS_ENROLL:", 64, "enrollmentCode"),
        ("@@KRYOSS_APIURL:", 256, "apiUrl"),
        ("@@KRYOSS_ORGNAM:", 128, "orgName"),
        ("@@KRYOSS_MSPNAM:", 128, "mspName"),
        ("@@CLRPRI:", 17, "primaryColor"),
        ("@@CLRACC:", 17, "accentColor"),
    ];

    /// <summary>
    /// Patches sentinel values in the binary. Returns a new byte array with patched values.
    /// </summary>
    public static byte[] Patch(byte[] templateBinary, Dictionary<string, string> values)
    {
        var result = (byte[])templateBinary.Clone();

        foreach (var (prefix, totalLen, key) in Sentinels)
        {
            if (!values.TryGetValue(key, out var value) || string.IsNullOrEmpty(value))
                continue;

            var prefixBytes = Encoding.ASCII.GetBytes(prefix);
            var offset = FindBytes(result, prefixBytes);
            if (offset < 0)
            {
                Console.Error.WriteLine($"[WARN] Sentinel prefix '{prefix}' not found in binary");
                continue;
            }

            // Payload area starts after prefix, ends 3 bytes before totalLen (for "@@" suffix)
            // But we just replace from prefix to prefix+totalLen with: prefix + value + padding + @@
            var suffixBytes = Encoding.ASCII.GetBytes("@@");
            var payloadLen = totalLen - prefixBytes.Length - suffixBytes.Length;
            var valueBytes = Encoding.ASCII.GetBytes(value);

            if (valueBytes.Length > payloadLen)
            {
                // Truncate value to fit
                valueBytes = valueBytes[..payloadLen];
            }

            // Write: prefix (already there) + value + null padding + suffix
            var payloadOffset = offset + prefixBytes.Length;
            Array.Clear(result, payloadOffset, payloadLen); // Zero the payload area
            Array.Copy(valueBytes, 0, result, payloadOffset, valueBytes.Length);

            // Suffix is already in place (@@), no need to rewrite
        }

        return result;
    }

    private static int FindBytes(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) return i;
        }
        return -1;
    }
}
```

- [ ] **Step 2: Create AgentDownloadFunction.cs**

```csharp
// KryossApi/src/KryossApi/Functions/Portal/AgentDownloadFunction.cs
using System.Net;
using Azure.Storage.Blobs;
using KryossApi.Data;
using KryossApi.Middleware;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace KryossApi.Functions.Portal;

/// <summary>
/// Serves a pre-configured KryossAgent.exe with embedded enrollment code,
/// API URL, org name, MSP name, and brand colors.
/// Template binary stored in Azure Blob Storage.
/// </summary>
[RequirePermission("agents:download")]
public class AgentDownloadFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IEnrollmentService _enrollment;
    private readonly IConfiguration _config;

    public AgentDownloadFunction(
        KryossDbContext db,
        ICurrentUserService user,
        IEnrollmentService enrollment,
        IConfiguration config)
    {
        _db = db;
        _user = user;
        _enrollment = enrollment;
        _config = config;
    }

    [Function("AgentDownload")]
    public async Task<HttpResponseData> Download(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/agent/download")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var orgIdStr = query["orgId"];

        if (!Guid.TryParse(orgIdStr, out var orgId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "orgId query parameter is required" });
            return bad;
        }

        // Load org with brand + franchise
        var org = await _db.Organizations
            .Include(o => o.Brand)
            .Include(o => o.Franchise)
            .FirstOrDefaultAsync(o => o.Id == orgId);

        if (org is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Organization not found" });
            return notFound;
        }

        // Get or create a multi-use enrollment code for the org
        var existingCode = await _db.EnrollmentCodes
            .Where(e => e.OrganizationId == orgId
                && e.MaxUses != null
                && e.UseCount < e.MaxUses
                && e.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => e.Code)
            .FirstOrDefaultAsync();

        var enrollCode = existingCode
            ?? await _enrollment.GenerateCodeAsync(orgId, null, "Agent download", 30, 999);

        // Read template binary from blob storage
        var blobConnStr = _config["AzureWebJobsStorage"]
            ?? _config.GetConnectionString("BlobStorage");
        if (string.IsNullOrEmpty(blobConnStr))
        {
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteAsJsonAsync(new { error = "Blob storage not configured" });
            return err;
        }

        var blobClient = new BlobServiceClient(blobConnStr);
        var container = blobClient.GetBlobContainerClient("kryoss-agent-templates");
        var blob = container.GetBlobClient("latest/KryossAgent.exe");

        if (!await blob.ExistsAsync())
        {
            var err = req.CreateResponse(HttpStatusCode.NotFound);
            await err.WriteAsJsonAsync(new { error = "Agent template binary not found in blob storage" });
            return err;
        }

        using var blobStream = new MemoryStream();
        await blob.DownloadToAsync(blobStream);
        var templateBytes = blobStream.ToArray();

        // Resolve branding
        var mspName = org.Brand?.Name ?? org.Franchise?.BrandName ?? org.Franchise?.Name ?? "TeamLogic IT";
        var primaryColor = org.Brand?.ColorPrimary ?? org.Franchise?.BrandColorPrimary ?? "#008852";
        var accentColor = org.Brand?.ColorAccent ?? org.Franchise?.BrandColorAccent ?? "#A2C564";
        var apiUrl = _config["AgentApiUrl"] ?? "https://func-kryoss.azurewebsites.net";

        // Patch binary
        var patchedBytes = BinaryPatcher.Patch(templateBytes, new Dictionary<string, string>
        {
            ["enrollmentCode"] = enrollCode,
            ["apiUrl"] = apiUrl,
            ["orgName"] = org.Name,
            ["mspName"] = mspName,
            ["primaryColor"] = primaryColor,
            ["accentColor"] = accentColor,
        });

        // Stream as download
        var slug = org.Name.ToLowerInvariant().Replace(' ', '-');
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/octet-stream");
        response.Headers.Add("Content-Disposition", $"attachment; filename=\"KryossAgent-{slug}.exe\"");
        await response.Body.WriteAsync(patchedBytes);

        return response;
    }
}
```

- [ ] **Step 3: Add Azure.Storage.Blobs NuGet package**

Run: `cd KryossApi/src/KryossApi && dotnet add package Azure.Storage.Blobs`

- [ ] **Step 4: Build to verify**

Run: `cd KryossApi/src/KryossApi && dotnet build`
Expected: Build succeeded. May have warnings about nullable Brand/Franchise navigation properties — fix with null-conditional operators if needed.

- [ ] **Step 5: Commit**

```bash
git add KryossApi/src/KryossApi/Services/BinaryPatcher.cs
git add KryossApi/src/KryossApi/Functions/Portal/AgentDownloadFunction.cs
git add KryossApi/src/KryossApi/KryossApi.csproj
git commit -m "feat(api): binary patcher + agent download endpoint for portal"
```

---

### Task 7: Portal — Download Agent Button

**Files:**
- Modify: `KryossPortal/src/components/org-detail/OrgDetail.tsx`

- [ ] **Step 1: Add download button to OrgDetail header**

In `OrgDetail.tsx`, add import and button:

```tsx
// Add to imports
import { Building2, Pencil, Trash2, Download, Loader2 } from 'lucide-react';
import { toast } from 'sonner';
import { API_BASE } from '@/auth/msalConfig';
import { msalInstance } from '@/auth/msalInstance';
import { loginRequest } from '@/auth/msalConfig';
import { useResolvedOrgId } from '@/api/organizations';
import { slugify } from '@/lib/slugify';
```

Add a download handler function inside the component:

```tsx
const [downloading, setDownloading] = useState(false);
const resolvedOrgId = useResolvedOrgId(orgSlug);

const handleDownloadAgent = async () => {
  if (!resolvedOrgId) return;
  setDownloading(true);
  try {
    const accounts = msalInstance.getAllAccounts();
    const tokenRes = await msalInstance.acquireTokenSilent({
      ...loginRequest,
      account: accounts[0],
    });
    const res = await fetch(
      `${API_BASE}/v2/agent/download?orgId=${resolvedOrgId}`,
      { headers: { Authorization: `Bearer ${tokenRes.accessToken}` } }
    );
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const blob = await res.blob();
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = `KryossAgent-${slugify(org?.name ?? 'agent')}.exe`;
    a.click();
    URL.revokeObjectURL(a.href);
    toast.success('Agent downloaded');
  } catch (err: any) {
    toast.error(`Download failed: ${err.message}`);
  } finally {
    setDownloading(false);
  }
};
```

Add the button in the header actions area (after the Edit button):

```tsx
<Can permission="agents:download">
  <Button
    variant="outline"
    size="sm"
    disabled={downloading}
    onClick={handleDownloadAgent}
  >
    {downloading ? (
      <Loader2 className="mr-1.5 h-4 w-4 animate-spin" />
    ) : (
      <Download className="mr-1.5 h-4 w-4" />
    )}
    Download Agent
  </Button>
</Can>
```

- [ ] **Step 2: Add useState import if not present**

```tsx
import { useState } from 'react';
```

- [ ] **Step 3: Build to verify**

Run: `cd KryossPortal && npx tsc --noEmit`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add KryossPortal/src/components/org-detail/OrgDetail.tsx
git commit -m "feat(portal): download agent button on org detail page"
```

---

### Task 8: Publish Agent + Upload Template to Blob Storage

- [ ] **Step 1: Publish the agent**

Run: `cd KryossAgent && .\publish.ps1`
Expected: `KryossAgent.exe` in `publish/` directory (~66MB with embedded PsExec).

- [ ] **Step 2: Create blob storage container and upload template**

```bash
# Create container
az storage container create --name kryoss-agent-templates --account-name <storage-account> --auth-mode login

# Upload template
az storage blob upload --container-name kryoss-agent-templates --name latest/KryossAgent.exe --file KryossAgent/publish/KryossAgent.exe --account-name <storage-account> --auth-mode login --overwrite
```

- [ ] **Step 3: Deploy API**

Run: `cd KryossApi/src/KryossApi && func azure functionapp publish func-kryoss --dotnet-isolated`

- [ ] **Step 4: Deploy Portal**

Run: `cd KryossPortal && npm run build && npx swa deploy dist --env production --app-name swa-kryoss-portal --resource-group rg-kryoss`

- [ ] **Step 5: Commit all remaining changes**

```bash
git add -A
git commit -m "chore: agent self-contained evolution — all components ready"
```

---

### Task 9: End-to-End Verification

- [ ] **Step 1: Test generic binary (unpatched)**

```
.\KryossAgent.exe
```
Expected: Shows generic banner ("Kryoss Security Agent v1.0.0"), prompts for enrollment code.

- [ ] **Step 2: Test --scan mode**

```
.\KryossAgent.exe --scan --discover-arp --code XXXX --threads 3
```
Expected: Discovers hosts via ARP, confirms target list, deploys self, scans in parallel (3 threads), prints summary.

- [ ] **Step 3: Test portal download**

In portal: Organization → "Download Agent" button.
Expected: Downloads `KryossAgent-cox-science-museum.exe`. Run it — should show branded banner and auto-enroll.

- [ ] **Step 4: Test patched binary auto-detect**

Run the downloaded .exe:
```
.\KryossAgent-cox-science-museum.exe
```
Expected: Shows branded banner ("TeamLogic IT — Security Assessment / Cox Science Museum"), auto-enrolls, scans, uploads, shows score.

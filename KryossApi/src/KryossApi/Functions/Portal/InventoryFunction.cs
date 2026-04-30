using System.Net;
using System.Text.Json;
using KryossApi.Data;
using KryossApi.Middleware;
using KryossApi.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;

namespace KryossApi.Functions.Portal;

/// <summary>
/// Org-level hardware and software inventory endpoints.
/// </summary>
[RequirePermission("machines:read")]
public class InventoryFunction
{
    private readonly KryossDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IBlobPayloadService? _blob;

    public InventoryFunction(KryossDbContext db, ICurrentUserService user, IBlobPayloadService? blob = null)
    {
        _db = db;
        _user = user;
        _blob = blob;
    }

    // ── Hardware Inventory ──

    [Function("Inventory_Hardware")]
    public async Task<HttpResponseData> Hardware(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/inventory/hardware")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var orgIdStr = query["organizationId"];

        Guid? orgId = Guid.TryParse(orgIdStr, out var parsed) ? parsed : _user.OrganizationId;
        if (orgId is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId is required" });
            return bad;
        }

        var machines = await _db.Machines
            .Where(m => m.OrganizationId == orgId.Value && m.IsActive)
            .OrderBy(m => m.Hostname)
            .Select(m => new HardwareItem
            {
                Id = m.Id,
                Hostname = m.Hostname,
                OsName = m.OsName,
                OsVersion = m.OsVersion,
                CpuName = m.CpuName,
                CpuCores = m.CpuCores,
                RamGb = m.RamGb,
                DiskType = m.DiskType,
                DiskSizeGb = m.DiskSizeGb,
                DiskFreeGb = m.DiskFreeGb,
                Manufacturer = m.Manufacturer,
                Model = m.Model,
                SerialNumber = m.SerialNumber,
                TpmPresent = m.TpmPresent,
                TpmVersion = m.TpmVersion,
                SecureBoot = m.SecureBoot,
                Bitlocker = m.Bitlocker,
                IpAddress = m.IpAddress,
                MacAddress = m.MacAddress,
                LastSeenAt = m.LastSeenAt
            })
            .ToListAsync();

        // Load per-machine disk inventory
        var machineIds = machines.Select(m => m.Id).ToList();
        var allDisks = await _db.MachineDisks
            .Where(d => machineIds.Contains(d.MachineId))
            .OrderBy(d => d.DriveLetter)
            .ToListAsync();
        var disksByMachine = allDisks.GroupBy(d => d.MachineId).ToDictionary(g => g.Key, g => g.ToList());

        // Attach disks to each machine
        foreach (var m in machines)
        {
            m.Disks = disksByMachine.TryGetValue(m.Id, out var disks)
                ? disks.Select(d => new DiskEntry
                {
                    DriveLetter = d.DriveLetter,
                    Label = d.Label,
                    DiskType = d.DiskType,
                    TotalGb = d.TotalGb,
                    FreeGb = d.FreeGb,
                    FileSystem = d.FileSystem,
                }).ToList()
                : [];
        }

        // Compute Win11 readiness — only for workstations, not servers
        foreach (var m in machines)
        {
            var isServer = m.OsName != null &&
                (m.OsName.Contains("Server", StringComparison.OrdinalIgnoreCase) ||
                 m.OsName.Contains("Domain Controller", StringComparison.OrdinalIgnoreCase));

            if (isServer)
            {
                // Servers don't need Win11 readiness check
                m.Win11Ready = null; // null = not applicable
                m.Win11Blockers = [];
                continue;
            }

            // Already running Windows 11 = ready
            var alreadyWin11 = m.OsName != null &&
                m.OsName.Contains("Windows 11", StringComparison.OrdinalIgnoreCase);

            if (alreadyWin11)
            {
                m.Win11Ready = true;
                m.Win11Blockers = [];
                continue;
            }

            // Windows 10 — check hardware requirements
            var blockers = new List<string>();
            if (m.TpmPresent != true || m.TpmVersion != "2.0")
                blockers.Add("TPM 2.0 required");
            if (m.SecureBoot != true)
                blockers.Add("Secure Boot required");
            if (m.RamGb is null || m.RamGb < 4)
                blockers.Add("RAM >= 4 GB required");
            if (m.DiskSizeGb is null || m.DiskSizeGb < 64)
                blockers.Add("Disk >= 64 GB required");

            m.Win11Ready = blockers.Count == 0;
            m.Win11Blockers = blockers;
        }

        var workstations = machines.Where(m => m.Win11Ready != null).ToList();
        var win11Ready = workstations.Count(m => m.Win11Ready == true);
        var servers = machines.Count(m => m.Win11Ready == null);
        var result = new
        {
            total = machines.Count,
            workstations = workstations.Count,
            servers,
            win11Ready,
            win11NotReady = workstations.Count - win11Ready,
            items = machines
        };

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);
        return response;
    }

    // ── Software Inventory ──

    [Function("Inventory_Software")]
    public async Task<HttpResponseData> Software(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v2/inventory/software")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var orgIdStr = query["organizationId"];

        Guid? orgId = Guid.TryParse(orgIdStr, out var parsed) ? parsed : _user.OrganizationId;
        if (orgId is null)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new { error = "organizationId is required" });
            return bad;
        }

        var machineIds = await _db.Machines
            .Where(m => m.OrganizationId == orgId.Value && m.IsActive)
            .Select(m => new { m.Id, m.Hostname })
            .ToListAsync();

        var machineHostnames = machineIds.ToDictionary(m => m.Id, m => m.Hostname);
        var ids = machineIds.Select(m => m.Id).ToList();

        var swRows = await _db.MachineSoftware
            .Include(ms => ms.Software)
            .Where(ms => ids.Contains(ms.MachineId) && ms.RemovedAt == null)
            .Select(ms => new
            {
                ms.MachineId,
                ms.Software.Name,
                ms.Software.Publisher,
                ms.Version,
                ms.Software.Category,
            })
            .ToListAsync();

        var softwareMap = new Dictionary<string, SoftwareAggregation>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in swRows)
        {
            var key = $"{row.Name}|||{row.Publisher}|||{row.Version}";
            if (!softwareMap.TryGetValue(key, out var agg))
            {
                agg = new SoftwareAggregation
                {
                    Name = row.Name,
                    Publisher = row.Publisher,
                    Version = row.Version,
                    Category = row.Category ?? Categorize(row.Name, row.Publisher),
                    Machines = []
                };
                softwareMap[key] = agg;
            }
            var hostname = machineHostnames.GetValueOrDefault(row.MachineId, "unknown");
            if (!agg.Machines.Contains(hostname, StringComparer.OrdinalIgnoreCase))
                agg.Machines.Add(hostname);
        }

        var items = softwareMap.Values
            .OrderBy(s => s.Name)
            .Select(s => new
            {
                s.Name,
                s.Publisher,
                s.Version,
                machineCount = s.Machines.Count,
                s.Category,
                machines = s.Machines.OrderBy(h => h).ToList()
            })
            .ToList();

        var licensed = items.Count(i => i.Category == "licensed");
        var remoteAccess = items.Count(i => i.Category == "remote_access");
        var suspicious = items.Count(i => i.Category == "suspicious");

        var result = new
        {
            total = items.Count,
            licensed,
            remoteAccess,
            suspicious,
            items
        };

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result);
        return response;
    }

    // ── Categorization ──

    private static string Categorize(string name, string? publisher)
    {
        var combined = $"{name} {publisher}";

        if (MatchesAny(combined, SuspiciousPatterns))
            return "suspicious";
        if (MatchesAny(combined, RemoteAccessPatterns))
            return "remote_access";
        if (MatchesAny(combined, LicensedPatterns))
            return "licensed";

        return "standard";
    }

    private static bool MatchesAny(string text, string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            if (text.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    // ── Commercial software patterns (600+ products from master list) ──

    private static readonly string[] LicensedPatterns =
    [
        // Microsoft Office & 365
        "Microsoft Office", "Microsoft 365", "Microsoft Visio", "Microsoft Project",
        "Microsoft Access", "Microsoft Publisher", "Microsoft InfoPath",
        // Microsoft Server & Platform
        "SQL Server", "SharePoint Server", "Exchange Server", "BizTalk Server",
        "System Center", "Endpoint Configuration Manager",
        // Microsoft Business
        "Dynamics 365", "Dynamics GP", "Dynamics NAV", "Dynamics AX", "Dynamics CRM",
        "Power BI Desktop",
        // Other Office suites
        "WordPerfect", "WPS Office Professional", "Kingsoft Office", "Hancom Office",
        // Collaboration
        "Slack", "Zoom", "Webex", "GoTo Meeting", "GoTo Webinar", "RingCentral",
        "BlueJeans",
        // Email
        "HCL Notes", "IBM Notes", "Lotus Notes", "eM Client", "Mailbird",
        // Adobe
        "Adobe Acrobat", "Adobe Creative Cloud", "Adobe Connect", "Adobe ColdFusion",
        "Adobe Captivate", "Adobe FrameMaker", "Adobe RoboHelp", "Adobe Photoshop",
        "Adobe Illustrator", "Adobe InDesign", "Adobe Premiere", "Adobe After Effects",
        "Adobe Media Encoder", "Adobe Animate", "Adobe Dreamweaver", "Adobe XD",
        "Adobe Lightroom", "Adobe Audition", "Adobe Substance",
        // Graphics
        "CorelDRAW", "Corel Painter", "Corel PaintShop", "Affinity Designer",
        "Affinity Photo", "Affinity Publisher", "Figma",
        // Video
        "DaVinci Resolve Studio", "Camtasia", "VEGAS Pro", "Pinnacle Studio",
        "PowerDirector", "PowerDVD", "Movavi Video", "Wondershare Filmora",
        "Wirecast", "vMix", "Avid Media Composer",
        // Screen capture
        "SnagIt", "Snagit", "Bandicam", "Loom",
        // Audio
        "Pro Tools", "Cubase", "FL Studio", "Ableton Live", "Studio One",
        // 3D
        "Autodesk Maya", "Autodesk 3ds Max", "Cinema 4D", "ZBrush", "Houdini",
        "KeyShot", "V-Ray",
        // Desktop publishing
        "QuarkXPress",
        // Photo
        "Capture One", "ACDSee", "DxO PhotoLab",
        // OCR & PDF
        "ABBYY FineReader", "OmniPage", "Readiris",
        "Nuance Power PDF", "Kofax Power PDF", "Tungsten Power PDF",
        "Foxit PDF Editor", "Foxit PhantomPDF", "Nitro Pro", "Nitro PDF",
        "PDF-XChange Editor",
        // Diagramming
        "MindManager", "SmartDraw", "EdrawMax", "iGrafx", "ConceptDraw",
        // CAD
        "AutoCAD", "Autodesk Inventor", "Autodesk Fusion", "Autodesk Revit",
        "Autodesk Navisworks", "SOLIDWORKS", "CATIA", "Siemens NX",
        "Siemens Solid Edge", "PTC Creo", "PTC Windchill", "BricsCAD",
        "DraftSight", "TurboCAD", "MicroStation", "SketchUp Pro", "Rhinoceros",
        // BIM
        "ArchiCAD", "Vectorworks", "Chief Architect", "Tekla Structures",
        "Bluebeam Revu", "PlanSwift", "On-Screen Takeoff",
        // Engineering
        "ANSYS", "COMSOL Multiphysics", "MATLAB", "Simulink", "Mathcad",
        "Maple", "Abaqus", "Altair HyperWorks", "Simcenter", "MSC Nastran",
        "MSC Adams", "Wolfram Mathematica",
        // EDA
        "Altium Designer", "Cadence OrCAD", "Mentor Graphics", "LabVIEW",
        // GIS
        "ArcGIS", "MapInfo Pro", "Global Mapper", "Trimble",
        // Accounting SMB
        "QuickBooks", "Sage 50", "Sage 100", "Sage 200", "Sage 300", "Sage X3",
        "Sage Intacct", "MYOB", "Reckon Accounts", "AccountEdge",
        // Accounting Enterprise
        "SAP Business One", "SAP GUI", "SAP Logon", "SAP Business Client",
        "Oracle E-Business", "Oracle JD Edwards", "Oracle PeopleSoft",
        "Epicor", "IFS Applications", "Acumatica", "SYSPRO", "Exact Globe",
        "Unit4 ERP", "Deltek", "Infor LN", "Infor M3",
        // Tax
        "TurboTax", "H&R Block", "Lacerte Tax", "ProSeries Tax", "Drake Tax",
        "UltraTax", "GoSystem Tax", "CCH Axcess", "CCH ProSystem", "ATX Tax",
        // CRM
        "Salesforce", "HubSpot", "Act! Premium", "GoldMine", "SugarCRM",
        "Maximizer CRM", "Sage CRM", "Infor CRM", "Freshsales",
        // HR
        "ADP Workforce", "ADP Run", "Kronos", "UKG", "Ceridian Dayforce",
        "Paychex", "SAP SuccessFactors", "Oracle HCM",
        // BI
        "Tableau Desktop", "Tableau Prep", "Qlik Sense", "QlikView",
        "SAP BusinessObjects", "SAP Crystal Reports", "IBM Cognos",
        "MicroStrategy", "Sisense", "Alteryx Designer", "TIBCO Spotfire",
        "SAS Enterprise", "Domo Workbench",
        // Dev tools
        "Visual Studio Enterprise", "Visual Studio Professional",
        "JetBrains IntelliJ", "JetBrains PyCharm", "JetBrains WebStorm",
        "JetBrains ReSharper", "JetBrains Rider", "JetBrains CLion",
        "JetBrains GoLand", "JetBrains DataGrip", "JetBrains PhpStorm",
        "JetBrains RubyMine", "JetBrains dotPeek", "JetBrains dotTrace",
        "Embarcadero RAD Studio", "Embarcadero Delphi",
        "Unity Editor", "Unity Hub", "Unreal Engine",
        // DB tools
        "Oracle Database", "Oracle SQL Developer", "IBM Db2",
        "Toad for Oracle", "Toad for SQL", "Toad for MySQL",
        "Navicat", "DBeaver Enterprise", "Aqua Data Studio",
        "SQL Toolbelt", "Redgate SQL", "ApexSQL", "ERwin",
        // Virtualization
        "VMware Workstation", "VMware Horizon", "VMware vSphere",
        "Citrix Workspace", "Citrix Receiver", "Citrix Virtual Apps",
        "Docker Desktop",
        // Source control
        "GitKraken", "Perforce Helix", "SmartGit",
        "Azure DevOps Server", "Octopus Deploy",
        // API & Testing
        "Postman", "SoapUI Pro", "ReadyAPI", "LoadRunner",
        "Unified Functional Testing", "Ranorex Studio", "TestComplete",
        "Telerik Test Studio", "Parasoft", "Tricentis Tosca", "Katalon Studio",
        // Text editors
        "Sublime Text", "UltraEdit", "EmEditor Professional", "TextPad",
        // Terminal
        "SecureCRT", "Xshell", "MobaXterm Professional", "Royal TS",
        "Remote Desktop Manager",
        // File transfer
        "CuteFTP", "WS_FTP", "SmartFTP", "Globalscape EFT",
        "MOVEit Transfer", "GoAnywhere MFT",
        // Compression
        "WinRAR", "WinZip", "PKZIP",
        // System utilities
        "TreeSize Professional", "Beyond Compare", "Acronis Disk Director",
        "Total Commander", "Directory Opus",
        // Printing
        "PaperCut", "Pharos Print", "Equitrac", "uniFLOW",
        "BarTender", "NiceLabel", "ZebraDesigner", "DYMO Label",
        // Password management
        "LastPass", "1Password", "Dashlane", "Keeper Password",
        "CyberArk", "Thycotic",
        // Endpoint security
        "Symantec Endpoint", "Norton", "McAfee", "Trellix",
        "Trend Micro", "Kaspersky", "Bitdefender", "ESET",
        "Sophos", "Webroot", "F-Secure", "WatchGuard Endpoint",
        "Panda", "Microsoft Defender for Endpoint",
        "Malwarebytes", "Cylance", "CrowdStrike", "SentinelOne",
        "Carbon Black", "Cortex XDR", "FortiClient", "Check Point Endpoint",
        "Huntress", "Deep Instinct",
        // Email security
        "Proofpoint", "Mimecast", "Barracuda",
        // VPN
        "Cisco AnyConnect", "Cisco Secure Client", "GlobalProtect",
        "FortiClient VPN", "SonicWall NetExtender", "SonicWall Global VPN",
        "WatchGuard Mobile VPN", "Junos Pulse", "Pulse Secure",
        "Ivanti Secure Access", "Check Point VPN", "Zscaler", "Netskope",
        // Encryption
        "Symantec Encryption", "PGP Desktop", "McAfee Drive Encryption",
        "Sophos SafeGuard", "WinMagic SecureDoc",
        "Digital Guardian", "Forcepoint DLP",
        // Backup
        "Veeam", "Acronis", "Veritas Backup", "Commvault",
        "Arcserve", "Datto", "StorageCraft", "ShadowProtect",
        "Zerto", "Carbonite", "CrashPlan", "Code42",
        "Macrium Reflect", "NAKIVO", "Altaro", "Hornetsecurity VM Backup",
        "MSP360", "CloudBerry", "NovaBACKUP", "BackupAssist",
        "Unitrends", "Rubrik", "Cohesity", "Dell EMC Avamar",
        "Dell EMC NetWorker", "IBM Spectrum Protect", "Druva inSync",
        // Monitoring
        "SolarWinds", "PRTG", "ManageEngine", "Nagios",
        "WhatsUp Gold", "Datadog", "New Relic", "Auvik",
        "LogicMonitor", "Splunk", "Elastic Agent", "Zabbix",
        // Patch management
        "Ivanti Patch", "PDQ Deploy", "PDQ Inventory", "Automox",
        // ITSM
        "ManageEngine ServiceDesk", "BMC", "SysAid", "Freshservice",
        // Asset management
        "Lansweeper", "Snow Inventory", "Snow License", "Flexera",
        "Ivanti Neurons", "Qualys Cloud Agent",
        // Healthcare
        "Epic Hyperspace", "Epic Hyperdrive", "Cerner", "MEDITECH",
        "eClinicalWorks", "Allscripts", "NextGen", "athenahealth",
        "Dentrix", "Eaglesoft", "Carestream", "Planmeca",
        // Legal
        "LexisNexis", "Westlaw", "Clio", "MyCase", "PracticePanther",
        "Amicus Attorney", "Time Matters", "ProLaw", "Tabs3",
        "PCLaw", "Smokeball", "Relativity",
        // Real estate
        "Yardi", "AppFolio", "RealPage", "MRI Software", "Buildium",
        "Rent Manager", "CoStar",
        // Hospitality
        "Oracle MICROS", "NCR Aloha", "Toast POS", "Lightspeed POS",
        // Education
        "Blackboard", "Respondus LockDown", "Turnitin",
        // Scientific
        "LabWare", "Waters Empower", "Chromeleon", "OpenLAB",
        "ChemDraw", "GraphPad Prism", "OriginPro", "IBM SPSS",
        "Stata", "Minitab", "JMP", "NVivo", "ATLAS.ti", "EndNote",
        // Supply chain
        "Fishbowl", "inFlow Inventory", "Cin7", "DEAR Inventory",
        // Estimating
        "Sage Estimating", "ProEst", "HeavyBid",
        // Billing
        "FreshBooks", "Harvest Desktop", "Toggl Track", "BQE Core",
    ];

    private static readonly string[] RemoteAccessPatterns =
    [
        "TeamViewer", "AnyDesk", "ScreenConnect", "ConnectWise Control",
        "ConnectWise Automate", "LabTech", "Kaseya", "VSA X",
        "Datto RMM", "CentraStage", "NinjaRMM", "NinjaOne",
        "N-able N-central", "N-able Windows Agent", "SolarWinds MSP RMM",
        "Atera Agent", "Splashtop", "LogMeIn", "GoTo Resolve",
        "GoToAssist", "BeyondTrust Remote", "Bomgar",
        "RealVNC", "VNC Connect", "DameWare", "RemotePC",
        "Zoho Assist", "ISL Online", "Pulseway", "Action1",
        "Syncro", "SuperOps", "ITarian Remote", "Naverisk",
        "UltraVNC", "TightVNC", "Ammyy", "Supremo", "Radmin",
        "GoToMyPC", "Chrome Remote Desktop",
    ];

    private static readonly string[] SuspiciousPatterns =
    [
        // P2P / Torrent
        "BitTorrent", "uTorrent", "qBittorrent", "Vuze", "Deluge",
        "Transmission", "Tixati",
        // Crypto mining
        "NiceHash", "XMRig", "PhoenixMiner", "CGMiner", "T-Rex",
        "lolMiner", "NBMiner", "Claymore",
        // Hacking / pentest tools (on non-security workstations)
        "Wireshark", "Nmap", "Metasploit", "Burp Suite",
        "Cain", "John the Ripper", "Hashcat", "Mimikatz",
        "Cobalt Strike", "BloodHound", "Rubeus", "SharpHound",
        "LaZagne", "PowerSploit",
        // PUPs
        "CCleaner", "IObit", "Avast Free", "AVG Free",
        "Hola VPN", "Hotspot Shield Free", "Brave Browser",
        // Unauthorized VPN
        "Psiphon", "Lantern", "Windscribe Free",
    ];

    // ── DTOs ──

    private class HardwareItem
    {
        public Guid Id { get; set; }
        public string Hostname { get; set; } = null!;
        public string? OsName { get; set; }
        public string? OsVersion { get; set; }
        public string? CpuName { get; set; }
        public short? CpuCores { get; set; }
        public short? RamGb { get; set; }
        public string? DiskType { get; set; }
        public int? DiskSizeGb { get; set; }
        public decimal? DiskFreeGb { get; set; }
        public string? Manufacturer { get; set; }
        public string? Model { get; set; }
        public string? SerialNumber { get; set; }
        public bool? TpmPresent { get; set; }
        public string? TpmVersion { get; set; }
        public bool? SecureBoot { get; set; }
        public bool? Bitlocker { get; set; }
        public string? IpAddress { get; set; }
        public string? MacAddress { get; set; }
        public DateTime? LastSeenAt { get; set; }
        public bool? Win11Ready { get; set; }
        public List<string> Win11Blockers { get; set; } = [];
        public List<DiskEntry> Disks { get; set; } = [];
    }

    private class DiskEntry
    {
        public string DriveLetter { get; set; } = null!;
        public string? Label { get; set; }
        public string? DiskType { get; set; }
        public int? TotalGb { get; set; }
        public decimal? FreeGb { get; set; }
        public string? FileSystem { get; set; }
    }

    private class SoftwareAggregation
    {
        public string Name { get; set; } = null!;
        public string? Publisher { get; set; }
        public string? Version { get; set; }
        public string Category { get; set; } = "standard";
        public List<string> Machines { get; set; } = [];
    }
}

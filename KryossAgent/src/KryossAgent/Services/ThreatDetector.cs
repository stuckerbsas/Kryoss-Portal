using System.Diagnostics;
using KryossAgent.Models;
using Microsoft.Win32;

namespace KryossAgent.Services;

/// <summary>
/// Scans for malware, spyware, hacking tools, and PUPs via two vectors:
/// registry-installed software and running processes. 343+ threat signatures.
/// </summary>
public static class ThreatDetector
{
    // ── Threat signature definition ──

    private record struct Sig(string Pattern, string ThreatName, int Category, string Severity, string Vector);

    // Category names by number
    private static readonly string[] CategoryNames =
    [
        "",                          // 0 unused
        "Browser Hijacker",          // 1
        "Adware",                    // 2
        "PUP/Fake Optimizer",        // 3
        "Stalkerware",               // 4
        "Keylogger",                 // 5
        "Employee Monitoring",       // 6
        "Remote Access Trojan",      // 7
        "C2/Hacking Tool",           // 8
        "Cryptominer",               // 9
        "Ransomware",                // 10
        "Fake Antivirus",            // 11
        "Loader/Stealer"             // 12
    ];

    // ── Signature database (343+ entries) ──
    // Vector: "registry" = DisplayName match, "process" = process name match, "both" = either

    private static readonly Sig[] Signatures =
    [
        // ═══════════════════════════════════════════════════════════════
        // Category 1 — Browser Hijackers (low)
        // ═══════════════════════════════════════════════════════════════
        new("Ask Toolbar", "Ask Toolbar", 1, "low", "registry"),
        new("AskBar", "Ask Toolbar", 1, "low", "process"),
        new("Conduit Toolbar", "Conduit Toolbar", 1, "low", "registry"),
        new("Conduit", "Conduit Toolbar", 1, "low", "process"),
        new("Search Protect", "Search Protect by Conduit", 1, "low", "both"),
        new("CltMngSvc", "Search Protect by Conduit", 1, "low", "process"),
        new("SPSetup", "Search Protect by Conduit", 1, "low", "process"),
        new("Babylon Toolbar", "Babylon Toolbar", 1, "low", "registry"),
        new("BabylonToolbar", "Babylon Toolbar", 1, "low", "process"),
        new("Delta Toolbar", "Delta Toolbar", 1, "low", "registry"),
        new("DeltaTB", "Delta Toolbar", 1, "low", "process"),
        new("Snap.do", "Snap.do", 1, "low", "both"),
        new("SnapDo", "Snap.do", 1, "low", "process"),
        new("MyWebSearch", "MyWebSearch", 1, "low", "both"),
        new("Trovi", "Trovi Search", 1, "low", "both"),
        new("BonziBuddy", "BonziBuddy", 1, "low", "both"),
        new("BonziBDY", "BonziBuddy", 1, "low", "process"),
        new("Web Companion", "Web Companion (Adaware)", 1, "low", "both"),
        new("WebCompanion", "Web Companion (Adaware)", 1, "low", "process"),
        new("SafeFinder", "SafeFinder", 1, "low", "both"),
        new("CoolWebSearch", "CoolWebSearch", 1, "low", "both"),
        new("MySearch", "MySearchDial", 1, "low", "both"),
        new("SearchDial", "MySearchDial", 1, "low", "process"),
        new("Omnibox", "Omnibox Hijacker", 1, "low", "registry"),
        new("Browser Guard", "Browser Guard (PUP)", 1, "low", "registry"),
        new("SearchApp", "SearchApp Hijacker", 1, "low", "registry"),
        new("sweetim", "SweetIM", 1, "low", "both"),
        new("SweetPacks", "SweetPacks Toolbar", 1, "low", "both"),
        new("Coupon Printer", "Coupon Printer for Windows", 1, "low", "both"),
        new("DefaultTab", "DefaultTab", 1, "low", "both"),
        new("Iminent Toolbar", "Iminent Toolbar", 1, "low", "both"),
        new("iminent", "Iminent Toolbar", 1, "low", "process"),
        new("Mindspark", "Mindspark Toolbar", 1, "low", "both"),
        new("MindsparkTB", "Mindspark Toolbar", 1, "low", "process"),
        new("Wajam", "Wajam", 1, "low", "both"),
        new("WajamUpdater", "Wajam", 1, "low", "process"),

        // ═══════════════════════════════════════════════════════════════
        // Category 2 — Adware (low)
        // ═══════════════════════════════════════════════════════════════
        new("OpenCandy", "OpenCandy", 2, "low", "both"),
        new("Superfish", "Superfish VisualDiscovery", 2, "low", "both"),
        new("VisualDiscovery", "Superfish VisualDiscovery", 2, "low", "process"),
        new("InstallCore", "InstallCore", 2, "low", "both"),
        new("Crossrider", "Crossrider Adware", 2, "low", "both"),
        new("Yontoo", "Yontoo", 2, "low", "both"),
        new("DealPly", "DealPly", 2, "low", "both"),
        new("ShopperPro", "ShopperPro", 2, "low", "both"),
        new("Fireball", "Fireball", 2, "medium", "both"),
        new("DNS Unlocker", "DNS Unlocker", 2, "low", "both"),
        new("DNSUnlocker", "DNS Unlocker", 2, "low", "process"),
        new("Hola VPN", "Hola VPN (P2P abuse)", 2, "low", "registry"),
        new("hola_svc", "Hola VPN (P2P abuse)", 2, "low", "process"),
        new("HolaSvc", "Hola VPN (P2P abuse)", 2, "low", "process"),
        new("BrowseFox", "BrowseFox", 2, "low", "both"),
        new("eSafe", "eSafe", 2, "low", "both"),
        new("eType", "eType", 2, "low", "both"),
        new("Vuze Toolbar", "Vuze Toolbar", 2, "low", "registry"),
        new("VuzeToolbar", "Vuze Toolbar", 2, "low", "process"),
        new("Multiplug", "Multiplug", 2, "low", "both"),
        new("Bettersurf", "BetterSurf", 2, "low", "both"),
        new("CinemaPlus", "Cinema Plus", 2, "low", "both"),
        new("JollyWallet", "JollyWallet", 2, "low", "both"),
        new("PriceMeter", "PriceMeter", 2, "low", "both"),
        new("Genieo", "Genieo", 2, "low", "both"),
        new("GlobalUpdate", "GlobalUpdate", 2, "low", "both"),
        new("Savepath", "Savepath Deals", 2, "low", "both"),
        new("VOPackage", "VO Package", 2, "low", "both"),
        new("Tuto4PC", "Tuto4PC", 2, "low", "both"),
        new("Pricora", "Pricora", 2, "low", "both"),
        new("LyricsGet", "LyricsGet", 2, "low", "both"),
        new("InboxAce", "InboxAce Toolbar", 2, "low", "both"),
        new("CouponBar", "CouponBar", 2, "low", "both"),

        // ═══════════════════════════════════════════════════════════════
        // Category 3 — Fake Optimizers / PUPs (low)
        // ═══════════════════════════════════════════════════════════════
        new("MyPC Backup", "MyPC Backup", 3, "low", "both"),
        new("PC Optimizer Pro", "PC Optimizer Pro", 3, "low", "both"),
        new("PCOptimizerPro", "PC Optimizer Pro", 3, "low", "process"),
        new("Reimage Repair", "Reimage Repair", 3, "low", "both"),
        new("ReimageRepair", "Reimage Repair", 3, "low", "process"),
        new("SlimCleaner", "SlimCleaner", 3, "low", "both"),
        new("SpeedUpMyPC", "SpeedUpMyPC", 3, "low", "both"),
        new("WinZip Driver Updater", "WinZip Driver Updater", 3, "low", "registry"),
        new("WinZipDU", "WinZip Driver Updater", 3, "low", "process"),
        new("Segurazo", "Segurazo Antivirus (PUP)", 3, "low", "both"),
        new("ByteFence", "ByteFence", 3, "low", "both"),
        new("IObit", "IObit (PUP bundle)", 3, "low", "registry"),
        new("iobitun", "IObit (PUP bundle)", 3, "low", "process"),
        new("CCleaner", "CCleaner (flagged PUP)", 3, "low", "both"),
        new("Advanced SystemCare", "Advanced SystemCare", 3, "low", "both"),
        new("Registry Mechanic", "Registry Mechanic", 3, "low", "both"),
        new("WinFixer", "WinFixer", 3, "low", "both"),
        new("ErrorSafe", "ErrorSafe", 3, "low", "both"),
        new("DriverUpdate", "DriverUpdate (PUP)", 3, "low", "both"),
        new("OneSafe PC Cleaner", "OneSafe PC Cleaner", 3, "low", "both"),
        new("TweakBit", "TweakBit", 3, "low", "both"),
        new("Auslogics BoostSpeed", "Auslogics BoostSpeed (PUP)", 3, "low", "both"),
        new("System Mechanic", "System Mechanic (PUP)", 3, "low", "registry"),
        new("WinTonic", "WinTonic", 3, "low", "both"),
        new("Driver Tonic", "Driver Tonic", 3, "low", "both"),
        new("PC Accelerate", "PC Accelerate", 3, "low", "both"),
        new("Smart PC Care", "Smart PC Care", 3, "low", "both"),
        new("Total PC Cleaner", "Total PC Cleaner", 3, "low", "both"),
        new("Auto PC Speedup", "Auto PC Speedup", 3, "low", "both"),
        new("PC HelpSoft", "PC HelpSoft", 3, "low", "both"),
        new("Uniblue", "Uniblue (PUP suite)", 3, "low", "both"),
        new("DriverEasy", "Driver Easy (PUP)", 3, "low", "both"),

        // ═══════════════════════════════════════════════════════════════
        // Category 4 — Stalkerware (high)
        // ═══════════════════════════════════════════════════════════════
        new("FlexiSpy", "FlexiSPY", 4, "high", "both"),
        new("FlexiSPY", "FlexiSPY", 4, "high", "process"),
        new("mSpy", "mSpy", 4, "high", "both"),
        new("Hoverwatch", "Hoverwatch", 4, "high", "both"),
        new("iKeyMonitor", "iKeyMonitor", 4, "high", "both"),
        new("Spyera", "Spyera", 4, "high", "both"),
        new("WebWatcher", "WebWatcher", 4, "high", "both"),
        new("KidsGuard", "KidsGuard Pro / MoniVisor", 4, "high", "both"),
        new("MoniVisor", "KidsGuard Pro / MoniVisor", 4, "high", "both"),
        new("pcTattletale", "pcTattletale", 4, "high", "both"),
        new("Cocospy", "Cocospy", 4, "high", "both"),
        new("SpyHuman", "SpyHuman", 4, "high", "both"),
        new("XNSPY", "XNSPY", 4, "high", "both"),
        new("eyeZy", "eyeZy", 4, "high", "both"),
        new("Highster Mobile", "Highster Mobile", 4, "high", "both"),
        new("TheTruthSpy", "TheTruthSpy", 4, "high", "both"),
        new("SpyFone", "SpyFone", 4, "high", "both"),
        new("PhoneSheriff", "PhoneSheriff", 4, "high", "both"),
        new("Mobistealth", "Mobistealth", 4, "high", "both"),
        new("SpyBubble", "SpyBubble", 4, "high", "both"),
        new("Auto Forward", "Auto Forward Spy", 4, "high", "both"),
        new("OwnSpy", "OwnSpy", 4, "high", "both"),

        // ═══════════════════════════════════════════════════════════════
        // Category 5 — Keyloggers (high)
        // ═══════════════════════════════════════════════════════════════
        new("Ardamax", "Ardamax Keylogger", 5, "high", "both"),
        new("akl.exe", "Ardamax Keylogger", 5, "high", "process"),
        new("Spyrix", "Spyrix Keylogger", 5, "high", "both"),
        new("spyrix", "Spyrix Keylogger", 5, "high", "process"),
        new("REFOG", "REFOG Keylogger", 5, "high", "both"),
        new("refog", "REFOG Keylogger", 5, "high", "process"),
        new("Elite Keylogger", "Elite Keylogger", 5, "high", "both"),
        new("Revealer Keylogger", "Revealer Keylogger", 5, "high", "both"),
        new("rvlkl", "Revealer Keylogger", 5, "high", "process"),
        new("Perfect Keylogger", "Perfect Keylogger", 5, "high", "both"),
        new("KGB Spy", "KGB Spy", 5, "high", "both"),
        new("kgbspy", "KGB Spy", 5, "high", "process"),
        new("Actual Keylogger", "Actual Keylogger", 5, "high", "both"),
        new("All In One Keylogger", "All In One Keylogger", 5, "high", "both"),
        new("SniperSpy", "SniperSpy", 5, "high", "both"),
        new("Micro Keylogger", "Micro Keylogger", 5, "high", "both"),
        new("Iwantsoft", "Iwantsoft Keylogger", 5, "high", "both"),
        new("Gecko Monitor", "Gecko Monitor", 5, "high", "both"),
        new("NetBull", "NetBull Keylogger", 5, "high", "both"),
        new("Shadow Keylogger", "Shadow Keylogger", 5, "high", "process"),
        new("WinSpy", "WinSpy Keylogger", 5, "high", "both"),
        new("HomeKeyLogger", "Home KeyLogger", 5, "high", "both"),

        // ═══════════════════════════════════════════════════════════════
        // Category 6 — Employee Monitoring (info)
        // ═══════════════════════════════════════════════════════════════
        new("SpyAgent", "SpyAgent (SpyTech)", 6, "info", "both"),
        new("Realtime-Spy", "Realtime-Spy", 6, "info", "both"),
        new("RealtimeSpy", "Realtime-Spy", 6, "info", "process"),
        new("Veriato", "Veriato (Cerebral)", 6, "info", "both"),
        new("Teramind", "Teramind", 6, "info", "both"),
        new("teaborot", "Teramind", 6, "info", "process"),
        new("ActivTrak", "ActivTrak", 6, "info", "both"),
        new("StaffCop", "StaffCop", 6, "info", "both"),
        new("Kickidler", "Kickidler", 6, "info", "both"),
        new("InterGuard", "InterGuard", 6, "info", "both"),
        new("SentryPC", "SentryPC", 6, "info", "both"),
        new("Hubstaff", "Hubstaff", 6, "info", "both"),
        new("Time Doctor", "Time Doctor", 6, "info", "both"),
        new("DeskTime", "DeskTime", 6, "info", "both"),
        new("WorkPuls", "WorkPuls (Insightful)", 6, "info", "both"),
        new("Insightful", "Insightful (WorkPuls)", 6, "info", "both"),
        new("CurrentWare", "CurrentWare", 6, "info", "both"),
        new("EmpMonitor", "EmpMonitor", 6, "info", "both"),
        new("CleverControl", "CleverControl", 6, "info", "both"),
        new("Controlio", "Controlio", 6, "info", "both"),

        // ═══════════════════════════════════════════════════════════════
        // Category 7 — Remote Access Trojans (critical)
        // ═══════════════════════════════════════════════════════════════
        new("njRAT", "njRAT", 7, "critical", "both"),
        new("njrat", "njRAT", 7, "critical", "process"),
        new("Bladabindi", "njRAT (Bladabindi)", 7, "critical", "process"),
        new("DarkComet", "DarkComet RAT", 7, "critical", "both"),
        new("DarkCometRAT", "DarkComet RAT", 7, "critical", "process"),
        new("fynloski", "DarkComet RAT", 7, "critical", "process"),
        new("Poison Ivy", "Poison Ivy RAT", 7, "critical", "both"),
        new("poisonivy", "Poison Ivy RAT", 7, "critical", "process"),
        new("NanoCore", "NanoCore RAT", 7, "critical", "both"),
        new("NanoCoreRAT", "NanoCore RAT", 7, "critical", "process"),
        new("AsyncRAT", "AsyncRAT", 7, "critical", "both"),
        new("Async_RAT", "AsyncRAT", 7, "critical", "process"),
        new("Quasar", "Quasar RAT", 7, "critical", "both"),
        new("QuasarRAT", "Quasar RAT", 7, "critical", "process"),
        new("Warzone", "Warzone RAT (Ave Maria)", 7, "critical", "both"),
        new("WarzoneRAT", "Warzone RAT (Ave Maria)", 7, "critical", "process"),
        new("AveMaria", "Warzone RAT (Ave Maria)", 7, "critical", "process"),
        new("Remcos", "Remcos RAT", 7, "critical", "both"),
        new("remcos", "Remcos RAT", 7, "critical", "process"),
        new("NetWire", "NetWire RAT", 7, "critical", "both"),
        new("NWI Agent", "NetWire RAT", 7, "critical", "process"),
        new("Agent Tesla", "Agent Tesla", 7, "critical", "both"),
        new("AgentTesla", "Agent Tesla", 7, "critical", "process"),
        new("XWorm", "XWorm", 7, "critical", "both"),
        new("DCRat", "DCRat", 7, "critical", "both"),
        new("dcrat", "DCRat", 7, "critical", "process"),
        new("Gh0st", "Gh0st RAT", 7, "critical", "both"),
        new("gh0st", "Gh0st RAT", 7, "critical", "process"),
        new("Orcus", "Orcus RAT", 7, "critical", "both"),
        new("orcus", "Orcus RAT", 7, "critical", "process"),
        new("SpyNote", "SpyNote RAT", 7, "critical", "both"),
        new("CyberGate", "CyberGate RAT", 7, "critical", "both"),
        new("Blackshades", "Blackshades RAT", 7, "critical", "both"),
        new("bss_server", "Blackshades RAT", 7, "critical", "process"),
        new("LimeRAT", "LimeRAT", 7, "critical", "both"),
        new("Havoc", "Havoc C2 Agent", 7, "critical", "both"),
        new("SilverRAT", "SilverRAT", 7, "critical", "both"),
        new("VenomRAT", "VenomRAT", 7, "critical", "both"),
        new("Pandora", "Pandora RAT", 7, "critical", "both"),

        // ═══════════════════════════════════════════════════════════════
        // Category 8 — C2 / Hacking Tools
        // ═══════════════════════════════════════════════════════════════
        // Critical
        new("mimikatz", "Mimikatz", 8, "critical", "both"),
        new("sekurlsa", "Mimikatz (sekurlsa)", 8, "critical", "process"),
        new("Cobalt Strike", "Cobalt Strike", 8, "critical", "both"),
        new("cobaltstrike", "Cobalt Strike", 8, "critical", "process"),
        new("beacon", "Cobalt Strike Beacon", 8, "critical", "process"),
        new("Metasploit", "Metasploit", 8, "critical", "both"),
        new("msfconsole", "Metasploit", 8, "critical", "process"),
        new("msfvenom", "Metasploit", 8, "critical", "process"),
        new("meterpreter", "Metasploit Meterpreter", 8, "critical", "process"),
        new("BloodHound", "BloodHound", 8, "critical", "both"),
        new("SharpHound", "SharpHound (BloodHound collector)", 8, "critical", "both"),
        new("Rubeus", "Rubeus (Kerberos abuse)", 8, "critical", "both"),
        new("rubeus", "Rubeus (Kerberos abuse)", 8, "critical", "process"),
        new("Impacket", "Impacket", 8, "critical", "both"),
        new("smbexec", "Impacket smbexec", 8, "critical", "process"),
        new("wmiexec", "Impacket wmiexec", 8, "critical", "process"),
        new("secretsdump", "Impacket secretsdump", 8, "critical", "process"),
        new("CrackMapExec", "CrackMapExec", 8, "critical", "both"),
        new("crackmapexec", "CrackMapExec", 8, "critical", "process"),
        new("NetExec", "NetExec (CrackMapExec successor)", 8, "critical", "both"),
        new("nxc", "NetExec", 8, "critical", "process"),
        new("hashcat", "hashcat", 8, "critical", "both"),
        new("JuicyPotato", "JuicyPotato", 8, "critical", "both"),
        new("GodPotato", "GodPotato", 8, "critical", "both"),
        new("SweetPotato", "SweetPotato", 8, "critical", "both"),
        new("PrintSpoofer", "PrintSpoofer", 8, "critical", "both"),
        new("Certify", "Certify (AD CS abuse)", 8, "critical", "both"),
        new("Certipy", "Certipy (AD CS abuse)", 8, "critical", "both"),
        new("KrbRelay", "KrbRelay", 8, "critical", "both"),
        new("Covenant", "Covenant C2", 8, "critical", "both"),
        new("Brute Ratel", "Brute Ratel C4", 8, "critical", "both"),
        new("bruteratel", "Brute Ratel C4", 8, "critical", "process"),
        new("Sliver", "Sliver C2", 8, "critical", "both"),
        new("sliver-client", "Sliver C2", 8, "critical", "process"),
        new("sliver-server", "Sliver C2", 8, "critical", "process"),
        new("Empire", "PowerShell Empire", 8, "critical", "both"),
        new("Mythic", "Mythic C2", 8, "critical", "both"),
        new("PoshC2", "PoshC2", 8, "critical", "both"),
        new("LaZagne", "LaZagne (credential dump)", 8, "critical", "both"),
        new("lazagne", "LaZagne (credential dump)", 8, "critical", "process"),
        new("SharpUp", "SharpUp (privesc)", 8, "critical", "both"),
        new("Seatbelt", "Seatbelt (enumeration)", 8, "critical", "both"),
        new("ADRecon", "ADRecon", 8, "critical", "both"),

        // High
        new("Netcat", "Netcat", 8, "high", "both"),
        new("nc.exe", "Netcat", 8, "high", "process"),
        new("ncat", "Ncat (Netcat)", 8, "high", "process"),
        new("Chisel", "Chisel (tunnel)", 8, "high", "both"),
        new("chisel", "Chisel (tunnel)", 8, "high", "process"),
        new("ngrok", "ngrok (tunnel)", 8, "high", "both"),
        new("ProxyChains", "ProxyChains", 8, "high", "both"),
        new("proxychains", "ProxyChains", 8, "high", "process"),
        new("John the Ripper", "John the Ripper", 8, "high", "both"),
        new("john.exe", "John the Ripper", 8, "high", "process"),
        new("Responder", "Responder (LLMNR/NBT-NS)", 8, "high", "both"),
        new("responder", "Responder (LLMNR/NBT-NS)", 8, "high", "process"),
        new("PowerSploit", "PowerSploit", 8, "high", "both"),
        new("Invoke-Mimikatz", "PowerSploit Invoke-Mimikatz", 8, "high", "process"),
        new("Hydra", "THC-Hydra", 8, "high", "both"),
        new("hydra.exe", "THC-Hydra", 8, "high", "process"),
        new("SQLMap", "SQLMap", 8, "high", "both"),
        new("sqlmap", "SQLMap", 8, "high", "process"),
        new("Burp Suite", "Burp Suite", 8, "high", "registry"),
        new("BurpSuite", "Burp Suite", 8, "high", "process"),

        // Info — dual-use / legitimate tools
        // PsExec excluded — Kryoss agent uses it for remote deployment
        new("Nmap", "Nmap", 8, "info", "both"),
        new("nmap", "Nmap", 8, "info", "process"),
        new("zenmap", "Nmap Zenmap", 8, "info", "process"),
        new("Wireshark", "Wireshark", 8, "info", "both"),
        new("wireshark", "Wireshark", 8, "info", "process"),
        new("tshark", "Wireshark tshark", 8, "info", "process"),
        new("dumpcap", "Wireshark dumpcap", 8, "info", "process"),
        new("Nirsoft", "NirSoft utilities", 8, "info", "both"),
        new("Nirsoft", "NirSoft utilities", 8, "info", "process"),
        new("WinSCP", "WinSCP", 8, "info", "both"),
        new("winscp", "WinSCP", 8, "info", "process"),
        new("Angry IP Scanner", "Angry IP Scanner", 8, "info", "both"),
        new("ipscan", "Angry IP Scanner", 8, "info", "process"),
        new("Advanced IP Scanner", "Advanced IP Scanner", 8, "info", "both"),
        new("advanced_ip_scanner", "Advanced IP Scanner", 8, "info", "process"),
        new("Cain & Abel", "Cain & Abel", 8, "high", "both"),
        new("cain.exe", "Cain & Abel", 8, "high", "process"),

        // ═══════════════════════════════════════════════════════════════
        // Category 9 — Cryptominers (high)
        // ═══════════════════════════════════════════════════════════════
        new("XMRig", "XMRig Miner", 9, "high", "both"),
        new("xmrig", "XMRig Miner", 9, "high", "process"),
        new("NiceHash", "NiceHash Miner", 9, "high", "both"),
        new("NiceHashMiner", "NiceHash Miner", 9, "high", "process"),
        new("nhmpayload", "NiceHash Miner", 9, "high", "process"),
        new("cpuminer", "cpuminer", 9, "high", "both"),
        new("minerd", "cpuminer (minerd)", 9, "high", "process"),
        new("Claymore", "Claymore Miner", 9, "high", "both"),
        new("EthDcrMiner", "Claymore Miner", 9, "high", "process"),
        new("PhoenixMiner", "PhoenixMiner", 9, "high", "both"),
        new("T-Rex", "T-Rex Miner", 9, "high", "both"),
        new("t-rex", "T-Rex Miner", 9, "high", "process"),
        new("NBMiner", "NBMiner", 9, "high", "both"),
        new("nbminer", "NBMiner", 9, "high", "process"),
        new("LemonDuck", "LemonDuck", 9, "high", "both"),
        new("lemon_duck", "LemonDuck", 9, "high", "process"),
        new("Coinhive", "Coinhive", 9, "high", "both"),
        new("TeamRedMiner", "TeamRedMiner", 9, "high", "both"),
        new("teamredminer", "TeamRedMiner", 9, "high", "process"),
        new("lolMiner", "lolMiner", 9, "high", "both"),
        new("lolminer", "lolMiner", 9, "high", "process"),
        new("GMiner", "GMiner", 9, "high", "both"),
        new("miner.exe", "Generic Miner", 9, "high", "process"),
        new("SRBMiner", "SRBMiner", 9, "high", "both"),
        new("srbminer", "SRBMiner", 9, "high", "process"),
        new("WildRig", "WildRig Multi", 9, "high", "both"),
        new("wildrig", "WildRig Multi", 9, "high", "process"),
        new("minergate", "MinerGate", 9, "high", "both"),
        new("ethminer", "Ethminer", 9, "high", "both"),
        new("CudoMiner", "Cudo Miner", 9, "high", "both"),
        new("cudominer", "Cudo Miner", 9, "high", "process"),

        // ═══════════════════════════════════════════════════════════════
        // Category 10 — Ransomware (critical)
        // ═══════════════════════════════════════════════════════════════
        new("WannaCry", "WannaCry", 10, "critical", "both"),
        new("wannacrypt", "WannaCry", 10, "critical", "process"),
        new("wcry", "WannaCry", 10, "critical", "process"),
        new("mssecsvc", "WannaCry (mssecsvc)", 10, "critical", "process"),
        new("tasksche", "WannaCry (tasksche)", 10, "critical", "process"),
        new("NotPetya", "NotPetya", 10, "critical", "both"),
        new("Petya", "Petya / NotPetya", 10, "critical", "both"),
        new("Ryuk", "Ryuk", 10, "critical", "both"),
        new("REvil", "REvil (Sodinokibi)", 10, "critical", "both"),
        new("Sodinokibi", "REvil (Sodinokibi)", 10, "critical", "both"),
        new("Conti", "Conti Ransomware", 10, "critical", "both"),
        new("LockBit", "LockBit", 10, "critical", "both"),
        new("lockbit", "LockBit", 10, "critical", "process"),
        new("BlackCat", "BlackCat (ALPHV)", 10, "critical", "both"),
        new("ALPHV", "BlackCat (ALPHV)", 10, "critical", "both"),
        new("Dharma", "Dharma Ransomware", 10, "critical", "both"),
        new("CrySiS", "Dharma/CrySiS", 10, "critical", "both"),
        new("STOP Ransomware", "STOP/Djvu", 10, "critical", "both"),
        new("Djvu", "STOP/Djvu", 10, "critical", "both"),
        new("Hive Ransomware", "Hive", 10, "critical", "both"),
        new("Royal Ransomware", "Royal", 10, "critical", "both"),
        new("Black Basta", "Black Basta", 10, "critical", "both"),
        new("Akira", "Akira Ransomware", 10, "critical", "both"),
        new("Maze", "Maze Ransomware", 10, "critical", "both"),
        new("DoppelPaymer", "DoppelPaymer", 10, "critical", "both"),
        new("Egregor", "Egregor", 10, "critical", "both"),
        new("Clop", "Clop Ransomware", 10, "critical", "both"),
        new("Ragnar Locker", "Ragnar Locker", 10, "critical", "both"),
        new("Avaddon", "Avaddon", 10, "critical", "both"),
        new("Babuk", "Babuk Ransomware", 10, "critical", "both"),
        new("BlackMatter", "BlackMatter", 10, "critical", "both"),
        new("Cuba Ransomware", "Cuba Ransomware", 10, "critical", "both"),
        new("Medusa Ransomware", "Medusa", 10, "critical", "both"),
        new("MedusaLocker", "MedusaLocker", 10, "critical", "both"),
        new("Play Ransomware", "Play", 10, "critical", "both"),
        new("Rhysida", "Rhysida", 10, "critical", "both"),
        new("AvosLocker", "AvosLocker", 10, "critical", "both"),
        new("Phobos", "Phobos Ransomware", 10, "critical", "both"),
        new("GandCrab", "GandCrab", 10, "critical", "both"),
        new("SamSam", "SamSam", 10, "critical", "both"),
        new("Cerber", "Cerber Ransomware", 10, "critical", "both"),
        new("Locky", "Locky Ransomware", 10, "critical", "both"),
        new("CryptoLocker", "CryptoLocker", 10, "critical", "both"),
        new("CryptoWall", "CryptoWall", 10, "critical", "both"),
        new("TeslaCrypt", "TeslaCrypt", 10, "critical", "both"),
        new("Jigsaw", "Jigsaw Ransomware", 10, "critical", "both"),
        new("drpbx.exe", "Jigsaw (drpbx)", 10, "critical", "process"),
        new("BitPaymer", "BitPaymer", 10, "critical", "both"),
        new("WastedLocker", "WastedLocker", 10, "critical", "both"),
        new("Yanluowang", "Yanluowang", 10, "critical", "both"),
        new("Nokoyawa", "Nokoyawa", 10, "critical", "both"),
        new("Trigona", "Trigona", 10, "critical", "both"),
        new("Snatch", "Snatch Ransomware", 10, "critical", "both"),
        new("Vice Society", "Vice Society", 10, "critical", "both"),
        new("BianLian", "BianLian", 10, "critical", "both"),

        // ═══════════════════════════════════════════════════════════════
        // Category 11 — Fake Antivirus (medium)
        // ═══════════════════════════════════════════════════════════════
        new("Antivirus XP", "Antivirus XP (fake)", 11, "medium", "both"),
        new("Security Shield", "Security Shield (fake)", 11, "medium", "both"),
        new("Windows Police Pro", "Windows Police Pro (fake)", 11, "medium", "both"),
        new("Total Security", "Total Security (fake AV)", 11, "medium", "registry"),
        new("AntiVirus Pro", "AntiVirus Pro (fake)", 11, "medium", "both"),
        new("System Security", "System Security (fake)", 11, "medium", "both"),
        new("Live PC Help", "Live PC Help (fake)", 11, "medium", "both"),
        new("MS Antivirus", "MS Antivirus (fake)", 11, "medium", "both"),
        new("AntiMalware", "Fake AntiMalware", 11, "medium", "registry"),
        new("WinAntiVirus", "WinAntiVirus (fake)", 11, "medium", "both"),
        new("SpySheriff", "SpySheriff (fake)", 11, "medium", "both"),
        new("SpywareGuard", "SpywareGuard (fake)", 11, "medium", "both"),
        new("Personal Antivirus", "Personal Antivirus (fake)", 11, "medium", "both"),
        new("Security Essentials 2010", "Security Essentials 2010 (fake)", 11, "medium", "both"),
        new("Desktop Security", "Desktop Security (fake)", 11, "medium", "both"),
        new("Internet Security 20", "Fake Internet Security 20xx", 11, "medium", "registry"),
        new("SystemGuard", "Fake SystemGuard", 11, "medium", "both"),
        new("Virus Trigger", "Virus Trigger (fake)", 11, "medium", "both"),

        // ═══════════════════════════════════════════════════════════════
        // Category 12 — Loaders / Stealers (medium-high)
        // ═══════════════════════════════════════════════════════════════
        new("SocGholish", "SocGholish (FakeUpdates)", 12, "medium", "both"),
        new("FakeUpdate", "SocGholish (FakeUpdates)", 12, "medium", "process"),
        new("Emotet", "Emotet", 12, "high", "both"),
        new("emotet", "Emotet", 12, "high", "process"),
        new("TrickBot", "TrickBot", 12, "high", "both"),
        new("trickbot", "TrickBot", 12, "high", "process"),
        new("Qakbot", "Qakbot (QBot)", 12, "high", "both"),
        new("QBot", "Qakbot (QBot)", 12, "high", "both"),
        new("qakbot", "Qakbot (QBot)", 12, "high", "process"),
        new("IcedID", "IcedID (BokBot)", 12, "high", "both"),
        new("BokBot", "IcedID (BokBot)", 12, "high", "process"),
        new("Raccoon Stealer", "Raccoon Stealer", 12, "high", "both"),
        new("raccoon", "Raccoon Stealer", 12, "high", "process"),
        new("RedLine", "RedLine Stealer", 12, "high", "both"),
        new("redline", "RedLine Stealer", 12, "high", "process"),
        new("Vidar", "Vidar Stealer", 12, "high", "both"),
        new("vidar", "Vidar Stealer", 12, "high", "process"),
        new("FormBook", "FormBook", 12, "high", "both"),
        new("formbook", "FormBook", 12, "high", "process"),
        new("Lumma Stealer", "Lumma Stealer", 12, "high", "both"),
        new("lumma", "Lumma Stealer", 12, "high", "process"),
        new("Stealc", "Stealc", 12, "high", "both"),
        new("Aurora Stealer", "Aurora Stealer", 12, "high", "both"),
        new("BatLoader", "BatLoader", 12, "medium", "both"),
        new("BumbleBee", "BumbleBee Loader", 12, "high", "both"),
        new("bumblebee", "BumbleBee Loader", 12, "high", "process"),
        new("Gootloader", "Gootloader", 12, "high", "both"),
        new("gootloader", "Gootloader", 12, "high", "process"),
        new("Danabot", "Danabot", 12, "high", "both"),
        new("Amadey", "Amadey Bot", 12, "high", "both"),
        new("amadey", "Amadey Bot", 12, "high", "process"),
        new("SystemBC", "SystemBC", 12, "high", "both"),
        new("SmokeLoader", "SmokeLoader", 12, "high", "both"),
        new("smokeloader", "SmokeLoader", 12, "high", "process"),
        new("Phorpiex", "Phorpiex", 12, "medium", "both"),
        new("GCleaner", "GCleaner Loader", 12, "medium", "both"),
        new("PrivateLoader", "PrivateLoader", 12, "medium", "both"),
        new("Zloader", "Zloader", 12, "high", "both"),
        new("zloader", "Zloader", 12, "high", "process"),
        new("Rhadamanthys", "Rhadamanthys Stealer", 12, "high", "both"),
        new("ArkeiStealer", "Arkei Stealer", 12, "high", "both"),
        new("Mars Stealer", "Mars Stealer", 12, "high", "both"),
    ];

    // ── Known malware-specific registry keys ──
    private static readonly (string Path, string ThreatName, int Category, string Severity)[] MalwareRegistryKeys =
    [
        (@"Software\NETwIRe", "NetWire RAT", 7, "critical"),
        (@"Software\CryptoLocker", "CryptoLocker", 10, "critical"),
        (@"Software\GCleaner", "GCleaner Loader", 12, "medium"),
        (@"Software\DarkComet", "DarkComet RAT", 7, "critical"),
        (@"Software\njRAT", "njRAT", 7, "critical"),
        (@"Software\Remcos", "Remcos RAT", 7, "critical"),
        (@"Software\AsyncRAT", "AsyncRAT", 7, "critical"),
        (@"Software\XWorm", "XWorm", 7, "critical"),
        (@"Software\NanoCore", "NanoCore RAT", 7, "critical"),
    ];

    // ── Public API ──

    public static List<ThreatFinding> ScanAll()
    {
        var findings = new List<ThreatFinding>();
        findings.AddRange(ScanRegistry());
        findings.AddRange(ScanProcesses());
        Console.WriteLine($"  Threat scan: {findings.Count} findings");
        return findings;
    }

    public static List<ThreatFinding> ScanRegistry()
    {
        var findings = new List<ThreatFinding>();

        // Uninstall registry paths
        string[] uninstallPaths =
        [
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        ];

        // Scan HKLM uninstall keys
        foreach (var basePath in uninstallPaths)
        {
            ScanUninstallKey(Registry.LocalMachine, basePath, findings);
        }

        // Scan HKCU uninstall key
        ScanUninstallKey(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", findings);

        // Check known malware-specific registry keys in HKCU and HKLM
        foreach (var (path, threatName, category, severity) in MalwareRegistryKeys)
        {
            CheckMalwareKey(Registry.CurrentUser, path, threatName, category, severity, findings);
            CheckMalwareKey(Registry.LocalMachine, @"SOFTWARE\" + path.Replace(@"Software\", ""), threatName, category, severity, findings);
        }

        return findings;
    }

    public static List<ThreatFinding> ScanProcesses()
    {
        var findings = new List<ThreatFinding>();

        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch
        {
            return findings;
        }

        // Build set of process names (case-insensitive)
        var processNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var proc in processes)
        {
            try
            {
                processNames.Add(proc.ProcessName);
            }
            catch
            {
                // Access denied for some system processes
            }
        }

        // Deduplicate: track which threat names we've already reported via process scan
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sig in Signatures)
        {
            if (sig.Vector is not "process" and not "both")
                continue;

            foreach (var procName in processNames)
            {
                if (procName.Contains(sig.Pattern, StringComparison.OrdinalIgnoreCase))
                {
                    var key = sig.ThreatName + "|" + procName;
                    if (reported.Add(key))
                    {
                        findings.Add(new ThreatFinding
                        {
                            ThreatName = sig.ThreatName,
                            Category = CategoryNames[sig.Category],
                            Severity = sig.Severity,
                            Vector = "process",
                            Detail = $"Process: {procName}"
                        });
                    }
                }
            }
        }

        return findings;
    }

    // ── Private helpers ──

    private static void ScanUninstallKey(RegistryKey hive, string basePath, List<ThreatFinding> findings)
    {
        try
        {
            using var key = hive.OpenSubKey(basePath);
            if (key is null) return;

            foreach (var subKeyName in key.GetSubKeyNames())
            {
                try
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    if (subKey is null) continue;

                    var displayName = subKey.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(displayName)) continue;

                    foreach (var sig in Signatures)
                    {
                        if (sig.Vector is not "registry" and not "both")
                            continue;

                        if (displayName.Contains(sig.Pattern, StringComparison.OrdinalIgnoreCase))
                        {
                            findings.Add(new ThreatFinding
                            {
                                ThreatName = sig.ThreatName,
                                Category = CategoryNames[sig.Category],
                                Severity = sig.Severity,
                                Vector = "registry",
                                Detail = $"Installed: {displayName}"
                            });
                            break; // One match per DisplayName is enough
                        }
                    }
                }
                catch
                {
                    // Skip inaccessible subkeys
                }
            }
        }
        catch
        {
            // Skip inaccessible hive/path
        }
    }

    private static void CheckMalwareKey(RegistryKey hive, string path, string threatName,
        int category, string severity, List<ThreatFinding> findings)
    {
        try
        {
            using var key = hive.OpenSubKey(path);
            if (key is not null)
            {
                findings.Add(new ThreatFinding
                {
                    ThreatName = threatName,
                    Category = CategoryNames[category],
                    Severity = severity,
                    Vector = "registry",
                    Detail = $"Registry key: {hive.Name}\\{path}"
                });
            }
        }
        catch
        {
            // Ignore access errors
        }
    }
}

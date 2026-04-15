using KryossApi.Data.Entities;

namespace KryossApi.Services;

/// <summary>
/// Represents a capital sin that forces the C-Level Risk Posture semáforo
/// to RED regardless of the computed score. Each sin links to a specific
/// CTA rule so the CTA #1 in Block 3 auto-promotes to match the sin.
/// </summary>
internal record CapitalSin(
    string Code,
    string Narrative,
    string LinkedCtaRule);

/// <summary>
/// The 4-rule collapse logic. Rules are checked in priority order:
///   1. Active threats  (Incidentes)
///   2. M365 admin without MFA  (requires tenant connected)
///   3. LAPS coverage at 0%
///   4. RDP exposed on server-role hostnames
/// Returns the first sin that fires, or null if none fire.
/// </summary>
internal static class CapitalSinDetector
{
    public static CapitalSin? Detect(
        List<AssessmentRun> runs,
        HygieneScanDto? hygiene,
        OrgEnrichment enrichment,
        bool m365TenantConnected,
        List<M365Finding>? m365Findings,
        string lang)
    {
        var es = lang == "es";

        // 1. Active threats — "Invasión en curso"
        if (enrichment.Threats.Count > 0)
        {
            return new CapitalSin(
                "threats",
                es ? "Invasión en curso — amenazas activas detectadas en la red"
                   : "Intrusion in progress — active threats detected on the network",
                "active-threats");
        }

        // 2. M365 admin without MFA — "Llave maestra sin protección"
        if (m365TenantConnected && m365Findings != null)
        {
            var adminNoMfa = m365Findings.Any(f =>
                f.Status == "fail" &&
                f.CheckId.Contains("MFA", StringComparison.OrdinalIgnoreCase) &&
                f.Name.Contains("admin", StringComparison.OrdinalIgnoreCase));
            if (adminNoMfa)
            {
                return new CapitalSin(
                    "m365-admin-mfa",
                    es ? "Llave maestra sin protección — administradores M365 sin MFA obligatorio"
                       : "Master key unprotected — M365 admins without enforced MFA",
                    "m365-admin-mfa");
            }
        }

        // 3. LAPS coverage == 0% — "Sin barreras internas"
        var totalAdMachines = hygiene?.TotalMachines ?? 0;
        var lapsFailing = hygiene?.Findings.Count(f => f.Status == "NoLAPS") ?? 0;
        if (totalAdMachines > 0 && lapsFailing == totalAdMachines)
        {
            return new CapitalSin(
                "laps-zero",
                es ? "Sin barreras internas — cero LAPS desplegado en la flota"
                   : "No internal barriers — zero LAPS deployment across the fleet",
                "laps-rollout");
        }

        // 4. RDP exposed on server-role hostnames — "Puerta abierta a internet"
        // Matches common server naming conventions: *-DC*, *-SRV*, *-APP*, *-SQL*, *-WEB*, *-FS*
        var serverPatterns = new[] { "-DC", "-SRV", "-APP", "-SQL", "-WEB", "-FS" };
        var rdpOnServer = enrichment.Ports
            .Where(p => p.Port == 3389 && p.Risk != null)
            .Join(runs, p => p.MachineId, r => r.Machine.Id, (p, r) => r.Machine.Hostname)
            .Any(host => host != null &&
                serverPatterns.Any(pat => host.Contains(pat, StringComparison.OrdinalIgnoreCase)));
        if (rdpOnServer)
        {
            return new CapitalSin(
                "rdp-servers",
                es ? "Puerta abierta a internet — RDP expuesto directamente en servidores"
                   : "Internet door open — RDP exposed directly on servers",
                "rdp-vpn-gateway");
        }

        return null;
    }
}

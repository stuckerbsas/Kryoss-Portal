using KryossApi.Data.Entities;

namespace KryossApi.Services;

/// <summary>
/// CTA candidate produced by the rule engine.
/// PriorityRank: 1=Incidentes, 2=Hardening, 3=Budget, 4=Risk.
/// </summary>
internal record CtaCandidate(
    string RuleId,
    string Title,
    string Description,
    string Category,
    int    PriorityRank,
    int    AffectedCount);

/// <summary>
/// C-Level Block 3 auto-detection engine.
/// Runs 12 rules against the fleet data and returns ranked candidates.
/// The operator can edit/suppress/add via the portal (ExecutiveCtasFunction)
/// and the final rendering happens in BuildOrgCLevelReport.
/// </summary>
internal static class CtaRuleEngine
{
    private const int PrIncidentes = 1;
    private const int PrHardening  = 2;
    private const int PrBudget     = 3;
    // PrRisk reserved (=4) for future explicit risk-acceptance rules

    public static List<CtaCandidate> DetectCtas(
        List<AssessmentRun> runs,
        List<OrgControlResult> allResults,
        HygieneScanDto? hygiene,
        OrgEnrichment enrichment,
        bool m365TenantConnected,
        List<M365Finding>? m365Findings,
        string lang)
    {
        var es = lang == "es";
        var found = new List<CtaCandidate>();

        int FailMachines(Func<OrgControlResult, bool> pred) => allResults
            .Where(r => r.Status == "fail" && pred(r))
            .Select(r => r.RunId).Distinct().Count();

        // 1. Active threats → Incidentes
        if (enrichment.Threats.Count > 0)
        {
            var machineCount = enrichment.Threats.Select(t => t.MachineId).Distinct().Count();
            found.Add(new CtaCandidate(
                "active-threats",
                es ? "Aprobar engagement de respuesta forense inmediata"
                   : "Approve immediate forensic IR engagement",
                es ? $"Se detectaron {enrichment.Threats.Count} firmas de amenazas activas en {machineCount} equipos. Requiere análisis forense inmediato antes de que escalen."
                   : $"{enrichment.Threats.Count} active threat signatures detected on {machineCount} machines. Immediate forensic analysis required before they escalate.",
                "Incidentes", PrIncidentes, machineCount));
        }

        // 2. LAPS coverage < 50% → Hardening
        var lapsFailing = hygiene?.Findings.Count(f => f.Status == "NoLAPS") ?? 0;
        var totalAdMachines = hygiene?.TotalMachines ?? 0;
        if (totalAdMachines > 0)
        {
            var lapsCoverage = 100.0 * (1.0 - (double)lapsFailing / totalAdMachines);
            if (lapsCoverage < 50)
            {
                found.Add(new CtaCandidate(
                    "laps-rollout",
                    es ? "Aprobar rollout de LAPS en la flota"
                       : "Approve LAPS rollout across the fleet",
                    es ? $"LAPS desplegado solo en {lapsCoverage:F0}% ({lapsFailing} equipos sin cobertura). Una credencial local comprometida da acceso a toda la red."
                       : $"LAPS coverage at {lapsCoverage:F0}% ({lapsFailing} machines missing it). A single compromised local admin password grants lateral access to the entire fleet.",
                    "Hardening", PrHardening, lapsFailing));
            }
        }

        // 3. BitLocker missing on >30% → Hardening
        var bitlockerMissing = runs.Count(r => r.Machine.Bitlocker != true);
        if (runs.Count > 0 && bitlockerMissing >= runs.Count * 0.3)
        {
            found.Add(new CtaCandidate(
                "bitlocker-program",
                es ? "Aprobar programa de cifrado de discos"
                   : "Approve disk encryption program",
                es ? $"{bitlockerMissing} equipos sin BitLocker. El robo o pérdida de un solo dispositivo expone todos los datos corporativos."
                   : $"{bitlockerMissing} machines without BitLocker. Theft or loss of a single device exposes all corporate data.",
                "Hardening", PrHardening, bitlockerMissing));
        }

        // 4. Privileged accounts excess → Hardening
        var privilegedCount = hygiene?.Findings.Count(f => f.Status == "Privileged") ?? 0;
        if (privilegedCount > 10)
        {
            found.Add(new CtaCandidate(
                "privileged-review",
                es ? "Aprobar revisión trimestral de cuentas privilegiadas"
                   : "Approve quarterly privileged account review",
                es ? $"Hoy existen {privilegedCount} cuentas con privilegios elevados. Cada una multiplica el radio de impacto ante un compromiso."
                   : $"{privilegedCount} accounts currently hold elevated privileges. Each one multiplies the blast radius of a credential compromise.",
                "Hardening", PrHardening, privilegedCount));
        }

        // 5. Password never expires > 10 → Hardening
        var pwdNever = hygiene?.PwdNeverExpire ?? 0;
        if (pwdNever > 10)
        {
            found.Add(new CtaCandidate(
                "password-rotation",
                es ? "Firmar política de rotación de contraseñas"
                   : "Sign password rotation policy",
                es ? $"{pwdNever} cuentas con contraseñas que nunca expiran. Viola todo framework de compliance y multiplica el impacto del robo de credenciales."
                   : $"{pwdNever} accounts with non-expiring passwords. Violates every compliance framework and amplifies credential theft impact.",
                "Hardening", PrHardening, pwdNever));
        }

        // 6. Kerberoastable → Hardening
        var kerberoast = hygiene?.Findings.Count(f => f.Status == "Kerberoastable") ?? 0;
        if (kerberoast > 0)
        {
            found.Add(new CtaCandidate(
                "kerberoast-remediation",
                es ? "Aprobar remediación de cuentas Kerberoastables"
                   : "Approve Kerberoastable account remediation",
                es ? $"{kerberoast} cuentas de servicio vulnerables a Kerberoast. Sus contraseñas pueden crackearse offline sin una sola alerta."
                   : $"{kerberoast} service accounts vulnerable to Kerberoast. Passwords can be cracked offline without generating a single alert.",
                "Hardening", PrHardening, kerberoast));
        }

        // 7. RDP exposed → Hardening
        var rdpExposed = enrichment.Ports
            .Where(p => p.Port == 3389 && p.Risk != null)
            .Select(p => p.MachineId).Distinct().Count();
        if (rdpExposed > 0)
        {
            found.Add(new CtaCandidate(
                "rdp-vpn-gateway",
                es ? "Aprobar VPN / RD Gateway mandatorio"
                   : "Approve mandatory VPN / RD Gateway",
                es ? $"{rdpExposed} equipos con RDP directo a internet. El vector #1 de ransomware de los últimos 3 años."
                   : $"{rdpExposed} machines with RDP exposed directly to the internet. The #1 ransomware vector of the past 3 years.",
                "Hardening", PrHardening, rdpExposed));
        }

        // 8. SMBv1 / NTLM legacy protocols → Hardening (90-day telemetry pitch)
        var legacyProto = FailMachines(r =>
            r.Name.Contains("SMBv1", StringComparison.OrdinalIgnoreCase) ||
            r.Name.Contains("SMB1",  StringComparison.OrdinalIgnoreCase) ||
            r.Name.Contains("NTLM",  StringComparison.OrdinalIgnoreCase));
        if (legacyProto > 10)
        {
            found.Add(new CtaCandidate(
                "passive-telemetry-90",
                es ? "Aprobar arranque de telemetría pasiva de 90 días"
                   : "Approve 90-day passive telemetry engagement",
                es ? $"{legacyProto} equipos con protocolos legacy activos (SMBv1/NTLM). Iniciamos el ciclo de 90 días de mapeo de dependencias para deprecación segura con cero downtime."
                   : $"{legacyProto} machines with legacy protocols active (SMBv1/NTLM). Start the 90-day dependency-mapping cycle for safe zero-downtime deprecation.",
                "Hardening", PrHardening, legacyProto));
        }

        // 9. M365 admin MFA gap → Hardening (only if tenant connected)
        if (m365TenantConnected && m365Findings != null)
        {
            var adminMfaGap = m365Findings.Count(f =>
                f.Status == "fail" &&
                f.CheckId.Contains("MFA", StringComparison.OrdinalIgnoreCase) &&
                f.Name.Contains("admin", StringComparison.OrdinalIgnoreCase));
            if (adminMfaGap > 0)
            {
                found.Add(new CtaCandidate(
                    "m365-admin-mfa",
                    es ? "Firmar enforcement de MFA obligatorio para administradores M365"
                       : "Sign mandatory MFA enforcement for M365 admins",
                    es ? $"{adminMfaGap} administradores M365 sin MFA obligatorio. Una credencial comprometida da acceso total al tenant."
                       : $"{adminMfaGap} M365 admins lack enforced MFA. A single compromised credential grants full tenant access.",
                    "Hardening", PrHardening, adminMfaGap));
            }
        }

        // 10. Legacy OS → Budget
        var legacyOs = runs.Count(r => r.Machine.OsName != null && (
            r.Machine.OsName.Contains("2008") ||
            r.Machine.OsName.Contains("2003") ||
            r.Machine.OsName.Contains("Windows 7") ||
            r.Machine.OsName.Contains("Vista")));
        if (legacyOs > 0)
        {
            found.Add(new CtaCandidate(
                "legacy-os-migration",
                es ? "Aprobar budget de migración Azure para SO fuera de soporte"
                   : "Approve Azure migration budget for end-of-life OS",
                es ? $"{legacyOs} máquinas con Windows 2003/2008/7/Vista. No reciben parches modernos — representan el mayor riesgo residual."
                   : $"{legacyOs} machines running Windows 2003/2008/7/Vista. Cannot receive modern security patches — highest residual risk vector.",
                "Budget", PrBudget, legacyOs));
        }

        // 11. Domain functional level → Budget
        var domainLevel = hygiene?.Findings.FirstOrDefault(f => f.Status == "DomainLevel");
        if (domainLevel != null && (domainLevel.Detail?.Contains("2008") == true || domainLevel.Detail?.Contains("2003") == true))
        {
            found.Add(new CtaCandidate(
                "domain-level-upgrade",
                es ? "Aprobar upgrade del dominio AD a funcional 2016+"
                   : "Approve AD domain upgrade to functional level 2016+",
                es ? $"Dominio operando en nivel funcional {domainLevel.Detail}. Bloquea features modernos como Protected Users y Credential Guard."
                   : $"Domain running at functional level {domainLevel.Detail}. Blocks modern security features like Protected Users and Credential Guard.",
                "Budget", PrBudget, 1));
        }

        // 12. Critical findings > 5 → Budget (patch sprint)
        var criticalFails = allResults.Count(r => r.Status == "fail" && r.Severity == "critical");
        if (criticalFails > 5)
        {
            found.Add(new CtaCandidate(
                "critical-patch-sprint",
                es ? "Aprobar sprint de parcheo crítico"
                   : "Approve critical patching sprint",
                es ? $"{criticalFails} hallazgos de severidad crítica sin remediar. Requieren un sprint dedicado fuera del backlog mensual de hardening."
                   : $"{criticalFails} unremediated critical-severity findings. Requires a dedicated patching sprint outside the monthly hardening backlog.",
                "Budget", PrBudget, criticalFails));
        }

        return found
            .OrderBy(c => c.PriorityRank)
            .ThenByDescending(c => c.AffectedCount)
            .ToList();
    }
}

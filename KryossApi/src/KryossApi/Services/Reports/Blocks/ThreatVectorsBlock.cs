using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class ThreatVectorsBlock : IReportBlock
{
    // headline-item: padding(28) + margin-bottom(10) + title-strong(22) + spacing(8) = 68px chrome
    private const int VectorCardChrome = 68;
    // headline-text font-size:13px line-height:1.6 = ~21px/line; impact box 9pt*1.45 ≈ 18px/line
    private const int DescLinePx = 21;
    // impact box: margin(6) + padding(16) + title-strong(18) = 40px chrome
    private const int ImpactChrome = 40;
    private const int ImpactLinePx = 18;
    // evidence: margin(4) + 8.5pt*1.4 ≈ 16px/line
    private const int EvidenceLinePx = 16;
    private const int EvidenceMargin = 4;
    // CTA box: padding(40) + title(20) + subtitle(24) + body(~100) + footer(30) ≈ 214px + margin-top(24)
    private const int CtaBoxHeight = 240;

    public string Render(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        var es = options.IsSpanish;
        var brand = data.Branding;
        var budget = new PageBudget();

        var vectors = DetectCompromiseVectors(data, es);
        var pageTitle = es ? "Vectores de Compromiso" : "Compromise Vectors";

        budget.StartPage(sb, pageTitle, brand);

        if (vectors.Count == 0)
        {
            sb.AppendLine("<div class='headline-findings'>");
            sb.AppendLine("<div class='headline-item' style='background:#f0fdf4;border-color:#006536'>");
            sb.AppendLine("<div class='headline-icon' style='background:#006536'>&#10003;</div>");
            sb.AppendLine("<div class='headline-text'>");
            sb.AppendLine($"<strong>{ReportHelpers.HtmlEncode(es ? "Sin vectores críticos detectados" : "No critical compromise vectors detected")}</strong><br>");
            sb.AppendLine(ReportHelpers.HtmlEncode(es
                ? "La postura general es saludable. Igual vale la pena revisar los hallazgos medios y bajos para mantener la madurez en el tiempo."
                : "The overall posture is healthy. Still worth reviewing the medium and low severity findings to sustain maturity over time."));
            sb.AppendLine("</div></div>");
            sb.AppendLine("</div>");
            budget.Spend(120);
        }
        else
        {
            int n = 1;
            foreach (var v in vectors)
            {
                var descLines = PageBudget.LineCount(v.Description, 90);
                var impactLines = PageBudget.LineCount(v.Impact, 95);
                var evidenceLines = PageBudget.LineCount(v.Evidence, 95);

                int cardHeight = VectorCardChrome
                    + descLines * DescLinePx
                    + ImpactChrome + impactLines * ImpactLinePx
                    + (evidenceLines > 0 ? EvidenceMargin + evidenceLines * EvidenceLinePx : 0);

                if (budget.WouldOverflow(cardHeight))
                    budget.NewPage(sb, pageTitle, brand);

                sb.AppendLine("<div class='headline-item'>");
                sb.AppendLine($"<div class='headline-icon'>{n++}</div>");
                sb.AppendLine("<div class='headline-text'>");
                sb.AppendLine($"<strong>{ReportHelpers.HtmlEncode(v.Title)}</strong><br>");
                sb.AppendLine($"<p style='margin:4px 0 8px;font-size:10pt;line-height:1.5'>{ReportHelpers.HtmlEncode(v.Description)}</p>");

                // Impact section
                sb.AppendLine($"<div style='margin:6px 0;padding:8px 12px;background:#fff8f0;border-left:3px solid #D97706;border-radius:4px'>");
                sb.AppendLine($"<strong style='font-size:9pt;color:#92400e'>{ReportHelpers.HtmlEncode(es ? "Impacto Operacional" : "Operational Impact")}</strong>");
                sb.AppendLine($"<p style='margin:3px 0 0;font-size:9pt;line-height:1.45;color:#78350f'>{ReportHelpers.HtmlEncode(v.Impact)}</p>");
                sb.AppendLine("</div>");

                // Evidence line
                if (!string.IsNullOrEmpty(v.Evidence))
                {
                    sb.AppendLine($"<div style='margin:4px 0 0;font-size:8.5pt;color:#666;font-style:italic'>");
                    sb.AppendLine($"<strong>{ReportHelpers.HtmlEncode(es ? "Evidencia:" : "Evidence:")}</strong> {ReportHelpers.HtmlEncode(v.Evidence)}");
                    sb.AppendLine("</div>");
                }

                sb.AppendLine("</div></div>");
                budget.Spend(cardHeight);
            }
        }

        // CTA block
        if (budget.WouldOverflow(CtaBoxHeight))
            budget.NewPage(sb, pageTitle, brand);

        sb.AppendLine("<div class='cta-box'>");
        sb.AppendLine($"<p style='font-size:11px;font-weight:700;letter-spacing:0.12em;text-transform:uppercase;color:{brand.AccentColor};margin-bottom:4px'>{ReportHelpers.HtmlEncode(es ? "LOS PRÓXIMOS 90 DÍAS" : "THE NEXT 90 DAYS")}</p>");
        sb.AppendLine($"<p style='font-size:17px;font-weight:700;margin-bottom:10px'>{ReportHelpers.HtmlEncode(es ? "Antes de romper nada." : "Before you disable anything.")}</p>");
        if (es)
        {
            sb.AppendLine($"<p style='font-size:12px;line-height:1.65;margin-bottom:12px;color:#d4d4d4;text-align:left'>Apagar SMBv1, NTLM o cualquier protocolo legacy sin entender quién depende de ellos rompe aplicaciones críticas — y esa caída es suya. Nuestra respuesta es la <strong style='color:{brand.AccentColor}'>Auditoría de Depreciación Segura de 90 Días</strong>: desplegamos un motor de telemetría pasiva que mapea, sin interrumpir a nadie, exactamente qué sistemas, usuarios y procesos dependen de cada protocolo legacy que hoy lo expone. Al día 90 usted recibe el plan de depreciación con <strong style='color:#fff'>cero ambigüedad</strong> y <strong style='color:#fff'>cero downtime operacional</strong>.</p>");
            sb.AppendLine($"<p style='font-size:13px;margin:0;padding-top:10px;border-top:1px solid #666'>Reserve 30 minutos con <strong>{ReportHelpers.HtmlEncode(brand.CompanyName)}</strong> para iniciar la telemetría.</p>");
        }
        else
        {
            sb.AppendLine($"<p style='font-size:12px;line-height:1.65;margin-bottom:12px;color:#d4d4d4;text-align:left'>Turning off SMBv1, NTLM or any legacy protocol without knowing who depends on them breaks critical applications — and that outage is yours to own. Our answer is the <strong style='color:{brand.AccentColor}'>90-Day Safe Deprecation Audit</strong>: we deploy a passive telemetry engine that maps, without interrupting anyone, exactly which systems, users and processes rely on each legacy protocol exposing you today. On day 90 you receive a deprecation plan with <strong style='color:#fff'>zero ambiguity</strong> and <strong style='color:#fff'>zero operational downtime</strong>.</p>");
            sb.AppendLine($"<p style='font-size:13px;margin:0;padding-top:10px;border-top:1px solid #666'>Book 30 minutes with <strong>{ReportHelpers.HtmlEncode(brand.CompanyName)}</strong> to start the telemetry.</p>");
        }
        sb.AppendLine("</div>");
        budget.Spend(CtaBoxHeight);

        budget.EndPage(sb);
        return sb.ToString();
    }

    private static List<VectorDetail> DetectCompromiseVectors(ReportData data, bool es)
    {
        var found = new List<VectorDetail>();
        var allResults = data.ControlResults;
        var hygiene = data.Hygiene;
        var enrichment = data.Enrichment;
        var runs = data.Runs;

        int FailCount(Func<OrgControlResult, bool> predicate) => allResults
            .Where(r => r.Status == "fail" && predicate(r))
            .Select(r => r.RunId).Distinct().Count();

        // 1. Active threats
        if (enrichment.Threats.Count > 0)
        {
            var machines = enrichment.Threats.Select(t => t.MachineId).Distinct().Count();
            var critCount = enrichment.Threats.Count(t => t.Severity == "critical");
            var highCount = enrichment.Threats.Count(t => t.Severity == "high");
            var categories = enrichment.Threats.Select(t => t.Category).Distinct().Take(4).ToList();
            found.Add(new VectorDetail
            {
                Title = es ? "Amenazas activas detectadas en la red" : "Active threats detected on the network",
                Description = es
                    ? $"Nuestro escaneo detectó {enrichment.Threats.Count} firmas de amenazas distribuidas en {machines} equipos. Esto indica sistemas potencialmente comprometidos que requieren análisis forense inmediato."
                    : $"Our scan detected {enrichment.Threats.Count} threat signatures spread across {machines} machines. This indicates potentially compromised systems requiring immediate forensic analysis.",
                Impact = es
                    ? $"Sin contención, un atacante con presencia activa puede exfiltrar datos, desplegar ransomware o establecer persistencia en horas. El costo promedio de un incidente de ransomware para empresas de este tamaño es de USD 1.85M según IBM 2024."
                    : $"Without containment, an attacker with active presence can exfiltrate data, deploy ransomware or establish persistence within hours. The average cost of a ransomware incident for companies this size is USD 1.85M according to IBM 2024.",
                Evidence = es
                    ? $"{critCount} críticas, {highCount} altas. Categorías: {string.Join(", ", categories)}. Hosts afectados: {string.Join(", ", enrichment.Threats.Select(t => t.MachineId).Distinct().Take(3).Select((_, idx) => runs.ElementAtOrDefault(idx)?.Machine.Hostname ?? "unknown"))}."
                    : $"{critCount} critical, {highCount} high. Categories: {string.Join(", ", categories)}. Affected hosts: {string.Join(", ", enrichment.Threats.Select(t => t.MachineId).Distinct().Take(3).Select((_, idx) => runs.ElementAtOrDefault(idx)?.Machine.Hostname ?? "unknown"))}.",
                Priority = 100,
            });
        }

        // 2. Credential theft
        var credMachines = FailCount(r =>
            r.Name.Contains("WDigest", StringComparison.OrdinalIgnoreCase) ||
            r.Name.Contains("LSA", StringComparison.OrdinalIgnoreCase) ||
            r.Name.Contains("Credential", StringComparison.OrdinalIgnoreCase) ||
            r.Name.Contains("LM Hash", StringComparison.OrdinalIgnoreCase) ||
            r.Name.Contains("Clear Text", StringComparison.OrdinalIgnoreCase));
        if (credMachines > 0)
            found.Add(new VectorDetail
            {
                Title = es ? "Contraseñas expuestas en memoria" : "Credentials exposed in memory",
                Description = es
                    ? $"En {credMachines} equipos se detectaron configuraciones que dejan contraseñas cacheadas en texto claro en la memoria del sistema. Un atacante con privilegios locales puede extraerlas en segundos con herramientas como mimikatz, y luego moverse lateralmente usando esas credenciales robadas."
                    : $"On {credMachines} machines we found settings that cache passwords in clear text in system memory. An attacker with local privileges can extract them in seconds with tools like mimikatz, then move laterally using those stolen credentials.",
                Impact = es
                    ? $"Cada equipo comprometido se convierte en una fuente de credenciales para escalar privilegios. Si un Domain Admin inició sesión alguna vez en esos equipos, su contraseña podría estar cacheada y lista para ser extraída."
                    : $"Every compromised machine becomes a credential source for privilege escalation. If a Domain Admin ever logged into those machines, their password could be cached and ready for extraction.",
                Evidence = es
                    ? $"Controles fallidos: WDigest Authentication, LSA Protection, Credential Guard. {credMachines} de {runs.Count} equipos afectados ({(runs.Count > 0 ? credMachines * 100 / runs.Count : 0)}% de la flota)."
                    : $"Failed controls: WDigest Authentication, LSA Protection, Credential Guard. {credMachines} of {runs.Count} machines affected ({(runs.Count > 0 ? credMachines * 100 / runs.Count : 0)}% of fleet).",
                Priority = 95,
            });

        // 3. RDP exposed
        var rdpMachines = enrichment.Ports
            .Where(p => p.Port == 3389 && p.Risk != null)
            .Select(p => p.MachineId).Distinct().Count();
        if (rdpMachines > 0)
            found.Add(new VectorDetail
            {
                Title = es ? "Puertas RDP abiertas directamente a internet" : "RDP doors open directly to the Internet",
                Description = es
                    ? $"{rdpMachines} equipos tienen Escritorio Remoto (RDP) expuesto. RDP es el vector de entrada de ransomware #1 en los últimos 3 años. Un atacante probablemente ya está intentando contraseñas contra estos puertos en este momento."
                    : $"{rdpMachines} machines have Remote Desktop (RDP) exposed. RDP is the #1 ransomware entry vector of the past 3 years. An attacker is likely brute-forcing these ports right now.",
                Impact = es
                    ? $"Un solo par de credenciales débiles da acceso completo al escritorio del usuario: correo, archivos, acceso a la red interna. Las herramientas de ataque como Hydra y Crowbar pueden probar miles de combinaciones por minuto."
                    : $"A single weak credential pair grants full access to the user's desktop: email, files, internal network access. Attack tools like Hydra and Crowbar can test thousands of combinations per minute.",
                Evidence = es
                    ? $"Puerto 3389/TCP abierto en {rdpMachines} hosts. Hosts: {string.Join(", ", enrichment.Ports.Where(p => p.Port == 3389).Select(p => p.MachineId).Distinct().Take(3).Select((mid, _) => runs.FirstOrDefault(r => r.MachineId == mid)?.Machine.Hostname ?? mid.ToString()[..8]))}."
                    : $"Port 3389/TCP open on {rdpMachines} hosts. Hosts: {string.Join(", ", enrichment.Ports.Where(p => p.Port == 3389).Select(p => p.MachineId).Distinct().Take(3).Select((mid, _) => runs.FirstOrDefault(r => r.MachineId == mid)?.Machine.Hostname ?? mid.ToString()[..8]))}.",
                Priority = 90,
            });

        // 4. SMBv1
        var smbMachines = FailCount(r =>
            r.Name.Contains("SMBv1", StringComparison.OrdinalIgnoreCase) ||
            r.Name.Contains("SMB1", StringComparison.OrdinalIgnoreCase) ||
            r.ControlId.Contains("SMB1", StringComparison.OrdinalIgnoreCase));
        if (smbMachines > 0)
            found.Add(new VectorDetail
            {
                Title = es ? "Protocolo SMBv1 habilitado" : "SMBv1 protocol enabled",
                Description = es
                    ? $"El protocolo legacy SMBv1 está activo en {smbMachines} equipos. Es el mismo vector de explotación que permitió a NotPetya y WannaCry destruir miles de redes corporativas en 2017. Sigue siendo explotable hoy."
                    : $"The legacy SMBv1 protocol is active on {smbMachines} machines. This is the same exploit vector that allowed NotPetya and WannaCry to destroy thousands of corporate networks in 2017. It is still exploitable today.",
                Impact = es
                    ? $"SMBv1 permite ejecución remota de código sin autenticación (EternalBlue, MS17-010). Un atacante en la misma red puede tomar control total de cualquier equipo con SMBv1 en menos de 30 segundos. La propagación es automática."
                    : $"SMBv1 enables unauthenticated remote code execution (EternalBlue, MS17-010). An attacker on the same network can take full control of any SMBv1 machine in under 30 seconds. Propagation is automatic.",
                Evidence = es
                    ? $"{smbMachines} de {runs.Count} equipos con SMBv1 habilitado. Controles fallidos: SMBv1 Client, SMBv1 Server. Prioridad de remediación: inmediata."
                    : $"{smbMachines} of {runs.Count} machines with SMBv1 enabled. Failed controls: SMBv1 Client, SMBv1 Server. Remediation priority: immediate.",
                Priority = 85,
            });

        // 5. Kerberoastable accounts
        if (hygiene != null)
        {
            var kerberoast = hygiene.Findings.Count(f => f.Status == "Kerberoastable");
            if (kerberoast > 0)
                found.Add(new VectorDetail
                {
                    Title = es ? "Cuentas de servicio vulnerables a Kerberoast" : "Service accounts vulnerable to Kerberoast",
                    Description = es
                        ? $"{kerberoast} cuentas de servicio en Active Directory son vulnerables a Kerberoast. Sus contraseñas pueden ser crackeadas offline sin generar una sola alerta, y muchas tienen privilegios elevados en la red."
                        : $"{kerberoast} service accounts in Active Directory are vulnerable to Kerberoast. Their passwords can be cracked offline without generating a single alert, and many have elevated privileges across the network.",
                    Impact = es
                        ? $"Un atacante autenticado (incluso con una cuenta de usuario regular sin privilegios) puede solicitar tickets de servicio Kerberos para estas cuentas, extraer el hash del ticket y crackearlo offline con hashcat. Con GPUs modernas, contraseñas de 8 caracteres caen en minutos."
                        : $"An authenticated attacker (even with a regular unprivileged user account) can request Kerberos service tickets for these accounts, extract the ticket hash, and crack it offline with hashcat. With modern GPUs, 8-character passwords fall in minutes.",
                    Evidence = es
                        ? $"{kerberoast} cuentas con SPN registrado. Cuentas: {string.Join(", ", hygiene.Findings.Where(f => f.Status == "Kerberoastable").Take(3).Select(f => f.Name))}."
                        : $"{kerberoast} accounts with registered SPN. Accounts: {string.Join(", ", hygiene.Findings.Where(f => f.Status == "Kerberoastable").Take(3).Select(f => f.Name))}.",
                    Priority = 80,
                });

            // 6. LAPS missing
            var lapsMissing = hygiene.Findings.Count(f => f.Status == "NoLAPS");
            if (lapsMissing > 10)
                found.Add(new VectorDetail
                {
                    Title = es ? "Sin LAPS — contraseña de administrador local compartida" : "No LAPS — shared local admin password",
                    Description = es
                        ? $"{lapsMissing} equipos no tienen LAPS (Local Administrator Password Solution) desplegado. Probablemente usan la misma contraseña de administrador local en toda la flota, convirtiendo un solo equipo comprometido en acceso total a todos."
                        : $"{lapsMissing} machines don't have LAPS (Local Administrator Password Solution) deployed. They probably share the same local admin password fleet-wide, turning a single compromised machine into full access to every other one.",
                    Impact = es
                        ? $"Pass-the-hash: un atacante extrae el hash del administrador local de un equipo y lo reutiliza en todos los demás. No necesita crackear la contraseña. Con {lapsMissing} equipos sin LAPS, el movimiento lateral es trivial."
                        : $"Pass-the-hash: an attacker extracts the local admin hash from one machine and reuses it on every other one. No need to crack the password. With {lapsMissing} machines without LAPS, lateral movement is trivial.",
                    Evidence = es
                        ? $"{lapsMissing} de {hygiene.TotalMachines} equipos sin LAPS ({(hygiene.TotalMachines > 0 ? lapsMissing * 100 / hygiene.TotalMachines : 0)}%). Tecnología disponible: Windows LAPS (nativo en W11 23H2+) o Microsoft LAPS legacy."
                        : $"{lapsMissing} of {hygiene.TotalMachines} machines without LAPS ({(hygiene.TotalMachines > 0 ? lapsMissing * 100 / hygiene.TotalMachines : 0)}%). Available tech: Windows LAPS (native in W11 23H2+) or Microsoft LAPS legacy.",
                    Priority = 75,
                });

            // 7. Legacy domain functional level
            var domainLevel = hygiene.Findings.FirstOrDefault(f => f.Status == "DomainLevel");
            if (domainLevel != null && (domainLevel.Detail?.Contains("2008") == true || domainLevel.Detail?.Contains("2003") == true))
                found.Add(new VectorDetail
                {
                    Title = es ? "Dominio Active Directory en nivel funcional obsoleto" : "Active Directory domain at legacy functional level",
                    Description = es
                        ? $"El dominio está operando en nivel funcional {domainLevel.Detail}, bloqueando el uso de features de seguridad modernas como Protected Users, Authentication Policy Silos y Credential Guard. Heredaron todas las vulnerabilidades de una década atrás."
                        : $"The domain is running at functional level {domainLevel.Detail}, blocking modern security features like Protected Users, Authentication Policy Silos and Credential Guard. It inherits every vulnerability from a decade ago.",
                    Impact = es
                        ? $"El nivel funcional obsoleto impide desplegar: Protected Users (previene credential caching), Authentication Policies (Tier 0 isolation), gMSA (contraseñas de servicio auto-rotadas), y Kerberos AES-256 por defecto. El dominio está expuesto a ataques que Microsoft ya resolvió hace años."
                        : $"The legacy functional level prevents deploying: Protected Users (prevents credential caching), Authentication Policies (Tier 0 isolation), gMSA (auto-rotating service passwords), and Kerberos AES-256 by default. The domain is exposed to attacks Microsoft solved years ago.",
                    Evidence = es
                        ? $"Nivel funcional: {domainLevel.Detail}. Nivel mínimo recomendado: Windows Server 2016 (permite Protected Users y Authentication Policy Silos)."
                        : $"Functional level: {domainLevel.Detail}. Minimum recommended level: Windows Server 2016 (enables Protected Users and Authentication Policy Silos).",
                    Priority = 70,
                });
        }

        // 8. No audit logging
        var auditMachines = FailCount(r => r.Category.Contains("Audit", StringComparison.OrdinalIgnoreCase));
        var auditFailCount = allResults.Count(r => r.Category.Contains("Audit", StringComparison.OrdinalIgnoreCase) && r.Status == "fail");
        if (auditMachines > 0 && auditFailCount > 20)
            found.Add(new VectorDetail
            {
                Title = es ? "Sin registro de eventos de seguridad" : "Security event logging disabled",
                Description = es
                    ? $"En {auditMachines} equipos el registro de eventos críticos está deshabilitado o incompleto. Un atacante que entre a estos sistemas no dejaría un solo rastro para análisis forense. Literalmente no sabrías que pasó."
                    : $"On {auditMachines} machines the logging of critical events is disabled or incomplete. An attacker breaching these systems would leave no forensic trail whatsoever. You would literally not know what happened.",
                Impact = es
                    ? $"Sin logs de seguridad, un incidente se detecta días o semanas después (si se detecta). El costo de un breach sin log retention adecuado es 2.7x mayor según Ponemon 2024 porque no se puede determinar el alcance del compromiso."
                    : $"Without security logs, an incident is detected days or weeks later (if at all). The cost of a breach without proper log retention is 2.7x higher per Ponemon 2024 because the scope of compromise cannot be determined.",
                Evidence = es
                    ? $"{auditFailCount} controles de auditoría fallidos en {auditMachines} equipos. Categorías afectadas: Logon/Logoff, Object Access, Privilege Use, Policy Change."
                    : $"{auditFailCount} audit controls failed on {auditMachines} machines. Affected categories: Logon/Logoff, Object Access, Privilege Use, Policy Change.",
                Priority = 65,
            });

        // 9. BitLocker disabled
        var noBitlocker = runs.Count(r => r.Machine.Bitlocker != true);
        if (noBitlocker > 0 && noBitlocker >= runs.Count * 0.3)
            found.Add(new VectorDetail
            {
                Title = es ? "Discos sin cifrar" : "Unencrypted drives",
                Description = es
                    ? $"{noBitlocker} equipos tienen los discos sin cifrar. El robo o pérdida de un solo dispositivo expone toda la información almacenada: correos, archivos, credenciales guardadas, claves de API."
                    : $"{noBitlocker} machines have unencrypted drives. The theft or loss of a single device exposes every piece of data stored on it: emails, files, saved credentials, API keys.",
                Impact = es
                    ? $"Sin cifrado, un disco extraído de un equipo robado puede ser leído en minutos conectando a otro sistema. Toda la información de la empresa queda expuesta sin posibilidad de contención. HIPAA y PCI-DSS requieren cifrado en reposo como control obligatorio."
                    : $"Without encryption, a drive removed from a stolen machine can be read in minutes by connecting to another system. All company data is exposed with no containment possible. HIPAA and PCI-DSS require encryption at rest as a mandatory control.",
                Evidence = es
                    ? $"{noBitlocker} de {runs.Count} equipos sin BitLocker ({(runs.Count > 0 ? noBitlocker * 100 / runs.Count : 0)}%). TPM disponible en {runs.Count(r => r.Machine.TpmPresent == true)} equipos (requisito para BitLocker sin PIN)."
                    : $"{noBitlocker} of {runs.Count} machines without BitLocker ({(runs.Count > 0 ? noBitlocker * 100 / runs.Count : 0)}%). TPM available on {runs.Count(r => r.Machine.TpmPresent == true)} machines (required for BitLocker without PIN).",
                Priority = 60,
            });

        return found
            .OrderByDescending(v => v.Priority)
            .Take(6)
            .ToList();
    }

    private class VectorDetail
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Impact { get; set; } = "";
        public string? Evidence { get; set; }
        public int Priority { get; set; }
    }
}

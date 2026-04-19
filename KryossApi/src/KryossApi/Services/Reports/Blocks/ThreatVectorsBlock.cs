using System.Text;

namespace KryossApi.Services.Reports.Blocks;

/// <summary>
/// Renders the top 4 critical compromise vectors in business language.
/// Extracted from BuildOrgPresalesOpenerReport / DetectCompromiseVectors.
/// Audience: non-technical operations/management prospect.
/// </summary>
public class ThreatVectorsBlock : IReportBlock
{
    public string Render(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        var es = options.IsSpanish;
        var brand = data.Branding;

        var vectors = DetectCompromiseVectors(data, es);

        sb.AppendLine("<div class='headline-findings'>");
        int n = 1;
        foreach (var v in vectors)
        {
            sb.AppendLine("<div class='headline-item'>");
            sb.AppendLine($"<div class='headline-icon'>{n++}</div>");
            sb.AppendLine("<div class='headline-text'>");
            sb.AppendLine($"<strong>{ReportHelpers.HtmlEncode(v.Title)}</strong><br>");
            sb.AppendLine(ReportHelpers.HtmlEncode(v.Description));
            sb.AppendLine("</div>");
            sb.AppendLine("</div>");
        }
        if (vectors.Count == 0)
        {
            sb.AppendLine("<div class='headline-item' style='background:#f0fdf4;border-color:#006536'>");
            sb.AppendLine("<div class='headline-icon' style='background:#006536'>&#10003;</div>");
            sb.AppendLine("<div class='headline-text'>");
            sb.AppendLine($"<strong>{ReportHelpers.HtmlEncode(es ? "Sin vectores cr\u00edticos detectados" : "No critical compromise vectors detected")}</strong><br>");
            sb.AppendLine(ReportHelpers.HtmlEncode(es
                ? "La postura general es saludable. Igual vale la pena revisar los hallazgos medios y bajos para mantener la madurez en el tiempo."
                : "The overall posture is healthy. Still worth reviewing the medium and low severity findings to sustain maturity over time."));
            sb.AppendLine("</div></div>");
        }
        sb.AppendLine("</div>");

        // CTA block
        sb.AppendLine("<div class='cta-box'>");
        sb.AppendLine($"<p style='font-size:11px;font-weight:700;letter-spacing:0.12em;text-transform:uppercase;color:{brand.AccentColor};margin-bottom:4px'>{ReportHelpers.HtmlEncode(es ? "LOS PR\u00d3XIMOS 90 D\u00cdAS" : "THE NEXT 90 DAYS")}</p>");
        sb.AppendLine($"<p style='font-size:17px;font-weight:700;margin-bottom:10px'>{ReportHelpers.HtmlEncode(es ? "Antes de romper nada." : "Before you disable anything.")}</p>");
        if (es)
        {
            sb.AppendLine($"<p style='font-size:12px;line-height:1.65;margin-bottom:12px;color:#d4d4d4;text-align:left'>Apagar SMBv1, NTLM o cualquier protocolo legacy sin entender qui\u00e9n depende de ellos rompe aplicaciones cr\u00edticas \u2014 y esa ca\u00edda es suya. Nuestra respuesta es la <strong style='color:{brand.AccentColor}'>Auditor\u00eda de Depreciaci\u00f3n Segura de 90 D\u00edas</strong>: desplegamos un motor de telemetr\u00eda pasiva que mapea, sin interrumpir a nadie, exactamente qu\u00e9 sistemas, usuarios y procesos dependen de cada protocolo legacy que hoy lo expone. Al d\u00eda 90 usted recibe el plan de depreciaci\u00f3n con <strong style='color:#fff'>cero ambig\u00fcedad</strong> y <strong style='color:#fff'>cero downtime operacional</strong>.</p>");
            sb.AppendLine($"<p style='font-size:13px;margin:0;padding-top:10px;border-top:1px solid #666'>Reserve 30 minutos con <strong>{ReportHelpers.HtmlEncode(brand.CompanyName)}</strong> para iniciar la telemetr\u00eda.</p>");
        }
        else
        {
            sb.AppendLine($"<p style='font-size:12px;line-height:1.65;margin-bottom:12px;color:#d4d4d4;text-align:left'>Turning off SMBv1, NTLM or any legacy protocol without knowing who depends on them breaks critical applications \u2014 and that outage is yours to own. Our answer is the <strong style='color:{brand.AccentColor}'>90-Day Safe Deprecation Audit</strong>: we deploy a passive telemetry engine that maps, without interrupting anyone, exactly which systems, users and processes rely on each legacy protocol exposing you today. On day 90 you receive a deprecation plan with <strong style='color:#fff'>zero ambiguity</strong> and <strong style='color:#fff'>zero operational downtime</strong>.</p>");
            sb.AppendLine($"<p style='font-size:13px;margin:0;padding-top:10px;border-top:1px solid #666'>Book 30 minutes with <strong>{ReportHelpers.HtmlEncode(brand.CompanyName)}</strong> to start the telemetry.</p>");
        }
        sb.AppendLine("</div>");

        return sb.ToString();
    }

    private static List<(string Title, string Description)> DetectCompromiseVectors(ReportData data, bool es)
    {
        var found = new List<(string Title, string Description, int Priority)>();
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
            found.Add((
                es ? "Amenazas activas detectadas en la red" : "Active threats detected on the network",
                es ? $"Nuestro escaneo detect\u00f3 {enrichment.Threats.Count} firmas de amenazas distribuidas en {machines} equipos. Esto indica sistemas potencialmente comprometidos que requieren an\u00e1lisis forense inmediato."
                   : $"Our scan detected {enrichment.Threats.Count} threat signatures spread across {machines} machines. This indicates potentially compromised systems requiring immediate forensic analysis.",
                100));
        }

        // 2. Credential theft (WDigest, LSA, LM Hash, Clear Text)
        var credMachines = FailCount(r =>
            r.Name.Contains("WDigest",    StringComparison.OrdinalIgnoreCase) ||
            r.Name.Contains("LSA",        StringComparison.OrdinalIgnoreCase) ||
            r.Name.Contains("Credential", StringComparison.OrdinalIgnoreCase) ||
            r.Name.Contains("LM Hash",    StringComparison.OrdinalIgnoreCase) ||
            r.Name.Contains("Clear Text", StringComparison.OrdinalIgnoreCase));
        if (credMachines > 0)
            found.Add((
                es ? "Contrase\u00f1as expuestas en memoria" : "Credentials exposed in memory",
                es ? $"En {credMachines} equipos se detectaron configuraciones que dejan contrase\u00f1as cacheadas en texto claro en la memoria del sistema. Un atacante con privilegios locales puede extraerlas en segundos con herramientas como mimikatz, y luego moverse lateralmente usando esas credenciales robadas."
                   : $"On {credMachines} machines we found settings that cache passwords in clear text in system memory. An attacker with local privileges can extract them in seconds with tools like mimikatz, then move laterally using those stolen credentials.",
                95));

        // 3. RDP exposed
        var rdpMachines = enrichment.Ports
            .Where(p => p.Port == 3389 && p.Risk != null)
            .Select(p => p.MachineId).Distinct().Count();
        if (rdpMachines > 0)
            found.Add((
                es ? "Puertas RDP abiertas directamente a internet" : "RDP doors open directly to the Internet",
                es ? $"{rdpMachines} equipos tienen Escritorio Remoto (RDP) expuesto. RDP es el vector de entrada de ransomware #1 en los \u00faltimos 3 a\u00f1os. Un atacante probablemente ya est\u00e1 intentando contrase\u00f1as contra estos puertos en este momento."
                   : $"{rdpMachines} machines have Remote Desktop (RDP) exposed. RDP is the #1 ransomware entry vector of the past 3 years. An attacker is likely brute-forcing these ports right now.",
                90));

        // 4. SMBv1
        var smbMachines = FailCount(r =>
            r.Name.Contains("SMBv1", StringComparison.OrdinalIgnoreCase) ||
            r.Name.Contains("SMB1",  StringComparison.OrdinalIgnoreCase) ||
            r.ControlId.Contains("SMB1", StringComparison.OrdinalIgnoreCase));
        if (smbMachines > 0)
            found.Add((
                es ? "Protocolo SMBv1 habilitado" : "SMBv1 protocol enabled",
                es ? $"El protocolo legacy SMBv1 est\u00e1 activo en {smbMachines} equipos. Es el mismo vector de explotaci\u00f3n que permiti\u00f3 a NotPetya y WannaCry destruir miles de redes corporativas en 2017. Sigue siendo explotable hoy."
                   : $"The legacy SMBv1 protocol is active on {smbMachines} machines. This is the same exploit vector that allowed NotPetya and WannaCry to destroy thousands of corporate networks in 2017. It is still exploitable today.",
                85));

        // 5. Kerberoastable accounts
        if (hygiene != null)
        {
            var kerberoast = hygiene.Findings.Count(f => f.Status == "Kerberoastable");
            if (kerberoast > 0)
                found.Add((
                    es ? "Cuentas de servicio vulnerables a Kerberoast" : "Service accounts vulnerable to Kerberoast",
                    es ? $"{kerberoast} cuentas de servicio en Active Directory son vulnerables a Kerberoast. Sus contrase\u00f1as pueden ser crackeadas offline sin generar una sola alerta, y muchas tienen privilegios elevados en la red."
                       : $"{kerberoast} service accounts in Active Directory are vulnerable to Kerberoast. Their passwords can be cracked offline without generating a single alert, and many have elevated privileges across the network.",
                    80));

            // 6. LAPS missing
            var lapsMissing = hygiene.Findings.Count(f => f.Status == "NoLAPS");
            if (lapsMissing > 10)
                found.Add((
                    es ? "Sin LAPS \u2014 contrase\u00f1a de administrador local compartida" : "No LAPS \u2014 shared local admin password",
                    es ? $"{lapsMissing} equipos no tienen LAPS (Local Administrator Password Solution) desplegado. Probablemente usan la misma contrase\u00f1a de administrador local en toda la flota, convirtiendo un solo equipo comprometido en acceso total a todos."
                       : $"{lapsMissing} machines don't have LAPS (Local Administrator Password Solution) deployed. They probably share the same local admin password fleet-wide, turning a single compromised machine into full access to every other one.",
                    75));

            // 7. Legacy domain functional level
            var domainLevel = hygiene.Findings.FirstOrDefault(f => f.Status == "DomainLevel");
            if (domainLevel != null && (domainLevel.Detail?.Contains("2008") == true || domainLevel.Detail?.Contains("2003") == true))
                found.Add((
                    es ? "Dominio Active Directory en nivel funcional obsoleto" : "Active Directory domain at legacy functional level",
                    es ? $"El dominio est\u00e1 operando en nivel funcional {domainLevel.Detail}, bloqueando el uso de features de seguridad modernas como Protected Users, Authentication Policy Silos y Credential Guard. Heredaron todas las vulnerabilidades de una d\u00e9cada atr\u00e1s."
                       : $"The domain is running at functional level {domainLevel.Detail}, blocking modern security features like Protected Users, Authentication Policy Silos and Credential Guard. It inherits every vulnerability from a decade ago.",
                    70));
        }

        // 8. No audit logging
        var auditMachines = FailCount(r => r.Category.Contains("Audit", StringComparison.OrdinalIgnoreCase));
        if (auditMachines > 0 && allResults.Count(r => r.Category.Contains("Audit", StringComparison.OrdinalIgnoreCase) && r.Status == "fail") > 20)
            found.Add((
                es ? "Sin registro de eventos de seguridad" : "Security event logging disabled",
                es ? $"En {auditMachines} equipos el registro de eventos cr\u00edticos est\u00e1 deshabilitado o incompleto. Un atacante que entre a estos sistemas no dejar\u00eda un solo rastro para an\u00e1lisis forense. Literalmente no sabr\u00edas que pas\u00f3."
                   : $"On {auditMachines} machines the logging of critical events is disabled or incomplete. An attacker breaching these systems would leave no forensic trail whatsoever. You would literally not know what happened.",
                65));

        // 9. BitLocker disabled
        var noBitlocker = runs.Count(r => r.Machine.Bitlocker != true);
        if (noBitlocker > 0 && noBitlocker >= runs.Count * 0.3)
            found.Add((
                es ? "Discos sin cifrar" : "Unencrypted drives",
                es ? $"{noBitlocker} equipos tienen los discos sin cifrar. El robo o p\u00e9rdida de un solo dispositivo expone toda la informaci\u00f3n almacenada: correos, archivos, credenciales guardadas, claves de API."
                   : $"{noBitlocker} machines have unencrypted drives. The theft or loss of a single device exposes every piece of data stored on it: emails, files, saved credentials, API keys.",
                60));

        return found
            .OrderByDescending(v => v.Priority)
            .Take(4)
            .Select(v => (v.Title, v.Description))
            .ToList();
    }
}

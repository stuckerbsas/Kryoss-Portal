using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class RiskSummaryBlock : IFlowBlock
{
    public string? SectionTitle(ReportOptions options) =>
        options.IsSpanish ? "Evaluación de Riesgos" : "Risk Assessment";

    public int EstimateHeight(ReportData data)
    {
        int h = 200;
        if (data.Enrichment.Threats.Count > 0) h += 200;
        if (data.Enrichment.Ports.Count > 0) h += 200;
        return h;
    }

    public string RenderContent(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        var es = options.IsSpanish;

        var threats = data.Enrichment.Threats;
        var ports = data.Enrichment.Ports;
        var controls = data.ControlResults;

        // Attack surface KPIs
        var openPorts = ports.Count(p => p.Status == "open");
        var rdpExposed = ports.Count(p => p.Port == 3389 && p.Status == "open");
        var threatCount = threats.Count;
        var affectedMachines = threats.Select(t => t.MachineId).Distinct().Count();
        var criticalControls = controls.Where(r => r.Status == "fail" && r.Severity == "critical").Select(r => r.ControlId).Distinct().Count();

        sb.AppendLine("<div style='display:grid;grid-template-columns:repeat(5,1fr);gap:8px;margin-bottom:16px'>");
        RenderKpi(sb, threatCount.ToString(), es ? "Amenazas" : "Threats", threatCount > 0 ? "#991B1B" : "#15803D");
        RenderKpi(sb, affectedMachines.ToString(), es ? "Equipos Afectados" : "Affected Machines", affectedMachines > 0 ? "#991B1B" : "#15803D");
        RenderKpi(sb, openPorts.ToString(), es ? "Puertos Abiertos" : "Open Ports", openPorts > 20 ? "#B45309" : "#15803D");
        RenderKpi(sb, rdpExposed.ToString(), "RDP Exposed", rdpExposed > 0 ? "#991B1B" : "#15803D");
        RenderKpi(sb, criticalControls.ToString(), es ? "Fallos Críticos" : "Critical Failures", criticalControls > 0 ? "#991B1B" : "#15803D");
        sb.AppendLine("</div>");

        // Threats detail
        if (threats.Count > 0)
        {
            sb.AppendLine($"<h3>{(es ? "Amenazas Detectadas" : "Detected Threats")}</h3>");
            sb.AppendLine("<table class='data-table'><thead><tr>");
            sb.AppendLine($"<th>{(es ? "Equipo" : "Machine")}</th><th>{(es ? "Categoría" : "Category")}</th><th>{(es ? "Nombre" : "Name")}</th><th>{(es ? "Vector" : "Vector")}</th><th>{(es ? "Severidad" : "Severity")}</th>");
            sb.AppendLine("</tr></thead><tbody>");

            foreach (var t in threats.OrderByDescending(t => t.Severity == "critical" ? 3 : t.Severity == "high" ? 2 : 1).Take(15))
            {
                var sevClass = t.Severity is "critical" or "high" ? "fail" : t.Severity == "medium" ? "warn" : "";
                var hostname = data.Runs.FirstOrDefault(r => r.MachineId == t.MachineId)?.Machine?.Hostname ?? t.MachineId.ToString()[..8];
                sb.AppendLine($"<tr>");
                sb.AppendLine($"<td>{ReportHelpers.HtmlEncode(hostname)}</td>");
                sb.AppendLine($"<td>{ReportHelpers.HtmlEncode(ReportTranslations.TranslateCategory(t.Category, options.Lang))}</td>");
                sb.AppendLine($"<td>{ReportHelpers.HtmlEncode(t.ThreatName)}</td>");
                sb.AppendLine($"<td style='font-size:7pt;max-width:200px;overflow:hidden;text-overflow:ellipsis'>{ReportHelpers.HtmlEncode(t.Vector)}</td>");
                sb.AppendLine($"<td class='{sevClass}'>{ReportHelpers.HtmlEncode(t.Severity)}</td>");
                sb.AppendLine("</tr>");
            }
            if (threats.Count > 15)
                sb.AppendLine($"<tr><td colspan='5' style='color:#64748B;font-style:italic'>...{(es ? $"y {threats.Count - 15} más" : $"and {threats.Count - 15} more")}</td></tr>");
            sb.AppendLine("</tbody></table>");
        }

        // Open ports by service
        if (ports.Count > 0)
        {
            sb.AppendLine($"<h3>{(es ? "Puertos Abiertos por Servicio" : "Open Ports by Service")}</h3>");
            var portGroups = ports.Where(p => p.Status == "open")
                .GroupBy(p => new { p.Port, p.Service })
                .OrderByDescending(g => g.Key.Port == 3389 ? 1000 : g.Key.Port == 445 ? 999 : g.Count())
                .Take(15)
                .ToList();

            sb.AppendLine("<table class='data-table'><thead><tr>");
            sb.AppendLine($"<th>{(es ? "Puerto" : "Port")}</th><th>{(es ? "Servicio" : "Service")}</th><th>{(es ? "Equipos" : "Machines")}</th><th>{(es ? "Riesgo" : "Risk")}</th>");
            sb.AppendLine("</tr></thead><tbody>");

            foreach (var g in portGroups)
            {
                var risk = g.Key.Port switch { 3389 => "Critical", 445 => "High", 23 => "High", 21 => "High", 135 or 139 => "Medium", _ => "Low" };
                var riskClass = risk == "Critical" ? "fail" : risk == "High" ? "warn" : "";
                sb.AppendLine($"<tr>");
                sb.AppendLine($"<td>{g.Key.Port}</td>");
                sb.AppendLine($"<td>{ReportHelpers.HtmlEncode(g.Key.Service ?? "unknown")}</td>");
                sb.AppendLine($"<td>{g.Count()}</td>");
                sb.AppendLine($"<td class='{riskClass}'>{risk}</td>");
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</tbody></table>");
        }

        // Attack vectors (from credential/protocol controls)
        var attackVectors = new List<(string Name, int Count, string Severity)>();

        var credFails = controls.Where(r => r.Status == "fail" &&
            (r.Name.Contains("WDigest", StringComparison.OrdinalIgnoreCase) ||
             r.Name.Contains("LSA", StringComparison.OrdinalIgnoreCase) ||
             r.Name.Contains("Credential", StringComparison.OrdinalIgnoreCase)))
            .Select(r => r.RunId).Distinct().Count();
        if (credFails > 0) attackVectors.Add((es ? "Credenciales en Memoria" : "Credentials in Memory", credFails, "critical"));

        var smbFails = controls.Where(r => r.Status == "fail" && r.ControlId.Contains("SMB1")).Select(r => r.RunId).Distinct().Count();
        if (smbFails > 0) attackVectors.Add(("SMBv1", smbFails, "critical"));

        var ntlmFails = controls.Where(r => r.Status == "fail" && r.ControlId.Contains("NTLM")).Select(r => r.RunId).Distinct().Count();
        if (ntlmFails > 0) attackVectors.Add(("NTLMv1", ntlmFails, "high"));

        var firewallFails = controls.Where(r => r.Status == "fail" && r.Category.Contains("Firewall", StringComparison.OrdinalIgnoreCase)).Select(r => r.RunId).Distinct().Count();
        if (firewallFails > 0) attackVectors.Add((es ? "Firewall Deshabilitado" : "Firewall Disabled", firewallFails, "high"));

        if (attackVectors.Count > 0)
        {
            sb.AppendLine($"<h3>{(es ? "Vectores de Ataque" : "Attack Vectors")}</h3>");
            sb.AppendLine("<div style='display:grid;grid-template-columns:repeat(2,1fr);gap:8px'>");
            foreach (var (name, count, sev) in attackVectors.OrderByDescending(v => v.Severity == "critical" ? 2 : 1))
            {
                var color = sev == "critical" ? "#991B1B" : "#B45309";
                sb.AppendLine($"<div style='padding:10px;border:1px solid #E2E8F0;border-left:3px solid {color};border-radius:4px'>");
                sb.AppendLine($"<div style='display:flex;justify-content:space-between'>");
                sb.AppendLine($"<strong style='font-size:9pt'>{ReportHelpers.HtmlEncode(name)}</strong>");
                sb.AppendLine($"<span style='font-weight:700;color:{color}'>{count} {(es ? "equipos" : "machines")}</span>");
                sb.AppendLine("</div></div>");
            }
            sb.AppendLine("</div>");
        }

        return sb.ToString();
    }

    public string Render(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb, SectionTitle(options)!, data.Branding);
        sb.AppendLine("<div class='pb'>");
        sb.Append(RenderContent(data, options));
        sb.AppendLine("</div></div>");
        return sb.ToString();
    }

    private static void RenderKpi(StringBuilder sb, string value, string label, string color)
    {
        sb.AppendLine($"<div style='text-align:center;padding:8px;border:1px solid #E2E8F0;border-top:2px solid {color};border-radius:4px'>");
        sb.AppendLine($"<div style='font-size:16pt;font-weight:800;color:{color}'>{value}</div>");
        sb.AppendLine($"<div style='font-size:7pt;color:#64748B'>{ReportHelpers.HtmlEncode(label)}</div>");
        sb.AppendLine("</div>");
    }
}

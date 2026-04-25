using System.Text;
using KryossApi.Data.Entities;

namespace KryossApi.Services.Reports.Blocks;

public class ServiceCatalogBlock : IReportBlock
{
    private readonly bool _showPricing;
    private readonly bool _tierGrid;

    public ServiceCatalogBlock(bool showPricing = true, bool tierGrid = false)
    {
        _showPricing = showPricing;
        _tierGrid = tierGrid;
    }

    public string Render(ReportData data, ReportOptions options)
    {
        if (data.ServiceCatalog.Count == 0) return "";

        return _tierGrid ? RenderTierGrid(data, options) : RenderTable(data, options);
    }

    private string RenderTable(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        var es = options.IsSpanish;
        var rate = data.Rate?.HourlyRate ?? 150m;
        var margin = data.Rate?.MarginPct ?? 0m;
        var currency = data.Rate?.Currency ?? "USD";

        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb, es ? "Catálogo de Servicios" : "Service Catalog", data.Branding);
        sb.AppendLine("<div class='pb'>");

        sb.AppendLine("<table class='data-table'><thead><tr>");
        sb.AppendLine($"<th>{(es ? "Servicio" : "Service")}</th>");
        sb.AppendLine($"<th>{(es ? "Afectados" : "Affected")}</th>");
        sb.AppendLine($"<th>{(es ? "Horas Est." : "Est. Hours")}</th>");
        if (_showPricing)
            sb.AppendLine($"<th>{(es ? "Costo" : "Cost")}</th>");
        sb.AppendLine("</tr></thead><tbody>");

        decimal totalHours = 0, totalCost = 0;
        foreach (var svc in data.ServiceCatalog)
        {
            var affected = CountAffected(svc, data);
            if (affected == 0) continue;

            var hours = affected * svc.BaseHours;
            var cost = hours * rate * (1 + margin / 100);
            totalHours += hours;
            totalCost += cost;

            var name = es ? svc.NameEs : svc.NameEn;
            var sevClass = svc.Severity == "critical" ? "fail" : svc.Severity == "high" ? "warn" : "";

            sb.AppendLine($"<tr class='{sevClass}'>");
            sb.AppendLine($"<td>{ReportHelpers.HtmlEncode(name)}</td>");
            sb.AppendLine($"<td>{affected}</td>");
            sb.AppendLine($"<td>{hours:F1}h</td>");
            if (_showPricing)
                sb.AppendLine($"<td>{currency} {cost:N0}</td>");
            sb.AppendLine("</tr>");
        }

        sb.AppendLine("<tr style='font-weight:700;border-top:2px solid #1E293B;'>");
        sb.AppendLine($"<td><strong>Total</strong></td><td></td><td>{totalHours:F1}h</td>");
        if (_showPricing)
            sb.AppendLine($"<td>{currency} {totalCost:N0}</td>");
        sb.AppendLine("</tr>");
        sb.AppendLine("</tbody></table>");
        sb.AppendLine("</div></div>");

        return sb.ToString();
    }

    private string RenderTierGrid(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        var es = options.IsSpanish;
        var rate = data.Rate?.HourlyRate ?? 150m;
        var margin = data.Rate?.MarginPct ?? 0m;
        var currency = data.Rate?.Currency ?? "USD";

        var items = data.ServiceCatalog
            .Select(svc => new TierItem(svc, CountAffected(svc, data)))
            .Where(x => x.Affected > 0)
            .OrderByDescending(x => x.Svc.Severity == "critical" ? 4 : x.Svc.Severity == "high" ? 3 : 2)
            .ToList();

        if (items.Count == 0) return "";

        var tierCritical = items.Where(x => x.Svc.Severity == "critical").ToList();
        var tierHigh = items.Where(x => x.Svc.Severity == "high").ToList();
        var tierOther = items.Where(x => x.Svc.Severity is not "critical" and not "high").ToList();

        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb, es ? "Paquetes de Remediación" : "Remediation Packages", data.Branding);
        sb.AppendLine("<div class='pb'>");

        sb.AppendLine("<div class='tier-grid'>");

        RenderTierCard(sb, es,
            es ? "Urgente" : "Urgent",
            es ? "Riesgos críticos que requieren acción inmediata" : "Critical risks requiring immediate action",
            tierCritical, rate, margin, currency, "#991B1B", true);

        RenderTierCard(sb, es,
            es ? "Prioritario" : "Priority",
            es ? "Riesgos altos a resolver en 30 días" : "High risks to resolve within 30 days",
            tierHigh, rate, margin, currency, "#B45309", false);

        RenderTierCard(sb, es,
            es ? "Mantenimiento" : "Maintenance",
            es ? "Mejoras de postura a implementar en 90 días" : "Posture improvements to implement within 90 days",
            tierOther, rate, margin, currency, "#15803D", false);

        sb.AppendLine("</div>");
        sb.AppendLine("</div></div>");

        return sb.ToString();
    }

    private record TierItem(ServiceCatalogItem Svc, int Affected);

    private void RenderTierCard(StringBuilder sb, bool es, string tierName, string subtitle,
        List<TierItem> items, decimal rate, decimal margin, string currency, string accentColor, bool highlight)
    {
        var highlightClass = highlight ? " highlight" : "";
        sb.AppendLine($"<div class='tier-card{highlightClass}' style='border-top:4px solid {accentColor}'>");
        sb.AppendLine("<div class='tier-header'>");
        sb.AppendLine($"<div class='tier-name'>{ReportHelpers.HtmlEncode(tierName)}</div>");
        sb.AppendLine($"<div style='font-size:9pt;color:#64748b'>{ReportHelpers.HtmlEncode(subtitle)}</div>");

        decimal totalCost = 0;
        var bullets = new List<string>();
        foreach (var x in items)
        {
            var hours = x.Affected * x.Svc.BaseHours;
            var cost = hours * rate * (1 + margin / 100);
            totalCost += cost;
            var name = es ? x.Svc.NameEs : x.Svc.NameEn;
            bullets.Add($"{name} ({x.Affected})");
        }

        if (_showPricing && totalCost > 0)
            sb.AppendLine($"<div class='tier-price'>{currency} {totalCost:N0}</div>");

        sb.AppendLine("</div>");

        if (bullets.Count > 0)
        {
            sb.AppendLine("<ul class='tier-bullets'>");
            foreach (var b in bullets)
                sb.AppendLine($"<li>{ReportHelpers.HtmlEncode(b)}</li>");
            sb.AppendLine("</ul>");
        }
        else
        {
            sb.AppendLine($"<div style='font-size:9pt;color:#94a3b8;padding:8px 0'>{(es ? "Sin hallazgos en esta categoría" : "No findings in this tier")}</div>");
        }

        sb.AppendLine("</div>");
    }

    internal static int CountAffected(ServiceCatalogItem svc, ReportData data) => svc.CategoryCode switch
    {
        "disk_encryption" => data.Runs.Count(r => r.Machine?.Bitlocker != true),
        "laps_deploy" => data.Hygiene?.Findings?.Count(f => f.Status == "NoLAPS") ?? 0,
        "endpoint_protection" => data.ControlResults
            .Where(r => r.Status == "fail" &&
                (r.Name.Contains("Defender", StringComparison.OrdinalIgnoreCase) ||
                 r.Name.Contains("antivirus", StringComparison.OrdinalIgnoreCase)))
            .Select(r => r.RunId).Distinct().Count(),
        "patch_management" => data.Runs.Count(r => IsLegacyOs(r.Machine?.OsName)),
        "protocol_hardening" => data.ControlResults
            .Where(r => r.Status == "fail" && (r.ControlId.Contains("NTLM") || r.ControlId.Contains("SMB1")))
            .Select(r => r.RunId).Distinct().Count(),
        "password_policy" => (data.Hygiene?.PwdNeverExpire ?? 0) > 0 ? 1 : 0,
        "privileged_access" => data.Hygiene?.Findings?.Count(f => f.Status is "PrivilegedAccount" or "LocalAdmin") ?? 0,
        "rdp_hardening" => data.Enrichment.Ports.Count(p => p.Port == 3389 && p.Status == "open"),
        "m365_security" => data.M365Connected && data.M365Findings.Any(f => f.Severity == "high" || f.Severity == "critical") ? 1 : 0,
        "azure_hardening" => data.CloudFindings?.Any(f => f.Area == "azure" && f.Status is "action_required" or "warning" or "disabled") == true ? 1 : 0,
        "firewall_hardening" => data.ControlResults
            .Where(r => r.Status == "fail" && r.Category.Contains("Firewall", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.RunId).Distinct().Count(),
        "audit_logging" => data.ControlResults.Any(r => r.Status == "fail" && r.Category.Contains("Audit", StringComparison.OrdinalIgnoreCase)) ? 1 : 0,
        "cert_hygiene" => data.ControlResults.Count(r => r.Status == "fail" && r.ControlId.StartsWith("BL-047")),
        "ad_restructuring" => data.Hygiene?.Findings?.Any(f => f.ObjectType == "DomainInfo" && f.Status != "OK") == true ? 1 : 0,
        _ => 0
    };

    private static bool IsLegacyOs(string? os) =>
        os != null && (os.Contains("2008") || os.Contains("2003") || os.Contains("Windows 7") || os.Contains("Vista") || os.Contains("Windows 8"));
}

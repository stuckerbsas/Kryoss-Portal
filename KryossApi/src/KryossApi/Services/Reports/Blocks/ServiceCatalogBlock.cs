using System.Text;
using KryossApi.Data.Entities;

namespace KryossApi.Services.Reports.Blocks;

public class ServiceCatalogBlock : IReportBlock
{
    private readonly bool _showPricing;
    public ServiceCatalogBlock(bool showPricing = true) => _showPricing = showPricing;

    public string Render(ReportData data, ReportOptions options)
    {
        if (data.ServiceCatalog.Count == 0) return "";

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
        "privileged_access" => data.Hygiene?.Findings?.Count(f => f.ObjectType == "PrivilegedAccount") ?? 0,
        "rdp_hardening" => data.Enrichment.Ports.Count(p => p.Port == 3389 && p.Status == "open"),
        "m365_security" => data.M365Connected && data.M365Findings.Any(f => f.Severity == "high" || f.Severity == "critical") ? 1 : 0,
        "azure_hardening" => data.CloudFindings?.Any(f => f.Area == "azure" && f.Status == "fail") == true ? 1 : 0,
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

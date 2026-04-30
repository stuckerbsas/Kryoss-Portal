using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class DcHealthBlock : IFlowBlock
{
    private readonly bool _compact;
    public DcHealthBlock(bool compact = false) => _compact = compact;

    public string? SectionTitle(ReportOptions options) =>
        options.IsSpanish ? "Salud del Controlador de Dominio" : "Domain Controller Health";

    public int EstimateHeight(ReportData data) => _compact ? 220 : 450;

    public string RenderContent(ReportData data, ReportOptions options)
    {
        if (!data.HasDcHealthData) return "";

        var es = options.IsSpanish;
        var dc = data.DcHealth!;
        var sb = new StringBuilder();

        // KPI row
        sb.AppendLine("<div style='display:grid;grid-template-columns:repeat(5,1fr);gap:8px;margin-bottom:12px'>");
        RenderKpi(sb, dc.SchemaVersionLabel ?? $"v{dc.SchemaVersion}", es ? "Schema AD" : "AD Schema", "#1e293b");
        RenderKpi(sb, dc.DcCount.ToString(), es ? "Controladores" : "Domain Controllers",
            dc.DcCount < 2 ? "#dc2626" : "#16a34a");
        RenderKpi(sb, dc.SiteCount.ToString(), es ? "Sitios AD" : "AD Sites", "#475569");
        RenderKpi(sb, dc.ReplFailureCount.ToString(), es ? "Fallas Replicación" : "Replication Failures",
            dc.ReplFailureCount > 0 ? "#dc2626" : "#16a34a");
        var fsmoColor = dc.FsmoSinglePoint ? "#dc2626" : "#16a34a";
        RenderKpi(sb, dc.FsmoSinglePoint ? (es ? "SÍ" : "YES") : "NO",
            es ? "FSMO Punto Único" : "FSMO Single Point", fsmoColor);
        sb.AppendLine("</div>");

        // Domain info
        sb.AppendLine("<div style='display:grid;grid-template-columns:1fr 1fr;gap:8px;margin-bottom:12px;font-size:8pt'>");
        sb.AppendLine($"<div><b>{(es ? "Bosque" : "Forest")}:</b> {ReportHelpers.HtmlEncode(dc.ForestName ?? "—")}</div>");
        sb.AppendLine($"<div><b>{(es ? "Dominio" : "Domain")}:</b> {ReportHelpers.HtmlEncode(dc.DomainName ?? "—")}</div>");
        sb.AppendLine($"<div><b>{(es ? "Nivel Funcional Bosque" : "Forest Level")}:</b> {ReportHelpers.HtmlEncode(dc.ForestLevel ?? "—")}</div>");
        sb.AppendLine($"<div><b>{(es ? "Nivel Funcional Dominio" : "Domain Level")}:</b> {ReportHelpers.HtmlEncode(dc.DomainLevel ?? "—")}</div>");
        sb.AppendLine("</div>");

        if (_compact) return sb.ToString();

        // FSMO roles table
        sb.AppendLine($"<div style='font-size:9pt;font-weight:600;margin:8px 0 4px'>{(es ? "Roles FSMO" : "FSMO Roles")}</div>");
        sb.AppendLine("<table style='width:100%;border-collapse:collapse;font-size:8pt'>");
        sb.AppendLine("<thead><tr style='background:#f1f5f9;text-align:left'>");
        sb.AppendLine($"<th style='padding:4px 6px'>{(es ? "Rol" : "Role")}</th>");
        sb.AppendLine($"<th style='padding:4px 6px'>{(es ? "Servidor" : "Server")}</th></tr></thead><tbody>");
        RenderFsmoRow(sb, "Schema Master", dc.SchemaMaster);
        RenderFsmoRow(sb, "Domain Naming Master", dc.DomainNamingMaster);
        RenderFsmoRow(sb, "PDC Emulator", dc.PdcEmulator);
        RenderFsmoRow(sb, "RID Master", dc.RidMaster);
        RenderFsmoRow(sb, "Infrastructure Master", dc.InfrastructureMaster);
        sb.AppendLine("</tbody></table>");

        if (dc.FsmoSinglePoint)
        {
            sb.AppendLine($"<div style='background:#fef2f2;border-left:3px solid #dc2626;padding:6px 10px;margin:8px 0;font-size:8pt'>");
            sb.AppendLine(es
                ? "⚠ Todos los roles FSMO están en un único servidor. Si este servidor falla, las operaciones críticas del dominio se detendrán."
                : "⚠ All FSMO roles are on a single server. If it fails, critical domain operations will stop.");
            sb.AppendLine("</div>");
        }

        // Replication partners
        if (dc.ReplicationPartners.Count > 0)
        {
            sb.AppendLine($"<div style='font-size:9pt;font-weight:600;margin:12px 0 4px'>{(es ? "Partners de Replicación" : "Replication Partners")}</div>");
            sb.AppendLine("<table style='width:100%;border-collapse:collapse;font-size:8pt'>");
            sb.AppendLine("<thead><tr style='background:#f1f5f9;text-align:left'>");
            sb.AppendLine($"<th style='padding:4px 6px'>{(es ? "Partner" : "Partner")}</th>");
            sb.AppendLine($"<th style='padding:4px 6px'>{(es ? "Dirección" : "Direction")}</th>");
            sb.AppendLine($"<th style='padding:4px 6px'>{(es ? "Último Éxito" : "Last Success")}</th>");
            sb.AppendLine($"<th style='padding:4px 6px'>{(es ? "Fallas" : "Failures")}</th>");
            sb.AppendLine($"<th style='padding:4px 6px'>{(es ? "Estado" : "Status")}</th>");
            sb.AppendLine("</tr></thead><tbody>");

            foreach (var p in dc.ReplicationPartners)
            {
                var ok = p.FailureCount == 0;
                var statusColor = ok ? "#16a34a" : "#dc2626";
                var statusLabel = ok ? (es ? "OK" : "OK") : (es ? "ERROR" : "ERROR");
                sb.AppendLine("<tr style='border-bottom:1px solid #e5e7eb'>");
                sb.AppendLine($"<td style='padding:3px 6px'>{ReportHelpers.HtmlEncode(p.PartnerHostname ?? "—")}</td>");
                sb.AppendLine($"<td style='padding:3px 6px'>{ReportHelpers.HtmlEncode(p.Direction ?? "—")}</td>");
                sb.AppendLine($"<td style='padding:3px 6px'>{(p.LastSuccess?.ToString("yyyy-MM-dd HH:mm") ?? "—")}</td>");
                sb.AppendLine($"<td style='padding:3px 6px'>{p.FailureCount}</td>");
                sb.AppendLine($"<td style='padding:3px 6px'><span style='background:{statusColor};color:#fff;padding:1px 6px;border-radius:3px;font-size:7pt'>{statusLabel}</span></td>");
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</tbody></table>");
        }

        return sb.ToString();
    }

    public string Render(ReportData data, ReportOptions options)
    {
        var content = RenderContent(data, options);
        if (string.IsNullOrEmpty(content)) return "";
        var sb = new StringBuilder();
        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb, SectionTitle(options)!, data.Branding);
        sb.AppendLine("<div class='pb'>");
        sb.Append(content);
        sb.AppendLine("</div></div>");
        return sb.ToString();
    }

    private static void RenderKpi(StringBuilder sb, string value, string label, string color)
    {
        sb.AppendLine($"<div style='border:1px solid #e5e7eb;border-radius:6px;padding:8px;text-align:center'>");
        sb.AppendLine($"<div style='font-size:16pt;font-weight:700;color:{color}'>{ReportHelpers.HtmlEncode(value)}</div>");
        sb.AppendLine($"<div style='font-size:7pt;color:#6b7280'>{ReportHelpers.HtmlEncode(label)}</div>");
        sb.AppendLine("</div>");
    }

    private static void RenderFsmoRow(StringBuilder sb, string role, string? server)
    {
        sb.AppendLine("<tr style='border-bottom:1px solid #e5e7eb'>");
        sb.AppendLine($"<td style='padding:3px 6px'>{ReportHelpers.HtmlEncode(role)}</td>");
        sb.AppendLine($"<td style='padding:3px 6px;font-family:monospace;font-size:7pt'>{ReportHelpers.HtmlEncode(server ?? "—")}</td>");
        sb.AppendLine("</tr>");
    }
}

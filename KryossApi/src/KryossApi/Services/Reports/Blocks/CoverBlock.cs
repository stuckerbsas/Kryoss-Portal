using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class CoverBlock : IReportBlock
{
    private readonly string _reportTypeKey;

    public CoverBlock(string reportTypeKey) => _reportTypeKey = reportTypeKey;

    public string Render(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        var es = options.IsSpanish;
        var brand = data.Branding;

        var reportTitle = GetTitle(es, options);
        var eyebrow = options.FrameworkName != null
            ? ReportHelpers.HtmlEncode(options.FrameworkName.ToUpperInvariant())
            : GetEyebrow(es);

        sb.AppendLine("<div class='cover'>");
        ReportHelpers.AppendRibbonSvg(sb);
        sb.AppendLine("<div class='cover-content'>");
        if (brand.LogoUrl is not null)
            sb.AppendLine($"<img src='{ReportHelpers.HtmlEncode(brand.LogoUrl)}' class='logo' alt='{ReportHelpers.HtmlEncode(brand.CompanyName)}'>");
        sb.AppendLine($"<p class='eyebrow'>{eyebrow}</p>");
        sb.AppendLine($"<h1>{ReportHelpers.HtmlEncode(reportTitle)}</h1>");
        sb.AppendLine($"<h2>{ReportHelpers.HtmlEncode(data.Org.Name)}</h2>");
        var dateStr = es ? data.ScanDate.ToString("dd 'de' MMMM 'de' yyyy") : data.ScanDate.ToString("MMMM dd, yyyy");
        var devicesLabel = es ? "dispositivos evaluados" : "devices assessed";
        sb.AppendLine($"<p class='meta'>{dateStr} &mdash; {data.TotalMachines} {devicesLabel}</p>");
        sb.AppendLine($"<div class='grade-badge grade-{data.OrgGrade.Replace("+", "plus")}'>{ReportHelpers.HtmlEncode(data.OrgGrade)}</div>");
        sb.AppendLine($"<p class='score'>{data.AvgScore:F1}%</p>");
        sb.AppendLine("</div></div>");

        return sb.ToString();
    }

    private string GetTitle(bool es, ReportOptions options) => _reportTypeKey switch
    {
        "c-level" => es ? "Informe Ejecutivo C-Level" : "C-Level Security Briefing",
        "technical" => options.FrameworkName != null
            ? $"{options.FrameworkName} {(es ? "Informe Técnico" : "Technical Report")}"
            : (es ? "Informe Técnico de Seguridad" : "Security Technical Report"),
        "preventa-opener" => es ? "Evaluación de Riesgo" : "Risk Assessment",
        "preventa-detailed" => es ? "Propuesta de Seguridad" : "Security Proposal",
        "monthly" => es ? "Informe de Progreso Mensual" : "Monthly Progress Report",
        "framework" => $"{options.FrameworkName} {(es ? "Informe de Cumplimiento" : "Compliance Report")}",
        "proposal" => es ? "Propuesta Comercial de Remediación" : "Remediation Business Proposal",
        _ => es ? "Informe de Seguridad" : "Security Report"
    };

    private string GetEyebrow(bool es) => _reportTypeKey switch
    {
        "c-level" => es ? "BRIEFING EJECUTIVO" : "EXECUTIVE BRIEFING",
        "technical" => es ? "INFORME TÉCNICO" : "TECHNICAL REPORT",
        "preventa-opener" or "preventa-detailed" => es ? "EVALUACIÓN DE SEGURIDAD" : "SECURITY ASSESSMENT",
        "monthly" => es ? "PROGRESO MENSUAL" : "MONTHLY PROGRESS",
        "framework" => es ? "CUMPLIMIENTO" : "COMPLIANCE",
        "proposal" => es ? "PROPUESTA COMERCIAL" : "BUSINESS PROPOSAL",
        _ => es ? "EVALUACIÓN DE SEGURIDAD" : "SECURITY ASSESSMENT"
    };
}

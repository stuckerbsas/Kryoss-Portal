using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class NextStepBlock : IFlowBlock
{
    public string? SectionTitle(ReportOptions options) =>
        options.IsSpanish ? "Próximos Pasos" : "Next Steps";

    public int EstimateHeight(ReportData data) => 300;

    public string RenderContent(ReportData data, ReportOptions options)
    {
        var es = options.IsSpanish;
        var sb = new StringBuilder();
        var brand = data.Branding;

        var critCount = data.ControlResults.Count(r => r.Status == "fail" && r.Severity == "critical");
        var highCount = data.ControlResults.Count(r => r.Status == "fail" && r.Severity == "high");
        var totalFail = data.ControlResults.Count(r => r.Status == "fail");

        sb.AppendLine($"<h3 style='font-size:12pt;margin-bottom:10px;color:#1e293b'>{(es ? "Lo que encontramos" : "What we found")}</h3>");
        sb.AppendLine("<ul style='font-size:10pt;color:#334155;line-height:1.8;margin:0 0 16px 20px'>");
        sb.AppendLine($"<li>{(es ? $"{data.TotalMachines} equipos auditados sin interrupciones" : $"{data.TotalMachines} machines audited with zero disruption")}</li>");
        if (critCount > 0)
            sb.AppendLine($"<li style='color:#991b1b;font-weight:600'>{(es ? $"{critCount} hallazgos críticos requieren acción inmediata" : $"{critCount} critical findings require immediate action")}</li>");
        if (highCount > 0)
            sb.AppendLine($"<li>{(es ? $"{highCount} hallazgos de severidad alta" : $"{highCount} high-severity findings")}</li>");
        sb.AppendLine($"<li>{(es ? $"{totalFail} controles fallidos en total" : $"{totalFail} total failing controls")}</li>");
        if (data.HasCloudData)
            sb.AppendLine($"<li>{(es ? "Evaluación cloud incluida (M365/Azure)" : "Cloud assessment included (M365/Azure)")}</li>");
        sb.AppendLine("</ul>");

        sb.AppendLine($"<h3 style='font-size:12pt;margin-bottom:10px;color:#1e293b'>{(es ? "Cómo te ayudamos" : "How we help")}</h3>");
        sb.AppendLine("<ul style='font-size:10pt;color:#334155;line-height:1.8;margin:0 0 16px 20px'>");
        sb.AppendLine($"<li>{(es ? "Plan de remediación priorizado por severidad e impacto" : "Remediation plan prioritized by severity and impact")}</li>");
        sb.AppendLine($"<li>{(es ? "Ejecución sin interrupciones operacionales" : "Execution with zero operational disruption")}</li>");
        sb.AppendLine($"<li>{(es ? "Monitoreo continuo post-remediación" : "Continuous monitoring post-remediation")}</li>");
        sb.AppendLine("</ul>");

        sb.AppendLine("<div class='cta-box' style='margin-top:16px'>");
        if (es)
            sb.AppendLine($"<p style='font-size:13px;margin:0'><strong>Próximo paso:</strong> agendar una reunión de 30 minutos con {ReportHelpers.HtmlEncode(brand.CompanyName)} para revisar estos hallazgos y definir un plan de acción con cronograma.</p>");
        else
            sb.AppendLine($"<p style='font-size:13px;margin:0'><strong>Next step:</strong> schedule a 30-minute meeting with {ReportHelpers.HtmlEncode(brand.CompanyName)} to review these findings and define an action plan with timeline.</p>");
        sb.AppendLine("</div>");

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
}

using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class FrameworkCoverBlock : IReportBlock
{
    public string Render(ReportData data, ReportOptions options)
    {
        var es = options.IsSpanish;
        var sb = new StringBuilder();
        var brand = data.Branding;

        var fwName = options.FrameworkName ?? options.FrameworkCode ?? "Compliance";
        var fwScore = data.FrameworkScores?.FirstOrDefault(f => f.Code == options.FrameworkCode);
        var score = fwScore?.Score ?? (double)data.AvgScore;
        var grade = ReportHelpers.GetGrade((decimal)score);

        sb.AppendLine("<div class='cover'>");
        sb.AppendLine($"<img class='cover-ribbon' src='{RibbonData.DataUri}' alt=''/>");
        sb.AppendLine("<div class='cover-content'>");
        if (brand.LogoUrl is not null)
            sb.AppendLine($"<img class='logo' src='{ReportHelpers.HtmlEncode(brand.LogoUrl)}' alt='{ReportHelpers.HtmlEncode(brand.CompanyName)}'/>");
        sb.AppendLine($"<div class='eyebrow'>{ReportHelpers.HtmlEncode(es ? "INFORME DE EVIDENCIA DE CUMPLIMIENTO" : "COMPLIANCE EVIDENCE REPORT")}</div>");
        sb.AppendLine($"<h1>{ReportHelpers.HtmlEncode(fwName)}</h1>");
        sb.AppendLine($"<h2>{ReportHelpers.HtmlEncode(data.Org.Name)}</h2>");
        sb.AppendLine($"<div class='meta'>{data.ScanDate:yyyy-MM-dd}</div>");

        sb.AppendLine($"<div class='grade-badge grade-{grade}'>{grade}</div>");
        sb.AppendLine($"<div class='score'>{score:F0}%</div>");

        sb.AppendLine("<dl class='cover-audit-meta'>");
        sb.AppendLine($"<dt>{(es ? "Informe preparado por" : "Report prepared by")}</dt>");
        sb.AppendLine($"<dd>{ReportHelpers.HtmlEncode(brand.CompanyName)}</dd>");
        sb.AppendLine($"<dt>{(es ? "Fecha del informe" : "Report date")}</dt>");
        sb.AppendLine($"<dd>{data.ScanDate:yyyy-MM-dd}</dd>");
        sb.AppendLine($"<dt>{(es ? "Dispositivos en alcance" : "Devices in scope")}</dt>");
        sb.AppendLine($"<dd>{data.TotalMachines}</dd>");
        sb.AppendLine("</dl>");

        sb.AppendLine("</div></div>");
        return sb.ToString();
    }
}

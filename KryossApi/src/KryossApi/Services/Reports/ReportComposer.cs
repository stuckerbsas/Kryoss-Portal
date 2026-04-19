using System.Text;

namespace KryossApi.Services.Reports;

public interface IReportComposer
{
    Task<string> GenerateAsync(Guid orgId, string reportType, ReportOptions options);
}

public class ReportComposer : IReportComposer
{
    private readonly IReportDataLoader _loader;

    public ReportComposer(IReportDataLoader loader) => _loader = loader;

    public async Task<string> GenerateAsync(Guid orgId, string reportType, ReportOptions options)
    {
        var data = await _loader.LoadAsync(orgId, options);
        var recipe = ResolveRecipe(reportType, options);
        var blocks = recipe.GetBlocks(data).ToList();

        var sb = new StringBuilder();
        var reportTitle = recipe.ReportTitle(options);
        var detail = $"{data.TotalMachines} {(options.IsSpanish ? "dispositivos" : "devices")} · {data.Org.Name}";

        ReportHelpers.AppendHtmlHead(sb, $"{reportTitle} - {data.Org.Name}", data.Branding,
            isOrgReport: true, htmlLang: options.Lang, user: data.UserInfo, detail: detail);

        int pageNum = 0;
        foreach (var block in blocks)
        {
            var html = block.Render(data, options);
            if (string.IsNullOrEmpty(html)) continue;

            bool isCover = html.Contains("<div class='cover'>");
            if (!isCover)
            {
                pageNum++;
                var footer = RenderPageFooter(data, options, pageNum);
                var lastClose = html.LastIndexOf("</div>");
                if (lastClose >= 0)
                {
                    // Insert between </div> (pb close) and </div> (page close)
                    var insertAt = lastClose;
                    html = html.Insert(insertAt, "\n" + footer);
                }
            }
            sb.Append(html);
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static string RenderPageFooter(ReportData data, ReportOptions options, int pageNum)
    {
        var es = options.IsSpanish;
        var user = data.UserInfo;
        var date = data.ScanDate.ToString(es ? "dd/MM/yyyy" : "yyyy-MM-dd");

        var sb = new StringBuilder();
        sb.AppendLine("<div class='page-footer'>");
        sb.AppendLine("<div class='pf-left'>");
        if (!string.IsNullOrEmpty(user.FullName))
            sb.AppendLine($"<div>{ReportHelpers.HtmlEncode(es ? "Preparado por" : "Prepared by")}: {ReportHelpers.HtmlEncode(user.FullName)}</div>");
        var contactParts = new List<string>();
        if (!string.IsNullOrEmpty(user.Phone))
            contactParts.Add($"Tel: {ReportHelpers.HtmlEncode(user.Phone)}");
        if (!string.IsNullOrEmpty(user.Email))
            contactParts.Add($"Mail: {ReportHelpers.HtmlEncode(user.Email)}");
        if (contactParts.Count > 0)
            sb.AppendLine($"<div>{string.Join(" &middot; ", contactParts)}</div>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class='pf-right'>");
        sb.AppendLine($"<div>{date}</div>");
        sb.AppendLine($"<div>{(es ? "Página" : "Page")} {pageNum}</div>");
        sb.AppendLine("</div>");
        sb.AppendLine("</div>");
        return sb.ToString();
    }

    private static IReportRecipe ResolveRecipe(string reportType, ReportOptions options) => reportType switch
    {
        "c-level" => new Recipes.CLevelRecipe(),
        "technical" => new Recipes.TechnicalRecipe(),
        "preventa" or "preventas" => options.Tone == "detailed"
            ? new Recipes.PreventaDetailedRecipe()
            : new Recipes.PreventaOpenerRecipe(),
        "presales" => new Recipes.PreventaDetailedRecipe(),
        "presales-opener" => new Recipes.PreventaOpenerRecipe(),
        "monthly" or "monthly-briefing" => new Recipes.MonthlyRecipe(),
        "framework" => new Recipes.FrameworkRecipe(),
        "proposal" => new Recipes.ProposalRecipe(),
        _ => throw new ArgumentException($"Unknown report type: {reportType}")
    };
}

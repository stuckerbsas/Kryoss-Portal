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

        foreach (var block in blocks)
            sb.Append(block.Render(data, options));

        sb.AppendLine("</body></html>");
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

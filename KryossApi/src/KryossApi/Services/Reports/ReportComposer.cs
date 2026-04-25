using System.Diagnostics;
using System.Text;

namespace KryossApi.Services.Reports;

public interface IReportComposer
{
    Task<string> GenerateAsync(Guid orgId, string reportType, ReportOptions options);
    Task<DiagnosticResult> DiagnoseAsync(Guid orgId, string? singleType, ReportOptions options);
}

public class DiagnosticResult
{
    public long DataLoadMs { get; set; }
    public Dictionary<string, long>? QueryTimings { get; set; }
    public int TotalMachines { get; set; }
    public int TotalControlResults { get; set; }
    public string? DataNeedsFlags { get; set; }
    public int QueriesSkipped { get; set; }
    public List<RecipeDiag> Recipes { get; set; } = new();
}

public class RecipeDiag
{
    public string Type { get; set; } = "";
    public string RecipeName { get; set; } = "";
    public string? DataNeeds { get; set; }
    public string? Error { get; set; }
    public List<BlockDiag> Blocks { get; set; } = new();
}

public class BlockDiag
{
    public string Name { get; set; } = "";
    public long Ms { get; set; }
    public int HtmlLength { get; set; }
    public string? Error { get; set; }
}

public class ReportComposer : IReportComposer
{
    private readonly IReportDataLoader _loader;

    public ReportComposer(IReportDataLoader loader) => _loader = loader;

    public async Task<string> GenerateAsync(Guid orgId, string reportType, ReportOptions options)
    {
        var recipe = ResolveRecipe(reportType, options);

        ReportData data;
        if (recipe is ISelfContainedRecipe selfContained)
            data = selfContained.BuildSyntheticData();
        else
            data = await _loader.LoadAsync(orgId, options, recipe.DataNeeds);
        var blocks = recipe.GetBlocks(data).ToList();

        var sb = new StringBuilder();
        var reportTitle = recipe.ReportTitle(options);
        var detail = $"{data.TotalMachines} {(options.IsSpanish ? "dispositivos" : "devices")} · {data.Org.Name}";

        ReportHelpers.AppendHtmlHead(sb, $"{reportTitle} - {data.Org.Name}", data.Branding,
            isOrgReport: true, htmlLang: options.Lang, user: null, detail: detail);

        int pageNum = 0;
        int blockIdx = 0;
        var sharedBudget = new PageBudget();
        bool flowPageOpen = false;
        const int SectionSeparator = 40;

        foreach (var block in blocks)
        {
            var blockName = block.GetType().Name;
            var tag = $"<!-- block:{blockName} #{blockIdx} -->";
            blockIdx++;

            try
            {
                if (block is IFlowBlock flow)
                {
                    var content = flow.RenderContent(data, options);
                    if (string.IsNullOrEmpty(content)) continue;

                    var height = flow.EstimateHeight(data);
                    var title = flow.SectionTitle(options);
                    var needed = height + (flowPageOpen ? SectionSeparator : 0);

                    if (!flowPageOpen || sharedBudget.WouldOverflow(needed))
                    {
                        if (flowPageOpen)
                        {
                            pageNum++;
                            sb.Append(RenderPageFooter(data, options, pageNum));
                            sb.AppendLine("</div></div>");
                        }
                        sb.AppendLine(tag);
                        sb.AppendLine("<div class='page'>");
                        ReportHelpers.AppendPageHeader(sb, title ?? reportTitle, data.Branding);
                        sb.AppendLine("<div class='pb'>");
                        sharedBudget = new PageBudget();
                        flowPageOpen = true;
                    }
                    else
                    {
                        sb.AppendLine(tag);
                        if (title != null)
                        {
                            sb.AppendLine($"<div style='border-top:1px solid #e2e8f0;margin:14px 0 10px;padding-top:10px'>");
                            sb.AppendLine($"<div style='font-size:10pt;font-weight:700;color:#1e293b;margin-bottom:6px'>{ReportHelpers.HtmlEncode(title)}</div>");
                            sb.AppendLine("</div>");
                            sharedBudget.Spend(SectionSeparator);
                        }
                    }

                    sb.Append(content);
                    sharedBudget.Spend(height);
                    continue;
                }
            }
            catch { continue; }

            // Legacy block: close any open flow page first
            if (flowPageOpen)
            {
                pageNum++;
                sb.Append(RenderPageFooter(data, options, pageNum));
                sb.AppendLine("</div></div>");
                flowPageOpen = false;
            }

            string html;
            try
            {
                html = block.Render(data, options);
            }
            catch { continue; }
            if (string.IsNullOrEmpty(html)) continue;

            sb.AppendLine(tag);

            if (html.Contains("<div class='cover'>"))
            {
                sb.Append(html);
                continue;
            }

            if (!html.Contains(PageBudget.FooterMarker))
            {
                const string pageOpen = "<div class='page'>";
                var pages = html.Split(pageOpen);
                var normalized = new StringBuilder();
                for (int p = 0; p < pages.Length; p++)
                {
                    if (p > 0) normalized.Append(pageOpen);
                    var segment = pages[p];
                    if (p > 0)
                    {
                        var lastClose = segment.LastIndexOf("</div></div>");
                        if (lastClose >= 0)
                            segment = segment.Insert(lastClose, "\n" + PageBudget.FooterMarker + "\n");
                    }
                    normalized.Append(segment);
                }
                html = normalized.ToString();
            }

            var parts = html.Split(PageBudget.FooterMarker);
            for (int i = 0; i < parts.Length; i++)
            {
                sb.Append(parts[i]);
                if (i < parts.Length - 1)
                {
                    pageNum++;
                    sb.Append(RenderPageFooter(data, options, pageNum));
                }
            }
        }

        if (flowPageOpen)
        {
            pageNum++;
            sb.Append(RenderPageFooter(data, options, pageNum));
            sb.AppendLine("</div></div>");
        }

        sb.AppendLine($"<!-- kryoss-report recipe={recipe.GetType().Name} blocks=[{string.Join(",", blocks.Select(b => b.GetType().Name))}] pages={pageNum} generated={DateTime.UtcNow:o} -->");
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

    public async Task<DiagnosticResult> DiagnoseAsync(Guid orgId, string? singleType, ReportOptions options)
    {
        var types = singleType != null
            ? new[] { singleType }
            : AllRecipeTypes;

        // Self-contained recipes bypass the loader entirely
        if (types.Length == 1)
        {
            try
            {
                var r = ResolveRecipe(types[0], options);
                if (r is ISelfContainedRecipe sc)
                    return DiagnoseSelfContained(sc, options);
            }
            catch { /* fall through to normal path */ }
        }

        // Determine combined needs for all recipes being diagnosed
        ReportDataNeeds combinedNeeds = ReportDataNeeds.None;
        foreach (var rt in types)
        {
            try
            {
                var r = ResolveRecipe(rt, options);
                combinedNeeds |= r.DataNeeds;
            }
            catch { combinedNeeds = ReportDataNeeds.All; break; }
        }

        var sw = Stopwatch.StartNew();
        var (data, queryTimings) = await _loader.LoadWithTimingsAsync(orgId, options, combinedNeeds);
        var dataMs = sw.ElapsedMilliseconds;

        var allFlags = Enum.GetValues<ReportDataNeeds>()
            .Where(f => f != ReportDataNeeds.None && f != ReportDataNeeds.EndpointCore && f != ReportDataNeeds.All)
            .ToList();
        var skipped = allFlags.Count(f => (combinedNeeds & f) == 0);

        var result = new DiagnosticResult
        {
            DataLoadMs = dataMs,
            QueryTimings = queryTimings,
            TotalMachines = data.TotalMachines,
            TotalControlResults = data.ControlResults.Count,
            DataNeedsFlags = combinedNeeds.ToString(),
            QueriesSkipped = skipped,
        };

        foreach (var rt in types)
        {
            var rd = new RecipeDiag { Type = rt };
            try
            {
                var recipe = ResolveRecipe(rt, options);
                rd.RecipeName = recipe.GetType().Name;
                rd.DataNeeds = recipe.DataNeeds.ToString();
                var blocks = recipe.GetBlocks(data).ToList();

                foreach (var block in blocks)
                {
                    var bd = new BlockDiag { Name = block.GetType().Name };
                    sw.Restart();
                    try
                    {
                        var html = block.Render(data, options);
                        bd.Ms = sw.ElapsedMilliseconds;
                        bd.HtmlLength = html?.Length ?? 0;
                    }
                    catch (Exception ex)
                    {
                        bd.Ms = sw.ElapsedMilliseconds;
                        bd.Error = $"{ex.GetType().Name}: {ex.Message}";
                    }
                    rd.Blocks.Add(bd);
                }
            }
            catch (Exception ex)
            {
                rd.Error = $"{ex.GetType().Name}: {ex.Message}";
            }
            result.Recipes.Add(rd);
        }

        return result;
    }

    private static DiagnosticResult DiagnoseSelfContained(ISelfContainedRecipe recipe, ReportOptions options)
    {
        var sw = Stopwatch.StartNew();
        var data = recipe.BuildSyntheticData();
        var dataMs = sw.ElapsedMilliseconds;

        var result = new DiagnosticResult
        {
            DataLoadMs = dataMs,
            QueryTimings = new() { ["synthetic"] = dataMs },
            TotalMachines = data.TotalMachines,
            TotalControlResults = data.ControlResults.Count,
            DataNeedsFlags = "None (synthetic)",
            QueriesSkipped = 12,
        };

        var rd = new RecipeDiag { Type = "test-fixture", RecipeName = recipe.GetType().Name, DataNeeds = "None (synthetic)" };
        var blocks = recipe.GetBlocks(data).ToList();
        foreach (var block in blocks)
        {
            var bd = new BlockDiag { Name = block.GetType().Name };
            sw.Restart();
            try
            {
                var html = block.Render(data, options);
                bd.Ms = sw.ElapsedMilliseconds;
                bd.HtmlLength = html?.Length ?? 0;
            }
            catch (Exception ex)
            {
                bd.Ms = sw.ElapsedMilliseconds;
                bd.Error = $"{ex.GetType().Name}: {ex.Message}";
            }
            rd.Blocks.Add(bd);
        }
        result.Recipes.Add(rd);
        return result;
    }

    private static readonly string[] AllRecipeTypes = new[]
    {
        "c-level", "technical", "preventa-opener", "preventa-detailed",
        "preventa", "presales", "monthly", "framework", "proposal",
        "network", "cloud-executive", "exec-onepager", "m365",
        "hygiene", "risk-assessment", "inventory"
    };

    private static IReportRecipe ResolveRecipe(string reportType, ReportOptions options) => reportType switch
    {
        "c-level" or "executive" => new Recipes.CLevelRecipe(),
        "technical" => new Recipes.TechnicalRecipe(),
        "preventa-opener" => new Recipes.PreventaOpenerRecipe(),
        "preventa-detailed" => new Recipes.PreventaDetailedRecipe(),
        "preventa" or "preventas" => options.Tone == "detailed"
            ? new Recipes.PreventaDetailedRecipe()
            : new Recipes.PreventaOpenerRecipe(),
        "presales" => new Recipes.PreventaDetailedRecipe(),
        "presales-opener" => new Recipes.PreventaOpenerRecipe(),
        "monthly" or "monthly-briefing" => new Recipes.MonthlyRecipe(),
        "framework" or "compliance" => new Recipes.FrameworkRecipe(),
        "proposal" => new Recipes.ProposalRecipe(),
        "network" => new Recipes.NetworkRecipe(),
        "cloud-executive" => new Recipes.CloudExecutiveRecipe(),
        "exec-onepager" => new Recipes.ExecOnePagerRecipe(),
        "m365" => new Recipes.M365Recipe(),
        "hygiene" => new Recipes.HygieneRecipe(),
        "risk-assessment" => new Recipes.RiskAssessmentRecipe(),
        "inventory" => new Recipes.InventoryRecipe(),
        "test-fixture" => new Recipes.TestFixtureRecipe(),
        _ => throw new ArgumentException($"Unknown report type: {reportType}")
    };
}

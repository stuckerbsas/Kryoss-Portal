namespace KryossApi.Services.Reports.Recipes;

public class CLevelRecipe : IReportRecipe
{
    public string ReportTitle(ReportOptions options) => options.IsSpanish ? "Informe C-Level" : "C-Level Briefing";
    public IEnumerable<IReportBlock> GetBlocks(ReportData data) { yield break; }
}

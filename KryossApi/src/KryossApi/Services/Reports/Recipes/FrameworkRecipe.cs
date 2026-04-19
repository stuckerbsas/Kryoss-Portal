namespace KryossApi.Services.Reports.Recipes;

public class FrameworkRecipe : IReportRecipe
{
    public string ReportTitle(ReportOptions options) => options.IsSpanish ? "Informe de Framework" : "Framework Report";
    public IEnumerable<IReportBlock> GetBlocks(ReportData data) { yield break; }
}

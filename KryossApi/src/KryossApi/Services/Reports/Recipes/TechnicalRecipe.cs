namespace KryossApi.Services.Reports.Recipes;

public class TechnicalRecipe : IReportRecipe
{
    public string ReportTitle(ReportOptions options) => options.IsSpanish ? "Informe Técnico" : "Technical Report";
    public IEnumerable<IReportBlock> GetBlocks(ReportData data) { yield break; }
}

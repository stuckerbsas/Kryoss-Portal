namespace KryossApi.Services.Reports.Recipes;

public class PreventaDetailedRecipe : IReportRecipe
{
    public string ReportTitle(ReportOptions options) => options.IsSpanish ? "Preventa — Detallado" : "Presales — Detailed";
    public IEnumerable<IReportBlock> GetBlocks(ReportData data) { yield break; }
}

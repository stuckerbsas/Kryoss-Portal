namespace KryossApi.Services.Reports.Recipes;

public class PreventaOpenerRecipe : IReportRecipe
{
    public string ReportTitle(ReportOptions options) => options.IsSpanish ? "Preventa — Apertura" : "Presales — Opener";
    public IEnumerable<IReportBlock> GetBlocks(ReportData data) { yield break; }
}

namespace KryossApi.Services.Reports.Recipes;

public class MonthlyRecipe : IReportRecipe
{
    public string ReportTitle(ReportOptions options) => options.IsSpanish ? "Informe Mensual" : "Monthly Report";
    public IEnumerable<IReportBlock> GetBlocks(ReportData data) { yield break; }
}

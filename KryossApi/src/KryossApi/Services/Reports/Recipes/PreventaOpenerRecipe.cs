using KryossApi.Services.Reports.Blocks;

namespace KryossApi.Services.Reports.Recipes;

public class PreventaOpenerRecipe : IReportRecipe
{
    public string ReportTitle(ReportOptions options) =>
        options.IsSpanish ? "Evaluación de Riesgo" : "Risk Assessment";

    public IEnumerable<IReportBlock> GetBlocks(ReportData data)
    {
        yield return new CoverBlock("preventa-opener");
        yield return new RiskScoreBlock();
        yield return new ThreatVectorsBlock();
        yield return new MethodologyBlock();
    }
}

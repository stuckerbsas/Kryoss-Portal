namespace KryossApi.Services.Reports.Recipes;

public class ProposalRecipe : IReportRecipe
{
    public string ReportTitle(ReportOptions options) => options.IsSpanish ? "Propuesta" : "Proposal";
    public IEnumerable<IReportBlock> GetBlocks(ReportData data) { yield break; }
}

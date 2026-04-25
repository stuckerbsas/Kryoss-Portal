using KryossApi.Services.Reports.Blocks;

namespace KryossApi.Services.Reports.Recipes;

public class NetworkRecipe : IReportRecipe
{
    public ReportDataNeeds DataNeeds =>
        ReportDataNeeds.EndpointCore | ReportDataNeeds.Cloud | ReportDataNeeds.Network;

    public string ReportTitle(ReportOptions options) =>
        options.IsSpanish ? "Evaluación de Red" : "Network Assessment";

    public IEnumerable<IReportBlock> GetBlocks(ReportData data)
    {
        yield return new CoverBlock("network");
        yield return new KpiBlock();
        yield return new NetworkBlock();
        if (data.HasCloudData)
            yield return new CloudPostureBlock(compact: true);
        yield return new MethodologyBlock();
    }
}

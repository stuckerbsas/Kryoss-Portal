using KryossApi.Services.Reports.Blocks;

namespace KryossApi.Services.Reports.Recipes;

public class InventoryRecipe : IReportRecipe
{
    public ReportDataNeeds DataNeeds =>
        ReportDataNeeds.Runs | ReportDataNeeds.Enrichment | ReportDataNeeds.Network;

    public string ReportTitle(ReportOptions options) =>
        options.IsSpanish ? "Inventario de Activos" : "Asset Inventory Report";

    public IEnumerable<IReportBlock> GetBlocks(ReportData data)
    {
        yield return new CoverBlock("inventory");
        yield return new KpiBlock(KpiVariant.Compact);
        yield return new InventoryBlock();
        yield return new AssetMatrixBlock();
        yield return new NetworkBlock();
    }
}

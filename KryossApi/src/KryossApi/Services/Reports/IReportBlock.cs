namespace KryossApi.Services.Reports;

public interface IReportBlock
{
    string Render(ReportData data, ReportOptions options);
}

public interface IReportRecipe
{
    string ReportTitle(ReportOptions options);
    IEnumerable<IReportBlock> GetBlocks(ReportData data);
}

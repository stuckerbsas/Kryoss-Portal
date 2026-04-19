namespace KryossApi.Services.Reports;

public interface IReportDataLoader
{
    Task<ReportData> LoadAsync(Guid orgId, ReportOptions options);
}

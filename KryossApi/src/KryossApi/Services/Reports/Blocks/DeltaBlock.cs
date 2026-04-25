using System.Text;
namespace KryossApi.Services.Reports.Blocks;

[Obsolete("Merged into ScoreTrendBlock(showDelta: true). Remove after Phase 5 verification.")]
public class DeltaBlock : IReportBlock
{
    public string Render(ReportData data, ReportOptions options) => "";
}

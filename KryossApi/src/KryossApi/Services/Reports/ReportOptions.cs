namespace KryossApi.Services.Reports;

public enum KpiVariant { Exec, Business, Compact }
public enum CtaMode { Simple, Stepped }
public enum AudiencePerspective { Technical, Audit }
public enum NetworkVariant { Full, Summary, SitesTable }
public enum CloudVariant { Posture, Executive }

public record ReportOptions(
    string Lang = "en",
    string? FrameworkCode = null,
    string? FrameworkName = null,
    string? Tone = null
)
{
    public bool IsSpanish => Lang == "es";
    public KpiVariant KpiVariant { get; init; } = KpiVariant.Exec;
    public CtaMode CtaMode { get; init; } = CtaMode.Simple;
    public AudiencePerspective Audience { get; init; } = AudiencePerspective.Technical;
    public NetworkVariant NetworkVariant { get; init; } = NetworkVariant.Full;
    public CloudVariant CloudVariant { get; init; } = CloudVariant.Posture;
}

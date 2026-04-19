namespace KryossApi.Services.Reports;

public record ReportOptions(
    string Lang = "en",
    string? FrameworkCode = null,
    string? FrameworkName = null,
    string? Tone = null
)
{
    public bool IsSpanish => Lang == "es";
}

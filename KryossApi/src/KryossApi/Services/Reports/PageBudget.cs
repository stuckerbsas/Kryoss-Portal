using System.Text;

namespace KryossApi.Services.Reports;

/// <summary>
/// Tracks vertical px usage on A4 pages. All costs in CSS px.
/// Page = 296mm = 1119px. Header = ~90px. pb padding = 46px. Footer = ~38px.
/// Usable content area = 1119 - 90 - 46 - 38 = 945px.
/// </summary>
public class PageBudget
{
    public const int UsablePx = 945;
    public const string FooterMarker = "<!-- PAGE_FOOTER -->";

    private int _used;

    public int Remaining => UsablePx - _used;

    public void Spend(int px) => _used += px;

    public bool WouldOverflow(int px) => _used + px > UsablePx;

    public void NewPage(StringBuilder sb, string title, ReportBranding branding)
    {
        sb.AppendLine(FooterMarker);
        sb.AppendLine("</div></div>");
        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb, title, branding);
        sb.AppendLine("<div class='pb'>");
        _used = 0;
    }

    public void StartPage(StringBuilder sb, string title, ReportBranding branding)
    {
        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb, title, branding);
        sb.AppendLine("<div class='pb'>");
        _used = 0;
    }

    public void EndPage(StringBuilder sb)
    {
        sb.AppendLine(FooterMarker);
        sb.AppendLine("</div></div>");
        _used = 0;
    }

    // ── Element sizes in px (measured from browser) ──

    public const int H3 = 38;              // h3 + border-bottom + margins
    public const int TableHeader = 32;     // thead row with padding
    public const int TableRow = 24;        // tbody row at 9-10pt
    public const int FindingCard = 45;     // standard card (measured: 44.175px)
    public const int FindingCardShort = 30; // card without recommendation
    public const int Paragraph = 22;       // single <p> at 10pt
    public const int SummaryLine = 16;     // italic small text
    public const int RadarChart = 300;     // SVG 280px max-width + margins
    public const int ScoreCards = 70;      // 5-column grid row with padding
    public const int CategoryBar = 45;     // gap analysis bar (measured: 44.175px)

    // ── Text wrapping (105 chars/line, word boundary) ──

    public const int MaxChars = 105;
    public const int LinePx8pt = 15;   // 8pt (10.67px) × 1.4 line-height ≈ 15px
    public const int LinePx9pt = 17;   // 9pt (12px) × 1.4 line-height ≈ 17px

    public static int LineCount(string? text, int maxChars = MaxChars)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        int lines = 0, pos = 0;
        while (pos < text.Length)
        {
            lines++;
            var remaining = text.Length - pos;
            if (remaining <= maxChars) break;
            var searchEnd = pos + maxChars;
            var lastSpace = text.LastIndexOf(' ', searchEnd, maxChars);
            pos = lastSpace > pos ? lastSpace + 1 : searchEnd;
        }
        return lines;
    }

    public static string WordWrap(string? text, int maxChars = MaxChars)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (text.Length <= maxChars) return text;
        var sb = new StringBuilder(text.Length + 20);
        int pos = 0;
        while (pos < text.Length)
        {
            var remaining = text.Length - pos;
            if (remaining <= maxChars)
            {
                sb.Append(text, pos, remaining);
                break;
            }
            var searchEnd = pos + maxChars;
            var lastSpace = text.LastIndexOf(' ', searchEnd, maxChars);
            int cut = lastSpace > pos ? lastSpace : searchEnd;
            sb.Append(text, pos, cut - pos);
            sb.Append("<br>");
            pos = lastSpace > pos ? lastSpace + 1 : searchEnd;
        }
        return sb.ToString();
    }

    // ── Dynamic card heights ──

    public const int CardChrome = 58;       // margin(10) + padding(16) + title(17) + meta(15)
    public const int CloudCardChrome = 48;  // margin(8) + padding(16) + title(17) + badge + spacing
    public const int ObsMargin = 3;
    public const int RecMargin = 4;

    public static int RemediationCardHeight(string? remediation)
    {
        var lines = LineCount(remediation);
        return lines > 0 ? CardChrome + 6 + lines * LinePx8pt : CardChrome;
    }

    public static int CloudFindingHeight(string? observation, string? recommendation)
    {
        int h = CloudCardChrome;
        var obsLines = LineCount(observation);
        if (obsLines > 0) h += ObsMargin + obsLines * LinePx8pt;
        var recLines = LineCount(recommendation);
        if (recLines > 0) h += RecMargin + recLines * LinePx8pt;
        return h;
    }
}

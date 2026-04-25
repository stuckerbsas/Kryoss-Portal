using System.Text;

namespace KryossApi.Services.Reports.Charts;

public record DonutSegment(string Label, decimal Value, string Color);

public static class SvgDonut
{
    public static string Render(
        List<DonutSegment> segments,
        int size = 160,
        double innerRadius = 0.65,
        string? centerLabel = null,
        bool showLegend = true)
    {
        if (segments.Count == 0) return "";

        var total = segments.Sum(s => s.Value);
        if (total <= 0) return "";

        var sb = new StringBuilder();
        var half = size / 2;
        var outerR = half - 4;
        var innerR = outerR * innerRadius;

        sb.AppendLine("<div style='display:flex;align-items:center;gap:16px;margin:10px 0'>");
        sb.AppendLine($"<svg viewBox='0 0 {size} {size}' xmlns='http://www.w3.org/2000/svg' style='width:{size}px;height:{size}px;flex-shrink:0'>");

        double startAngle = -Math.PI / 2;
        foreach (var seg in segments)
        {
            var pct = (double)(seg.Value / total);
            var sweepAngle = pct * 2 * Math.PI;
            var endAngle = startAngle + sweepAngle;

            var x1o = half + outerR * Math.Cos(startAngle);
            var y1o = half + outerR * Math.Sin(startAngle);
            var x2o = half + outerR * Math.Cos(endAngle);
            var y2o = half + outerR * Math.Sin(endAngle);
            var x1i = half + innerR * Math.Cos(endAngle);
            var y1i = half + innerR * Math.Sin(endAngle);
            var x2i = half + innerR * Math.Cos(startAngle);
            var y2i = half + innerR * Math.Sin(startAngle);
            var largeArc = sweepAngle > Math.PI ? 1 : 0;

            sb.Append($"<path d='M {x1o:F2},{y1o:F2} ");
            sb.Append($"A {outerR:F2},{outerR:F2} 0 {largeArc} 1 {x2o:F2},{y2o:F2} ");
            sb.Append($"L {x1i:F2},{y1i:F2} ");
            sb.Append($"A {innerR:F2},{innerR:F2} 0 {largeArc} 0 {x2i:F2},{y2i:F2} Z' ");
            sb.AppendLine($"fill='{seg.Color}'/>");

            startAngle = endAngle;
        }

        if (centerLabel != null)
        {
            sb.AppendLine($"<text x='{half}' y='{half}' text-anchor='middle' dominant-baseline='central' font-size='18' font-weight='700' fill='#1e293b' font-family='Montserrat,sans-serif'>{ReportHelpers.HtmlEncode(centerLabel)}</text>");
        }

        sb.AppendLine("</svg>");

        if (showLegend)
        {
            sb.AppendLine("<div style='font-size:9pt;line-height:1.6'>");
            foreach (var seg in segments)
            {
                var pct = total > 0 ? (double)(seg.Value / total) * 100 : 0;
                sb.AppendLine($"<div><span style='display:inline-block;width:10px;height:10px;border-radius:2px;background:{seg.Color};margin-right:6px;vertical-align:middle'></span>{ReportHelpers.HtmlEncode(seg.Label)} <span style='color:#6b7280'>({pct:F0}%)</span></div>");
            }
            sb.AppendLine("</div>");
        }

        sb.AppendLine("</div>");
        return sb.ToString();
    }
}

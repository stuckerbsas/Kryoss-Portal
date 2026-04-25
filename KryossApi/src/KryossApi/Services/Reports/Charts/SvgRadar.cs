using System.Text;

namespace KryossApi.Services.Reports.Charts;

public record RadarSeries(string Name, List<decimal> Values, string Color, double FillOpacity = 0.2);

public static class SvgRadar
{
    public static string Render(
        List<string> axes,
        List<RadarSeries> series,
        decimal maxValue = 100m,
        int size = 300)
    {
        if (axes.Count < 3 || series.Count == 0) return "";

        var sb = new StringBuilder();
        var n = axes.Count;
        var cx = size / 2;
        var cy = size / 2;
        var r = size / 2 - 30;
        var step = 2 * Math.PI / n;
        var startAngle = -Math.PI / 2;

        (double x, double y) Point(double angle, double radius) =>
            (cx + radius * Math.Cos(angle), cy + radius * Math.Sin(angle));

        sb.AppendLine($"<svg viewBox='0 0 {size} {size}' xmlns='http://www.w3.org/2000/svg' style='max-width:{size}px;display:block;margin:0 auto'>");

        // Grid rings
        foreach (var pct in new[] { 0.25, 0.50, 0.75, 1.0 })
        {
            var pts = new List<string>();
            for (int i = 0; i < n; i++)
            {
                var (x, y) = Point(startAngle + i * step, r * pct);
                pts.Add($"{x:F1},{y:F1}");
            }
            sb.AppendLine($"<polygon points='{string.Join(" ", pts)}' fill='none' stroke='#e5e7eb' stroke-width='1'/>");
        }

        // Axis lines
        for (int i = 0; i < n; i++)
        {
            var (x, y) = Point(startAngle + i * step, r);
            sb.AppendLine($"<line x1='{cx}' y1='{cy}' x2='{x:F1}' y2='{y:F1}' stroke='#d1d5db' stroke-width='1'/>");
        }

        // Series polygons
        foreach (var s in series)
        {
            var pts = new List<string>();
            for (int i = 0; i < n; i++)
            {
                var val = i < s.Values.Count ? s.Values[i] : 0;
                var norm = maxValue > 0 ? (double)(val / maxValue) : 0;
                var (x, y) = Point(startAngle + i * step, r * Math.Min(norm, 1.0));
                pts.Add($"{x:F1},{y:F1}");
            }
            sb.AppendLine($"<polygon points='{string.Join(" ", pts)}' fill='{s.Color}' fill-opacity='{s.FillOpacity:F2}' stroke='{s.Color}' stroke-width='2'/>");

            for (int i = 0; i < n; i++)
            {
                var val = i < s.Values.Count ? s.Values[i] : 0;
                var norm = maxValue > 0 ? (double)(val / maxValue) : 0;
                var (dx, dy) = Point(startAngle + i * step, r * Math.Min(norm, 1.0));
                sb.AppendLine($"<circle cx='{dx:F1}' cy='{dy:F1}' r='3' fill='{s.Color}'/>");
            }
        }

        // Axis labels
        for (int i = 0; i < n; i++)
        {
            var (lx, ly) = Point(startAngle + i * step, r + 18);
            var anchor = lx < cx - 5 ? "end" : lx > cx + 5 ? "start" : "middle";
            sb.AppendLine($"<text x='{lx:F1}' y='{ly:F1}' text-anchor='{anchor}' dominant-baseline='middle' font-size='11' fill='#374151' font-family='Montserrat,sans-serif'>{ReportHelpers.HtmlEncode(axes[i])}</text>");
        }

        sb.AppendLine("</svg>");
        return sb.ToString();
    }
}

using System.Text;

namespace KryossApi.Services.Reports.Charts;

public static class SvgSparkline
{
    public static string Render(
        List<(DateTime Date, decimal Value)> points,
        int width = 120,
        int height = 30,
        string color = "#2BB673",
        bool showDots = false,
        bool showEndValue = false)
    {
        if (points.Count < 2) return "";

        var sb = new StringBuilder();
        var min = points.Min(p => p.Value);
        var max = points.Max(p => p.Value);
        var range = max - min;
        if (range == 0) range = 1;

        var stepX = (double)width / Math.Max(1, points.Count - 1);
        var coords = new List<string>();

        for (int i = 0; i < points.Count; i++)
        {
            var x = i * stepX;
            var y = height - (double)((points[i].Value - min) / range) * height;
            coords.Add($"{x:F1},{y:F1}");
        }

        sb.Append($"<svg viewBox='0 0 {width} {height + 4}' xmlns='http://www.w3.org/2000/svg' style='width:{width}px;height:{height + 4}px;display:inline-block;vertical-align:middle'>");
        sb.Append($"<polyline points='{string.Join(" ", coords)}' fill='none' stroke='{color}' stroke-width='1.5' stroke-linecap='round' stroke-linejoin='round'/>");

        if (showDots)
        {
            for (int i = 0; i < points.Count; i++)
            {
                var x = i * stepX;
                var y = height - (double)((points[i].Value - min) / range) * height;
                sb.Append($"<circle cx='{x:F1}' cy='{y:F1}' r='2' fill='{color}'/>");
            }
        }

        sb.Append("</svg>");

        if (showEndValue)
        {
            var last = points[^1].Value;
            sb.Append($"<span style='font-size:9pt;font-weight:600;color:{color};margin-left:4px'>{last:F1}</span>");
        }

        return sb.ToString();
    }
}

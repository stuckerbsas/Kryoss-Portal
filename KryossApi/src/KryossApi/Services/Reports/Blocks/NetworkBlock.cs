namespace KryossApi.Services.Reports.Blocks;

public class NetworkBlock : IReportBlock
{
    private readonly NetworkVariant _variant;
    public NetworkBlock(NetworkVariant variant = NetworkVariant.Full) => _variant = variant;

    public string Render(ReportData data, ReportOptions options)
    {
        if (!data.HasNetworkData) return "";

        return _variant switch
        {
            NetworkVariant.Summary => RenderSummary(data, options),
            NetworkVariant.SitesTable => RenderSitesTable(data, options),
            _ => RenderFull(data, options),
        };
    }

    private static string RenderSummary(ReportData data, ReportOptions options)
    {
        var es = options.IsSpanish;
        var sb = new System.Text.StringBuilder();
        var diags = data.NetworkDiags;

        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb, es ? "Red — Resumen" : "Network — Summary", data.Branding);
        sb.AppendLine("<div class='pb'>");

        var avgDown = diags.Average(d => d.DownloadMbps ?? 0);
        var avgUp = diags.Average(d => d.UploadMbps ?? 0);
        var avgLat = diags.Average(d => d.InternetLatencyMs ?? 0);
        var vpnCount = diags.Count(d => d.VpnDetected);

        sb.AppendLine("<div style='display:grid;grid-template-columns:repeat(4,1fr);gap:10px;margin:12px 0'>");
        RenderKpi(sb, $"{avgDown:F1}", "Avg Download (Mbps)", avgDown >= 50 ? "#16a34a" : avgDown >= 10 ? "#eab308" : "#ef4444");
        RenderKpi(sb, $"{avgUp:F1}", "Avg Upload (Mbps)", avgUp >= 20 ? "#16a34a" : avgUp >= 5 ? "#eab308" : "#ef4444");
        RenderKpi(sb, $"{avgLat:F0}ms", es ? "Latencia Prom." : "Avg Latency", avgLat <= 50 ? "#16a34a" : avgLat <= 100 ? "#eab308" : "#ef4444");
        RenderKpi(sb, $"{vpnCount}/{diags.Count}", "VPN", "#64748b");
        sb.AppendLine("</div>");

        var issues = diags.Count(d => (d.InternetLatencyMs ?? 0) > 100 || (d.DownloadMbps ?? 0) < 10);
        if (issues > 0)
        {
            sb.AppendLine($"<div style='margin-top:12px;padding:10px;background:#fef2f2;border-left:3px solid #ef4444;border-radius:4px;font-size:9pt;color:#7f1d1d'>");
            sb.AppendLine(es ? $"{issues} equipos con métricas de red por debajo del umbral." : $"{issues} machines with network metrics below threshold.");
            sb.AppendLine("</div>");
        }

        sb.AppendLine("</div></div>");
        return sb.ToString();
    }

    private static string RenderSitesTable(ReportData data, ReportOptions options)
    {
        var es = options.IsSpanish;
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb, es ? "Red — Detalle por Equipo" : "Network — Per-Machine Detail", data.Branding);
        sb.AppendLine("<div class='pb'>");

        sb.AppendLine("<table class='data-table'>");
        sb.AppendLine($"<thead><tr><th>{(es ? "Equipo" : "Machine")}</th><th>Download</th><th>Upload</th><th>{(es ? "Latencia" : "Latency")}</th><th>VPN</th><th>{(es ? "Adaptadores" : "Adapters")}</th></tr></thead><tbody>");

        foreach (var d in data.NetworkDiags)
        {
            var latencyClass = d.InternetLatencyMs switch { <= 50 => "pass", <= 100 => "warn", _ => "fail" };
            var vpnBadge = d.VpnDetected ? "<span class='badge badge-info'>VPN</span>" : "<span class='badge badge-ok'>Direct</span>";
            sb.AppendLine($"<tr>");
            sb.AppendLine($"<td>{ReportHelpers.HtmlEncode(d.Machine?.Hostname ?? d.MachineId.ToString())}</td>");
            sb.AppendLine($"<td>{d.DownloadMbps:F1} Mbps</td>");
            sb.AppendLine($"<td>{d.UploadMbps:F1} Mbps</td>");
            sb.AppendLine($"<td class='{latencyClass}'>{d.InternetLatencyMs:F0} ms</td>");
            sb.AppendLine($"<td>{vpnBadge}</td>");
            sb.AppendLine($"<td>{d.AdapterCount}</td>");
            sb.AppendLine($"</tr>");
        }
        sb.AppendLine("</tbody></table>");
        sb.AppendLine("</div></div>");
        return sb.ToString();
    }

    private const int RowsPerPage = 28;

    private static string RenderFull(ReportData data, ReportOptions options)
    {
        var es = options.IsSpanish;
        var sb = new System.Text.StringBuilder();
        var title = es ? "Diagnóstico de Red" : "Network Diagnostics";
        var diagHeader = $"<thead><tr><th>{(es ? "Equipo" : "Machine")}</th><th>Download</th><th>Upload</th><th>{(es ? "Latencia" : "Latency")}</th><th>VPN</th><th>{(es ? "Rutas" : "Routes")}</th><th>{(es ? "Adaptadores" : "Adapters")}</th></tr></thead>";

        // ── Page 1: KPIs + first chunk of diagnostics table ──
        var avgDown = data.NetworkDiags.Average(d => d.DownloadMbps ?? 0);
        var avgUp = data.NetworkDiags.Average(d => d.UploadMbps ?? 0);
        var avgLat = data.NetworkDiags.Average(d => d.InternetLatencyMs ?? 0);
        var vpnMachines = data.NetworkDiags.Where(d => d.VpnDetected).ToList();

        sb.AppendLine("<div class='page'>");
        ReportHelpers.AppendPageHeader(sb, title, data.Branding);
        sb.AppendLine("<div class='pb'>");

        sb.AppendLine("<div style='display:grid;grid-template-columns:repeat(4,1fr);gap:10px;margin:12px 0'>");
        RenderKpi(sb, $"{avgDown:F1}", "Avg Download (Mbps)", avgDown >= 50 ? "#16a34a" : avgDown >= 10 ? "#eab308" : "#ef4444");
        RenderKpi(sb, $"{avgUp:F1}", "Avg Upload (Mbps)", avgUp >= 20 ? "#16a34a" : avgUp >= 5 ? "#eab308" : "#ef4444");
        RenderKpi(sb, $"{avgLat:F0}ms", es ? "Latencia Prom." : "Avg Latency", avgLat <= 50 ? "#16a34a" : avgLat <= 100 ? "#eab308" : "#ef4444");
        RenderKpi(sb, $"{vpnMachines.Count}/{data.NetworkDiags.Count}", "VPN", "#64748b");
        sb.AppendLine("</div>");

        // First page has KPIs (~100px) so fewer rows fit
        var firstPageRows = RowsPerPage - 5;
        var chunks = data.NetworkDiags
            .Select((d, i) => (d, i))
            .GroupBy(x => x.i < firstPageRows ? 0 : 1 + (x.i - firstPageRows) / RowsPerPage)
            .ToList();

        if (chunks.Count > 0)
        {
            sb.AppendLine("<table class='data-table'>");
            sb.AppendLine(diagHeader);
            sb.AppendLine("<tbody>");
            foreach (var (d, _) in chunks[0])
                AppendDiagRow(sb, d);
            sb.AppendLine("</tbody></table>");
        }

        sb.AppendLine("</div></div>");

        // ── Additional pages for remaining diagnostics rows ──
        for (int c = 1; c < chunks.Count; c++)
        {
            sb.AppendLine("<div class='page'>");
            ReportHelpers.AppendPageHeader(sb, $"{title} ({c + 1})", data.Branding);
            sb.AppendLine("<div class='pb'>");
            sb.AppendLine("<table class='data-table'>");
            sb.AppendLine(diagHeader);
            sb.AppendLine("<tbody>");
            foreach (var (d, _) in chunks[c])
                AppendDiagRow(sb, d);
            sb.AppendLine("</tbody></table>");
            sb.AppendLine("</div></div>");
        }

        // ── Internal latency peers page ──
        var withPeers = data.NetworkDiags.Where(d => d.LatencyPeers.Count > 0).Take(5).ToList();
        if (withPeers.Count > 0)
        {
            var peerRows = withPeers.SelectMany(diag =>
                diag.LatencyPeers.OrderBy(p => p.AvgMs == null).ThenBy(p => p.AvgMs)
                    .Select(peer => (diag, peer))).ToList();

            var peerChunks = peerRows
                .Select((x, i) => (x, i))
                .GroupBy(x => x.i / RowsPerPage)
                .ToList();

            var peerHeader = $"<thead><tr><th>{(es ? "Equipo" : "Machine")}</th><th>Peer</th><th>{(es ? "Subred" : "Subnet")}</th><th>Avg (ms)</th><th>Jitter (ms)</th><th>{(es ? "Pérdida" : "Loss")} %</th><th>{(es ? "Alcanzable" : "Reachable")}</th></tr></thead>";

            foreach (var (chunk, ci) in peerChunks.Select((ch, ci) => (ch, ci)))
            {
                sb.AppendLine("<div class='page'>");
                ReportHelpers.AppendPageHeader(sb,
                    es ? "Latencia Interna" + (ci > 0 ? $" ({ci + 1})" : "") : "Internal Latency" + (ci > 0 ? $" ({ci + 1})" : ""),
                    data.Branding);
                sb.AppendLine("<div class='pb'>");
                sb.AppendLine("<table class='data-table'>");
                sb.AppendLine(peerHeader);
                sb.AppendLine("<tbody>");
                foreach (var ((diag, peer), _) in chunk)
                {
                    var hostname = diag.Machine?.Hostname ?? diag.MachineId.ToString();
                    var reachClass = peer.Reachable ? "pass" : "fail";
                    var lossClass = peer.PacketLoss switch { null or 0 => "pass", <= 5 => "warn", _ => "fail" };
                    sb.AppendLine($"<tr>");
                    sb.AppendLine($"<td>{ReportHelpers.HtmlEncode(hostname)}</td>");
                    sb.AppendLine($"<td>{peer.Host}</td>");
                    sb.AppendLine($"<td>{peer.Subnet ?? "-"}</td>");
                    sb.AppendLine($"<td>{(peer.AvgMs.HasValue ? $"{peer.AvgMs:F1}" : "-")}</td>");
                    sb.AppendLine($"<td>{(peer.JitterMs.HasValue ? $"{peer.JitterMs:F1}" : "-")}</td>");
                    sb.AppendLine($"<td class='{lossClass}'>{peer.PacketLoss ?? 0}%</td>");
                    sb.AppendLine($"<td class='{reachClass}'>{(peer.Reachable ? "✓" : "✗")}</td>");
                    sb.AppendLine($"</tr>");
                }
                sb.AppendLine("</tbody></table>");
                sb.AppendLine("</div></div>");
            }
        }

        // ── VPN + Subnets (combined into one page if small enough) ──
        if (vpnMachines.Count > 0 || data.NetworkDiags.SelectMany(d => d.Routes).Any())
        {
            sb.AppendLine("<div class='page'>");
            ReportHelpers.AppendPageHeader(sb, es ? "VPN y Subredes" : "VPN & Subnets", data.Branding);
            sb.AppendLine("<div class='pb'>");

            if (vpnMachines.Count > 0)
            {
                sb.AppendLine($"<h3>{(es ? "Topología VPN" : "VPN Topology")}</h3>");
                sb.AppendLine("<table class='data-table'>");
                sb.AppendLine($"<thead><tr><th>{(es ? "Equipo" : "Machine")}</th><th>{(es ? "Adaptadores VPN" : "VPN Adapters")}</th></tr></thead><tbody>");
                foreach (var d in vpnMachines)
                    sb.AppendLine($"<tr><td>{ReportHelpers.HtmlEncode(d.Machine?.Hostname ?? "")}</td><td>{ReportHelpers.HtmlEncode(d.VpnAdapters ?? "")}</td></tr>");
                sb.AppendLine("</tbody></table>");
            }

            var allRoutes = data.NetworkDiags.SelectMany(d => d.Routes).ToList();
            if (allRoutes.Count > 0)
            {
                var distinctSubnets = allRoutes
                    .Where(r => r.Destination != "0.0.0.0" && r.Destination != "127.0.0.0" && r.Destination != "255.255.255.255")
                    .Select(r => $"{r.Destination}/{r.Mask}")
                    .Distinct().OrderBy(s => s).Take(20).ToList();

                if (distinctSubnets.Count > 0)
                {
                    sb.AppendLine($"<h3>{(es ? "Subredes Detectadas" : "Detected Subnets")}</h3>");
                    sb.AppendLine("<div class='subnet-list'>");
                    foreach (var subnet in distinctSubnets)
                        sb.AppendLine($"<span class='badge badge-info'>{subnet}</span> ");
                    sb.AppendLine("</div>");
                }
            }

            sb.AppendLine("</div></div>");
        }

        return sb.ToString();
    }

    private static void AppendDiagRow(System.Text.StringBuilder sb, Data.Entities.MachineNetworkDiag d)
    {
        var vpnBadge = d.VpnDetected
            ? "<span class='badge badge-info'>VPN</span>"
            : "<span class='badge badge-ok'>Direct</span>";
        var latencyClass = d.InternetLatencyMs switch { <= 50 => "pass", <= 100 => "warn", _ => "fail" };
        sb.AppendLine($"<tr>");
        sb.AppendLine($"<td>{ReportHelpers.HtmlEncode(d.Machine?.Hostname ?? d.MachineId.ToString())}</td>");
        sb.AppendLine($"<td>{d.DownloadMbps:F1} Mbps</td>");
        sb.AppendLine($"<td>{d.UploadMbps:F1} Mbps</td>");
        sb.AppendLine($"<td class='{latencyClass}'>{d.InternetLatencyMs:F0} ms</td>");
        sb.AppendLine($"<td>{vpnBadge}</td>");
        sb.AppendLine($"<td>{d.RouteCount}</td>");
        sb.AppendLine($"<td>{d.AdapterCount}</td>");
        sb.AppendLine($"</tr>");
    }

    private static void RenderKpi(System.Text.StringBuilder sb, string value, string label, string color)
    {
        sb.AppendLine($"<div style='border:1px solid #e5e7eb;border-radius:8px;padding:12px;text-align:center'>");
        sb.AppendLine($"<div style='font-size:20pt;font-weight:700;color:{color}'>{value}</div>");
        sb.AppendLine($"<div style='font-size:7pt;color:#6b7280;margin-top:2px'>{ReportHelpers.HtmlEncode(label)}</div>");
        sb.AppendLine("</div>");
    }
}

using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class InventoryBlock : IFlowBlock
{
    public string? SectionTitle(ReportOptions options) =>
        options.IsSpanish ? "Inventario de Activos" : "Asset Inventory";

    public int EstimateHeight(ReportData data)
    {
        if (data.Runs.Count == 0) return 0;
        int h = 250;
        if (data.Enrichment.Disks.Count > 0) h += 120;
        h += Math.Min(20, data.Runs.Count) * 22;
        return h;
    }

    public string RenderContent(ReportData data, ReportOptions options)
    {
        if (data.Runs.Count == 0) return "";

        var sb = new StringBuilder();
        var es = options.IsSpanish;

        var machines = data.Runs.Select(r => r.Machine).Where(m => m != null).ToList();

        // OS distribution
        var osGroups = machines.GroupBy(m => m.OsName ?? "Unknown").OrderByDescending(g => g.Count()).ToList();
        sb.AppendLine($"<h3>{(es ? "Distribución de Sistemas Operativos" : "Operating System Distribution")}</h3>");
        sb.AppendLine("<div style='display:grid;grid-template-columns:repeat(auto-fill,minmax(160px,1fr));gap:8px;margin-bottom:14px'>");
        foreach (var g in osGroups)
        {
            var isLegacy = g.Key.Contains("2008") || g.Key.Contains("2003") || g.Key.Contains("Windows 7") || g.Key.Contains("Vista");
            var color = isLegacy ? "#991B1B" : "#0F172A";
            sb.AppendLine($"<div style='padding:8px 10px;border:1px solid #E2E8F0;border-radius:4px;display:flex;justify-content:space-between;align-items:center'>");
            sb.AppendLine($"<span style='font-size:8pt;color:{color}'>{ReportHelpers.HtmlEncode(TruncateOs(g.Key))}</span>");
            sb.AppendLine($"<span style='font-weight:700;color:{color}'>{g.Count()}</span>");
            sb.AppendLine("</div>");
        }
        sb.AppendLine("</div>");

        // Security features coverage
        var bitlocker = machines.Count(m => m.Bitlocker == true);
        var tpm = machines.Count(m => m.TpmPresent == true);
        var secureBoot = machines.Count(m => m.SecureBoot == true);
        var domainJoined = machines.Count(m => !string.IsNullOrEmpty(m.DomainName));
        var total = machines.Count;

        sb.AppendLine($"<h3>{(es ? "Cobertura de Seguridad" : "Security Coverage")}</h3>");
        sb.AppendLine("<table class='data-table'><thead><tr>");
        sb.AppendLine($"<th>{(es ? "Característica" : "Feature")}</th><th>{(es ? "Habilitado" : "Enabled")}</th><th>{(es ? "Faltante" : "Missing")}</th><th>%</th>");
        sb.AppendLine("</tr></thead><tbody>");
        RenderCoverageRow(sb, "BitLocker", bitlocker, total);
        RenderCoverageRow(sb, "TPM", tpm, total);
        RenderCoverageRow(sb, "Secure Boot", secureBoot, total);
        RenderCoverageRow(sb, es ? "Dominio" : "Domain Joined", domainJoined, total);
        sb.AppendLine("</tbody></table>");

        // Disk usage summary
        if (data.Enrichment.Disks.Count > 0)
        {
            sb.AppendLine($"<h3>{(es ? "Almacenamiento" : "Storage")}</h3>");
            var totalDiskGB = data.Enrichment.Disks.Sum(d => (decimal)(d.TotalGb ?? 0));
            var freeDiskGB = data.Enrichment.Disks.Sum(d => d.FreeGb ?? 0m);
            var usedDiskGB = totalDiskGB - freeDiskGB;
            var usedPct = totalDiskGB > 0 ? 100m * usedDiskGB / totalDiskGB : 0m;

            sb.AppendLine("<div style='display:grid;grid-template-columns:repeat(3,1fr);gap:10px;margin-bottom:10px'>");
            RenderKpi(sb, $"{totalDiskGB:F0} GB", es ? "Capacidad Total" : "Total Capacity", "#0F172A");
            RenderKpi(sb, $"{usedPct:F0}%", es ? "Utilizado" : "Used", usedPct > 90m ? "#991B1B" : usedPct > 75m ? "#B45309" : "#15803D");
            RenderKpi(sb, $"{freeDiskGB:F0} GB", es ? "Disponible" : "Available", "#15803D");
            sb.AppendLine("</div>");

            // Machines with low disk
            var lowDisk = data.Enrichment.Disks
                .Where(d => d.FreeGb.HasValue && d.TotalGb.HasValue && d.TotalGb > 0 && (d.FreeGb.Value / (decimal)d.TotalGb.Value) < 0.1m)
                .ToList();
            if (lowDisk.Count > 0)
            {
                sb.AppendLine($"<div style='padding:8px 12px;border:1px solid #E2E8F0;border-left:3px solid #991B1B;border-radius:4px;margin-bottom:10px'>");
                sb.AppendLine($"<strong style='color:#991B1B'>⚠ {lowDisk.Count} {(es ? "discos con <10% libre" : "disks with <10% free")}</strong>");
                sb.AppendLine("</div>");
            }
        }

        // Hardware summary table
        sb.AppendLine($"<h3>{(es ? "Resumen de Hardware" : "Hardware Summary")}</h3>");
        sb.AppendLine("<table class='data-table'><thead><tr>");
        sb.AppendLine($"<th>{(es ? "Equipo" : "Machine")}</th><th>OS</th><th>CPU</th><th>RAM</th><th>{(es ? "Disco" : "Disk")}</th><th>Score</th>");
        sb.AppendLine("</tr></thead><tbody>");
        foreach (var run in data.Runs.OrderByDescending(r => r.GlobalScore ?? 0).Take(20))
        {
            var m = run.Machine;
            if (m == null) continue;
            var scoreClass = (run.GlobalScore ?? 0) >= 80 ? "pass" : (run.GlobalScore ?? 0) >= 60 ? "warn" : "fail";
            sb.AppendLine("<tr>");
            sb.AppendLine($"<td>{ReportHelpers.HtmlEncode(m.Hostname ?? "—")}</td>");
            sb.AppendLine($"<td style='font-size:7pt'>{ReportHelpers.HtmlEncode(TruncateOs(m.OsName))}</td>");
            sb.AppendLine($"<td style='font-size:7pt'>{ReportHelpers.HtmlEncode(ReportHelpers.TruncateCpu(m.CpuName))}</td>");
            sb.AppendLine($"<td>{(m.RamGb.HasValue ? $"{m.RamGb:F0} GB" : "—")}</td>");
            sb.AppendLine($"<td>{(m.DiskSizeGb.HasValue ? $"{m.DiskSizeGb:F0} GB" : "—")}</td>");
            sb.AppendLine($"<td class='{scoreClass}' style='font-weight:700'>{run.GlobalScore ?? 0:F0}</td>");
            sb.AppendLine("</tr>");
        }
        if (data.Runs.Count > 20)
            sb.AppendLine($"<tr><td colspan='6' style='color:#64748B;font-style:italic'>...{(es ? $"y {data.Runs.Count - 20} más" : $"and {data.Runs.Count - 20} more")}</td></tr>");
        sb.AppendLine("</tbody></table>");

        return sb.ToString();
    }

    public string Render(ReportData data, ReportOptions options)
    {
        if (data.Runs.Count == 0) return "";

        var sb = new StringBuilder();
        var es = options.IsSpanish;
        var budget = new PageBudget();
        var pageTitle = SectionTitle(options)!;
        var machines = data.Runs.Select(r => r.Machine).Where(m => m != null).ToList();

        budget.StartPage(sb, pageTitle, data.Branding);

        // OS distribution
        var osGroups = machines.GroupBy(m => m.OsName ?? "Unknown").OrderByDescending(g => g.Count()).ToList();
        var osGridHeight = PageBudget.H3 + (int)Math.Ceiling(osGroups.Count / 3.0) * 36 + 14;
        if (budget.WouldOverflow(osGridHeight))
            budget.NewPage(sb, pageTitle, data.Branding);

        sb.AppendLine($"<h3>{(es ? "Distribución de Sistemas Operativos" : "Operating System Distribution")}</h3>");
        budget.Spend(PageBudget.H3);
        sb.AppendLine("<div style='display:grid;grid-template-columns:repeat(auto-fill,minmax(160px,1fr));gap:8px;margin-bottom:14px'>");
        foreach (var g in osGroups)
        {
            var isLegacy = g.Key.Contains("2008") || g.Key.Contains("2003") || g.Key.Contains("Windows 7") || g.Key.Contains("Vista");
            var color = isLegacy ? "#991B1B" : "#0F172A";
            sb.AppendLine($"<div style='padding:8px 10px;border:1px solid #E2E8F0;border-radius:4px;display:flex;justify-content:space-between;align-items:center'>");
            sb.AppendLine($"<span style='font-size:8pt;color:{color}'>{ReportHelpers.HtmlEncode(TruncateOs(g.Key))}</span>");
            sb.AppendLine($"<span style='font-weight:700;color:{color}'>{g.Count()}</span>");
            sb.AppendLine("</div>");
        }
        sb.AppendLine("</div>");
        budget.Spend(osGridHeight - PageBudget.H3);

        // Security coverage table
        var coverageHeight = PageBudget.H3 + PageBudget.TableHeader + 4 * PageBudget.TableRow;
        if (budget.WouldOverflow(coverageHeight))
            budget.NewPage(sb, pageTitle, data.Branding);

        var bitlocker = machines.Count(m => m.Bitlocker == true);
        var tpm = machines.Count(m => m.TpmPresent == true);
        var secureBoot = machines.Count(m => m.SecureBoot == true);
        var domainJoined = machines.Count(m => !string.IsNullOrEmpty(m.DomainName));
        var total = machines.Count;

        sb.AppendLine($"<h3>{(es ? "Cobertura de Seguridad" : "Security Coverage")}</h3>");
        sb.AppendLine("<table class='data-table'><thead><tr>");
        sb.AppendLine($"<th>{(es ? "Característica" : "Feature")}</th><th>{(es ? "Habilitado" : "Enabled")}</th><th>{(es ? "Faltante" : "Missing")}</th><th>%</th>");
        sb.AppendLine("</tr></thead><tbody>");
        RenderCoverageRow(sb, "BitLocker", bitlocker, total);
        RenderCoverageRow(sb, "TPM", tpm, total);
        RenderCoverageRow(sb, "Secure Boot", secureBoot, total);
        RenderCoverageRow(sb, es ? "Dominio" : "Domain Joined", domainJoined, total);
        sb.AppendLine("</tbody></table>");
        budget.Spend(coverageHeight);

        // Disk usage
        if (data.Enrichment.Disks.Count > 0)
        {
            var diskHeight = PageBudget.H3 + PageBudget.ScoreCards + 40;
            if (budget.WouldOverflow(diskHeight))
                budget.NewPage(sb, pageTitle, data.Branding);

            sb.AppendLine($"<h3>{(es ? "Almacenamiento" : "Storage")}</h3>");
            budget.Spend(PageBudget.H3);

            var totalDiskGB = data.Enrichment.Disks.Sum(d => (decimal)(d.TotalGb ?? 0));
            var freeDiskGB = data.Enrichment.Disks.Sum(d => d.FreeGb ?? 0m);
            var usedDiskGB = totalDiskGB - freeDiskGB;
            var usedPct = totalDiskGB > 0 ? 100m * usedDiskGB / totalDiskGB : 0m;

            sb.AppendLine("<div style='display:grid;grid-template-columns:repeat(3,1fr);gap:10px;margin-bottom:10px'>");
            RenderKpi(sb, $"{totalDiskGB:F0} GB", es ? "Capacidad Total" : "Total Capacity", "#0F172A");
            RenderKpi(sb, $"{usedPct:F0}%", es ? "Utilizado" : "Used", usedPct > 90m ? "#991B1B" : usedPct > 75m ? "#B45309" : "#15803D");
            RenderKpi(sb, $"{freeDiskGB:F0} GB", es ? "Disponible" : "Available", "#15803D");
            sb.AppendLine("</div>");
            budget.Spend(PageBudget.ScoreCards);

            var lowDisk = data.Enrichment.Disks
                .Where(d => d.FreeGb.HasValue && d.TotalGb.HasValue && d.TotalGb > 0 && (d.FreeGb.Value / (decimal)d.TotalGb.Value) < 0.1m)
                .ToList();
            if (lowDisk.Count > 0)
            {
                sb.AppendLine($"<div style='padding:8px 12px;border:1px solid #E2E8F0;border-left:3px solid #991B1B;border-radius:4px;margin-bottom:10px'>");
                sb.AppendLine($"<strong style='color:#991B1B'>⚠ {lowDisk.Count} {(es ? "discos con <10% libre" : "disks with <10% free")}</strong>");
                sb.AppendLine("</div>");
                budget.Spend(40);
            }
        }

        // Hardware summary table with pagination
        if (budget.WouldOverflow(PageBudget.H3 + PageBudget.TableHeader + PageBudget.TableRow))
            budget.NewPage(sb, pageTitle, data.Branding);

        sb.AppendLine($"<h3>{(es ? "Resumen de Hardware" : "Hardware Summary")}</h3>");
        budget.Spend(PageBudget.H3);

        sb.AppendLine("<table class='data-table'><thead><tr>");
        sb.AppendLine($"<th>{(es ? "Equipo" : "Machine")}</th><th>OS</th><th>CPU</th><th>RAM</th><th>{(es ? "Disco" : "Disk")}</th><th>Score</th>");
        sb.AppendLine("</tr></thead><tbody>");
        budget.Spend(PageBudget.TableHeader);

        var hwTableHeader = $"<th>{(es ? "Equipo" : "Machine")}</th><th>OS</th><th>CPU</th><th>RAM</th><th>{(es ? "Disco" : "Disk")}</th><th>Score</th>";

        foreach (var run in data.Runs.OrderByDescending(r => r.GlobalScore ?? 0).Take(50))
        {
            var m = run.Machine;
            if (m == null) continue;

            if (budget.WouldOverflow(PageBudget.TableRow))
            {
                sb.AppendLine("</tbody></table>");
                budget.NewPage(sb, pageTitle, data.Branding);
                sb.AppendLine($"<table class='data-table'><thead><tr>{hwTableHeader}</tr></thead><tbody>");
                budget.Spend(PageBudget.TableHeader);
            }

            var scoreClass = (run.GlobalScore ?? 0) >= 80 ? "pass" : (run.GlobalScore ?? 0) >= 60 ? "warn" : "fail";
            sb.AppendLine("<tr>");
            sb.AppendLine($"<td>{ReportHelpers.HtmlEncode(m.Hostname ?? "—")}</td>");
            sb.AppendLine($"<td style='font-size:7pt'>{ReportHelpers.HtmlEncode(TruncateOs(m.OsName))}</td>");
            sb.AppendLine($"<td style='font-size:7pt'>{ReportHelpers.HtmlEncode(ReportHelpers.TruncateCpu(m.CpuName))}</td>");
            sb.AppendLine($"<td>{(m.RamGb.HasValue ? $"{m.RamGb:F0} GB" : "—")}</td>");
            sb.AppendLine($"<td>{(m.DiskSizeGb.HasValue ? $"{m.DiskSizeGb:F0} GB" : "—")}</td>");
            sb.AppendLine($"<td class='{scoreClass}' style='font-weight:700'>{run.GlobalScore ?? 0:F0}</td>");
            sb.AppendLine("</tr>");
            budget.Spend(PageBudget.TableRow);
        }
        if (data.Runs.Count > 50)
            sb.AppendLine($"<tr><td colspan='6' style='color:#64748B;font-style:italic'>...{(es ? $"y {data.Runs.Count - 50} más" : $"and {data.Runs.Count - 50} more")}</td></tr>");
        sb.AppendLine("</tbody></table>");

        budget.EndPage(sb);
        return sb.ToString();
    }

    private static void RenderCoverageRow(StringBuilder sb, string label, int enabled, int total)
    {
        var pct = total > 0 ? 100.0 * enabled / total : 0;
        var pctClass = pct >= 90 ? "pass" : pct >= 60 ? "warn" : "fail";
        sb.AppendLine($"<tr><td>{ReportHelpers.HtmlEncode(label)}</td><td class='pass'>{enabled}</td><td class='fail'>{total - enabled}</td><td class='{pctClass}' style='font-weight:700'>{pct:F0}%</td></tr>");
    }

    private static void RenderKpi(StringBuilder sb, string value, string label, string color)
    {
        sb.AppendLine($"<div style='text-align:center;padding:8px;border:1px solid #E2E8F0;border-top:2px solid {color};border-radius:4px'>");
        sb.AppendLine($"<div style='font-size:14pt;font-weight:800;color:{color}'>{value}</div>");
        sb.AppendLine($"<div style='font-size:7pt;color:#64748B'>{ReportHelpers.HtmlEncode(label)}</div>");
        sb.AppendLine("</div>");
    }

    private static string TruncateOs(string? os)
    {
        if (os == null) return "Unknown";
        return os.Length > 35 ? os[..32] + "..." : os;
    }
}

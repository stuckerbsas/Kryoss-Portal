using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class HygieneBlock : IFlowBlock
{
    public string? SectionTitle(ReportOptions options) =>
        options.IsSpanish ? "Auditoría Active Directory" : "Active Directory Audit";

    public int EstimateHeight(ReportData data)
    {
        if (data.Hygiene == null) return 0;
        int h = 200;
        if (data.Hygiene.Findings.Count > 0) h += 200;
        if (data.Hygiene.Findings.Count(f => f.Status == "PrivilegedAccount") > 0) h += 150;
        return h;
    }

    public string RenderContent(ReportData data, ReportOptions options)
    {
        if (data.Hygiene == null) return "";

        var sb = new StringBuilder();
        var es = options.IsSpanish;
        var h = data.Hygiene;

        // Summary KPIs
        sb.AppendLine("<div style='display:grid;grid-template-columns:repeat(4,1fr);gap:10px;margin-bottom:16px'>");
        RenderKpi(sb, h.TotalMachines.ToString(), es ? "Equipos" : "Machines", "#0F172A");
        RenderKpi(sb, h.TotalUsers.ToString(), es ? "Usuarios" : "Users", "#0F172A");
        RenderKpi(sb, (h.StaleMachines + h.StaleUsers).ToString(), es ? "Objetos Obsoletos" : "Stale Objects", h.StaleMachines + h.StaleUsers > 0 ? "#B45309" : "#15803D");
        RenderKpi(sb, h.PwdNeverExpire.ToString(), es ? "Pwd No Expira" : "Pwd Never Expire", h.PwdNeverExpire > 0 ? "#991B1B" : "#15803D");
        sb.AppendLine("</div>");

        // Object health table
        sb.AppendLine($"<h3>{(es ? "Salud de Objetos" : "Object Health")}</h3>");
        sb.AppendLine("<table class='data-table'><thead><tr>");
        sb.AppendLine($"<th>{(es ? "Categoría" : "Category")}</th><th>{(es ? "Equipos" : "Machines")}</th><th>{(es ? "Usuarios" : "Users")}</th><th>{(es ? "Riesgo" : "Risk")}</th>");
        sb.AppendLine("</tr></thead><tbody>");

        RenderHealthRow(sb, es ? "Obsoletos (>90 días)" : "Stale (>90 days)", h.StaleMachines, h.StaleUsers, "warn");
        RenderHealthRow(sb, es ? "Dormidos (>180 días)" : "Dormant (>180 days)", h.DormantMachines, h.DormantUsers, "fail");
        RenderHealthRow(sb, es ? "Deshabilitados" : "Disabled", 0, h.DisabledUsers, "");
        sb.AppendLine("</tbody></table>");

        // Security findings
        if (h.Findings.Count > 0)
        {
            var privileged = h.Findings.Where(f => f.Status == "PrivilegedAccount").ToList();
            var localAdmins = h.Findings.Where(f => f.Status == "LocalAdmin").ToList();
            var kerberoastable = h.Findings.Where(f => f.Status == "Kerberoastable").ToList();
            var unconstrained = h.Findings.Where(f => f.Status == "UnconstrainedDelegation").ToList();
            var noLaps = h.Findings.Where(f => f.Status == "NoLAPS").ToList();
            var adminCount = h.Findings.Where(f => f.Status == "AdminCountResidual").ToList();
            var domainInfo = h.Findings.FirstOrDefault(f => f.ObjectType == "DomainInfo");

            // Security risk summary
            sb.AppendLine($"<h3>{(es ? "Riesgos de Seguridad AD" : "AD Security Risks")}</h3>");
            sb.AppendLine("<div style='display:grid;grid-template-columns:repeat(3,1fr);gap:8px;margin-bottom:12px'>");
            RenderRiskBadge(sb, privileged.Count, es ? "Cuentas Privilegiadas" : "Privileged Accounts", "#991B1B");
            RenderRiskBadge(sb, kerberoastable.Count, "Kerberoastable", "#991B1B");
            RenderRiskBadge(sb, unconstrained.Count, es ? "Delegación Sin Restricción" : "Unconstrained Delegation", "#B45309");
            RenderRiskBadge(sb, localAdmins.Count, es ? "Admin Local Excesivo" : "Excessive Local Admin", "#B45309");
            RenderRiskBadge(sb, noLaps.Count, es ? "Sin LAPS" : "No LAPS", "#B45309");
            RenderRiskBadge(sb, adminCount.Count, "adminCount Residual", "#64748B");
            sb.AppendLine("</div>");

            // Privileged accounts detail
            if (privileged.Count > 0)
            {
                sb.AppendLine($"<h4>{(es ? "Cuentas Privilegiadas" : "Privileged Accounts")} ({privileged.Count})</h4>");
                sb.AppendLine("<table class='data-table'><thead><tr>");
                sb.AppendLine($"<th>{(es ? "Cuenta" : "Account")}</th><th>{(es ? "Tipo" : "Type")}</th><th>{(es ? "Detalle" : "Detail")}</th>");
                sb.AppendLine("</tr></thead><tbody>");
                foreach (var p in privileged.Take(20))
                {
                    sb.AppendLine($"<tr><td>{ReportHelpers.HtmlEncode(p.Name)}</td>");
                    sb.AppendLine($"<td>{ReportHelpers.HtmlEncode(p.ObjectType)}</td>");
                    sb.AppendLine($"<td style='font-size:8pt;color:#64748B'>{ReportHelpers.HtmlEncode(p.Detail ?? "—")}</td></tr>");
                }
                if (privileged.Count > 20)
                    sb.AppendLine($"<tr><td colspan='3' style='color:#64748B;font-style:italic'>...{(es ? $"y {privileged.Count - 20} más" : $"and {privileged.Count - 20} more")}</td></tr>");
                sb.AppendLine("</tbody></table>");
            }

            // Domain info
            if (domainInfo != null)
            {
                sb.AppendLine($"<div style='margin-top:12px;padding:10px 14px;border:1px solid #E2E8F0;border-left:3px solid #0F172A;border-radius:4px'>");
                sb.AppendLine($"<strong>{(es ? "Dominio" : "Domain")}:</strong> {ReportHelpers.HtmlEncode(domainInfo.Name)}");
                if (!string.IsNullOrEmpty(domainInfo.Detail))
                    sb.AppendLine($" <span style='color:#64748B'>— {ReportHelpers.HtmlEncode(domainInfo.Detail)}</span>");
                sb.AppendLine("</div>");
            }
        }

        return sb.ToString();
    }

    public string Render(ReportData data, ReportOptions options)
    {
        if (data.Hygiene == null) return "";

        var sb = new StringBuilder();
        var es = options.IsSpanish;
        var h = data.Hygiene;
        var title = SectionTitle(options)!;
        var budget = new PageBudget();

        budget.StartPage(sb, title, data.Branding);

        sb.AppendLine("<div style='display:grid;grid-template-columns:repeat(4,1fr);gap:10px;margin-bottom:16px'>");
        RenderKpi(sb, h.TotalMachines.ToString(), es ? "Equipos" : "Machines", "#0F172A");
        RenderKpi(sb, h.TotalUsers.ToString(), es ? "Usuarios" : "Users", "#0F172A");
        RenderKpi(sb, (h.StaleMachines + h.StaleUsers).ToString(), es ? "Objetos Obsoletos" : "Stale Objects", h.StaleMachines + h.StaleUsers > 0 ? "#B45309" : "#15803D");
        RenderKpi(sb, h.PwdNeverExpire.ToString(), es ? "Pwd No Expira" : "Pwd Never Expire", h.PwdNeverExpire > 0 ? "#991B1B" : "#15803D");
        sb.AppendLine("</div>");
        budget.Spend(PageBudget.ScoreCards);

        sb.AppendLine($"<h3>{(es ? "Salud de Objetos" : "Object Health")}</h3>");
        budget.Spend(PageBudget.H3);
        sb.AppendLine("<table class='data-table'><thead><tr>");
        sb.AppendLine($"<th>{(es ? "Categoría" : "Category")}</th><th>{(es ? "Equipos" : "Machines")}</th><th>{(es ? "Usuarios" : "Users")}</th><th>{(es ? "Riesgo" : "Risk")}</th>");
        sb.AppendLine("</tr></thead><tbody>");
        budget.Spend(PageBudget.TableHeader);
        RenderHealthRow(sb, es ? "Obsoletos (>90 días)" : "Stale (>90 days)", h.StaleMachines, h.StaleUsers, "warn");
        RenderHealthRow(sb, es ? "Dormidos (>180 días)" : "Dormant (>180 days)", h.DormantMachines, h.DormantUsers, "fail");
        RenderHealthRow(sb, es ? "Deshabilitados" : "Disabled", 0, h.DisabledUsers, "");
        sb.AppendLine("</tbody></table>");
        budget.Spend(PageBudget.TableRow * 3);

        if (h.Findings.Count > 0)
        {
            var privileged = h.Findings.Where(f => f.Status == "PrivilegedAccount").ToList();
            var localAdmins = h.Findings.Where(f => f.Status == "LocalAdmin").ToList();
            var kerberoastable = h.Findings.Where(f => f.Status == "Kerberoastable").ToList();
            var unconstrained = h.Findings.Where(f => f.Status == "UnconstrainedDelegation").ToList();
            var noLaps = h.Findings.Where(f => f.Status == "NoLAPS").ToList();
            var adminCountFindings = h.Findings.Where(f => f.Status == "AdminCountResidual").ToList();
            var domainInfo = h.Findings.FirstOrDefault(f => f.ObjectType == "DomainInfo");

            var badgeCount = new[] { privileged.Count, kerberoastable.Count, unconstrained.Count, localAdmins.Count, noLaps.Count, adminCountFindings.Count }.Count(c => c > 0);
            var risksHeight = PageBudget.H3 + ((badgeCount + 2) / 3) * 45;

            if (budget.WouldOverflow(risksHeight))
                budget.NewPage(sb, title, data.Branding);

            sb.AppendLine($"<h3>{(es ? "Riesgos de Seguridad AD" : "AD Security Risks")}</h3>");
            budget.Spend(PageBudget.H3);
            sb.AppendLine("<div style='display:grid;grid-template-columns:repeat(3,1fr);gap:8px;margin-bottom:12px'>");
            RenderRiskBadge(sb, privileged.Count, es ? "Cuentas Privilegiadas" : "Privileged Accounts", "#991B1B");
            RenderRiskBadge(sb, kerberoastable.Count, "Kerberoastable", "#991B1B");
            RenderRiskBadge(sb, unconstrained.Count, es ? "Delegación Sin Restricción" : "Unconstrained Delegation", "#B45309");
            RenderRiskBadge(sb, localAdmins.Count, es ? "Admin Local Excesivo" : "Excessive Local Admin", "#B45309");
            RenderRiskBadge(sb, noLaps.Count, es ? "Sin LAPS" : "No LAPS", "#B45309");
            RenderRiskBadge(sb, adminCountFindings.Count, "adminCount Residual", "#64748B");
            sb.AppendLine("</div>");
            budget.Spend(((badgeCount + 2) / 3) * 45);

            if (privileged.Count > 0)
            {
                if (budget.WouldOverflow(30 + PageBudget.TableHeader + PageBudget.TableRow))
                    budget.NewPage(sb, title, data.Branding);

                sb.AppendLine($"<h4>{(es ? "Cuentas Privilegiadas" : "Privileged Accounts")} ({privileged.Count})</h4>");
                budget.Spend(30);
                sb.AppendLine("<table class='data-table'><thead><tr>");
                sb.AppendLine($"<th>{(es ? "Cuenta" : "Account")}</th><th>{(es ? "Tipo" : "Type")}</th><th>{(es ? "Detalle" : "Detail")}</th>");
                sb.AppendLine("</tr></thead><tbody>");
                budget.Spend(PageBudget.TableHeader);

                foreach (var p in privileged.Take(20))
                {
                    if (budget.WouldOverflow(PageBudget.TableRow))
                    {
                        sb.AppendLine("</tbody></table>");
                        budget.NewPage(sb, title, data.Branding);
                        sb.AppendLine("<table class='data-table'><thead><tr>");
                        sb.AppendLine($"<th>{(es ? "Cuenta" : "Account")}</th><th>{(es ? "Tipo" : "Type")}</th><th>{(es ? "Detalle" : "Detail")}</th>");
                        sb.AppendLine("</tr></thead><tbody>");
                        budget.Spend(PageBudget.TableHeader);
                    }
                    sb.AppendLine($"<tr><td>{ReportHelpers.HtmlEncode(p.Name)}</td>");
                    sb.AppendLine($"<td>{ReportHelpers.HtmlEncode(p.ObjectType)}</td>");
                    sb.AppendLine($"<td style='font-size:8pt;color:#64748B'>{ReportHelpers.HtmlEncode(p.Detail ?? "—")}</td></tr>");
                    budget.Spend(PageBudget.TableRow);
                }
                if (privileged.Count > 20)
                    sb.AppendLine($"<tr><td colspan='3' style='color:#64748B;font-style:italic'>...{(es ? $"y {privileged.Count - 20} más" : $"and {privileged.Count - 20} more")}</td></tr>");
                sb.AppendLine("</tbody></table>");
            }

            if (domainInfo != null)
            {
                if (budget.WouldOverflow(50))
                    budget.NewPage(sb, title, data.Branding);
                sb.AppendLine($"<div style='margin-top:12px;padding:10px 14px;border:1px solid #E2E8F0;border-left:3px solid #0F172A;border-radius:4px'>");
                sb.AppendLine($"<strong>{(es ? "Dominio" : "Domain")}:</strong> {ReportHelpers.HtmlEncode(domainInfo.Name)}");
                if (!string.IsNullOrEmpty(domainInfo.Detail))
                    sb.AppendLine($" <span style='color:#64748B'>— {ReportHelpers.HtmlEncode(domainInfo.Detail)}</span>");
                sb.AppendLine("</div>");
                budget.Spend(50);
            }
        }

        budget.EndPage(sb);
        return sb.ToString();
    }

    private static void RenderKpi(StringBuilder sb, string value, string label, string color)
    {
        sb.AppendLine($"<div style='text-align:center;padding:10px;border:1px solid #E2E8F0;border-top:3px solid {color};border-radius:6px'>");
        sb.AppendLine($"<div style='font-size:20pt;font-weight:800;color:{color}'>{value}</div>");
        sb.AppendLine($"<div style='font-size:8pt;color:#64748B'>{ReportHelpers.HtmlEncode(label)}</div>");
        sb.AppendLine("</div>");
    }

    private static void RenderHealthRow(StringBuilder sb, string label, int machines, int users, string cls)
    {
        sb.AppendLine($"<tr><td>{ReportHelpers.HtmlEncode(label)}</td>");
        sb.AppendLine($"<td class='{cls}'>{machines}</td>");
        sb.AppendLine($"<td class='{cls}'>{users}</td>");
        var risk = (machines + users) switch { 0 => "Low", < 10 => "Medium", _ => "High" };
        var riskClass = risk == "High" ? "fail" : risk == "Medium" ? "warn" : "pass";
        sb.AppendLine($"<td class='{riskClass}'>{risk}</td></tr>");
    }

    private static void RenderRiskBadge(StringBuilder sb, int count, string label, string color)
    {
        if (count == 0) return;
        sb.AppendLine($"<div style='padding:8px;border:1px solid #E2E8F0;border-left:3px solid {color};border-radius:4px;display:flex;justify-content:space-between;align-items:center'>");
        sb.AppendLine($"<span style='font-size:8pt'>{ReportHelpers.HtmlEncode(label)}</span>");
        sb.AppendLine($"<span style='font-size:12pt;font-weight:700;color:{color}'>{count}</span>");
        sb.AppendLine("</div>");
    }
}

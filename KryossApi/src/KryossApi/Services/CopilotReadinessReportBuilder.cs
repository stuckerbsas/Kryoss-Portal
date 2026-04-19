using System.Text;
using KryossApi.Data.Entities;
using KryossApi.Services.CopilotReadiness;

namespace KryossApi.Services;

/// <summary>
/// Generates the self-contained HTML "Unified M365 Security &amp; Copilot Readiness"
/// report.  11 sections (cover through methodology) on strict A4 pages with
/// Brand 2025 styling identical to other org-level reports.
/// </summary>
internal static class CopilotReadinessReportBuilder
{
    // ──────────────────────────────────────────────────────────────────────
    // PUBLIC ENTRY POINT
    // ──────────────────────────────────────────────────────────────────────

    public static string BuildUnifiedM365Report(
        Organization org,
        List<AssessmentRun> runs,
        List<OrgControlResult> allResults,
        ReportBranding brand,
        List<FrameworkScoreDto> frameworkScores,
        HygieneScanDto? hygiene,
        OrgEnrichment enrichment,
        ReportUserInfo userInfo,
        CopilotReadinessScan? copilotScan,
        List<M365Finding>? m365Findings,
        bool m365Connected,
        string lang)
    {
        var sb = new StringBuilder(32_000);
        var s = (string key) => CopilotReadinessStrings.Get(key, lang);
        var es = lang == "es";
        var totalMachines = runs.Count;
        var scanDate = copilotScan?.CompletedAt ?? copilotScan?.StartedAt ?? DateTime.UtcNow;
        var tenantId = copilotScan?.TenantId.ToString() ?? "—";
        var overallScore = copilotScan?.OverallScore ?? 0m;
        var verdict = copilotScan?.Verdict ?? "not_ready";

        var reportTitle = s("cover.title");

        // ── HTML head + styles ──
        AppendHtmlHead(sb, $"{reportTitle} - {org.Name}", brand, lang,
            userInfo, $"{totalMachines} {s("cover.devices_label")} · {org.Name}");

        // ══════════════════════════════════════════════════════════════════
        // SECTION 1: COVER PAGE
        // ══════════════════════════════════════════════════════════════════
        sb.AppendLine("<div class='cover'>");
        sb.AppendLine($"<img class='cover-ribbon' src='{RibbonData.DataUri}' alt='' />");
        sb.AppendLine("<div class='cover-content'>");
        if (brand.LogoUrl is not null)
            sb.AppendLine($"<img src='{H(brand.LogoUrl)}' class='logo' alt='{H(brand.CompanyName)}'>");
        sb.AppendLine($"<p class='eyebrow'>{H(s("cover.eyebrow"))}</p>");
        sb.AppendLine($"<h1>{H(reportTitle)}</h1>");
        sb.AppendLine($"<h2>{H(org.Name)}</h2>");
        sb.AppendLine($"<p class='meta'>{H(s("cover.date_label"))}: {scanDate:yyyy-MM-dd} &mdash; {H(s("cover.tenant_label"))}: {H(tenantId)}</p>");

        // Large overall score circle
        var scoreColor = overallScore >= 4m ? "#15803D" : overallScore >= 3m ? "#B45309" : "#991B1B";
        sb.AppendLine($"<div class='score-circle' style='border-color:{scoreColor};color:{scoreColor}'>{overallScore:F1}</div>");

        // Verdict text
        var verdictKey = verdict switch { "ready" => "verdict.ready", "nearly_ready" => "verdict.nearly", _ => "verdict.not_ready" };
        var verdictDescKey = verdict switch { "ready" => "verdict.ready_desc", "nearly_ready" => "verdict.nearly_desc", _ => "verdict.not_ready_desc" };
        sb.AppendLine($"<p class='verdict' style='color:{scoreColor}'>{H(s(verdictKey))}</p>");
        sb.AppendLine($"<p class='verdict-desc'>{H(s(verdictDescKey))}</p>");

        sb.AppendLine("</div></div>"); // cover

        // ══════════════════════════════════════════════════════════════════
        // SECTION 2: EXECUTIVE SUMMARY
        // ══════════════════════════════════════════════════════════════════
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, s("exec.title"), brand, s("exec.eyebrow"));
        sb.AppendLine("<div class='pb'>");

        if (copilotScan == null)
        {
            AppendNoScanBanner(sb, s("verdict.no_scan"));
        }
        else
        {
            // Dedupe: multiple pipelines may emit the same metric key (e.g. Entra+Defender
            // both set risky_users_high). Last writer wins — matches in-memory dict semantics.
            var metrics = copilotScan.Metrics?
                .GroupBy(m => m.MetricKey)
                .ToDictionary(g => g.Key, g => g.Last().MetricValue) ?? new();
            string Mv(string k) => metrics.GetValueOrDefault(k, "—");

            // 6 KPI stat boxes
            sb.AppendLine("<div class='summary-grid'>");
            AppendKpiBox(sb, s("exec.kpi.label_coverage"), Mv("label_coverage_pct") + "%", brand.PrimaryColor);
            AppendKpiBox(sb, s("exec.kpi.overshared"), Mv("overshared_pct") + "%", "#D97706");
            AppendKpiBox(sb, s("exec.kpi.external_users"), Mv("total_external_users"), "#6366F1");
            AppendKpiBox(sb, s("exec.kpi.ca_score"), Mv("ca_score_pct") + "%", "#2563EB");
            AppendKpiBox(sb, s("exec.kpi.zt_gaps"), Mv("zt_gaps"), "#C0392B");
            AppendKpiBox(sb, s("exec.kpi.compliance_gaps"), Mv("compliance_gaps"), "#C0392B");
            sb.AppendLine("</div>");

            // D1-D6 horizontal bar scorecard
            sb.AppendLine($"<h3>{H(s("exec.scorecard"))}</h3>");
            sb.AppendLine("<div class='dim-scorecard'>");
            AppendDimensionBar(sb, s("dim.d1"), copilotScan.D1Score, brand);
            AppendDimensionBar(sb, s("dim.d2"), copilotScan.D2Score, brand);
            AppendDimensionBar(sb, s("dim.d3"), copilotScan.D3Score, brand);
            AppendDimensionBar(sb, s("dim.d4"), copilotScan.D4Score, brand);
            AppendDimensionBar(sb, s("dim.d5"), copilotScan.D5Score, brand);
            AppendDimensionBar(sb, s("dim.d6"), copilotScan.D6Score, brand);
            sb.AppendLine("</div>");

            // Copilot license banner
            var hasCopilot = metrics.GetValueOrDefault("copilot_licensed", "false") == "true";
            var licColor = hasCopilot ? "#15803D" : "#B45309";
            var licBg = hasCopilot ? "#F0FDF4" : "#FFFBEB";
            var licText = hasCopilot ? s("exec.copilot_detected") : s("exec.copilot_not_detected");
            sb.AppendLine($"<div class='license-banner' style='background:{licBg};border-left:4px solid {licColor};color:{licColor}'>");
            sb.AppendLine($"<strong>{H(s("exec.copilot_license"))}</strong>: {H(licText)}");
            sb.AppendLine("</div>");

            // Action count
            var actionCount = copilotScan.Findings?.Count(f =>
                f.Status is "Critical" or "Action Required" or "Warning") ?? 0;
            if (actionCount > 0)
            {
                sb.AppendLine($"<p class='action-count'>{H(s("exec.actions_before_copilot").Replace("{0}", actionCount.ToString()))}</p>");
            }
        }

        sb.AppendLine("</div></div>"); // page

        // ══════════════════════════════════════════════════════════════════
        // SECTION 3: M365 SECURITY POSTURE (existing 50 checks)
        // ══════════════════════════════════════════════════════════════════
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, s("m365sec.title"), brand, s("m365sec.eyebrow"));
        sb.AppendLine("<div class='pb'>");

        if (m365Findings == null || m365Findings.Count == 0)
        {
            AppendNoScanBanner(sb, s("m365sec.no_data"));
        }
        else
        {
            // Category breakdown table
            var byCat = m365Findings.GroupBy(f => f.Category).OrderBy(g => g.Key).ToList();
            sb.AppendLine("<table class='data-table'>");
            sb.AppendLine($"<thead><tr><th>{H(s("m365sec.category"))}</th><th style='text-align:center'>Total</th><th style='text-align:center'>{H(s("m365sec.pass"))}</th><th style='text-align:center'>{H(s("m365sec.fail"))}</th><th style='text-align:center'>{H(s("m365sec.warn"))}</th></tr></thead>");
            sb.AppendLine("<tbody>");
            foreach (var g in byCat)
            {
                var catPass = g.Count(f => f.Status.Equals("pass", StringComparison.OrdinalIgnoreCase));
                var catFail = g.Count(f => f.Status.Equals("fail", StringComparison.OrdinalIgnoreCase));
                var catWarn = g.Count(f => f.Status.Equals("warn", StringComparison.OrdinalIgnoreCase) || f.Status.Equals("warning", StringComparison.OrdinalIgnoreCase));
                sb.AppendLine($"<tr><td>{H(g.Key)}</td><td style='text-align:center'>{g.Count()}</td><td style='text-align:center;color:#15803D'>{catPass}</td><td style='text-align:center;color:#C0392B'>{catFail}</td><td style='text-align:center;color:#D97706'>{catWarn}</td></tr>");
            }
            sb.AppendLine("</tbody></table>");

            // Top 10 critical findings
            var top10 = m365Findings
                .Where(f => f.Status.Equals("fail", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.Severity.Equals("critical", StringComparison.OrdinalIgnoreCase) ? 4
                    : f.Severity.Equals("high", StringComparison.OrdinalIgnoreCase) ? 3
                    : f.Severity.Equals("medium", StringComparison.OrdinalIgnoreCase) ? 2 : 1)
                .Take(10)
                .ToList();

            if (top10.Count > 0)
            {
                sb.AppendLine($"<h3>{H(s("m365sec.top10"))}</h3>");
                sb.AppendLine("<table class='data-table'>");
                sb.AppendLine($"<thead><tr><th>{H(s("m365sec.check_id"))}</th><th>{H(s("m365sec.name"))}</th><th>{H(s("m365sec.severity"))}</th><th>{H(s("m365sec.status"))}</th></tr></thead>");
                sb.AppendLine("<tbody>");
                foreach (var f in top10)
                {
                    sb.AppendLine($"<tr><td><code>{H(f.CheckId)}</code></td><td>{H(f.Name)}</td><td>{SeverityBadge(f.Severity, s)}</td><td>{StatusBadge("fail", s)}</td></tr>");
                }
                sb.AppendLine("</tbody></table>");
            }
        }

        sb.AppendLine("</div></div>"); // page

        // ══════════════════════════════════════════════════════════════════
        // SECTION 4: D1 — Information Protection
        // ══════════════════════════════════════════════════════════════════
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, s("d1.title"), brand, s("d1.eyebrow"));
        sb.AppendLine("<div class='pb'>");

        if (copilotScan == null)
        {
            AppendNoScanBanner(sb, s("verdict.no_scan"));
        }
        else
        {
            var sites = copilotScan.SharepointSites?.OrderByDescending(sp => sp.TotalFiles).ToList() ?? [];
            var totalFiles = sites.Sum(sp => sp.TotalFiles);
            var totalLabeled = sites.Sum(sp => sp.LabeledFiles);
            var coveragePct = totalFiles > 0 ? Math.Round((double)totalLabeled / totalFiles * 100, 1) : 0;

            // Label coverage bar
            sb.AppendLine($"<h3>{H(s("d1.label_coverage"))}: {coveragePct}%</h3>");
            sb.AppendLine("<div class='pct-bar-track'>");
            var barColor = coveragePct >= 80 ? "#15803D" : coveragePct >= 50 ? "#D97706" : "#C0392B";
            sb.AppendLine($"<div class='pct-bar-fill' style='width:{coveragePct}%;background:{barColor}'></div>");
            sb.AppendLine("</div>");

            // SharePoint sites table
            if (sites.Count > 0)
            {
                sb.AppendLine($"<h3>{H(s("d1.top_unlabeled"))}</h3>");
                sb.AppendLine("<table class='data-table'>");
                sb.AppendLine($"<thead><tr><th>{H(s("d1.site"))}</th><th style='text-align:center'>{H(s("d1.total_files"))}</th><th style='text-align:center'>{H(s("d1.labeled"))}</th><th style='text-align:center'>{H(s("d1.pct"))}</th><th>{H(s("d1.labels"))}</th></tr></thead>");
                sb.AppendLine("<tbody>");
                foreach (var sp in sites.Take(15))
                {
                    var spPct = sp.TotalFiles > 0 ? Math.Round((double)sp.LabeledFiles / sp.TotalFiles * 100, 1) : 0;
                    var labels = string.IsNullOrEmpty(sp.TopLabels) ? "—" : sp.TopLabels;
                    sb.AppendLine($"<tr><td>{H(sp.SiteTitle ?? sp.SiteUrl)}</td><td style='text-align:center'>{sp.TotalFiles:N0}</td><td style='text-align:center'>{sp.LabeledFiles:N0}</td><td style='text-align:center'>{spPct}%</td><td style='font-size:10px'>{H(labels)}</td></tr>");
                }
                sb.AppendLine("</tbody></table>");
            }
        }

        sb.AppendLine("</div></div>"); // page

        // ══════════════════════════════════════════════════════════════════
        // SECTION 5: D2 — Data Sharing & Oversharing
        // ══════════════════════════════════════════════════════════════════
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, s("d2.title"), brand, s("d2.eyebrow"));
        sb.AppendLine("<div class='pb'>");

        if (copilotScan == null)
        {
            AppendNoScanBanner(sb, s("verdict.no_scan"));
        }
        else
        {
            var sites = copilotScan.SharepointSites?.Where(sp => sp.OversharedFiles > 0)
                .OrderByDescending(sp => sp.OversharedFiles).ToList() ?? [];
            var totalFiles = copilotScan.SharepointSites?.Sum(sp => sp.TotalFiles) ?? 0;
            var totalOvershared = copilotScan.SharepointSites?.Sum(sp => sp.OversharedFiles) ?? 0;
            var oversharedPct = totalFiles > 0 ? Math.Round((double)totalOvershared / totalFiles * 100, 1) : 0;

            sb.AppendLine($"<h3>{H(s("d2.overshare_pct"))}: {oversharedPct}%</h3>");
            sb.AppendLine("<div class='pct-bar-track'>");
            var barColor = oversharedPct <= 5 ? "#15803D" : oversharedPct <= 15 ? "#D97706" : "#C0392B";
            sb.AppendLine($"<div class='pct-bar-fill' style='width:{Math.Min(oversharedPct, 100)}%;background:{barColor}'></div>");
            sb.AppendLine("</div>");

            if (sites.Count > 0)
            {
                sb.AppendLine($"<h3>{H(s("d2.top_overshared"))}</h3>");
                sb.AppendLine("<table class='data-table'>");
                sb.AppendLine($"<thead><tr><th>{H(s("d1.site"))}</th><th style='text-align:center'>{H(s("d2.overshared_files"))}</th><th style='text-align:center'>{H(s("d2.risk"))}</th></tr></thead>");
                sb.AppendLine("<tbody>");
                foreach (var sp in sites.Take(10))
                {
                    var riskBadge = RiskBadge(sp.RiskLevel);
                    sb.AppendLine($"<tr><td>{H(sp.SiteTitle ?? sp.SiteUrl)}</td><td style='text-align:center'>{sp.OversharedFiles:N0}</td><td style='text-align:center'>{riskBadge}</td></tr>");
                }
                sb.AppendLine("</tbody></table>");
            }
            else
            {
                sb.AppendLine($"<p class='no-findings'>{H(s("misc.no_findings"))}</p>");
            }
        }

        sb.AppendLine("</div></div>"); // page

        // ══════════════════════════════════════════════════════════════════
        // SECTION 6: D3 — External User Access
        // ══════════════════════════════════════════════════════════════════
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, s("d3.title"), brand, s("d3.eyebrow"));
        sb.AppendLine("<div class='pb'>");

        if (copilotScan == null)
        {
            AppendNoScanBanner(sb, s("verdict.no_scan"));
        }
        else
        {
            var extUsers = copilotScan.ExternalUsers?.ToList() ?? [];
            var highRisk = extUsers.Where(u => u.RiskLevel?.Equals("High", StringComparison.OrdinalIgnoreCase) == true).ToList();
            var pending = extUsers.Where(u => u.LastSignIn == null).ToList();
            var inactive = extUsers.Where(u => u.LastSignIn.HasValue && u.LastSignIn.Value < DateTime.UtcNow.AddDays(-90)).ToList();

            // 4 KPI boxes
            sb.AppendLine("<div class='summary-grid'>");
            AppendKpiBox(sb, s("d3.total_external"), extUsers.Count.ToString(), "#6366F1");
            AppendKpiBox(sb, s("d3.high_risk"), highRisk.Count.ToString(), "#C0392B");
            AppendKpiBox(sb, s("d3.pending"), pending.Count.ToString(), "#D97706");
            AppendKpiBox(sb, es ? "Inactivos" : "Inactive", inactive.Count.ToString(), "#9CA3AF");
            sb.AppendLine("</div>");

            // High-risk external users table
            if (highRisk.Count > 0)
            {
                sb.AppendLine($"<h3>{H(s("d3.user_table"))}</h3>");
                sb.AppendLine("<table class='data-table'>");
                sb.AppendLine($"<thead><tr><th>{H(s("d3.user"))}</th><th>{H(s("d3.domain"))}</th><th>{H(s("d3.last_signin"))}</th></tr></thead>");
                sb.AppendLine("<tbody>");
                foreach (var u in highRisk.Take(20))
                {
                    var lastSign = u.LastSignIn?.ToString("yyyy-MM-dd") ?? "—";
                    sb.AppendLine($"<tr><td>{H(u.DisplayName ?? u.UserPrincipal)}</td><td>{H(u.EmailDomain ?? "—")}</td><td>{lastSign}</td></tr>");
                }
                sb.AppendLine("</tbody></table>");
            }
            else
            {
                sb.AppendLine($"<p class='no-findings'>{H(s("misc.no_findings"))}</p>");
            }
        }

        sb.AppendLine("</div></div>"); // page

        // ══════════════════════════════════════════════════════════════════
        // SECTION 7: D4 — Conditional Access
        // ══════════════════════════════════════════════════════════════════
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, s("d4.title"), brand, s("d4.eyebrow"));
        sb.AppendLine("<div class='pb'>");

        if (copilotScan == null)
        {
            AppendNoScanBanner(sb, s("verdict.no_scan"));
        }
        else
        {
            var caFindings = copilotScan.Findings?
                .Where(f => f.Service.Equals("entra", StringComparison.OrdinalIgnoreCase)
                    && (f.Feature.Contains("Conditional Access", StringComparison.OrdinalIgnoreCase)
                        || f.Feature.Contains("CA", StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(f => PriorityOrder(f.Priority))
                .ToList() ?? [];

            if (caFindings.Count > 0)
            {
                sb.AppendLine("<table class='data-table'>");
                sb.AppendLine($"<thead><tr><th>{H(s("d4.feature"))}</th><th>{H(s("m365sec.status"))}</th><th>{H(s("d4.observation"))}</th></tr></thead>");
                sb.AppendLine("<tbody>");
                foreach (var f in caFindings)
                {
                    sb.AppendLine($"<tr><td>{H(f.Feature)}</td><td>{StatusBadge(f.Status, s)}</td><td>{H(f.Observation ?? "—")}</td></tr>");
                }
                sb.AppendLine("</tbody></table>");
            }
            else
            {
                sb.AppendLine($"<p class='no-findings'>{H(s("misc.no_findings"))}</p>");
            }
        }

        sb.AppendLine("</div></div>"); // page

        // ══════════════════════════════════════════════════════════════════
        // SECTION 8: D5+D6 — Zero Trust & Compliance
        // ══════════════════════════════════════════════════════════════════
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, s("d5d6.title"), brand, s("d5d6.eyebrow"));
        sb.AppendLine("<div class='pb'>");

        if (copilotScan == null)
        {
            AppendNoScanBanner(sb, s("verdict.no_scan"));
        }
        else
        {
            var findings = copilotScan.Findings?.ToList() ?? [];
            // D5: Entra + Defender (excluding CA findings already shown)
            var entraFindings = findings
                .Where(f => f.Service.Equals("entra", StringComparison.OrdinalIgnoreCase)
                    && !f.Feature.Contains("Conditional Access", StringComparison.OrdinalIgnoreCase)
                    && !f.Feature.Contains("CA", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => PriorityOrder(f.Priority))
                .ToList();
            var defenderFindings = findings
                .Where(f => f.Service.Equals("defender", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => PriorityOrder(f.Priority))
                .ToList();
            // D6: Purview
            var purviewFindings = findings
                .Where(f => f.Service.Equals("purview", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => PriorityOrder(f.Priority))
                .ToList();

            sb.AppendLine("<div class='split-cols'>");

            // Left: D5
            sb.AppendLine("<div class='split-col'>");
            sb.AppendLine($"<h3>{H(s("dim.d5"))}</h3>");

            if (entraFindings.Count > 0)
            {
                sb.AppendLine($"<h4>{H(s("d5d6.entra_findings"))}</h4>");
                AppendFindingsTable(sb, entraFindings, s);
            }
            if (defenderFindings.Count > 0)
            {
                sb.AppendLine($"<h4>{H(s("d5d6.defender_findings"))}</h4>");
                AppendFindingsTable(sb, defenderFindings, s);
            }
            if (entraFindings.Count == 0 && defenderFindings.Count == 0)
                sb.AppendLine($"<p class='no-findings'>{H(s("misc.no_findings"))}</p>");

            sb.AppendLine("</div>");

            // Right: D6
            sb.AppendLine("<div class='split-col'>");
            sb.AppendLine($"<h3>{H(s("dim.d6"))}</h3>");
            if (purviewFindings.Count > 0)
            {
                sb.AppendLine($"<h4>{H(s("d5d6.purview_findings"))}</h4>");
                AppendFindingsTable(sb, purviewFindings, s);
            }
            else
            {
                sb.AppendLine($"<p class='no-findings'>{H(s("misc.no_findings"))}</p>");
            }
            sb.AppendLine("</div>");

            sb.AppendLine("</div>"); // split-cols
        }

        sb.AppendLine("</div></div>"); // page

        // ══════════════════════════════════════════════════════════════════
        // SECTION 9: LICENSE INVENTORY
        // ══════════════════════════════════════════════════════════════════
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, s("lic.title"), brand, s("lic.eyebrow"));
        sb.AppendLine("<div class='pb'>");

        if (copilotScan == null)
        {
            AppendNoScanBanner(sb, s("verdict.no_scan"));
        }
        else
        {
            var allFindings = copilotScan.Findings?
                .OrderBy(f => StatusOrder(f.Status))
                .ThenBy(f => f.Service)
                .ThenBy(f => f.Feature)
                .ToList() ?? [];

            if (allFindings.Count > 0)
            {
                // Group by service
                var byService = allFindings.GroupBy(f => f.Service).OrderBy(g => g.Key);
                sb.AppendLine("<table class='data-table'>");
                sb.AppendLine($"<thead><tr><th>{H(s("lic.service"))}</th><th>{H(s("lic.feature"))}</th><th>{H(s("lic.status"))}</th></tr></thead>");
                sb.AppendLine("<tbody>");
                foreach (var group in byService)
                {
                    var first = true;
                    foreach (var f in group)
                    {
                        var svc = first ? H(group.Key) : "";
                        first = false;
                        sb.AppendLine($"<tr><td><strong>{svc}</strong></td><td>{H(f.Feature)}</td><td>{StatusBadge(f.Status, s)}</td></tr>");
                    }
                }
                sb.AppendLine("</tbody></table>");
            }
            else
            {
                sb.AppendLine($"<p class='no-findings'>{H(s("misc.no_findings"))}</p>");
            }
        }

        sb.AppendLine("</div></div>"); // page

        // ══════════════════════════════════════════════════════════════════
        // SECTION 10: REMEDIATION ROADMAP
        // ══════════════════════════════════════════════════════════════════
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, s("roadmap.title"), brand, s("roadmap.eyebrow"));
        sb.AppendLine("<div class='pb'>");

        if (copilotScan == null)
        {
            AppendNoScanBanner(sb, s("verdict.no_scan"));
        }
        else
        {
            var actionable = copilotScan.Findings?
                .Where(f => !string.IsNullOrEmpty(f.Recommendation))
                .ToList() ?? [];

            var phase1 = actionable.Where(f => f.Priority.Equals("High", StringComparison.OrdinalIgnoreCase) || f.Priority.Equals("Critical", StringComparison.OrdinalIgnoreCase)).ToList();
            var phase2 = actionable.Where(f => f.Priority.Equals("Medium", StringComparison.OrdinalIgnoreCase)).ToList();
            var phase3 = actionable.Where(f => f.Priority.Equals("Low", StringComparison.OrdinalIgnoreCase)).ToList();

            AppendRoadmapPhase(sb, s("roadmap.phase1"), phase1, s, "#C0392B");
            AppendRoadmapPhase(sb, s("roadmap.phase2"), phase2, s, "#D97706");
            AppendRoadmapPhase(sb, s("roadmap.phase3"), phase3, s, "#2563EB");
        }

        sb.AppendLine("</div></div>"); // page

        // ══════════════════════════════════════════════════════════════════
        // SECTION 11: METHODOLOGY
        // ══════════════════════════════════════════════════════════════════
        sb.AppendLine("<div class='page'>");
        AppendPageHeader(sb, s("method.title"), brand, s("method.eyebrow"));
        sb.AppendLine("<div class='pb'>");

        // Scoring formula
        sb.AppendLine($"<h3>{H(s("method.scoring"))}</h3>");
        sb.AppendLine($"<p>{H(s("method.scoring_desc"))}</p>");

        // Dimension weights table
        sb.AppendLine($"<h3>{H(s("method.weights"))}</h3>");
        sb.AppendLine("<table class='data-table'>");
        sb.AppendLine($"<thead><tr><th>Dimension</th><th style='text-align:center'>Weight</th><th style='text-align:center'>Score Range</th></tr></thead>");
        sb.AppendLine("<tbody>");
        sb.AppendLine($"<tr><td>{H(s("dim.d1"))}</td><td style='text-align:center'>25%</td><td style='text-align:center'>0 - 5</td></tr>");
        sb.AppendLine($"<tr><td>{H(s("dim.d2"))}</td><td style='text-align:center'>25%</td><td style='text-align:center'>0 - 5</td></tr>");
        sb.AppendLine($"<tr><td>{H(s("dim.d3"))}</td><td style='text-align:center'>20%</td><td style='text-align:center'>0 - 5</td></tr>");
        sb.AppendLine($"<tr><td>{H(s("dim.d4"))}</td><td style='text-align:center'>15%</td><td style='text-align:center'>0 - 5</td></tr>");
        sb.AppendLine($"<tr><td>{H(s("dim.d5"))}</td><td style='text-align:center'>10%</td><td style='text-align:center'>0 - 5</td></tr>");
        sb.AppendLine($"<tr><td>{H(s("dim.d6"))}</td><td style='text-align:center'>5%</td><td style='text-align:center'>0 - 5</td></tr>");
        sb.AppendLine("</tbody></table>");

        // Thresholds
        sb.AppendLine($"<h3>{H(s("method.thresholds"))}</h3>");
        sb.AppendLine("<ul>");
        sb.AppendLine($"<li style='color:#15803D'>{H(s("method.threshold_ready"))}</li>");
        sb.AppendLine($"<li style='color:#D97706'>{H(s("method.threshold_nearly"))}</li>");
        sb.AppendLine($"<li style='color:#C0392B'>{H(s("method.threshold_not"))}</li>");
        sb.AppendLine("</ul>");

        // Data sources
        sb.AppendLine($"<h3>{H(s("method.data_sources"))}</h3>");
        sb.AppendLine("<ul>");
        sb.AppendLine($"<li>{H(s("method.ds_graph"))}</li>");
        sb.AppendLine($"<li>{H(s("method.ds_defender"))}</li>");
        sb.AppendLine($"<li>{H(s("method.ds_purview"))}</li>");
        sb.AppendLine($"<li>{H(s("method.ds_sharepoint"))}</li>");
        sb.AppendLine($"<li>{H(s("method.ds_entra"))}</li>");
        sb.AppendLine("</ul>");

        // Scan timestamp
        if (copilotScan != null)
        {
            sb.AppendLine($"<p class='scan-meta'>{H(s("cover.date_label"))}: {copilotScan.StartedAt:yyyy-MM-dd HH:mm} UTC");
            if (!string.IsNullOrEmpty(copilotScan.PipelineStatus))
                sb.AppendLine($" &mdash; Pipeline: {H(copilotScan.PipelineStatus)}");
            sb.AppendLine("</p>");
        }

        // Footer
        sb.AppendLine("</div>");
        AppendFooter(sb, brand, $"{totalMachines} {s("cover.devices_label")}", userInfo);
        sb.AppendLine("</div>"); // page

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    // ──────────────────────────────────────────────────────────────────────
    // PRIVATE HELPERS
    // ──────────────────────────────────────────────────────────────────────

    private static string H(string? value) =>
        System.Net.WebUtility.HtmlEncode(value ?? "");

    private static void AppendNoScanBanner(StringBuilder sb, string message)
    {
        sb.AppendLine($"<div class='no-scan-banner'>{H(message)}</div>");
    }

    private static void AppendKpiBox(StringBuilder sb, string label, string value, string color)
    {
        sb.AppendLine($"<div class='stat'>");
        sb.AppendLine($"<span class='stat-value' style='color:{color}'>{H(value)}</span>");
        sb.AppendLine($"<span class='stat-label'>{H(label)}</span>");
        sb.AppendLine("</div>");
    }

    private static void AppendDimensionBar(StringBuilder sb, string label, decimal? score, ReportBranding brand)
    {
        var val = score ?? 0m;
        var pct = Math.Min((double)val / 5.0 * 100, 100);
        var color = val >= 4m ? "#15803D" : val >= 3m ? "#D97706" : "#C0392B";
        sb.AppendLine("<div class='dim-bar-row'>");
        sb.AppendLine($"<span class='dim-label'>{H(label)}</span>");
        sb.AppendLine($"<div class='dim-track'><div class='dim-fill' style='width:{pct:F0}%;background:{color}'></div></div>");
        sb.AppendLine($"<span class='dim-score'>{val:F1}</span>");
        sb.AppendLine("</div>");
    }

    private static void AppendFindingsTable(StringBuilder sb, List<CopilotReadinessFinding> findings, Func<string, string> s)
    {
        sb.AppendLine("<table class='data-table compact'>");
        sb.AppendLine($"<thead><tr><th>{H(s("d4.feature"))}</th><th>{H(s("m365sec.status"))}</th><th>{H(s("d5d6.priority"))}</th><th>{H(s("d4.observation"))}</th></tr></thead>");
        sb.AppendLine("<tbody>");
        foreach (var f in findings.Take(15))
        {
            sb.AppendLine($"<tr><td>{H(f.Feature)}</td><td>{StatusBadge(f.Status, s)}</td><td>{PriorityBadge(f.Priority, s)}</td><td style='font-size:10px'>{H(f.Observation ?? "—")}</td></tr>");
        }
        sb.AppendLine("</tbody></table>");
    }

    private static void AppendRoadmapPhase(StringBuilder sb, string phaseTitle, List<CopilotReadinessFinding> items, Func<string, string> s, string color)
    {
        sb.AppendLine($"<h3 style='border-color:{color}'>{H(phaseTitle)}</h3>");
        if (items.Count == 0)
        {
            sb.AppendLine($"<p class='no-findings'>{H(s("roadmap.no_actions"))}</p>");
            return;
        }
        sb.AppendLine("<ol class='roadmap-list'>");
        foreach (var item in items)
        {
            sb.AppendLine($"<li><strong>{H(item.Feature)}</strong>: {H(item.Recommendation ?? "—")}</li>");
        }
        sb.AppendLine("</ol>");
    }

    private static string StatusBadge(string status, Func<string, string> s)
    {
        var (color, bg, key) = status.ToLowerInvariant() switch
        {
            "pass" or "success" => ("#15803D", "#F0FDF4", "status.pass"),
            "fail" or "critical" => ("#C0392B", "#FEF2F2", "status.fail"),
            "warn" or "warning" => ("#D97706", "#FFFBEB", "status.warn"),
            "action required" => ("#C0392B", "#FEF2F2", "status.action_required"),
            "disabled" => ("#6B7280", "#F3F4F6", "status.disabled"),
            "info" => ("#2563EB", "#EFF6FF", "status.info"),
            _ => ("#6B7280", "#F3F4F6", "status.na")
        };
        return $"<span class='badge' style='background:{bg};color:{color}'>{H(s(key))}</span>";
    }

    private static string SeverityBadge(string severity, Func<string, string> s)
    {
        var (color, bg, key) = severity.ToLowerInvariant() switch
        {
            "critical" => ("#C0392B", "#FEF2F2", "sev.critical"),
            "high" => ("#D97706", "#FFFBEB", "sev.high"),
            "medium" => ("#2563EB", "#EFF6FF", "sev.medium"),
            _ => ("#6B7280", "#F3F4F6", "sev.low")
        };
        return $"<span class='badge' style='background:{bg};color:{color}'>{H(s(key))}</span>";
    }

    private static string PriorityBadge(string priority, Func<string, string> s)
    {
        var (color, bg, key) = priority.ToLowerInvariant() switch
        {
            "high" or "critical" => ("#C0392B", "#FEF2F2", "sev.high"),
            "medium" => ("#D97706", "#FFFBEB", "sev.medium"),
            _ => ("#2563EB", "#EFF6FF", "sev.low")
        };
        return $"<span class='badge' style='background:{bg};color:{color}'>{H(s(key))}</span>";
    }

    private static string RiskBadge(string? risk)
    {
        var (color, bg) = (risk ?? "").ToLowerInvariant() switch
        {
            "high" => ("#C0392B", "#FEF2F2"),
            "medium" => ("#D97706", "#FFFBEB"),
            "low" => ("#15803D", "#F0FDF4"),
            _ => ("#6B7280", "#F3F4F6")
        };
        return $"<span class='badge' style='background:{bg};color:{color}'>{H(risk ?? "—")}</span>";
    }

    private static int PriorityOrder(string priority) => priority.ToLowerInvariant() switch
    {
        "critical" => 4,
        "high" => 3,
        "medium" => 2,
        "low" => 1,
        _ => 0
    };

    private static int StatusOrder(string status) => status.ToLowerInvariant() switch
    {
        "critical" => 0,
        "fail" => 1,
        "action required" => 2,
        "warn" or "warning" => 3,
        "disabled" => 4,
        "pass" or "success" => 5,
        _ => 6
    };

    // ──────────────────────────────────────────────────────────────────────
    // HTML HEAD + STYLES (self-contained, mirrors ReportService patterns)
    // ──────────────────────────────────────────────────────────────────────

    private static void AppendHtmlHead(StringBuilder sb, string title, ReportBranding brand,
        string htmlLang, ReportUserInfo? user, string? detail)
    {
        sb.AppendLine($"<!DOCTYPE html><html lang='{H(htmlLang)}'><head><meta charset='UTF-8'>");
        sb.AppendLine("<meta name='viewport' content='width=device-width, initial-scale=1.0'>");
        sb.AppendLine($"<title>{H(title)}</title>");
        sb.AppendLine("<link href='https://fonts.googleapis.com/css2?family=Montserrat:wght@300;400;500;600;700;900&display=swap' rel='stylesheet'>");
        sb.AppendLine("<style>");
        sb.AppendLine(GetStyles(brand));
        sb.AppendLine(GetA4PrintCss());
        if (user is not null)
            AppendRunningFooterCss(sb, user, detail);
        sb.AppendLine("</style></head><body>");
    }

    private static void AppendPageHeader(StringBuilder sb, string title, ReportBranding brand, string? eyebrow = null)
    {
        var effectiveEyebrow = eyebrow ?? brand.CompanyName;
        sb.AppendLine("<div class='ph'>");
        sb.AppendLine("<div class='ph-text'>");
        sb.AppendLine($"<div class='ph-eyebrow'>{H(effectiveEyebrow.ToUpperInvariant())}</div>");
        sb.AppendLine($"<h1>{H(title)}</h1>");
        sb.AppendLine("</div>");
        if (brand.LogoUrl is not null)
            sb.AppendLine($"<img src='{H(brand.LogoUrl)}' alt=''>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class='stripe'></div>");
    }

    private static void AppendFooter(StringBuilder sb, ReportBranding brand, string detail, ReportUserInfo? user = null)
    {
        sb.AppendLine("<div class='footer'>");
        if (user is not null)
        {
            var fullName = string.IsNullOrWhiteSpace(user.FullName) ? "\u2014" : user.FullName!;
            var email    = string.IsNullOrWhiteSpace(user.Email)    ? "\u2014" : user.Email!;
            var phone    = string.IsNullOrWhiteSpace(user.Phone)    ? "\u2014" : user.Phone!;
            var job      = string.IsNullOrWhiteSpace(user.JobTitle) ? ""  : $" &middot; {H(user.JobTitle)}";
            sb.AppendLine($"<p><strong>Prepared by: {H(fullName)}</strong>{job}</p>");
            sb.AppendLine($"<p>{H(email)} &middot; {H(phone)}</p>");
        }
        sb.AppendLine($"<p>Generated by {H(brand.CompanyName)} &mdash; Your Technology Advisor</p>");
        sb.AppendLine($"<p>{DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC &bull; {detail}</p>");
        sb.AppendLine("</div>");
    }

    private static void AppendRunningFooterCss(StringBuilder sb, ReportUserInfo user, string? detail)
    {
        static string CssEscape(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "\u2014";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "");
        }

        var fullName = CssEscape(user.FullName);
        var email    = CssEscape(user.Email);
        var phone    = CssEscape(user.Phone);
        var jobTitle = string.IsNullOrWhiteSpace(user.JobTitle) ? "" : " \u00B7 " + CssEscape(user.JobTitle);
        var detailStr = CssEscape(detail ?? "");
        var ts       = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm");

        var line1 = $"Prepared by {fullName}{jobTitle}  \u00B7  {email}  \u00B7  {phone}";
        var line2 = $"Generated {ts} UTC  \u00B7  {detailStr}";

        sb.AppendLine(".page { position: relative !important; }");
        sb.AppendLine(".page::after {");
        sb.AppendLine($"    content: \"{line1}\\A {line2}\";");
        sb.AppendLine("    white-space: pre;");
        sb.AppendLine("    position: absolute; left: 0; right: 0; bottom: 0;");
        sb.AppendLine("    padding: 3mm 12mm 4mm;");
        sb.AppendLine("    font-family: 'Montserrat', 'Verdana', sans-serif;");
        sb.AppendLine("    font-size: 7.5pt; line-height: 1.45; color: #666;");
        sb.AppendLine("    background: #fafafa; border-top: 0.5pt solid #e5e7eb; text-align: center; z-index: 10;");
        sb.AppendLine("}");
        sb.AppendLine(".page .pb { padding-bottom: 18mm !important; }");
    }

    private static string GetStyles(ReportBranding brand) => $$"""
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body { font-family: 'Montserrat', 'Verdana', sans-serif; color: #333; font-size: 13px; line-height: 1.6;
               -webkit-print-color-adjust: exact; print-color-adjust: exact; background: #ECEAE4; }

        /* Cover */
        .cover { width: 210mm; min-height: 297mm; margin: 0 auto 20px; background: #3D4043; position: relative;
                 overflow: hidden; display: flex; align-items: flex-end; page-break-after: always; }
        .cover-ribbon { position: absolute; bottom: 0; right: -404px; width: 100%; height: 59%; pointer-events: none; object-fit: cover; object-position: right bottom; }
        .cover-content { padding: 44px; position: relative; z-index: 2; color: #fff; }
        .cover .logo { height: 50px; margin-bottom: 30px; display: block; }
        .eyebrow { font-size: 9px; font-weight: 700; letter-spacing: 0.2em; text-transform: uppercase; color: {{brand.AccentColor}}; margin-bottom: 12px; }
        .cover h1 { font-size: 30px; font-weight: 700; line-height: 1.1; margin-bottom: 8px; }
        .cover h2 { font-size: 20px; font-weight: 400; color: {{brand.AccentColor}}; margin-bottom: 8px; }
        .cover .meta { font-size: 12px; opacity: 0.6; margin-bottom: 20px; }

        /* Score circle on cover */
        .score-circle { display: inline-block; width: 100px; height: 100px; border: 6px solid; border-radius: 50%;
                        font-size: 36px; font-weight: 900; line-height: 88px; text-align: center; margin: 16px 0 8px; }
        .verdict { font-size: 22px; font-weight: 700; margin-bottom: 4px; }
        .verdict-desc { font-size: 13px; opacity: 0.8; max-width: 500px; }

        /* Pages */
        .page { width: 210mm; margin: 0 auto 20px; background: #fff; overflow: hidden; page-break-after: always; }

        /* Page header (dark band + ribbon) */
        .ph {
            background-color: #3D4043;
            background-image: url('{{RibbonData.DataUri}}');
            background-repeat: no-repeat;
            background-position: right center;
            background-size: auto 220%;
            padding: 8mm 12mm 5mm;
            display: flex;
            justify-content: space-between;
            align-items: center;
            position: relative;
            overflow: hidden;
        }
        .ph-text { position: relative; z-index: 2; min-width: 0; }
        .ph-eyebrow {
            font-size: 8pt; font-weight: 700; letter-spacing: 0.12em;
            text-transform: uppercase; color: {{brand.AccentColor}}; margin-bottom: 1mm;
        }
        .ph h1 { font-size: 13pt; font-weight: 700; color: #fff; letter-spacing: 0.01em; line-height: 1.15; }
        .ph img { height: 10mm; position: relative; z-index: 2; }
        .stripe { height: 3mm; background: linear-gradient(90deg, #006536 0%, #2BB673 20%, #39B54A 40%, #8DC63F 60%, #B2D235 80%, #D3E173 100%); }
        .pb { padding: 18px 36px 28px; }

        /* Summary grid */
        .summary-grid { display: flex; gap: 16px; margin: 16px 0 24px; flex-wrap: wrap; }
        .stat { background: #f8f9fa; border: 1px solid #e5e7eb; border-radius: 8px; padding: 16px 20px;
                text-align: center; flex: 1; min-width: 100px; }
        .stat-value { display: block; font-size: 28px; font-weight: 700; }
        .stat-label { display: block; font-size: 10px; color: #666; margin-top: 4px; text-transform: uppercase; letter-spacing: 0.05em; }

        /* Typography */
        h3 { font-size: 14px; font-weight: 700; color: {{brand.PrimaryColor}}; margin: 20px 0 10px;
             border-bottom: 2px solid {{brand.AccentColor}}; padding-bottom: 6px; }
        h4 { font-size: 12px; font-weight: 600; color: #3D4043; margin: 14px 0 6px; }
        p { margin: 6px 0; }
        ul, ol { margin: 8px 0 8px 20px; }
        li { margin-bottom: 4px; }

        /* Dimension scorecard bars */
        .dim-scorecard { margin: 12px 0; }
        .dim-bar-row { display: flex; align-items: center; gap: 10px; margin-bottom: 8px; }
        .dim-label { width: 200px; font-size: 11px; font-weight: 600; text-align: right; }
        .dim-track { flex: 1; height: 20px; background: #f0f0f0; border-radius: 4px; overflow: hidden; }
        .dim-fill { height: 100%; border-radius: 4px; min-width: 2px; transition: width 0.3s; }
        .dim-score { width: 36px; font-weight: 700; font-size: 13px; }

        /* Percentage bar */
        .pct-bar-track { height: 24px; background: #f0f0f0; border-radius: 6px; overflow: hidden; margin: 8px 0 16px; }
        .pct-bar-fill { height: 100%; border-radius: 6px; min-width: 2px; transition: width 0.3s; }

        /* Data tables */
        .data-table { width: 100%; border-collapse: collapse; font-size: 11px; margin: 12px 0; }
        .data-table th { background: #f8f9fa; font-weight: 700; text-align: left; padding: 8px 10px;
                         border-bottom: 2px solid {{brand.AccentColor}}; font-size: 10px; text-transform: uppercase; letter-spacing: 0.03em; }
        .data-table td { padding: 6px 10px; border-bottom: 1px solid #f0f0f0; vertical-align: top; }
        .data-table tbody tr:nth-child(even) { background: #fafafa; }
        .data-table.compact { font-size: 10px; }
        .data-table.compact th, .data-table.compact td { padding: 4px 8px; }
        .data-table code { font-size: 10px; background: #f3f4f6; padding: 1px 4px; border-radius: 3px; }

        /* Badges */
        .badge { display: inline-block; padding: 2px 8px; border-radius: 4px; font-size: 9px;
                 font-weight: 700; text-transform: uppercase; letter-spacing: 0.03em; white-space: nowrap; }

        /* Split columns (D5+D6) */
        .split-cols { display: flex; gap: 24px; }
        .split-col { flex: 1; min-width: 0; }

        /* License banner */
        .license-banner { padding: 12px 16px; border-radius: 6px; margin: 16px 0; font-size: 12px; }

        /* Action count */
        .action-count { font-size: 13px; font-weight: 600; color: #C0392B; margin: 8px 0; }

        /* No-scan banner */
        .no-scan-banner { background: #FFFBEB; border: 1px solid #D97706; border-radius: 8px; padding: 20px;
                          text-align: center; color: #92400E; font-size: 13px; margin: 24px 0; }

        /* No findings message */
        .no-findings { color: #9CA3AF; font-style: italic; margin: 12px 0; }

        /* Roadmap list */
        .roadmap-list { margin: 8px 0 16px 20px; }
        .roadmap-list li { margin-bottom: 6px; font-size: 12px; line-height: 1.5; }

        /* Scan metadata */
        .scan-meta { font-size: 11px; color: #9CA3AF; margin-top: 16px; }

        /* Footer */
        .footer { text-align: center; padding: 20px; color: #999; font-size: 11px; border-top: 1px solid #eee; margin-top: 20px; }

        @media print { .page { margin: 0; box-shadow: none; } body { background: #fff; } }
        """;

    private static string GetA4PrintCss() => """
        @page { size: A4 portrait; margin: 0; }
        html, body {
            margin: 0 !important; padding: 0 !important;
            -webkit-print-color-adjust: exact !important; print-color-adjust: exact !important;
        }
        .cover, .page {
            width: 210mm !important; height: 296mm !important;
            min-height: 296mm !important; max-height: 296mm !important;
            box-sizing: border-box !important; overflow: hidden !important;
        }
        .cover { margin: 0 auto 24px auto !important; }
        .page  { margin: 0 auto 24px auto !important; }

        @media screen {
            html { background: #F8F9FA; }
            body { zoom: 0.78; padding-top: 28px; padding-bottom: 28px; }
            .cover, .page {
                box-shadow: 0 10px 30px -4px rgba(15, 23, 42, 0.18),
                            0 4px 8px -2px rgba(15, 23, 42, 0.08);
                border-radius: 2mm;
            }
        }

        @media print {
            html, body { background: #fff !important; }
            body { font-size: 10pt; margin: 0 !important; padding: 0 !important; }
            .cover, .page {
                margin: 0 !important; padding: 0 !important;
                box-shadow: none !important;
                page-break-after: always; break-after: page;
                page-break-inside: avoid; break-inside: avoid;
            }
            .cover:last-of-type, .page:last-of-type {
                page-break-after: auto; break-after: auto;
            }
            .no-print { display: none !important; }
        }

        .ph {
            width: 100% !important; margin: 0 !important; box-sizing: border-box !important;
        }
        """;
}

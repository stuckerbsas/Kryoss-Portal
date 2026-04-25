namespace KryossApi.Services.Reports;

public static class ReportStyles
{
    public static string GetOrgReportStyles(ReportBranding brand) => $$"""
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
        .cover h1 { font-size: 34px; font-weight: 700; line-height: 1.1; margin-bottom: 8px; }
        .cover h2 { font-size: 20px; font-weight: 400; color: {{brand.AccentColor}}; margin-bottom: 8px; }
        .cover .meta { font-size: 12px; opacity: 0.6; margin-bottom: 20px; }
        .cover .score { font-size: 36px; font-weight: 900; margin-top: 8px; }

        /* Grade badge */
        .grade-badge { display: inline-block; font-size: 48px; font-weight: 900; padding: 12px 28px;
                       border-radius: 10px; margin: 16px 0 4px; color: #fff; }
        .grade-Aplus, .grade-A, .grade-A- { background: {{brand.PrimaryColor}}; }
        .grade-Bplus, .grade-B, .grade-B- { background: #2563EB; }
        .grade-Cplus, .grade-C, .grade-C- { background: #D97706; }
        .grade-Dplus, .grade-D, .grade-D- { background: #EA580C; }
        .grade-F { background: #C0392B; }

        /* Pages */
        .page { width: 210mm; min-height: 297mm; margin: 0 auto 20px; background: #fff; page-break-after: always; position: relative; }

        /* Unified page header — dark band + ribbon gradient layered behind
           the logo (echoes the cover art), eyebrow + page title on the left,
           white logo on the right. Used by every page of every report type. */
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
            font-size: 8pt;
            font-weight: 700;
            letter-spacing: 0.12em;
            text-transform: uppercase;
            color: {{brand.AccentColor}};
            margin-bottom: 1mm;
        }
        .ph h1 {
            font-size: 13pt;
            font-weight: 700;
            color: #fff;
            letter-spacing: 0.01em;
            line-height: 1.15;
        }
        .ph img {
            height: 10mm;
            position: relative;
            z-index: 2;
        }
        .stripe { height: 3mm; background: linear-gradient(90deg, #006536 0%, #2BB673 20%, #39B54A 40%, #8DC63F 60%, #B2D235 80%, #D3E173 100%); }
        .pb { padding: 18px 36px 28px; }

        /* Summary grid */
        .summary-grid { display: flex; gap: 16px; margin: 16px 0 24px; flex-wrap: wrap; }
        .stat { background: #f8f9fa; border: 1px solid #e5e7eb; border-radius: 8px; padding: 16px 20px;
                text-align: center; flex: 1; min-width: 120px; }
        .stat.pass-stat { background: #f0fdf4; border-color: {{brand.PrimaryColor}}33; }
        .stat.warn-stat { background: #fffbeb; border-color: #D9770633; }
        .stat.fail-stat { background: #fef2f2; border-color: #C0392B33; }
        .stat-value { display: block; font-size: 28px; font-weight: 700; color: #1a1a1a; }
        .stat-label { display: block; font-size: 11px; color: #666; margin-top: 4px; text-transform: uppercase; letter-spacing: 0.05em; }

        /* Typography */
        h3 { font-size: 14px; font-weight: 700; color: {{brand.PrimaryColor}}; margin: 20px 0 10px;
             border-bottom: 2px solid {{brand.AccentColor}}; padding-bottom: 6px; }
        h4 { font-size: 12px; font-weight: 600; color: #3D4043; margin: 8px 0 4px; }
        .cat-summary { font-size: 12px; color: #666; margin-bottom: 12px; }

        /* Category header with bar */
        .cat-header { margin-bottom: 12px; }
        .cat-bar { height: 8px; border-radius: 4px; display: flex; overflow: hidden; margin-top: 6px; background: #f0f0f0; }
        .cat-bar-pass { background: {{brand.PrimaryColor}}; }
        .cat-bar-warn { background: #D97706; }
        .cat-bar-fail { background: #C0392B; }

        /* Framework compliance bars */
        .framework-bars { margin: 12px 0; }
        .fw-bar-row { display: flex; align-items: center; gap: 10px; margin-bottom: 8px; }
        .fw-label { width: 60px; font-weight: 700; font-size: 11px; text-align: right; text-transform: uppercase; }
        .fw-track { flex: 1; height: 20px; background: #f0f0f0; border-radius: 4px; overflow: hidden; }
        .fw-fill { height: 100%; border-radius: 4px; min-width: 2px; transition: width 0.3s; }
        .fw-pct { width: 40px; font-weight: 700; font-size: 12px; }
        .fw-detail { font-size: 10px; color: #888; width: 70px; }

        /* Grade distribution */
        .grade-dist { margin: 12px 0; }
        .grade-bar { display: flex; align-items: center; gap: 10px; margin-bottom: 6px; }
        .grade-label { width: 30px; font-weight: 700; font-size: 13px; text-align: right; }
        .bar-track { flex: 1; height: 22px; background: #f0f0f0; border-radius: 4px; overflow: hidden; }
        .bar-fill { height: 100%; border-radius: 4px; min-width: 2px; }
        .grade-count { width: 30px; font-weight: 600; font-size: 13px; }

        /* Tables */
        .results-table, .fleet-table { width: 100%; border-collapse: collapse; margin: 10px 0; font-size: 12px; }
        .results-table th, .fleet-table th { background: #3D4043; color: #fff; padding: 8px 10px;
                                              text-align: left; font-weight: 600; font-size: 11px; }
        .results-table td, .fleet-table td { padding: 6px 10px; border-bottom: 1px solid #eee; }
        .results-table tr:nth-child(even), .fleet-table tr:nth-child(even) { background: #fafafa; }
        .results-table tr.pass, .fleet-table tr.pass { background: #f0fdf4; }
        .results-table tr.fail, .fleet-table tr.fail { background: #fef2f2; }
        .results-table tr.warn, .fleet-table tr.warn { background: #fffbeb; }
        .hostname { font-weight: 600; font-family: monospace; }
        .num { text-align: center; font-weight: 600; }
        .pass-cell { color: {{brand.PrimaryColor}}; }
        .warn-cell { color: #D97706; }
        .fail-cell { color: #C0392B; }

        /* Mini grades in fleet table */
        .grade-mini { padding: 2px 8px; border-radius: 4px; font-weight: 700; font-size: 11px; color: #fff; }

        /* Severity + status badges */
        .severity { padding: 2px 6px; border-radius: 3px; font-size: 10px; font-weight: 600; }
        .severity.critical { background: #7f1d1d; color: #fff; }
        .severity.high { background: #C0392B; color: #fff; }
        .severity.medium { background: #D97706; color: #fff; }
        .severity.low { background: #2563EB; color: #fff; }

        .status-badge { padding: 2px 8px; border-radius: 4px; font-weight: 600; font-size: 11px; text-transform: uppercase; }
        .status-badge.pass { background: {{brand.PrimaryColor}}; color: #fff; }
        .status-badge.warn { background: #D97706; color: #fff; }
        .status-badge.fail { background: #C0392B; color: #fff; }

        .remediation-row td { background: #f9fafb; font-size: 11px; color: #555; border-left: 3px solid {{brand.PrimaryColor}}; }

        /* Risk cards */
        .risk-cards { margin: 12px 0; }
        .risk-card { display: flex; gap: 12px; padding: 12px; border: 1px solid #fecaca; border-radius: 8px;
                     background: #fef2f2; margin-bottom: 8px; align-items: flex-start; }
        .risk-num { background: #C0392B; color: #fff; width: 28px; height: 28px; border-radius: 50%;
                    display: flex; align-items: center; justify-content: center; font-weight: 700;
                    font-size: 13px; flex-shrink: 0; }
        .risk-body { flex: 1; }
        .risk-body strong { display: block; margin-bottom: 4px; font-size: 13px; }
        .risk-detail { font-size: 11px; color: #666; margin-top: 4px; }

        /* Insight box */
        .insight-box { background: #f0f4ff; border: 1px solid #c7d2fe; border-radius: 8px; padding: 16px 20px;
                       margin: 16px 0; font-size: 13px; line-height: 1.7; }
        .insight-box p { margin: 0; }
        .insight-box.fail-box { background: #fef2f2; border-color: #fecaca; }

        /* Big number box (presales) */
        .big-number-box { text-align: center; padding: 32px 20px; margin: 16px 0; background: #fef2f2;
                          border: 2px solid #C0392B; border-radius: 12px; }
        .big-number { font-size: 72px; font-weight: 900; color: #C0392B; line-height: 1; }
        .big-number-label { font-size: 16px; font-weight: 600; color: #666; margin-top: 8px; }

        /* Headline findings (presales) */
        .headline-findings { margin: 20px 0; }
        .headline-item { display: flex; gap: 14px; padding: 14px 18px; border-radius: 8px; margin-bottom: 10px;
                         align-items: flex-start; background: #fef2f2; border: 1px solid #fecaca; }
        .headline-icon { background: #C0392B; color: #fff; width: 28px; height: 28px; border-radius: 50%;
                         display: flex; align-items: center; justify-content: center; font-weight: 900;
                         font-size: 16px; flex-shrink: 0; }
        .headline-text { flex: 1; font-size: 13px; line-height: 1.6; }
        .headline-text strong { color: #7f1d1d; }

        /* Recommendation box */
        .recommendation-box { background: #f0fdf4; border: 1px solid {{brand.PrimaryColor}}44; border-radius: 8px;
                              padding: 20px 24px; margin: 16px 0; }
        .recommendation-box h3 { border: none; margin-top: 0; padding-bottom: 8px; }
        .recommendation-box p { font-size: 13px; line-height: 1.7; }

        /* Next steps */
        .next-steps { margin: 16px 0; }
        .step { display: flex; gap: 14px; margin-bottom: 14px; align-items: flex-start; }
        .step-num { background: {{brand.PrimaryColor}}; color: #fff; width: 32px; height: 32px; border-radius: 50%;
                    display: flex; align-items: center; justify-content: center; font-weight: 700;
                    font-size: 14px; flex-shrink: 0; }
        .step strong { display: block; font-size: 13px; margin-bottom: 2px; }
        .step p { font-size: 12px; color: #666; margin: 0; }

        /* Phase lists */
        .phase-list { margin: 6px 0 0 16px; font-size: 12px; color: #555; }
        .phase-list li { margin-bottom: 4px; }

        /* CTA box */
        .cta-box { background: #3D4043; color: #fff; border-radius: 8px; padding: 20px 24px; margin-top: 24px;
                   text-align: center; }
        .cta-box p { margin: 0; font-size: 14px; }

        /* Hardware grid */
        .hw-grid { display: flex; gap: 16px; margin: 12px 0; flex-wrap: wrap; }
        .hw-col { flex: 1; min-width: 200px; }

        /* Info table */
        .info-table { width: 100%; border-collapse: collapse; margin: 4px 0; }
        .info-table td { padding: 4px 8px; border-bottom: 1px solid #eee; font-size: 12px; }
        .info-table td:first-child { font-weight: 600; width: 120px; color: #555; }

        /* Risk list (legacy) */
        .risk-list { margin: 10px 0 10px 20px; }
        .risk-list li { margin-bottom: 8px; font-size: 13px; }

        /* Footer */
        .footer { text-align: center; padding: 20px; color: #999; font-size: 11px; border-top: 1px solid #eee; margin-top: 20px; }

        /* Page footer (contact + page number) */
        .page-footer {
            position: absolute; bottom: 6mm; left: 10%; right: 8mm;
            display: flex; justify-content: space-between; align-items: flex-end;
            font-size: 7.5pt; color: #94A3B8; line-height: 1.4;
            border-top: 0.5pt solid #E2E8F0; padding-top: 2mm;
        }
        .pf-left { text-align: left; }
        .pf-right { text-align: right; }

        /* Tier grid (ServiceCatalogBlock) */
        .tier-grid { display:grid; grid-template-columns:repeat(3,1fr); gap:14px; margin:16px 0; }
        .tier-card { border:1px solid #E2E8F0; border-radius:6px; padding:18px; background:#FFFFFF; break-inside:avoid; }
        .tier-card.highlight { border-width:2px; border-color:#15803D; box-shadow:0 4px 12px rgba(15,128,61,0.12); }
        .tier-header { margin-bottom:10px; }
        .tier-name { font-size:14px; font-weight:700; color:#0F172A; }
        .tier-price { font-size:22px; font-weight:900; color:#15803D; margin:6px 0; }
        .tier-bullets { list-style:none; padding:0; margin:12px 0; font-size:11px; }
        .tier-bullets li { padding:4px 0 4px 18px; position:relative; }
        .tier-bullets li::before { content:'✓'; position:absolute; left:0; color:#15803D; font-weight:700; }

        /* Decisions matrix (DecisionsMatrixBlock) */
        .decisions-grid { display:grid; grid-template-columns:repeat(3,1fr); gap:14px; margin:12px 0; }
        .decisions-col { border:1px solid #E2E8F0; border-radius:6px; padding:14px; }
        .decisions-col h4 { font-size:11px; font-weight:700; text-transform:uppercase; letter-spacing:0.05em; margin:0 0 10px; padding-bottom:6px; border-bottom:2px solid currentColor; }
        .decisions-col.approved h4 { color:#15803D; }
        .decisions-col.pending h4 { color:#B45309; }
        .decisions-col.recommended h4 { color:#2563EB; }
        .decision-item { padding:8px 0; border-bottom:1px solid #F1F5F9; }
        .decision-title { font-size:11px; font-weight:600; color:#1E293B; }
        .decision-ask { font-size:9px; color:#64748B; margin-top:2px; }
        .decision-cost { font-size:10px; font-weight:700; color:#15803D; margin-top:2px; }

        /* Control detail cards (ControlDetailBlock) */
        .control-card { border:1px solid #E2E8F0; border-radius:6px; padding:12px 14px; margin-bottom:8px; border-left:4px solid #94A3B8; }
        .control-card.critical { border-left-color:#991B1B; background:#FEF2F2; }
        .control-card.high { border-left-color:#C0392B; background:#FFF5F5; }
        .control-card.medium { border-left-color:#B45309; background:#FFFBEB; }
        .control-card.low { border-left-color:#2563EB; background:#EFF6FF; }
        .control-head { display:flex; align-items:center; gap:8px; margin-bottom:6px; }
        .control-code { font-family:monospace; font-size:10px; font-weight:700; color:#64748B; }
        .control-title { font-size:11px; font-weight:600; color:#1E293B; flex:1; }
        .control-req { font-size:9px; color:#475569; line-height:1.5; margin-bottom:6px; }
        .control-meta { font-size:8px; color:#94A3B8; display:flex; justify-content:space-between; }

        /* Evidence appendix (EvidenceAppendixBlock) */
        .evidence-item { border-bottom:1px solid #E2E8F0; padding:8px 0; }
        .evidence-head { font-size:11px; font-weight:600; color:#1E293B; }
        .evidence-meta { font-size:9px; color:#64748B; margin-top:2px; }
        .evidence-code { font-family:monospace; font-size:9px; color:#991B1B; margin-top:2px; }

        /* Top 3 risk cards (Top3RiskBlock) */
        .top-risk-grid { display:grid; grid-template-columns:repeat(3,1fr); gap:14px; margin:12px 0; }
        .top-risk-card { background:#FEF2F2; border:1px solid #FECACA; border-radius:8px; padding:16px; }
        .top-risk-title { font-size:13px; font-weight:700; color:#7F1D1D; margin-bottom:6px; }
        .top-risk-body { font-size:10px; color:#334155; line-height:1.5; }
        .top-risk-cost { font-size:12px; font-weight:700; color:#991B1B; margin-top:8px; }

        /* Framework cover audit meta (FrameworkCoverBlock) */
        .cover-audit-meta { margin-top:16px; }
        .cover-audit-meta dt { font-size:9px; color:rgba(255,255,255,0.5); text-transform:uppercase; letter-spacing:0.05em; margin-top:8px; }
        .cover-audit-meta dd { font-size:12px; color:#fff; margin:2px 0 0; }

        /* Network mini (NetworkMiniBlock) */
        .network-mini-split { display:grid; grid-template-columns:1fr 1fr; gap:16px; margin:12px 0; }
        .network-mini-map { background:#F1F5F9; border-radius:8px; display:flex; align-items:center; justify-content:center; min-height:180px; color:#94A3B8; font-size:11px; }
        .network-mini-kpis { display:grid; grid-template-columns:1fr 1fr; gap:10px; }

        /* Data table (CategoryBreakdownBlock) */
        .data-table { width:100%; border-collapse:collapse; margin:10px 0; font-size:11px; }
        .data-table th { background:#F8F9FA; color:#1E293B; padding:8px 10px; text-align:left; font-weight:700; font-size:10px; text-transform:uppercase; letter-spacing:0.05em; border-bottom:2px solid #0F172A; }
        .data-table td { padding:6px 10px; border-bottom:1px solid #E2E8F0; }
        .data-table tr:nth-child(even) { background:#F8F9FA; }

        /* Badges (ControlDetailBlock) */
        .badge { padding:2px 6px; border-radius:3px; font-size:9px; font-weight:600; }
        .badge-action { background:#991B1B; color:#fff; }
        .badge-warning { background:#B45309; color:#fff; }
        .badge-muted { background:#E2E8F0; color:#64748B; }

        @media print { .page { margin: 0; box-shadow: none; } body { background: #fff; } }
        """;

    public static string GetReportStyles(ReportBranding brand) => $$"""
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
        .cover h1 { font-size: 34px; font-weight: 700; line-height: 1.1; margin-bottom: 8px; }
        .cover h2 { font-size: 20px; font-weight: 400; color: {{brand.AccentColor}}; margin-bottom: 8px; }
        .cover .meta { font-size: 12px; opacity: 0.6; margin-bottom: 20px; }
        .cover .score { font-size: 36px; font-weight: 900; margin-top: 8px; }

        /* Grade badge */
        .grade-badge { display: inline-block; font-size: 48px; font-weight: 900; padding: 12px 28px;
                       border-radius: 10px; margin: 16px 0 4px; color: #fff; }
        .grade-Aplus, .grade-A, .grade-A- { background: {{brand.PrimaryColor}}; }
        .grade-Bplus, .grade-B, .grade-B- { background: #2563EB; }
        .grade-Cplus, .grade-C, .grade-C- { background: #D97706; }
        .grade-Dplus, .grade-D, .grade-D- { background: #EA580C; }
        .grade-F { background: #C0392B; }

        /* Pages */
        .page { width: 210mm; min-height: 297mm; margin: 0 auto 20px; background: #fff; page-break-after: always; position: relative; }

        /* Unified page header (same as org reports — single source of
           truth for the baseline visual across all report types). */
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
            font-size: 8pt;
            font-weight: 700;
            letter-spacing: 0.12em;
            text-transform: uppercase;
            color: {{brand.AccentColor}};
            margin-bottom: 1mm;
        }
        .ph h1 {
            font-size: 13pt;
            font-weight: 700;
            color: #fff;
            letter-spacing: 0.01em;
            line-height: 1.15;
        }
        .ph img {
            height: 10mm;
            position: relative;
            z-index: 2;
        }
        .stripe { height: 3mm; background: linear-gradient(90deg, #006536 0%, #2BB673 20%, #39B54A 40%, #8DC63F 60%, #B2D235 80%, #D3E173 100%); }
        .pb { padding: 18px 36px 28px; }

        /* Summary grid */
        .summary-grid { display: flex; gap: 16px; margin: 16px 0 24px; flex-wrap: wrap; }
        .stat { background: #f8f9fa; border: 1px solid #e5e7eb; border-radius: 8px; padding: 16px 20px;
                text-align: center; flex: 1; min-width: 120px; }
        .stat.pass-stat { background: #f0fdf4; border-color: {{brand.PrimaryColor}}33; }
        .stat.warn-stat { background: #fffbeb; border-color: #D9770633; }
        .stat.fail-stat { background: #fef2f2; border-color: #C0392B33; }
        .stat-value { display: block; font-size: 28px; font-weight: 700; color: #1a1a1a; }
        .stat-label { display: block; font-size: 11px; color: #666; margin-top: 4px; text-transform: uppercase; letter-spacing: 0.05em; }

        /* Typography */
        h3 { font-size: 14px; font-weight: 700; color: {{brand.PrimaryColor}}; margin: 20px 0 10px;
             border-bottom: 2px solid {{brand.AccentColor}}; padding-bottom: 6px; }
        h4 { font-size: 12px; font-weight: 600; color: #3D4043; margin: 8px 0 4px; }
        .cat-summary { font-size: 12px; color: #666; margin-bottom: 12px; }
        .cat-score { font-weight: 400; font-size: 14px; color: #666; }

        /* Category header with bar */
        .cat-header { margin-bottom: 12px; }
        .cat-bar { height: 8px; border-radius: 4px; display: flex; overflow: hidden; margin-top: 6px; background: #f0f0f0; }
        .cat-bar-pass { background: {{brand.PrimaryColor}}; }
        .cat-bar-warn { background: #D97706; }
        .cat-bar-fail { background: #C0392B; }

        /* Framework compliance bars */
        .framework-bars { margin: 12px 0; }
        .fw-bar-row { display: flex; align-items: center; gap: 10px; margin-bottom: 8px; }
        .fw-label { width: 60px; font-weight: 700; font-size: 11px; text-align: right; text-transform: uppercase; }
        .fw-track { flex: 1; height: 20px; background: #f0f0f0; border-radius: 4px; overflow: hidden; }
        .fw-fill { height: 100%; border-radius: 4px; min-width: 2px; }
        .fw-pct { width: 40px; font-weight: 700; font-size: 12px; }
        .fw-detail { font-size: 10px; color: #888; width: 70px; }

        /* Tables */
        .results-table, .fleet-table { width: 100%; border-collapse: collapse; margin: 10px 0; font-size: 12px; }
        .results-table th, .fleet-table th { background: #3D4043; color: #fff; padding: 8px 10px;
                                              text-align: left; font-weight: 600; font-size: 11px; }
        .results-table td, .fleet-table td { padding: 6px 10px; border-bottom: 1px solid #eee; }
        .results-table tr:nth-child(even), .fleet-table tr:nth-child(even) { background: #fafafa; }
        .results-table tr.pass, .fleet-table tr.pass { background: #f0fdf4; }
        .results-table tr.fail, .fleet-table tr.fail { background: #fef2f2; }
        .results-table tr.warn, .fleet-table tr.warn { background: #fffbeb; }
        .hostname { font-weight: 600; font-family: monospace; }
        .num { text-align: center; font-weight: 600; }
        .pass-cell { color: {{brand.PrimaryColor}}; }
        .warn-cell { color: #D97706; }
        .fail-cell { color: #C0392B; }

        /* Mini grades */
        .grade-mini { padding: 2px 8px; border-radius: 4px; font-weight: 700; font-size: 11px; color: #fff; }

        /* Severity + status badges */
        .severity { padding: 2px 6px; border-radius: 3px; font-size: 10px; font-weight: 600; }
        .severity.critical { background: #7f1d1d; color: #fff; }
        .severity.high { background: #C0392B; color: #fff; }
        .severity.medium { background: #D97706; color: #fff; }
        .severity.low { background: #2563EB; color: #fff; }

        .status-badge { padding: 2px 8px; border-radius: 4px; font-weight: 600; font-size: 11px; text-transform: uppercase; }
        .status-badge.pass { background: {{brand.PrimaryColor}}; color: #fff; }
        .status-badge.warn { background: #D97706; color: #fff; }
        .status-badge.fail { background: #C0392B; color: #fff; }

        .remediation-row td { background: #f9fafb; font-size: 11px; color: #555; border-left: 3px solid {{brand.PrimaryColor}}; }

        /* Risk cards */
        .risk-cards { margin: 12px 0; }
        .risk-card { display: flex; gap: 12px; padding: 12px; border: 1px solid #fecaca; border-radius: 8px;
                     background: #fef2f2; margin-bottom: 8px; align-items: flex-start; }
        .risk-num { background: #C0392B; color: #fff; width: 28px; height: 28px; border-radius: 50%;
                    display: flex; align-items: center; justify-content: center; font-weight: 700;
                    font-size: 13px; flex-shrink: 0; }
        .risk-body { flex: 1; }
        .risk-body strong { display: block; margin-bottom: 4px; font-size: 13px; }
        .risk-detail { font-size: 11px; color: #666; margin-top: 4px; }

        /* Insight box */
        .insight-box { background: #f0f4ff; border: 1px solid #c7d2fe; border-radius: 8px; padding: 16px 20px;
                       margin: 16px 0; font-size: 13px; line-height: 1.7; }
        .insight-box p { margin: 0; }
        .insight-box.fail-box { background: #fef2f2; border-color: #fecaca; }

        /* Big number box (presales) */
        .big-number-box { text-align: center; padding: 32px 20px; margin: 16px 0; background: #fef2f2;
                          border: 2px solid #C0392B; border-radius: 12px; }
        .big-number { font-size: 72px; font-weight: 900; color: #C0392B; line-height: 1; }
        .big-number-label { font-size: 16px; font-weight: 600; color: #666; margin-top: 8px; }

        /* Headline findings (presales) */
        .headline-findings { margin: 20px 0; }
        .headline-item { display: flex; gap: 14px; padding: 14px 18px; border-radius: 8px; margin-bottom: 10px;
                         align-items: flex-start; background: #fef2f2; border: 1px solid #fecaca; }
        .headline-icon { background: #C0392B; color: #fff; width: 28px; height: 28px; border-radius: 50%;
                         display: flex; align-items: center; justify-content: center; font-weight: 900;
                         font-size: 16px; flex-shrink: 0; }
        .headline-text { flex: 1; font-size: 13px; line-height: 1.6; }
        .headline-text strong { color: #7f1d1d; }

        /* Recommendation box */
        .recommendation-box { background: #f0fdf4; border: 1px solid {{brand.PrimaryColor}}44; border-radius: 8px;
                              padding: 20px 24px; margin: 16px 0; }
        .recommendation-box h3 { border: none; margin-top: 0; padding-bottom: 8px; }
        .recommendation-box p { font-size: 13px; line-height: 1.7; }

        /* Next steps */
        .next-steps { margin: 16px 0; }
        .step { display: flex; gap: 14px; margin-bottom: 14px; align-items: flex-start; }
        .step-num { background: {{brand.PrimaryColor}}; color: #fff; width: 32px; height: 32px; border-radius: 50%;
                    display: flex; align-items: center; justify-content: center; font-weight: 700;
                    font-size: 14px; flex-shrink: 0; }
        .step strong { display: block; font-size: 13px; margin-bottom: 2px; }
        .step p { font-size: 12px; color: #666; margin: 0; }

        /* Phase lists */
        .phase-list { margin: 6px 0 0 16px; font-size: 12px; color: #555; }
        .phase-list li { margin-bottom: 4px; }

        /* CTA box */
        .cta-box { background: #3D4043; color: #fff; border-radius: 8px; padding: 20px 24px; margin-top: 24px;
                   text-align: center; }
        .cta-box p { margin: 0; font-size: 14px; }

        /* Hardware grid */
        .hw-grid { display: flex; gap: 16px; margin: 12px 0; flex-wrap: wrap; }
        .hw-col { flex: 1; min-width: 200px; }

        /* Info table */
        .info-table { width: 100%; border-collapse: collapse; margin: 4px 0; }
        .info-table td { padding: 4px 8px; border-bottom: 1px solid #eee; font-size: 12px; }
        .info-table td:first-child { font-weight: 600; width: 120px; color: #555; }

        /* Grade distribution */
        .grade-dist { margin: 12px 0; }
        .grade-bar { display: flex; align-items: center; gap: 10px; margin-bottom: 6px; }
        .grade-label { width: 30px; font-weight: 700; font-size: 13px; text-align: right; }
        .bar-track { flex: 1; height: 22px; background: #f0f0f0; border-radius: 4px; overflow: hidden; }
        .bar-fill { height: 100%; border-radius: 4px; min-width: 2px; }
        .grade-count { width: 30px; font-weight: 600; font-size: 13px; }

        /* Footer */
        .footer { text-align: center; padding: 20px; color: #999; font-size: 11px; border-top: 1px solid #eee; margin-top: 20px; }

        /* Page footer (contact + page number) */
        .page-footer {
            position: absolute; bottom: 6mm; left: 10%; right: 8mm;
            display: flex; justify-content: space-between; align-items: flex-end;
            font-size: 7.5pt; color: #94A3B8; line-height: 1.4;
            border-top: 0.5pt solid #E2E8F0; padding-top: 2mm;
        }
        .pf-left { text-align: left; }
        .pf-right { text-align: right; }

        /* Tier grid (ServiceCatalogBlock) */
        .tier-grid { display:grid; grid-template-columns:repeat(3,1fr); gap:14px; margin:16px 0; }
        .tier-card { border:1px solid #E2E8F0; border-radius:6px; padding:18px; background:#FFFFFF; break-inside:avoid; }
        .tier-card.highlight { border-width:2px; border-color:#15803D; box-shadow:0 4px 12px rgba(15,128,61,0.12); }
        .tier-header { margin-bottom:10px; }
        .tier-name { font-size:14px; font-weight:700; color:#0F172A; }
        .tier-price { font-size:22px; font-weight:900; color:#15803D; margin:6px 0; }
        .tier-bullets { list-style:none; padding:0; margin:12px 0; font-size:11px; }
        .tier-bullets li { padding:4px 0 4px 18px; position:relative; }
        .tier-bullets li::before { content:'✓'; position:absolute; left:0; color:#15803D; font-weight:700; }

        /* Decisions matrix (DecisionsMatrixBlock) */
        .decisions-grid { display:grid; grid-template-columns:repeat(3,1fr); gap:14px; margin:12px 0; }
        .decisions-col { border:1px solid #E2E8F0; border-radius:6px; padding:14px; }
        .decisions-col h4 { font-size:11px; font-weight:700; text-transform:uppercase; letter-spacing:0.05em; margin:0 0 10px; padding-bottom:6px; border-bottom:2px solid currentColor; }
        .decisions-col.approved h4 { color:#15803D; }
        .decisions-col.pending h4 { color:#B45309; }
        .decisions-col.recommended h4 { color:#2563EB; }
        .decision-item { padding:8px 0; border-bottom:1px solid #F1F5F9; }
        .decision-title { font-size:11px; font-weight:600; color:#1E293B; }
        .decision-ask { font-size:9px; color:#64748B; margin-top:2px; }
        .decision-cost { font-size:10px; font-weight:700; color:#15803D; margin-top:2px; }

        /* Control detail cards (ControlDetailBlock) */
        .control-card { border:1px solid #E2E8F0; border-radius:6px; padding:12px 14px; margin-bottom:8px; border-left:4px solid #94A3B8; }
        .control-card.critical { border-left-color:#991B1B; background:#FEF2F2; }
        .control-card.high { border-left-color:#C0392B; background:#FFF5F5; }
        .control-card.medium { border-left-color:#B45309; background:#FFFBEB; }
        .control-card.low { border-left-color:#2563EB; background:#EFF6FF; }
        .control-head { display:flex; align-items:center; gap:8px; margin-bottom:6px; }
        .control-code { font-family:monospace; font-size:10px; font-weight:700; color:#64748B; }
        .control-title { font-size:11px; font-weight:600; color:#1E293B; flex:1; }
        .control-req { font-size:9px; color:#475569; line-height:1.5; margin-bottom:6px; }
        .control-meta { font-size:8px; color:#94A3B8; display:flex; justify-content:space-between; }

        /* Evidence appendix (EvidenceAppendixBlock) */
        .evidence-item { border-bottom:1px solid #E2E8F0; padding:8px 0; }
        .evidence-head { font-size:11px; font-weight:600; color:#1E293B; }
        .evidence-meta { font-size:9px; color:#64748B; margin-top:2px; }
        .evidence-code { font-family:monospace; font-size:9px; color:#991B1B; margin-top:2px; }

        /* Top 3 risk cards (Top3RiskBlock) */
        .top-risk-grid { display:grid; grid-template-columns:repeat(3,1fr); gap:14px; margin:12px 0; }
        .top-risk-card { background:#FEF2F2; border:1px solid #FECACA; border-radius:8px; padding:16px; }
        .top-risk-title { font-size:13px; font-weight:700; color:#7F1D1D; margin-bottom:6px; }
        .top-risk-body { font-size:10px; color:#334155; line-height:1.5; }
        .top-risk-cost { font-size:12px; font-weight:700; color:#991B1B; margin-top:8px; }

        /* Framework cover audit meta (FrameworkCoverBlock) */
        .cover-audit-meta { margin-top:16px; }
        .cover-audit-meta dt { font-size:9px; color:rgba(255,255,255,0.5); text-transform:uppercase; letter-spacing:0.05em; margin-top:8px; }
        .cover-audit-meta dd { font-size:12px; color:#fff; margin:2px 0 0; }

        /* Network mini (NetworkMiniBlock) */
        .network-mini-split { display:grid; grid-template-columns:1fr 1fr; gap:16px; margin:12px 0; }
        .network-mini-map { background:#F1F5F9; border-radius:8px; display:flex; align-items:center; justify-content:center; min-height:180px; color:#94A3B8; font-size:11px; }
        .network-mini-kpis { display:grid; grid-template-columns:1fr 1fr; gap:10px; }

        /* Data table (CategoryBreakdownBlock) */
        .data-table { width:100%; border-collapse:collapse; margin:10px 0; font-size:11px; }
        .data-table th { background:#F8F9FA; color:#1E293B; padding:8px 10px; text-align:left; font-weight:700; font-size:10px; text-transform:uppercase; letter-spacing:0.05em; border-bottom:2px solid #0F172A; }
        .data-table td { padding:6px 10px; border-bottom:1px solid #E2E8F0; }
        .data-table tr:nth-child(even) { background:#F8F9FA; }

        /* Badges (ControlDetailBlock) */
        .badge { padding:2px 6px; border-radius:3px; font-size:9px; font-weight:600; }
        .badge-action { background:#991B1B; color:#fff; }
        .badge-warning { background:#B45309; color:#fff; }
        .badge-muted { background:#E2E8F0; color:#64748B; }

        @media print { .page { margin: 0; box-shadow: none; } body { background: #fff; } }
        """;

    public static string GetA4PrintCss(ReportBranding brand) => $$"""
        /* ── A4 print discipline ─────────────────────────────────────── */
        @page { size: A4 portrait; margin: 0; }
        html, body {
            margin: 0 !important;
            padding: 0 !important;
            -webkit-print-color-adjust: exact !important;
            print-color-adjust: exact !important;
        }

        /* The A4 page containers enforce a 210×296mm size on BOTH screen
           and print. Why 296mm and not 297mm: a 1mm safety buffer against
           browser mm→px rounding, printer non-printable margins, and
           parent margin collapse. Without it, a single stray pixel pushes
           content onto a second physical sheet, which is what was reported
           on the cover page. */
        .cover, .page {
            width: 210mm !important;
            height: 296mm !important;
            min-height: 296mm !important;
            max-height: 296mm !important;
            box-sizing: border-box !important;
        }
        .cover, .page { overflow: hidden !important; }
        /* On-screen preview: the A4 pages sit centered horizontally with a
           bottom gap between successive pages. Print rules below override
           these margins to 0 so the printed output remains edge-to-edge. */
        .cover { margin: 0 auto 24px auto !important; }
        .page  { margin: 0 auto 24px auto !important; }

        /* ── On-screen preview polish ────────────────────────────────── */
        /* Shrink the whole preview so a full A4 page fits comfortably in
           the viewport without horizontal scroll on 13"/14" laptops, and
           give the pages a soft "floating paper" drop shadow. `zoom` is
           supported in all modern Chromium browsers, Safari, and Firefox
           126+. None of this applies to @media print so the output is
           unchanged. */
        @media screen {
            html { background: #F8F9FA; }
            body {
                zoom: 0.78;
                padding-top: 28px;
                padding-bottom: 28px;
            }
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
                margin: 0 !important;
                padding: 0 !important;
                box-shadow: none !important;
                page-break-after: always;
                break-after: page;
                /* Strict 296mm even in print, plus `page-break-inside:avoid`
                   so the browser never splits one logical page across two
                   physical sheets. */
                page-break-inside: avoid;
                break-inside: avoid;
            }
            .cover:last-of-type, .page:last-of-type {
                page-break-after: auto;
                break-after: auto;
            }
            .risk-card, .headline-item, .stat, .fw-bar-row, .cat-header,
            .results-table tr, .hw-col, .recommendation-box, .insight-box,
            .op-risk, .op-win, .op-kpi, .op-footer, .op-header {
                break-inside: avoid;
                page-break-inside: avoid;
            }
            .no-print { display: none !important; }
        }

        /* ── Header bars: edge-to-edge for ALL report types ──────────── */
        /* The 210mm parent with box-sizing border-box already guarantees
           this, but we force explicit width/margin to eliminate any
           compounded padding from other CSS layers. Logos render white
           via filter so they read clearly against the dark #3D4043
           background (and the ribbon gradient behind them). */
        .ph {
            width: 100% !important;
            margin: 0 !important;
            box-sizing: border-box !important;
        }
        .cover .logo,
        .ph img {
            filter: brightness(0) invert(1) !important;
            -webkit-filter: brightness(0) invert(1) !important;
        }

        /* ── Big 4 Financial Audit light palette (unified content area) ──
           Premium light-mode overrides applied to every .pb (page body) of
           every report type. Reports that define their own theme block
           later in the document — .page-mb (monthly briefing, dark) and
           .pres-light (detailed presales) — override these via source
           order, so the base stays Big 4 and the specialty skins keep
           their distinct look. */
        .page { background: #FFFFFF !important; }
        .pb { color: #334155; }

        /* Typography */
        .pb h3 {
            color: #1E293B !important;
            border-bottom: 2px solid #0F172A !important;
            font-weight: 700 !important;
            letter-spacing: 0.02em !important;
        }
        .pb h4 { color: #1E293B !important; }
        .pb p, .pb li, .pb td { color: #334155; }
        .pb strong { color: #1E293B; }
        .pb .cat-summary { color: #64748B !important; }

        /* Stat cards (used by exec summary, presales, etc.) */
        .pb .stat {
            background: #F8F9FA !important;
            border: 1px solid #E2E8F0 !important;
            box-shadow: 0 1px 2px 0 rgba(15, 23, 42, 0.04);
        }
        .pb .stat.pass-stat { background: #F0FDF4 !important; border-color: #15803D !important; border-left: 3px solid #15803D !important; }
        .pb .stat.warn-stat { background: #FFFBEB !important; border-color: #B45309 !important; border-left: 3px solid #B45309 !important; }
        .pb .stat.fail-stat { background: #FEF2F2 !important; border-color: #991B1B !important; border-left: 3px solid #991B1B !important; }
        .pb .stat-value { color: #0F172A !important; }
        .pb .stat-label { color: #64748B !important; text-transform: uppercase !important; letter-spacing: 0.08em !important; }

        /* Results tables */
        .pb .results-table {
            background: #FFFFFF !important;
            border: 1px solid #E2E8F0 !important;
            border-radius: 4px;
            overflow: hidden;
            box-shadow: 0 1px 2px 0 rgba(15, 23, 42, 0.04);
        }
        .pb .results-table th {
            background: #F8F9FA !important;
            color: #0F172A !important;
            border-bottom: 2px solid #0F172A !important;
            text-transform: uppercase !important;
            letter-spacing: 0.05em !important;
            font-weight: 700 !important;
        }
        .pb .results-table td { color: #334155 !important; border-bottom: 1px solid #E2E8F0 !important; }
        .pb .results-table tr { background: #FFFFFF !important; }
        .pb .results-table tr:nth-child(even) { background: #F8F9FA !important; }
        .pb .results-table tr.pass { background: #F0FDF4 !important; }
        .pb .results-table tr.warn { background: #FFFBEB !important; }
        .pb .results-table tr.fail { background: #FEF2F2 !important; }
        .pb .results-table tr.fail td { color: #7F1D1D !important; }

        /* Generic call-out / insight box */
        .pb .insight-box {
            background: #F8F9FA !important;
            border: 1px solid #CBD5E1 !important;
            border-left: 4px solid #0F172A !important;
            color: #334155 !important;
            box-shadow: 0 1px 2px 0 rgba(15, 23, 42, 0.04);
        }
        .pb .insight-box p { color: #334155 !important; }
        .pb .insight-box strong { color: #0F172A !important; }
        .pb .insight-box.fail-box {
            background: #FEF2F2 !important;
            border-color: #FECACA !important;
            border-left: 4px solid #991B1B !important;
        }
        .pb .insight-box.fail-box p { color: #7F1D1D !important; }
        .pb .insight-box.fail-box strong { color: #450A0A !important; }

        /* Risk cards */
        .pb .risk-card {
            background: #FEF2F2 !important;
            border: 1px solid #FECACA !important;
            border-left: 4px solid #991B1B !important;
            box-shadow: 0 1px 2px 0 rgba(15, 23, 42, 0.04);
        }
        .pb .risk-num { background: #991B1B !important; color: #FFFFFF !important; }
        .pb .risk-body strong { color: #1E293B !important; }
        .pb .risk-detail { color: #64748B !important; }

        /* Big number box */
        .pb .big-number-box {
            background: #FEF2F2 !important;
            border: 2px solid #991B1B !important;
            border-radius: 8px !important;
            box-shadow: 0 4px 6px -1px rgba(15, 23, 42, 0.06), 0 2px 4px -2px rgba(15, 23, 42, 0.04);
        }
        .pb .big-number { color: #991B1B !important; }
        .pb .big-number-label { color: #64748B !important; }

        /* Headline items (presales detailed + opener) */
        .pb .headline-item {
            background: #FEF2F2 !important;
            border: 1px solid #FECACA !important;
            border-left: 4px solid #991B1B !important;
            box-shadow: 0 1px 2px 0 rgba(15, 23, 42, 0.04);
        }
        .pb .headline-icon { background: #991B1B !important; color: #FFFFFF !important; }
        .pb .headline-text { color: #334155 !important; }
        .pb .headline-text strong { color: #450A0A !important; }

        /* Recommendation box */
        .pb .recommendation-box {
            background: #F8F9FA !important;
            border: 1px solid #CBD5E1 !important;
            border-top: 4px solid #0F172A !important;
            box-shadow: 0 4px 6px -1px rgba(15, 23, 42, 0.06);
        }
        .pb .recommendation-box h3 { color: #1E293B !important; border: none !important; }
        .pb .recommendation-box p { color: #334155 !important; }
        .pb .recommendation-box strong { color: #0F172A !important; }

        /* Steps / roadmap */
        .pb .step-num { background: #0F172A !important; color: #FFFFFF !important; }
        .pb .step strong { color: #1E293B !important; }
        .pb .step p { color: #64748B !important; }
        .pb .phase-list li { color: #334155 !important; }

        /* CTA box */
        .pb .cta-box {
            background: #F8F9FA !important;
            border: 1px solid #CBD5E1 !important;
            border-top: 4px solid #0F172A !important;
            color: #334155 !important;
            box-shadow: 0 4px 6px -1px rgba(15, 23, 42, 0.06), 0 2px 4px -2px rgba(15, 23, 42, 0.04);
        }
        .pb .cta-box p { color: #334155 !important; }
        .pb .cta-box strong { color: #0F172A !important; }

        /* Framework bars */
        .pb .fw-label { color: #1E293B !important; }
        .pb .fw-pct { color: #0F172A !important; }
        .pb .fw-detail { color: #64748B !important; }

        /* Severity + status badges */
        .pb .severity.critical { background: #7F1D1D !important; color: #FFFFFF !important; }
        .pb .severity.high     { background: #991B1B !important; color: #FFFFFF !important; }
        .pb .severity.medium   { background: #B45309 !important; color: #FFFFFF !important; }
        .pb .severity.low      { background: #0F172A !important; color: #FFFFFF !important; }
        .pb .status-badge.pass { background: #15803D !important; color: #FFFFFF !important; }
        .pb .status-badge.warn { background: #B45309 !important; color: #FFFFFF !important; }
        .pb .status-badge.fail { background: #991B1B !important; color: #FFFFFF !important; }

        /* Info table (hardware/security/network grids) */
        .pb .info-table td { color: #334155 !important; border-bottom: 1px solid #E2E8F0 !important; }
        .pb .info-table td:first-child { color: #1E293B !important; font-weight: 600 !important; }
        .pb .hw-col h4 { color: #0F172A !important; }

        /* Big number / pass-cell / warn-cell / fail-cell */
        .pb .pass-cell { color: #15803D !important; }
        .pb .warn-cell { color: #B45309 !important; }
        .pb .fail-cell { color: #991B1B !important; }

        /* Grade distribution bars */
        .pb .grade-label { color: #1E293B !important; }
        .pb .grade-count { color: #1E293B !important; }

        /* ── Executive One-Pager layout (screen + print) ─────────────── */
        /* Uses flexbox column so the footer always sticks to the bottom
           of the 296mm page without absolute positioning. The one-pager
           now uses the SAME .ph header as every other report (via
           AppendPageHeader) — no special .op-header styles needed. */
        .onepager {
            display: flex !important;
            flex-direction: column !important;
            page-break-after: always;
            position: relative;
        }
        /* The unified .ph header inside the onepager occupies its natural
           height. .stripe sits below it. Both are `flex: 0 0 auto`. */
        .onepager > .ph,
        .onepager > .stripe { flex: 0 0 auto; }
        .onepager .op-body {
            padding: 6mm 12mm 0;
            flex: 1 1 auto;
            min-height: 0; /* allow flex child to shrink if content grows */
        }

        .op-kpis { display: grid; grid-template-columns: 1.4fr 1fr 1fr 1fr; gap: 4mm; margin-bottom: 5mm; }
        .op-kpi {
            background: #f8f9fa; border: 1px solid #e5e7eb; border-radius: 2mm;
            padding: 4mm; text-align: center;
        }
        .op-kpi.op-kpi-hero { background: {{brand.PrimaryColor}}; color: #fff; border-color: {{brand.PrimaryColor}}; }
        .op-kpi .op-kpi-val { display: block; font-size: 22pt; font-weight: 900; line-height: 1; }
        .op-kpi .op-kpi-label { display: block; font-size: 7pt; text-transform: uppercase; letter-spacing: 0.08em; margin-top: 2mm; color: #666; }
        .op-kpi.op-kpi-hero .op-kpi-label { color: {{brand.AccentColor}}; }
        .op-kpi.op-kpi-grade .op-kpi-val { font-size: 28pt; color: {{brand.PrimaryColor}}; }

        .op-section-title {
            font-size: 9pt; font-weight: 800; text-transform: uppercase;
            letter-spacing: 0.1em; color: {{brand.PrimaryColor}};
            border-bottom: 1.5pt solid {{brand.AccentColor}};
            padding-bottom: 1mm; margin: 4mm 0 2.5mm;
        }

        .op-fw-bars { margin-bottom: 4mm; }
        .op-fw-bars .fw-bar-row { margin-bottom: 2mm; }
        .op-fw-bars .fw-label { font-size: 8pt; width: 18mm; }
        .op-fw-bars .fw-track { height: 3.5mm; }
        .op-fw-bars .fw-pct { font-size: 8pt; width: 12mm; }
        .op-fw-bars .fw-detail { font-size: 7pt; width: 22mm; }

        .op-lists { display: grid; grid-template-columns: 1fr 1fr; gap: 5mm; }
        .op-list-col { }
        .op-risk, .op-win {
            display: flex; gap: 3mm; padding: 2.5mm 3mm;
            border-radius: 1.5mm; margin-bottom: 2mm;
            font-size: 8.5pt; line-height: 1.35;
        }
        .op-risk { background: #fef2f2; border: 0.5pt solid #fecaca; }
        .op-win  { background: #f0fdf4; border: 0.5pt solid {{brand.PrimaryColor}}44; }
        .op-risk .op-num, .op-win .op-num {
            width: 5mm; height: 5mm; border-radius: 50%;
            display: flex; align-items: center; justify-content: center;
            font-weight: 800; font-size: 8pt; color: #fff; flex-shrink: 0;
        }
        .op-risk .op-num { background: #C0392B; }
        .op-win  .op-num { background: {{brand.PrimaryColor}}; }
        .op-risk strong, .op-win strong { display: block; font-size: 9pt; margin-bottom: 0.5mm; }
        .op-risk .op-meta, .op-win .op-meta { font-size: 7pt; color: #666; margin-top: 0.5mm; }

        .op-remediation {
            background: #f0f4ff; border: 0.5pt solid #c7d2fe;
            border-radius: 2mm; padding: 3mm 4mm; margin-top: 3mm;
            display: flex; justify-content: space-between; align-items: center;
            font-size: 9pt;
        }
        .op-remediation .op-hours { font-size: 16pt; font-weight: 900; color: {{brand.PrimaryColor}}; }

        .op-footer {
            flex: 0 0 auto;
            padding: 4mm 12mm 6mm; border-top: 0.5pt solid #e5e7eb;
            background: #fafafa;
            font-size: 7.5pt; color: #555; line-height: 1.4;
        }
        .op-footer .op-footer-row { display: flex; justify-content: space-between; gap: 6mm; }
        .op-footer .op-footer-user { font-weight: 600; color: #3D4043; font-size: 8pt; }
        .op-footer .op-footer-brand { color: #999; font-size: 7pt; text-align: right; }
        """;
}

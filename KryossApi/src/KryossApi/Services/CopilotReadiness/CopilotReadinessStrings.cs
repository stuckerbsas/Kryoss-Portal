namespace KryossApi.Services.CopilotReadiness;

/// <summary>
/// Bilingual (EN/ES) string table for the Copilot Readiness M365 report.
/// Every piece of user-visible text lives here so the report builder
/// stays language-agnostic.
/// </summary>
internal static class CopilotReadinessStrings
{
    public static string Get(string key, string lang) =>
        lang == "es"
            ? _es.GetValueOrDefault(key, _en.GetValueOrDefault(key, key))
            : _en.GetValueOrDefault(key, key);

    // ── English ────────────────────────────────────────────────────

    private static readonly Dictionary<string, string> _en = new()
    {
        // Cover
        ["cover.eyebrow"] = "COPILOT READINESS ASSESSMENT",
        ["cover.title"] = "Microsoft 365 Security & Copilot Readiness Report",
        ["cover.subtitle"] = "Comprehensive Assessment",
        ["cover.date_label"] = "Assessment Date",
        ["cover.tenant_label"] = "Tenant",
        ["cover.devices_label"] = "devices",

        // Verdicts
        ["verdict.ready"] = "Ready",
        ["verdict.nearly"] = "Nearly Ready",
        ["verdict.not_ready"] = "Not Ready",
        ["verdict.ready_desc"] = "Your environment meets the baseline requirements for Microsoft Copilot deployment.",
        ["verdict.nearly_desc"] = "Your environment is close to readiness. Address the items below before enabling Copilot.",
        ["verdict.not_ready_desc"] = "Significant gaps remain. We recommend completing the remediation roadmap before Copilot deployment.",
        ["verdict.no_scan"] = "No Copilot Readiness scan has been performed yet. Connect your M365 tenant and run a scan to generate this section.",

        // Executive Summary
        ["exec.title"] = "Executive Summary",
        ["exec.eyebrow"] = "OVERVIEW",
        ["exec.kpi.label_coverage"] = "Label Coverage",
        ["exec.kpi.overshared"] = "Overshared Items",
        ["exec.kpi.external_users"] = "External Users",
        ["exec.kpi.ca_score"] = "Conditional Access",
        ["exec.kpi.zt_gaps"] = "Zero Trust Gaps",
        ["exec.kpi.compliance_gaps"] = "Compliance Gaps",
        ["exec.scorecard"] = "Dimension Scorecard",
        ["exec.copilot_license"] = "Copilot License Status",
        ["exec.copilot_detected"] = "Microsoft 365 Copilot licenses detected",
        ["exec.copilot_not_detected"] = "No Copilot licenses detected",
        ["exec.actions_before_copilot"] = "{0} actions to complete before Copilot deployment",

        // Dimensions
        ["dim.d1"] = "D1: Information Protection",
        ["dim.d2"] = "D2: Oversharing Control",
        ["dim.d3"] = "D3: External User Governance",
        ["dim.d4"] = "D4: Conditional Access",
        ["dim.d5"] = "D5: Zero Trust Posture",
        ["dim.d6"] = "D6: Compliance & Purview",
        ["dim.d1.short"] = "Info Protection",
        ["dim.d2.short"] = "Oversharing",
        ["dim.d3.short"] = "External Users",
        ["dim.d4.short"] = "Cond. Access",
        ["dim.d5.short"] = "Zero Trust",
        ["dim.d6.short"] = "Compliance",

        // M365 Security Posture (Section 3)
        ["m365sec.title"] = "M365 Security Posture",
        ["m365sec.eyebrow"] = "50-CHECK BASELINE",
        ["m365sec.category"] = "Category",
        ["m365sec.pass"] = "Pass",
        ["m365sec.fail"] = "Fail",
        ["m365sec.warn"] = "Warn",
        ["m365sec.top10"] = "Top 10 Critical Findings",
        ["m365sec.check_id"] = "Check",
        ["m365sec.name"] = "Finding",
        ["m365sec.severity"] = "Severity",
        ["m365sec.status"] = "Status",
        ["m365sec.no_data"] = "Connect your M365 tenant to see security posture data.",

        // D1: Information Protection (Section 4)
        ["d1.title"] = "Information Protection",
        ["d1.eyebrow"] = "D1 - SENSITIVITY LABELS",
        ["d1.label_coverage"] = "Label Coverage",
        ["d1.label_distribution"] = "Label Distribution",
        ["d1.top_unlabeled"] = "Top Unlabeled Sites",
        ["d1.site"] = "Site",
        ["d1.total_files"] = "Total Files",
        ["d1.labeled"] = "Labeled",
        ["d1.pct"] = "Coverage %",
        ["d1.labels"] = "Top Labels",

        // D2: Oversharing (Section 5)
        ["d2.title"] = "Oversharing Analysis",
        ["d2.eyebrow"] = "D2 - DATA EXPOSURE",
        ["d2.overshare_pct"] = "Oversharing Rate",
        ["d2.top_overshared"] = "Top 10 Overshared Sites",
        ["d2.risk_breakdown"] = "Risk Breakdown",
        ["d2.overshared_files"] = "Overshared",
        ["d2.risk"] = "Risk Level",

        // D3: External Users (Section 6)
        ["d3.title"] = "External User Governance",
        ["d3.eyebrow"] = "D3 - GUEST ACCESS",
        ["d3.total_external"] = "Total External Users",
        ["d3.high_risk"] = "High Risk Users",
        ["d3.pending"] = "Pending Invitations",
        ["d3.user_table"] = "High-Risk External Users",
        ["d3.user"] = "User",
        ["d3.domain"] = "Domain",
        ["d3.last_signin"] = "Last Sign-In",
        ["d3.sites"] = "Sites",
        ["d3.permission"] = "Permission",

        // D4: Conditional Access (Section 7)
        ["d4.title"] = "Conditional Access",
        ["d4.eyebrow"] = "D4 - IDENTITY SECURITY",
        ["d4.coverage"] = "Policy Coverage",
        ["d4.findings"] = "Findings",
        ["d4.feature"] = "Feature",
        ["d4.observation"] = "Observation",
        ["d4.recommendation"] = "Recommendation",

        // D5+D6: Zero Trust & Compliance (Section 8)
        ["d5d6.title"] = "Zero Trust & Compliance",
        ["d5d6.eyebrow"] = "D5 + D6 - POSTURE",
        ["d5d6.entra_findings"] = "Entra ID Findings",
        ["d5d6.defender_findings"] = "Defender Findings",
        ["d5d6.purview_findings"] = "Purview Findings",
        ["d5d6.governance"] = "Governance Pillars",
        ["d5d6.service"] = "Service",
        ["d5d6.priority"] = "Priority",

        // License Inventory (Section 9)
        ["lic.title"] = "License Inventory",
        ["lic.eyebrow"] = "SERVICE STATUS",
        ["lic.service"] = "Service",
        ["lic.feature"] = "Feature",
        ["lic.status"] = "Status",
        ["lic.finding"] = "Finding",

        // Remediation Roadmap (Section 10)
        ["roadmap.title"] = "Remediation Roadmap",
        ["roadmap.eyebrow"] = "ACTION PLAN",
        ["roadmap.phase1"] = "Phase 1: Critical (0-30 days)",
        ["roadmap.phase2"] = "Phase 2: Recommended (30-60 days)",
        ["roadmap.phase3"] = "Phase 3: Ongoing (60-90 days)",
        ["roadmap.action"] = "Action",
        ["roadmap.priority"] = "Priority",
        ["roadmap.no_actions"] = "No actions required in this phase.",

        // Methodology (Section 11)
        ["method.title"] = "Methodology",
        ["method.eyebrow"] = "SCORING FRAMEWORK",
        ["method.scoring"] = "Scoring Formula",
        ["method.scoring_desc"] = "Overall = (D1 x 25%) + (D2 x 25%) + (D3 x 20%) + (D4 x 15%) + (D5 x 10%) + (D6 x 5%)",
        ["method.thresholds"] = "Thresholds",
        ["method.threshold_ready"] = "Ready: Overall >= 4.0 / 5.0",
        ["method.threshold_nearly"] = "Nearly Ready: Overall >= 3.0 / 5.0",
        ["method.threshold_not"] = "Not Ready: Overall < 3.0 / 5.0",
        ["method.data_sources"] = "Data Sources",
        ["method.ds_graph"] = "Microsoft Graph API (v1.0 + beta)",
        ["method.ds_defender"] = "Microsoft Defender for Endpoint API",
        ["method.ds_purview"] = "Microsoft Purview Compliance API",
        ["method.ds_sharepoint"] = "SharePoint Online Management API",
        ["method.ds_entra"] = "Microsoft Entra ID (Conditional Access, Identity Protection)",
        ["method.weights"] = "Dimension Weights",

        // Status badges
        ["status.pass"] = "PASS",
        ["status.fail"] = "FAIL",
        ["status.warn"] = "WARNING",
        ["status.action_required"] = "ACTION REQUIRED",
        ["status.success"] = "OK",
        ["status.disabled"] = "DISABLED",
        ["status.warning"] = "WARNING",
        ["status.info"] = "INFO",
        ["status.na"] = "N/A",

        // Severity
        ["sev.critical"] = "Critical",
        ["sev.high"] = "High",
        ["sev.medium"] = "Medium",
        ["sev.low"] = "Low",

        // Misc
        ["misc.of"] = "of",
        ["misc.score"] = "Score",
        ["misc.overall"] = "Overall Score",
        ["misc.no_findings"] = "No findings in this category.",
        ["misc.page"] = "Page",
        ["misc.confidential"] = "CONFIDENTIAL",
        ["misc.generated_by"] = "Generated by",
        ["misc.technology_advisor"] = "Your Technology Advisor",
        ["misc.not_connected"] = "M365 tenant not connected",
    };

    // ── Spanish ────────────────────────────────────────────────────

    private static readonly Dictionary<string, string> _es = new()
    {
        // Cover
        ["cover.eyebrow"] = "EVALUACION DE PREPARACION PARA COPILOT",
        ["cover.title"] = "Informe de Seguridad Microsoft 365 y Preparacion para Copilot",
        ["cover.subtitle"] = "Evaluacion Integral",
        ["cover.date_label"] = "Fecha de Evaluacion",
        ["cover.tenant_label"] = "Tenant",
        ["cover.devices_label"] = "equipos",

        // Verdicts
        ["verdict.ready"] = "Listo",
        ["verdict.nearly"] = "Casi Listo",
        ["verdict.not_ready"] = "No Listo",
        ["verdict.ready_desc"] = "Su entorno cumple con los requisitos base para la implementacion de Microsoft Copilot.",
        ["verdict.nearly_desc"] = "Su entorno esta cerca de estar listo. Atienda los elementos indicados antes de habilitar Copilot.",
        ["verdict.not_ready_desc"] = "Existen brechas significativas. Recomendamos completar la hoja de ruta de remediacion antes de implementar Copilot.",
        ["verdict.no_scan"] = "Aun no se ha realizado un escaneo de Preparacion para Copilot. Conecte su tenant M365 y ejecute un escaneo para generar esta seccion.",

        // Executive Summary
        ["exec.title"] = "Resumen Ejecutivo",
        ["exec.eyebrow"] = "PANORAMA GENERAL",
        ["exec.kpi.label_coverage"] = "Cobertura de Etiquetas",
        ["exec.kpi.overshared"] = "Elementos Sobreexpuestos",
        ["exec.kpi.external_users"] = "Usuarios Externos",
        ["exec.kpi.ca_score"] = "Acceso Condicional",
        ["exec.kpi.zt_gaps"] = "Brechas Zero Trust",
        ["exec.kpi.compliance_gaps"] = "Brechas de Cumplimiento",
        ["exec.scorecard"] = "Cuadro de Dimensiones",
        ["exec.copilot_license"] = "Estado de Licencia Copilot",
        ["exec.copilot_detected"] = "Licencias de Microsoft 365 Copilot detectadas",
        ["exec.copilot_not_detected"] = "No se detectaron licencias de Copilot",
        ["exec.actions_before_copilot"] = "{0} acciones a completar antes de implementar Copilot",

        // Dimensions
        ["dim.d1"] = "D1: Proteccion de la Informacion",
        ["dim.d2"] = "D2: Control de Sobreexposicion",
        ["dim.d3"] = "D3: Gobernanza de Usuarios Externos",
        ["dim.d4"] = "D4: Acceso Condicional",
        ["dim.d5"] = "D5: Postura Zero Trust",
        ["dim.d6"] = "D6: Cumplimiento y Purview",
        ["dim.d1.short"] = "Proteccion Info",
        ["dim.d2.short"] = "Sobreexposicion",
        ["dim.d3.short"] = "Usuarios Ext.",
        ["dim.d4.short"] = "Acceso Cond.",
        ["dim.d5.short"] = "Zero Trust",
        ["dim.d6.short"] = "Cumplimiento",

        // M365 Security Posture
        ["m365sec.title"] = "Postura de Seguridad M365",
        ["m365sec.eyebrow"] = "LINEA BASE DE 50 CONTROLES",
        ["m365sec.category"] = "Categoria",
        ["m365sec.pass"] = "Aprobado",
        ["m365sec.fail"] = "Fallo",
        ["m365sec.warn"] = "Advertencia",
        ["m365sec.top10"] = "Top 10 Hallazgos Criticos",
        ["m365sec.check_id"] = "Control",
        ["m365sec.name"] = "Hallazgo",
        ["m365sec.severity"] = "Severidad",
        ["m365sec.status"] = "Estado",
        ["m365sec.no_data"] = "Conecte su tenant M365 para ver datos de postura de seguridad.",

        // D1
        ["d1.title"] = "Proteccion de la Informacion",
        ["d1.eyebrow"] = "D1 - ETIQUETAS DE SENSIBILIDAD",
        ["d1.label_coverage"] = "Cobertura de Etiquetas",
        ["d1.label_distribution"] = "Distribucion de Etiquetas",
        ["d1.top_unlabeled"] = "Sitios con Menor Cobertura",
        ["d1.site"] = "Sitio",
        ["d1.total_files"] = "Archivos Totales",
        ["d1.labeled"] = "Etiquetados",
        ["d1.pct"] = "Cobertura %",
        ["d1.labels"] = "Etiquetas Principales",

        // D2
        ["d2.title"] = "Analisis de Sobreexposicion",
        ["d2.eyebrow"] = "D2 - EXPOSICION DE DATOS",
        ["d2.overshare_pct"] = "Tasa de Sobreexposicion",
        ["d2.top_overshared"] = "Top 10 Sitios Sobreexpuestos",
        ["d2.risk_breakdown"] = "Desglose por Riesgo",
        ["d2.overshared_files"] = "Sobreexpuestos",
        ["d2.risk"] = "Nivel de Riesgo",

        // D3
        ["d3.title"] = "Gobernanza de Usuarios Externos",
        ["d3.eyebrow"] = "D3 - ACCESO DE INVITADOS",
        ["d3.total_external"] = "Total Usuarios Externos",
        ["d3.high_risk"] = "Usuarios de Alto Riesgo",
        ["d3.pending"] = "Invitaciones Pendientes",
        ["d3.user_table"] = "Usuarios Externos de Alto Riesgo",
        ["d3.user"] = "Usuario",
        ["d3.domain"] = "Dominio",
        ["d3.last_signin"] = "Ultimo Inicio de Sesion",
        ["d3.sites"] = "Sitios",
        ["d3.permission"] = "Permiso",

        // D4
        ["d4.title"] = "Acceso Condicional",
        ["d4.eyebrow"] = "D4 - SEGURIDAD DE IDENTIDAD",
        ["d4.coverage"] = "Cobertura de Politicas",
        ["d4.findings"] = "Hallazgos",
        ["d4.feature"] = "Caracteristica",
        ["d4.observation"] = "Observacion",
        ["d4.recommendation"] = "Recomendacion",

        // D5+D6
        ["d5d6.title"] = "Zero Trust y Cumplimiento",
        ["d5d6.eyebrow"] = "D5 + D6 - POSTURA",
        ["d5d6.entra_findings"] = "Hallazgos de Entra ID",
        ["d5d6.defender_findings"] = "Hallazgos de Defender",
        ["d5d6.purview_findings"] = "Hallazgos de Purview",
        ["d5d6.governance"] = "Pilares de Gobernanza",
        ["d5d6.service"] = "Servicio",
        ["d5d6.priority"] = "Prioridad",

        // License Inventory
        ["lic.title"] = "Inventario de Licencias",
        ["lic.eyebrow"] = "ESTADO DE SERVICIOS",
        ["lic.service"] = "Servicio",
        ["lic.feature"] = "Caracteristica",
        ["lic.status"] = "Estado",
        ["lic.finding"] = "Hallazgo",

        // Roadmap
        ["roadmap.title"] = "Hoja de Ruta de Remediacion",
        ["roadmap.eyebrow"] = "PLAN DE ACCION",
        ["roadmap.phase1"] = "Fase 1: Critico (0-30 dias)",
        ["roadmap.phase2"] = "Fase 2: Recomendado (30-60 dias)",
        ["roadmap.phase3"] = "Fase 3: Continuo (60-90 dias)",
        ["roadmap.action"] = "Accion",
        ["roadmap.priority"] = "Prioridad",
        ["roadmap.no_actions"] = "No se requieren acciones en esta fase.",

        // Methodology
        ["method.title"] = "Metodologia",
        ["method.eyebrow"] = "MARCO DE PUNTUACION",
        ["method.scoring"] = "Formula de Puntuacion",
        ["method.scoring_desc"] = "Total = (D1 x 25%) + (D2 x 25%) + (D3 x 20%) + (D4 x 15%) + (D5 x 10%) + (D6 x 5%)",
        ["method.thresholds"] = "Umbrales",
        ["method.threshold_ready"] = "Listo: Total >= 4.0 / 5.0",
        ["method.threshold_nearly"] = "Casi Listo: Total >= 3.0 / 5.0",
        ["method.threshold_not"] = "No Listo: Total < 3.0 / 5.0",
        ["method.data_sources"] = "Fuentes de Datos",
        ["method.ds_graph"] = "Microsoft Graph API (v1.0 + beta)",
        ["method.ds_defender"] = "Microsoft Defender for Endpoint API",
        ["method.ds_purview"] = "Microsoft Purview Compliance API",
        ["method.ds_sharepoint"] = "SharePoint Online Management API",
        ["method.ds_entra"] = "Microsoft Entra ID (Acceso Condicional, Proteccion de Identidad)",
        ["method.weights"] = "Pesos por Dimension",

        // Status badges
        ["status.pass"] = "APROBADO",
        ["status.fail"] = "FALLO",
        ["status.warn"] = "ADVERTENCIA",
        ["status.action_required"] = "ACCION REQUERIDA",
        ["status.success"] = "OK",
        ["status.disabled"] = "DESHABILITADO",
        ["status.warning"] = "ADVERTENCIA",
        ["status.info"] = "INFO",
        ["status.na"] = "N/A",

        // Severity
        ["sev.critical"] = "Critico",
        ["sev.high"] = "Alto",
        ["sev.medium"] = "Medio",
        ["sev.low"] = "Bajo",

        // Misc
        ["misc.of"] = "de",
        ["misc.score"] = "Puntuacion",
        ["misc.overall"] = "Puntuacion General",
        ["misc.no_findings"] = "Sin hallazgos en esta categoria.",
        ["misc.page"] = "Pagina",
        ["misc.confidential"] = "CONFIDENCIAL",
        ["misc.generated_by"] = "Generado por",
        ["misc.technology_advisor"] = "Su Asesor Tecnologico",
        ["misc.not_connected"] = "Tenant M365 no conectado",
    };
}

using System.Text;

namespace KryossApi.Services.Reports.Blocks;

public class MethodologyBlock : IFlowBlock
{
    private readonly AudiencePerspective _audience;
    public MethodologyBlock(AudiencePerspective audience = AudiencePerspective.Technical) => _audience = audience;

    public string? SectionTitle(ReportOptions options) =>
        options.IsSpanish ? "Metodología: Depreciación Segura de 90 Días" : "Methodology: 90-Day Safe Deprecation";

    public int EstimateHeight(ReportData data) => _audience == AudiencePerspective.Audit ? 700 : 600;

    public string RenderContent(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        var es = options.IsSpanish;
        var brand = data.Branding;

        sb.AppendLine("<div class='methodology-warning'>");
        if (es)
            sb.AppendLine("<strong>&#9888; El error que vemos cometer a los clientes:</strong> Desactivar abruptamente protocolos legacy (NTLM, SMBv1, WDigest, Kerberos RC4) sin mapear primero las dependencias causa interrupciones catastróficas e inmediatas del negocio. Aplicaciones de línea de negocio, file shares legacy, escáneres, sistemas telefónicos e integraciones dejan de funcionar en el momento en que el protocolo muere &mdash; y la interrupción recae directamente sobre el equipo de TI que apagó el switch.");
        else
            sb.AppendLine("<strong>&#9888; The mistake we watch clients make:</strong> Abruptly turning off legacy protocols (NTLM, SMBv1, WDigest, Kerberos RC4) without first mapping dependencies causes catastrophic, immediate business outages. Line-of-business applications, legacy file shares, scanners, phone systems and integrations break the moment the protocol dies &mdash; and the outage lands squarely on the IT team that flipped the switch.");
        sb.AppendLine("</div>");

        sb.AppendLine("<div class='methodology-card'>");
        if (es)
        {
            sb.AppendLine("<h4>Cómo cerramos estos vectores sin dejarte offline</h4>");
            sb.AppendLine("<p>TeamLogic IT despliega un <strong>motor de telemetría pasiva</strong> a través de tu red durante una <strong>ventana de observación de 90 días</strong>. El motor captura silenciosamente cada llamada de protocolo legacy &mdash; qué hosts las hacen, qué cuentas las inician, qué aplicaciones dependen de ellas y con qué cadencia. Cero huella de instalación en los endpoints críticos, cero reinicios, cero interrupciones para el usuario.</p>");
            sb.AppendLine("<ul>");
            sb.AppendLine("<li><strong>Día 1-30:</strong> captura baseline de todo el tráfico NTLM / SMBv1 / RC4 / WDigest en la red y en el DC</li>");
            sb.AppendLine("<li><strong>Día 31-60:</strong> atribución &mdash; cada llamada legacy vinculada a una aplicación, usuario o servicio específico</li>");
            sb.AppendLine("<li><strong>Día 61-90:</strong> blueprint de depreciación &mdash; el orden exacto, por dependencia, en que cada protocolo legacy puede apagarse sin romper nada</li>");
            sb.AppendLine("</ul>");
            sb.AppendLine("<p style='margin-top:10px'>Al final de los 90 días recibes un <strong>plan de depreciación por sistema</strong> con <strong style='color:#0F172A'>cero ambigüedad</strong> y la garantía explícita de que cada eliminación de protocolo se ejecutará con <strong style='color:#0F172A'>cero downtime operacional</strong>.</p>");
        }
        else
        {
            sb.AppendLine("<h4>How we close these vectors without taking you offline</h4>");
            sb.AppendLine("<p>TeamLogic IT deploys a <strong>passive telemetry engine</strong> across your network for a full <strong>90-day observation window</strong>. The engine silently captures every legacy protocol call &mdash; which hosts make them, which accounts initiate them, which applications depend on them and at what cadence. Zero installation footprint on the endpoints that matter, zero reboots, zero user-facing disruption.</p>");
            sb.AppendLine("<ul>");
            sb.AppendLine("<li><strong>Day 1-30:</strong> baseline capture of all NTLM / SMBv1 / RC4 / WDigest traffic on the wire and at the DC</li>");
            sb.AppendLine("<li><strong>Day 31-60:</strong> attribution &mdash; every legacy call tied to a specific application, user or service</li>");
            sb.AppendLine("<li><strong>Day 61-90:</strong> deprecation blueprint &mdash; the exact order, per-dependency, in which each legacy protocol can be turned off without breaking anything</li>");
            sb.AppendLine("</ul>");
            sb.AppendLine("<p style='margin-top:10px'>At the end of the 90 days you receive a <strong>per-system deprecation plan</strong> with <strong style='color:#0F172A'>zero ambiguity</strong> and the explicit guarantee that each protocol kill will land with <strong style='color:#0F172A'>zero operational downtime</strong>.</p>");
        }
        sb.AppendLine("</div>");

        sb.AppendLine("<div style='display:flex;gap:12px;margin-top:20px'>");
        AppendPhaseCard(sb, es ? "Descubrimiento" : "Discovery", "1–30", "#C0392B", es ? "Captura baseline de protocolos legacy y mapeo inicial de activos en la red." : "Baseline capture of legacy protocols and initial asset mapping on the network.", es);
        sb.AppendLine("<div style='align-self:center;font-size:18px;color:#94a3b8'>&rarr;</div>");
        AppendPhaseCard(sb, es ? "Telemetría" : "Telemetry", "31–60", "#D97706", es ? "Atribución: cada llamada legacy vinculada a aplicación, usuario o servicio específico." : "Attribution: every legacy call tied to a specific application, user or service.", es);
        sb.AppendLine("<div style='align-self:center;font-size:18px;color:#94a3b8'>&rarr;</div>");
        AppendPhaseCard(sb, es ? "Aplicación" : "Enforcement", "61–90", "#006536", es ? "Blueprint de depreciación: orden exacto para apagar cada protocolo sin downtime." : "Deprecation blueprint: exact order to turn off each protocol with zero downtime.", es);
        sb.AppendLine("</div>");

        if (_audience == AudiencePerspective.Audit)
            RenderAuditSection(sb, data, es);

        sb.AppendLine("<div class='cta-box' style='margin-top:20px'>");
        if (es)
            sb.AppendLine($"<p style='font-size:13px;margin:0'><strong>Próximo paso:</strong> un kickoff de 45 minutos con {ReportHelpers.HtmlEncode(brand.CompanyName)} para desplegar el motor de telemetría pasiva e iniciar tu mapa de dependencias de 90 días. El reloj sobre la exposición actual corre ya sea que el engagement comience esta semana o el próximo trimestre.</p>");
        else
            sb.AppendLine($"<p style='font-size:13px;margin:0'><strong>Next step:</strong> a 45-minute kickoff with {ReportHelpers.HtmlEncode(brand.CompanyName)} to deploy the passive telemetry engine and start your 90-day dependency map. The clock on the current exposure runs whether the engagement starts this week or next quarter.</p>");
        sb.AppendLine("</div>");

        return sb.ToString();
    }

    public string Render(ReportData data, ReportOptions options)
    {
        var es = options.IsSpanish;
        var sb = new StringBuilder();
        sb.AppendLine("<div class='page pres-light'>");
        ReportHelpers.AppendPageHeader(sb,
            SectionTitle(options)!,
            data.Branding,
            es ? "EL ENFOQUE TEAMLOGIC" : "THE TEAMLOGIC APPROACH");
        sb.AppendLine("<div class='pb'>");
        sb.Append(RenderContent(data, options));
        sb.AppendLine("</div></div>");
        return sb.ToString();
    }

    private static void RenderAuditSection(StringBuilder sb, ReportData data, bool es)
    {
        sb.AppendLine("<div style='margin-top:20px;padding:14px;background:#f8fafc;border:1px solid #e2e8f0;border-radius:6px'>");
        sb.AppendLine($"<h4 style='font-size:10pt;color:#1e293b;margin:0 0 8px'>{(es ? "Datos de Auditoría" : "Audit Data")}</h4>");

        sb.AppendLine("<table class='data-table' style='font-size:9pt'><tbody>");
        sb.AppendLine($"<tr><td style='font-weight:600;width:35%'>{(es ? "Fuentes de datos" : "Data Sources")}</td><td>Kryoss Agent v1.6+, Microsoft Graph API, Azure Resource Manager</td></tr>");
        sb.AppendLine($"<tr><td style='font-weight:600'>{(es ? "Controles evaluados" : "Controls Evaluated")}</td><td>{data.ControlResults.Count}</td></tr>");
        sb.AppendLine($"<tr><td style='font-weight:600'>{(es ? "Dispositivos escaneados" : "Devices Scanned")}</td><td>{data.TotalMachines}</td></tr>");
        sb.AppendLine($"<tr><td style='font-weight:600'>{(es ? "Fecha de escaneo" : "Scan Date")}</td><td>{data.ScanDate:yyyy-MM-dd HH:mm UTC}</td></tr>");

        var frameworks = data.FrameworkScores?.Select(f => f.Code).ToList() ?? [];
        if (frameworks.Count > 0)
            sb.AppendLine($"<tr><td style='font-weight:600'>{(es ? "Marcos evaluados" : "Frameworks Evaluated")}</td><td>{string.Join(", ", frameworks)}</td></tr>");

        sb.AppendLine("</tbody></table>");

        sb.AppendLine($"<div style='margin-top:10px;font-size:8pt;color:#64748b;line-height:1.5'>");
        sb.AppendLine($"<strong>{(es ? "Limitaciones:" : "Limitations:")}</strong> ");
        sb.Append(es
            ? "Este informe evalúa controles técnicos automatizados. No cubre controles administrativos, físicos ni de personal. Los resultados reflejan el estado al momento del escaneo."
            : "This report evaluates automated technical controls. It does not cover administrative, physical, or personnel controls. Results reflect state at scan time.");
        sb.AppendLine("</div>");
        sb.AppendLine("</div>");
    }

    private static void AppendPhaseCard(StringBuilder sb, string phase, string days, string color, string description, bool es = false)
    {
        sb.AppendLine($"<div style='flex:1;padding:16px;background:#f8fafc;border:1px solid #e2e8f0;border-top:4px solid {color};border-radius:6px'>");
        sb.AppendLine($"<div style='font-size:10px;font-weight:700;letter-spacing:0.1em;text-transform:uppercase;color:{color};margin-bottom:4px'>{(es ? "Día" : "Day")} {days}</div>");
        sb.AppendLine($"<div style='font-size:14px;font-weight:700;color:#0F172A;margin-bottom:8px'>{ReportHelpers.HtmlEncode(phase)}</div>");
        sb.AppendLine($"<div style='font-size:11px;color:#475569;line-height:1.6'>{ReportHelpers.HtmlEncode(description)}</div>");
        sb.AppendLine("</div>");
    }
}

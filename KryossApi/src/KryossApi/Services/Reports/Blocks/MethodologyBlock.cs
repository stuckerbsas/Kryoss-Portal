using System.Text;

namespace KryossApi.Services.Reports.Blocks;

/// <summary>
/// Renders the 90-Day Safe Deprecation methodology page used in both
/// Preventa Opener and Preventa Detailed reports. Light-mode page class.
/// Extracted from BuildOrgPresalesReport (page 5) and the CTA block in
/// BuildOrgPresalesOpenerReport.
/// </summary>
public class MethodologyBlock : IReportBlock
{
    public string Render(ReportData data, ReportOptions options)
    {
        var sb = new StringBuilder();
        var es = options.IsSpanish;
        var brand = data.Branding;

        sb.AppendLine("<div class='page pres-light'>");
        ReportHelpers.AppendPageHeader(sb,
            es ? "Metodolog\u00eda: Depreciaci\u00f3n Segura de 90 D\u00edas" : "Methodology: 90-Day Safe Deprecation",
            brand,
            es ? "EL ENFOQUE TEAMLOGIC" : "THE TEAMLOGIC APPROACH");
        sb.AppendLine("<div class='pb'>");

        // Warning box
        sb.AppendLine("<div class='methodology-warning'>");
        if (es)
            sb.AppendLine("<strong>&#9888; El error que vemos cometer a los clientes:</strong> Desactivar abruptamente protocolos legacy (NTLM, SMBv1, WDigest, Kerberos RC4) sin mapear primero las dependencias causa interrupciones catastr\u00f3ficas e inmediatas del negocio. Aplicaciones de l\u00ednea de negocio, file shares legacy, esc\u00e1neres, sistemas telef\u00f3nicos e integraciones dejan de funcionar en el momento en que el protocolo muere &mdash; y la interrupci\u00f3n recae directamente sobre el equipo de TI que apag\u00f3 el switch.");
        else
            sb.AppendLine("<strong>&#9888; The mistake we watch clients make:</strong> Abruptly turning off legacy protocols (NTLM, SMBv1, WDigest, Kerberos RC4) without first mapping dependencies causes catastrophic, immediate business outages. Line-of-business applications, legacy file shares, scanners, phone systems and integrations break the moment the protocol dies &mdash; and the outage lands squarely on the IT team that flipped the switch.");
        sb.AppendLine("</div>");

        // 90-day phased approach card
        sb.AppendLine("<div class='methodology-card'>");
        if (es)
        {
            sb.AppendLine("<h4>C\u00f3mo cerramos estos vectores sin dejarte offline</h4>");
            sb.AppendLine("<p>TeamLogic IT despliega un <strong>motor de telemetr\u00eda pasiva</strong> a trav\u00e9s de tu red durante una <strong>ventana de observaci\u00f3n de 90 d\u00edas</strong>. El motor captura silenciosamente cada llamada de protocolo legacy &mdash; qu\u00e9 hosts las hacen, qu\u00e9 cuentas las inician, qu\u00e9 aplicaciones dependen de ellas y con qu\u00e9 cadencia. Cero huella de instalaci\u00f3n en los endpoints cr\u00edticos, cero reinicios, cero interrupciones para el usuario.</p>");
            sb.AppendLine("<ul>");
            sb.AppendLine("<li><strong>D\u00eda 1-30:</strong> captura baseline de todo el tr\u00e1fico NTLM / SMBv1 / RC4 / WDigest en la red y en el DC</li>");
            sb.AppendLine("<li><strong>D\u00eda 31-60:</strong> atribuci\u00f3n &mdash; cada llamada legacy vinculada a una aplicaci\u00f3n, usuario o servicio espec\u00edfico</li>");
            sb.AppendLine("<li><strong>D\u00eda 61-90:</strong> blueprint de depreciaci\u00f3n &mdash; el orden exacto, por dependencia, en que cada protocolo legacy puede apagarse sin romper nada</li>");
            sb.AppendLine("</ul>");
            sb.AppendLine("<p style='margin-top:10px'>Al final de los 90 d\u00edas recibes un <strong>plan de depreciaci\u00f3n por sistema</strong> con <strong style='color:#0F172A'>cero ambig\u00fcedad</strong> y la garant\u00eda expl\u00edcita de que cada eliminaci\u00f3n de protocolo se ejecutar\u00e1 con <strong style='color:#0F172A'>cero downtime operacional</strong>.</p>");
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

        // Timeline visual: Discovery → Telemetry → Enforcement
        sb.AppendLine("<div style='display:flex;gap:12px;margin-top:20px'>");
        AppendPhaseCard(sb, es ? "Descubrimiento" : "Discovery",    "1–30",  "#C0392B", es ? "Captura baseline de protocolos legacy y mapeo inicial de activos en la red." : "Baseline capture of legacy protocols and initial asset mapping on the network.");
        sb.AppendLine("<div style='align-self:center;font-size:18px;color:#94a3b8'>&rarr;</div>");
        AppendPhaseCard(sb, es ? "Telemetr\u00eda" : "Telemetry",  "31–60", "#D97706", es ? "Atribuci\u00f3n: cada llamada legacy vinculada a aplicaci\u00f3n, usuario o servicio espec\u00edfico." : "Attribution: every legacy call tied to a specific application, user or service.");
        sb.AppendLine("<div style='align-self:center;font-size:18px;color:#94a3b8'>&rarr;</div>");
        AppendPhaseCard(sb, es ? "Aplicaci\u00f3n"  : "Enforcement", "61–90", "#006536", es ? "Blueprint de depreciaci\u00f3n: orden exacto para apagar cada protocolo sin downtime." : "Deprecation blueprint: exact order to turn off each protocol with zero downtime.");
        sb.AppendLine("</div>");

        // CTA
        sb.AppendLine("<div class='cta-box' style='margin-top:20px'>");
        if (es)
            sb.AppendLine($"<p style='font-size:13px;margin:0'><strong>Pr\u00f3ximo paso:</strong> un kickoff de 45 minutos con {ReportHelpers.HtmlEncode(brand.CompanyName)} para desplegar el motor de telemetr\u00eda pasiva e iniciar tu mapa de dependencias de 90 d\u00edas. El reloj sobre la exposici\u00f3n actual corre ya sea que el engagement comience esta semana o el pr\u00f3ximo trimestre.</p>");
        else
            sb.AppendLine($"<p style='font-size:13px;margin:0'><strong>Next step:</strong> a 45-minute kickoff with {ReportHelpers.HtmlEncode(brand.CompanyName)} to deploy the passive telemetry engine and start your 90-day dependency map. The clock on the current exposure runs whether the engagement starts this week or next quarter.</p>");
        sb.AppendLine("</div>");

        sb.AppendLine("</div></div>");
        return sb.ToString();
    }

    private static void AppendPhaseCard(StringBuilder sb, string phase, string days, string color, string description)
    {
        sb.AppendLine($"<div style='flex:1;padding:16px;background:#f8fafc;border:1px solid #e2e8f0;border-top:4px solid {color};border-radius:6px'>");
        sb.AppendLine($"<div style='font-size:10px;font-weight:700;letter-spacing:0.1em;text-transform:uppercase;color:{color};margin-bottom:4px'>Day {days}</div>");
        sb.AppendLine($"<div style='font-size:14px;font-weight:700;color:#0F172A;margin-bottom:8px'>{ReportHelpers.HtmlEncode(phase)}</div>");
        sb.AppendLine($"<div style='font-size:11px;color:#475569;line-height:1.6'>{ReportHelpers.HtmlEncode(description)}</div>");
        sb.AppendLine("</div>");
    }
}

using System.Globalization;

namespace KryossApi.Services.Reports;

public static class ReportTranslations
{
    private static readonly CultureInfo EsCulture = new("es-ES");
    private static readonly CultureInfo EnCulture = new("en-US");

    public static string FormatDate(DateTime date, string lang) =>
        lang == "es"
            ? date.ToString("dd 'de' MMMM 'de' yyyy", EsCulture)
            : date.ToString("MMMM dd, yyyy", EnCulture);

    private static readonly Dictionary<string, string> CategoryEs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Account And Access Controls"] = "Control de Cuentas y Acceso",
        ["Application Control"] = "Control de Aplicaciones",
        ["Audit And Logging"] = "Auditoría y Registro",
        ["Audit, Logging And Monitoring"] = "Auditoría, Registro y Monitoreo",
        ["Authentication"] = "Autenticación",
        ["Backup And Recovery"] = "Respaldo y Recuperación",
        ["Browser And Application Policies"] = "Políticas de Navegador y Aplicaciones",
        ["Browser Hardening"] = "Endurecimiento de Navegador",
        ["Certificates And Cryptography"] = "Certificados y Criptografía",
        ["Credential Protection"] = "Protección de Credenciales",
        ["Cryptography"] = "Criptografía",
        ["Encryption"] = "Cifrado",
        ["Endpoint Protection And Patching"] = "Protección de Endpoints y Parches",
        ["Exploit And Memory Protection"] = "Protección contra Exploits y Memoria",
        ["File System And Shared Resources"] = "Sistema de Archivos y Recursos Compartidos",
        ["Firewall"] = "Firewall",
        ["Hardening"] = "Endurecimiento",
        ["Local Users And Account Management"] = "Usuarios Locales y Gestión de Cuentas",
        ["Multi-Framework Coverage"] = "Cobertura Multi-Framework",
        ["Network And Protocol Security"] = "Seguridad de Red y Protocolos",
        ["Network Security"] = "Seguridad de Red",
        ["Network Performance"] = "Rendimiento de Red",
        ["Office Hardening"] = "Endurecimiento de Office",
        ["Patch Management"] = "Gestión de Parches",
        ["Persistence Detection And Integrity"] = "Detección de Persistencia e Integridad",
        ["Privacy And Telemetry"] = "Privacidad y Telemetría",
        ["Remote Access"] = "Acceso Remoto",
        ["Security Options And Local Policy"] = "Opciones de Seguridad y Políticas Locales",
        ["Services Hardening"] = "Endurecimiento de Servicios",
        ["Software And Application Security"] = "Seguridad de Software y Aplicaciones",
        ["Time Synchronization"] = "Sincronización Horaria",
        ["Windows Security Baseline"] = "Línea Base de Seguridad Windows",
        ["IIS Hardening"] = "Endurecimiento de IIS",
        ["DNS Server"] = "Servidor DNS",
        ["DHCP Server"] = "Servidor DHCP",
        ["Hyper-V Security"] = "Seguridad Hyper-V",
        ["Print Server"] = "Servidor de Impresión",
        ["Server Core And General Hardening"] = "Endurecimiento General de Servidor",
        ["Virtualization Based Security"] = "Seguridad Basada en Virtualización",
        ["Windows Defender Advanced"] = "Windows Defender Avanzado",
        ["User Account Control"] = "Control de Cuentas de Usuario",
        ["AutoPlay And Media"] = "AutoPlay y Medios",
        ["Active Directory"] = "Active Directory",
        ["Kerberos Security"] = "Seguridad Kerberos",
        ["Domain Controller Hardening"] = "Endurecimiento de Controlador de Dominio",
        ["DNS Security"] = "Seguridad DNS",
        ["Edge Browser Security"] = "Seguridad de Navegador Edge",
        ["Attack Surface Reduction"] = "Reducción de Superficie de Ataque",
        ["Exploit Protection"] = "Protección contra Exploits",
        ["Protocol Usage Audit"] = "Auditoría de Uso de Protocolos",
        ["User Settings"] = "Configuración de Usuario",
    };

    public static string TranslateCategory(string name, string lang)
    {
        if (lang != "es") return name;
        return CategoryEs.TryGetValue(name, out var es) ? es : name;
    }

    private static readonly Dictionary<string, string> TermsEs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PASS"] = "APROBADO",
        ["FAIL"] = "FALLIDO",
        ["WARNING"] = "ADVERTENCIA",
        ["Passing"] = "Aprobados",
        ["Failing"] = "Fallidos",
        ["Coverage"] = "Cobertura",
        ["Category"] = "Categoría",
        ["Finding"] = "Hallazgo",
        ["Severity"] = "Severidad",
        ["Hosts"] = "Equipos",
        ["Fix"] = "Remediación",
        ["Remediation"] = "Remediación",
        ["Status"] = "Estado",
        ["Machine"] = "Equipo",
        ["Machines"] = "Equipos",
        ["Score"] = "Puntuación",
        ["Grade"] = "Calificación",
        ["Risk"] = "Riesgo",
        ["Low"] = "Bajo",
        ["Medium"] = "Medio",
        ["High"] = "Alto",
        ["Critical"] = "Crítico",
        ["devices assessed"] = "dispositivos evaluados",
        ["Controls"] = "Controles",
        ["Total"] = "Total",
        ["Subtotal"] = "Subtotal",
        ["Compliance"] = "Cumplimiento",
        ["Recommendation"] = "Recomendación",
        ["Impact"] = "Impacto",
        ["Priority"] = "Prioridad",
        ["Action Required"] = "Acción Requerida",
        ["No issues found"] = "Sin hallazgos",
        ["OK"] = "Correcto",
    };

    public static string T(string term, string lang)
    {
        if (lang != "es") return term;
        return TermsEs.TryGetValue(term, out var es) ? es : term;
    }
}
